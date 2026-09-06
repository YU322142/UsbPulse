# UsbPulse

[![Build UsbPulse](https://github.com/YU322142/UsbPulse/actions/workflows/build.yml/badge.svg)](https://github.com/YU322142/UsbPulse/actions/workflows/build.yml)

一个常驻系统托盘的可移动存储提示工具。插入 U 盘后，在右下角任务栏上方显示设备名称、盘符，以及“弹出 / 打开”按钮。

- Windows 使用 `WM_DEVICECHANGE`，Linux 使用 UDisks2 事件监听，空闲时不轮询盘符。
- 多设备时显示设备选择框，新接入的设备会自动选中并恢复窗口显示。
- 支持跟随系统、浅色、深色主题和六种强调色。

## 本地运行

```powershell
dotnet restore
dotnet run
```

## 发布

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish -c Release -r linux-x64 --self-contained true
dotnet publish -c Release -r osx-arm64 --self-contained true
```

LoongArch 的专用构建流程见 `.github/workflows/build.yml`，按参考项目下载 LoongNix .NET 8 运行时并注入 Avalonia 原生库。

## 开源许可

UsbPulse 以 [MIT License](LICENSE) 开源。构建、测试和贡献约定见
[CONTRIBUTING.md](CONTRIBUTING.md)，依赖许可见
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
