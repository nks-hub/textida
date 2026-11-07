# Changelog

Versions 0.1.0 to 0.1.4 were published as previews; 0.1.5 is the first regular release. Packages are signed ad hoc on macOS and carry no Authenticode signature on Windows.

## [0.1.7] - 2026-11-07

### Added

- The Markdown reader column follows the window width; `markdown.readerMaxWidth` sets a fixed width. Tables and code blocks use the full width and a wide table scrolls sideways instead of wrapping its cells word by word.
- Screen readers get the whole text of the Markdown reader, with words, selection and caret, through the same layer as the editor (UI Automation on Windows, NSAccessibility on macOS).
- Shortcut capture in the settings records two-step shortcuts such as Ctrl+K Ctrl+U and reports conflicts with a shortcut's first step.
- On Linux the System page of the settings turns the "Open in Textida as project" folder action on and off.
- The formatted JSON view wraps and edits keys longer than 4 KiB and shows JSONC comments placed between a key and its value.
- The hex minimap uses the colours of the editor theme.

### Changed

- The menus are shorter: 347 items instead of 468, at most 15 items per menu and two submenu levels. Rare and expert commands are only in the command palette, which still lists every command. Hex commands have their own menu that appears in hex mode.
- The settings window has 8 top-level pages instead of 11, expert settings are on the Advanced page and descriptions are one sentence.
- Text-only commands are disabled in the formatted JSON view; Cut and Paste still work inside a value.
- An edit during the background scan of a large JSON file no longer restarts the scan.

### Fixed

- A Markdown paragraph split at a tall picture no longer gets an empty line where the line wraps.
- The notice after importing a theme stays until the settings page is left or the notice is closed.
- Linux desktop entries quote the program path, so a home folder with a space works.
- The path of a JSON line that opens an object or array no longer repeats its key.

## [0.1.6] - 2026-11-07

### Fixed

- PSPad project files with backslash paths open on macOS and Linux.
- Git file history works in a repository reached through a symbolic link, such as `/tmp` on macOS.
- Plug-in bundles with backslashes in entry names unpack flat on macOS and Linux too.
- On macOS a write into an existing file is no longer reported as a new file in the file panel and the agent review.
- The status bar no longer rebuilds the accessible names of its items on every keystroke.
- Open as New Copy reads a file without a byte order mark in its guessed encoding instead of UTF-8.
- Mermaid subgraph frames no longer cover nodes of another subgraph, and an edge to a subgraph id ends on its frame.
- After Re-read As in another character width, the line endings shown come from the new reading at once.

### Added

- Insert Image reads the size of SVG, ICO, AVIF and HEIF files.
- The commit panel and the local history are tool windows that dock left, right or at the bottom like the document, file and plug-in panels. Each edge has a strip of buttons that shows and hides the panels docked there, Alt+0 and Alt+9 show the commit panel and the history, and the layout is kept in `window.json`.
- The commit panel lists new files apart from the changes, has a box for every change of the chosen file, and can save the open files and format the files of the commit before it commits.
- New Markdown reader. It lays out only the visible blocks, so a document of any length opens in Reading mode, and it uses the typography of GitHub (16 px base, line height 1.5, heading sizes and spacing from github-markdown-css 5.8.1) in the light and dark themes. Selection runs across paragraphs, lists, code and table cells; Ctrl+C copies plain text and Copy as Markdown copies the source.
- Headings panel for Markdown (Alt+8): a dockable tree of H1–H6 that stays open in every mode, marks the current heading, filters and collapses by level.
- Formatted view of JSON (Ctrl+K Ctrl+J) that reads the file in blocks in the background, so the first screen of a 1 GiB single-line file reads a few hundred KiB. It folds, jumps to a key, selects and copies, shows JSONC comments, wraps long values and changes only the bytes of the edited value.
- Editor colour themes: Light, Dark, Monokai, Solarized Light and Dark, Dracula, One Dark, GitHub Light and Dark and Nord, colour and bold/italic per token, custom themes, import of `.tmTheme` and VS Code themes and export as VS Code JSON. The hex view, the CSV table and comparisons use the theme too.
- Git pull fetches first and says "Up to date" when there is nothing to pull. The merge mode comes from `branch.<name>.rebase`, `pull.rebase` or `pull.ff`, otherwise Textida asks once and can remember the answer. The status bar shows incoming and outgoing commits, and a background fetch (every 10 minutes by default) offers an update for new commits.
- File, Open Recent with the last 15 files, keys 1–9 and 0, a mark on missing files and Clear List.
- The status bar shows the zoom in percent; clicking it offers zoom levels and 100 %.
- Open in Textida as Project in the folder menu of Explorer (folder and folder background), as a Finder quick action and as a Linux desktop action. `--project <folder>` opens a folder as the project root.
- File Language picker in a searchable palette with recent languages, the detected language and file extensions.
- Toolbar context menu (Customize, Hide, Remove This Button, Reset, small or large icons, coloured icons) and coloured toolbar icons by kind of command.
- About lists NKS Hub, the project links, the contact address and the licence.

