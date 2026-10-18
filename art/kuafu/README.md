# 夸父 · 第一个人形（人形家族成本实测）

《山海经·海外北经》：「夸父与日逐走，入日；渴，欲得饮，饮于河、渭……」《大荒北经》：「珥两黄蛇，把两黄蛇。」卡通巨人：发髻、大胡子、胸口一个红太阳，右手拄一根短木杖，两耳各挂一条小黄蛇。

目的：量出一个人形棋子的成本，给 08 §5「异兽和人形的比例」（04 §6）补上人形这一边。之前只有四足（狰、当康）走过管线。2026-10-18。

工具和当康一样：`tools/art/tripo.sh`（出图、编辑、图生 3D、绑定、重定向都走它，每个任务存 `<name>.req.json` + `<name>.task.json`），提示词放在同名 `.txt` / `_fix.txt`。

## 概念图（`concept/`）

| 文件 | 来源 | 结果 | 积分 |
|---|---|---|---|
| `toon_a_seedream.png` | 文生图 seedream_v5 2K，`toon_a.txt` | **活体态，直接用**：A 字站姿，手和身体分开，木杖不着地，两条黄蛇都在。提示词特意要求 A 字站姿和四肢分开，方便自动绑定 | 5 |
| `artifact_v1.png` | 编辑 seedream 那张，chat_image_2.5_sunburst，`artifact_fix.txt` | **器物态**：青铜 + 铜绿、闭眼，姿势和部件都没变。用于图生 3D。排队约 4 分钟 | 10 |
| `transition_v1.png` | 编辑 seedream 那张，sunburst，`transition_fix.txt` | **过渡态**：腿、左臂和木杖还是青铜，开裂透金光；头、蛇、胸口已经活了 | 10 |

首张图就没有瑕疵，活体态不用再编辑一次（当康要去掉嘴里的稻穗）。

## 模型、绑定和预设动作（`model/`）

| 文件 | 来源 | 结果 | 积分 |
|---|---|---|---|
| `gen_v1.glb` | 图生 3D，`input` 填 artifact_v1 的任务 ID，参数同当康 | **选用**：木杖、耳蛇、发髻、念珠都在。约 2 分钟。转台 `gen_v1.turntable.png` | 30 |
| `rigcheck_v1.*` | 可绑性检查（任务类型 `animate_prerigcheck`） | `riggable: true, rig_type: biped`，1 秒 | 0 |
| `rig_v1.glb` | 自动绑定，`rig_type: biped`、`spec: tripo`、模型 v1.0 | **选用**：46 根骨骼，权重干净（手指、脚趾都有），手和杖、头和蛇的权重都在 R_Hand / Head 上。约 1 分钟 | 25 |
| `retarget_v1.glb` | 重定向 4 个人形预设：slash、cast_a_spell、hit_to_body_01、defeat_02 | **不用**，见下。检查图 `retarget_v1.sheet.png` | 40 |

**结论：骨骼和权重用 Tripo 的，动作自己写。** 狰的自动绑定把头、腿、尾认错了，人形骨骼 Tripo 认得很准，省掉了四足那套「量关节 + 体素代理算权重 + 限制小部件权重」。预设动作不能用：

- 时长 1.3–8.4 秒，游戏里要 0.4–2 秒，而且攻击接触帧要在 0.42 秒、施法释放在 0.5 秒（BattleShow）。
- 是写实比例的动捕。Q 版大头的头和肩一样宽，一弯腰整个人折过去，木杖穿进身体。
- 没有倒地死亡：defeat_02 是跪下又站起来。也没有扑跃和觉醒。

## 骨骼家族脚本

