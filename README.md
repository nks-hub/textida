# Textida

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2B%20%7C%20macOS%2014%2B-lightgrey)](#requirements)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

> Text editor for Windows and macOS with a paged document core that opens and edits files larger than RAM. Built with C# / .NET 10 and Avalonia 12. Early development, no release yet.

Textida is a working name. The editor is a new program inspired by PSPad; it does not use PSPad code, data or resources.

## Features

What exists in the repository today:

- 📄 **Paged document core** - `NksHub.Textida.Core` maps the file into memory and keeps edits in a piece tree with undo. On the reference Windows VM (4 cores, 16 GiB RAM, virtual disk) the first screen of a 50 GiB file renders in 1.8 ms warm and 19.5 ms cold, with about 110 MiB private memory.
- ✏️ **Editing large files** - line counts stay correct after edits without a rescan. A 100 MiB file takes its first edit in under 40 ms (p95, warm) and adds at most 1.2 MiB of memory; inserting a character costs about 1 µs.
- 💾 **Byte-exact saving** - untouched ranges are written back byte for byte; saved files are checked against SHA-256 of an independently built expected file.
- 🔍 **Search** - literal and regex search over the paged document with cancellation. On 4 cores it takes 0.01-0.6 of the time of `rg --json` on the same data, with identical match counts.
- 🔤 **Encoding detection** - BOM, UTF-8 validation (parallel, 15 GB/s on 4 cores of the reference VM), UTF-16 without BOM and a single-byte fallback to CP1250.
- 📊 **Benchmarks** - `bench/NksHub.Textida.Bench` generates its own corpus and reproduces every number above.

The application itself (window, tabs, menus) is being built in stage P1.

## Requirements

- .NET SDK 10.0.402 (pinned in `global.json`)
- Windows 10 22H2 or newer, or macOS 14 or newer (target systems of the editor; the SDK runs on its own supported systems)
- [ripgrep](https://github.com/BurntSushi/ripgrep) only for the search comparison benchmark

## Installation

There is no binary release yet. Build from source:

```bash
git clone https://github.com/nks-hub/textida.git
cd textida
dotnet build
dotnet test
```

## Usage

Generate the benchmark corpus and run a scenario:

```bash
dotnet run -c Release --project bench/NksHub.Textida.Bench -- corpus --corpus ./bench-data/corpus --results ./bench-data/results
dotnet run -c Release --project bench/NksHub.Textida.Bench -- a3 text-1GiB.log --corpus ./bench-data/corpus --results ./bench-data/results
```

The corpus with the 10 GiB and 50 GiB files needs about 130 GB of free disk space. `bench/run-mac.sh` runs the macOS size profile.

## Development

```bash
dotnet build NksHub.Textida.sln
dotnet test NksHub.Textida.sln --filter "Category!=Perf"
dotnet test tests/NksHub.Textida.App.Tests --filter "Category=Perf"
```

Tests marked `Perf` measure latency against a limit (typing p95 33 ms), so they run alone, not next to the other test assemblies. `Large` tests use a sparse 4.5 GiB file and take about a minute.

A NativeAOT distribution comes from one command; it holds the editor, the built-in plugins in `plugins/<name>/` and `textida-mcp` in `mcp/`, without symbols (`-p:PublishSymbols=true` keeps the `.pdb` files). The AOT check verifies the layout and starts every program:

```bash
dotnet publish src/NksHub.Textida.App -c Release -r win-x64 -o ./dist
dotnet publish tools/NksHub.Textida.AotCheck -c Release -r win-x64 -o ./aotcheck
./aotcheck/NksHub.Textida.AotCheck --publish ./dist
```

The Windows package is one ZIP built from that output. It is the portable form (unpack, start `Textida.exe`) and the per-user installer: `Install.cmd` runs `Setup.ps1`, which copies the files to `%LOCALAPPDATA%\Programs\Textida`, adds a Start menu shortcut and an Apps & features entry, needs no administrator rights and does not touch PSPad. `Setup.ps1 -Uninstall` removes all of it and keeps the settings in `%APPDATA%\Textida`. The build runs the AOT check on the publish output and on the ZIP, and prints the SHA-256 of the ZIP:

```bash
dotnet run tools/BuildWindowsPackage.cs -- --version <version> --output ./artifacts
```

The Explorer menu entry "Open with Textida" and the optional file associations are written by the program itself under `HKCU\Software\Classes` (Tools menu: Add Textida to the Explorer Menu, File Associations, Remove Textida from Explorer). The uninstaller removes them through `Textida.exe --unregister-shell`. There is no macOS package yet.

Projects and namespaces use the `NksHub.Textida` prefix. The task list for all stages is in [TODO.md](TODO.md) (Czech).

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-change`)
3. Commit your changes (`git commit -m "feat: add my change"`)
4. Push to the branch (`git push origin feature/my-change`)
5. Open a Pull Request

## Support

- 📧 **Email:** dev@nks-hub.cz
- 🐛 **Bug reports:** [GitHub Issues](https://github.com/nks-hub/textida/issues)

## License

MIT License — see [LICENSE](LICENSE) for details.

---

<p align="center">Made with ❤️ by <a href="https://github.com/nks-hub">NKS Hub</a></p>
