## [start] Getting started
commands: nks.help.contents
This help is part of the program and needs no internet connection. Open it with F1 or from the Help menu. Press F1 on a selected command in the command palette or inside an open panel (search, files, documents, settings, regular expression tester) and its topic opens directly.
### Searching the help
- The topic list is on the left. Type a word in the box above it; only topics that contain all the words you typed stay in the list. Case and accents do not matter.
- The Up and Down arrows in the box switch topics, Esc closes the help.
### Shortcuts
- The procedures use Ctrl; on macOS use ⌘ (Cmd) instead. The shortcuts shown are the defaults. Your own shortcuts are in the settings, and the PSPad behaviour profile changes a few of them (topic Behaviour profile).
- Every command can be found in the command palette: Ctrl+Shift+P, then type part of its name.
### What this help covers
- It describes only functions this version really has. What this version does not have is in the topic What this version cannot do.
- Special topics: Crash recovery, Large files (paged mode), Error messages.

## [view.palette] Command palette
commands: nks.view.commandPalette
The palette is a searchable list of all commands of the program.
1. Press Ctrl+Shift+P.
2. Type part of the command name, for example save.
3. Select a row with the arrow keys and press Enter. Esc closes the palette.
- A command shows its shortcut. A command that cannot be used right now (for example Save without an open document) is dimmed and shows the reason.
- If you start with /, the palette searches the settings and Enter opens the settings at the item found.
- If you start with =, the palette calculates: =1+2*3 shows 7 and Enter copies the result. An invalid expression, a division by zero or a number that is too large is reported next to the expression.
- F1 on the selected command opens its help topic.

## [help.contents] Help
commands: nks.help.contents
Opens this help window.
1. Press F1, or choose Help and then Help.
2. The window opens at the topic that fits where you are: with a command selected in the palette at that command's topic, in the find bar, the file panel or the document list at the topic of that panel, otherwise at the start page.
3. F1 in the settings opens the topic about the settings, F1 in the regular expression tester the topic about the tester.
- The help is one window. Another F1 only switches it to a different topic.

## [help.about] About
commands: nks.help.about
Shows the program name and the version number.
1. Choose Help and then About.
2. Close the window with the Close button or the Esc key.
- The same version number is shown at the top of every help topic.

## [file.new] New document
commands: nks.file.new
1. Press Ctrl+N, or choose File and then New.
2. A tab named Untitled 1 opens (the next ones get 2, 3 and so on) and the caret is in it.
- The encoding of a new document comes from the setting Default encoding, the line endings from the setting End of line.
- The text is written to the recovery journal at once, so it is not lost even in a crash (topic Crash recovery). The document gets its name and place when you first save it.

## [file.open] Opening a file
commands: nks.file.open
1. Press Ctrl+O, or choose File and then Open.
2. In the system window select one or more files and confirm.
3. Each file opens in its own tab. A file that is already open is just shown.
- You can also drag files into the program window.
- The encoding is detected automatically. If it is only a guess, the status bar shows the percentage of certainty; click the encoding to change it (topic Encoding).
- A file larger than the large file threshold (6 MiB by default) opens in paged mode with limited editing (topic Large files).
- A file that cannot be read does not open and the status bar gives the reason (topic Error messages).

## [file.openFolder] Opening a folder
commands: nks.file.openFolder
1. Press Ctrl+K and then Ctrl+O, or choose File and then Open Folder.
2. Select the folder in the system window.
3. The Files panel with the folder tree appears on the left. Searching files by name (Ctrl+P) and Find in Files then work over this folder.
- If the folder cannot be opened, the status bar gives the reason.

## [file.save] Saving
commands: nks.file.save, nks.file.saveAll
1. Ctrl+S saves the active tab, Ctrl+Alt+S saves all tabs with unsaved changes.
2. A document without a file asks for a name and encoding when you save, just like Save As.
3. After saving, the status bar shows Saved.
- Before overwriting a file the program makes a backup of the previous version. If the backup fails, the file stays unchanged and the status bar says so. If only the backup after writing fails, it says Saved, but the backup failed.
- If the file changed or disappeared on disk meanwhile, the program asks: Overwrite File, Compare, or Cancel. Nothing is overwritten without your consent (topic File changed outside).
- If the text contains characters the file encoding cannot hold, the program lists them and offers Replace and Save (the characters become a question mark) or Cancel.
- The settings Trim trailing whitespace, Insert final newline and Auto save affect saving.

## [file.saveAs] Save As
commands: nks.file.saveAs
1. Press Ctrl+Shift+S, or choose File and then Save As.
2. Type the path in the File box or pick it with Browse.
3. Choose an encoding in the Encoding box. An encoding that would lose some characters is marked lossy with the number of characters.
4. Confirm with Save.
- If the target file exists, the program asks whether to overwrite it.
- The tab then belongs to the new file.
- A large file in paged mode can only be saved in its own encoding.

## [file.close] Closing a tab
commands: nks.file.close
1. Press Ctrl+W, or choose File and then Close.
2. If the document has unsaved changes, the program asks: Save, Don't Save, Keep in Journal or Cancel. Keep in Journal is offered only when crash recovery works; the changes then wait in the journal and come back at the next start.
- If two views of one document are open (split view), closing one asks nothing.
- In the PSPad behaviour profile Ctrl+W toggles word wrap; close the tab from the menu or with the cross on the tab.

## [file.reload] Reload from disk
commands: nks.file.reload
1. Choose File and then Reload from Disk, or find the command in the palette.
2. If the tab has unsaved changes, the program asks; the button Discard Changes and Reload throws them away.
3. The status bar shows Reloaded from disk.
- The command works only for a tab that has a file on disk.
- The bar above the document offers the same when a file changes outside (topic File changed outside).

## [file.compare] Compare with disk
commands: nks.file.compareWithDisk
The tab and the file on disk are shown side by side in a tab of their own. The tab is on the left with the changes you have not saved, the file on disk on the right.
1. Choose File and then Compare with Disk. The tab with the comparison opens (topic Comparing texts).
2. If External diff tool is set in the settings, your own program is started instead: the file on disk on the left, the content of the tab on the right. In the argument template {left} is the file on disk and {right} is the content of the tab. The copy of the tab handed to the program is deleted when it ends.
- A tab without a file on disk says: The tab has no file on disk to compare with.
- If the program does not start, the status bar says The diff tool did not start and gives the reason.
- The program has no comparison window of its own; it uses the one you set.

## [file.history] Local history
commands: nks.file.history
The program keeps a revision of a file at every save and at every change another program makes to it. It needs no project and no Git.
1. Choose File and then Local History. The list shows the revisions of the file in the tab, the newest first, with the reason, the size and the label.
2. Choose a revision. Compare opens it in your comparison program (the one from Compare with Disk) on the left and the tab on the right. Restore File puts the whole revision into the tab. Restore Selection replaces only the selected lines with the matching lines of the revision. Label adds a name to the revision.
- A restore is an edit in the tab; undo takes it back and nothing is written until you save. The text that was in the tab is kept as a revision.
- Revisions are stored in the profile folder, so an update of the program does not remove them.
- Files that hold secret values, such as .env, are not stored unless you turn on Keep history of secrets files in the settings.
- Limits (revisions per file, days, MiB per file, MiB in total) are set in the settings; 0 means no limit. The newest revision of a file is never deleted.
- If the disk is full, the status bar says so and no revision is damaged.

## [file.historyMark] Mark a revision
commands: nks.file.historyMark
1. Choose File and then Mark Revision and type a label, for example Before refactoring.
2. The current text of the tab is stored as a revision with that label. If the newest revision already holds the same text, the label goes to that revision.
- Find the revision later in Local History by its label.

## [file.historySearch] Search in history
commands: nks.file.historySearch
1. Choose File and then Search in History and type the text.
2. The list shows the revisions of the file that contain the text, with the number of matches and the first line. Choose one to open the actions for it, for example Compare.
- Revisions of secrets files are not searched.

- If your program does not start, the status bar says The diff tool did not start and gives the reason.
## [file.openPath] Open path at caret
commands: nks.file.openPathAtCaret
1. Put the caret on a file path in the text, for example src/a.cs:12.
2. Choose File and then Open Path at Caret, or find the command in the palette. It has no shortcut.
3. The file opens; a line (and column) after a colon is used.
- A relative path is looked up in the folder of the current file and in the open folder. A path starting with ~/ points into your home folder.
- If the path points to a folder, it is shown in the Files panel.
- No path under the caret: No file path at the caret. File does not exist: the path is shown with a note that it does not exist.

## [app.settings] Settings
commands: nks.app.openSettings
1. Press Ctrl+comma, or choose Settings and then Settings.
2. Type what you look for in the box at the top, for example tab or shortcut. Both settings and keyboard shortcuts are searched.
3. Every item shows its effective value and where it comes from (default, user, workspace, EditorConfig, document).
4. Change the value right in the row; it takes effect at once and is written to the settings file. Reset to Default restores the original value.
- A nonsensical value is refused and the row says what is wrong. If the settings file cannot be written, the reason is above the list.
- Shortcut conflicts are shown on the rows concerned.
- F1 in this window opens this topic.

## [app.quit] Exit
commands: nks.app.quit
1. Press Ctrl+Q, or choose File and then Exit.
2. If crash recovery works, the program asks nothing: unsaved text stays in the journal and comes back at the next start.
3. If it does not work (the journal cannot be written), the program asks for each document with unsaved changes: Save, Don't Save, Cancel.
- The tab layout is saved on exit and restored at the next start.

## [edit.undo] Undo and Redo
commands: nks.edit.undo, nks.edit.redo
1. Ctrl+Z reverts the last step, Ctrl+Y or Ctrl+Shift+Z puts it back. The Edit menu has the same.
2. A step is a whole run of typing or one command (for example Delete Line or an encoding conversion).
3. After a step the caret returns to where the change happened.
- Two views of one document share the history.
- Undo and Redo also work in paged mode.
- The PSPad profile also has Alt+Backspace (Undo) and Shift+Alt+Backspace (Redo).

