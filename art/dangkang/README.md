# 当康 · 第二只四足（Tripo 一站式出图实测）

《山海经·东山经》：「其状如豚而有牙，其名曰当康，其鸣自叫，见则天下大穰。」卡通野猪，两根獠牙，背上一排鬃毛，脖子上挂稻穗（丰收）。

目的：验证 11 §7 的「出图从 Mistral 换到 Tripo」，以及「同家族第二只」的成本。2026-10-08。

总览 `dangkang_overview.png`（活体、过渡、器物、3D）。

工具：`tools/art/tripo.sh`（出图、编辑、图生 3D 都走它，每个任务存 `<name>.req.json` + `<name>.task.json`），`tools/art/blender/turntable.py`（转台对比图）。提示词放在同名 `.txt` / `_fix.txt`。

## 概念图（`concept/`）

| 文件 | 来源 | 结果 | 积分 |
|---|---|---|---|
| `toon_a_banana2.png` | 文生图 banana2 1K，`toon_a.txt` | 风格对，朝左。两根獠牙一弯一直不对称，镜头偏平视 | 10 |
| `toon_a_seedream.png` | 文生图 seedream_v5 2K，同一提示词 | **选用**：獠牙对称，镜头更俯视。瑕疵：嘴里叼了一束稻穗 | 5 |
| `living_v1.png` | 编辑 seedream 那张（`input` 直接填任务 ID），chat_image_2.5_sunburst，`living_fix.txt` | **活体态**：只去掉了嘴里的稻穗，其余像素不变 | 10 |
| `artifact_v1.png` | 编辑 living_v1，sunburst，`artifact_fix.txt` | **器物态**：青铜 + 铜绿、闭眼、身上卷云纹，形体和活体态完全一致。用于图生 3D | 10 |
| `transition_v1.png` | 编辑 living_v1，sunburst，`transition_fix.txt` | **过渡态**：前半活、后半青铜开裂透金光，鬃毛也一半一半 | 10 |

三态各 1 次就对齐，没有返工。狰在 Mistral 上用了 8 张，丢尾巴、丢角，反复返工。

## 模型（`model/`）

| 文件 | 来源 | 结果 | 积分 |
|---|---|---|---|
| `gen_v1.glb` | 图生 3D，`input` 填 artifact_v1 的任务 ID，参数同狰的 toon_v1（v3.1、PBR、face_limit 20000） | **选用**：獠牙、鬃毛、卷尾、稻穗项圈都在，比例和概念图一致。约 1 分钟。预览 `gen_v1.preview.png` | 30 |
| `multiview_v1.*.jpeg` | 图转多视图，artifact_v1 先上传（这个接口不收任务 ID） | 4 张一致，但「正面」其实是斜 45°。汇总 `multiview_v1.sheet.jpeg` | 10 |
| `gen_mv1.glb` | 多视图转 3D，`inputs: [{task_id}]` | 更胖、头更大，背后多出一束稻穗（多视图猜的），表面更起伏 | 30 |

对比图 `compare_single_vs_multiview.png`（上：单图，下：多视图；前、左、后、右、棋盘镜头）。

**结论：单图就够了**。当康形体简单、左右对称，多视图多花 40 积分，反而偏离概念图。多视图留给狰这种背面有关键结构的角色，没有实测。

## 成本

| | 积分 | 美元 |
|---|---|---|
| 实际用到的路径：seedream 1 张 + 编辑 3 张 + 图生 3D | 65 | $0.65 |
| 对比试验：banana2 1 张 + 多视图两步 | 50 | $0.50 |
| 合计 | 115 | $1.15 |

API 时间合计约 10 分钟，没有限流等待（免费档同一时间只能跑 1 个任务，第 2 个会返回 `code 2000`，排队提交就行）。

## 绑定和动作（2026-10-08，`model/`）

狰的脚本拆成了四足家族共用部分和每个角色的关节表：

- `tools/art/blender/quadruped_rig.py`：负责删胡须、减面、体素代理自动权重、小部件权重限制和部位遮罩。
- `tools/art/blender/quadruped_anim.py`：7 个共用动作，只驱动 root、脊柱和四条腿。
- `tools/art/blender/creatures/<名>.py`：每个角色一个文件，写骨骼表、尾巴链、角（或獠牙）、权重限制，以及次级动画 `sec()`。可以用 `CLIPS` 替换整个动作。
- 拆完后用新脚本重跑狰，网格、权重、遮罩和 30528 个关键帧的哈希和拆之前完全一致。

