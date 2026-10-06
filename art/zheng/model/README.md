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
