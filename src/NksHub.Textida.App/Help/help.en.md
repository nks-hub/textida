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

## [remote.connections] Remote connections
commands: nks.remote.connections
A connection holds the server, the user and the protocol. The password is kept in the credential store of the operating system and is never written to a settings file or to a backup.
1. Choose File and then Remote Connections. The list shows the saved connections.
2. Choose New Connection and answer the questions: name, protocol, server, port, user and password. For SFTP you can give a private key file instead of a password.
3. Choose a connection to open a file from it, or to delete it with its saved password.
- FTPS and SFTP are the normal choices. Plain FTP sends the password and the files unencrypted and is used only after you allow it for that connection.
- The first time you connect, the program shows the identity of the server (an SSH host key or a TLS certificate) and asks you to trust it. Compare it with the one the server administrator gave you.
- If the identity of a server you trusted changes, the program does not connect until you confirm the new one. Read the warning before you do.
- Saving a password works on Windows (Credential Manager). On macOS and Linux the credential store is not supported yet, and the program says so instead of keeping the password anywhere else.

## [remote.open] Open a remote file
commands: nks.remote.open
1. Choose File and then Open Remote File, and choose a connection.
2. Type the path on the server. A path to a folder shows its content: choose a folder to go in, or a file to open it.
3. The file is copied to this computer and opens in a tab. Edit and save as usual: Save changes only the copy on this computer.
- The bar above the text says whether the copy is the same as on the server. The editor itself works without a connection.
- Opening a remote file that already has a copy shows the copy, including changes you did not send yet. The server version is never fetched over your work.
- The log of the transfers is in the Remote tab of the output window.

## [remote.upload] Upload to the server
commands: nks.remote.upload
1. Choose File and then Upload to Server. A changed tab is saved first.
2. Before it sends anything, the program looks at the file on the server. If it is still the version you opened, the new content is sent under a temporary name and then replaces the old file. A failed or interrupted upload leaves the old version on the server.
3. If the file on the server changed meanwhile, nothing is sent. Choose Show Differences to see the server version beside yours, copy what you need into your side, save, and upload again. Upload Anyway replaces exactly the server version you were shown.
- The setting Check of the server version chooses how a change is recognised: stat compares size and modification time, content always fetches the server file and compares it, and also notices a change within the same second and size.
- Limit: the check and the replacement are two steps, because FTP and SFTP have no conditional write. A change someone makes in that last moment is not noticed. The check runs after the upload is complete, so the moment is short.
- Nothing is repeated automatically. After a failure, choose Upload to Server again; it checks the server again first.

## [remote.check] Check the server version
commands: nks.remote.check
Choose File and then Check Server Version. The bar says: same as on the server, changed here and not sent, changed on the server, changed in both places, or the file no longer exists on the server. Nothing is downloaded or changed.

## [remote.unsent] Unsent remote changes
commands: nks.remote.unsent
Changes to a remote file are saved on this computer first. The list shows the remote files whose copy was changed and not sent, also after the program was closed or crashed. Choose one to open the copy; the bar offers Upload.
- Editing needs no connection. Unsent changes stay until you upload them.

## [source.open] Open from a plugin source
commands: nks.source.open
1. Choose File and then Open from Source. The list shows the sources that plugins offer and what each can do: whether it writes, whether a changed file is protected, whether it lists folders.
2. Choose a source and type the address, starting with the scheme of the source.
3. The text opens in a tab. The bar above it shows the source, the address with its secrets replaced by ***, and the revision the text was read at.
- Save writes the text back only if the file at the source is still at that revision. If it changed, nothing is written and your text stays in the tab. Choose Show Differences to compare, Overwrite to replace exactly the version that was found, or Cancel.
- A source that cannot check the revision in one step says so, and the program warns you before each save: it checks the revision first and then writes, so a change made in between is overwritten without notice.
- If the source cannot be reached, the tab opens from the copy the program keeps in memory and says Offline. Reload from source tries again and never replaces unsaved changes.
- Addresses with a password, a token or a key in them are shown with *** in the bar, the recent files and the logs. The recent files keep only the masked address, so a source that needs the secret in the address asks for it again.
- Limit: a secret inside the path of an address cannot be recognised and stays visible. Save As writes a copy to disk and ends the link to the source.

## [remote.backup] Back up connections
commands: nks.remote.exportConnections, nks.remote.importConnections
1. Choose File and then Export Connections and name the file. It lists the connections: name, protocol, server, port, user and the trusted server identity.
2. Choose File and then Import Connections to read such a file on another computer or after a reinstall.
- The file holds no passwords, passphrases or tokens, so it can be shared. After an import, choose a connection and Set Password.
- A connection that you import replaces a connection with the same internal identifier.

## [remote.proxy] Network proxy
commands: nks.remote.proxy
1. Choose File and then Network Proxy, and choose HTTP, SOCKS4, SOCKS5 or no proxy.
2. Enter the server and the port, and a user name and password if the proxy needs them. The password goes to the credential store.
- The proxy is used for FTPS and SFTP. A connection that has a proxy of its own keeps it.
- SOCKS4 works for SFTP only. For FTPS use SOCKS5 or HTTP.
- Any other proxy type is refused with a message.

## [file.locks] Locked files
A file opens read only when another program has it open and does not let others write to it, or when another window of Textida is editing it. The bar above the text names the reason.
- Try Again checks the lock again, for example after the other program closed the file.
- Edit Anyway turns on concurrent writing for that file. Every save then compares the file on disk with what you opened and asks when it changed; nothing is overwritten silently.
- The lock of a window ends with the window, also after a crash. Textida writes no lock files next to your documents and ignores .lck files of other programs.

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

