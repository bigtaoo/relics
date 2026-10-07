"""Hot update failure scenarios (design/07 §3): runs faulty_cdn.py and Relics.exe on a timeline,
then prints the player's [Boot] lines and the CDN's request log. Logs go to artifacts/hotupdate-tests/.

python failure_scenario.py NAME PLAYER_SECONDS [--fresh] [--cdn "ARGS"] [--at T ACTION ...] [--player-args "..."]
  --fresh   clear the player's resource cache first
  --cdn     start faulty_cdn.py at t=0 with these arguments ("" = no faults)
  actions:  kill (stop the CDN), start (start it again with --cdn args), start:ARGS (with other args),
            start-plain (python -m http.server, no Range support), corrupt:N (overwrite 1 KB in the middle
            of the N-th largest cached bundle)
Example, CDN down for 40 s in the middle of a download at 100 KB/s:
  python failure_scenario.py outage 90 --fresh --cdn "--rate 100" --at 5 kill --at 45 start
The CDN must have a version newer than the one built into the player, or there is nothing to download.
"""
import argparse, glob, os, re, shlex, shutil, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
P = os.path.join(REPO, "artifacts", "player", "PC")
CDN_ROOT = os.path.join(REPO, "artifacts", "cdn")
LOGS = os.path.join(REPO, "artifacts", "hotupdate-tests")
# Player resource cache (Application.persistentDataPath/yoo, see ResourceUpdater).
YOO =os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow", "bigtaoo", "Relics", "yoo")

ap = argparse.ArgumentParser()
ap.add_argument("name")
ap.add_argument("seconds", type=float)
ap.add_argument("--fresh", action="store_true")
ap.add_argument("--cdn", default=None, help="start the CDN at t=0 with these args ('' = no faults)")
ap.add_argument("--at", nargs=2, action="append", default=[], metavar=("T", "ACTION"))
ap.add_argument("--player-args", default="")
args = ap.parse_args()

os.makedirs(LOGS, exist_ok=True)
cdn_log = open(os.path.join(LOGS, args.name + ".cdn.log"), "w")
cdn = None
t0 = time.monotonic()


def stamp(msg):
    print(f"[{time.monotonic() - t0:5.1f}s] {msg}", flush=True)


def start_cdn(extra):
    global cdn
    cmd = [sys.executable, "-u", os.path.join(HERE, "faulty_cdn.py"), CDN_ROOT] + shlex.split(extra)
    cdn = subprocess.Popen(cmd, stdout=cdn_log, stderr=subprocess.STDOUT)
    stamp(f"cdn start {extra}")


def kill_cdn():
    global cdn
    if cdn:
        cdn.kill()
        cdn.wait()
        cdn = None
        stamp("cdn killed")


def corrupt(n):
    files = sorted(glob.glob(os.path.join(YOO, "DefaultPackage", "BundleFiles", "*", "*", "__data")),
                   key=os.path.getsize, reverse=True)
    f = files[n]
    with open(f, "r+b") as h:
        h.seek(os.path.getsize(f) // 2)
        h.write(b"\xde\xad\xbe\xef" * 256)
    stamp(f"corrupted {f} ({os.path.getsize(f)} bytes)")


if args.fresh and os.path.isdir(YOO):
    shutil.rmtree(YOO)
    stamp("cache cleared")
if args.cdn is not None:
    start_cdn(args.cdn)

log = os.path.join(LOGS, args.name + ".log")
size = [] if "-screen-width" in args.player_args else ["-screen-width", "800", "-screen-height", "450"]
player = subprocess.Popen([os.path.join(P, "Relics.exe"), "-screen-fullscreen", "0"] + size + ["-logFile", log]
                          + shlex.split(args.player_args))
stamp("player start")

pending = sorted(((float(t), a) for t, a in args.at), key=lambda x: x[0])
while time.monotonic() - t0 < args.seconds and player.poll() is None:
    now = time.monotonic() - t0
    while pending and pending[0][0] <= now:
        _, a = pending.pop(0)
        if a == "kill":
            kill_cdn()
        elif a == "start":
            start_cdn(args.cdn or "")
        elif a == "start-plain":
            cdn = subprocess.Popen([sys.executable, "-m", "http.server", "8000", "--bind", "127.0.0.1", "--directory", CDN_ROOT],
                                   stdout=cdn_log, stderr=subprocess.STDOUT)
            stamp("plain http.server start")
        elif a.startswith("start:"):
            start_cdn(a[6:])
        elif a.startswith("corrupt:"):
            corrupt(int(a[8:]))
    time.sleep(0.2)

if player.poll() is None:
    player.kill()
    stamp("player killed")
else:
    stamp(f"player exited {player.returncode}")
kill_cdn()
cdn_log.close()
time.sleep(0.5)
print("--- player")
skip = re.compile(r"^(YooAsset|UnityEngine|System|Automatic|HybridCLR|\(Filename|\s)|^$|memorysetup|UnityMemory"
                  r"|created file system|YooAssetSettings not found|Player connection|Physics::|D3D12|d3d12|Direct3D"
                  r"|<RI>|XInput|Subsystems|GfxDevice|Found 2 interfaces|Input System|UnloadTime|Initialize engine")
for line in open(log, encoding="utf-8", errors="replace"):
    if not skip.search(line):
        print(line.rstrip())
print("--- cdn")
print(open(os.path.join(LOGS, args.name + ".cdn.log")).read()[-4000:])
