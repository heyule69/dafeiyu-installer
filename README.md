# 大肥鱼一键安装器

Windows 图形安装器，自动检测和安装 DeepSeek Harness（DSH），再安装或升级大肥鱼 Aurabot 插件。

## 下载与使用

[查看最新版本与更新说明](https://github.com/heyule69/dafeiyu-installer/releases/latest)。

1. [下载一键安装器 DafeiyuInstaller.exe](https://github.com/heyule69/dafeiyu-installer/releases/latest/download/DafeiyuInstaller.exe)。
2. Windows 10 / 11 64 位系统下双击运行，点击“开始安装”。程序暂未签名，Windows 可能提示“Windows 已保护你的电脑”。
3. 按 DSH 安装窗口提示完成安装；安装插件前先保存 DSH 中的内容，再确认关闭，完成后会重新打开 DSH。
4. 在 DSH 中配置模型 API Key，然后打开 Aurabot 设置向导完成 QQ 和网关配置。

安装插件需要能够访问 GitHub。重复运行安装器即可升级插件，设置和记忆会保留。

## 已有 DSH 安装包

把 DSH 官方 Windows 安装包与 `DafeiyuInstaller.exe` 放在**同一文件夹**，安装器会直接使用，跳过下载。

- 优先识别 `dsh-latest-windows-x64.exe`。
- 也支持 `dsh-版本号-windows-x64.exe`、带 `(1)` 后缀的文件名，以及 `DeepSeek Harness Setup*.exe`。
- 存在多个候选时，优先标准文件名，否则选择修改时间最新的。
- 只搜索安装器所在目录，不搜索子文件夹或系统“下载”目录。
- 本地包缺失时，才会询问是否从 DeepSeek 官网下载。
- 所有 DSH 安装包运行前都会校验可信的 DeepSeek 官方数字签名。校验失败时停止，保留本地文件。

电脑已经安装 DSH 时，会跳过 DSH 安装步骤。SnowLuma 需自行下载和解压，并在插件向导中确认其协议。

## 日志和排错

安装日志：在资源管理器地址栏输入 `%TEMP%\dafeiyu-install.log`。

安装插件失败时，检查 GitHub 网络连接，并确认 DSH 已完全退出（包括托盘）。首次使用 DSH 时，安装器会自动打开一次完成初始化。

## 从源码编译

使用 Windows 自带的 .NET Framework 4.x C# 编译器，无需 Visual Studio：

```bat
installer\build.bat
```

生成文件：`dist/DafeiyuInstaller.exe`。窗口图片和图标已包含在源码中，直接编译即可。

需要重新生成图片时，在 Python 环境安装 Pillow，然后运行：

```powershell
python -m pip install Pillow
python installer/assets/prepare_assets.py
```

本地安装包识别和签名校验的回归检查在 `installer/tests/LocalPackageTests.cs`。编译时与两个安装器 C# 源文件一起传入 `csc`，指定 `/main:LocalPackageTests`；运行时传入有效的 DSH 官方安装包路径。检查不会启动安装包或更改本机安装。

## 相关项目

- [DeepSeek Harness 官网](https://www.deepseek.com/en/harness/)
- [大肥鱼 Aurabot 插件](https://github.com/accomplish07zrh-eng/dsh-aurabot-qq)
- [SnowLuma 下载](https://github.com/SnowLuma/SnowLuma/releases)

本仓库提供第三方安装工具，不是 DeepSeek、Aurabot 或 SnowLuma 的官方发布。
