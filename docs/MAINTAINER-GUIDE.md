# 维护与发布

## 结构

| 目录／文件 | 用途 |
|---|---|
| `src/Installer` | 发现原程序、独立 WPF 安装界面、配置合并与还原 |
| `src/Bootstrap.Framework` | 应用专属 AppDomainManager 引导 |
| `src/Bootstrap.Modern` | CE x64 本地宿主转发与启动钩子 |
| `src/Adapters` | 结构检查、运行时修复、原生提示和可选更新 |
| `scripts` | 构建、允许列表打包和清单签名 |
| `manifests` | 发布通道配置与清单示例 |
| `.github/workflows` | main／PR 构建检查与明确版本的自动签名发布 |
| `.local` | 本地开发资料、内部测试、日志、备份与缓存；不公开 |
| `runtime` | 已安装运行组件；不进入源码仓库 |
| `dist` | 生成的发布附件；作为 Release 附件分发 |

仓库只公开 `public-files.txt` 列出的源代码、许可及对外说明。内部测试和报告不属于源码发布包。`.gitignore` 与打包允许列表共同限制公开范围，不将整个工作目录直接压缩上传。

## 构建

需要 Windows、.NET Framework 4.8、.NET 9 SDK、Visual Studio 2022 C++ 工具及 Windows SDK。

```powershell
powershell -NoProfile -File .\scripts\build.ps1
powershell -NoProfile -File .\scripts\package.ps1
powershell -NoProfile -File .\scripts\verify-package.ps1
```

首次构建下载固定版本 Harmony 2.4.3，先校验上游发布 ZIP 的 SHA-256，再使用所需的 net48／net8.0 DLL。缓存进入 `.local/deps`。使用修复工具的普通用户不需要安装构建工具。

适配器发布版本集中在 `src/BuildInfo.cs`。常规迭代修改 `AdapterVersion`，构建与打包脚本据此选择目录、程序集版本及清单版本。引导代际与适配器小版本分开；更改引导协议时需要单独审查兼容性。

发布附件为修复安装器，不包含原 PCL EXE、赞助文件、游戏、私有运行时或备份源码。构建产物也不直接加入源码提交。

## 修复方式

原作者分支通过 EXE 旁的应用配置加载引导层；原 EXE 字节不变。引导层再加载独立版本目录下的适配器。

适配器保留新版的压缩包识别、路径校验、原生各格式安装器、异常处理与 ZIP 清理。根据真实调用和局部变量的数据流识别连接点，重新补上被裁剪的分发部分；不按绝对偏移修改 EXE，不拼入整份旧版模块。

列表加载器保留新版的完成回调。在线整合包行和保存图标使用各自真实的委托类型；显式补足 VB 的参数转换，不把二者假定为同一类型。非整合包资源沿用原回调。

提前接入时，原程序的完整依赖解析可能尚未注册。适配器按程序集身份解析原 PCL 已内嵌的依赖，核对名称、版本、区域和公钥标记，不仅限于几个固定库。尤其要保留 `Microsoft.Win32.Registry` 等兼容依赖，否则原生设置读取可能回退为空并生成错误识别码。此处恢复原有读取能力，不修改设备标识、游戏路径、账号或设置内容。

功能补丁不会在程序集刚加载时立即安装。引导层等待 WPF `OnStartup` 中原生启动处理完成，再在 `StartupUri` 创建主窗口之前安装功能补丁；主窗口加载后解除临时启动钩子。不拦截 WPF Window 构造函数。这样保留原应用的依赖、标识和设置初始化顺序，同时让页面与启动导入使用已经恢复的方法。

CE x64 使用应用本地的 `hostfxr.dll` 转发入口。注册列表只对指定启动器注入启动钩子，微软原宿主继续选择并运行 .NET；不写全局启动钩子、系统区域或自动启动项。启动钩子恢复进程环境中的原启动钩子变量，再加载适配器。

CE 适配在启用前审查当前 PCL 下载策略的调用者；不修改 Windows 的地区信息，也不修改其他程序的地区判定。

必需修复项组成一组：结构或签名不匹配时撤销本组补丁，保留原启动器运行，并记录真实状态。新格式、新委托或宿主大改需要重新审查，不能仅靠放宽版本号判定为兼容。