### Changed

- The settings window shows each setting on a card with its description, default value and reset, with a live preview on the Appearance and Editor pages. Every setting has its own control: switch, list, multi-select, number with range, font list with preview, colour, file or folder picker, tag list or shortcut capture. The icon set choice moved from the View menu to Appearance.
- The toolbar is on by default.
- A new comparison selects the first difference and scrolls both sides to it.
- Menus draw one narrow column for check marks and icons, and none when no item has either.
- `workbench.keymapProfile` is gone; the settings file moves its value to `editor.profile` (format version 2, a `.v1.bak` copy is kept).
- MCP tool switches cover plug-in tools and are kept in `mcp-tools.json`.
- Builds use .NET SDK 10.0.403 with runtime 10.0.14, which fixes CVE-2026-111497 in the Brotli decoder.
- The commit panel and the local history open in a tool window and no longer in a tab.
- With incremental sync, multi-caret edits of up to 2048 changed lines reach the language server as one range per run of changed lines.

## [0.1.5] - 2026-11-02

### Added

- Printing to PDF embeds a subset of the system font, so the text is Unicode, can be searched and copied, and right-to-left lines are shaped. Glyphs that stand for another character carry ActualText.
- Export with highlighting as LaTeX (File, Export). The preamble picks the engine with `iftex`: fontspec under XeTeX and LuaTeX, inputenc and T1 under pdfTeX. Characters that pdfTeX cannot set print as a bold `?` instead of stopping the build.
- The AI context copy counts tokens exactly offline with the OpenAI `o200k_base` vocabulary.
- TextMate grammar import:
  - `begin`/`while` rules;
  - colouring of `beginCaptures` and `endCaptures`;
  - possessive quantifiers;
  - `\G` and `\R`;
  - POSIX classes by the Oniguruma definitions;
  - back-references from `begin` in `end`;
  - nested and intersected character classes;
  - `\K` and a literal absence operator.
- A bracket without a partner at the caret is highlighted.
- Mermaid flowcharts draw `subgraph ... end` as a labelled frame, nested too. They also draw the stadium, hexagon, cylinder and parallelogram shapes, and edges end on the outline of each shape.
- The toolbar is a single Tab stop. Arrow keys, Home and End move between its buttons.
- Updates:
  - Textida checks for a new version a few seconds after start and then every 24 hours. Only the signed feed is fetched, and the setting `updates.checkOnStart` turns the check off.
  - A newer version shows a "New version" item in the status bar; clicking it installs the update.
  - While an update installs, a bar shows the downloaded size and percentage, with Cancel, and then the installing step.
  - After the restart, a notice says which version was updated to which.
- `SignUpdateFeed --resign` and the manual `resign-feed.yml` workflow sign the published feed again between releases.
- Release builds start the native program with `--smoke` under an OS-level socket watch (`tools/CheckNoNetwork.cs`). Any connection to a non-loopback address fails the build.

### Changed

- The signed update feed contains its issue and expiry date and is valid for 90 days. Builds from this release refuse an expired feed and a feed without a date. Releases up to 0.1.4 ignore the dates.
- Text, line numbers, diff highlights and syntax colours reach a 4.5:1 contrast in the light and dark themes as well as in high contrast. In compare views, changed blocks have a gutter bar, so they are not marked by colour alone.
- The accessibility text surface applies edits in place instead of reading the whole document after each keystroke. This covers multi-caret edits too.
- With incremental sync, the language server receives one UTF-16 range per changed block, from the last block to the first, instead of one range over the whole difference.
- A large document reports the line endings of the whole file once its line index is complete, instead of those of the first 64 KiB. A file that switches to CRLF further on now shows Mixed.
- The minimap and the change marks cover an insertion of more than 256 lines as one range, instead of stopping after the first 50,000 lines.

