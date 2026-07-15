# 💤 Windows Never Sleep

[English](README.md) | [简体中文](README.zh-CN.md)

> 只需双击，即可让 Windows 保持运行并阻止空闲自动锁屏。

这是一个简单的开源 Windows 小工具，只有两个入口：

- **Install.exe** — 安装后立即运行，阻止空闲睡眠和自动锁屏，并设置为登录自启。
- **Uninstall.exe** — 停止程序、移除自启、恢复安装前的睡眠时间并完成卸载。

手动按 `Win + L` 仍然可以正常锁屏。

## 🚀 下载

从 [GitHub Releases](https://github.com/zerbLion/windows-never-sleep/releases/latest) 下载最新版 ZIP，解压后双击 `Install.exe`。

需要卸载时，双击 `Uninstall.exe`；也可以在 Windows 设置 → 应用 → 已安装的应用中卸载 **Windows Never Sleep**。

## ✨ 功能

- 使用 Windows 原生电源请求阻止空闲睡眠。
- 将当前电源方案的交流/直流空闲睡眠时间设为“从不”，并保存安装前的值。
- 空闲达到 8 分钟时发送一个不产生文字的 `F15` 信号，避免触发 10 分钟自动锁屏。
- 保留手动锁屏、睡眠、关机和电源键功能。
- 按当前用户安装到 `%LOCALAPPDATA%\WindowsNeverSleep`，正常情况下不需要管理员权限。

F15 信号也会重置 Windows 的软件熄屏计时。如果需要黑屏，同时让无人值守的桌面操作继续运行，请直接关闭显示器的物理电源。

## 📦 两个入口

| 入口 | 效果 |
|---|---|
| `Install.exe` | 安装、立即运行、设置登录自启 |
| `Uninstall.exe` | 停止、卸载并恢复原睡眠时间 |

## 🛠️ 从源码构建

在 Windows PowerShell 中运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1
```

更多信息请参阅[构建说明](docs/BUILDING.md)和[技术原理](docs/TECHNICAL.md)。

## 📄 开源许可

[MIT](LICENSE)