## [edit.macro] Macros
commands: nks.macro.record, nks.macro.stop, nks.macro.playLast, nks.macro.save, nks.macro.run
1. Tools, Record Macro, starts recording. Do the work with commands and typing. Tools, Stop Recording Macro, ends it.
2. Tools, Play Last Macro, plays it again over the current selections and carets.
3. Tools, Save Last Macro As, stores it under a name in the folder macros of the profile. Tools, Run Saved Macro, lists the saved ones and plays the chosen one.
- A macro keeps commands, not keys: Duplicate Line, letter case, Add Next Occurrence and the like, and typed text. With several carets it does the same as doing the steps by hand.
- A playback is one Undo step. If a step cannot run, the macro stops there and the status bar names the step; what ran before stays one Undo step.
- While recording, a command that a macro cannot replay is refused and the status bar says so: everything with a dialog, file commands, Cut, Copy, Paste, moving the caret with the arrow keys, Find. Recording goes on.
- Playing needs an editable text document. Import of PSPad macros is not available.

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

## [format.inspector] Byte inspector
commands: nks.format.applyEditorConfig
Every action that rewrites bytes you did not edit shows what it changes first, and writes nothing until you confirm: Convert to Encoding, Line Endings, Normalize Unicode, Save As with another encoding and Apply EditorConfig.
- The window gives the number of changed ranges and bytes, the size before and after, the encoding and the line endings before and after, and one line per range: where it is, the old and the new length and the first bytes (up to 500 lines; the rest is counted).
- When the target encoding cannot hold some characters, the same window lists each one with its line, column and byte, and offers three answers: cancel (nothing is written), change the encoding, or replace each with a question mark.
- Apply EditorConfig Encoding and Line Endings in the Format menu converts the file to the charset and line ending its .editorconfig sets; each step opens the inspector. A property the file does not set is left alone.
- The ranges are those of the revision you looked at; if the document changed meanwhile, the action is refused.
- File and then Changes Before Saving shows the same list for the next save.

## [format.batchEncoding] Convert the encoding of many files
commands: nks.file.convertEncodingBatch
1. Format and then Convert Encoding of Files in a Folder. Choose the folder, file name masks (for example *.txt;*.php, empty means every file) and the target encoding.
2. The program reads the files and shows, before it writes anything, how many convert without loss and which files would lose characters, with the characters and their count. Files that are binary, already have these bytes, or have an encoding the program can only guess are skipped and listed with the reason.
3. Choose Convert N files (files that would lose characters stay as they are) or Convert All and Replace Lost Characters with a Question Mark.
4. A summary lists what was converted, what stayed and what changed after the preview.
- The folder walk follows the ignore files and skips hidden files and files over 64 MiB, like the search in files. Open documents with unsaved changes are left out.
- Each file is replaced as a whole or not at all, and only if it still has the content the preview was made from. The run is journaled: if the program or the computer stops in the middle, the next start offers to finish the batch or to put the old files back.
- A file in an encoding that is only a guess (the percentage is low) is never converted; open it, choose Reopen with Encoding and convert it from there.

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
- F3 and Shift+F3 find the next and previous match. The status bar says Match 1 of 4, or At least N matches when the search hit its limit.
- Ctrl+G opens Go to offset. Notation: 0x1F40, +0x100 (from the caret), end-512, 50%, 4K, sel (start of the selection), val (the four-byte value under the caret).
- Not found: there is no match. Invalid pattern or Invalid expression: correct the entry. The offset lies outside the file: enter a smaller value. The file changed on disk meanwhile, the search result was dropped: repeat the search.
- The wildcard *: 50 4B * 04 stands for 0 to N bytes between 4B and 04. N is the setting Longest wildcard * in hex search; while it is 0 (not chosen) the search asks for it. *{2,8} writes a range of your own and needs no setting.
- A pattern between slashes is a regular expression over bytes, for example /\x50\x4B[\x01-\x08]/. A byte is a character 0 to 255, . is any one byte (also 0A and 0D), \xHH is a byte and [...] a set of bytes. Groups, |, ? * + {n,m} and \n \r \t \0 \d \w \s work; ^, $, \b, back references and look-around do not, because bytes have no lines or words. An open repeat (.*, +, {n,}) reaches at most N bytes. A match needs at least one byte, and matches may overlap.
- Ctrl+H replaces bytes: first the pattern (the same forms as Find), then the replacement as hex, text in quotes or a number; an empty replacement deletes the matches. With a selection only the selection is searched.
- A preview lists every match with its old and new bytes and warns when the length of the file changes. Replace All writes all of them as one undo step. If the file changed after the preview, nothing is replaced. Matches that overlap an earlier one are left out.
- In overwrite mode the replacement must be as long as the match; switch to insert mode (the Insert key) to change the length.

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

## [hex.bookmarks] Hex: bookmarks and coloured regions
commands: nks.hex.bookmarks.panel, nks.hex.bookmarks.edit, nks.hex.bookmarks.export, nks.hex.bookmarks.import
A hex bookmark marks a region of bytes with a name, a colour, a comment and the type it was read as in the data inspector.
1. Ctrl+F2 marks the selection (without a selection: the value the data inspector shows at the caret, or one byte) and removes the narrowest bookmark under the caret when there is one. F2 and Shift+F2 jump to the next and previous bookmark.
2. Hex: Bookmark Properties opens the name, colour, comment and the type of the bookmark under the caret (a new bookmark when there is none).
3. Hex: Bookmarks Panel (Ctrl+K then Ctrl+B) lists the bookmarks with a filter that matches name, comment and type. Enter or a double click selects the region; Delete removes it.
4. Hex: Export Bookmarks writes a CSV (name, colour, comment, type, start and end offset) or, when the file name ends in .json, JSON. Hex: Import Bookmarks reads either form and adds the regions that fit the file; a region that is empty, lies outside the file or is already there is skipped, and the status bar counts them.
- A region is drawn tinted in its colour with a bar on top, so the colour is not the only sign; the status line names the bookmark under the caret.
- Regions follow the bytes: bytes inserted before a region move it, bytes inserted inside make it longer, an edit over its edge is taken into it, undo and redo move it back. A region whose bytes were all deleted is dropped.
- Bookmarks are kept in the session (they are back after a restart), never in the file. Clear Bookmarks removes all of them.

