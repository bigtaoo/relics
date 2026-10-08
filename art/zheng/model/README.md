# 狰 · 图生 3D + 自动绑定（08 §1，Tripo API 试验）

脚本：`tools/art/tripo.sh`（Tripo v3 API，key 在 `~/.vibe/tripo_curl_key.conf`）。
每个任务保存 `<name>.req.json`（请求）和 `<name>.task.json`（完整结果，含任务 ID 和积分）。
查看器：`python -m http.server 8010 --bind 127.0.0.1`（仓库根目录），打开
`http://127.0.0.1:8010/tools/art/glb_viewer.html?f=/art/zheng/model/walk_v2.glb`，可看骨骼、动画、单根骨骼的权重。

## 2026-10-06 第 1 轮

| 步骤 | 输入 | 结果 | 积分 |
|---|---|---|---|
| `gen_v1` 图生 3D | `concept/artifact_v5.png`（带底座），v3.1，PBR，face_limit 20000 | 模型很还原：五尾、青铜 + 铜绿贴图。19k 三角面 | 30 |
| `rigcheck_v1` | gen_v1 | **riggable: false**（底座和身体连成一体） | 0 |
| `gen_v2` 图生 3D | `input_nobase.png`（概念图编辑去掉底座，影子手动擦掉） | 同样质量，19386 三角面 / 11718 顶点，1 个材质 3 张贴图 | 30 |
| `rigcheck_v2` | gen_v2 | riggable: true，推荐类型 `others`（不在文档列出的 7 类里） | 0 |
| `rig_v2` 自动绑定 | gen_v2，rig v2.5，强制 `quadruped` | 40 根骨骼 | 25 |
| `walk_v2` 动作重定向 | rig_v2，`preset:quadruped:walk` | 2.6 秒走路循环，驱动 13 根骨骼 | 10 |

合计 95 积分（约 $0.95），API 余额 600 → 505。每个任务 10 秒到 3 分钟。

## 绑定结果分析（按每根骨骼主导的顶点位置统计）

- **五条尾巴都拿到了骨骼链**（好消息）：`bone_1–5`、`tripo::Head_0–3`、`tripo::1_Left_Limb_0–4`、`bone_13–15`、`bone_38–39`。
- **但部位识别错了**：
  - 一条尾巴被标成了 `Head`，另一条被标成了「左后腿」`1_Left_Limb`。
  - 真正的头和前爪全挂在 `Spine_1` 和一串零碎的 `bone_18–27` 上。
  - 四条腿里只有两条被认成腿（`0_Right_Limb`、`1_Right_Limb`）。
  - `Root` 独占 29% 的顶点，身体基本没有脊柱权重。
- 所以 **walk 预设是错的**：它按「头」「左后腿」去摆的其实是两条尾巴，另有一条真腿完全不动。
- Tripo 的非人形预设动作**只有 `walk` 一个**（四足），待机 / 攻击 / 施法 / 受击 / 死亡 / 觉醒都没有。

## 结论

- **Tripo 图生 3D：可用**。从定稿概念图到带 PBR 贴图的 2 万面模型，一次成功，$0.30。前提是概念图里不能有底座（底座在 Unity 里单独做静态模型）。
- **Tripo 自动绑定：不能直接用**。尾巴链可以当起点，但骨骼语义要在 DCC 里重命名、修正，权重要重刷。
- **动作：Tripo 给不了**。6 个动作都要自己做 → 需要 Blender（或外包）。

## 文件

- `gen_v1.*`：带底座的版本（不能绑定，仅作对比）。
- `gen_v2.glb`：无底座的模型，**后续以它为准**。
- `rig_v2.glb`、`walk_v2.glb`：Tripo 的绑定和 walk 结果，用来对比。
- `*.rendered_image.webp` / `*.preview.png`：Tripo 的渲染预览。

## 2026-10-06 第 2 轮：卡通版，Blender 绑定 + 动作（流程跑通到 FBX）

| 步骤 | 工具 / 输入 | 结果 | 成本 |
|---|---|---|---|
| `toon_v1` 图生 3D | Tripo，`concept/r2/artifact_v1.png`（本身无底座），参数同 gen_v2 | 一角、五尾、四腿都对，19539 三角面，1 个材质。预览 `toon_v1.preview.png` | 30 积分（余 475） |
| 绑定 `zheng_rig.blend` | `tools/art/blender/rig_zheng.py`（2026-10-08 起是 `quadruped_rig.py` + `creatures/zheng.py`，见文末） | 39 根骨骼：root + 脊柱 5 + 角 + 四腿各 3 + 五尾各 4。自动权重，38 个变形骨骼全部有权重。检查图 `zheng_rig.posetest.png`：单独弯每条尾巴只动那一条 | 0 |
| 动作 `zheng_anim.blend` | `tools/art/blender/anim_zheng.py`（现为 `quadruped_anim.py`），程序化关键帧 | 待机（4 秒循环，2026-10-06 加大幅度，见 04 §6）、攻击、施法、受击、死亡、觉醒，共 6 个。检查图 `zheng_anim.sheet.png` | 0 |
| 导出 | `tools/art/blender/export_fbx.py` | `client/Assets/HotRes/Art/Zheng/zheng.fbx`（6 个 take）+ `zheng_basecolor.png`。回导 Blender 验证：骨骼、动作、蒙皮都在 | 0 |

