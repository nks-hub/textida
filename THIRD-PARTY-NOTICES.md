# Third-party notices

Libraries that ship with Textida: for remote files (`NksHub.Textida.Remote`), for the XXH3 checksums, the .NET assembly viewer and Markdown parsing. Licence identifiers are the SPDX expressions in each package.

| Package | Version | Licence | Used for |
|---|---|---|---|
| [Markdig](https://github.com/xoofx/markdig) | 1.4.0 | BSD-2-Clause, Copyright (c) 2016-2026 Alexandre Mutel | Markdown syntax tree |
| [SSH.NET](https://github.com/sshnet/SSH.NET) | 2026.0.0 | MIT, Copyright (c) Renci 2010-2026 | SFTP client |
| [BouncyCastle.Cryptography](https://www.bouncycastle.org/) | 2.7.0 | MIT, Copyright (c) Legion of the Bouncy Castle Inc. 2000-2026 | Cryptography used by SSH.NET |
| [Microsoft.Extensions.Logging.Abstractions](https://github.com/dotnet/runtime) | 8.0.3 | MIT, Copyright (c) Microsoft Corporation | Logging interfaces used by SSH.NET |
| [Microsoft.Extensions.DependencyInjection.Abstractions](https://github.com/dotnet/runtime) | 8.0.2 | MIT, Copyright (c) Microsoft Corporation | Dependency of the logging interfaces |
| [FluentFTP](https://github.com/robinrodricks/FluentFTP) | 55.1.0 | MIT, Copyright (c) 2015 Robin Rodricks and FluentFTP Contributors | FTPS client |
| [System.IO.Hashing](https://github.com/dotnet/runtime) | 10.0.0 | MIT, Copyright (c) .NET Foundation and Contributors | XXH3 checksums |

| [ICSharpCode.Decompiler](https://github.com/icsharpcode/ILSpy) | 11.2.0.10050 | MIT, Copyright 2011-2026 AlphaSierraPapa | .NET assembly viewer (decompiled C#) |
| [WeCantSpell.Hunspell](https://github.com/aarondandy/WeCantSpell.Hunspell) | 7.0.1 | MIT, Copyright Aaron Dandy | Spelling check (Hunspell dictionaries the user chooses or downloads) |
| [Jint](https://github.com/sebastienros/jint) | 4.17.0 | BSD-2-Clause, Copyright Sebastien Ros | Scripts in the editor (prototype, off by default) |
| [Acornima](https://github.com/adams85/acornima) | 1.7.0 | BSD-3-Clause | JavaScript parser used by Jint |
| [Microsoft.ML.Tokenizers](https://github.com/dotnet/machinelearning) | 2.0.1 | MIT, Copyright (c) .NET Foundation and Contributors | Exact token count of the OpenAI preset |
| [Microsoft.ML.Tokenizers.Data.O200kBase](https://github.com/dotnet/machinelearning) | 2.0.1 | MIT, Copyright (c) .NET Foundation and Contributors | The o200k_base vocabulary of OpenAI tiktoken (MIT, Copyright (c) 2022 OpenAI, Shantanu Jain) |
| [Google.Protobuf](https://github.com/protocolbuffers/protobuf) | 3.30.2 | BSD-3-Clause, Copyright 2008 Google Inc. | Dependency of Microsoft.ML.Tokenizers |

Test-only (not shipped): FubarDev.FtpServer 3.1.2 (MIT), the in-process FTPS server of the remote tests; paramiko (LGPL-2.1), started as a separate process by the SFTP tests and never linked or distributed.

All MIT licences require the copyright notice and the permission notice to travel with copies of the software; the full texts are in the NuGet packages of the versions above.

## Plugin validator

`textida-plugin-validate` (`tools/NksHub.Textida.Plugin.Validate`) is a separate dotnet tool for plugin authors and is not part of the editor. It carries [Microsoft.CodeAnalysis.CSharp](https://github.com/dotnet/roslyn) 4.14.0 (MIT, Copyright (c) .NET Foundation and Contributors), which runs the analyzer that bans reflection over the C# sources of a plugin.

## Structure templates

The `.ksy` files in `src/NksHub.Textida.Core/Hex/Ksy/Templates` (BMP, PNG, ZIP, PE, ELF, binary STL) were written for Textida from the public descriptions of the formats: the BMP file and info headers, the PNG specification, the PKWARE APPNOTE, Microsoft's PE format description, the System V gABI ELF chapter and the binary STL layout. They are MIT, like the rest of this repository, and `pinned.yaml` in that folder lists each file with its version, licence, source and the constructs it uses. The file syntax comes from the [Kaitai Struct](https://kaitai.io/) project; Textida has its own reader for a subset of it and ships neither the Kaitai compiler or runtime (GPL-3 and MIT) nor any template of the Kaitai format gallery.

## Command icons

The icons of the commands (menus, command palette, toolbar) are the SVG files in `src/NksHub.Textida.Workbench/Icons/svg`. They are Textida's own drawings, made for this program on a 16-unit grid as outlines with one marked colour; no shape was copied or traced from an icon library, font or other program, so no third-party licence applies to them. They are MIT, like the rest of this repository. The program converts the files to path data when it is built (`src/NksHub.Textida.IconGenerator`) and takes the colours from the theme, so a file never carries a colour of its own. An icon a plugin brings is the plugin author's work and licence.

## Markdig

Copyright (c) 2016-2026, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without modification
, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
