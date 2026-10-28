# Third-party notices

Libraries that ship with Textida: for remote files (`NksHub.Textida.Remote`) and for the XXH3 checksums. Licence identifiers are the SPDX expressions in each package.

| Package | Version | Licence | Used for |
|---|---|---|---|
| [SSH.NET](https://github.com/sshnet/SSH.NET) | 2026.0.0 | MIT, Copyright (c) Renci 2010-2026 | SFTP client |
| [BouncyCastle.Cryptography](https://www.bouncycastle.org/) | 2.7.0 | MIT, Copyright (c) Legion of the Bouncy Castle Inc. 2000-2026 | Cryptography used by SSH.NET |
| [Microsoft.Extensions.Logging.Abstractions](https://github.com/dotnet/runtime) | 8.0.3 | MIT, Copyright (c) Microsoft Corporation | Logging interfaces used by SSH.NET |
| [Microsoft.Extensions.DependencyInjection.Abstractions](https://github.com/dotnet/runtime) | 8.0.2 | MIT, Copyright (c) Microsoft Corporation | Dependency of the logging interfaces |
| [FluentFTP](https://github.com/robinrodricks/FluentFTP) | 55.1.0 | MIT, Copyright (c) 2015 Robin Rodricks and FluentFTP Contributors | FTPS client |
| [System.IO.Hashing](https://github.com/dotnet/runtime) | 10.0.0 | MIT, Copyright (c) .NET Foundation and Contributors | XXH3 checksums |

Test-only (not shipped): FubarDev.FtpServer 3.1.2 (MIT), the in-process FTPS server of the remote tests; paramiko (LGPL-2.1), started as a separate process by the SFTP tests and never linked or distributed.

All MIT licences require the copyright notice and the permission notice to travel with copies of the software; the full texts are in the NuGet packages of the versions above.

## Structure templates

The `.ksy` files in `src/NksHub.Textida.Core/Hex/Ksy/Templates` (BMP, PNG, ZIP, PE, ELF, binary STL) were written for Textida from the public descriptions of the formats: the BMP file and info headers, the PNG specification, the PKWARE APPNOTE, Microsoft's PE format description, the System V gABI ELF chapter and the binary STL layout. They are MIT, like the rest of this repository, and `pinned.yaml` in that folder lists each file with its version, licence, source and the constructs it uses. The file syntax comes from the [Kaitai Struct](https://kaitai.io/) project; Textida has its own reader for a subset of it and ships neither the Kaitai compiler or runtime (GPL-3 and MIT) nor any template of the Kaitai format gallery.