## [edit.clipboard] Cut, Copy, Paste
commands: nks.edit.cut, nks.edit.copy, nks.edit.paste
1. Select text; Ctrl+X cuts it, Ctrl+C copies it, Ctrl+V pastes at the caret or over the selection.
- Pasted text takes the line endings of the document.
- With several carets the selected texts are copied one per line, and on paste every caret gets its own piece.
- In a block selection one clipboard line goes to each row of the block.
- Pasting into a string in a programming language escapes quotes and backslashes (setting Escape on paste into a string).
- Cut and Paste do not work in a read-only document or in paged mode (topic Large files).

## [edit.select] Selecting
commands: nks.edit.selectAll, nks.edit.selectLine, nks.edit.expandSelection
1. Ctrl+A selects all text.
2. Ctrl+L selects the line including its line ending. Pressing Ctrl+L again adds the next line.
3. Shift+Alt+Right arrow expands the selection: first the word, then the content of the brackets around it, then the brackets too, the next pair out, the whole lines and finally all text.
- With several carets each caret takes its own line.

## [edit.lines] Working with lines
commands: nks.edit.deleteLine, nks.edit.duplicateLine, nks.edit.moveLineUp, nks.edit.moveLineDown, nks.edit.joinLines, nks.edit.toggleComment
The commands act on the line with the caret, or on all lines of the selection. Each is one Undo step.
- Delete Line: Ctrl+Shift+K.
- Duplicate Line: Ctrl+Shift+D inserts a copy below the line.
- Move Line Up or Down: Alt+Up arrow, Alt+Down arrow.
- Join Lines: Ctrl+Shift+J joins the line with the next one.
- Toggle Comment: Ctrl+/ comments the lines out or back in, with the comment marks of the language at each line, so a script inside HTML gets // and the HTML around it an HTML comment.
- All of them are also in the Edit menu.
- They do not work in a read-only document or in paged mode.
- In the PSPad profile Delete Line is on Ctrl+Y, Duplicate Line on Shift+Alt+Down arrow, and the Alt+arrows do not move lines.