命令（仓库根目录，`B` 是 Blender 路径）：

```bash
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"; M=D:/automatic/art/zheng/model
"$B" -b --python tools/art/blender/quadruped_rig.py -- zheng $M/toon_v1.glb $M/zheng_rig.blend
"$B" -b --python tools/art/blender/quadruped_anim.py -- zheng $M/zheng_rig.blend $M/zheng_anim.blend
"$B" -b --python tools/art/blender/export_fbx.py -- $M/zheng_anim.blend D:/automatic/client/Assets/HotRes/Art/Zheng/zheng.fbx
```

绑定的坑（都已写进脚本）：
- Tripo 网格直接用骨骼热度（bone heat）权重会**全部失败**。做法：复制一份做体素重构（watertight 代理），删掉除最大块以外的碎片（胡须会变成游离小块，一块就能让整个求解失败），在代理上算权重，再按最近面插值传回原网格。
- 骨骼热度在 1 个单位大小的模型上也不稳，绑定时放大 10 倍，绑完缩回。
- 关节位置来自正交三视图 + 按高度切片的顶点聚类中心，换角色要重新量（约 20 分钟）。

动作的做法：每个动作是一个 `t → 姿势` 的函数，旋转按「生物空间」的轴写（头朝 +X），自动换算到每根骨骼的局部坐标，所以不用管骨骼 roll。五尾的次级动画是相位错开的正弦波（每节滞后、每条尾巴错相），所有动作共用。

质量判断：动作是「能用的占位」级别，节奏和姿态读得懂，但没有手 K 的弹性和夸张。正式版要么美术在这 6 个 blend 动作上精修，要么外包；骨骼和流程不用变。

Unity：`unity run client -- -executeMethod Automatic.Editor.Batch.ArtPreviewZheng`，导入规则在 `client/Assets/Editor/ArtPostprocessor.cs`，着色器 `client/Assets/HotRes/Shaders/Toon.shader`，每个动作 5 帧截图输出到 `artifacts/art_preview/zheng/`，汇总图 `zheng_unity.sheet.png`。

## 2026-10-06 删胡须 + 材质变体

**胡须**：Tripo 把胡须做成头发丝粗细的管子，挂在脸上（焊接 UV 接缝后和身体是同一块网格，按碎块删不掉），在 Unity 描边下变成黑色细棍。`quadruped_rig.py` 的 `remove_whiskers`（参数在 `creatures/zheng.py` 的 `WHISKERS`）：
- 用 0.01 体素重构一份粗外壳，胡须太细，不会进入外壳。
- 离外壳 > 0.02 的顶点当种子（胡须尖），沿网格往回扩，只要还在外壳外 0.004 以上就算胡须。
- z > 0.2 的不算（角尖也细，要保留）。结果删掉 238 个顶点，阈值在 0.002–0.008 间结果稳定，不会扩进脸里。

**材质**：一张贴图出 5 套（`client/Assets/Editor/ToonVariants.cs` 是参数表，生成 `zheng_<名>.mat`）：

| 名称 | 用途 | 做法 |
|---|---|---|
| pottery | 1 档：彩陶 | 赤陶底 + 深褐彩绘斑，哑光，弱边缘光 |
| bronze | 2 档：青铜 | 原贴图不重着色，加小高光 |
| jade | 3 档：玉 | 浅玉绿 + 深绿斑，亮阴影 + 强边缘光，显得通透 |
| gold | 4 档：鎏金 | 亮黄金 + 深金斑，硬高光 |
| living | 活体态 | 赤橙豹身 + 黑斑，金角，尾巴从青绿到橙黄渐变并发光 |

- `Relics/Toon` 的重着色：铜绿斑点按「绿 > 红」识别成遮罩，亮度除以身体 / 斑点的中位亮度作为明暗细节，颜色全部来自材质参数。
- 尾巴和角的遮罩来自绑定权重，烘进顶点色（`paint_parts`：R = 沿尾巴的位置，G = 尾巴，B = 角），FBX 按原始字节导出。
- 坑：材质颜色要按青铜贴图的亮度来定（身体线性亮度约 0.19），第一版颜色直接按概念图取，全部过曝发白。