## [hex.map] Hex: file map and byte statistics
commands: nks.hex.minimap, nks.hex.minimap.mode, nks.hex.statistics
The map is a strip beside the scroll bar that shows the whole file at once, in about four-pixel cells. A rectangle marks the part on screen; a click or a drag scrolls the grid there.
1. Hex: File Map (Ctrl+K then Ctrl+G) shows or hides the map. Hex: Map by Entropy or Kind of Byte switches what the cells show: the entropy of the bytes (light for ordered data, strong for compressed or encrypted data), or the kind of byte that makes up most of the cell (zero bytes, text, control bytes, high bytes, FF).
2. A narrow lane at the right edge marks the matches of the last search, the bookmarks in their colours and the places changed since the last save.
3. Hex: Byte Statistics (Ctrl+K then Ctrl+S) opens a panel with the histogram of the selection, or of the whole file when nothing is selected: how many of the 256 byte values occur, the entropy in bits per byte, the most frequent values and the shares of the byte classes.
- A file up to 1 GiB is read whole in the background. For a larger file the map first shows a sample, a few blocks of every cell, and gets better as the program reads more, up to 1 GiB in all; a cell that was not read yet is shown pale.
- The statistics of a whole file above 1 GiB are not counted at once: select a range.
- The map and the counting run beside the window and are redone when the file changes.

## [hex.template] Hex: structure template
commands: nks.hex.template
The structure panel reads the file through a template and shows its fields as a tree. Selecting a field selects its bytes in the grid.
1. Hex: Structure Template (Ctrl+J) opens the panel, or closes it when it already has the focus. The template that fits the file is chosen by its magic number, then by the file extension; the list above the tree lets you pick another one, and the choice is kept for that extension until the program ends.
2. A field with parts (a structure or a list) is read when you open it, so a large file opens at once. Each row shows the name, the value and, in grey, the offset and the length in bytes.
3. A field that cannot be read is shown in red with the reason, for example that it runs past the end of the data or differs from the expected bytes. The fields behind it are not shown, because their place is unknown.
- Templates are a subset of the Kaitai Struct .ksy format: integers, floats, strings, raw bytes, fixed bytes (contents), enums, nested types, size, size-eos and repeat (eos or a count). A size or a count is a number or an earlier integer field. Expressions, if, switch-on, instances and process are not supported.
- Reading stops with a message when a template nests too deep, repeats one field too often, or reads a string or an amount of work above a fixed limit.
- Shipped templates: BMP header and PNG chunk list. Opening your own .ksy file is not available yet.

## [hex.compare] Hex: binary comparison
commands: nks.hex.compare.file, nks.hex.compare.saved
Two hex grids side by side, scrolling together, with the differences tinted and listed below.
1. Hex: Compare with File asks for a file and compares it with the document of the active tab (with its unsaved changes). Hex: Compare with Saved File compares the document with its own file on disk, so you see exactly what the next save would change.
2. Aligned (the default) looks for bytes that one file has inserted or deleted, so an inserted block shows as an insertion and the bytes behind it are not reported as changed. Byte by byte compares every offset with the same offset in the other file, which is what you want for files with a fixed layout. It is used for any file over 256 MiB, which the aligned mode does not read.
3. The list names each difference: changed, inserted or deleted, with its range in the left and in the right file. Enter on a row, F8 and Shift+F8 (or the buttons) go to the next and previous difference in both grids.
- Scrolling one grid scrolls the other to the same place in the other file; with inserted bytes that is the corresponding place, not the same offset.
- The grids are read-only. The map beside each grid marks the differences.
- The list stops at 1,000,000 differences (the window says so); two unrelated files would otherwise fill the memory.
- Two revisions of a file from the local history are not offered yet, because the local history does not exist.

## [tools.run] Tools: run an external program
commands: nks.tools.run, nks.tools.stop
1. Tools, then Run Tool lists the tools written in the file tools.json in the profile folder. The last row opens that file; the first time it writes an empty one for you to fill in.
2. A tool has an id, a title, an executable and args (a list: one entry is one argument). Optional are cwd (the directory it runs in; the folder of the current file when missing), timeoutSeconds (without it there is no time limit) and logPattern (see LOG pattern). Example: {"externalTools": [{"id": "build", "title": "Build", "executable": "dotnet", "args": ["build", "${File}"], "cwd": "${FileDir}", "timeoutSeconds": 120}]}
3. In executable, args and cwd the variables ${File} (full path), ${FileDir}, ${FileName}, ${FileBase}, ${FileExt}, ${Line} and ${Column} stand for the current document; $$ is a dollar sign. An argument stays one argument whatever the value holds, spaces and quotes included: no shell is involved. An unknown variable, or ${File} for a document that has no file, stops the run before anything starts.
4. A tool whose command and directory have not run before is shown with both and starts only after you confirm. The confirmation is remembered until the definition, or the program it leads to, changes.
5. The output goes line by line to the Tasks tab of the output window, followed by the exit code and the time. Stop in that window, or Stop Running Tool, ends the program and everything it started; so does the time limit.
- Batch files (.bat, .cmd) are started through cmd.exe with every argument quoted for it, so spaces, quotes and characters such as & | < > ^ % pass through unchanged. An argument with a line break or another control character is refused before the start, and so is one with ! when the batch file turns delayed expansion on.
- A program name without a path is looked for in PATH only, never in the working directory.
- Output is read as UTF-8. One tool runs at a time.