## [edit.indent] Indentation
commands: nks.edit.indent, nks.edit.outdent, nks.edit.insertIndent, nks.edit.tab
1. Ctrl+] increases the indentation of the selected lines (or of the line with the caret), Ctrl+[ decreases it.
2. The Tab key with a selection over several lines indents all of them; without such a selection it inserts indentation at the caret. Shift+Tab decreases the indentation.
- The indentation is as many spaces or tabs as the settings Indent size and Indent with spaces say. For an opened file the program guesses the indentation (setting Detect indentation) and a file covered by .editorconfig follows it. You can change the indentation by clicking it in the status bar.
- Insert Indentation has no shortcut; it is what Tab does without a selection over several lines.
- In the PSPad profile Indent and Outdent are on Ctrl+Shift+I and Ctrl+Shift+U.

## [edit.newline] New line
commands: nks.edit.newline
1. Enter starts a new line with the indentation of the previous one, in the line ending format of the document.
2. If the caret is in a comment, the new line continues the comment (setting Continue comments).

## [edit.case] Letter case
commands: nks.edit.upperCase, nks.edit.lowerCase
1. Select text.
2. Ctrl+K then Ctrl+U turns the letters to upper case, Ctrl+K then Ctrl+L to lower case.
- Without a selection the commands are dimmed. They are also in the Edit menu.

## [edit.clipboardHistory] Clipboard history
commands: nks.edit.clipboardHistory, nks.edit.pasteKeepCaret
1. Press Ctrl+Shift+V (Edit menu, Clipboard History). The list shows the texts you copied or cut last, newest first, with a preview of the highlighted one.
2. Type to filter the list, pick a text with the arrow keys and press Enter.
- The text is pasted like Ctrl+V: with several carets it goes into every caret (one line per caret when the number of lines equals the number of carets), as one Undo step. It also works in paged mode.
- The history holds as many texts as the setting Clipboard history size says. It is kept in memory only and forgotten when the program closes. A text longer than 1,048,576 characters is not kept.
- Paste Without Moving the Caret (Edit menu) pastes the clipboard text and leaves the caret where it was, in front of the new text. In a block selection the caret returns to the top left corner.
- Pasting does not work in a read-only document; Paste Without Moving the Caret does not work in paged mode either (topic Large files).

## [edit.insertLines] Insert into lines
commands: nks.edit.insertIntoLines
The command writes text, numbers or bullets into the lines of the selection, or into all lines when there is only a caret.
1. Choose Edit, Insert into Lines. The preview shows the first lines as they will look.
2. Choose Text, Numbers or Bullets, and where to write: start of each line, end of each line, before a column, or at each caret.
3. Press OK. The result is one Undo step.
- Numbers: First number, Digits (the number is padded with zeros to that width) and Separator, which goes between the number and the text of the line (after the number at the start of a line, before it at the end). At each caret only the number is written, so three carets get 1, 2 and 3.
- Start of each line writes at the line start; Skip the indentation writes before the first visible character instead. Before column counts characters from 1, and a shorter line gets the text at its end. Skip empty lines also skips lines with only blanks, and they do not use up a number.
- The line endings of the document do not change. It does not work in a read-only document or in paged mode (topic Large files).
- Repeat Last Command runs it again with the same choices. In the PSPad profile it is Alt+I.

## [edit.sort] Sorting lines
commands: nks.edit.sortLines
1. Select the lines (or put the caret anywhere to sort all lines) and choose Edit, Sort Lines.
2. Choose ascending or descending, whether letter case matters, whether to remove duplicate lines, and what to compare by. The preview shows the first lines of the result.
3. Press OK. The result is one Undo step and the sorted lines stay selected.
- Character code orders by the code of the characters. Locale uses the rules of a language, given as a tag such as cs-CZ or a hexadecimal id such as 0405 (empty means language-neutral). Number at the start reads the number a line starts with; lines without a number come first.
- From column and To column compare only that part of every line (0 means the whole line).
- Removing duplicates compares whole lines and keeps the first one.
- Several selections are sorted each on its own. The line endings do not change. It does not work in a read-only document or in paged mode.

## [search.selectLines] Select lines by range
commands: nks.search.selectLinesByRange
1. Choose Search, Select Lines by Range.
2. Type line numbers and ranges, for example 10-20, 40-50, separated by commas, semicolons or spaces. The line under the box tells how many selections it makes.
3. Press OK. Each range becomes its own selection with its own caret, and Copy then copies the lines of all ranges.
- Ranges that overlap or touch are joined. Line numbers start at 1; lines after the end of the document are ignored.
- Repeat Last Command selects the same lines again.
- It does not work in paged mode.

## [format.block] Aligning and wrapping lines
commands: nks.edit.reformatParagraph, nks.edit.justifyLeft, nks.edit.justifyRight, nks.edit.justifyCenter, nks.edit.justifyBlock
These commands are in the Format menu. They act on the selected lines, or on all lines when there is only a caret (Reformat Paragraph: on the paragraph with the caret). Each is one Undo step.
- Reformat Paragraph wraps the text at the column of the setting Right margin column, joins the lines of a paragraph and removes extra blanks. Paragraphs are separated by blank lines, and every line of a paragraph gets the indentation of its first line. When no right margin is set, the status bar says so.
- Align Lines Left removes the blanks at the start and end of the lines.
- Align Lines Right and Center Lines align to the width of the longest line.
- Justify Lines widens the gaps between words until every line is as wide as the longest one; a line of one word stays.
- In the PSPad profile Align Lines Left and Align Lines Right are Ctrl+Alt+Left arrow and Ctrl+Alt+Right arrow.
- They do not work in a read-only document or in paged mode.

## [edit.insertSmall] Date, GUID and repeat
commands: nks.edit.insertDateTime, nks.edit.insertGuid, nks.edit.repeatLast
- Insert Date and Time writes the current date and time at every caret, in the format of the setting Date and time format (a .NET custom format such as yyyy-MM-dd HH:mm; month and day names follow the interface language). A selection is replaced. In the PSPad profile it is Alt+D.
- Insert GUID writes a new GUID at every caret, each caret its own: lower case, with hyphens, without braces.
- Repeat Last Command runs the last text-changing command again: Delete Line, Duplicate Line, Move Line, Join Lines, Indent, Outdent, letter case, Toggle Comment, Insert into Lines, Sort Lines, Reformat Paragraph and the alignment commands, Insert Date and Time, Insert GUID. A command with a dialog repeats with the choices of its last run, without asking. In the PSPad profile it is Alt+Q.
- They do not work in a read-only document or in paged mode.

## [edit.json] Format JSON
commands: nks.edit.formatJson, nks.edit.previewFormatJson
1. Select JSON text, or select nothing to format the whole document.
2. Edit menu, Format JSON, formats the selected ranges (every selection) or the whole document. All ranges are one Undo step; text outside them stays byte for byte as it was, including the line endings.
3. Preview JSON Formatting shows the change as a proposal first. Apply it from the bar above the editor, or discard it.
- A syntax error is reported with its line and column and nothing is changed. Text larger than the limit is refused with a message.
- If the document changed after the command started, the command stops and asks you to run it again.

## [tools.stats] Document statistics and hashes
commands: nks.tools.statistics, nks.tools.hash
1. Tools menu, Document Statistics, counts bytes, characters, words, lines and the longest line of the whole document.
2. Tools menu, Calculate Hash, asks for the algorithm (MD5, SHA-1, SHA-256, SHA-384 or SHA-512) and computes the hash of the raw bytes of the whole file, or of the selected part.
- Both read the document in blocks while you keep working, show progress and can be cancelled. Neither holds the whole file in memory, so they work on files of any size, also in paged and hex view.
- A cancelled statistic is shown as incomplete, with how many bytes were counted. A cancelled hash gives no result.
- The statistic and the hash are of the document as it was when the command started; if you edit meanwhile, they do not change.
- A word is a run of letters and digits. The hash has no result for several selections at once.

## [tools.calculate] Block calculation, expressions and number bases
commands: nks.tools.calculateBlock, nks.tools.evaluateExpression, nks.tools.convertBase
1. Calculate Selected Block adds up the numbers in the selection (a block selection, a column of numbers) and shows the count, sum, smallest, largest and average.
2. Evaluate Expression asks for an expression, starting with the selected text, and shows the result. It understands + - * / ^ ( ), MOD and the functions ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR and TRUNC. Numbers are written with a dot. Anything else, such as a call of a system command, is refused and nothing is run.
3. Convert Number Base asks for a whole number, decimal or with the prefix 0x, 0b or 0o, and shows it in decimal, hexadecimal, octal and binary.
- The program recognises how the numbers in the selection are written (Czech and Slovak: 1 234,50, German: 1.234,50, English: 1,234.50). When more than one way fits, such as 1,234, it takes the one of the program language. The results are shown in the format of the program language. Changing the language does not change the document.
- Text in the selection that is not a number is skipped and counted. The number conversion works for values from -9223372036854775808 to 18446744073709551615.
- Powers take a positive base only. The calculation uses double precision numbers.

## [tools.colors] Colours
commands: nks.tools.pickColor, nks.tools.colorToHex, nks.tools.colorToRgb, nks.tools.colorToHsl, nks.tools.colorToOklch
1. In CSS, HTML, XML, JavaScript, TypeScript, PHP, Razor and Latte files every colour value (`#ff8800`, `rgb()`, `hsl()`, `oklch()`) has a small swatch of its colour in front of it. The swatch takes no character of the document.
2. Click the swatch, or put the caret on the value and choose Tools, Pick Color at Caret, to open the picker: sliders for hue, saturation, lightness and opacity, and a value box that also accepts a colour in another notation. Clicking outside the picker writes the colour over the value in the notation it had, as one undo step; Esc closes the picker without a change.
3. Tools, Convert Color to HEX, RGB, HSL or oklch rewrites the colour value at the caret, or every one inside the selections, in that notation. The colour stays the same eight-bit colour; oklch is written with five decimals for lightness and chroma and two for the hue.
- A colour outside the sRGB range is cut to it. Opacity is kept, as `/ alpha` or as the last two hex digits.
- Text that only looks like a colour, such as `rgb(var(--c))`, the HTML entity `&#123;` or `#12345`, is left alone.
- A file larger than the limit of the plugin is not converted; the message says so.

## [tools.keys] Key paths in YAML and JSON
commands: nks.tools.copyKeyPath, nks.tools.showKeys, nks.tools.goToKey, nks.tools.compareKeys
1. In a YAML or JSON file the status bar shows the path of the key under the caret, for example `cs.app.title`. Items of a list are written `items[0]`, and a key that contains a dot or a bracket is written `["a.b"]`. The path is read for the revision and the caret it was asked for: if you type or move meanwhile, the answer is dropped.
2. Tools, Copy Key Path puts the path on the clipboard.
3. Tools, Go to Key (or `@` in Go to Anything) lists the keys of the file by their path; Enter selects the key as it is written. Show Key Outline lists the same keys in the plugin panel, and a click there selects the same bytes.
4. Tools, Compare Keys with Another File asks for another open YAML or JSON file and writes to the output window the keys with a value that each file lacks.
- Block style YAML is understood: nested keys, lists, quoted keys and block scalars. A value in braces or brackets counts as one value. JSON may have comments and trailing commas.
- A syntax error stops the reading at that place; the keys before it are kept and the message gives the line.

## [tools.outline] Code outline and symbols
commands: nks.tools.showOutline, nks.tools.goToSymbol
1. In HTML, PHP, CSS, C# and Markdown files, Tools, Show Code Outline lists the functions, classes, headings and rules of the file as a tree in the plugin panel. A click selects the name of the symbol.
2. Tools, Go to Symbol (or `@` in Go to Anything) lists the same symbols by name; Enter selects the same bytes as a click in the outline.
3. The outline belongs to one revision of the document. After an edit it is refreshed on its own; until the new one arrives the old one stays, marked as outdated, and a jump is carried over the edits made since, or refused when the text the symbol stood on was changed. An answer for an older revision is never shown as current.
4. If the plugin process stops, the outline is read again once it is back.
- It is read with simple rules of the language, without a language server and without a project index: HTML (headings, elements with an `id`, `main`, `nav`, `section` and similar), PHP (namespaces, classes, interfaces, traits, enums, functions, methods), CSS (rules and at-rules), C# (namespaces, types, methods, constructors, properties), Markdown (`#` headings).
- Comments and strings are not read. What the rules do not recognise is skipped.
- A file larger than the limit of the plugin, or with more symbols than the outline holds, is shown cut short and the message says so.

## [edit.carets] Multiple carets
commands: nks.edit.addNextOccurrence, nks.edit.skipOccurrence, nks.edit.selectAllOccurrences, nks.edit.addCaretAbove, nks.edit.addCaretBelow, nks.edit.clearCarets
1. Select a word; Ctrl+D adds its next occurrence to the selection. Repeat for further occurrences.
2. Ctrl+K then Ctrl+D skips the last occurrence and takes the next one.
3. Ctrl+Shift+L selects all occurrences at once.
4. Ctrl+Alt+Up arrow and Ctrl+Alt+Down arrow add a caret on the line above and below.
5. Typing changes all places at once. Esc keeps a single caret.
- The status bar shows the number of carets.
- Alt+drag with the mouse creates a caret on every line or a block selection, depending on the setting Alt+drag.

## [edit.mark] Mark and select to mark
commands: nks.edit.selectToMark
1. Find the command Set Mark / Select to Mark in the Edit menu.
2. The first use stores a mark at the caret.
3. Move the caret elsewhere and use the command again: the text from the mark to the caret is selected and the mark is gone.
- This command has the F8 key only in the PSPad behaviour profile; in the default profile F8 is block selection.

## [edit.block] Block selection
commands: nks.edit.toggleBlockSelect
1. Press F8 (in the PSPad profile Ctrl+Shift+C).
2. Extend the rectangle over lines and columns with the arrow keys, Home, or Shift with the arrows.
3. Typing or pasting changes the text on every line of the block. Pasting uses the clipboard lines one by one.
4. Press F8 again to cancel the block.
- A block can also be made by dragging the mouse with Alt, when the setting Alt+drag allows it.
- There is no block selection in paged mode.

## [search.find] Finding in the document
commands: nks.search.find, nks.search.findNext, nks.search.findPrevious, nks.search.findWord
1. Ctrl+F opens the find bar at the bottom. Type the text; the number of matches appears next to the box and the matches are marked.
2. Enter finds the next occurrence, Shift+Enter the previous one. F3 and Shift+F3 work the same way.
3. Ctrl+F3 searches for the word under the caret.
4. Esc closes the bar.
- Toggles: Aa matches case (Alt+C), .* is a regular expression (Alt+R), ab whole words (Alt+W), Selection searches only in the selected text.
- When the search reaches the end, it continues from the start and the status bar says so.
- An invalid pattern is shown next to the match count. A pattern that is too slow is stopped by the search.
- Finding works in paged mode too; replacing does not.
- When you turn on the regular expression, a Try button appears (topic Regular expression tester).

## [search.replace] Replacing
commands: nks.search.replace
1. Press Ctrl+H, or choose Search and then Replace. The bar has an extra box Replace with.
2. Type the text to find and the replacement.
3. Enter in the replacement box replaces one match, Ctrl+Alt+Enter replaces all. The buttons Replace and All do the same.
4. The status bar says how many matches were replaced, or Nothing was replaced.
- In a regular expression replacement $1, $& and the case conversion \U \L \E \u \l work.
- It does not work in a read-only document or in paged mode.

## [search.inFiles] Find and replace in files
commands: nks.search.findInFiles, nks.search.replaceInFiles
1. Ctrl+Shift+F opens the bar with the Folder box; Ctrl+Shift+H adds the replacement box.
2. Fill in the text to find and the folder (the Browse button picks it) and press Enter or Find.
3. The results open in the Search Results tab: files, lines and counts. Enter or a double click opens a match.
4. For replacing, the Preview button shows the proposed changes. Unticking excludes a file or a single change, Apply selected changes writes them.
- A search can be cancelled with the Cancel button or Esc. With too many matches it stops and says how many it showed.
- When replacing, files with a conflict stay unchanged: the document changed after the preview, another program changed the file, the file disappeared, the new text cannot be written in the file encoding, or the change no longer fits. New preview makes the preview again.
- Before writing to files the program records the plan in the journal; after a crash in the middle of a batch it offers Finish or Roll back.
- F1 in the results tab opens this topic.

## [search.regex] Regular expression tester
commands: nks.search.regexTester
1. Choose Search and then Regular Expression Tester, or press Try in the find bar when the regular expression toggle is on.
2. Type the pattern, optionally a replacement, and a sample text.
3. The window shows the matches with their groups, the text after the replacement, and an explanation of the pattern part by part.
- It uses the same engine as the search (.NET regular expressions). The explanation marks syntax of other dialects as not supported.
- A pattern with a backreference, lookaround or an atomic group runs in a slower classic engine and is stopped after the limit in the setting Regular expression timeout (2000 ms by default); other patterns run in linear time without a limit.
- If a pattern runs too long, it is stopped and the window says so. Only a limited number of matches is listed.
- F1 in this window opens this topic.

## [search.gotoLine] Go to line
commands: nks.search.gotoLine
1. Press Ctrl+G, or choose Search and then Go to Line.
2. Type a line number (120), line and column (120:5), a percentage (40 %) or a byte offset (@4096).
3. Confirm with Enter. The caret jumps there.
- A number above the line count goes to the last line.
- An invalid entry says “...” is not a line number.
- In a large file whose lines are still being numbered the status bar says: The line index does not reach that position yet; try again in a moment. A byte offset (@4096) or a percentage works at once; until the numbering catches up, the line number after such a jump is marked with ≈.

## [search.gotoBracket] Go to matching bracket
commands: nks.search.gotoBracket
1. Put the caret next to a bracket.
2. Press Ctrl+Shift+Backslash, or choose Search and then Go to Matching Bracket.
3. The caret jumps to the opposite bracket.
- If the bracket has no match, the status bar says The bracket has no match.
- The command is not available in a large file in paged mode.

## [search.gotoAnything] Go to anything
commands: nks.search.gotoAnything
1. Press Ctrl+P.
2. Type part of a file name or path. The list shows files of the open folder, open documents and recent files.
3. Enter opens the file. If you type :120, the caret jumps to line 120 of the active document.
- Without an open folder only open and recent files are listed.
- A file that no longer exists is marked missing.
- Below the list there is a preview of the highlighted file; a binary file is not shown. The values of a `.env` file are shown as `••••`.
- In a YAML or JSON file, `@` and a part of a key path list the keys of the file (topic Key paths in YAML and JSON).

## [search.jump] Edit locations and jumps
commands: nks.search.recentEdits, nks.search.jumpBack, nks.search.jumpForward
1. Ctrl+K then Ctrl+E opens a list of the places where you last edited text, newest first. A checkbox limits it to modified documents. Enter jumps to the place.
2. Alt+Left arrow returns to the place you jumped from (after Go to Line, opening a path, a related file and similar). Alt+Right arrow goes forward again.
- If there is nowhere to jump, the status bar says Nowhere to jump back to or forward to. A list without edits says No edit locations yet.
- The places move with the text and survive closing a document.

## [search.related] Related file
commands: nks.search.relatedFile
1. In a saved document press Alt+O, or choose Search and then Go to Related File.
2. If exactly one is found, it opens. If there are more, choose from the list.
- Built-in pairs: .c with .h or .hpp, .cpp with .hpp or .h, .m with .h, a file and its _test (Go, Python), Test.php, Tests.cs, .spec.ts and .test.ts, .xaml with .xaml.cs (likewise axaml, razor).
- Your own pairs go into the setting Related file rules as ending|ending, separated by semicolons.
- A document without a file says: Save the document first. No match: No related file for the name.

## [search.bookmarks] Bookmarks
commands: nks.bookmarks.toggle, nks.bookmarks.next, nks.bookmarks.previous, nks.bookmarks.clear
1. Ctrl+F2 sets or removes a bookmark on the line of the caret; a dot appears in the margin left of the line numbers.
2. F2 jumps to the next bookmark, Shift+F2 to the previous one; after the last one it continues from the first.
- Search, Clear Bookmarks removes all bookmarks of the document.
- A bookmark moves with the text when lines are added or removed above it, and it is kept in the session: it is back after a restart.

## [view.wordwrap] Word wrap
commands: nks.view.toggleWordWrap
1. Press Alt+Z, or choose View and then Toggle Word Wrap.
2. Long lines wrap at the window edge. Press again to return.
- The choice is stored in the setting Word wrap.
- The command is not available in paged mode.
- In the PSPad profile the shortcut is Ctrl+W.

## [view.zoom] Font size
commands: nks.view.zoomIn, nks.view.zoomOut, nks.view.zoomReset
1. Ctrl+= enlarges the font by one point, Ctrl+- reduces it.
2. Ctrl+0 restores the default size.
- The size ranges from 6 to 72 points and is stored in the setting Font size.

## [view.display] Hidden characters, line numbers and minimap
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
1. Choose View and then Show Hidden Characters (Ctrl+K, Ctrl+W): spaces, tabs and the line endings CR, LF and CRLF are drawn. The document is not changed.
2. Show Line Numbers (Ctrl+K, Ctrl+N), Highlight Current Line, Show Right Margin (a vertical line at column 80, set with the setting Right margin column) and Clickable Links switch the rest on and off.
3. Show Minimap (Ctrl+K, Ctrl+M) shows a reduced picture of the text at the right edge in the colors of the highlighting. Click or drag it to scroll. In a large file it shows only the lines around the screen.
- Matches of the find bar are marked in the narrow strip at the right edge of the minimap.
- Links open with Ctrl+click and are not available in paged mode.
- A line longer than 50,000 characters is never wrapped: Toggle Word Wrap then says so in the status bar. The paged mode never wraps.
- Characters the main font lacks are drawn with the fonts of the setting Fallback fonts.
- Ctrl and the mouse wheel change the font size like the zoom commands.

## [view.secrets] Hidden values in .env files
commands: nks.view.toggleSecrets
1. Files named `.env`, `.env.*` or `*.env` show every value of a variable as `••••`. View, Show Hidden Values shows them in the current tab, and using it again hides them.
- Resting the pointer on a value shows just that one.
- The text of the file is not changed: saving writes the same bytes.
- Hidden values are also hidden in the minimap, in the previews of Go to Anything and Edit Locations, and in search results. Copy puts `••••` on the clipboard in place of a hidden value, and Cut does not delete one.
- A plugin reads the character `*` in place of every hidden value. Before a built-in command runs on such a file, the program asks whether it may read the values.
- Paged mode and the hex view show the bytes as they are.

## [view.tabs] Switching tabs
commands: nks.view.nextTab, nks.view.previousTab
1. Ctrl+Tab switches to the next tab of the group, Ctrl+Shift+Tab to the previous one.
2. After the last tab it continues with the first.
- Tabs can also be dragged with the mouse to reorder them.

## [view.documents] List of open documents
commands: nks.view.toggleDocumentList
1. Ctrl+B opens or closes the Open Documents panel on the left.
2. Select a document with the arrows and press Enter to show it. Unsaved documents carry the mark ●.
3. The box at the top filters the list.
- Esc closes the panel.

## [view.files] Files panel
commands: nks.view.toggleFilePanel
1. Ctrl+Shift+E opens or closes the Files panel. Without an open folder it offers Open Folder.
2. The arrows walk through the tree, Right and Left arrow expand and collapse a folder, Enter or a double click opens a file.
3. The right button offers file operations (topic File operations). F2 renames, Delete moves to the recycle bin or trash, F5 refreshes.
- The tree follows changes on disk by itself.

## [files.create] New file and new folder
commands: nks.files.newFile, nks.files.newFolder
1. Open a folder and select in the Files panel the folder to create in.
2. With the right button choose New File in Folder or New Folder, or find the command in the palette.
3. Type the name and confirm.
- The status bar shows Created and the name.
- An invalid or existing name is refused (topic Error messages) and nothing changes.

## [files.manage] Duplicate, rename, move, delete
commands: nks.files.duplicate, nks.files.rename, nks.files.move, nks.files.delete
Select a file or folder in the Files panel. The operations are in the context menu and in the palette.
- Duplicate makes a copy next to the original.
- Rename (F2): type the new name. Open tabs and the lists of places follow it.
- Move: type the target folder.
- Move to Recycle Bin or Trash (Delete): the program asks, and the file goes to the bin, from where you can restore it. If it is open with unsaved changes, the tab stays open and keeps them.
- If an operation fails, a bar above the document gives the reason and says Nothing was changed.

## [files.refresh] Refresh and reveal a file
commands: nks.files.refresh, nks.files.reveal
- Refresh Files (F5 in the panel) reads the tree again from disk.
- Show Active File in Panel opens the panel and selects the file of the active tab. It works with an open folder and an open document.

## [format.encoding] Encoding
commands: nks.format.reread, nks.format.convert
The status bar shows the encoding; clicking it opens the same choice. If the encoding is a guess, the percentage of certainty is shown with it.
- Reopen with Encoding: the file is read again from disk in another encoding. Use it when wrong letters appear after opening.
- Convert to Encoding: the document is converted to another encoding. Which characters would be lost is shown in the list (lossy and a count) and in a window with the line, column and byte of each character. Replace and Convert replaces them with a question mark.
- Default list of encodings in the settings: UTF-8 (with and without BOM), UTF-16 LE and BE, Windows-1250, Windows-1252, ISO-8859-2.
- For a large file in paged mode both choices are not available.

## [format.eol] Line endings
commands: nks.format.eolCrLf, nks.format.eolLf, nks.format.eolCr
1. Choose Format and then Line Endings to CRLF, LF or CR, or click the line endings in the status bar (mixed ones read mixed).
2. A preview of the change appears: how many line endings will change. Apply carries it out.
- If there is nothing to change, the status bar says Nothing to change.
- Undo reverts it.
- In a read-only document and in paged mode the command is not available.

## [format.normalize] Unicode normalization
commands: nks.format.normalizeNfc, nks.format.normalizeNfd
A character with an accent can be written as one character (NFC) or as a letter plus a separate accent (NFD). They look the same, but search and comparison tell them apart.
1. Choose Format and then Normalize Unicode to NFC (or NFD).
2. A preview with the number of places appears. Apply carries it out.
- If after normalizing some character could not be written in the file encoding, the program refuses the change and says so.
- If there was nothing to change, the status bar says Nothing to change.

## [format.language] Language mode
commands: nks.format.language
1. Choose Format and then Language Mode, or click the language in the status bar.
2. Choose a language, for example Plain text.
- The language decides which settings apply only to that language (for example word wrap or indentation) and the highlighting rules.
- When a file opens, the language is chosen from its extension.
- Your own highlighters are data: contributes.grammars in nkspad-plugin.json lists grammar files (JSON, formatVersion 1, described by grammar.schema.json): id, name, extensions, fileNames, comments, keywords and rules. A rule is match (a regular expression), words, operators, include, or a region with begin and end that may embed another language.
- A highlighter can include and embed the built-in languages, for example "embed": "javascript". A file that ends with one of its extensions or has one of its fileNames opens in that language; the id of the highlighter is the language in the settings of a language.
- A highlighter with a wrong regular expression, an unknown property, an unknown include or embed, or the id of a built-in language is refused when the plugin is read. The plugin is skipped and the status bar says which file and rule.
- In a very long line a highlighter of a plugin leaves the line without colours.

## [window.new] New window
commands: nks.window.new
1. Press Ctrl+Shift+N, or choose Window and then New Window.
2. A second program window opens. Each window has its own documents and tabs.
- When a window is closed, unsaved work stays in the journal and comes back at the next start.

## [window.split] Split view
commands: nks.window.splitView, nks.window.moveToOtherGroup
1. Ctrl+\ shows a second view of the same document in the group on the right. Both views share the text and the Undo history.
2. Move Tab to Other Group moves the tab to the other group (the command is in the Window menu).
3. The boundary between groups can be dragged with the mouse.
- Tabs can also be dragged between groups.
- A group disappears when no tab is left in it.

## [settings.theme] Theme
commands: nks.settings.theme.system, nks.settings.theme.light, nks.settings.theme.dark
1. Choose Settings and one of the themes: Theme: Follow System, Theme: Light, Theme: Dark.
2. The change takes effect at once and is remembered.
- Follow System means the program follows the system setting.

## [settings.language] Interface language
commands: nks.settings.language.cs, nks.settings.language.en, nks.settings.language.sk, nks.settings.language.de
1. Choose Settings and then Čeština, English, Slovenčina or Deutsch.
2. Menus, messages and this help switch at once.
- The default is to follow the system (setting Interface language).

## [settings.profile] Behaviour profile
commands: nks.settings.profile.default, nks.settings.profile.pspad
1. Choose Settings and then Behaviour Profile: Default or Behaviour Profile: PSPad.
- The PSPad profile changes: Home and End jump between the first character and the edge of the line, Backspace in indentation deletes back to the tab stop, Alt+drag makes a block selection, paired characters are completed the PSPad way, and a few shortcuts (Delete Line Ctrl+Y, Undo Alt+Backspace, Word wrap Ctrl+W, F8 mark, Ctrl+Shift+C block selection, indentation Ctrl+Shift+I and U).
- Your own shortcuts in the settings take precedence over the profile.

## [view.output] Output window
commands: nks.view.toggleOutput
The output window is a strip below the editor that tasks and searches write to. It is hidden by default.
1. Press Ctrl+Shift+Y, or choose View and then Output.
2. The tabs Tasks and Search switch the channel. The summary of a search in files is also written to the Search channel.
3. The Clear button empties the channel, Close (or Esc) hides the window.
- The text can be selected and copied. An empty channel says Nothing has been written to this output yet.
- The output is read from files in the profile, so it stays visible after the program that wrote it crashed.

## [view.fullscreen] Full screen
commands: nks.view.fullScreen
1. Press F11, or choose View and then Full Screen.
2. The window frame disappears; the menu, tabs, editor and status bar stay. Typing still goes to the editor.
3. Another F11 restores the previous state, a maximized window too.
- On macOS the system uses F11; use the View menu.

## [hex.open] Hex: opening and moving
commands: nks.view.hex, nks.file.openAsHex
Hex shows a file byte by byte: offset, bytes in hexadecimal and characters. It reads only the rows on the screen, so the file size does not matter.
1. Ctrl+K then Ctrl+H switches the active document between text and hex (View menu, Toggle Hex and Text). It is the same document with the same Undo history; the selection is converted through bytes.
2. File and then Open in Hex opens the chosen file straight in hex. From the command line the switch --hex does it, optionally with --offset.
3. A file that looks binary (a NUL byte in the first 8,000 bytes) gets a bar above the document: Open in hex, or Keep as text.
- Arrows, PageUp and PageDown, Home and End (within the row), Ctrl+Home and Ctrl+End (whole file). Shift extends the selection. Tab switches between the byte column and the character column.
- Cut, Copy and Paste work with bytes; between two hex views they transfer exactly.

## [hex.find] Hex: finding and go to offset
- Ctrl+F opens the Find bytes window. Enter bytes in hexadecimal (50 4B ?? 04, ?? is any byte), text in quotes, or a number with a type (0x1F40 i32).
- F3 and Shift+F3 find the next and previous match. The status bar says Match 1 of 4, or At least N matches when the search hit its limit. There is no replacing of bytes.
- Ctrl+G opens Go to offset. Notation: 0x1F40, +0x100 (from the caret), end-512, 50%, 4K, sel (start of the selection), val (the four-byte value under the caret).
- Not found: there is no match. Invalid pattern or Invalid expression: correct the entry. The offset lies outside the file: enter a smaller value. The file changed on disk meanwhile, the search result was dropped: repeat the search.

## [hex.edit] Hex: editing
commands: nks.hex.toggleInsert
1. The Insert key (⌥⌘I on macOS) switches between OVERWRITE and INSERT; the state is shown in the status bar.
2. In overwrite mode bytes are replaced and the file length does not change. At the end of the file the program says that overwriting does not change the length and to turn on insert mode.
3. Delete and Backspace in overwrite mode ask: Zero out (the length stays) or Delete (the file gets shorter).
4. If you paste more bytes than remain to the end of the file, the program asks: Extend the file, or Paste only what fits.
- A character in the character column that the encoding cannot write is refused: The character cannot be written in the encoding.
- Undo and Redo work. Changed bytes are shaded and underlined.
- While the file is being saved, edits wait until the save ends.

## [hex.inspector] Hex: data inspector
commands: nks.hex.inspector
1. Ctrl+K then Ctrl+I opens or closes the Data Inspector panel on the right (in hex only).
2. It shows the value from the caret as various types: numbers, date, GUID, character. The LE and BE switch chooses the byte order.
3. You overwrite a value in its row: Enter writes it, Esc cancels.
- A value at the end of the file that lacks bytes is incomplete and cannot be overwritten. An invalid entry and a value outside the range of the type are reported (for example Out of range for the type, Invalid GUID, Date and time as YYYY-MM-DD HH:MM:SS).
- A value that takes a different number of bytes than it replaces needs insert mode.

## [hex.layout] Hex: grid layout
commands: nks.hex.charset, nks.hex.layout, nks.hex.bytesPerRow8, nks.hex.bytesPerRow16, nks.hex.bytesPerRow24, nks.hex.bytesPerRow32, nks.hex.bytesPerRow48, nks.hex.bytesPerRow64, nks.hex.offsetFormat
The commands are available in hex, in the View menu, in the status bar and in the palette.
- Character column encoding: the encoding in which bytes are shown as characters.
- Layout: bytes per row (8, 16, 24, 32, 48, 64), grouping of bytes and the offset format. Each size also has its own command.
- Toggle offsets between decimal and hexadecimal.
- The next hex view remembers the choices.

## [hex.save] Hex: saving and changes before saving
commands: nks.hex.inspectChanges
1. Ctrl+S saves the hex. Saving runs in the background and the window stays usable; Esc cancels the save before the data is written (Saving cancelled, the file was left unchanged).
2. File and then Changes Before Saving shows what the save will write: the changed ranges with old and new bytes and the way of writing.
- If only byte values change, the save is done in place (Saved in place, N B changed) and the original bytes are first written to the journal so a save interrupted by a crash can be finished or rolled back.
- If the length changes, a new file is written and swapped in atomically.
- Before a smaller file is overwritten a backup is made; if it fails, the file stays unchanged.

## [hex.copyAs] Hex: copy, paste and export as
commands: nks.hex.copyAs, nks.hex.pasteAs, nks.hex.exportSelection
1. Edit and then Hex: Copy Selection As puts the selected bytes on the clipboard as text: hex string, an array for C, C#, Java, Python, Rust, Go, JavaScript or Pascal, Base64, Intel HEX, Motorola S-record or a hexdump.
2. Hex: Paste As reads the clipboard as hex text, Base64 or an array from source code and pastes the bytes like Ctrl+V does.
3. Hex: Export Selection writes the selection to a file, as a binary file or in one of the text forms. It runs in the background and Esc cancels it.
- Intel HEX, S-record and hexdump carry the offset of each byte in the file as its address; Intel HEX and S-record hold 32-bit addresses, so a selection beyond 4 GiB is refused.
- A selection whose text would not fit on the clipboard is refused with a hint to export it.
- An array pasted from source code may hold decimal, 0x hex, $ hex and 0b binary numbers; a number outside -128 to 255 stops the paste.

## [hex.fill] Hex: fill and operations on the selection
commands: nks.hex.fill, nks.hex.operate
1. Hex: Fill Selection fills the selection with a pattern of hex bytes or text, repeats the selection behind itself, writes random bytes or writes numbers with a step. Without a selection, a pattern followed by * and a count fills that many bytes at the caret (they are inserted in insert mode).
2. Hex: Operations on Selection applies XOR, AND, OR, NOT, Add, a shift or rotation by 1 to 7 bits (of each byte) or a swap of the byte order in groups of 2, 4 or 8 bytes. Operands repeat over the selection.
3. The dialog shows how many bytes will change before you confirm. Every fill or operation is one undo step and the filled bytes stay selected.
- A fill larger than 256 MiB is kept as a pattern and a length, not as bytes, so even 5 GiB costs no memory and no time; saving writes it as a new file. Undo of such a fill, and of the steps before it, lasts until the file is saved over.
- Random bytes, numbers with a step and the operations hold the selection in memory and refuse more than 2 GiB.

## [hex.checksum] Hex: checksums and hashes
commands: nks.hex.checksum
1. Hex: Checksums and Hashes calculates over the selection, or over the whole file when nothing is selected: CRC-16, CRC-32, CRC-64 in several variants, a CRC with your own polynomial, Adler-32, Sum-8/16/32, xxHash32, xxHash64, MD5, SHA-1, SHA-256, SHA-384 and SHA-512.
2. The calculation runs in the background and shows its progress in the status line; Esc cancels it.
3. The result can be copied, or compared with a pasted value; spaces, colons, 0x and case do not matter.
- Every CRC variant has a fixed polynomial and parameters (width, start value, reflected input and result, final xor), the same as in the catalogue of CRC algorithms.
- A CRC with your own polynomial takes the width in bits and, in hex, the polynomial (odd), the start value, the two reflection flags (0 or 1) and the final xor.

## [tools.run] Tools: run an external program
commands: nks.tools.run, nks.tools.stop
1. Tools, then Run Tool lists the tools written in the file tools.json in the profile folder. The last row opens that file; the first time it writes an empty one for you to fill in.
2. A tool has an id, a title, an executable and args (a list: one entry is one argument). Optional are cwd (the directory it runs in; the folder of the current file when missing), timeoutSeconds (without it there is no time limit) and logPattern (see LOG pattern). Example: {"externalTools": [{"id": "build", "title": "Build", "executable": "dotnet", "args": ["build", "${File}"], "cwd": "${FileDir}", "timeoutSeconds": 120}]}
3. In executable, args and cwd the variables ${File} (full path), ${FileDir}, ${FileName}, ${FileBase}, ${FileExt}, ${Line} and ${Column} stand for the current document; $$ is a dollar sign. An argument stays one argument whatever the value holds, spaces and quotes included: no shell is involved. An unknown variable, or ${File} for a document that has no file, stops the run before anything starts.
4. A tool whose command and directory have not run before is shown with both and starts only after you confirm. The confirmation is remembered until the definition, or the program it leads to, changes.
5. The output goes line by line to the Tasks tab of the output window, followed by the exit code and the time. Stop in that window, or Stop Running Tool, ends the program and everything it started; so does the time limit.
- Batch files (.bat, .cmd) are refused on Windows, because the command interpreter would split and expand the arguments again. Name the program the batch file runs.
- A program name without a path is looked for in PATH only, never in the working directory.
- Output is read as UTF-8. One tool runs at a time.

## [tools.logPattern] LOG pattern: click an error in the output
commands: nks.tools.logPattern
1. LOG Pattern for the Output opens a tester with the latest output of the tools as the sample. Each line of the sample shows the file, line and column the pattern found in it, or that it found nothing.
2. The mask is matched from the left and without regard to case. * skips any text up to the next literal part of the mask. %F is the file name, up to the next literal part. %D is a drive (C: followed by a slash) in front of it. %L is the line and %C the column (digits). %S skips spaces and tabs. %E ends the mask. A mask needs %L; without %F it points to the file that was open. Examples: %F:%L:%C:* for main.c:12:5: error, *] %F(%L) for [Error] a.pas(346): text.
3. The other language is a regular expression with the groups line (needed), file and column.
4. Use for the output makes the pattern work for the lines in the output window: a click on a line, or Enter, opens the file at that line. A relative file name is looked for in the directory the tool ran in, then in the folder of the file that was open when it started, then among the open documents by name.
- A tool can bring its own pattern: "logPatterns": [{"id": "gcc", "title": "gcc", "syntax": "mask", "pattern": "%F:%L:%C:*"}] in tools.json and "logPattern": "gcc" in the tool. Without "syntax" the pattern is a regular expression.