| 步骤 | 结果 |
|---|---|
| 量关节 | 三视图加网格线（正交，0.05 一格），加按高度切片的顶点聚类，见 `creatures/dangkang.py` 开头的注释。腿很短，肚子下面只露出 z -0.43 到 -0.29 |
| `dangkang_rig.blend` | 26 根骨骼：root、脊柱 5、獠牙 2、耳朵 2、稻穗 1、四腿各 3、卷尾 3。减到 3000 面。权重分布图 `dangkang_weights.png`（侧、前、上、后，按主导骨骼着色） |
| `dangkang_anim.blend` | 7 个动作。待机、施法、受击、扑跃、死亡、觉醒和狰共用，攻击换成獠牙挑（`gore`）：低头后退蓄力，冲出去把獠牙往上挑，接触帧和狰一样在 0.42。次级动画是卷尾摆动、耳朵抖动、稻穗滞后摆动。侧视检查图 `dangkang_anim.sheet.png` |
| 导出 | `client/Assets/HotRes/Art/Dangkang/dangkang.fbx`（7 个 take）+ `dangkang_basecolor.png` |
| Unity | `Batch.ArtPreviewDangkang`，输出到 `artifacts/art_preview/dangkang/`。5 套材质 `dangkang_variants.png`，活体态 7 个动作 `dangkang_unity.sheet.png` |

命令（仓库根目录）：

```bash
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"; M=D:/automatic/art/dangkang/model
"$B" -b --python tools/art/blender/quadruped_rig.py -- dangkang $M/gen_v1.glb $M/dangkang_rig.blend
"$B" -b --python tools/art/blender/quadruped_anim.py -- dangkang $M/dangkang_rig.blend $M/dangkang_anim.blend
"$B" -b --python tools/art/blender/export_fbx.py -- $M/dangkang_anim.blend D:/automatic/client/Assets/HotRes/Art/Dangkang/dangkang.fbx
```

坑：
- **圆身体上，小部件会抢走躯干的权重。** 第一版里耳朵骨骼拿走了整条鬃毛和半个头，卷尾根部拿走了后半截鬃毛，稻穗拿走了整个左肩，胸和颈没有分到一个顶点。原因是骨骼热度看的是离表面的距离：脊柱埋在胖身体中间，耳朵和稻穗贴着表面。现在的做法是 `limit_weights`：小部件只保留离自己骨骼 r 以内的权重，到 2r 衰减为 0，减掉的部分按比例分给这个顶点的其他骨骼。只在这些骨骼都没有权重时，才交给父骨骼。一开始全交给父骨骼，结果头分到了半个后背。
- 大腿和上臂的起点放低到肚子里（z -0.24），不然会拿走两侧的肚皮。
- 当康的青铜贴图里，铜绿斑的中位亮度只有 0.03，狰是 0.13。着色器原来把狰的亮度中位数写死了，现在改成材质参数 `_BodyLum` / `_SpotLum`，每个角色在 `ToonVariants.cs` 里各填各的。
- 活体态没有肚皮和猪鼻子的遮罩，米白肚皮和粉鼻子都显示成身体色，只有獠牙（复用「角」的遮罩）是象牙色。要分开的话需要在绑定时多刷遮罩通道，现在只有尾巴和角两个。

**同家族第二只的工期**（Claude 实际耗时）：从量关节到 Unity 截图，约 15 分钟。其中量关节约 3 分钟，权重调了 1 轮，写挑击动作和调活体态颜色各几分钟。拆分脚本和修 ArtPreview 的问题（见 `client/Assets/Editor/ArtPreview.cs` 里预热渲染的注释）是一次性的，不算在内。外部花费 0。

质量判断和狰一样，是「能用的占位」。胖身体后仰施法的姿势有点滑稽，但读得懂。

## 下一步

- 当康还没有进棋盘切片（`BoardSlice` 只摆狰），也没有专属特效。
- 第三只四足可以直接照这个流程做：新建 `creatures/<名>.py`，量关节，跑上面三条命令，在 `ToonVariants.cs` 里加一行。