## 可选在线更新

当前 `manifests/channel.json` 已预置 `plumess/PCL2-download-repair`，更新入口是 `https://github.com/plumess/PCL2-download-repair/releases/latest/download/stable.signed.json`。自动更新不使用 GitHub 令牌，需要可匿名访问的稳定 Release；网络请求失败不会阻塞启动。保持同一仓库和公钥时，已安装客户端自动沿用这个地址，无需重装。

更新清单使用一个 JSON 信封：`payload` 为清单原始字节的 Base64，`signature` 为这些字节的 RSA-SHA256/PKCS#1 签名。客户端只信任已安装通道里的公钥，要求 RSA 至少 2048 位。不要只依赖与文件放在同一站点的散列值。

参考 `manifests/update.example.json`，填入实际版本、4 个组件的 HTTPS 地址、字节数及 SHA-256。发布时应使用清晰的稳定版本号；普通文档提交不触发客户端更新。

## 自动构建与发布

普通提交到 `main` 或面向 `main` 的 PR 会运行 **Build**：云端使用 Windows 2022、.NET 9 SDK 和 C++ 工具构建，按允许列表打包、核查源码及组件内容，再保存 7 天的构建附件。普通构建不接触发布密钥，也不创建正式 Release。

明确发布有两种入口：

1. 修改 `src/BuildInfo.cs` 中的 `AdapterVersion`，同步更新 `docs/RELEASE-NOTES.md` 和教程中的示例版本，把修改提交到 `main`。例如下一版为 `0.3.7`。
2. 在 GitHub 仓库中打开 **Actions → Release → Run workflow**，选择 `main`，填写完全相同的版本号；取消勾选 `dry_run`，点击运行。
3. 等待任务完成。构建、ZIP、签名清单、散列校验、附件上传和正式 Release 都由云端完成，不用从本机上传压缩包。

也可以在该 main 提交上推送 `release-0.3.7` 标签，触发同一条发布流水线。标签必须与 `AdapterVersion` 相同，源提交必须属于 main 历史。`dry_run` 默认勾选时只构建校验，不访问签名密钥、不发版。正式 Release 的正文自动读取 `docs/RELEASE-NOTES.md`。

发布过程先保存为草稿，全部附件上传并与 GitHub 返回的 SHA-256 核对后，才发布为仓库内的稳定 Release。已正式发布的同版本不允许覆盖；失败的同提交草稿允许重试。发版不会自动改变仓库可见性。

需要额外验证真实启动器时，可以勾选 `hold_as_draft`：云端仍完成构建、签名、上传与散列检查，最后停在已验证草稿；本机验收同一份组件后再发布草稿，无需重打包或替换附件。正常自动发版保持该项关闭。

### 云端签名 Secret

本仓库的正式签名方式是 GitHub Actions Secret **`PCL_REPAIR_SIGNING_KEY`**，值为完整 RSA 私钥 XML 的 UTF-8 字节再进行 Base64 编码。生成的密钥为 RSA 3072 位，公钥在 `manifests/channel.json`；私钥不存进源码或构建附件。

只有发布任务的签名步骤会把该 Secret 注入环境变量，签名脚本在内存中读取、检查是否匹配公钥，再清除进程内该环境变量。普通 CI、PR 和构建任务不获得该密钥。工作流使用固定提交的官方 Actions、只读 checkout，只有发布任务具有 `contents: write` 权限。

签名由云端自动调用：

```powershell
powershell -NoProfile -File .\scripts\sign-release.ps1 -FromEnvironment `
  -ManifestPath .\dist\update\manifest.json `
  -OutputPath .\dist\update\stable.signed.json
powershell -NoProfile -File .\scripts\verify-package.ps1 -RequireSignature
```

Secret 不是只允许签名的硬件密钥库：获准读取它的工作流可以取得私钥明文。应保护 GitHub 管理账号、发布权限以及工作流和签名脚本的修改权限，不把不可信 PR 代码放入带密钥的任务。云密钥库／HSM 可以进一步限制私钥导出，但签名调用权限本身仍需保护。

