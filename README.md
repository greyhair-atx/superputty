# SuperPuTTY

> **Community fork:** This repository is an unofficial fork of
> [jimradford/superputty](https://github.com/jimradford/superputty). It is used
> to develop and evaluate possible fixes before they are reviewed or merged
> upstream. Changes and test builds published here may be experimental and
> should not be treated as official SuperPuTTY releases.

SuperPuTTY is a Windows application for managing PuTTY SSH terminals. It also
supports RDP sessions and provides a simple scripting language for common
tasks.

## Development

The `sp-1.6.0` codebase targets .NET 10 for Windows x64. Install the .NET 10
SDK, then build and test from PowerShell:

```powershell
dotnet build .\SuperPutty.sln -c Release
dotnet test .\SuperPuttyUnitTests\SuperPuttyUnitTests.csproj -c Release --no-build --filter "TestCategory!~Net"
```

The release build produces a self-contained application in `bin\Release` and
an x64 MSI at `SuperPuttyInstaller\bin\Release\SuperPuttySetup.msi`. The MSI
includes the .NET runtime, so end users do not need to install .NET separately.

Tests whose category contains `Net` require a configured PuTTY `pscp.exe` and
a disposable SSH endpoint; they are excluded from the default CI test run.

## Project resources

- [Official SuperPuTTY repository](https://github.com/jimradford/superputty)
- [Official releases](https://github.com/jimradford/superputty/releases)
- [Official documentation](https://github.com/jimradford/superputty/wiki/Documentation)
- [Candidate fixes in this fork](https://github.com/greyhair-atx/superputty/branches)
- [Unsigned test builds from this fork](https://github.com/greyhair-atx/superputty/releases)

Please use the upstream repository for official downloads, documentation, and
general issue reporting. Fork-specific test builds are provided only to help
validate proposed fixes; review their release notes before installing them.

## License

SuperPuTTY is licensed under the MIT License. See `License.txt` for details.

## Upstream status

[![GitHub release](https://img.shields.io/github/v/release/jimradford/superputty)](https://github.com/jimradford/superputty/releases/latest)
[![Latest release downloads](https://img.shields.io/github/downloads/jimradford/superputty/latest/total?label=latest%20release%20downloads)](https://github.com/jimradford/superputty/releases/latest)
[![All release downloads](https://img.shields.io/github/downloads/jimradford/superputty/total?label=total%20downloads)](https://github.com/jimradford/superputty/releases)
[![Build status](https://ci.appveyor.com/api/projects/status/s6thtyntec4beaqk/branch/master?svg=true)](https://ci.appveyor.com/project/jimradford/superputty/branch/master)