## [log.mode] Log mode: levels and the next error
commands: nks.log.nextError, nks.log.previousError, nks.log.levels
1. A document in the language Log (files ending in .log, or Format, Language) colors each line by its level: error, warning, info or debug. The most severe level found in a line decides.
2. Log: Next Error and Log: Previous Error jump from the caret to the next or previous line of level error. The file is read in blocks and the search stops at the first hit, so it works in a file of any size; the status line shows the progress and Esc cancels.
3. Log: Levels chooses the rules for the document. The default rules look for the upper-case words ERROR, ERR, FATAL, CRITICAL, SEVERE, WARN, WARNING, INFO, DEBUG and TRACE as whole words. Your own rules come from tools.json: "logPatterns": [{"id": "mine", "title": "My log", "levels": {"error": "\\|E\\|", "warning": "\\|W\\|"}}]. The keys are error, warning, info and debug; each value is a regular expression. The command also turns log mode on for the document.
- The rules are kept while the document is open and are not saved.
- A line longer than 8192 characters is judged by its first 8192 characters in the editor.

## [view.options] Editor display
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
The commands are in the View menu and in the palette. Each toggles one setting; the settings can also be changed in the settings.
- Show Hidden Characters (Ctrl+K then Ctrl+W): draws spaces and tabs. It matches the setting Show whitespace.
- Show Line Numbers (Ctrl+K then Ctrl+N): a column with line numbers on the left (setting Line numbers).
- Highlight Current Line: shades the line with the caret (setting Highlight current line).
- Show Right Margin: a vertical line after the column from the setting Right margin column; the value 0 turns the line off.
- Clickable Links: a web address opens on a click with Ctrl held (setting Clickable links). The command is not available in a large file in paged mode.
- Show Minimap (Ctrl+K then Ctrl+M): a reduced picture of the text on the right, dragging scrolls it (setting Minimap).
- The commands are dimmed without an open document.