这里的签名是补丁更新清单签名，不是 Windows Authenticode 代码签名证书。

### 变更更新签名公钥

客户端只信任已安装通道里的公钥，不能自动信任一个任意替换的新公钥。如果需要更换密钥，应明确安排迁移：例如让用户下载新的完整修复包运行一次，更新通道公钥；此后保持同一密钥，普通适配更新无需反复安装。不能覆盖旧 Release 的附件来伪装成无须迁移。

签名脚本也提供 Store／File 模式供自建通道维护使用，密钥必须与对应通道公钥匹配。私钥应由维护者单独保管，不能放入源码、日志、构建附件或 Release。

`package.ps1` 同时准备 `dist/update` 下的四个组件和待签名清单。正式发布的标签与标题都使用 `release-<AdapterVersion>`，例如 `release-0.3.6`。云端把四个 DLL、`stable.signed.json`、两个 ZIP 和 `SHA256SUMS.txt` 附加到同一 Release。`manifest.json` 是签名输入，不单独进入正式 Release；用户只需下载修复 ZIP。用户 ZIP 同时包含新手教程、README、界面图片、发布说明和许可证。

发布附件的固定命名：

- `PCL2-download-repair-release-0.3.6.zip`：普通用户下载。
- `PCL2-download-repair-release-0.3.6-source.zip`：允许列表中的公开源码。
- `stable.signed.json`、`framework-adapter.dll`、`framework-harmony.dll`、`modern-adapter.dll`、`modern-harmony.dll`：后台适配更新使用，名字保持稳定。

下次发布时以新的 `AdapterVersion` 替换示例中的 `0.3.6`。版本使用三段数字 `MAJOR.MINOR.PATCH`，不省略补丁号、不加前导零。例如小修复为 `0.3.7`，增加功能为 `0.4.0`；不兼容变化必须说明迁移方法。`BootstrapGeneration` 是内部引导协议编号，与产品发布版本不同。

产品名及仓库名为 `PCL2-download-repair`。Release 附件名、压缩包内所有路径、程序文件名和文档文件名统一使用英文／ASCII；教程与界面保留中文，便于新手使用。打包脚本会拒绝非 ASCII 的压缩条目名。

云端发版不再依赖本机在线。不要随意删除或重新生成签名 Secret：更换密钥需要再次设计客户端信任迁移。GitHub 不提供从设置界面读回 Secret 明文的功能；应按自己的密钥管理方式保护签名能力。Fork 项目应使用自己的更新地址、公钥和签名 Secret。

客户端在主窗口就绪后后台检查，通常 12 小时一次，带条件请求缓存；启动器文件变化时可提前检查。下载失败保留当前版本，不弹重复的网络失败提示，不上传启动器、路径、账号或日志。

新组件通过签名、大小、散列和程序集身份检查后放入独立版本目录，再原子切换版本指针。当前进程继续用原有程序集，下一次正常启动加载新版本。只保留当前与上一个版本；坏版本会被隔离，失败后回退指针。引导组件自身不通过此组件通道强制热替换。

## 安装与还原的约束

- 合并原配置中的其他节点，保存必要的原配置小备份。
- 已存在不同的 AppDomainManager 时不覆盖。
- CE 入口记录本工具所写文件的散列；只升级或移除能确认归属的入口。
- 正常关闭原 PCL 后处理，不强制结束用户游戏。
- 先解除应用配置／宿主注册，再考虑移除运行组件。
- 官方更新替换整个目录或更换宿主机制时，引导可能消失，需要重新安装；不能保证任何未来版本永远无需维护。

## 新适配发布前应核对

核对原入口、快捷方式、启动器自身更新后的重启、拖入、手动选择与取消、启动自动导包、在线安装与独立保存、各格式、目录层级、异常退出及 ZIP 释放。实际安装应检查任务完成、实例文件和校验值，不能仅看版本列表是否出现。

区分真实安装、路由检查、替换更新测试和图形界面验收。内部测试数据、原始启动器和截图不随公共源码发布。仅保存最新下载模块源码的小型本地备份，避免镜像历史仓库。

本目录不包含自动初始化仓库、创建远端或推送的脚本；这些操作由维护者另行执行。
