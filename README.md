# Textida

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2B%20%7C%20macOS%2014%2B-lightgrey)](#requirements)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

> Text editor for Windows and macOS with a paged document core that opens and edits files larger than RAM. Built with C# / .NET 10 and Avalonia 12. Early development, no release yet.

Textida is a working name. The editor is a new program inspired by PSPad; it does not use PSPad code, data or resources.

## Features

What exists in the repository today:

- 📄 **Paged document core** - `NksPad.Core` maps the file into memory and keeps edits in a piece tree with undo. On the reference Windows VM (4 cores, 16 GiB RAM, virtual disk) the first screen of a 50 GiB file renders in 1.8 ms warm and 19.5 ms cold, with about 110 MiB private memory.
- ✏️ **Editing large files** - line counts stay correct after edits without a rescan. A 100 MiB file takes its first edit in under 40 ms (p95, warm) and adds at most 1.2 MiB of memory; inserting a character costs about 1 µs.
- 💾 **Byte-exact saving** - untouched ranges are written back byte for byte; saved files are checked against SHA-256 of an independently built expected file.
- 🔍 **Search** - literal and regex search over the paged document with cancellation. On 4 cores it takes 0.01-0.6 of the time of `rg --json` on the same data, with identical match counts.
- 🔤 **Encoding detection** - BOM, UTF-8 validation (parallel, 15 GB/s on 4 cores of the reference VM), UTF-16 without BOM and a single-byte fallback to CP1250.
- 📊 **Benchmarks** - `bench/NksPad.Bench` generates its own corpus and reproduces every number above.

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
dotnet run -c Release --project bench/NksPad.Bench -- corpus --corpus ./bench-data/corpus --results ./bench-data/results
dotnet run -c Release --project bench/NksPad.Bench -- a3 text-1GiB.log --corpus ./bench-data/corpus --results ./bench-data/results
```

The corpus with the 10 GiB and 50 GiB files needs about 130 GB of free disk space. `bench/run-mac.sh` runs the macOS size profile.

## Development

```bash
dotnet build NksPad.sln
dotnet test tests/NksPad.Core.Tests
```

Namespaces keep the `NksPad` prefix until the final product name is confirmed. The task list for all stages is in [TODO.md](TODO.md) (Czech).

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