效果：`zheng_variants.png`（左到右：陶、青铜、玉、金、活体），活体态 6 个动作 `zheng_living.sheet.png`，青铜态 `zheng_unity.sheet.png`。战斗镜头下 5 套一眼能分开；活体态的火焰尾巴只是颜色渐变 + 自发光，没有动态火焰，正式版要靠特效补。

## 2026-10-06 减面

`quadruped_rig.py` 的 `decimate`：删胡须之后、绑定之前执行。先焊接 glb 在 UV 接缝处拆开的顶点（UV 按角存，接缝不受影响），再用 Blender 的 collapse 减面到目标面数（默认 3000，第 3 个参数可改）。权重在减面后的网格上计算，所以绑定流程不用改。

| 面数 | 近景（`zheng_variants.png` 的镜头） | 棋盘战斗镜头 | 满场 46 只 |
|---|---|---|---|
| 19168（Tripo 原样） | 基准 | 基准 | 88.3 万面 |
| 5000 | 和原版几乎一样，角尖稍钝 | 看不出区别 | 23 万面 |
| **3000（默认）** | 尾巴有棱角，描边略锯齿，斑点稍糊 | 看不出区别 | **13.8 万面** |

6 个动作减面后蒙皮正常（`zheng_living.sheet.png`）。以后做特写（技能镜头、商店展示）需要更细的版本时，用参数 5000 再出一份。

## 第 2 轮结论

- **从概念图到 Unity 里卡通着色、带 6 个动作的角色，全程脚本化，可重跑**：图生 3D 约 3 分钟，绑定 + 动作 + 导出约 1 分钟，花费 30 积分（$0.30）。
- 人工部分：关节位置测量、动作函数编写。下一个四足角色可以复用骨骼结构和动作函数，只改关节表。

## 2026-10-07 战斗用动作

- 为战斗演出切片（`art/fx/README.md` §4）新增了 `leap` 扑跃动作。攻击、施法、受击、死亡加大了幅度，加上预备动作和过冲。现在共 7 个动作。
- 修正了一个旧错误：`hips` 绕 +Y 转是整个身体低头。原来施法时写的「后坐」，实际效果是低头。
- 侧视检查图：`tools/art/blender/anim_sheet.py`，每个动作截 8 帧，直接从 .blend 渲染，不经过 Unity。

```bash
"$B" -b --python tools/art/blender/anim_sheet.py -- $M/zheng_anim.blend <out_dir> [clip ...]
```

- Unity 里的 Animator Controller 由 `BoardSlice.Controller` 自动补齐新动作的状态，在 `Batch.FxSlice` 里执行，不用重建棋盘场景。

## 2026-10-07 召唤物低模

同屏 300 单位测试用（`art/board/README.md`「同屏 300 单位」）。`tools/art/blender/lowpoly.py` 读入带动作的 `zheng_anim.blend`，把蒙皮网格减到约 400 面，骨骼、权重、顶点色和全部动作保留，再用 `export_fbx.py` 导出 `zheng_lo.fbx`（导出时顺带写出的 `zheng_lo_basecolor.png` 删掉，材质继续共用 `zheng_basecolor.png`）。

```
blender -b --python tools/art/blender/lowpoly.py -- art/zheng/model/zheng_anim.blend artifacts/crowd/zheng_lo.blend 400
blender -b --python tools/art/blender/export_fbx.py -- artifacts/crowd/zheng_lo.blend client/Assets/HotRes/Art/Zheng/zheng_lo.fbx
```

- 结果：3000 面减到 400 面，在 Unity 里是 605 个顶点（UV 接缝处顶点被拆开）。骨骼还是 39 根。
- 减面修改器要先挪到修改器栈的第一位再应用。否则应用时会连同骨骼的当前姿势一起烘进网格。
- 正式的召唤物会是另外的模型，骨骼应该更少。这里只是拿来测性能。

## 2026-10-08 拆成四足家族脚本

做第二只四足当康时，`rig_zheng.py` / `anim_zheng.py` 拆成了共用部分 `quadruped_rig.py` / `quadruped_anim.py` 和关节表 `creatures/zheng.py`（骨骼、五尾链、胡须参数、五尾次级动画）。拆完重跑，网格、权重、遮罩和全部关键帧的哈希都和拆之前一致，所以 `zheng.fbx` 没有重导。命令见上面「第 2 轮」。拆分方式和当康的结果见 `art/dangkang/README.md`。

同一天修了 `ArtPreview` 的 5 套材质合影 `zheng_variants.png`。这张图最近一直是错的（10-07 中午那次还正常）：5 只全是同一个材质，或者全黑，单只的截图正常。原因是刚生成的单位第一次渲染时，蒙皮顶点还没算好。现在每次截图前先空渲一次。`zheng_variants.png` 已经重新生成，陶是灰色的。