## [tools.logPattern] LOG pattern: click an error in the output
commands: nks.tools.logPattern
1. LOG Pattern for the Output opens a tester with the latest output of the tools as the sample. Each line of the sample shows the file, line and column the pattern found in it, or that it found nothing.
2. The mask is matched from the left and without regard to case. * skips any text up to the next literal part of the mask. %F is the file name, up to the next literal part. %D is a drive (C: followed by a slash) in front of it. %L is the line and %C the column (digits). %S skips spaces and tabs. %E ends the mask. A mask needs %L; without %F it points to the file that was open. Examples: %F:%L:%C:* for main.c:12:5: error, *] %F(%L) for [Error] a.pas(346): text.
3. The other language is a regular expression with the groups line (needed), file and column.
4. Use for the output makes the pattern work for the lines in the output window: a click on a line, or Enter, opens the file at that line. A relative file name is looked for in the directory the tool ran in, then in the folder of the file that was open when it started, then among the open documents by name.
- A tool can bring its own pattern: "logPatterns": [{"id": "gcc", "title": "gcc", "syntax": "mask", "pattern": "%F:%L:%C:*"}] in tools.json and "logPattern": "gcc" in the tool. Without "syntax" the pattern is a regular expression.

## [tools.edit] Tools: edit definitions, typed variables, tools from a folder
commands: nks.tools.edit
1. Tools, Edit Tool Definitions opens one editor for the tools of the file tools.json. New, Save and Delete write the file at once. The line under the form shows the command exactly as it would start for the open file, or why it cannot run now. The editor and Run Tool use the same definition, so a tool starts with the same arguments from both.
2. A definition can declare variables, one per line: Name:text or Name:path, optionally followed by =value (the value may use other variables, for example Out:path=${FileDir}/out). Without a value the user variable or the built-in variable of that name is used. The order is: declared value, then your own variables (Edit User Variables), then the built-in ones. Use the name as ${Name} in executable, args and cwd.
3. A text is one line. A path is one line, not empty, without the characters * ? " < > | . A value of the wrong type stops the run before anything starts and the output names the variable.
4. The folder of the open file, or a folder above it, may hold .textida/tools.json with tools of the same shape. They are listed in Run Tool, but they run only after you confirm the command and the directory, even when the same text is in your own file. A definition that changes, including its variables, is asked again. These tools cannot be edited in the editor.
- An empty argument cannot be written in the editor. Saving drops comments in tools.json.

## [tools.make] Makefile: run a target
commands: nks.tools.makeTargets
1. With a Makefile open (Makefile, GNUmakefile, *.mk, *.mak), Tools, Run Makefile Target lists its targets, read from the text of the tab. Choose one to run make -f with that file and the target, in the folder of the file.
2. The run goes through the tool runner: the command is shown and has to be confirmed the first time, the output goes to the Tasks tab, Stop ends make and what it started.
3. A line of the output that names a file and a line (main.c:12:5: error, Makefile:7:) opens that place on a click or Enter.
- Make has to be installed and found in PATH. Special targets (.PHONY), pattern rules and names with a variable or wildcard are not listed. The confirmation is tied to the command, not to the content of the Makefile, because running a Makefile means running what it says.

## [packages.registry] Package manifests: names, versions and newer-version marks
commands: nks.packages.lookup, nks.packages.refresh, nks.packages.revoke
1. In package.json, composer.json, requirements.txt, a .csproj file, go.mod, Cargo.toml and Gemfile the editor can ask the package registry (npm, Packagist, PyPI, NuGet, Go proxy, crates.io, RubyGems) for names and versions. Nothing is sent until you agree: Tools, Check Package Versions shows the servers and asks once for each registry.
2. After you agree, Ctrl+J in the name of an entry offers names from the registry, and in a version it offers the versions of that package, newest first. PyPI and the Go proxy have no search: a name is offered only when the typed text is the whole name.
3. An entry that pins an exact version (1.2.3, ==1.2.3, v1.2.3) and has a newer release gets an information mark on the version. Ranges (^, ~, >=) are not marked. A pre-release is not taken as the newer version while a release exists. The check runs when you stop typing.
4. Answers are kept in a cache until the editor closes. An offer or a mark that came from the cache says so ("cache"). Refresh Package Versions drops the cache of that registry and asks again.
5. Forbid Package Registry Lookups takes every consent back and drops the cache. The network proxy of the Network Proxy command is used; a proxy with a user name cannot be used, and the lookups stop then.
- Nothing is downloaded or installed, vulnerabilities are not looked up and go mod download is never run. Only package names, the typed text and your network address reach the registry.
- A registry that fails gives no offers; Check Package Versions names the error in the status bar. The time limit, the size limit and the number of packages per check are provisional.

## [packages.scripts] Package scripts: npm and Composer
commands: nks.packages.script
1. With a package.json or composer.json open, Tools, Run Package Script lists its scripts. Choose one to run npm run name or composer run-script name in the folder of the file.
2. It goes through the tool runner like a tool from a project folder: the command, the directory and the file it comes from are shown and have to be confirmed. The confirmation is tied to the text of the script, so a changed script asks again. The output goes to the Tasks tab; Stop ends it.
- npm or Composer has to be installed. On Windows both are batch files; the editor starts Node with npm-cli.js, or PHP with composer.phar, from the folder of the batch file, so a script name with a line break or ! still works. Without that standard layout the batch file is started through cmd.exe.

## [plugins.manager] Plugin manager: install, update, roll back and remove plugins
commands: nks.plugins.manage
1. Tools, Plugin Manager lists the plugins that come with the editor and the ones you installed, with the permissions each holds. Choose Install plugin from ZIP to add one.
2. The ZIP is unpacked into a staging folder and checked before anything of it runs: every path has to stay inside the plugin folder, links are refused, size and file count are limited, the manifest nkspad-plugin.json has to sit in the root of the ZIP and be valid, and the id may not belong to a built-in plugin. A problem is named in its own message and nothing is installed.
3. Then the editor shows the permissions the plugin asks for and waits for your answer. Nothing of the plugin runs before you allow it. The permission to edit and create files below the workspace folders (workspace.files) is granted here, by you, like every other one. If the ZIP was downloaded (Mark of the Web on Windows) the dialog says so.
4. Choose an installed plugin to enable or disable it, update it from a newer ZIP, roll back to the previous version, change its permissions or uninstall it. A new version is unpacked beside the old one and becomes active only when it loads; the old one stays for the roll back. An update asks only about permissions you were never asked about; your earlier answers stay.
5. A permission you take back stops new requests of the plugin at once and cancels the ones waiting. A plugin without a permission gets an error, not an empty result. Disabling stops the process; uninstalling deletes only the folder of that plugin.
- Permissions are a record of what you allowed, not a sandbox: a plugin is a program that runs with your user rights. Install only plugins you trust. Signatures are not checked yet. The size and file count limits for a ZIP are provisional. New commands of an updated plugin appear after a restart.

