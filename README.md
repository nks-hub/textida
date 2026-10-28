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

The Windows package is built from that output in two forms.

- `Textida-<version>-win-x64.zip` is the portable form (unpack, start `Textida.exe`). `Install.cmd` in it runs `Setup.ps1`, a per-user script installer without options.
- `TextidaSetup-<version>-win-x64.exe` is the installer with options: one NativeAOT file that carries the ZIP and the native libraries of its window. It has four languages (the language of Windows first, switchable), shows the MIT licence, and asks for the install folder, a Start menu shortcut, a desktop shortcut, the Explorer menu entry "Open with Textida", the file types offered under Open with, and whether to start Textida when it is done. It needs no administrator rights and does not touch PSPad.

The setup installs to `%LOCALAPPDATA%\Programs\Textida` by default and keeps a copy of itself there as the uninstaller (`TextidaSetup.exe --uninstall`, the command behind Apps & features). The uninstaller removes the files, shortcuts, menu entry, associations and the Apps & features entry, and keeps the settings in `%APPDATA%\Textida` unless "Delete my settings as well" is ticked. Running the setup over an installed Textida updates it, keeps the previous version in `Textida.previous` and starts with the options as they are now. For scripts there is a silent mode:

```bat
TextidaSetup.exe --silent --dir "D:\Apps\Textida" --no-menu --desktop --assoc .txt,.md --lang en
TextidaSetup.exe --uninstall --silent --delete-settings
TextidaSetup.exe --help
```

The exit code is 0 when it worked, 1 when it failed (the reason is printed) and 2 for a wrong command line. The build runs the AOT check on the publish output, on the ZIP and on the setup, and prints the SHA-256 of both files:

```bash
dotnet run tools/BuildWindowsPackage.cs -- --version <version> --output ./artifacts
```

The same switches are in the settings under System integration (searchable): the Explorer menu entry, the file types, the Start menu and desktop shortcuts, and a button that opens the default apps page of Windows, where the default program of a file type is chosen. They read the registry and write it at once; the Tools menu has the same commands for the menu entry and the associations. All of it is written by the program itself under `HKCU\Software\Classes`. There is no macOS package yet.

Updates are checked only when you choose Help, Check for Updates, or at start if the setting `updates.checkOnStart` is on (off by default). The check is one GET of a static feed signed with ECDSA P-256; the package must match the SHA-256 and size inside the signed part. The new version is unpacked next to the old one (`Textida.new`), and `TextidaSetup.exe --apply-update` (`Setup.ps1 -ApplyUpdate` for an install made by the script) switches the folders after Textida has closed; the old version stays as `Textida.previous` and Help, Return to the Previous Version swaps back. The feed address and the public key are still empty constants in `AppUpdater`, so a build checks nothing until they are set.

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
