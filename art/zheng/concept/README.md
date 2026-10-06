# 狰 · 概念图（08 §1）

工具：`tools/art/`（从 standing 搬来的 Mistral 出图脚本，key 在 `~/.vibe/`，不进仓库）。
每张图旁边放同名 `.txt` 提示词；`_fix.txt` 是 `edit_image.sh` 用的编辑提示词。落选的放在 `r1/`。

## 定稿（2026-10-06，第 1 轮）

| 文件 | 来源 | 说明 |
|---|---|---|
| `artifact_v5.png` | `edit_image.sh artifact_v4.jpg` + `artifact_v5_fix.txt` | **器物态**：青铜 + 铜绿、五尾、圆底座、俯视斜角。后续图生 3D 用这张 |
| `living_v1.png` | `edit_image.sh artifact_v5.png` + `living_fix.txt` | **活体态**：赤豹红毛、豹斑、玉色独角、尾尖带火 |
| `transition_v1.png` | `edit_image.sh artifact_v5.png` + `transition_fix.txt` | **过渡态**：青铜内部透出熔金光，头部先活 |
| `artifact_v4.jpg` | `generate_image.sh` + `artifact_v4.txt` | v5 的造型来源（材质跑成了红漆） |

已知瑕疵（建模时处理，不再重出图）：
- 器物态头上像有两只小角（一角 + 耳朵混在一起），建模按「一角」做。
- 活体态底座被染成了红色，应保持石座 / 青铜座。
- 活体态没有保留「金色纹路」，觉醒特效里再补。

## 第 1 轮过程（`r1/`）

| 版本 | 问题 |
|---|---|
| v1 | 两只角；尾巴像翅膀；彩漆 + 鎏金，不像青铜；镜头偏平视 |
| v2 | 独角对了，质感太亮，只有 1 条尾巴 |
| v2_tails / v2b | 编辑加尾巴 → 背上长出扇骨 / 毛刷，原尾巴还在 |
| v3 | 俯视角和蹲伏姿势好；尾巴变成带疙瘩的触手，偏黑 |
| v3_fix / v3b | 青铜质感对了，尾巴始终是 7 条 |

## 经验

- **数量写不准**：「五尾」直接生成几乎从不正确（1、6、7 条都出现过）。有效做法是先要一个「扇形排列、像张开的五指」的造型，再用编辑「删掉最左边一条」精确到 5。
- 写了「red leopard」就会整体变红；要青铜就必须写「ONE material… No red, no paint, no lacquer」。
- 「chess piece / 底座 / 俯视 45°」一起写，才会出棋子感和俯视镜头。
- 换材质、换状态用 `edit_image` 从同一张图出，造型和姿势能保持一致（三态对齐，后续建模只需一套形体）。
- 每个 key 有出图配额，被限流后脚本 3 分钟换下一个 key；两张图并行时给不同的 `MISTRAL_KEY`。

## 成本

- 生成 + 编辑共 12 次，约 25 分钟（多数是等限流），Mistral 免费档，费用 0。

## 第 2 轮：卡通风格（2026-10-06，进行中，`r2/`）

风格改为卡通（04 §3），第 1 轮写实青铜图作废，只留作流程参考。

| 文件 | 来源 | 结果 |
|---|---|---|
| `toon_a.jpg` | 生成，`toon_a.txt`（Supercell / 荒野乱斗风） | 风格对；五条火焰尾巴好看。问题：多一条普通豹尾、两只角、豹皮而非青铜 |
| `toon_b.jpg` | 生成，`toon_b.txt`（Q 版搪胶玩具风） | 太萌、正面朝镜头、只有 2 条尾、青绿漆不像青铜，弃用 |
| `living_v1.png` | 编辑 toon_a，`living_fix.txt` | 角对了，尾巴全没了 |
| `living_v2.png` | 编辑 v1 加尾巴，`living_tails_fix.txt` | 豹子没了，只剩一团火焰，弃用 |
| `living_v3.png` | 编辑 toon_a，`living_fix2.txt`（「其余像素不变」） | 五尾正确、普通豹尾去掉，但两只角都没了 |
| `living_v4.png` | 编辑 v3 加角，`living_horn_fix.txt` | **活体态候选**：一角、五尾。角偏长 |
| `transition_v1.png` | 编辑 v4，`transition_fix.txt` | 尾巴又丢了，几乎全身青铜 + 裂纹，质感偏写实，不合格 |
| 器物态 | 编辑 v4，`artifact_fix.txt` | 未出：5 个 key 全部被限流 15 分钟以上，等配额恢复后重跑 |

经验（补充）：
- 编辑时写「Keep EVERYTHING else pixel-identical」比「Keep the same…」更能守住其余部分。
- 半透明的火焰尾巴在换材质的编辑里很容易被整个删掉；器物态要让尾巴是实心的造型，这样图生 3D 也才能做出来。