## [tools.lint] Linters: underline what PHPStan, Psalm, golangci-lint, Clippy, clang-tidy and RuboCop find
commands: nks.tools.lint
1. Tools, Check File with Linter runs the linter that suits the open file: PHPStan or Psalm for PHP, golangci-lint for Go, Clippy for Rust (it needs a Cargo.toml in the folder of the file or above it), clang-tidy for C and C++, RuboCop for Ruby. If two linters handle the file, the first one whose configuration file (phpstan.neon, psalm.xml) is found is used, else the first one that is installed.
2. The linter checks the file on disk, so save first: a document with unsaved changes is not checked. The findings are underlined in the text (the message shows when the pointer rests on it) and listed in the Tasks tab, one per line; a click or Enter opens the place. The next run of the same linter replaces them, so a problem you fixed is gone after it.
3. To check after every save, switch on "Check with PHPStan" (or the other linter) in File, Save Actions, or write {"kind": "lint", "id": "phpstan"} in saveActions.json. A linter never runs on a change. The ids are phpstan, psalm, golangci-lint, clippy, clang-tidy and rubocop-lint.
- The program is looked for in vendor/bin, node_modules/.bin, .venv and PATH; nothing is downloaded. The first run asks you to confirm the command.
- No time limit is preset: while lintTimeoutSeconds is missing in saveActions.json, no linter runs and the status bar says so. A linter that runs over the limit is stopped and reported; the editor stays usable meanwhile.
- PHPStan reports a line and no column, so the text of that line is underlined. A program that is a batch file (phpstan.bat) is started through cmd.exe, as for every tool.

## [file.saveActions] Save actions: format, run a tool, upload
commands: nks.file.saveActions
1. File, Save Actions lists what runs around a save: format, run a tool, upload to the server. A row switches the action on or off; the row under it switches "only on manual save". A formatter (gofmt, rustfmt, clang-format, RuboCop) is offered only when its configuration file (go.mod, rustfmt.toml, .clang-format, .rubocop.yml) is in the folder of the file or above it, and it is off until you switch it on.
2. The order is fixed: formatters first, then the save, then tools and the upload. A formatter works on a copy of the text and is stopped when the time limit runs out. If it times out, fails (a syntax error, for example) or is not installed, the file is saved as it is and the status bar and the Tasks tab say why. Text you typed while it ran is never overwritten. Only a document with unsaved changes is formatted.
3. The upload sends exactly the bytes this save wrote, never older or newer ones. If the file on disk changed after the save, nothing is sent. It works for a file opened from a server and goes to that server.
4. An autosave runs the actions that are not marked "only on manual save". A save made by a plugin runs no action, so an action that saves cannot start a loop. Save As runs no action.
5. The actions are written in saveActions.json in the profile (Open the save actions file) and, for a project, in .textida/saveActions.json in the folder of the file or above it. A project entry replaces the entry of the profile with the same kind and id. Example: {"formatTimeoutSeconds": 10, "actions": [{"kind": "format", "id": "gofmt"}, {"kind": "tool", "id": "lint", "onlyManual": true}, {"kind": "upload"}]}. A tool is one of Run Tool, by its id.
- No time limit is preset: while formatTimeoutSeconds is missing, a formatter does not run and the status bar says so. The value in the example is only an example.
- After an autosave nobody can answer: a tool that was never confirmed does not start, and a newer version on the server shows on the bar of the tab instead of a dialog.
- Switching an action in the list rewrites saveActions.json of the profile and drops comments in it. Actions from a project are listed but changed in the project file.
- Save Actions also offers "Check with ..." for the linters that handle the file; it is off until you switch it on. The check runs after the save and does not hold it; its time limit is lintTimeoutSeconds.
## [file.configFiles] Configuration files of the file
commands: nks.file.configFiles
1. The status bar names the configuration files that apply to the open file: .editorconfig, .clang-format, .clang-tidy, .rubocop.yml, rustfmt.toml, .golangci.yml and the configuration files of the language (for example ruff.toml, stylua.toml, .shellcheckrc). Each one is the nearest in the folder of the file or above it. Click the item, or use File, Configuration Files of This File.
2. The list opens a file you choose. A configuration that is missing can be created from a minimal template; it is written to the nearest parent folder that holds .git, otherwise to the folder of the file, and an existing file is never replaced.
- Only folders are looked at: no tool is started, so the list shows what is on disk, not what a tool would use. The list is read again when the file is opened, saved or renamed, so a file created outside the editor shows after the next save.
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
- A search results tab has no bookmarks. In hex the same commands mark regions of bytes (see Hex: bookmarks). In a large file whose lines are still being numbered the command says the line index does not reach that place yet.

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

