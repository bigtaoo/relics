# 09 · 构建与发布（CI）

> 状态：草案。参考 funny 的 `release-ios.yml`、`ota-publish.yml` 和 `design/game/IOS_RELEASE.md`，结论和踩过的坑直接沿用。
> 关联：热更方案 07，平台决策 ADR-009。

## 1. 原则

- **不需要 Mac**：iOS 在 GitHub Actions 的 macOS runner 上构建、签名、上传 TestFlight。签名材料在 Apple 后台在线生成，转 base64 存进 GitHub Secrets（funny IOS_RELEASE §2～3）。只有本地真机调试才需要 Mac。
- **三端一条版本线**：三端同服（ADR-009），同一局的人必须是同一版核心（07 §5）。版本号规则全平台统一，热更版本全平台同时发布。
- **CI 构建是唯一的发布来源**，不从开发机手动出包。

## 2. 流水线

| 流水线 | 触发 | Runner | 产物 |
|---|---|---|---|
| `ci.yml` | PR、推送 | ubuntu | `dotnet test`（Core + 服务端）、Unity EditMode 测试 |
| `release-ios.yml` | tag `ios-v*` / 手动 | ubuntu 导出 Xcode 工程 → macOS 编译签名 | IPA → TestFlight |
| `release-android.yml` | tag `android-v*` / 手动 | ubuntu | AAB → Google Play 内部测试轨道 |
| `release-steam.yml` | tag `steam-v*` / 手动 | windows | Windows 包 → Steam（先传 beta 分支） |
| `hotupdate-publish.yml` | tag `hu-v*` / 手动 | ubuntu | 三平台热更 DLL + YooAsset 资源包 → CDN，最后传版本清单 |

### iOS 两段式构建

1. **ubuntu 上用 Unity 导出 Xcode 工程**（GameCI `unity-builder`，targetPlatform iOS）。HybridCLR 的生成步骤（`Generate/All`）在导出前通过 `-executeMethod` 执行。
2. 导出的工程作为 artifact 交给 **macOS job**：`xcodebuild archive` → `exportArchive` → `altool` 上传。签名、导出选项、上传步骤照搬 funny `release-ios.yml`。

macOS runner 分钟数更贵，所以 Unity 的耗时步骤放在 ubuntu 上。HybridCLR 在 iOS 上的具体构建要求以官方文档为准，在 07 §7 的最小验证中跑通。

手动触发保留 funny 的 `destination: none` 选项：只编译不上传。用来回答「原生层还能不能编译」这个 Windows 开发机回答不了的问题，又不白白消耗 build 号。

## 3. 版本号（沿用 funny §11.3 的版本线）

| 产物 | 版本 | 例 |
|---|---|---|
| 安装包（壳） | `<X.Y>.<build>`，build = CI run number | `0.1.15` |
| 热更 | `<最新壳版本>.<n>` | `0.1.15.1`、`0.1.15.2` |

- 热更一定比它所基于的壳新；下一个壳一定比任何为旧壳出的热更新，不会被「更新」回旧代码。
- 版本服的 `最低壳版本`：热更依赖了新的原生能力时就抬高它，老壳不会拉到跑不起来的包。
- **护栏**（funny 的教训）：
  - 壳的版本号必须在 CI 中烘焙进包。funny build 11～14 因为没烘焙、版本是 `0.0.0`，被当成开发包，**永远收不到热更**，只能靠用户重装修复。
  - 要有一个测试读取 workflow 文本，断言版本烘焙和护栏步骤都还在。
  - 发布热更时：版本必须严格大于线上版本；**先传包，最后传版本清单**，避免清单指向尚未上传的文件。
  - 发布壳时：线上热更版本 ≥ 本次壳版本就失败，否则新壳首次启动会降级。

## 4. 平台注意事项

### iOS

- **部署目标设为 iOS 15 以上**：Apple 已经发出警告，2027 年春季起上传要求最低 iOS 15（funny 上传时收到的 90068 警告）。基准机型 A13 能升级到 iOS 15，不受影响。
- 代码热更的合规边界见 07 §8：只修 bug、调数值、迭代已审核过的玩法；**绝不能**通过热更引入绕过 Apple IAP 的支付路径。
- Apple 账号、签名证书、ASC API Key 可以和 funny 共用同一个开发者账号，新建 App ID 即可。Bundle ID 一旦上架就不能改，起名前先确认。

### Steam

- Steamworks SDK 属于原生插件，放在 AOT 壳里（07 §4）。
- Steam 本身就能推送整包更新，但仍然走我们的热更通道，原因是要与手机端**同时**更新版本、保持三端同服。Steam 整包更新只用于更新壳。
- 用 `steamcmd` 上传，先传 beta 分支，验证后再在 Steamworks 后台设为默认分支。

### Unity 授权

- CI 运行 Unity 需要授权，GameCI 的文档说明了 Personal 和 Pro 两种激活方式。授权信息存进 Secrets。

## 5. 待办

- [ ] 确定 Bundle ID / Steam App ID / Android 包名（上架后不可改）
- [ ] Apple：新建 App ID 和 ASC 记录，签名材料存进本仓库的 Secrets
- [ ] Steam：注册 App（Steam Direct 费用），建 depot 和 beta 分支
- [ ] Unity 授权接入 CI
- [ ] 07 §7 热更最小验证在 TestFlight 真机上跑通
