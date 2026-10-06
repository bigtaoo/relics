# automatic — 8 人自走棋

Unity 客户端 + C# 服务端，共享一份定点数确定性战斗核心。设计文档入口：[`design/README.md`](design/README.md)。
本文件只放会话规则。

## 语言

- **对用户说话**：中文。
- **写进仓库的代码、注释、commit message、PR**：英文。`git commit` / `gh pr` 命令里出现中文会被 `.claude/hooks/no-cjk-vcs.mjs` 拦截，改成英文重跑，不要绕过。
- **设计文档**（`design/`）：中文。

## 目录

| 路径 | 内容 |
|---|---|
| `src/Battle.Core/` | 确定性战斗核心。netstandard2.1 / C# 9。同时是 Unity 本地包（`package.json` + asmdef，`noEngineReferences`） |
| `src/Server/` | .NET 10 服务端 |
| `tests/Battle.Core.Tests/` | xUnit 测试 |
| `client/` | Unity 工程（Unity 6），通过 `file:../../src/Battle.Core` 引用核心 |
| `design/` | 设计文档与 ADR |
| `artifacts/` | 构建输出（`UseArtifactsOutput`，保证 Unity 不会导入 bin/obj），不提交 |

## 确定性规则（Battle.Core 内强制）

- 禁止 `float` / `double` / `decimal`、`System.Math`、`DateTime`、`System.Random`、`Guid`、多线程。数值一律 `FP`，随机一律注入的 `Prng`。
- 禁止遍历 `Dictionary` / `HashSet` 来决定逻辑顺序；需要顺序时用 List 或按 id 排序。
- 只有结果确实依赖随机时才抽随机数（条件抽取会让之后的每次抽取都错位）。
- 任何改变战斗结果的改动都要升 `EngineVersion` 并记录（等对应代码落地后执行）。
- 后续会用 Roslyn 分析器把以上规则变成编译错误。在那之前靠代码评审。

## 验证

```bash
dotnet test Automatic.sln
```

改动 Battle.Core 后必须全部通过。Unity 侧的改动要在编辑器里实际跑过才算完成。

## 分支与提交

- **当前阶段（技术与美术验证期）直接在 `main` 上提交**，不建每日分支。
- 验证完成、开始加功能后切换为：禁止直接提交 `main`，每个任务一个分支（或 worktree），合进当日分支 `DD.MM.YYYY`。切换时更新本条。
- 只 `git add` 自己改过的路径，不要 `git add -A`。
- 先更新 `design/` 对应文档，再提交代码。

## 结束任务（用户说「结束任务」时，按顺序完整走一遍）

1. **记录信息**：把这次会话里产生的、文档里还没有的决策、现状和踩过的坑，写回项目文档，包括 `design/` 对应章节（08 的进度条目）、`art/*/README.md`、`client/README.md`。只留在对话里等于没记。
2. **更新记忆**：`MEMORY.md` 和对应的记忆文件。文档记项目本身，记忆记「该怎么和用户协作」，两者不重复。
3. **提交并推送**：按「分支与提交」直接提交 `main` 并 push。提交前回退 Unity 改写的两个 ProjectSettings 文件，`git add` 只加明确的路径。
4. **停进程**：停掉会话里起过的进程，包括本地 CDN（`http.server 8000`）、`Relics.exe`、Unity。

切换到分支流程后，第 3 步改为合进当日分支，并加一步删除任务分支和 worktree。某一步没事可做就跳过，不用问。

## 代码组织

- 源文件不超过 500 行，拆分优先级：独立函数模块 → 组合 + 显式 deps → 继承（最后手段）。
- 设计文档同样不超过 500 行，超出就拆成总纲 + 分卷。