## [bookmarks] Bookmarks
commands: nks.bookmarks.toggle, nks.bookmarks.next, nks.bookmarks.previous, nks.bookmarks.clear
A bookmark marks a line you want to return to.
1. Ctrl+F2 sets a bookmark on the line with the caret, and removes it on a line that already has one. The status bar says Bookmark set on line N, or Bookmark on line N removed.
2. F2 jumps to the next bookmark, Shift+F2 to the previous one. After the last it continues with the first.
3. Clear Bookmarks (in the palette) removes all bookmarks of the document; the status bar says Bookmarks removed.
- If the document has no bookmarks, the status bar says The document has no bookmarks.
- There are no bookmarks in hex or in a search results tab. In a large file whose lines are still being numbered the command says the line index does not reach that place yet.

## [recovery] Crash recovery
The program continuously writes unsaved text to a journal, within two seconds of a change at the latest. An unsaved document is not lost even if the program crashes or the computer is switched off.
1. Start the program again.
2. The tabs come back including the unsaved text. A bar above the document says After an unexpected exit tabs came back, with the number of tabs with unsaved changes in brackets.
3. The Details button lists each document: restored with unsaved changes, reopened from the file, the file changed meanwhile, the file no longer exists, or could not be restored with the reason.
- If a file changed on disk meanwhile, the changes stay in the journal and nothing is overwritten.
- The journal lives in the profile folder. Another folder is set by the setting Journal folder, a fallback by Journal fallback folder.
- If the journal cannot be written to any folder, the bar says: Crash recovery is off. Unsaved text then stays only in the tabs; save by hand. If it stops working during use: The journal stopped working, crash recovery no longer applies, save by hand.
- If the fallback folder is used, the bar says which one.
- An interrupted batch of replacements in files is offered after the start: Finish or Roll back.