## [edit.recipeFiles] Recipes over files
commands: nks.edit.runRecipeFiles
A saved recipe runs again over another set of files. You see every change first; nothing is written before you apply it.
1. Choose Edit, Run Recipe on Files and pick a recipe: one from a data plugin, or a file in the folder recipes of the profile.
2. Pick the files: a folder with a file mask (for example *.cs;*.txt; the ignore files of the search apply), the open tabs, or the files of the search results.
3. The Search Results tab lists the changes file by file, with the same ticks as Replace in Files. Apply writes the ticked ones. A file that was changed by someone else after the preview is not touched: it is named as a conflict and the other files are written. Open documents get one Undo step.
- A recipe file is JSON with id, title and steps, the same fields as a recipe in nkspad-plugin.json, plus an optional version and an optional converters list (the converters its convert steps name). A file without a version is version 1, the format of the first recipes, and loads as before. A file of a newer version is refused with a message.
- Steps: matches, columns, convert, replace and set work as in Edit Recipes (each file is its own whole text). A step {"kind": "format", "formatter": "id"} runs a formatter of Save Actions over the result of the steps before it; it must come last, it needs the time limit formatTimeoutSeconds of saveActions.json, and it works on named files of the type that formatter handles. A file where a step cannot run is left out and named with the reason.
- Every file keeps its encoding and its line endings; a new character the encoding cannot hold is shown as a change that cannot be applied. Format steps do not run on a single file in Run Edit Recipe.

## [edit.aiContext] Copy as AI context
commands: nks.edit.copyAiContext
Puts the selection, the file, the open tabs or the opened folder on the clipboard as one block of Markdown or XML, to paste into an AI assistant. Nothing is sent anywhere.
1. Choose Edit, Copy as AI Context and pick what to copy: the Selection (every selection), This file, the Open tabs or the Opened folder.
2. The preview shows how many files go in, the size as Markdown and as XML, and what was left out with the reason. Choose Copy as Markdown or Copy as XML.
- Each file has its path, the lines, the language, the encoding and the revision. A file with changes that are not saved is marked, and the text is the text in the editor now.
- Left out and named: .env files (their values are never copied, also when they are shown in the editor), files and folders the ignore files exclude, hidden ones, binary files, files over the size limit of the folder walk and files that cannot be read.
- A large file in paged mode is never copied whole: only its selection is. Without a selection it is listed as left out.

## [edit.fragment] Edit a string as JSON, SQL or HTML
commands: nks.edit.fragmentJson, nks.edit.fragmentSql, nks.edit.fragmentHtml, nks.edit.fragmentWriteBack
A string in a PHP or JavaScript file opens in a tab of its own, in the language you pick, and goes back into the string with the escaping of its kind.
1. Put the caret in the string and choose Edit, Edit String as JSON, Edit String as SQL or Edit String as HTML. The new tab shows the text the string stands for, without the quotes and escapes.
2. Edit the text. Choose Edit, Write String Fragment Back: the text is escaped for the string and replaces its content. The tab stays open, so you can write again.
- Kinds: PHP single-quoted and double-quoted strings, heredoc and nowdoc; JavaScript strings in single quotes, double quotes and templates in backticks. In a double-quoted string and in a heredoc every dollar sign gets a backslash, so no variable can appear by accident.
- Refused with a message: a string with variables or expressions (`$name`, `{$x}`), a JavaScript template with `${...}`, an escape that is not text (a byte above 127, octal) and, when writing, a line that would end a heredoc or nowdoc.
- The tab follows the string through edits of the file elsewhere. If the file changed inside the string after the tab was opened, nothing is overwritten without asking: the dialog offers Overwrite or Cancel.
- When the file is closed, the fragment tab stays open as a detached text. Its title says so and Write String Fragment Back writes nothing.

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

## [tools.git] Git: status, compare and stage a hunk
commands: nks.git.status, nks.git.diffIndex, nks.git.diffHead, nks.git.stageHunk
1. Open a file from a Git repository and choose Tools, Git: File Status. The panel shows what `git status` reports for the whole repository: what is staged and what is changed in the working tree. Run it again after the repository changes from outside.
2. Git: Compare with Index and Git: Compare with HEAD open a comparison tab of the open text against its version in the index or in HEAD. The hunks are the same as in `git diff`.
3. Git: Stage Hunk at Caret puts only the change the caret is in into the index. The other changes of the file stay in the working tree only. The file on disk is not changed.
4. If another program changed the index in the meantime, nothing is written. The plugin reads the index again and reports a conflict; check the changes and repeat the command.
- The plugin writes to the repository only on Git: Stage Hunk at Caret. Status and compare only read and do not even refresh the index file.
- The text open in the editor is staged, saved or not. A file with an unresolved merge conflict is not staged.
- Not there yet: unstage a hunk, commit and branches. A file with line ending conversion (`autocrlf`) can differ from the index on every line.
- The Git process starts with the first command. The `git` program must be on the PATH.

## [tools.lsp] Language server: definition, references, rename, completion and diagnostics
commands: nks.lsp.start, nks.lsp.goToDefinition, nks.lsp.findReferences, nks.lsp.rename, nks.lsp.restart, nks.lsp.stop
1. Open a text document and choose Tools, Language Server: Start for This Document. The server for the language of the document starts and gets the text. From then on every edit goes to it, and what it finds wrong is underlined.
2. Language Server: Go to Definition jumps to the definition of the word at the caret, also in another file. Language Server: Find References lists every place in the panel on the left; Enter or a double click on an item opens it.
3. Language Server: Rename Symbol asks for the new name of the word at the caret. The server's changes in all files, with the creating, renaming and deleting of files it asks for, are shown together in the change review; what you accept is applied as one step. A file that is open in another tab must have been started with the first command too, otherwise the rename is refused as a whole.
4. Ctrl+J (Edit, Complete Word or Clip) also lists what the running server offers at the caret. Choosing such an entry also makes the additional changes the server asks for, such as adding an import, in the same Undo step. An answer that arrives after you changed the text is not shown.
5. If the server stops by itself, a notice says so and the underlines it drew disappear. Language Server: Restart (or any other command of this group) starts it again and hands it every open document as it is now. Language Server: Stop shuts it down.
- The editor does not start a server on its own: nothing runs until the first command of this group, and a server is not told about a file until then.
- Large files shown in pages and files over the size limit never go to a server.
- The server is looked up by name on the PATH. If it is not found, a notice names the program that was looked for.
- The status bar shows LSP: running, LSP: not found or LSP: off for the language of the last command.
- Not there yet: the choice of the project folder (the folder of the first document is used), and a rename in files that are not UTF-8.