| 文件 | 内容 |
|---|---|
| `tools/art/blender/biped_rig.py` | 保留 Tripo 的骨骼和权重，减面到 3000（权重跟着塌缩走），给自动绑定不认识的部件加骨骼（木杖 1 根，两条蛇各 3 节），按距离把顶点交给新骨骼（握杖的拳头不动），刷部位遮罩 |
| `tools/art/blender/biped_anim.py` | 7 个动作，长度和约定与 `quadruped_anim.py` 一样，在角色空间（朝 +X、左 +Y）写旋转再换到骨骼自己的坐标系。武器方向由 `aim()` 对准 |
| `tools/art/blender/creatures/kuafu.py` | 只有部件骨骼、遮罩和次级动画（蛇摆），身体没有关节表 |

| 动作 | 内容 |
|---|---|
| idle（4 秒循环） | 呼吸、重心左右换、转头张望，木杖点地两次，蛇摆。整桌镜头下杖和头的动作看得出来 |
| attack（0.8 秒） | 双手举杖过头，0.42 秒砸下，前腿跨出 |
| cast（1.5 秒） | 举杖蓄力，0.5 秒前刺释放 |
| hit（0.5 秒） | 后仰，杖甩开 |
| leap（0.4 秒） | 下蹲、收腿、起跳 |
| death（1.5 秒） | 后仰倒地，杖脱手 |
| awaken（2 秒） | 器物醒来：低头蜷着，伸展，举杖 |

检查图：`model/kuafu_anim.sheet.png`（Blender，每个动作侧面和正面各 10 帧），`model/kuafu_unity.sheet.png`（Unity 活体态，每个动作 5 帧），`model/kuafu_variants.png`（5 套材质），`model/kuafu_masks.png`（遮罩：蓝 = 角遮罩，绿黄 = 尾巴遮罩）。

命令（仓库根目录）：

```bash
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"; M=art/kuafu/model
"$B" -b --python tools/art/blender/biped_rig.py -- kuafu $M/rig_v1.glb $M/kuafu_rig.blend
"$B" -b --python tools/art/blender/biped_anim.py -- kuafu $M/kuafu_rig.blend $M/kuafu_anim.blend
"$B" -b --python tools/art/blender/export_fbx.py -- $M/kuafu_anim.blend client/Assets/HotRes/Art/Kuafu/kuafu.fbx
```

Unity：`Batch.ArtPreviewKuafu`，输出到 `artifacts/art_preview/kuafu/`；材质 `ToonVariants.cs` 里的 kuafu 行（`_BodyLum` 0.11，`_SpotLum` 0.04，按贴图量出来的中位亮度）。

## 进棋盘

`BoardSlice` 现在按种类摆棋子：每种有自己的模型、动作、材质和 `<名>_anim.controller`，模型对象以种类命名（`zheng` / `kuafu`），Demo（`ShopDemo`）和 `FxTimeline` 按这个名字换材质。人形没有角，朝向取脚尖方向；缩放取「长 0.85」和「高 1.0」里更小的那个，夸父实际站高约 0.89，和狰（0.875）差不多。

两只夸父：己方前排第 4 格（成型手牌里的一张，开战时觉醒成活体），敌方前排第 2 格（器物态，战斗里出手攻击、被打、最后被击杀）。

**在实际镜头下验收**（PC 包，竖屏 540×1170 按 iPhone 13 尺寸，`-autoplay`），截图 `model/kuafu_demo.png`：上面 4 帧是准备、胡牌、战斗中、结束；下面三条依次是己方夸父（准备 → 胡牌）、己方夸父（战斗）、敌方夸父（战斗）的放大。
- 一格 75 px（9 mm），夸父和狰一样大。准备阶段己方是玉档，idle 时木杖和头在动（0.0 和 0.3 两帧差 1096 个像素）。
- 胡牌时觉醒成活体：棕发、肤色、深棕木杖、黄蛇，一眼能和狰分开。
- 战斗中己方夸父扑出去攻击、跟着牌型发动、受击闪白。敌方青铜夸父被打、出手，最后碎裂。
- 器物态（陶、青铜、玉、金）在整桌镜头下读得出是一个拄杖的小人，但五官看不清，主要靠剪影。