## [paged] Large files (paged mode)
A file larger than the large file threshold (setting Large file threshold, 6 MiB by default) opens in paged mode. To browse and edit a file byte by byte without these limits, open it in hex (topic Hex: opening and moving). The program reads only the lines on the screen, so the file opens quickly and does not fill the memory.
The status bar then shows Large file, its size and the revision.
### What works
- Moving with the arrows, PageUp, PageDown, Home, End, and Ctrl+Home and Ctrl+End. Go to a line, a percentage or a byte (Ctrl+G).
- Typing, deleting (Backspace and Delete), Enter and Tab at the caret. Undo and Redo. Save.
- Find (Ctrl+F). Find in Files.
### What does not work
- Selection, clipboard (Cut, Copy, Paste), block selection, multiple carets.
- Commands for lines, indentation, letter case, replacing.
- Go to Matching Bracket, word wrap.
- Converting and rereading the encoding, unifying line endings, normalization.
- Save As only in the same encoding.
- A line longer than 16 KiB is shown only in part and cannot be edited.
### Messages
- Line numbering is still running, editing starts in a moment: wait until the file has been read through.
- The line is longer than 16 KiB and cannot be edited in the paged mode: edit the line in another program.
- The character cannot be written in the file encoding: type another character.
- A large file in an encoding cannot be opened yet: the file does not open; the program cannot read other encodings in a large file yet.
- Saving a large file holds up the window.

## [conflict] File changed outside
If another program changes the file, a bar appears above the document: The file changed on disk.
- Reload discards your changes and loads the file from disk.
- Keep Mine keeps your changes; the next save asks before it overwrites the file.
- Compare opens the diff tool (topic Compare with disk).
If the file was deleted or renamed, the bar says it is no longer there. Keep leaves the tab, Close Tab closes it.
When you save, you get the same question in the window File Changed: Overwrite File, Compare, Cancel. Nothing is overwritten without consent.

## [statusbar] Status bar
At the bottom you see the line and column of the caret (the number of characters for a selection, the number of carets for several), the indentation, the encoding, the line endings and the language. Messages about the result of a command appear in the same place.
- Clicking the indentation offers spaces or tabs and the size.
- Clicking the encoding opens the choice of encoding (topic Encoding).
- Clicking the line endings unifies them (topic Line endings).
- Clicking the language opens the choice of language (topic Language mode).
- Highlighting is still being computed: in a long file the highlighting catches up, the text is usable. The line is too long to highlight: such a line stays without colours.
- For a file covered by .editorconfig the item .editorconfig is shown; clicking it opens the file.

## [errors.file] Error messages: files
- Could not open the file: path (reason). Check that the file exists, that you have permission and that no other program holds it.
- Saving failed: reason. The content stays in the tab. Save elsewhere (Save As) or fix the cause (disk space, permissions) and save again.
- The backup failed, the file was left as it was: fix the cause (usually disk space) and save again.
- Saved, but the backup failed: the file is fine, only the backup of the previous version is missing.
- File Changed or The file is no longer on disk: see the topic File changed outside.
- Characters the Encoding Cannot Hold: choose another encoding (for example UTF-8), or choose Replace and Save, which replaces the characters with a question mark.
- After normalising, some characters could not be written in the encoding: change the file encoding, then repeat the normalization.
- The change failed, the text changed meanwhile: repeat the command.
- Trailing blanks were not trimmed on save: the file is saved without trimming; turn the trimming off in the settings, or make the file smaller.
- A large file in an encoding cannot be opened yet: see the topic Large files.