## [edit.complete] Completion of words and clips
commands: nks.edit.complete
1. Press Ctrl+J (Edit, Complete Word or Clip). The list offers the words that continue the word in front of the caret, then the clips that fit the language of the document.
2. Pick an entry. A word replaces the word in front of every caret; a clip is inserted like in the topic Clips and replaces that word. It is one Undo step.
- The words are taken from all open text tabs. The word that is being typed is not offered, and a word is offered only when it is longer than what is typed. Words of the document being edited come first, then the more frequent ones.
- Clips come from the library of the topic Clips. A clip with no language fits every document; a clip with a language fits only documents of that language.
- The values of a secrets file (`.env`) are never offered while they are hidden. They are offered only in the file itself and only while shown.
- The list holds a limited number of words; type in the box to narrow it. Entries of a running language server come with them, see the topic Language server.

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

## [table.copyAs] Copying cells as Markdown, SQL, JSON or HTML
commands: nks.table.copyAs
Select cells in the table view and choose View, Copy Table As, then a form. The clipboard gets the selected columns and rows in that form.
- Markdown table: a pipe table with the header (or the column letters A, B, ... when there is no header). Special characters such as the pipe, the backslash, asterisks, underscores and angle brackets get a backslash, and a line break in a cell becomes `<br>`.
- SQL INSERT: one statement per row with the values as text in single quotes (a quote is doubled). The header gives the column names. You are asked for the table name, starting with the file name.
- JSON: an array of objects named by the header, or an array of arrays when the header is missing, has an empty name or repeats a name. All values are text.
- HTML table: a table element with entities for `& < > " '`; the clipboard holds the HTML as source text and as HTML for pasting into a document.
- A hidden value of a .env file is copied as stars, not as its text. Copy and cut with Ctrl+C and Ctrl+X put the placeholder in its place too, and a cut that touches one deletes nothing.
- If the file changes while the command runs, nothing is copied and a notice asks you to run it again.

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

## [tools.mcp] The editor as an MCP server
commands: nks.tools.mcpServer, nks.tools.mcpShareTab, nks.tools.mcpTools
An AI assistant that you start on this computer (for example a coding agent) can use Textida through the Model Context Protocol. It sees only the tabs you share, and it never writes: you see each proposal as a change preview and you decide.
1. Choose Tools, MCP Server. The dialog lists what the assistant gets. Switch on to start the server; choosing the command again switches it off and takes every tab back.
2. Configure the assistant to start the program textida-mcp.exe from the mcp folder next to the editor (the status bar shows the full path). It talks to the editor over a local pipe; there is no network port.
3. Open a tab and choose Tools, Share This Tab with MCP. The same command takes the tab back. A tab that is not shared is not even listed.
4. Choose Tools, MCP Tools to switch single tools on or off. Choosing a tool in the list changes its state; Escape closes the list.
- The assistant reads the text as it is in the editor, also what is not saved, searches in it, asks for the outline, the encoding and the line endings, finds lines of a log level in a log of any size and reads or searches bytes (at most 64 KiB per read).
- A change is a proposal for one revision of the text. If the text changed in the meantime, the proposal is refused and the assistant has to read again. You see the proposal like other whole-document changes and choose to apply it or not; the assistant can ask how it ended.
- Values in .env files are shown to the assistant as *. When you share such a tab you are asked whether it may see the real values; the answer holds for that tab only.
- Withdrawing a tab or switching the server off stops the requests that are running. A reload of a file also withdraws it.
- The program for the assistant speaks the current MCP version (2026-07-28) and, for assistants that still start with initialize, version 2025-11-25.

## [tools.ide] The /ide bridge for Claude Code
commands: nks.tools.ideBridge
Claude Code that runs in a terminal on this computer can connect to Textida with its /ide command. The agent then learns where your selection is and can propose changes. It sees only the tabs you share, and it never writes: you see each proposal as a change preview and you decide.
1. First switch on the MCP server (Tools, MCP Server) and share a tab (Tools, Share This Tab with MCP). The bridge uses the same shared tabs.
2. Choose Tools, IDE Bridge /ide. The dialog says what the agent gets. Only after you confirm does Textida open a port, on this computer only (127.0.0.1), and write a lock file with a token to ~/.claude/ide (or to the ide folder of CLAUDE_CONFIG_DIR). Choosing the command again closes the port and deletes the file.
3. In the terminal run /ide in Claude Code. A program without the token from the lock file is refused.
- The agent is told about the selection and path of a shared tab each time the selection changes. Text of .env values is hidden as in every other read. Withdrawing the tab stops this at once.
- The agent's changes arrive as proposals through the same change review as for MCP.
- Not finished: the lock file fields, the WebSocket subprotocol, the selection message and the diff call of Claude Code are not described in its public documentation, so no Claude Code version is confirmed to work with the bridge yet. Diagnostics are an empty list until language servers are connected.

