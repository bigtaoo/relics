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

## 下一步

绑定和动作还没做。`rig_zheng.py` 和 `anim_zheng.py` 的骨骼表、尾巴链是狰专用的。当康要把它们拆成「四足家族共用部分 + 每个角色的关节表」，关节位置要重新量；当康只有一条短卷尾，獠牙和鬃毛不用单独的骨骼。