## [errors.files] Error messages: Files panel
When an operation on a file fails, a bar above the document says The operation failed with the reason, and always Nothing was changed.
- path does not exist: refresh the tree (F5), the file has disappeared meanwhile.
- path already exists: choose another name.
- No permission for path: change the permissions in the file manager of the system.
- path is in use by another program: close that program and try again.
- Not enough space on the disk: free some space.
- name is not a valid name: remove characters the system forbids in names.
- path cannot be handled this way on this system: use the file manager of the system.
- The folder cannot be opened: choose another folder or check the permissions.

## [errors.search] Error messages: searching
- Invalid pattern: reason. Fix the regular expression or turn off .*; the Try button shows how the pattern is read.
- The pattern is too slow; the search stopped it: simplify the pattern, especially nested repetitions, or raise the setting Regular expression timeout.
- Select the text to search in first: the Selection toggle is on without a selection; select text or turn the toggle off.
- Folder not found: fix the path in the Folder box.
- No results: there is no match; check the toggles Aa, .* and ab.
- Showing the first N matches; the search stopped: narrow the search.
- Search cancelled: start it again.
- Files with a conflict were left unchanged: see the topic Find and replace in files.
- The recovery journal is not available: replacing in files runs without the certainty that it can be finished or rolled back after a crash; better start it when the journal works.
- “...” is not a line number: in the Go to Line window type a number, line:column, a percentage or @byte (topic Go to line).

## [limits] What this version cannot do
This help describes only what this version can do. These things are not in it yet:
- Program update: the program does not update itself and does not check for new versions. You install a new version yourself.
- The hex editor has no minimap, does not collect all matches into a tab and does not replace bytes; go to offset shows no preview of the target; a setting that would always open files in hex is missing.
- A comparison window of its own: an external diff tool is used (topic Compare with disk).
- Editing very large files: only limited, see the topic Large files.
- Plugins, remote files and other functions planned for version 1.0: not in this version.