播放器和其他会话共用一份热更缓存（`LocalLow/bigtaoo/Relics`），清单按版本号缓存：本地 CDN 的版本号和别的会话撞了（都是 0.1.0.1），播放器就拿别人的清单去下载，全部 404。先把 `artifacts/cdn/PC/DefaultPackage.version` 改成一个没人用的号（这次是 0.1.0.8300），再跑 `BuildHotUpdate`。

## 成本

| | 积分 | 美元 |
|---|---|---|
| 实际用到的路径：seedream 1 张 + 编辑 2 张 + 图生 3D + 绑定 | 80 | $0.80 |
| 试验：4 个预设动作重定向 | 40 | $0.40 |
| 合计 | 120 | $1.20 |

和当康比：概念图少 1 次编辑（-10），多一次绑定（+25）。四足的绑定是 Blender 脚本，不花钱；人形的绑定交给 Tripo，花 25 积分，换来的是不用量关节、不用调权重。以后的人形不用再试预设，每只 $0.80。

**工期**（Claude 实际耗时，2026-10-18，本机时间）：

| 步骤 | 时间 | 用时 |
|---|---|---|
| 读文档和现有脚本 | 14:38–14:40 | 2 分钟 |
| 概念图 3 张 | 14:40–14:47 | 7 分钟（其中排队 4 分钟） |
| 图生 3D | 14:48–14:50 | 2 分钟 |
| 可绑性检查 + 绑定 + 重定向预设 | 14:51–14:55 | 4 分钟 |
| 看预设、写 `biped_rig` / `biped_anim` 和 7 个动作 | 14:55–15:03 | 8 分钟 |
| 导出、Unity 导入、头发遮罩、材质预览 | 15:03–15:13 | 10 分钟 |
| 进棋盘、出包、在 Demo 里看 | 15:14–15:45 | 31 分钟（出包 9 分钟，热更版本号冲突 15 分钟） |

从立项到 Unity 里 5 套材质加 7 个动作，约 35 分钟。人形家族的第一只比四足第二只（当康，绑定到 Unity 约 15 分钟）只多出写动作的时间，因为身体的绑定直接用 Tripo 的。

## 坑

- **worktree 里的模型文件是 LFS 指针**：新 worktree 要先 `git lfs pull`，不然 Blender 打不开 glb 和 blend。
- **Tripo 的可绑性检查**任务类型叫 `animate_prerigcheck`，不扣积分。绑定的 `model` 要填 `v1.0-20240301`，`spec: tripo` 出来的骨骼名是 `L_Thigh`、`R_Hand`、`Head` 这种。
- **重定向会给每个预设出一个 take**，`animate_in_place: true` 去掉位移。按个数扣积分（10 / 个），先只试 1 个更省。
- **Tripo 的 glb 里多一个没蒙皮的小球**（icosphere），`biped_rig.py` 删掉了。
- **活体态整个是粉色一团**：着色器只有 4 个部位遮罩（身体、铜绿、角、尾巴），头发和胡子没有自己的遮罩，就显示成肤色。现在把「头上除了脸以外的部分」（`creatures/kuafu.py` 的 `_hair`，按头部特写量的范围）也刷进角遮罩，和木杖共用深棕色。腰布和念珠还是肤色，要分开得给着色器加遮罩通道。
- 陶、玉、金三档看起来比较淡，和当康是同一个程度，没有单独调。
- 预设的检查图要在 Blender 里自己渲：Tripo 的 `rendered_image` 只有一张静帧。

## 下一步

- 下一个人形：新建 `creatures/<名>.py`（部件骨骼、遮罩、`sec()`），照上面的命令跑，在 `ToonVariants.cs` 里加一行，在 `BoardSlice` 里加种类。不用再试预设动作。
- 攻击是通用的「举杖砸」。拿剑、拿弓、空手的人形需要换攻击动作（`CLIPS` 替换，和当康的獠牙挑一样）。
- 夸父还没有专属战法特效，施法沿用狰的。
