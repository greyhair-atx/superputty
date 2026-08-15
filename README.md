# SuperPuTTY

> **Community fork:** This repository is an unofficial fork of
> [jimradford/superputty](https://github.com/jimradford/superputty). It is used
> to develop and evaluate possible fixes before they are reviewed or merged
> upstream. Changes and test builds published here may be experimental and
> should not be treated as official SuperPuTTY releases.

SuperPuTTY is a Windows application for managing PuTTY SSH terminals. It also
supports RDP sessions and provides a simple scripting language for common
tasks.

## System requirements

The `sp-1.6.0` branch produces **64-bit Windows builds only**. The application,
test harness, portable package, and MSI installer all target x64; x86 and
AnyCPU configurations are not supported.

- 64-bit Windows 10 or Windows 11
- .NET Framework 4.8
- PuTTY and PSCP installed or supplied separately

The x64 MSI installs under the native 64-bit Program Files directory. It cannot
be installed on 32-bit Windows.

Release artifacts use an explicit `x64` suffix:

- `SuperPutty-1.6.0-standalone-net48-x64.zip` — portable .NET Framework 4.8 build
- `SuperPutty-1.6.0-win-x64.msi` — 64-bit Windows installer

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