## [edit.clips] Clips
commands: nks.edit.insertClip, nks.edit.clipFromSelection, nks.clips.editVariables
A clip is a piece of text with places for variables and carets. Clips come from data plugins in the folder plugins of the profile; one subfolder per plugin, with a file nkspad-plugin.json and no program.
1. Choose Edit, Insert Clip. The list shows the clips with a preview of the text that would be inserted.
2. Pick a clip. If it has a form, fill it in: the preview follows what you type, a wrong value is marked and OK stays dimmed until every value fits its type.
3. The text goes into every caret and replaces every selection, as one Undo step. The carets end where the clip has the mark $|.
- Save Selection as Clip asks for a name and adds the selected text to your own library (the plugin user.clips, folder user-clips). The text is kept literally, so a dollar sign in it stays a dollar sign. The clip is in the list at once.
- Template language: ${name} is a variable, $| marks a caret (as many as you like), $$ is a dollar sign. Line breaks become the line break of the document.
- Where a variable gets its value, strongest first: 1. the value typed in the form of the clip, 2. your own variables (Edit, Edit User Variables opens the file variables.json of the profile: one JSON object of name and text), 3. the built-in ones. The same name means the same everywhere.
- Built-in variables: file.path, file.dir, file.name, file.base, file.ext (an unsaved document has no file.*), date (yyyy-MM-dd), time (HH:mm:ss), datetime (the setting Date and time format), guid (a new one for each caret), selection (the text selected at that caret), user.name. A name with no value stops the insertion and the status bar names it.
- Form fields: name, label, type, default, choices, required. Types: text, integer, number (decimal point), color (#RGB, #RGBA, #RRGGBB, #RRGGBBAA), date (yyyy-MM-dd), file (an existing file), choice (one of choices). A default may use variables, for example ${date}.
- In nkspad-plugin.json a clip is {"id": "box", "title": "Box", "text": "<div>$|</div>", "fields": [...]} under contributes.clips; a clip with "command" also gets a command of that name.
- Problems with a plugin (a damaged file, a program in a data plugin) are shown in the status bar and the plugin is skipped.
- Clips do not work in a read-only document or in paged mode (topic Large files).

## [file.templates] New file from a template
commands: nks.file.newFromTemplate
1. Choose File, New from Template and pick a template. A template with a form asks for its values like a clip does.
2. A new tab opens with the text of the template and the carets at its marks $| (at the start when it has none).
- Templates are data too: contributes.templates in nkspad-plugin.json, with id, title, text, fields and optionally command. They use the template language and the variables of the topic Clips; a new file has no file.* variables.

## [edit.convert] Convert text
commands: nks.edit.convert
A conversion changes the text of every selection at once, or of the whole file when nothing is selected. The new text stays selected.
1. Choose Edit, Convert. The list shows the conversions with a preview on the selected text (on the first sample of the converter when nothing is selected).
2. Pick one. All selections change as one Undo step. If one of them cannot be converted, nothing is changed and the status bar says why.
- Repeat Last Command runs the conversion that ran last, without the list.
- Converters are data: contributes.converters in nkspad-plugin.json, with id, title, rules and optionally samples and command. A rule is {"find": "&", "replace": "&amp;", "regex": false, "ignoreCase": false}. Rules run in order, each on the result of the one before. With regex on, the dialect of the search applies: replace may use $1, ${name}, $& and the case codes \U \L \E \u \l.
- A sample is {"input": "...", "expected": "..."}. When the plugin is read, every sample is converted. A converter whose sample gives another text, one with no rules, with an empty find or with a wrong regular expression is refused, and the plugin is skipped with the reason in the status bar.
- A converter with "command" also gets a command of that name that converts at once, without the list.
- The special conversions of the editor itself are in the same list. This version has none yet.

## [edit.recipes] Edit recipes
commands: nks.edit.runRecipe
A recipe chooses parts of the open file, changes them and shows the result before anything is written.
1. Choose Edit, Run Edit Recipe. The list shows each recipe with the changes it would make in this file.
2. Pick one. The preview window lists every change: line, column, old text and new text. Choose Apply to write them.
3. All changes are one Undo step; Undo brings the file back exactly as it was. The changed parts stay selected. Nothing is written when the file changed meanwhile or when the recipe cannot run (a variable without a value, a pattern that takes too long).
- A recipe works on the selections, or on the whole file when nothing is selected. Repeat Last Command applies the last recipe without the preview.
- Recipes are data: contributes.recipes in nkspad-plugin.json, with id, title, steps and optionally command. Steps that choose come first. {"kind": "matches", "find": "...", "regex": false, "ignoreCase": false, "wholeWord": false} keeps the matches inside the current parts. {"kind": "columns", "from": 3, "to": 5} keeps columns 3 to 5 of every line (counted in characters from 1; to 0 or left out means up to the end of the line; a shorter line gives nothing).
- Steps that change: {"kind": "convert", "converter": "id"} (a converter of the same plugin), {"kind": "replace", "find": "...", "replacement": "...", "regex": false} and {"kind": "set", "text": "..."}, which replaces the part with a text in the template language of clips. In it ${selection} is the part itself, and the variables have the same values as in a clip (topic Clips); carets $| are ignored.
- A recipe with no steps, an unknown kind, a bad pattern, an unknown converter, a choosing step after a changing one or no changing step is refused when the plugin is read.

## [edit.aiContext] Copy as AI context
commands: nks.edit.copyAiContext
Puts the selection, the file, the open tabs or the opened folder on the clipboard as one block of Markdown or XML, to paste into an AI assistant. Nothing is sent anywhere.
1. Choose Edit, Copy as AI Context and pick what to copy: the Selection (every selection), This file, the Open tabs or the Opened folder.
2. The preview shows how many files go in, the size as Markdown and as XML, and what was left out with the reason. Choose Copy as Markdown or Copy as XML.
- Each file has its path, the lines, the language, the encoding and the revision. A file with changes that are not saved is marked, and the text is the text in the editor now.
- Left out and named: .env files (their values are never copied, also when they are shown in the editor), files and folders the ignore files exclude, hidden ones, binary files, files over the size limit of the folder walk and files that cannot be read.
- A large file in paged mode is never copied whole: only its selection is. Without a selection it is listed as left out.

## [tools.emmet] Emmet abbreviations
commands: nks.tools.emmetPreview, nks.tools.emmetWrap
1. In an HTML or CSS file, type an abbreviation and press Tab. `ul>li*3` becomes a list of three items with a caret inside each of them.
2. Tab indents as usual when no abbreviation stands in front of the caret, when text is selected, when the caret is inside an open tag, or when the abbreviation is not valid.
3. Tools, Preview Emmet Abbreviation shows what the abbreviation in front of the caret would become (`|` marks the carets) and expands it when you agree.
4. Tools, Wrap with Emmet Abbreviation (HTML only) asks for an abbreviation and wraps every selection in it. The text goes into the innermost last element; a bare `*` on that element (`ul>li*`) makes one element per line.
5. Every expansion goes into all carets at once and is one Undo step.
- HTML: `name#id.class[attr=value]{text}*3`; `>` child, `+` sibling, `^` one level up, `( )` group; in names, classes and text `$` is the number of the repeat (`$$` pads with zeros, `$@-` counts down, `$@3` starts at 3). A lone word expands only when it is an HTML tag name or a short name of the plugin (`bq`, `btn`, `a:link`, `input:checkbox`, `!` for a page skeleton). Inside an element the missing name is `li`, `tr`, `td`, `option`, `span` or `div`, as the parent needs.
- CSS: `m10-20` is `margin: 10px 20px;`, `w100p` is `width: 100%;`, `c#f00`, `pos:a`, `dn`, a trailing `!` adds `!important`, `+` joins several declarations. A whole number gets `px`, a fraction `em`; the letters `p`, `e`, `x`, `r` after a number mean `%`, `em`, `ex`, `rem`. It works only inside a `{ }` block.
- The short names are the plugin's own table of common properties and tags, not the full Emmet set.
- The carets stand in every empty element at once (in the first empty attribute when there is none); a jump from one placeholder to the next does not exist yet.
- The Emmet process starts with the first Tab in an HTML or CSS file.

## [edit.complete] Completion of words and clips
commands: nks.edit.complete
1. Press Ctrl+J (Edit, Complete Word or Clip). The list offers the words that continue the word in front of the caret, then the clips that fit the language of the document.
2. Pick an entry. A word replaces the word in front of every caret; a clip is inserted like in the topic Clips and replaces that word. It is one Undo step.
- The words are taken from all open text tabs. The word that is being typed is not offered, and a word is offered only when it is longer than what is typed. Words of the document being edited come first, then the more frequent ones.
- Clips come from the library of the topic Clips. A clip with no language fits every document; a clip with a language fits only documents of that language.
- The values of a secrets file (`.env`) are never offered while they are hidden. They are offered only in the file itself and only while shown.
- The list holds a limited number of words; type in the box to narrow it. LSP completion is not part of this version.

## [edit.sql] SQL: dialect and formatting
commands: nks.edit.formatSql, nks.edit.previewFormatSql, nks.tools.sqlDialect
1. Edit, Format SQL lays out the selections, or the whole file when nothing is selected: a clause per line, one select item per line, sub-queries indented, keywords in one letter case. It is one undo step and nothing outside the selection changes.
2. Edit, Preview SQL Formatting shows the same result in the change preview first.
3. The dialect decides the colors and which words count as keywords. The first of these that is set wins: Tools, SQL Dialect (this file only); `sql_dialect = mysql` in an `.editorconfig` (every SQL file in that folder and below); the setting Default SQL dialect. With none of them the dialect is Generic, and nothing is reported as an error.
- Dialects: generic, mysql, postgres, sqlite, tsql. The editor never connects to a database.
- Strings, quoted names and comments are copied as written. Formatting a result again changes nothing.
- A selection larger than the limit of the plugin is refused with a message.
- Indentation (4 spaces) and keyword case (upper) are fixed for now; they are not settings yet.

## [edit.html] HTML: entities and HTML Tidy
commands: nks.edit.htmlEncode, nks.edit.htmlDecode, nks.edit.htmlTidy
1. Edit, Convert to HTML Entities replaces & < > " ' in the selections (the whole file when nothing is selected) with entities. Convert from HTML Entities reads named, decimal and hexadecimal entities back. Converting an encoded text back gives the text you started from. Each is one undo step.
2. Edit, Format with HTML Tidy sends the HTML to the HTML Tidy program and shows the result in the change preview. The messages of Tidy go to the output window.
3. Tidy is a separate program. The editor looks for it at the path in the setting HTML Tidy program, in the folder `tidy` next to the formatters plugin, in `tools/tidy` of the application folder and in the folders of PATH. If it is not found, the message lists the places.
- Tidy is not part of this build: install it and set the path, or put it in one of those places.
- Line numbers in the messages count from the start of the text that was sent to Tidy.
- Letters outside ASCII are not written as entities; the encoding of the file holds them.

## [help.language] Help for a language
commands: nks.help.languageHelp
1. Put the caret on a word, for example a function name, and press Alt+F1.
2. The editor looks the word up in the help sources set for the language of the file. A file of the plugin opens in the editor. A web address is shown first and opens in your browser only after you confirm.
3. With more than one source for the language you pick one.
- Sources are data. A plugin without a program lists them under `languageHelp` in `nkspad-plugin.json`, each with `id`, `title`, `language` (the language id, for example `php`), `kind` (`url` or `file`) and `target`. In a web address `${word}` stands for the word and `${language}` for the language id. The target of a file is a path inside the plugin folder.
- Only `http` and `https` addresses are accepted; a plugin with any other address is refused when it is read.
- No sources are included yet. Without one for the language the status bar says so.

## [table.view] Table view for CSV and TSV
commands: nks.view.table, nks.table.dialect
A CSV or TSV file can be shown as a table: one row of the file per row, one field per cell. It is the same document as in the text view with the same Undo history; nothing is converted, and editing a cell changes only the bytes of that cell.
1. Ctrl+K then Ctrl+T (View menu, Toggle Table View) switches between table and text. A .tsv or .tab file is split on tabs, any other file on commas.
2. View, Table Delimiter and Header chooses comma, semicolon, tab or vertical bar as the delimiter, whether a double quote encloses values, and whether the first row is the header.
- A large file opens at once: rows are counted in the background and the scroll range grows while the file is read. Until the end is reached the status bar shows the row count with a plus, like 12+.
- Arrows, Tab, PageUp and PageDown, Home and End (within the row), Ctrl+Home and Ctrl+End move the current cell; Shift extends the selection. Click a column header to select the column and a row number to select the row.
- A quoted cell may hold line breaks; the table shows its first line and the mark ↵.
- Copy puts the selected cells on the clipboard as tab-separated text; Paste writes tab-separated text from the current cell on.

## [table.edit] Table view: editing and filtering
commands: nks.table.insertRow, nks.table.deleteRow, nks.table.insertColumn, nks.table.deleteColumn, nks.table.filter, nks.table.clearFilter
1. Enter or F2 (or just typing) edits the current cell. Enter saves and moves down, Shift+Enter inserts a line break, Esc cancels. A cell that was quoted stays quoted; another value is quoted only when it holds the delimiter, a double quote or a line break.
2. View, Insert Row adds an empty row after the current one. Insert Column asks for the name of the column and adds it after the current one. Delete Row and Delete Column remove the selected rows or columns. Each action is one Undo step.
3. View, Filter Rows shows only the rows whose current column contains the text (upper and lower case do not matter). Clear Filter shows all rows again. A filter never changes the file, and row numbers stay those of the file.
- Delete clears the selected cells. A short row gets the missing delimiters when a cell behind its end is filled in.
- When the quote character is turned off in Table Delimiter and Header, a value with the delimiter or a line break is refused.
- Rows cannot be added while a filter is on.

## [table.region] Editing selected text as a table
commands: nks.table.editRegion
Select a part of a text, for example a block of comma-separated lines in a note or a log, and choose View, Edit Selection as Table.
1. The dialog asks for the delimiter (comma, semicolon, tab or vertical bar), whether a double quote encloses values and whether the first selected row is the header. The preview shows the first rows as cells.
2. The selected text becomes the table. The rest of the file stays text: a cell edit changes the bytes of that cell only, and nothing outside the selection is read or changed.
- Everything of the table view works in it: moving, editing, adding and deleting rows and columns, filtering, copy and paste. The status bar says that only the selected text is the table.
- Rows added at the end go inside the selection. Toggle Table View returns to the text; the next Toggle Table View shows the whole file as a table.

## [compare.text] Comparing texts
commands: nks.compare.clipboard, nks.compare.file, nks.compare.texts
Two texts are compared line by line in a tab of their own. Equal lines stand level, a changed block is tinted on both sides and marked in the middle strip, and the bar above says how many differences there are.
1. Compare with Clipboard (Tools menu, or the context menu of the editor) compares the tab with the text on the clipboard. With text selected in the editor, only the selection is compared.
2. Compare with File asks for a file and compares the tab with it. Compare with Disk is in the File menu and compares the tab with its own file.
3. Compare Two Texts opens two empty sides. Paste left text and Paste right text put the clipboard into a side.
- From the command line, textida --diff left right opens the comparison of two files: the left file opens in a tab and can take blocks of the right one, which is read-only.
- The tab side is the document itself: when it changes, the comparison is made again after a short pause. The clipboard, a selection and a file on disk are read-only sides.
- A different line ending (CR, LF, CRLF) does not make a line differ. Spaces and letter case do.
- A large file in paged mode is compared without being loaded: only the lines on screen are read.
- Each side may have up to 16 million lines between the common beginning and the common end; beyond that the stretch is one changed block. Up to 10,000 changed lines are found exactly; with more, the bar says not minimal and changed lines are grouped in larger blocks. The result is still correct.

## [compare.navigate] Moving between differences and copying them
commands: nks.compare.next, nks.compare.previous, nks.compare.copyToRight, nks.compare.copyToLeft
1. F7 goes to the next difference and Shift+F7 to the previous one (also in the Tools menu). The current difference has a frame.
2. In the middle strip, the first row of each difference has two arrows. The left one copies the block of the right side to the left, the right one copies the block of the left side to the right. The buttons above the sides and the commands Copy Difference to the Right and to the Left do the same for the current difference.
3. A copy is one edit of the document, so Undo takes it back in one step.
- Only a side that is an open document can receive a block. A file on disk, the clipboard and a selection have no arrow.
- If the document changed after the comparison was made, nothing is copied; the comparison is made again and you copy once more.
- If the encoding of the target cannot hold the characters of the block, nothing is copied and the status bar says so.
- The mouse wheel moves both sides together, Shift and the wheel scroll sideways.

## [view.syncScroll] Synchronized scrolling
commands: nks.view.syncScroll
Two tabs scroll together, so equal text of two files stays side by side.
1. Show the two files next to each other: choose Move to Other Group in the menu of a tab, so each file has its own group.
2. Choose View and then Synchronize Scrolling. The active tab and the active tab of the next group follow each other. Choose the command again to stop.
- The lines are matched with the comparison of Comparing texts: the other file scrolls to the line that corresponds, also across inserted or removed blocks. While that comparison is being made, the same line number is used.
- It stops when one of the two tabs is closed. The hex view and the table view do not take part.
