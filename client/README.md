# client — Unity project

Unity 6.3 LTS (6000.3.x), URP, IL2CPP on all platforms. Hot update: HybridCLR (code) + YooAsset
(assets), see `design/07-hot-update.md`.

## Layout

| Path | Assembly | Hot update |
|---|---|---|
| `Assets/Boot/` | `Automatic.Boot` | No (AOT shell). Must never reference hot assemblies |
| `Packages/` → `src/Battle.Core` | `Automatic.Battle.Core` | Yes |
| `Assets/HotUpdate/Game/` | `Automatic.Game` | Yes. Entry: `Automatic.Game.GameEntry.Start()` |
| `Assets/HotRes/` | — | Yes. Everything here is collected into the YooAsset package `DefaultPackage`: one bundle per folder, with each folder's materials and textures in bundles of their own (`MaterialPackRule.cs`) |
| `Assets/Editor/` | `Automatic.Editor` | Editor only: setup and build menus |

## First-time setup

Prerequisites: Unity Hub, Unity 6000.3 LTS with *Windows Build Support (IL2CPP)* (plus Android / iOS
modules later), Visual Studio with the *Desktop development with C++* workload (IL2CPP needs MSVC), git.

1. Unity Hub → Add → `D:\automatic\client`, open with 6000.3.x.
2. `HybridCLR/Installer...` → Install (clones il2cpp_plus; needs git and network).
3. `Automatic/1. Setup Project` (idempotent): player settings, HybridCLR settings, URP, `HotCube`
   prefab and material, YooAsset collector, boot scene.

## Shop demo (design/08 §3)

`Relics.exe` (and Play on `Assets/Boot/Boot.unity` in the editor) starts the playable UI slice:
the board in the preparation phase with the shop. Click the glowing card to play the hu show, the
battle start and a scripted battle of about 16 s (`BattleScript.cs` / `BattleShow.cs`, art/fx/README.md
§4); click again or press R to start over, Esc quits. With the CDN running (below):

```bash
artifacts/player/PC/Relics.exe -screen-fullscreen 0 -screen-width 1600 -screen-height 900
```

`-autoplay <dir>` runs it by itself and saves screenshots along the timeline as raw RGB24 (the
player has no image encoder module), then quits. To check that something moves, diff two
frames of the same phase (e.g. `0.0` and `0.3`, both before the click lands). The battle shots
are taken at each act's blow, the hand going off, and the end.

Hot code may only call engine API the shell kept: `Assets/Boot/link.xml` preserves the engine
modules hot code is expected to use (design/07 §4). A `MissingMethodException` in the player log
means the API was stripped from the shell: add its module there and rebuild the player.

Phones get their own HUD scale and layout (`PhoneLayout.cs`, art/ui/README.md §5.1). To see it on
PC, open the window at the phone's aspect and name the phone: `-device iphone13|a16 -layout A|B|C`
(e.g. `-screen-width 1560 -screen-height 720`). With `-autoplay` the log has the on-screen size of
the board cells (`[Demo] ... front-row cell`).

Art in `Assets/HotRes/` (models, clips, prefabs) only needs `BuildHotUpdate`; the player picks up
the new version on its next start. Player settings stay as built: `runInBackground` is on, since
an unfocused player otherwise pauses at `[Boot] Initializing`.

## Hot update minimal validation (design/07 §7)

The check below now needs `-hotcheck` on the player command line (the default start is the demo).

1. Editor: open `Assets/Boot/Boot.unity`, Play. Expect a spinning bronze cube and all `PASS` lines
   (temporarily route `GameEntry.Start` to the check: the editor has no `-hotcheck` switch).
2. `Automatic/3. Build Player` (Windows). Output `artifacts/player/PC/Relics.exe`, resources
   published to `artifacts/cdn/PC/`.
3. Serve the CDN from the repo root:

   ```bash
   python -m http.server 8000 --directory artifacts/cdn
   ```

4. Run `Relics.exe -hotcheck`: same cube, all `PASS` (now inside the HybridCLR interpreter).
5. Change `GameEntry.BuildLabel` and the color of `Assets/HotRes/Bronze.mat`, then
   `Automatic/2. Build Hot Update`. Restart `Relics.exe -hotcheck` without rebuilding it: new label and color.

## Headless (Unity CLI)

Every menu step has a batch entry in `Assets/Editor/Batch.cs`, usable locally and in CI:

```bash
unity run client --no-tail -l setup.log -- -executeMethod Automatic.Editor.Batch.InstallHybridClr
unity run client --no-tail -l setup.log -- -executeMethod Automatic.Editor.Batch.Setup
unity run client --no-tail -l player.log -- -buildTarget Win64 -executeMethod Automatic.Editor.Batch.BuildPlayer
unity run client --no-tail -l hot.log -- -buildTarget Win64 -executeMethod Automatic.Editor.Batch.BuildHotUpdate
```

Board frame-rate test (design/08 §2): with the CDN running, `Relics.exe -bench -screen-width 1920
-screen-height 1080 -screen-fullscreen 0 -logFile bench.log` loads `board_west` from the resource
package, runs uncapped for 10 s and logs `[Bench]` lines (frame times, per-frame render counters as
medians, memory), then quits. `-nosrpbatch` turns the SRP Batcher off and `-nooutline` the toon
outline, to see what each costs.

The toon outline is drawn by the `Toon Outline` RenderObjects feature on `Assets/Settings/URP-Renderer.asset`
(LightMode `Outline`, after opaques; added by `Setup`), not in the forward draw, which kept the SRP
Batcher from batching. The renderer ships in the player: a shader change that relies on it needs a
player build, not only a hot update.

`unity run` adds `-batchmode -quit` itself; do not pass them. The player writes `[Boot]` and `[Hot]`
lines to its log (`Relics.exe -logFile run.log`), so a run can be checked without reading the screen.

Build products (`HybridCLRData/`, `Bundles/`, `Assets/HotRes/Dlls/`, `Assets/StreamingAssets/`,
`artifacts/`) are not committed.

## Resource cache and update failures

The player caches downloaded bundles in `Application.persistentDataPath/yoo`
(`%USERPROFILE%\AppData\LocalLow\bigtaoo\Relics\yoo` on Windows); delete it to start like a fresh
install. A fresh install downloads nothing until the CDN has a version newer than the one built into
the player. Failure handling (retries, download watchdog, resume, cache CRC check) is in
`ResourceUpdater` / `Boot`, see design/07 §3.

`tools/hotupdate/` reruns the failure tests:
- `faulty_cdn.py` serves `artifacts/cdn` with a bandwidth cap, stalls or dropped connections, and
  Range support. `python -m http.server` has no Range support, so resume cannot be tested on it.
- `failure_scenario.py` runs the CDN and the player on a timeline and prints the `[Boot]` lines.

Example, CDN down for 40 s in the middle of a download:

```bash
python tools/hotupdate/failure_scenario.py outage 90 --fresh --cdn "--rate 100" --at 5 kill --at 45 start
```
