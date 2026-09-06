# Contributing

UsbPulse is an MIT-licensed open-source project. Issues and pull requests are welcome.

## Development

Requirements: .NET 8 SDK on Windows, Linux, or macOS.

```powershell
dotnet restore
dotnet build -c Release
dotnet test tests/UsbPulse.Tests/UsbPulse.Tests.csproj -c Release
```

Keep platform-specific device code behind `OperatingSystem.IsWindows()`,
`OperatingSystem.IsLinux()`, or `OperatingSystem.IsMacOS()` checks. Do not add
idle polling for removable storage; use the platform event source and refresh
only after an event or an explicit user action.

Changes to device enumeration should include focused tests. UI changes should
be checked in light and dark themes at common display scaling values.
