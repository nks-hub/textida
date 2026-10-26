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
The command starts your own comparison program: the file on disk on the left, the content of the tab on the right.
1. Once, fill in External diff tool (the path to a program) in the settings, and if needed Diff tool arguments. In the template {left} is the file on disk and {right} is the content of the tab.
2. Choose File and then Compare with Disk.
- Without a program set, a bar appears: No external diff tool is set, with the button Open Settings.
- A tab without a file on disk says: The tab has no file on disk to compare with.
- If the program does not start, the status bar says The diff tool did not start and gives the reason.
- The program has no comparison window of its own; it uses the one you set.

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
commands: nks.edit.selectAll, nks.edit.selectLine
1. Ctrl+A selects all text.
2. Ctrl+L selects the line including its line ending. Pressing Ctrl+L again adds the next line.
- With several carets each caret takes its own line.

## [edit.lines] Working with lines
commands: nks.edit.deleteLine, nks.edit.duplicateLine, nks.edit.moveLineUp, nks.edit.moveLineDown, nks.edit.joinLines
The commands act on the line with the caret, or on all lines of the selection. Each is one Undo step.
- Delete Line: Ctrl+Shift+K.
- Duplicate Line: Ctrl+Shift+D inserts a copy below the line.
- Move Line Up or Down: Alt+Up arrow, Alt+Down arrow.
- Join Lines: Ctrl+Shift+J joins the line with the next one.
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
- If a pattern runs too long, it is stopped and the window says so. Only a limited number of matches is listed.
- F1 in this window opens this topic.

## [search.gotoLine] Go to line
commands: nks.search.gotoLine
1. Press Ctrl+G, or choose Search and then Go to Line.
2. Type a line number (120), line and column (120:5), a percentage (40 %) or a byte offset (@4096).
3. Confirm with Enter. The caret jumps there.
- A number above the line count goes to the last line.
- An invalid entry says “...” is not a line number.
- In a large file whose lines are still being numbered the status bar says: The line index does not reach that position yet; try again in a moment.

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
- Below the list there is a preview of the highlighted file; a binary file is not shown.

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
A file larger than the large file threshold (setting Large file threshold, 6 MiB by default) opens in paged mode. The program reads only the lines on the screen, so the file opens quickly and does not fill the memory.
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
- The pattern is too slow; the search stopped it: simplify the pattern, especially nested repetitions.
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
- Hexadecimal view and editing: not in this version. A file that is not text is opened as text.
- A comparison window of its own: an external diff tool is used (topic Compare with disk).
- Editing very large files: only limited, see the topic Large files.
- Plugins, remote files and other functions planned for version 1.0: not in this version.