## [tools.agent] Agent over ACP
commands: nks.agent.start, nks.agent.panel, nks.agent.shareTab, nks.agent.cancel, nks.agent.stop
1. Tools, Start Agent... starts a coding agent that speaks the Agent Client Protocol (ACP): Gemini CLI, Codex, Claude, Junie, GitHub Copilot or OpenCode. The dialog shows the exact command and the folder it runs in; nothing starts before you confirm. The agent must be installed and found in PATH. A batch file (.cmd, .bat), the usual form of an npm-installed agent on Windows, is started through cmd.exe with every argument quoted for it.
2. The Agent Panel under the editor shows the conversation. Type a message and press Enter. Cancel Agent Turn (or the Cancel turn button) sends the cancel to the agent and answers its open permission questions with "cancelled"; Stop Agent ends the process.
3. The agent reads only the tabs you share: Share This Tab with Agent. It then gets the text as it is in the editor, unsaved changes included; values of .env files stay hidden. Stopping the agent takes every shared tab back.
4. When the agent writes a shared file, the change is shown as hunks in the change review (the Search Results tab) and is written only when you apply it. If you reject it, the agent gets an error. A file that is not shared, or not open, is not written through the editor.
5. When the agent asks for permission, a dialog shows the choices the agent offers. Cancel or closing the dialog answers "cancelled".
- Where the guarantee ends: the editor controls only what goes through it. The agent's own tools can change files, run programs and use the network without the editor, and the editor does not set the permission mode of any agent (each keeps its own default). Set what the agent may do in the agent's own settings.
- Every agent is listed as "not verified": no run against the real program has passed the test scenarios yet. A turn in which the agent neither says nor asks anything for too long ends as timed out. The time limits and the size of a file handed to the agent are provisional.
## [tools.mcpAgents] MCP servers of agents
commands: nks.tools.mcpServers
One list of the MCP servers your coding agents are set up with. Textida reads Claude Code (.mcp.json, ~/.claude.json), Cursor (.cursor/mcp.json), Codex (~/.codex/config.toml) and OpenCode (opencode.json, opencode.jsonc). Project files are looked for in the folder of the active file and its parent folders. Textida only reads these files; it never writes to them.
1. Choose Tools, MCP Servers of Agents. Each row shows the server, the agent and file it comes from, and its command or address with secrets hidden.
2. Choose a server, then Try. A dialog shows the exact command, the folder and the variables with their values hidden (***). Nothing starts until you press Start.
3. After Start, Textida runs the program, asks it for its tools (MCP versions 2026-07-28 and 2025-11-25) and ends it. The result says how many tools it offers, or gives the error and the last lines the program wrote to its standard error. The same lines are in the Tasks output.
4. Choose Convert the entry to another agent to get the entry in the format of that agent in a new tab. The snippet is checked against that format first. Values of variables and headers are replaced by ***, so fill them in before you paste the snippet into the file of that agent.
- ${NAME} in a command, an argument or a variable is replaced from the environment of Textida, ${NAME:-text} gives a default. A name that is not set there is looked up in the Windows Credential Manager under textida:mcp:env:NAME. A name without a value is an error, and nothing starts.
- Only a server started by a command can be tried. A server reached by an address is listed and can be converted, but it is not started. npx and npm are started as node with the script of the Node.js shim, so no cmd.exe stands between the editor and the server. Any other batch file is started through cmd.exe.
- Secrets are hidden in the list, the dialog, the output and the snippets, also where one appears inside an argument or in the error output of the program. Values shorter than 4 characters are not searched for in text.

## [tools.shell] Explorer menu and file associations
commands: nks.tools.shellMenuAdd, nks.tools.shellAssociate, nks.tools.shellRemove
Windows only. Textida can add itself to the Explorer context menu of your user and to the Open with list. Nothing needs administrator rights, and only keys of Textida under HKEY_CURRENT_USER\Software\Classes are written. Keys of other programs, PSPad included, are not read or changed. Both commands work in the installed Textida.exe.
1. Choose Tools, Add Textida to the Explorer Menu. Files get the entry Open with Textida; it starts Textida with the file, or hands the file to the Textida that is already running. On Windows 11 the entry is under Show more options.
2. Choose Tools, File Associations to pick extensions. Choosing an extension in the list adds or removes Textida in its Open with list; Escape closes the list. No extension is associated until you choose it.
3. To make Textida the default program of an extension, use Open with, Choose another app in Explorer. Windows keeps that choice to itself.
4. Choose Tools, Remove Textida from Explorer to delete the menu entry and every association that Textida added. The uninstaller does the same.
- The text of the menu entry is in the language of Textida at the time you add it. Add it again after you change the language.
- Moving Textida.exe to another folder leaves the old path in the entries. Add the menu entry again, or remove it first.

## [help.updates] Updates and return to the previous version
commands: nks.help.updateCheck, nks.help.updateReturn
Windows only, in the installed Textida (the folder made by Setup.ps1). Textida never checks by itself unless you turn on the setting Check for updates at start, which is off by default. A check is one request for a small signed file; nothing else is sent, and no identifier of you or your computer.
1. Choose Help, Check for Updates. If a newer version is offered, Textida asks before it does anything.
2. Install and restart downloads the package, checks its checksum against the signed file, and unpacks it next to the current version. Only then Textida closes, the folders are switched and Textida starts again with your tabs. Unsaved text is kept by the journal; with the journal off, save or close the documents first.
3. If anything is wrong (signature, checksum, a package that does not match), nothing is installed and the current version keeps running.
4. Choose Help, Return to the Previous Version to go back to the version that was installed before the last update. The newer version then becomes the one you can return to.
- The settings of Textida and the journal stay where they are, and a portable profile folder is carried over.
- This build checks only when it has an update address and a signing key; a build without them says so.

## [review.external] Review changes from outside
commands: nks.review.open, nks.review.next, nks.review.previous
When another program (a script, an assistant, a build tool) changes the file of a tab that has no unsaved edits, the bar offers Review changes.
1. Choose Review changes. The tab takes the new content, the earlier content is kept in the local history and cannot be deleted while the review waits, and the Search Results tab lists the changes line by line.
2. Tick the changes you want to keep. Unticked ones, or the whole file when its box is unticked, go back to the earlier content when you choose Apply.
3. Apply writes the reverted text into the tab as one undo step and saves the file; bytes outside the reverted changes stay as they are.
- The tab shows a badge with the number of waiting changes. Choose File and then Review Changes from Outside to open the list again; it also comes back after a restart.
- Alt+F7 and Alt+Shift+F7 jump to the next and previous change from outside.
- If you type in the tab during the review, the changes are shown again from the new text.
- Files that hold secret values (.env) and files with unsaved edits keep the older bar: Reload, Keep Mine, Compare.
- With Keep changes from outside without review in the settings, such a tab takes the new content at once. It is off by default.