### Fixed

- Security:
  - Git status no longer runs programs named in a repository's configuration (fsmonitor, clean and process filters).
  - Saving keeps the file mode and creates temporary files readable by the owner only.
  - On Linux, IPC and spill files use private per-user folders.
  - `cmd.exe` starts from the system folder.
- Elevated reads and saves:
  - Elevated reads and saves on macOS and Linux go through a folder created by root.
  - On Windows, elevated results come back through a folder created by the administrator, and a source path swapped for a link is refused.
- Update download:
  - A stalled update download waited forever without any message. The download now fails after 30 s without data and resumes from the bytes it already has with a Range request.
  - Any failure during an update shows its reason and a link to the release page instead of ending silently.
- FTPS through an HTTP proxy failed on macOS and Linux: the CONNECT request ended its lines with a bare LF.
- MCP over HTTP could time out the first tool call on a busy machine, because the handshake ended before the server acknowledged `initialized`.
- Terminal: output written before the panel attached its handlers was lost, so `cmd` sometimes opened empty.
- Decompiler:
  - Closing an assembly tab stops its decompilation.
  - When the time limit runs out in the middle of a type, the types finished before it are kept.
- Agent transcripts: when the limit of pending tool calls overflowed, all of them were forgotten, and output read from a secrets file could then show unmasked.
- Toolbar: a click no longer pulls the keyboard out of the Find bar.
- The HTML preview applies `colspan` and `rowspan`.
- The Python outline lists a `def` or `class` under `if`, `try`, `with` or `for` with its real parent, and ignores definitions inside strings, comments and bracketed continuation lines.
- LSP: a change inside a `\r\n` pair gave the server a wrong range, and its copy of the document drifted from the editor.
- Plugin SDK: progress reports that nobody waited for could fill all of the editor's request slots during a long Git fetch, and the editor then refused the command's own requests. A task now keeps at most one progress report in flight.
- The word at the caret in a large file is read past 512 bytes.
- The conversion preview counts tab stops from the column where the selection starts.
- Dragging the minimap keeps syntax colouring off the UI thread while the view stays inside the coloured window.

## [0.1.4] - 2026-11-01

### Added

- Linux ARM64 (aarch64) packages next to Linux x64, each as `.deb` and `.tar.gz`.

### Changed

- Markdown preview, headings, table commands and the generated table of contents share one parser. Nested lists, numbering, code fences, escapes, combined emphasis and headings such as `C#` keep their meaning.
- Switching Markdown modes keeps the scroll position and the selection. Table formatting keeps line endings and undoes in one step.
- Preview parsing runs off the UI thread, and a result for an older revision cannot replace a newer one. When a limit on nesting, table cells or visual elements is reached, the preview names the reason and shows the source.
- The signed update feed must list packages for all five targets.

### Fixed

- Image checks refuse paths that go through links below the document folder, handle malformed BMP dimensions and bound the decoded height as well as the width.

## [0.1.3] - 2026-11-01

### Fixed

- Text statistics read at most 32 KiB per document request, which keeps the RPC buffers below the large-object threshold. On a 2 GiB file the native plugin peaked at 17.05 MiB private commit (0.1.1: 76.25 MiB) and took 47.6 s instead of 34.7 s; cancelling took 21 ms.
- The macOS startup check waits for queued focus changes and checks both the Avalonia and the native accessibility focus.

## [0.1.2] - 2026-11-01

Tagged, not released. It contains the statistics read limit that shipped in 0.1.3.

## [0.1.1] - 2026-11-01

### Added

- A signed update feed (ECDSA P-256) for all package targets.
- Atomic version switches on Windows and recovery after an interrupted switch.

### Changed

- A settings migration refuses a rollback that would lose settings, including keys it does not know.

Upgrading from 0.1.0 needs a manual installation: 0.1.0 has no feed or public key.

## [0.1.0] - 2026-10-29

First preview: a per-user installer and a portable ZIP for Windows 10 22H2 and 11 (x64), DMG packages for macOS 14 or newer (Apple silicon and Intel), and a `.deb` and `.tar.gz` preview for Linux x64.

[0.1.5]: https://github.com/nks-hub/textida/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/nks-hub/textida/compare/v0.1.3...v0.1.4
[0.1.3]: https://github.com/nks-hub/textida/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/nks-hub/textida/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/nks-hub/textida/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/nks-hub/textida/releases/tag/v0.1.0
