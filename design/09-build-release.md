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

### iOS 现状（2026-10-07）

`.github/workflows/release-ios.yml` 已写好，还没跑过：缺 Unity 授权和 Apple 签名的 Secrets（§5）。

- **Windows 上已验证的部分**：装了 iOS 模块后，`BuildPlayer -buildTarget iOS` 能导出 Xcode 工程（1.3 GB），同一份工程就是 CI 第一段的产物。
  - HybridCLR 的 libil2cpp 源码在导出的工程里，由 Xcode 编译，不需要另外打 libil2cpp.a。
  - 热更构建的 API 检查在 iOS 上同样生效。第一次导出就拦下了热更代码新用到、但壳里被裁掉的 `UploadHandlerRaw`（已加进 link.xml）。
- **CI 第一段**：GameCI 的 `unityci/editor:ubuntu-6000.3.25f1-ios-3` 镜像，入口是 `Batch.CiBuildPlayer`，先装 HybridCLR，再走 `PlayerBuild`。通过 `-shellVersion 0.1.<run> -buildNumber <run>` 把壳的版本烘焙进包（§3 的护栏）。产物有两份：Xcode 工程，以及这个壳对应的 `cdn/iOS` 资源。
- **CI 第二段**：
  - `none`：不签名，只编译（`CODE_SIGNING_ALLOWED=NO`），只要 Unity 的 Secrets 就能跑。
  - `testflight`：用 ASC API Key 自动签名（`-allowProvisioningUpdates`），Xcode 自己注册 Bundle ID、生成描述文件，所以不用像 funny 那样手动存描述文件。签名证书还是要导入。
- **Unity 授权有风险**：GameCI 的个人版激活文档要 `Unity_lic.ulf`，但 Unity 6 的个人版是 entitlement 授权，本机只有 `UnityEntitlementLicense.xml`，没有 `.ulf`。先只配邮箱和密码试一次；不行的话，改为在本机导出 Xcode 工程，传给 macOS 那一段（用草稿 Release 中转，不公开），只有签名和编译在 CI 上做。
- **不用 Mac 跑真机测试**：iOS 没有启动参数，也没有 adb。
  - 启动方式：用链接 `relics://run?cdn=...&name=...&args=...` 启动应用（URL Scheme）。壳从链接里读 CDN 地址并记住，热更层从链接里读压测参数（`LaunchArgs`）。
  - 测试页：电脑上运行 `tools/bench/phone_cdn.py`，它代替 `http.server` 提供 CDN，还提供一个测试页 `/ios`，每个测试一个链接。在 iPhone 的 Safari 里打开测试页，点链接即可。
  - 结果回传：压测结束后，结果行通过 POST 传回电脑（`BenchReport`），存在 `artifacts/bench/ios/`，同时留在屏幕上，不退出应用。
  - 准备工作：iPhone 和电脑要在同一个 Wi-Fi；Windows 第一次会询问是否允许 Python 接受专用网络连接，要选允许；iOS 第一次会询问是否允许访问本地网络，也要允许。

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

- [ ] 确定 Bundle ID / Steam App ID / Android 包名（上架后不可改）。现在用的是占位的 `com.bigtaoo.Relics`
- [ ] Apple：在 App Store Connect 新建 App 记录（Bundle ID 由 CI 自动注册）。把 funny 的签名证书、Team ID、ASC API Key 存进本仓库的 Secrets（Secrets 的值不能跨仓库复制，要重新填）。名单见 `release-ios.yml` 文件头
- [ ] Steam：注册 App（Steam Direct 费用），建 depot 和 beta 分支
- [ ] Unity 授权接入 CI：配 `UNITY_EMAIL` / `UNITY_PASSWORD`，先跑一次 `destination: none`
- [ ] 07 §7 热更最小验证在 TestFlight 真机上跑通
