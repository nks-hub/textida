## [start] Getting started
commands: nks.help.contents
This help is part of the program and needs no internet connection. Open it with F1 or from the Help menu. F1 works in every window, dialog and panel: press it on a selected command in the command palette, inside a panel (search, files, documents, output, preview, agent, hex panels), in a bar above a document, in a dialog or in another window of the program, and the topic that fits opens directly.
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
2. The window opens at the topic that fits where you are: with a command selected in the palette at that command's topic, in the find bar, a panel, a view (hex, table, comparison, large file) or a bar above a document at the topic of that part, in a dialog at the topic of the command that opened it, otherwise at the start page.
3. F1 in the settings, the regular expression tester, the tool editor, the LOG pattern tester, the binary comparison and this help window opens the topic about that window.
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

## [remote.connections] Remote connections
commands: nks.remote.connections
A connection holds the server, the user and the protocol. The password is kept in the credential store of the operating system and is never written to a settings file or to a backup.
1. Choose File and then Remote Files and then Remote Connections. The list shows the saved connections.
2. Choose New Connection and answer the questions: name, protocol, server, port, user and password. For SFTP you can give a private key file instead of a password.
3. Choose a connection to open a file from it, or to delete it with its saved password.
- FTPS and SFTP are the normal choices. Plain FTP sends the password and the files unencrypted and is used only after you allow it for that connection.
- The first time you connect, the program shows the identity of the server (an SSH host key or a TLS certificate) and asks you to trust it. Compare it with the one the server administrator gave you.
- If the identity of a server you trusted changes, the program does not connect until you confirm the new one. Read the warning before you do.
- A password is saved in the credential store of the system: Credential Manager on Windows, the Keychain on macOS, the Secret Service (through libsecret, for example GNOME Keyring or KWallet) on Linux. If that store is missing, the program says so instead of keeping the password anywhere else. The macOS and Linux stores have not been tried on a real system yet.
- ssh-rsa: some old SFTP servers accept an RSA key, or show their host key, only with the weak SHA-1 signature called ssh-rsa. The program never offers it by itself. If such a server refuses the connection, choose the connection and then Old RSA Signature (ssh-rsa), read the warning and allow it for that connection only. The Remote output warns at every connection that allows it; turn it off as soon as the server is updated.

## [remote.open] Open a remote file
commands: nks.remote.open
1. Choose File and then Remote Files and then Open Remote File, and choose a connection.
2. Type the path on the server. A path to a folder shows its content: choose a folder to go in, or a file to open it.
3. The file is copied to this computer and opens in a tab. Edit and save as usual: Save changes only the copy on this computer.
- The bar above the text says whether the copy is the same as on the server. The editor itself works without a connection.
- Opening a remote file that already has a copy shows the copy, including changes you did not send yet. The server version is never fetched over your work.
- The log of the transfers is in the Remote tab of the output window.
- A download shows its percent in the status bar and in the Remote output. Cancel Transfer stops it. What has arrived is kept, and the next open or reload of the same server version continues at that byte. If the size or the time of the file on the server changed meanwhile, the partial file is dropped and the download starts again. When the connection is lost, the program repeats the download a few times with growing pauses.

## [remote.upload] Upload to the server
commands: nks.remote.upload
1. Choose File and then Remote Files and then Upload to Server. A changed tab is saved first.
2. Before it sends anything, the program looks at the file on the server. If it is still the version you opened, the new content is sent under a temporary name and then replaces the old file. A failed or interrupted upload leaves the old version on the server.
3. If the file on the server changed meanwhile, nothing is sent. Choose Show Differences to see the server version beside yours, copy what you need into your side, save, and upload again. Upload Anyway replaces exactly the server version you were shown.
- The setting Check of the server version chooses how a change is recognised: stat compares size and modification time, content always fetches the server file and compares it, and also notices a change within the same second and size.
- Limit: the check and the replacement are two steps, because FTP and SFTP have no conditional write. A change someone makes in that last moment is not noticed. The check runs after the upload is complete, so the moment is short.
- When the connection is lost during an upload, the program tries again a few times with growing pauses. Every try checks the server version again first, and a partly sent temporary file is continued where it stopped after its content was compared with your file. If the server refuses the rename that puts the file in place, the old file is set aside, the new one is moved in, and the old one is restored if that fails.
- The status bar shows the percent of the upload, and the Remote output a line for every tenth of it (the step is provisional). The percent counts the bytes the connection has taken, so it can run a little ahead of the server.

## [remote.cancel] Cancel the transfer
commands: nks.remote.cancel
An upload or a download that takes too long can be stopped. Your changes stay saved on this computer.
1. Start Upload to Server (topic Upload to the server), or open a remote file.
2. While the transfer runs, choose File and then Remote Files and then Cancel Transfer.
3. The upload stops and the status bar says Upload of title cancelled. The server keeps its previous version.
- A partly sent temporary file is continued by the next upload, after its content was compared with your file.
- Download of path cancelled. What arrived is kept, and the next download of the same server version continues there.
- No transfer is running. Cancel Transfer works only while an upload or a download runs; there is nothing to cancel.

## [remote.check] Check the server version
commands: nks.remote.check
The check compares the copy on this computer with the file on the server. Nothing is downloaded or changed.
1. Keep the tab of a remote file active (topic Open a remote file).
2. Choose File and then Remote Files and then Check Server Version. The program connects to the server of the file.
3. The bar above the text gives the result: Same as on the server. Changed here and not sent to the server yet. Upload? The file on the server changed since you opened it. Changed here and on the server. Compare before you upload. The file no longer exists on the server. The buttons of the bar are Check Again, Upload, Show Differences and Reload from Server.
- This tab is not a remote file. Open one with Open Remote File. The command works on the tab of a remote file only.
- The connection of this file was deleted. The connection was deleted from the list, so the file cannot be checked. Create the connection again (topic Remote connections).
- Not checked against the server. Your changes stay saved on this computer. The server could not be reached. Choose Check Again in the bar when the connection works.
- The check against the server failed: reason. Nothing was downloaded or changed. Fix the reason and check again (topic Error messages: remote files and sources).

## [remote.unsent] Unsent remote changes
commands: nks.remote.unsent
Changes to a remote file are saved on this computer first. The list shows the remote files whose copy was changed and not sent, also after the program was closed or crashed. Choose one to open the copy; the bar offers Upload.
1. Choose File and then Remote Files and then Unsent Remote Changes. The list shows each remote file whose copy was changed and not sent, with the name of the connection and the path on the server.
2. Choose a file. The copy opens in a tab and the bar offers Upload.
3. Choose Upload in the bar, or File, Remote Files, Upload to Server (topic Upload to the server).
- Editing needs no connection. Unsent changes stay until you upload them.
- No remote file has unsent changes. Nothing waits to be sent.
- The local copy of path was deleted. The copy was removed from this computer. Open the file from the server again (topic Open a remote file).

## [remote.cache] Compare the cache with the server
commands: nks.remote.pairCache
The copies of remote files stay on this computer with the server version they were made from, also after the program was closed.
1. Choose File and then Remote Files and then Compare Cache with Server, and choose a connection.
2. The program connects and compares every copy of that connection with the server by the stored version. The list says for each file: same as on the server, changed here and not sent, changed on the server, changed in both places, or no longer on the server.
3. Choose a file to open its copy. The bar checks it once more and offers Upload, Show Differences or Reload from Server.
- Nothing is downloaded or overwritten. The list offers only connections that have copies.

## [source.open] Open from a plugin source
commands: nks.source.open
1. Choose File and then Remote Files and then Open from Source. The list shows the sources that plugins offer and what each can do: whether it writes, whether a changed file is protected, whether it lists folders.
2. Choose a source and type the address, starting with the scheme of the source.
3. The text opens in a tab. The bar above it shows the source, the address with its secrets replaced by ***, and the revision the text was read at.
- Save writes the text back only if the file at the source is still at that revision. If it changed, nothing is written and your text stays in the tab. Choose Show Differences to compare, Overwrite to replace exactly the version that was found, or Cancel.
- A source that cannot check the revision in one step says so, and the program warns you before each save: it checks the revision first and then writes, so a change made in between is overwritten without notice.
- If the source cannot be reached, the tab opens from the copy the program keeps in memory and says Offline. Reload from source tries again and never replaces unsaved changes.
- Addresses with a password, a token or a key in them are shown with *** in the bar, the recent files and the logs. The recent files keep only the masked address, so a source that needs the secret in the address asks for it again.
- Limit: a secret inside the path of an address cannot be recognised and stays visible. Save As writes a copy to disk and ends the link to the source.
- A source that searches (the list says so) accepts words as well as an address. Words give a list of the pages found, and choosing one opens it; an address that starts with the scheme of the source opens at once.
- The Remote output says for every opened and saved file which adapter and which service the source named, for example: adapter 1.0.0, service NKS IS API v1.
- NKS IS wiki (nksis://wiki/page): the first time, the program asks for the address of the wiki and for an API key. Both are kept in the credential store of the system, never in a file. A key the wiki refuses is dropped and you are asked for a new one at once; if that one is refused too, the call ends with a message. The wiki cannot check the revision while it writes, so the program warns before each save, as described above. A page that does not exist yet cannot be created from here.

## [plugins.virtual] Tabs that a plugin supplies
A plugin can open a tab of its own with data it keeps: a transcript, a generated file, a listing, a diff. Such a tab has no file. It carries a badge ("virtual · read only" or "virtual · editable") and a bar that names the plugin and the address the plugin gave it (an address with a password or token shows *** in its place).
1. A read only tab can be searched, selected and copied, and it has an outline like any file of its language, but it cannot be typed into or saved. Save and Save As say so in the status bar.
2. An editable tab can be typed into, with Undo and Redo. The plugin is told about every edit without asking for it. Save (also when you close the tab and choose Save) sends the text to the plugin, never to a file; if the plugin refuses or does not answer in time, the tab stays unsaved and the status bar gives the reason. Save As is not available.
3. The plugin may replace the text of its tab. That also replaces what you typed and did not save, and ends the undo history of the tab; a plugin that wants to avoid this names the revision it last saw, and then a change you made in between stops the replacement.
4. When the plugin closes its tab, stops or is turned off, the tab closes without asking and the status bar says so. Text that was not saved is lost.
- A tab of a plugin is not kept in the session, the journal or the list of recent files.
- Values of a .env tab are hidden as in a file. The plugin sees stars in their place unless you allowed it.
- The number of such tabs per plugin, their size and the wait for a save are provisional.

## [remote.backup] Back up connections
commands: nks.remote.exportConnections, nks.remote.importConnections
1. Choose File and then Remote Files and then Export Connections and name the file. It lists the connections: name, protocol, server, port, user and the trusted server identity.
2. Choose File and then Remote Files and then Import Connections to read such a file on another computer or after a reinstall.
- The file holds no passwords, passphrases or tokens, so it can be shared. After an import, choose a connection and Set Password.
- A connection that you import replaces a connection with the same internal identifier.
- To move the whole configuration, connections included, choose Settings and then Back Up Configuration (topic Back up the configuration).

## [remote.proxy] Network proxy
commands: nks.remote.proxy
1. Choose File and then Remote Files and then Network Proxy, and choose HTTP, SOCKS4, SOCKS5 or no proxy.
2. Enter the server and the port, and a user name and password if the proxy needs them. The password goes to the credential store.
- The proxy is used for FTPS, SFTP, the update check and the requests plugins send through the editor (permission network). A connection that has a proxy of its own keeps it.
- A plugin that opens a connection by itself goes around the proxy. Package registry lookups use the proxy, but not one with a user name.
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
1. Choose File and then History and then Local History. The list shows the revisions of the file in the tab, the newest first, with the reason, the size and the label.
2. Choose a revision. Compare shows it on the left and the tab on the right in a comparison tab (topic Compare with disk); if External diff tool is set, that program is started instead. Restore File puts the whole revision into the tab. Restore Selection replaces only the selected lines with the matching lines of the revision. Label adds a name to the revision.
- A restore is an edit in the tab; undo takes it back and nothing is written until you save. The text that was in the tab is kept as a revision.
- Revisions are stored in the profile folder, so an update of the program does not remove them.
- Files that hold secret values, such as .env, are not stored unless you turn on Keep history of secrets files in the settings.
- Limits (revisions per file, days, MiB per file, MiB in total) are set in the settings; 0 means no limit. The newest revision of a file is never deleted.
- If the disk is full, the status bar says so and no revision is damaged.
- A pinned revision is never deleted by the limits; Pin in the revision menu keeps it, Unpin releases it. A file above the size limit gets no revisions and the status bar says so. Old revisions are cleaned in the background.
- A large file, one that opens in the large file view, is kept too. A large file is copied whole once, when you first change it, and every later revision is a small patch against that copy when the patch is shorter than half of the file; the list shows such a revision as patch with the size of the patch. A whole copy stays as long as a patch needs it, so the number of revisions can be higher than the limit.
- For a large file Compare needs the External diff tool from the settings, Restore File and Restore Selection are not offered, and Mark Revision wants the file saved first. Save as a Copy writes any revision beside the file, as name (date time).ext, and opens it; it works for every file.
- Revisions that are kept as patches are not found by Search in History.

## [file.historyMark] Mark a revision
commands: nks.file.historyMark
1. Choose File and then History and then Mark Revision and type a label, for example Before refactoring.
2. The current text of the tab is stored as a revision with that label. If the newest revision already holds the same text, the label goes to that revision.
- Find the revision later in Local History by its label.
- In a large file the revision is the saved file on disk, so save the file first.

## [file.historySearch] Search in history
commands: nks.file.historySearch
1. Choose File and then History and then Search in History and type the text.
2. The list shows the revisions of the file that contain the text, with the number of matches and the first line. Choose one to open the actions for it, for example Compare.
- Revisions of secrets files are not searched.

## [file.openPath] Open path at caret
commands: nks.file.openPathAtCaret
1. Put the caret on a file path in the text, for example src/a.cs:12.
2. Choose File and then Open Path at Caret, or find the command in the palette. It has no shortcut.
3. The file opens; a line (and column) after a colon is used.
- A relative path is looked up in the folder of the current file and in the open folder. A path starting with ~/ points into your home folder.
- If the path points to a folder, it is shown in the Files panel.
- No path under the caret: No file path at the caret. File does not exist: the path is shown with a note that it does not exist.

## [file.openSpecial] Open Special
commands: nks.open.special, nks.open.hex, nks.open.withEncoding, nks.format.reread, nks.open.readOnly, nks.open.newCopy, nks.file.openPathAtCaret, nks.search.relatedFile, nks.open.containingFolder, nks.open.defaultApp, nks.open.lastClosed
1. Choose File and then Open Special, or run Open Special in the palette. In the Files panel, right-click a file and choose Open Special.
2. Choose how to open the file. From the panel the selected file is used; elsewhere the file of the current tab, and if there is none you pick one.
- Open File as Hex, Open with Encoding (the encoding list opens after the file), Open Read Only (the tab does not save), Open as New Copy (an unsaved document with the text of the file).
- Reopen with Encoding, Open Path at Caret and Related File work on the current tab.
- Open Containing Folder shows the folder in the system file manager; Open with Default App hands the file to the program the system assigns to it.
- Reopen Last Closed Tab opens the file of the tab closed last; closed files that no longer exist are skipped.
## [app.settings] Settings
commands: nks.app.openSettings
1. Press Ctrl+comma, or choose Settings and then Settings.
2. Type what you look for in the box at the top, for example tab or shortcut. Both settings and keyboard shortcuts are searched.
3. Every item shows its effective value and where it comes from (default, user, workspace, EditorConfig, document).
4. Change the value right in the row; it takes effect at once and is written to the settings file. Reset to Default restores the original value.
- A nonsensical value is refused and the row says what is wrong. If the settings file cannot be written, the reason is above the list.
- Shortcut conflicts are shown on the rows concerned.
- The group Advanced holds the options that few people need: scrolling past the end, cutting a whole line without a selection, hiding the mouse pointer while typing, links with Ctrl+click, the indent of wrapped lines, how many recent files and undo steps are kept, the autosave interval, the blink time of the text cursor (0: it does not blink), smooth scrolling with the mouse wheel, and how many lines and columns around the screen get syntax colors at once. The text cursor of the usual blink time is the one of the editor; any other time draws the cursor in the program, and block selection mode uses the editor's again.
- F1 in this window opens this topic.

## [app.accessibility] Accessibility
commands: nks.view.nextPane, nks.view.previousPane
The program can be used with the keyboard alone and with a screen reader. It is tested through the accessibility tree of the program; it has not yet been tried with NVDA or VoiceOver themselves, so tell us where a screen reader reads something wrongly.
1. Press F6 to move the keyboard to the next part of the window and Shift+F6 to the previous one. The same commands are Next Pane and Previous Pane in the command palette.
2. In the part you reached, press Tab and Shift+Tab to move between the controls and the arrow keys between the rows of a list.
3. Press Esc to close a dialog, the palette or a panel; the keyboard returns to the document.
### Keyboard
- Every command is in the command palette (Ctrl+Shift+P) and, if it has one, in a menu. The menu bar is reached with the Alt key on Windows; on macOS it is the menu bar of the system (Ctrl+F2).
- The editor keeps the Tab key for itself. F6 moves the keyboard to the next part of the window: the list on the left, the document, the preview, agent and output panels, the find bar, the status bar. Shift+F6 goes back. A part that is closed is skipped.
- In the other parts Tab and Shift+Tab move between the controls from top to bottom and from left to right; in a list the arrow keys move between the rows. Esc closes a dialog, the palette and the panels and returns the keyboard to the document.
- When a dialog closes, the keyboard returns to the control that had it before.
### What a screen reader gets
- Every control, window and dialog has a name and a role in all four languages of the interface (Czech, English, Slovak, German). The names follow a change of language at once.
- A message in the status bar, a notice above the document, the number of matches of a search, the chosen row of the palette and the state of the agent are read out as they appear, without the keyboard moving.
### High contrast
- When the system asks for high contrast (a high contrast theme in Windows), the program uses the colours of the system for its windows, tabs, lists, borders and the focus frame, in the editor as well. The text of a chosen row has the colour the system sets for it. On macOS this applies when the system tells the program about it (the Increase contrast option).
- The colours of the syntax highlighting do not change. The keyboard focus is a frame around the control in the accent colour (in high contrast, in the colour the system uses for links).

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
- Cut and Paste do not work in a read-only document. In paged mode all three work (topic Large files); a selection larger than the copy limit there (256 MiB, a provisional value) is not copied and the status bar says so.

## [edit.select] Selecting
commands: nks.edit.selectAll, nks.edit.selectLine, nks.edit.expandSelection
1. Ctrl+A selects all text.
2. Ctrl+L selects the line including its line ending. Pressing Ctrl+L again adds the next line.
3. Shift+Alt+Right arrow expands the selection: first the word, then the content of the brackets around it, then the brackets too, the next pair out, the whole lines and finally all text.
- With several carets each caret takes its own line.

## [edit.lines] Working with lines
commands: nks.edit.deleteLine, nks.edit.duplicateLine, nks.edit.moveLineUp, nks.edit.moveLineDown, nks.edit.joinLines, nks.edit.toggleComment
The commands act on the line with the caret, or on all lines of the selection. Each is one Undo step.
1. Put the caret on a line, or select several lines.
2. Press the shortcut of the command, or choose it in the Edit menu or in the palette.
3. The command changes the whole line, or all selected lines, as one Undo step; Ctrl+Z takes it back.
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
- In a document that is read only Enter writes nothing. In a large file in paged mode Enter works as typing does (topic Large files (paged mode)).

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

## [edit.specialPaste] Paste as HTML and insert from a file
commands: nks.edit.pasteAsHtml, nks.edit.insertFromFile
1. Put the caret where the text should go. For Paste as HTML first copy formatted text in a browser or a word processor.
2. Choose Edit, Insert and then Paste as HTML or Insert from File. Insert from File asks you to choose the file.
3. The text goes in at every caret as one Undo step; Ctrl+Z takes it back.
- Paste as HTML (Edit, Insert) pastes the HTML source that a browser or a word processor put on the clipboard when it copied formatted text. It goes in as text, the way Ctrl+V pastes, as one Undo step. When the clipboard holds no HTML, the status bar says so.
- Insert from File (Edit, Insert) puts the text of the file you choose at every caret as one Undo step. The encoding of the file is detected as when it is opened, and its line endings become those of the document. A file larger than 16 MiB is not inserted.
- Neither works in a read-only document or in paged mode (topic Large files).
- The clipboard holds no HTML. Copy the text from a browser or a word processor that puts HTML on the clipboard. Nothing was pasted.
- name is larger than limit bytes and was not inserted. The limit is 16 MiB (a provisional value); nothing was inserted. Insert a smaller file, or open the large one in its own tab.

## [edit.insertLines] Insert into lines
commands: nks.edit.insertIntoLines
The command writes text, numbers or bullets into the lines of the selection, or into all lines when there is only a caret.
1. Choose Edit, Insert, Insert into Lines. The preview shows the first lines as they will look.
2. Choose Text, Numbers or Bullets, and where to write: start of each line, end of each line, before a column, or at each caret.
3. Press OK. The result is one Undo step.
- Numbers: First number, Digits (the number is padded with zeros to that width) and Separator, which goes between the number and the text of the line (after the number at the start of a line, before it at the end). At each caret only the number is written, so three carets get 1, 2 and 3.
- Start of each line writes at the line start; Skip the indentation writes before the first visible character instead. Before column counts characters from 1, and a shorter line gets the text at its end. Skip empty lines also skips lines with only blanks, and they do not use up a number.
- The line endings of the document do not change. It does not work in a read-only document or in paged mode (topic Large files).
- Repeat Last Command runs it again with the same choices. In the PSPad profile it is Alt+I.

## [edit.sort] Sorting lines
commands: nks.edit.sortLines
1. Select the lines (or put the caret anywhere to sort all lines) and choose Edit, Conversions and Recipes, Sort Lines.
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
commands: nks.edit.reformatParagraph, nks.edit.reformatShortLines, nks.edit.justifyLeft, nks.edit.justifyRight, nks.edit.justifyCenter, nks.edit.justifyBlock
These commands are in the Format menu. They act on the selected lines, or on all lines when there is only a caret (Reformat Paragraph: on the paragraph with the caret). Each is one Undo step.
1. Select the lines to change. With only a caret the commands act on all lines; Reformat Paragraph acts on the paragraph with the caret.
2. Choose Format and then Reformat Paragraph, Reformat Paragraph (Ends at a Short Line), Align Lines Left, Align Lines Right, Center Lines or Justify Lines.
3. The lines change as one Undo step; Ctrl+Z takes it back.
- Reformat Paragraph wraps the text at the column of the setting Right margin column, joins the lines of a paragraph and removes extra blanks. Paragraphs are separated by blank lines, and every line of a paragraph gets the indentation of its first line. When no right margin is set, the status bar says so.
- Reformat Paragraph (Ends at a Short Line) does the same, but a paragraph also ends after a line shorter than half the right margin (a provisional value), so it is not joined to the next line. It suits text with no blank lines between paragraphs.
- Align Lines Left removes the blanks at the start and end of the lines.
- Align Lines Right and Center Lines align to the width of the longest line.
- Justify Lines widens the gaps between words until every line is as wide as the longest one; a line of one word stays.
- In the PSPad profile Align Lines Left and Align Lines Right are Ctrl+Alt+Left arrow and Ctrl+Alt+Right arrow.
- They do not work in a read-only document or in paged mode.
- Set the Right margin column in the settings first; the paragraph is wrapped at that column. The two Reformat commands need the setting Right margin column.

## [edit.insertSmall] Date, GUID and repeat
commands: nks.edit.insertDateTime, nks.edit.insertGuid, nks.edit.repeatLast
1. Put the caret where the text should go; several carets are allowed.
2. Choose Edit, Insert and then Insert Date and Time or Insert GUID. Repeat Last Command is in Edit, Conversions and Recipes.
3. The text goes in at every caret as one Undo step; Ctrl+Z takes it back.
- Insert Date and Time writes the current date and time at every caret, in the format of the setting Date and time format (a .NET custom format such as yyyy-MM-dd HH:mm; month and day names follow the interface language). A selection is replaced. In the PSPad profile it is Alt+D.
- Insert GUID writes a new GUID at every caret, each caret its own: lower case, with hyphens, without braces.
- Repeat Last Command runs the last text-changing command again: Delete Line, Duplicate Line, Move Line, Join Lines, Indent, Outdent, letter case, Toggle Comment, Insert into Lines, Sort Lines, Reformat Paragraph and the alignment commands, Insert Date and Time, Insert GUID. A command with a dialog repeats with the choices of its last run, without asking. In the PSPad profile it is Alt+Q.
- They do not work in a read-only document or in paged mode.
- The date and time format is not valid: reason. Nothing was inserted. Fix the setting Date and time format: it must be a .NET custom format such as yyyy-MM-dd HH:mm.

## [edit.macro] Macros
commands: nks.macro.record, nks.macro.stop, nks.macro.playLast, nks.macro.save, nks.macro.run, nks.edit.cursorLeft, nks.edit.cursorLeftSelect, nks.edit.cursorRight, nks.edit.cursorRightSelect, nks.edit.cursorWordLeft, nks.edit.cursorWordLeftSelect, nks.edit.cursorWordRight, nks.edit.cursorWordRightSelect, nks.edit.cursorUp, nks.edit.cursorUpSelect, nks.edit.cursorDown, nks.edit.cursorDownSelect, nks.edit.cursorLineStart, nks.edit.cursorLineStartSelect, nks.edit.cursorLineEnd, nks.edit.cursorLineEndSelect, nks.edit.cursorDocumentStart, nks.edit.cursorDocumentStartSelect, nks.edit.cursorDocumentEnd, nks.edit.cursorDocumentEndSelect, nks.edit.cursorPageUp, nks.edit.cursorPageUpSelect, nks.edit.cursorPageDown, nks.edit.cursorPageDownSelect, nks.edit.deleteLeft, nks.edit.deleteRight, nks.edit.deleteWordLeft, nks.edit.deleteWordRight, nks.macro.rename, nks.macro.delete
1. Tools, Macros, Record Macro, starts recording. Do the work with commands and typing. Tools, Macros, Stop Recording Macro, ends it.
2. Tools, Macros, Play Last Macro, plays it again over the current selections and carets.
3. Tools, Macros, Save Last Macro As, stores it under a name in the folder macros of the profile. Tools, Macros, Run Saved Macro, lists the saved ones and plays the chosen one.
4. Tools, Macros, Rename Saved Macro, lists the saved ones and renames the chosen one; a name that another macro already has is refused. Tools, Macros, Delete Saved Macro, asks and then deletes the chosen one for good.
- A macro keeps commands, not keys: Duplicate Line, letter case, Add Next Occurrence and the like, Cut, Copy, Paste, and typed text. With several carets it does the same as doing the steps by hand.
- While recording, the arrow keys, Home, End, Ctrl+Home, Ctrl+End, Ctrl with the arrows (with or without Shift), Page Up, Page Down (with or without Shift), Backspace and Delete are recorded as commands too. They move and delete at every caret, except Page Up and Page Down: they move one caret, like the keys without a macro. A page is as many lines as the editor shows when the macro plays, so a macro with Page Down moves less in a smaller window.
- Find Next and Find Previous (F3, Shift+F3, Enter in the find bar) keep the search of the find bar with the step, so the macro finds the same text when it plays later. Open the find bar and type the search before you start recording; without a search the step is not recorded.
- Replace and All of the replace bar (the commands Replace Next and Replace All) keep the whole bar with the step: search, switches and replacement. Before the step runs, a macro sets the bar to these values, so the result does not depend on what the bar showed before. With Selection on, the step works on the selection the macro has when it runs. Without a search the step is not recorded.
- A playback is one Undo step. If a step cannot run, the macro stops there and the status bar names the step; what ran before stays one Undo step.
- While recording, a command that a macro cannot replay is refused and the status bar says so: everything with a dialog, file commands, opening the find bar. Recording goes on. Mouse clicks are not recorded.
- Playing needs an editable text document. Import of PSPad macros is not available.
- Default shortcuts, placeholders (OPEN, not decided yet): Ctrl+K Ctrl+R records, Ctrl+K Ctrl+Shift+R stops, Ctrl+K Ctrl+P plays the last macro; on macOS ⌘ instead of Ctrl. Your own shortcuts in the settings take precedence.
- The file of a saved macro larger than 1 MB (placeholder limit, OPEN) is not read; the list reports it as damaged.

## [edit.json] Format JSON
commands: nks.edit.formatJson, nks.edit.previewFormatJson, nks.edit.minifyJson, nks.edit.sortJsonKeys, nks.edit.fixJsonCommas
1. Select JSON text, or select nothing to work on the whole document.
2. Edit menu, Formatting, Format JSON, formats the selected ranges (every selection) or the whole document. All ranges are one Undo step; text outside them stays byte for byte as it was, including the line endings and the encoding.
3. Preview JSON Formatting shows the change as a proposal first. Apply it from the bar above the editor, or discard it.
4. Minify JSON writes the value on one line without white space and removes comments. Sort JSON Keys orders the members of every object by name (a comment moves with its member) and lays the text out like Format JSON. Remove JSON Trailing Commas removes only the commas in front of a closing bracket and changes nothing else.
- Comments and trailing commas are part of JSONC files (.jsonc, tsconfig.json and the like). Format JSON and Sort JSON Keys keep both there. In a plain JSON file they are reported as errors; Remove JSON Trailing Commas repairs the commas.
- A syntax error is reported with its line and column in the document, and nothing is changed. Text larger than the limit, or nested deeper than the limit, is refused with a message.
- A selection without a line break takes the line ending that most lines of the file use.
- If the document changed after the command started, the command stops and asks you to run it again.

## [tools.stats] Document statistics and hashes
commands: nks.tools.statistics, nks.tools.hash, nks.tools.hashCopy, nks.tools.hashCompare
1. Tools menu, Text and Calculations, Document Statistics, counts bytes, characters, words, lines and the longest line of the whole document, or of the selected parts together, and lists the most frequent characters and words.
2. Tools menu, Text and Calculations, Calculate Hash, asks for the algorithm and computes the hash of the raw bytes of the whole file, or of every selected part. The list is the one of the topic Hex: checksums and hashes (every CRC, Adler-32, the sums, xxHash32, xxHash64, XXH3, MD5, SHA-1, SHA-2). Calculate Hash and Copy also puts the hash on the clipboard. Compare Hash with Pasted asks for a hash and says whether it matches; spaces, colons, 0x and case do not matter. CRC-32, MD5, SHA-1, SHA-256, SHA-384 and SHA-512 are told by the length of the pasted hash, for any other length you choose the algorithm.
- Both read the document in blocks while you keep working, show progress and can be cancelled. Neither holds the whole file in memory, so they work on files of any size, also in paged and hex view.
- A cancelled statistic is shown as incomplete, with how many bytes were counted. A cancelled hash gives no result.
- The statistic and the hash are of the document as it was when the command started; if you edit meanwhile, they do not change.
- A word is a run of letters and digits; words are counted in lower case. The word list keeps a limited number of distinct words and says when it is full. Several selected parts are counted as one text.

## [tools.calculate] Block calculation, expressions and number bases
commands: nks.tools.calculateBlock, nks.tools.evaluateExpression, nks.tools.insertExpression, nks.tools.convertBase
1. Calculate Selected Block adds up the numbers in the selection (a block selection, a column of numbers) and shows the count, sum, smallest, largest and average.
2. Evaluate Expression asks for an expression, starting with the selected text or the line of the caret, and shows the result. Insert Expression Result writes " = result" behind the selection or the line, as one undo step, with a dot as the decimal mark whatever the language is. It understands + - * / ^ ( ), MOD and the functions ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR and TRUNC. Numbers are written with a dot. Anything else, such as a call of a system command, is refused and nothing is run.
3. Convert Number Base asks for a whole number, decimal, with the prefix 0x, 0b or 0o, or a Roman numeral, and shows it in decimal, hexadecimal, octal and binary, as a Roman numeral (1 to 3999) and as the date it is when read as Unix milliseconds.
- The program recognises how the numbers in the selection are written (Czech and Slovak: 1 234,50, German: 1.234,50, English: 1,234.50). When more than one way fits, such as 1,234, it takes the one of the program language. The results are shown in the format of the program language. Changing the language does not change the document.
- Text in the selection that is not a number is skipped and counted. The number conversion works for values from -9223372036854775808 to 18446744073709551615.
- Powers take a positive base only. The calculation uses double precision numbers.

## [tools.colors] Colours
commands: nks.tools.pickColor, nks.tools.colorToHex, nks.tools.colorToRgb, nks.tools.colorToHsl, nks.tools.colorToOklch
1. In CSS, HTML, XML, JavaScript, TypeScript, PHP, Razor and Latte files every colour value (`#ff8800`, `rgb()`, `hsl()`, `oklch()`) has a small swatch of its colour in front of it. The swatch takes no character of the document.
2. Click the swatch, or put the caret on the value and choose Tools, Colors, Pick Color at Caret, to open the picker: sliders for hue, saturation, lightness and opacity, and a value box that also accepts a colour in another notation. Clicking outside the picker writes the colour over the value in the notation it had, as one undo step; Esc closes the picker without a change.
3. Tools, Colors, Convert Color to HEX, RGB, HSL or oklch rewrites the colour value at the caret, or every one inside the selections, in that notation. The colour stays the same eight-bit colour; oklch is written with five decimals for lightness and chroma and two for the hue.
- A colour outside the sRGB range is cut to it. Opacity is kept, as `/ alpha` or as the last two hex digits.
- Text that only looks like a colour, such as `rgb(var(--c))`, the HTML entity `&#123;` or `#12345`, is left alone.
- A file larger than the limit of the plugin is not converted; the message says so.

## [tools.markdown] Markdown preview, tables and contents
commands: nks.tools.markdownPreview, nks.tools.markdownOutline, nks.tools.markdownFormatTable, nks.tools.markdownUpdateToc, nks.tools.markdownRepairTable, nks.markdown.nextCell, nks.markdown.previousCell, nks.markdown.newRow
1. Tools, Markdown, Preview Markdown or HTML opens the preview panel under the editor with the current file. The panel follows what you type: it is drawn again a moment after the last key. The editor and the panel scroll together: when you scroll one, the other follows.
2. Markdown is shown as a page: headings, lists, quotes, code, emphasis and tables with their cells (the colons in the row of dashes set the alignment of a column). A code block marked `mermaid` is drawn as a flowchart.
3. An HTML file is shown as a page too, but only a small part of HTML is understood: headings, paragraphs, lists, tables, preformatted text, code, links, images and emphasis. Every other tag, every comment and everything inside `script` and `style` is shown as the text it is. Nothing in the file runs.
4. Tools, Markdown, Show Markdown Outline lists the headings of the file in the same panel, indented by level. A click, or Enter on a heading, puts the caret on its line in the editor.
5. Tools, Markdown, Align Markdown Table rewrites the table under the caret so that every column is as wide as its widest cell. The marks `:--`, `:-:` and `--:` are kept and a short row is filled with empty cells. One undo step undoes it.
6. Tools, Markdown, Update Markdown Table of Contents writes links to the headings between the lines `<!-- TOC -->` and `<!-- /TOC -->`. Only the text between the two lines changes. Without them the two lines are added at the caret. The anchors are lower case, spaces become hyphens, and a repeated title gets `-1`, `-2`.
7. In a table, Tab selects the next cell and Shift+Tab the previous one; the columns are aligned on the way. Tab in the last cell adds a row. Outside a table both keys work as always. One undo step undoes each move.
8. In a table, Shift+Enter adds an empty row below the caret row, with the right number of cells. Tools, Markdown, Repair Markdown Table fixes a broken table: it adds a missing row of dashes, fills short rows, keeps the colons and aligns the columns.
- A picture is drawn from a `data:` address of a PNG, JPEG, GIF, BMP or WebP picture, from a picture a plugin attached (`preview:name`), and in the preview of your own file from a file next to it: a relative path that stays inside the folder of the file. SVG, a path outside the folder, a file larger than the limit and a picture with too many pixels are shown as a note with the alt text.
- A picture from the web is not loaded until you allow its host. The preview shows a button; pressing it asks once for the host, and the permission lasts until the window closes. The request uses the network settings of Textida including the proxy, redirects are not followed, and a failed picture is tried again after a while.
- A link to the web or to an e-mail address asks for your confirmation first; any other link, such as `file:` or `javascript:`, is only text.
- Mermaid: flowcharts (`graph` or `flowchart`, direction TD, TB, BT, LR or RL) with nodes such as `A[text]`, `A(text)`, `A{text}` or `A((text))` and connections `-->`, `---`, `-.->` or `==>`, with a label, are drawn. A subgraph or another kind of diagram is shown as its source. A screen reader reads the nodes and connections as the name of the diagram.
- A file longer than the limit of the preview is not drawn; the status bar says so. Esc or Close ends the preview. F6 moves the focus to the preview; the arrow keys, Page Up, Page Down, Home and End scroll it, and Tab reaches its links and buttons.
- Only headings written with `#` are listed. The width of a table cell is its display width: wide East Asian characters count as two columns. A pipe in a cell must be written `\|` or sit inside an inline code span.

## [tools.keys] Key paths in YAML and JSON
commands: nks.tools.copyKeyPath, nks.tools.showKeys, nks.tools.goToKey, nks.tools.compareKeys
1. In a YAML or JSON file the status bar shows the path of the key under the caret, for example `cs.app.title`. Items of a list are written `items[0]`, and a key that contains a dot or a bracket is written `["a.b"]`. The path is read for the revision and the caret it was asked for: if you type or move meanwhile, the answer is dropped.
2. Tools, Structure and Language, Copy Key Path puts the path on the clipboard.
3. Tools, Structure and Language, Go to Key (or `@` in Go to Anything) lists the keys of the file by their path; Enter selects the key as it is written. Show Key Outline lists the same keys in the plugin panel, and a click there selects the same bytes.
4. Tools, Structure and Language, Compare Keys with Another File asks for another open YAML or JSON file and writes to the output window the keys with a value that each file lacks.
- Block style YAML is understood: nested keys, lists, quoted keys and block scalars. A value in braces or brackets counts as one value. JSON may have comments and trailing commas.
- A syntax error stops the reading at that place; the keys before it are kept and the message gives the line.

## [tools.outline] Code outline and symbols
commands: nks.tools.showOutline, nks.tools.goToSymbol
1. In HTML, PHP, CSS, C# and Markdown files, Tools, Structure and Language, Show Code Outline lists the functions, classes, headings and rules of the file as a tree in the plugin panel. A click selects the name of the symbol.
2. Tools, Structure and Language, Go to Symbol (or `@` in Go to Anything) lists the same symbols by name; Enter selects the same bytes as a click in the outline.
3. The outline belongs to one revision of the document. After an edit it is refreshed on its own; until the new one arrives the old one stays, marked as outdated, and a jump is carried over the edits made since, or refused when the text the symbol stood on was changed. An answer for an older revision is never shown as current.
4. If the plugin process stops, the outline is read again once it is back.
5. While a file has an outline, the status bar names the symbol the caret is in, with its parents (for example `Class › method`); it follows the caret and is hidden while the outline is outdated. The outline shows the same item in bold, and a screen reader names it "current item". The mark is no selection and does not take the keyboard from you; it moves with the caret and stays on its item while the outline is refreshed.
- It is read with simple rules of the language, without a language server and without a project index. Besides the languages below: JavaScript and TypeScript (classes, interfaces, enums, functions, arrow functions, methods), Python (classes, functions, methods by indentation), Rust (modules, structs, enums, traits, impl blocks, functions, macros), Dart (classes, mixins, enums, extensions, functions, methods), C and C++ (namespaces, structs, classes, enums, functions, macros), Bash (functions), CMake (functions, macros, targets), Makefile (targets, `define`), Meson (targets, tests, subdirs). Keys of YAML and JSON come from the key outline. HTML (headings, elements with an `id`, `main`, `nav`, `section` and similar), PHP (namespaces, classes, interfaces, traits, enums, functions, methods), CSS (rules and at-rules), C# (namespaces, types, methods, constructors, properties), Markdown (`#` headings).
- Comments and strings are not read. What the rules do not recognise is skipped.
- A file larger than the limit of the plugin, or with more symbols than the outline holds, is shown cut short and the message says so. A large file opened in the paged view is read in the background from its start up to the limit; a jump to a symbol scrolls the paged view to it.

## [edit.carets] Multiple carets
commands: nks.edit.addNextOccurrence, nks.edit.skipOccurrence, nks.edit.selectAllOccurrences, nks.edit.addCaretAbove, nks.edit.addCaretBelow, nks.edit.clearCarets
1. Select a word; Ctrl+D adds its next occurrence to the selection. Repeat for further occurrences.
2. Ctrl+K then Ctrl+D skips the last occurrence and takes the next one.
3. Ctrl+Shift+L selects all occurrences at once.
4. Ctrl+Alt+Up arrow and Ctrl+Alt+Down arrow add a caret on the line above and below.
5. Typing changes all places at once. Esc keeps a single caret.
- The status bar shows the number of carets.
- Alt+drag with the mouse creates a caret on every line or a block selection, depending on the setting Alt+drag.
- With an input method (Japanese, Chinese, Korean) the text being composed is shown underlined at every caret. When you confirm it, it is written at all of them as one Undo step; Esc cancels it and keeps the carets.
- A large file in paged mode has multiple carets too, with the same keys and at most 1000 of them (topic Large files). Alt+drag does not work there.

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
- In a large file the bar shows the progress in percent and the Cancel button stops the search. List puts the matches in the Search Results tab; when there are more matches than it lists, the tab says so.
- When you turn on the regular expression, a Try button appears (topic Regular expression tester).

## [search.replace] Replacing
commands: nks.search.replace, nks.search.replaceNext, nks.search.replaceAll
1. Press Ctrl+H, or choose Search and then Replace. The bar has an extra box Replace with.
2. Type the text to find and the replacement.
3. Enter in the replacement box replaces one match, Ctrl+Alt+Enter replaces all. The buttons Replace and All do the same.
4. The status bar says how many matches were replaced, or Nothing was replaced.
- In a regular expression replacement $1, $& and the case conversion \U \L \E \u \l work.
- Replace Next and Replace All are also commands (Command palette). A macro can record them (topic Macros).
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
- While the caret stands next to a bracket and nothing is selected, the bracket and its partner are colored. Brackets in strings and comments do not count. Highlight matching brackets in the settings turns it off.
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
- The size has no fixed upper limit: it grows as long as at least one line fits in the view, and the minimum is 6 points. It is stored in the setting Font size.
- Ctrl+wheel changes the size in text, Ctrl+Shift+wheel in hex.

## [view.display] Hidden characters, line numbers and minimap
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
1. Choose View and then Show Hidden Characters (Ctrl+K, Ctrl+W): spaces, tabs and the line endings CR, LF and CRLF are drawn. The document is not changed.
2. Show Line Numbers (Ctrl+K, Ctrl+N), Highlight Current Line, Show Right Margin (a vertical line at column 80, set with the setting Right margin column) and Clickable Links switch the rest on and off.
3. Show Minimap (Ctrl+K, Ctrl+M) shows a reduced picture of the text at the right edge in the colors of the highlighting, a log in the colors of its levels. Click or drag it to scroll. A long file in the editor shows only the lines around the screen.
- In a large file (paged mode) the picture is sampled: short runs of lines from the starts of equal parts of the whole file, so the file is never read whole. A thin line shows the position instead of a rectangle. While the lines are being indexed, the picture covers the lines counted so far, as the scroll bar does. The number of lines in a run is a placeholder, not a decided value.
- Matches of the find bar are marked in the narrow strip at the right edge of the minimap. The lines that the filter of a live log view keeps are marked there too: error lines red, warnings yellow, the others blue.
- Links open with Ctrl+click and are not available in paged mode.
- A line longer than 50,000 characters is never wrapped: Toggle Word Wrap then says so in the status bar. The paged mode never wraps.
- Characters the main font lacks are drawn with the fonts of the setting Fallback fonts.
- Ctrl and the mouse wheel change the font size like the zoom commands.

## [view.secrets] Hidden values in .env files
commands: nks.view.toggleSecrets
1. Files named `.env`, `.env.*` or `*.env` show every value of a variable as `••••`, in the text editor, in paged mode and in the hex view. View, Show Hidden Values shows them in the current tab, and using it again hides them.
- In the text editor, resting the pointer on a value shows just that one.
- The text of the file is not changed: saving writes the same bytes.
- Hidden values are also hidden in the minimap, in the previews of Go to Anything and Edit Locations, in search results and in the exports with formatting (HTML, RTF). Copy puts `••••` on the clipboard in place of a hidden value, and Cut does not delete one.
- In the hex view a hidden value is a dot for each byte in both columns. Copy, Copy as, Export, the data inspector, the histogram, the structure panel, the byte map and the checksum see `*` for those bytes, and the status line says the value is hidden. The previews of Replace and of the unsaved changes show stars for a range that touches a value.
- In paged mode a screen reader reads a bullet for each character of a hidden value. A line longer than 16 KiB hides everything beyond its first part when it holds a value.
- A plugin reads the character `*` in place of every hidden value. Before a built-in command runs on such a file, the program asks whether it may read the values.
- The setting Secret files holds the name masks of the files whose values are hidden, separated by semicolons (`*` stands for any text). Its default list is provisional.

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
1. Select a file or a folder in the Files panel (topic Files panel).
2. Right-click it, or run Duplicate, Rename, Move or Move to Recycle Bin or Trash in the palette.
3. Answer what the command asks: the new name, the target folder, or the confirmation of the delete.
- Duplicate makes a copy next to the original.
- Rename (F2): type the new name. Open tabs and the lists of places follow it.
- Move: type the target folder.
- Move to Recycle Bin or Trash (Delete): the program asks, and the file goes to the bin, from where you can restore it. If it is open with unsaved changes, the tab stays open and keeps them.
- If an operation fails, a bar above the document gives the reason and says Nothing was changed.
- When an operation fails, fix the reason the bar names and repeat the operation (topic Error messages: Files panel).

## [files.refresh] Refresh and reveal a file
commands: nks.files.refresh, nks.files.reveal
1. Press F5 in the Files panel, or run Refresh Files in the palette. The tree is read again from the disk.
2. To see the file of the active tab in the tree, run Show Active File in Panel.
- Refresh Files (F5 in the panel) reads the tree again from disk.
- Show Active File in Panel opens the panel and selects the file of the active tab. It works with an open folder and an open document.
- Refresh Files needs an open folder; Show Active File in Panel needs an open folder and an open document. Without them the commands are dimmed.

## [workspace.projects] Projects
commands: nks.project.save, nks.project.open, nks.project.addFolder, nks.project.delete, nks.project.settings
A project is a named set of folders with the files that were open in them. It is kept in the profile and can be switched like a folder.
1. Open the folder of the project (File and then Open Folder) and, if the project has more folders, add them with Add Folder to Panel.
2. Open the files you want to find again, choose File, Projects and Sessions, Save Project and type a name.
3. To come back, choose File, Projects and Sessions, Open Project and pick the project.
- Add Folder to Panel shows one more folder next to the ones in the panel. Each folder is a top row of the tree.
- Save Project (File, Projects and Sessions) stores the folders of the panel and the open files that lie in them under a name. A project of the same name is replaced.
- Open Project shows the folders in the panel and opens the files the project had open. A folder or a file that is gone is named in the status bar; the others open.
- Switching to another project keeps the tabs that are open. The project you leave remembers its own files.
- Delete Project removes only the entry. Folders and files stay.
- Edit Project Settings opens the settings file of the active project (same format as the user settings). Its values apply while the project is active, after the project is opened again. Go to Anything searches the first folder only.
- Open a folder first: a project is made of the folders in the panel. Choose File and then Open Folder, then Save Project.
- None of the folders of project name exists. The folders were moved or deleted. Delete the project with Delete Project, or restore the folders.
- Project name opened. Not found: names. The project opened; the folders and files named there are gone from the disk, the others opened.
- The settings of project name cannot be read: reason. The values of the project are not applied until the file can be read. Open it with Edit Project Settings and fix it.
- No project is open. Open or save a project first. Edit Project Settings works for the active project only.

## [workspace.favorites] Favorites
commands: nks.favorite.add, nks.favorite.open, nks.favorite.remove
1. Select a file or a folder in the Files panel, or keep a file open in a tab.
2. Choose File, Projects and Sessions, Add to Favorites.
3. To come back to it, choose Favorites in the same menu and pick the entry; Remove from Favorites removes one.
- Add to Favorites stores the selected row of the file panel, else the active file, else the folder of the panel. It is also in the context menu of the panel.
- Favorites lists them (also from the empty panel). Enter opens a folder in the panel and a file in a tab. A path that is gone is shown dimmed with Not found.
- Remove from Favorites lists them and removes the one you choose.
- Nothing to add: open a file or a folder first. The command takes the selected row of the Files panel, else the active file, else the folder of the panel.
- Already in favorites: path. The entry is in the list already; nothing changed.

## [workspace.sessions] Named sessions
commands: nks.session.save, nks.session.open, nks.session.delete
1. Open the tabs you want to keep together.
2. Choose File, Projects and Sessions, Save Session and type a name.
3. Later choose File, Projects and Sessions, Open Session and pick it; Delete Session removes one.
- Save Session stores the open tabs under a name: groups, order, the active tab and the selection of each tab. A tab with unsaved changes, or without a file, keeps its text. A session of the same name is replaced.
- Open Session opens the tabs next to the ones already open. Files are read from disk, then the unsaved text of the session replaces their text, so you can still undo or discard it. A file that is gone is named in the status bar; its text, if the session had it, opens in an unnamed document.
- Delete Session asks first, because the session may hold unsaved text.
- A session file that cannot be read is skipped and named in a bar; the other sessions work.
- Large files opened in the paged mode are stored by path only.
- There are no open tabs to save. A session keeps tabs with a file or with unsaved text; a list of search results, a tab of a plugin and a text from a source are not stored.
- Session name opened. Not found: names. The files named there are gone; the others opened.

## [format.encoding] Encoding
commands: nks.format.reread, nks.format.convert
The status bar shows the encoding; clicking it opens the same choice. If the encoding is a guess, the percentage of certainty is shown with it.
1. Choose Format and then Convert to Encoding. To read the file again in another encoding choose Reopen with Encoding in File, Open Special.
2. Choose the encoding in the list. An encoding that would lose characters is marked lossy, with the number of characters.
3. Check the window Byte Changes and confirm it (topic Byte inspector). The status bar says Converted to encoding.
- Reopen with Encoding: the file is read again from disk in another encoding. Use it when wrong letters appear after opening.
- Convert to Encoding: the document is converted to another encoding. Which characters would be lost is shown in the list (lossy and a count) and in a window with the line, column and byte of each character. Replace and Convert replaces them with a question mark.
- Default list of encodings in the settings: UTF-8 (with and without BOM), UTF-16 LE and BE, Windows-1250, Windows-1252, ISO-8859-2.
- For a large file in paged mode both choices are not available.
- N characters cannot be written in encoding: characters. Choose another encoding (for example UTF-8), or Replace and Convert, which writes a question mark in place of each of them.
- Conversion to encoding failed. The document was not converted, for example because it changed while the window was open. Run the command again.

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
1. Run an action that rewrites bytes, for example Format and then Convert to Encoding or Apply EditorConfig Encoding and Line Endings.
2. Read the window Byte Changes: the changed ranges, the sizes before and after, the encoding and the line endings.
3. Confirm to write the change, or cancel; nothing is written before you confirm.
- The window gives the number of changed ranges and bytes, the size before and after, the encoding and the line endings before and after, and one line per range: where it is, the old and the new length and the first bytes (up to 500 lines; the rest is counted).
- When the target encoding cannot hold some characters, the same window lists each one with its line, column and byte, and offers three answers: cancel (nothing is written), change the encoding, or replace each with a question mark.
- Apply EditorConfig Encoding and Line Endings in the Format menu converts the file to the charset and line ending its .editorconfig sets; each step opens the inspector. A property the file does not set is left alone.
- The ranges are those of the revision you looked at; if the document changed meanwhile, the action is refused.
- File and then Changes Before Saving shows the same list for the next save.
- No byte changes; only the encoding label does. Confirming changes the label only; the bytes of the file stay.
- EditorConfig sets no encoding or line ending for this file. There is nothing to apply. Set charset or end_of_line in the .editorconfig of the folder, or choose the encoding and the line endings by hand.
- The file already matches EditorConfig. There is nothing to change.

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

## [settings.backup] Back up the configuration
commands: nks.settings.backup, nks.settings.restore
1. Choose Settings and then Back Up Configuration and name the file. The ZIP file has the date in its name. It holds the settings, keyboard shortcuts, user variables, external tools, save actions, log views, macros, recipes, clips and data plugins, workspaces, remote connections and the proxy.
2. On another computer or after a reinstall, choose Settings and then Restore Configuration and choose the file. The files come back at the next start: choose Quit and Restore, or At Next Start.
- A backup holds no passwords or tokens. Passwords stay in the credential store of the operating system. A value in the files that holds a saved password, or whose name says it is a password, token or key, is left out, and the status bar says how many. After a restore, enter the passwords again (Remote Connections and then Set Password).
- A restore replaces only the files that are in the backup. The configuration from before is saved first in the folder config-backups of the profile.
- Not in a backup: the journal, the local history, the backups of files, the copies of remote files, sessions, logs and installed plugins.
- A backup written by a newer version of the program is refused.

## [view.output] Output window
commands: nks.view.toggleOutput
The output window is a strip below the editor that tasks and searches write to. It is hidden by default.
1. Press Ctrl+Shift+Y, or choose View and then Output.
2. The tabs Tasks and Search switch the channel. The summary of a search in files is also written to the Search channel.
3. The Clear button empties the channel, Close (or Esc) hides the window.
- The text can be selected and copied. An empty channel says Nothing has been written to this output yet.
- The output is read from files in the profile, so it stays visible after the program that wrote it crashed.

## [view.pluginPanels] Plugin panels
commands: nks.view.pluginPanels
A plugin panel is a list or a tree that a plugin sends to the editor. The editor shows it in the left column.
1. Choose View and then Plugin Panels, or find the command in the palette. The list shows the panels the plugins have now, each with the name of its plugin.
2. Select a panel and press Enter. It opens with its first item selected.
3. Up and Down arrow walk through the items, Right and Left arrow expand and collapse a branch. Enter or a double click opens an item.
4. Esc in the panel closes it and takes you back to the text. While you type in the text, the panel stays open.
- A plugin that has not started yet but lists a panel (activation event onPanel) is in the list too, with the line "Starts the plugin, which then shows the panel". Choosing it starts the plugin; the panel opens as soon as the plugin sends it.
- When the plugin refreshes the items, the selected item stays selected.
- Cancel in the status bar cancels a task of a plugin. The row then says that the task is being cancelled. If the plugin does not end the task soon, the editor ends it and frees its place.
- When the plugin process stops, its panels, its status bar item and its tasks disappear.

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
- Ctrl+F opens the find bar in byte mode (marked Hex, without the text switches). Enter bytes in hexadecimal (50 4B ?? 04, ?? is any byte), text in quotes, a number with a type (0x1F40 i32) or a regular expression between slashes. While you type, the counter says how long a match may be, or why the entry is refused. Enter finds the next match, Shift+Enter the previous one, Esc closes the bar. Switching to a tab that is not hex (or the other way) closes the bar. Ctrl+H still opens the replace windows.
- F3 and Shift+F3 find the next and previous match, with the bar open or closed. The status bar and the counter of the bar say Match 1 of 4, or At least N matches when the search hit its limit.
- Ctrl+G opens Go to offset. Notation: 0x1F40, +0x100 (from the caret), end-512, 50%, 4K, sel (start of the selection), val (the four-byte value under the caret).
- Not found: there is no match. Invalid pattern or Invalid expression: correct the entry. The offset lies outside the file: enter a smaller value. The file changed on disk meanwhile, the search result was dropped: repeat the search.
- The wildcard *: 50 4B * 04 stands for 0 to N bytes between 4B and 04. N is the setting Longest wildcard * in hex search; while it is 0 (not chosen) the built-in default of 256 bytes (a provisional value) is used. *{2,8} writes a range of your own and needs no setting.
- A pattern between slashes is a regular expression over bytes, for example /\x50\x4B[\x01-\x08]/. A byte is a character 0 to 255, . is any one byte (also 0A and 0D), \xHH is a byte and [...] a set of bytes. Groups, |, ? * + {n,m} and \n \r \t \0 \d \w \s work; ^ and $ are the start and the end of the file (of the selection in Replace); \b, back references and look-around do not, because bytes have no lines or words. An open repeat (.*, +, {n,}) reaches at most N bytes. A match needs at least one byte, and matches may overlap.
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
1. Open a file in hex (topic Hex: opening and moving).
2. Choose View, Hex and then Hex Layout: Bytes per Row, Grouping or Character Column Encoding, or click the item in the status bar.
3. Pick the value; the grid is drawn again, and the next hex view remembers the choice.
- Character column encoding: the encoding in which bytes are shown as characters.
- Layout: bytes per row (8, 16, 24, 32, 48, 64), grouping of bytes and the offset format. Each size also has its own command.
- Ctrl+wheel over the hex adds or removes one block of bytes per row (one group, at least 8) and the row with the caret stays in place. There is no fixed limit, only the window width: when the next block does not fit, the wheel does nothing.
- Fit window width: the bytes per row follow the window width, rounded down to a multiple of the group. Choosing a size by hand or Ctrl+wheel turns it off.
- Ctrl+Shift+wheel changes the font size in hex too, like Ctrl+=, Ctrl+- and Ctrl+0.
- Toggle offsets between decimal and hexadecimal.
- The next hex view remembers the choices.
- Outside the hex view the commands are dimmed.

## [hex.save] Hex: saving and changes before saving
commands: nks.hex.inspectChanges
1. Ctrl+S saves the hex. Saving runs in the background and the window stays usable; Esc cancels the save before the data is written (Saving cancelled, the file was left unchanged).
2. File and then Changes Before Saving shows what the save will write: the changed ranges with old and new bytes and the way of writing.
- If only byte values change, the save is done in place (Saved in place, N B changed) and the original bytes are first written to the journal so a save interrupted by a crash can be finished or rolled back.
- If the length changes, a new file is written and swapped in atomically.
- Before a smaller file is overwritten a backup is made; if it fails, the file stays unchanged.

## [hex.copyAs] Hex: copy, paste and export as
commands: nks.hex.copyAs, nks.hex.pasteAs, nks.hex.exportSelection
1. Edit and then Hex and then Hex: Copy Selection As puts the selected bytes on the clipboard as text: hex string, an array for C, C#, Java, Python, Rust, Go, JavaScript or Pascal, Base64, Intel HEX, Motorola S-record or a hexdump.
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
1. Hex: Checksums and Hashes calculates over the selection, or over the whole file when nothing is selected: CRC-16, CRC-32, CRC-64 in several variants, a CRC with your own polynomial, Adler-32, Sum-8/16/32, xxHash32, xxHash64, XXH3-64, XXH3-128, MD5, SHA-1, SHA-256, SHA-384 and SHA-512. Tools, Text and Calculations, Calculate Hash offers the same list for the text view.
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
commands: nks.hex.template, nks.hex.template.open
The structure panel reads the file through a template and shows its fields as a tree. Selecting a field selects its bytes in the grid, and moving the caret in the grid selects the field that holds it.
1. Hex: Structure Template (Ctrl+J) opens the panel, or closes it when it already has the focus. The template that fits the file is chosen by its magic number, then by the file extension. The list above the tree lets you pick another one; the choice is kept for that file extension (setting hex.structureTemplates, written extension=template).
2. A field with parts (a structure or a list) is read when you open it, so a large file opens at once. A long list is shown as ranges (items[0..255]) that open into their elements, and a list whose elements all have the same size, such as the triangles of a 3D mesh, can have millions of elements and still opens at once. Each row shows the name, the value and, in grey, the offset and the length in bytes. Up and Down move through the rows, Right and Left open and close a field, Enter puts the focus back on the grid with the bytes of the field selected, Esc puts it back without selecting.
3. A field that cannot be read is shown in red with the reason, for example that it runs past the end of the data or differs from the expected bytes. The fields behind it are not shown, because their place is unknown.
4. Hex: Open Structure Template File asks for a .ksy file (the button under the list does the same) and offers its template in the list. A template that cannot be used is named in the note under the list with its line, for example "not supported: process". Templates opened this way stay in the list until the program ends.
- Shipped templates: BMP header (with the size of a row), PNG chunks (IHDR, gAMA, tEXt), ZIP archive sections, Windows PE headers, ELF headers (32 and 64 bit, both byte orders, dynamic section) and binary STL. The file pinned.yaml next to the templates lists each one with its version, its licence and the constructs it uses. They were written for Textida and are MIT like the program; no template of another project is copied. The panel shows the file, version and licence of the template in use.
- Templates are a subset of the Kaitai Struct .ksy format: integers (u1 to u8, s1 to s8), floats (f4, f8), bit-sized integers (b1 to b64; one after another, not repeated), strings (str, strz), raw bytes, fixed bytes (contents), enums (also inside a type), nested types, size, size-eos, repeat (eos, expr or until), if, switch-on with cases (key _ is the default), instances with pos (and io: _root._io) or value, meta endian (also with switch-on and cases) and bit-endian, and expressions. Expressions are allowed in size, repeat-expr, repeat-until, if, pos, io, value and switch-on. They compute 64-bit integers, floats, true/false and strings: + - * / %, << >> & | ^ ~, < <= > >= == !=, and or not, a ? b : c, brackets, numbers (decimal, 0x, 0b, and floats such as 1.5 or 2.5e-3), quoted strings, names of earlier fields and instances, a.b, list[i], enum::member and type::enum::member, _parent, _root, _index, _ (the last element in repeat-until), _io.pos, _io.size, _io.eof, .as<type> (no conversion), .to_i of a string, a float or a boolean, .length of a string, .size, .first and .last of a list. A float is never a size or a count (use .to_i), and % and the bit operators take integers only. pos counts from the start of the structure's stream (a structure with size is a stream of its own). Array literals, other methods, process (a xor or zlib step on the bytes) and everything else not listed are refused with the template line, for example "not supported: process". A division by zero, a missing field or a wrong kind of value gives an error on that field; a huge size or count ends in a limit error. Instances are listed after the fields.
- Reading stops with a message when a template nests too deep, repeats one field too often, reads a string or an amount of work above a fixed limit, or takes too long for one node.

## [hex.compare] Hex: binary comparison
commands: nks.hex.compare.file, nks.hex.compare.saved
Two hex grids side by side, scrolling together, with the differences tinted and listed below.
1. Hex: Compare with File asks for a file and compares it with the document of the active tab (with its unsaved changes). Hex: Compare with Saved File compares the document with its own file on disk, so you see exactly what the next save would change.
2. Aligned (the default) looks for bytes that one file has inserted or deleted, so an inserted block shows as an insertion and the bytes behind it are not reported as changed. Byte by byte compares every offset with the same offset in the other file, which is what you want for files with a fixed layout. It is used for any file over 256 MiB, which the aligned mode does not read.
3. The list names each difference: changed, inserted or deleted, with its range in the left and in the right file. Enter on a row, F8 and Shift+F8 (or the buttons) go to the next and previous difference in both grids.
- Scrolling one grid scrolls the other to the same place in the other file; with inserted bytes that is the corresponding place, not the same offset.
- The grids are read-only. The map beside each grid marks the differences.
- The list stops at 1,000,000 differences (the window says so); two unrelated files would otherwise fill the memory.
- Two revisions of a file from the local history cannot be compared in hex. Compare in the local history compares text (topic Local history).

## [hex.access] Hex: keyboard and screen reader
commands: nks.hex.toggleColumn, nks.hex.selectTo, nks.hex.minimap.focus
Everything in hex can be done from the keyboard. This topic lists what is not obvious and what a screen reader gets. It has been tested through the accessibility tree of the program, not yet with NVDA or VoiceOver themselves.
### Keyboard
- Arrows move by a byte or a row, Ctrl+Left and Ctrl+Right by a group, PageUp and PageDown by a screen, Home and End within the row, Ctrl+Home and Ctrl+End through the file. Shift extends the selection. Ctrl+A selects everything.
- Tab switches between the byte column and the character column and stays in the grid (Hex: Switch Between Hex and Character Column does the same from the palette). F6 leaves the grid for the next part of the window, Shift+F6 goes back. In a panel beside the grid Tab moves between its controls and Esc returns to the grid.
- Ctrl+G goes to an offset. Hex: Select to Offset (Ctrl+K then Ctrl+A) selects from the caret to an offset: +0x100 selects 256 bytes, end selects to the end of the file. Ctrl+F finds bytes and F3 goes on.
- The Insert key switches between overwrite and insert (⌥⌘I on macOS). Every hex action is in the command palette (Ctrl+Shift+P) and, if it has one, in a menu.
- The panels open from the keyboard: Data Inspector with Ctrl+K then Ctrl+I, Bookmarks with Ctrl+K then Ctrl+B, Structure Template with Ctrl+J, File Map with Ctrl+K then Ctrl+G. Ctrl+K then Ctrl+F puts the keyboard on the map: Up and Down move the caret by a screen, Page Up and Page Down by a tenth of the file, Home and End to the ends, Esc back to the grid.
### Screen reader
- The grid is one editable element. Its value is the byte under the caret: the offset, the byte in hexadecimal, its value in decimal and the character; a selection adds its length and its first and last offset, a byte changed since the last save says changed. Its description gives the row, the position in the row, the column and the editing mode.
- A hidden value of a .env file is read as a hidden value, never as bytes.
- A change of the mode or the column, the result of a search and an edit that was refused because the view is read-only are announced without the keyboard moving.
- The map is a slider whose value is the place of the grid in the file in percent. Its description says what the cells show and how many matches, bookmarks and changed places it marks.
### High contrast
- Changed bytes are tinted and underlined, a bookmark has a bar over it and the map keeps its frame. In high contrast the byte classes are told by more than colour: 00 is underlined, FF is bold and a control byte is italic.

## [tools.run] Tools: run an external program
commands: nks.tools.run, nks.tools.stop
1. Tools, then Running Tools, then Run Tool lists the tools written in the file tools.json in the profile folder. The last row opens that file; the first time it writes an empty one for you to fill in.
2. A tool has an id, a title, an executable and args (a list: one entry is one argument). Optional are cwd (the directory it runs in; the folder of the current file when missing), timeoutSeconds (without it there is no time limit) and logPattern (see LOG pattern). Example: {"externalTools": [{"id": "build", "title": "Build", "executable": "dotnet", "args": ["build", "${File}"], "cwd": "${FileDir}", "timeoutSeconds": 120}]}
3. In executable, args and cwd the variables ${File} (full path), ${FileDir}, ${FileName}, ${FileBase}, ${FileExt}, ${Line} and ${Column} stand for the current document; ${Selection} is the selected text (it may span lines), ${Word} the word under the caret and ${ProjectRoot} the nearest folder at or above the file that has .textida or .git; $$ is a dollar sign. An argument stays one argument whatever the value holds, spaces and quotes included: no shell is involved. An unknown variable, or one that does not apply (${File} for a document that has no file, ${Selection} with nothing selected, ${Word} outside a word), stops the run before anything starts. The variables of clips and recipes work here too (file.path, date, datetime, selection, user.name and your own variables), with the same value as there. Your own variable of the same name comes before ${File} and the others as well. When the file of your own variables cannot be read, the tool does not start and the output says why.
4. A tool whose command and directory have not run before is shown with both and starts only after you confirm. The confirmation is remembered until the definition, or the program it leads to, changes.
5. The output goes line by line to the Tasks tab of the output window, followed by the exit code and the time. Stop in that window, or Stop Running Tool, ends the program and everything it started; so does the time limit.
- Batch files (.bat, .cmd) are started through cmd.exe with every argument quoted for it, so spaces, quotes and characters such as & | < > ^ % pass through unchanged. An argument with a line break or another control character is refused before the start, and so is one with ! when the batch file turns delayed expansion on.
- A program name without a path is looked for in PATH only, never in the working directory.
- Output is read as UTF-8 unless the tool names another encoding ("outputEncoding": "windows-1250") or the setting Default output encoding of tools is set. A tool without timeoutSeconds gets the setting Default time limit of tools (0: no limit). One tool runs at a time.

## [tools.logPattern] LOG pattern: click an error in the output
commands: nks.tools.logPattern
1. LOG Pattern for the Output opens a tester with the latest output of the tools as the sample. Each line of the sample shows the file, line and column the pattern found in it, or that it found nothing.
2. The mask is matched from the left and without regard to case. * skips any text up to the next literal part of the mask. %F is the file name, up to the next literal part. %D is a drive (C: followed by a slash) in front of it. %L is the line and %C the column (digits). %S skips spaces and tabs. %E ends the mask. A mask needs %L; without %F it points to the file that was open. Examples: %F:%L:%C:* for main.c:12:5: error, *] %F(%L) for [Error] a.pas(346): text.
3. The other language is a regular expression with the groups line (needed), file and column.
4. Use for the output makes the pattern work for the lines in the output window: a click on a line, or Enter, opens the file at that line. A relative file name is looked for in the directory the tool ran in, then in the folder of the file that was open when it started, then among the open documents by name.
- A tool can bring its own pattern: "logPatterns": [{"id": "gcc", "title": "gcc", "syntax": "mask", "pattern": "%F:%L:%C:*"}] in tools.json and "logPattern": "gcc" in the tool. Without "syntax" the pattern is a regular expression.

## [tools.edit] Tools: edit definitions, typed variables, tools from a folder
commands: nks.tools.edit
1. Tools, Running Tools, Edit Tool Definitions opens one editor for the tools of the file tools.json. New, Save and Delete write the file at once. The line under the form shows the command exactly as it would start for the open file, or why it cannot run now. The editor and Run Tool use the same definition, so a tool starts with the same arguments from both.
2. A definition can declare variables, one per line: Name:text or Name:path, optionally followed by =value (the value may use other variables, for example Out:path=${FileDir}/out). Without a value the user variable or the built-in variable of that name is used. The order is: declared value, then the variables of the project (Edit Project Variables), then your own variables (Edit User Variables), then the built-in ones. Use the name as ${Name} in executable, args and cwd.
3. A text is one line. A path is one line, not empty, without the characters * ? " < > | . A value of the wrong type stops the run before anything starts and the output names the variable.
4. The folder of the open file, or a folder above it, may hold .textida/tools.json with tools of the same shape. They are listed in Run Tool, but they run only after you confirm the command and the directory, even when the same text is in your own file. A definition that changes, including its variables, is asked again. These tools cannot be edited in the editor.
- An empty argument cannot be written in the editor. Saving drops comments in tools.json.
- The field Output encoding names the encoding the output is read in (utf-8, windows-1250, ibm852); a name the system does not know is refused. Empty: the default of the settings.

## [tools.make] Makefile: run a target
commands: nks.tools.makeTargets, nks.tools.makeRepeat
1. Tools, Running Tools, Run Makefile Target lists the targets of the Makefile for the open file: the file itself when it is a Makefile (Makefile, GNUmakefile, *.mk, *.mak), otherwise the nearest GNUmakefile, makefile or Makefile in its folder or the folders above it. The search stops at the root of the project (a folder with .textida or .git). The list is read from the text of the tab when the Makefile is open. Choose one to run make -f with that file and the target, in the folder of the Makefile.
2. Repeat Last Makefile Target runs the target started last once more.
3. The run goes through the tool runner: the command is shown and has to be confirmed the first time, the output goes to the Tasks tab, Stop ends make and what it started.
4. A line of the output that names a file and a line (main.c:12:5: error, Makefile:7:) opens that place on a click or Enter. A recursive make says "Entering directory" and "Leaving directory"; a relative file name after such a line is looked for in that folder.
- Make has to be installed and found in PATH, or named in the setting Make program (for example mingw32-make). Special targets (.PHONY), pattern rules and names with a variable or wildcard are not listed. The confirmation is tied to the command and to the content of the Makefile on disk (the start of its SHA-256 is shown in the question): when the file changes, the question comes again, and the old answer applies again if the bytes return to what they were. Files that the Makefile includes are not covered, and neither is a change made after the question. The text of an unsaved tab is not what make runs.

## [packages.registry] Package manifests: names, versions and newer-version marks
commands: nks.packages.lookup, nks.packages.refresh, nks.packages.revoke
1. In package.json, composer.json, requirements.txt, a .csproj file, go.mod, Cargo.toml, Gemfile and pubspec.yaml the editor can ask the package registry (npm, Packagist, PyPI, NuGet, Go proxy, crates.io, RubyGems, pub.dev) for names and versions. Nothing is sent until you agree: Tools, Packages, Check Package Versions shows the servers and asks once for each registry.
2. After you agree, Ctrl+J in the name of an entry offers names from the registry, and in a version it offers the versions of that package, newest first. PyPI and the Go proxy have no search: a name is offered only when the typed text is the whole name.
3. An entry that pins an exact version (1.2.3, ==1.2.3, v1.2.3) and has a newer release gets an information mark on the version. Ranges (^, ~, >=) are not marked. A pre-release is not taken as the newer version while a release exists. The check runs when you stop typing.
4. Answers are kept in a cache in memory, up to a size limit (the oldest go first) and for a limited time, and no longer than the editor runs. An offer or a mark that came from the cache says so ("cache"). Refresh Package Versions drops the cache of that registry and asks again.
5. Forbid Registry Lookups and Schema Downloads takes every consent back and drops the cache. The network proxy of the Network Proxy command is used; a proxy with a user name cannot be used, and the lookups stop then.
- Nothing is downloaded or installed, vulnerabilities are not looked up and go mod download is never run. Only package names, the typed text and your network address reach the registry.
- A registry that fails gives no offers; Check Package Versions names the error in the status bar. The time limit, the size limit, the size and life of the cache and the number of packages per check are provisional.

## [tools.schema] Schemas for JSON, YAML and TOML: check, completion and descriptions
commands: nks.schemas.allow
1. A JSON, JSONC, TOML or YAML file is checked against the schema that fits it. The file can name its schema itself ("$schema" at the top of a JSON file, the comment "# yaml-language-server: $schema=ADDRESS" in YAML, "#:schema ADDRESS" in TOML); otherwise the file name decides (package.json, Cargo.toml, GitHub workflows, the files of Claude Code, MCP and Codex, and a short list of common configuration files). The status bar shows the name of the schema or "no schema". A finding is underlined and its tip names the place, with line and column for a syntax error.
2. Ctrl+J offers the keys the schema allows in the object around the caret and, where a value is written, the allowed values (enum values, true and false). Resting the pointer on a key or a value shows what the schema says about it: description, type, allowed values and default.
3. The schemas of package.json, Cargo.toml, GitHub workflows and the agent files come with the editor. The schema of any other known file, and a schema a file names by an address, is downloaded only after you agree. Until then the status bar says "download not allowed", and Tools, Packages, Allow Schema Download shows the server and asks once for each server. Forbid Registry Lookups and Schema Downloads takes every consent back. A schema a file names by a path is read from disk and needs no consent.
4. A downloaded schema is kept on this computer up to a size limit (the oldest are deleted first) and used again without a request while it is recent. When the server does not answer, the saved copy is used and the status bar says so. The network proxy of the Network Proxy command is used; a proxy with a user name cannot be used.
5. A syntax error in a JSON, JSONC, TOML or YAML file is underlined even when no schema applies. YAML with anchors, tags or complex keys cannot be read: such a file is not checked, and the status bar says so.
6. Your own rules: the setting Schema mappings (Settings, search "schema") maps files to schemas. Write MASK=SCHEMA, separated by semicolons or line breaks, for example *.cfg=package.json; config/**=https://example.test/a.json as yaml. MASK is a file name, the end of a path or a folder with /**. SCHEMA is the id of a known schema, an address or a file path; the ending as json, jsonc, toml, yaml or markdown sets how the file is read. The first rule that fits wins over the schemas the editor knows; only a $schema written in the file wins over a rule. A rule written differently is skipped and named in the status bar. A change applies to the open files at once.
- A subset of JSON Schema is understood: types, enum, required, properties, items, oneOf, anyOf, allOf, if/then, $ref and pattern. A $ref may name another schema file on this computer (relative to the schema file) or an address; an address is downloaded only after you agree to its server, as above, and a schema that came from the network never reads a file. A document is read once, so references that lead in a circle end; the length of the chain of documents and their number are limited. A reference that cannot be followed asks nothing; if only the consent for a server is missing, the status bar says "download not allowed". Anchors (#name) are not followed. The size limits, the life of the saved copies and the waiting times are provisional.

## [packages.scripts] Package scripts: npm and Composer
commands: nks.packages.script
1. With a package.json or composer.json open, Tools, Packages, Run Package Script lists its scripts. Choose one to run npm run name or composer run-script name in the folder of the file.
2. It goes through the tool runner like a tool from a project folder: the command, the directory and the file it comes from are shown and have to be confirmed. The confirmation is tied to the text of the script, so a changed script asks again. The output goes to the Tasks tab; Stop ends it.
- npm or Composer has to be installed. On Windows both are batch files; the editor starts Node with npm-cli.js, or PHP with composer.phar, from the folder of the batch file, so a script name with a line break or ! still works. Without that standard layout the batch file is started through cmd.exe.

## [plugins.manager] Plugin manager: install, update, roll back and remove plugins
commands: nks.plugins.manage
1. Tools, Plugin Manager lists the plugins that come with the editor and the ones you installed, with the permissions each holds. Choose Install plugin from ZIP to add one.
2. The ZIP is unpacked into a staging folder and checked before anything of it runs: every path has to stay inside the plugin folder, links are refused, size and file count are limited, the manifest nkspad-plugin.json has to sit in the root of the ZIP and be valid, and the id may not belong to a built-in plugin. A problem is named in its own message and nothing is installed.
3. Then the editor shows the permissions the plugin asks for and waits for your answer. Nothing of the plugin runs before you allow it. The permission to edit and create files below the workspace folders (workspace.files) is granted here, by you, like every other one. If the ZIP was downloaded (Mark of the Web on Windows) the dialog says so.
4. Choose an installed plugin to enable or disable it, update it from a newer ZIP, roll back to the previous version, change its permissions or uninstall it. A new version is unpacked beside the old one and becomes active only when it loads; the old one stays for the roll back. An update asks only about permissions you were never asked about; your earlier answers stay.
5. A permission you take back stops new requests of the plugin at once and cancels the ones waiting. A plugin without a permission gets an error, not an empty result. Disabling stops the process; uninstalling deletes only the folder of that plugin.
6. A command of an installed plugin that may read or edit documents works on the active tab, and the plugin sees that document only while the command runs. A change it proposes is shown in the change review and written only when you apply it. A direct edit (document.edit.direct) is one undo step. If the plugin stops before you decide, its proposal is dropped and nothing is written.
- Permissions are a record of what you allowed, not a sandbox: a plugin is a program that runs with your user rights. Install only plugins you trust. Signatures are not checked yet. The size and file count limits for a ZIP are provisional. The commands, menu items, keys and settings of a plugin appear when you install or switch it on and go when you switch it off or remove it; an update replaces them, no restart needed. A plugin with a process may also bring clips, converters and external tools; they run without starting the process.
- A plugin can bring keys and settings. Its keys (contributes.keybindings, with an optional "when") work like the keys of the editor, but a key of the editor, of the profile or your own keymap.json always wins over a plugin key; the clash is listed under the plugin in the Plugin Manager. Its settings (contributes.settings, keys that start with the plugin id) appear in Settings under the name of the plugin, with the default from the manifest, and a running plugin gets the values at its start and again as the event settings.changed. A command that names a tool in the manifest (contributes.externalTools) runs it without any plugin code, after you confirmed its command and directory.
- Every plugin runs as its own process. If it ends or stops answering, the editor ends it, a bar names the plugin and says when it starts again; after repeated crashes it stays turned off. Show output in the bar opens the Output window with the last error lines of the plugin. A plugin that waits for your answer (a change review, a question) is not ended for not answering. A command that was running is not repeated after the restart, so nothing is written or uploaded twice. The number of crashes, the waits and the time limits are provisional.

## [plugins.lifecycle] Plugin processes: when they start, how to see them and how they end
1. A plugin that runs a program starts only when something asks for it. What asks is its activation events in nkspad-plugin.json (main.activationEvents): onCommand:id (the command runs), onLanguage:id (a document in that language mode is shown), onFileType:.ext (a file with that extension is shown), workspaceContains:glob (an open folder holds a file that matches the glob), onStartup (the editor has started) and onPanel:id. Without activationEvents a plugin starts when one of its commands runs.
2. Tools, Plugin Manager shows the state of every plugin process: running, starting, not running (it starts when needed), stopped after a crash and waiting for its restart, or turned off. Choose a plugin to see the actions that fit: Start, Stop, Restart. Start on a plugin that was turned off clears its crashes and tries again; Stop on a plugin that waits for its restart cancels the restart.
3. Stop and Restart ask the plugin to leave, close its input and end the process if it has not gone within the grace time. A plugin that was stopped is not a crash: no notice appears and it starts again at its next activation event. A call that was running when it was stopped ends with an error and is not repeated.
4. A plugin may ask in its handshake to be ended when it has no work. The editor then ends its process after a quiet time, unless the plugin shows a panel, a task, a virtual tab or a source, or a call is running, and starts it again at the next activation event.
5. When you close the editor every plugin is asked to leave at the same time. Whatever is still alive after a short time is killed by its process id, so no plugin program stays behind.
- The glob of workspaceContains matches the name of a file at any depth when it has no slash, otherwise the end of the path below the folder; the search is limited in entries and depth and passes over .git and node_modules. The times (restart, quiet time, closing) and the limits of the search are provisional.

## [tools.lint] Linters: underline what PHPStan, Psalm, golangci-lint, Clippy, clang-tidy, RuboCop, ESLint, Ruff, ShellCheck, SQLFluff and dart analyze find
commands: nks.tools.lint
1. Tools, Running Tools, Check File with Linter runs the linter that suits the open file: PHPStan or Psalm for PHP, golangci-lint for Go, Clippy for Rust (it needs a Cargo.toml in the folder of the file or above it), clang-tidy for C and C++, RuboCop for Ruby, ESLint for JavaScript and TypeScript, Ruff for Python, ShellCheck for shell scripts, SQLFluff for SQL, dart analyze for Dart. If two linters handle the file, the first one whose configuration file (phpstan.neon, psalm.xml) is found is used, else the first one that is installed.
2. The linter checks the file on disk, so save first: a document with unsaved changes is not checked. The findings are underlined in the text (the message shows when the pointer rests on it) and listed in the Tasks tab, one per line; a click or Enter opens the place. The next run of the same linter replaces them, so a problem you fixed is gone after it. If you edit the file while a check runs, the findings move with the text and those whose text you changed are dropped.
3. To check after every save, switch on "Check with PHPStan" (or the other linter) in File, Save Actions, or write {"kind": "lint", "id": "phpstan"} in saveActions.json. A linter never runs on a change. The ids are phpstan, psalm, golangci-lint, clippy, clang-tidy, rubocop-lint, eslint, ruff, shellcheck, sqlfluff and dart-analyze. A check that a save starts waits half a second for a further save of the same file; the newer save replaces it.
- The program is looked for in the path you gave, then in vendor/bin, node_modules/.bin and .venv (from the folder of the file upwards) and in PATH; nothing is downloaded. You give a path in the saveActions.json of the profile: "toolPaths": {"phpstan": "C:\\tools\\phpstan.phar"} (a file, or the folder that holds the program). It works for formatters too, and a project file cannot set it. A path that does not exist is reported and not replaced by a search. The first run asks you to confirm the command.
- Without lintTimeoutSeconds in saveActions.json a linter has a provisional limit of 60 seconds; the final value is not decided yet. A linter that runs over the limit is stopped and reported; the editor stays usable meanwhile and the findings of the earlier run stay.
- PHPStan reports a line and no column, so the text of that line is underlined. A program that is a batch file (phpstan.bat, eslint.cmd) is started through cmd.exe, as for every tool.
- Only dart analyze was checked with the real program (Dart 3.12.2); the other linters are read by the documented format of their output.

## [file.saveActions] Save actions: format, run a tool, upload
commands: nks.file.saveActions
1. File, Save Actions lists what runs around a save: format, run a tool, upload to the server. A row switches the action on or off; the row under it switches "only on manual save". A formatter is offered only when its configuration file is in the folder of the file or above it, and it is off until you switch it on: gofmt (go.mod), rustfmt (rustfmt.toml), clang-format (.clang-format), RuboCop (.rubocop.yml), Prettier (.prettierrc), Biome (biome.json), Ruff (ruff.toml, pyproject.toml), shfmt (.editorconfig), SQLFluff (.sqlfluff), StyLua (stylua.toml), dart format (analysis_options.yaml).
2. The order is fixed: formatters first, then the save, then tools and the upload. A formatter works on a copy of the text and is stopped when the time limit runs out. If it times out, fails (a syntax error, for example) or is not installed, the file is saved as it is and the status bar and the Tasks tab say why. Text you typed while it ran is never overwritten. Only a document with unsaved changes is formatted.
3. The upload sends exactly the bytes this save wrote, never older or newer ones. If the file on disk changed after the save, nothing is sent. A file opened from a server goes back to that server; a file from the disk goes to the default server of its project (step 6).
4. An autosave runs the actions that are not marked "only on manual save". A save made by a plugin runs no action, so an action that saves cannot start a loop. Save As is a manual save: it runs the actions of the folder you save into.
5. The actions are written in saveActions.json in the profile (Open the save actions file) and, for a project, in .textida/saveActions.json in the folder of the file or above it. A project entry replaces the entry of the profile with the same kind and id. Example: {"formatTimeoutSeconds": 10, "actions": [{"kind": "format", "id": "gofmt"}, {"kind": "tool", "id": "lint", "onlyManual": true}, {"kind": "upload"}]}. A tool is one of Run Tool, by its id.
6. The default server of a project goes into the .textida/saveActions.json of the project: {"kind": "upload", "server": "Web", "remoteFolder": "/var/www/web"}. server is the name of a saved connection, remoteFolder is the folder on the server that matches the project folder (the one that holds .textida). The file src/a.php then goes to /var/www/web/src/a.php. Files in the .textida folder are not uploaded, and the profile cannot name a server.
- The project file comes with the project, so the first upload asks whether the files of the project may go to that server. The answer holds for this folder, this connection and this server folder. After an autosave nobody is asked and nothing is sent.
- The editor remembers the version the project last sent to the server. If the file on the server changed since then, or the project never sent it and it differs, nothing is overwritten: a manual save offers the differences or uploading anyway, after an autosave the status bar says so. A file that is the same on the server is only remembered.
- Without formatTimeoutSeconds a formatter has a provisional limit of 10 seconds; the final value is not decided yet. The value in the example is only an example. The program of a formatter is looked for in PATH, or taken from toolPaths in the saveActions.json of the profile, and nothing is downloaded.
- clang-format formats code that does not compile too and reports an error only for a broken configuration. dart format ends with code 65 for code it cannot read, and the file is saved unformatted.
- After an autosave nobody can answer: a tool that was never confirmed does not start, and a newer version on the server shows on the bar of the tab instead of a dialog.
- Switching an action in the list changes only that one value in saveActions.json of the profile, or adds the action at the end of the list. Comments and the rest of the file stay. Actions from a project are listed but changed in the project file.
- Save Actions also offers "Check with ..." for the linters that handle the file; it is off until you switch it on. The check runs after the save and does not hold it; its time limit is lintTimeoutSeconds (provisionally 60 seconds while it is missing).
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

## [log.live] Live log: following, saved views, rotation
commands: nks.log.follow, nks.log.saveView, nks.log.views, nks.log.nextMatch, nks.log.previousMatch
1. Log: Follow reads the file again whenever lines are added and puts the caret at its end. It works for a file of any size, also in paged mode. It needs a document saved as a file without unsaved changes; with unsaved changes it stops and the status line says so. Run the command again to stop.
2. Log: Save View asks for a name, a regular expression and the levels to keep (error, warning, info, debug; empty means all). The view is saved in logviews.json in the profile, together with the level rules of the document and the caret position, and becomes the live view of the document.
3. Log: Views lists the saved views. The chosen one sets the level rules, the filter and the caret position, and reads the file once to find the matching lines. When the view was saved for another file, the position is not used.
4. Log: Next Match of View and Log: Previous Match of View jump between the lines the filter keeps. As the file grows, only the added lines are examined; the part read before is not read again. A last line without its line ending is taken once the line ends.
5. If another program truncates the log, or replaces it (rotation: the file is renamed and a new one is created under the old name), the status line says so, the earlier matches are dropped, and reading starts again at the beginning of the new content. A missing file is reported once and waited for.
- A rotation is told from the first 4096 bytes of the file, from the 4096 bytes just before the point read so far and, on Windows, from its creation time. A new file that begins with the same bytes and is not shorter than the old one is told apart by the bytes before the point read, so this also works on macOS and Linux. A replaced file of the same length is looked at again only when its time of last write changed. A new file whose bytes up to the point read are all the same is not noticed.
- The file is looked at once per second. This and the 4096 bytes are not decided values.
- At most 1,000,000 matches of a view are kept in memory (placeholder limit, not decided). Beyond it the oldest matches are dropped, down to three quarters of the limit, and the status line says how many; the newest stay for Next Match and Previous Match.
- Log: Save View also asks for a text to highlight (a regular expression; empty marks nothing). When the view is applied, the matches of the expression get a background in every line, in the editor and in paged mode. Only the first 8192 characters of a line are searched, and a line whose search takes too long (placeholder limit, not decided) is left unmarked.

## [export.highlight] Export and copy with highlighting
commands: nks.file.exportHtml, nks.file.exportRtf, nks.edit.copyFormatted, nks.edit.copyHtmlSource
1. Edit, Copy As, Export as HTML and Export as RTF write the selection, or the whole document when nothing is selected, to a file you choose. The colors are those of the editor in the current light or dark theme: the same tokens of the grammar of the document, and in a log the colors of the levels. The page and text colors of the editor are included.
2. Edit, Copy As, Copy with Formatting puts the text, HTML and RTF on the clipboard together, so pasting into a word processor or a mail keeps the colors, while pasting into an editor gives plain text. Copy as HTML Source puts the HTML itself on the clipboard as text.
- The whole document is colored, then the selection is cut out of it, so a selection that starts inside a comment is colored as in the editor. With several carets or a block selection only the selected text is taken, and it is colored from its own start. In a large file (paged mode) the selection is colored as in the whole document: the text before it on its first line is read, and the colors at the start of that line are worked out by reading the file up to it, which for a selection deep in a very large file takes a while (the window stays usable, but the work cannot be cancelled). The line numbering must have reached the selection, otherwise a message asks you to try again. A part of a line longer than 32 MiB in front of the selection is not read, and the selection is colored from its own start. If the colors at the start of the line cannot be worked out (a line too long to be colored lies in front of it, topic Large files (paged mode)), the grammar is taken to start fresh at that line.
- A text over 8388608 characters is refused with a message; select a part of it. The limit is a placeholder, not a decided value. The work runs in the background, so the window stays usable.
- HTML is UTF-8. RTF is ASCII: backslash, braces and every character outside ASCII (for example Czech letters) are written as escapes.

## [log.full] Full log mode: folding, stack traces, heatmap, ANSI colors
commands: nks.log.fold, nks.log.unfold, nks.log.openFrame, nks.log.showRaw
1. Log: Fold Uninteresting Lines asks for a level (lines of at least that level stay: error, warning, info, debug) and a regular expression (lines that match stay). Each run of other lines becomes one placeholder row that a click opens. A line without a level word, such as a stack trace frame, goes with the entry above it. Log: Show All Lines opens everything again. In a paged file (very large) rows cannot be folded; the lines that would be folded are drawn dim instead.
2. Ctrl+click on a stack trace frame of .NET, Java, Python, PHP or Node.js opens the file at that line; Log: Open File of Stack Trace Frame does the same for the caret line. A relative path is looked for in the folder of the log, then in the workspace folders; a bare file name (Java) is searched below them.
3. The strip at the right edge of the minimap shows where error lines (red) and warning lines (yellow) are dense. It is counted in the background. A file up to 256 MiB is read whole; a larger one is sampled, so the picture is an estimate. These limits are not decided values.
4. ANSI escape sequences in a log are hidden and drawn as colors (16, 256 and true colors), bold and underline. The file is never changed, so saving keeps the escapes. Log: Show ANSI Escapes Raw switches between the colors and the plain sequences. A style that a line leaves open goes on into the next lines until a code changes it, as in a terminal; to find it the program looks back through a limited number of lines (a style that stays open for longer shows in the default style past that distance), and a line cut short for the display is read only as far as it is shown.
- No file and line of a stack trace under the caret. Put the caret on a line that holds a path and a line number of a frame, or Ctrl+click the frame.
- The file path was not found next to the log or in the workspace folders. Open the folder with the source (File and then Open Folder) or add it with Add Folder to Panel, then repeat the command.
- No line is hidden by this choice. Choose a higher level or another regular expression.
- Lines that would be folded are shown dim (a paged file cannot be folded). A log above the large file threshold cannot be folded (topic Large files (paged mode)).

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
- An interrupted batch of file changes (replacements in files, and the files a plugin's workspace edit creates, renames or deletes) is offered after the start: Finish or Roll back. A file that someone else changed meanwhile is left as it is.
### Large changes
- When the unsaved changes of one document are larger than the recovery limit (64 MiB, a provisional value), for example a Replace All or a big paste in a large file, its tab does not come back by itself. A bar names the file, the size of the changes and when they were made, and offers Restore changes and Discard changes. Until you choose, nothing is read from the journal and the file is not touched.
- Restore changes reads the journal in the background, so the program keeps working, and then opens the tab with the unsaved changes as after any other recovery. If that fails (the file changed meanwhile, a part of the journal is missing or damaged, there is not enough memory), the bar says why and the changes stay in the journal.
- Discard changes asks once more, because nothing brings the changes back; then they are deleted from the journal. The file on disk is not changed by either choice.
- Closing the bar decides nothing: the bar comes back at the next start.
- A Replace All with very many matches is written to the journal as a list in a file of its own, never as one huge record, so the journal keeps working however many matches there are. A damaged or missing list stops only that document from coming back, with the reason.

## [paged] Large files (paged mode)
A file larger than the large file threshold (setting Large file threshold, 6 MiB by default) opens in paged mode. To browse and edit a file byte by byte without these limits, open it in hex (topic Hex: opening and moving). The program reads only the lines on the screen, so the file opens quickly and does not fill the memory. Parts of the file it has already read, for example while numbering the lines, it gives back to the system, so the memory in use does not grow with the size of the file.
The status bar then shows Large file, its size and the revision.
### What works
- Moving with the arrows, PageUp, PageDown, Home, End, and Ctrl+Home and Ctrl+End. Go to a line, a percentage or a byte (Ctrl+G).
- Typing, deleting (Backspace and Delete), Enter and Tab at the caret. Undo and Redo. Save.
- Multiple carets: Ctrl+D, Ctrl+K then Ctrl+D, Ctrl+Shift+L, Ctrl+Alt+Up arrow and Ctrl+Alt+Down arrow, Alt+click and Esc work as in the text editor (topic Multiple carets). The arrows, Home and End move all carets. Typing, Backspace, Delete, Enter, Tab, Cut, Copy, Paste and Undo act on all of them as one step. Only the carets on the screen are drawn. A large file takes at most 1000 carets (a provisional limit); beyond that the status bar says so.
- Save (Ctrl+S) runs in the background: the window stays usable, the status bar shows how many percent of the new file are written, and Esc cancels the save before the data is committed (the file stays unchanged). You can keep typing meanwhile: the file gets the text as it was when you pressed Save, your later edits stay unsaved, and the tab shows them as unsaved changes. Undo and Redo work during the save and after it. A save that only overwrites bytes of the same length writes just those bytes and takes no edits for its short time. A file larger than 1 GiB (a provisional limit) is saved without a copy of the old version in the backup folder; the old file stays untouched until the new one is complete.

- Undo and Redo also work after Save: the steps made before the save can still be undone, and the next save writes the state after the undo. Typing a word without moving the caret is one step.
- Find (Ctrl+F). Find in Files.
- Selecting (Shift with the arrows, the mouse, Ctrl+A), Copy, Cut and Paste. A selection larger than the copy limit (256 MiB, a provisional value) is not copied and the status bar says so. A long selection is read in the background, and a cut deletes only if the text has not changed meanwhile.
- Paste takes at most 32 Mi characters (a provisional value); a longer text is not pasted and the status bar says so. A text over 1 Mi characters is prepared in the background and goes in where the caret is when that is done.
- A line longer than 16 KiB scrolls sideways with the bar below the text. A far part of the line shows … for a moment, until it is read. Its colors appear once the program has read the line up to there.
- A line of tens or hundreds of MiB is edited in place: typing, Backspace, Delete, Enter, Tab, paste, selecting and Undo read only the text around the caret, never the whole line, so the memory does not grow with the length of the line. The first move of the caret into a far part of such a line still has to count the characters up to it, which takes a moment; the program remembers the count for the next moves. A line longer than 1 GiB (a provisional value) is not edited; the status bar says so and the document does not change.
### What does not work
- Block selection and Alt+drag.
- Commands for lines, indentation, letter case, replacing.
- Go to Matching Bracket, word wrap.
- Converting and rereading the encoding, unifying line endings, normalization.
- Save As only in the same encoding.
### Messages
- Line numbering is still running, editing starts in a moment: wait until the file has been read through.
- The character cannot be written in the file encoding: type another character.
- The selection (size) is larger than the copy limit (limit). The clipboard was not changed. Select less. The limit is 256 MiB, a provisional value.
- The edit is too large to be undone; the document was not changed. Remove or insert less text at once.
- Line L is N long, more than the M that can be edited in paged mode. The document was not changed. Edit it in the hex view. The limit is 1 GiB, a provisional value.
- The clipboard text (size characters) is larger than the paste limit (limit characters). The document was not changed. Paste it in parts. The limit is 32 Mi characters, a provisional value.
- The encoding of the document changed while the text was being prepared; paste again. The document was not changed.
- A large file takes at most 1000 carets; no more were added: choose fewer places; the carets stay as they were.
- The selection is too long to look for its other occurrences: select a shorter text (the limit is a provisional 64 KiB).
- The text for all carets would be over the limit: paste less text or use fewer carets; the document does not change.
- A large file in an encoding cannot be opened yet: the file does not open; the program cannot read other encodings in a large file yet.
- The file is being saved, try again when the save ends: you tried something while a save that overwrites bytes in place was running, or reloaded the file during any save.
- Closing the tab or the window during a save waits for the end of the save; Esc cancels the save.

## [conflict] File changed outside
If another program changes the file, a bar appears above the document: The file changed on disk.
- Reload discards your changes and loads the file from disk.
- Keep Mine keeps your changes; the next save asks before it overwrites the file.
- Compare shows the differences between your tab and the file on disk (topic Compare with disk).
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
- title is open in another program that does not let others write to it, so it opened read only. Close the other program, then choose Try Again on the bar (topic Locked files).
- title is open for editing in another window of Textida, so it opened read only. Close the file in the other window, then choose Try Again on the bar, or Edit Anyway (topic Locked files).
- title is open for editing in another Textida (process N), so it opened read only. Close the file in that Textida, then choose Try Again on the bar, or Edit Anyway (topic Locked files).
- title is still locked. Try Again found the lock still in place; close the other program or window first.
- No change from outside waits for review in this tab. It has something to show after another program changed the file of a tab that has no unsaved edits (topic Review changes from outside).
- The earlier content could not be kept in the local history, so the changes cannot be reviewed. Use Reload.
- Reverted in the tab, but not saved: reason. The file on the disk was not changed. Fix the reason and save the tab again.
- Another review is open. Finish it first, then choose Review Changes from Outside.
- Not a file: path. Choose a file, not a folder (topic Open Special).
- The system could not open path. Check that a program is assigned to this type of file in the system.
- No closed tab to reopen. Reopen Last Closed Tab has no file to open yet (topic Open Special).

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

## [errors.remote] Error messages: remote files and sources
A message about a remote file or a source appears in the status bar, in the bar above the text or in a dialog. Each line quotes the message and says what to do.
- Cannot connect to name: reason. Check the host name, the port, the user, the password or key, and your network. If you use a proxy, check it too (topic Network proxy). If an old SFTP server refuses a key or shares no key algorithm, see Old RSA Signature (ssh-rsa) in the topic Remote connections.
- WARNING: the identity of host is not the one you trusted before. The program does not connect until you confirm the new identity. Ask the server administrator whether the server was reinstalled, and connect only if you know why it changed (topic Remote connections).
- The port must be a number from 1 to 65535. Type a whole number from 1 to 65535, or leave the box empty and the usual port of the protocol is used.
- The connection was not saved: reason. The reason says what failed: the profile folder (permissions, disk space) or the credential store of the system (topic What this version cannot do). Fix it and save again.
- path does not exist on the server. Check the path. A path to a folder shows its content, so you can browse from / to the file.
- Cannot open path: reason. Nothing was opened. Fix the reason (a permission on the server, a lost connection) and open the file again.
- Upload of title failed: reason. The server keeps its previous version; your changes stay saved here. Fix the reason and choose Upload to Server again; a partly sent file is continued (topic Upload to the server).
- The check against the server failed: reason. Nothing was downloaded or changed. When the connection works again, choose Check Again in the bar, or Check Server Version.
- The local copy of path was deleted. Open the file from the server again (Open Remote File).
- The file is not a usable list of connections: reason. Choose a file made by Export Connections (topic Back up connections).
- No plugin serves address. Compare the scheme at the start of the address with the list in Open from Source. A plugin that is turned off serves nothing (topic Plugin manager: install, update, roll back and remove plugins).
- Cannot open address: reason. Nothing was opened. Fix the reason and try again.
- Cannot save title: reason. Your text stays in the tab. Fix the reason and save again, or use Save As to write a copy to disk; that ends the link to the source.
- name does not accept writes. This source only reads. Use Save As to keep a copy on disk; that ends the link to the source.
- name did not take title: reason. The changes stay in the tab. Fix the reason and save again. The plugin has to be running (topic Tabs that a plugin supplies).
- This tab is read only because the file is locked. Use Edit Anyway on the bar, or Save As. Choose Edit Anyway on the bar: every save then checks the file on disk first. Or use Save As (topic Locked files).
- There are no connections yet. Create one in Remote Connections. Choose File, Remote Files, Remote Connections and New Connection (topic Remote connections).
- title on the server is not the version you opened. Nothing was uploaded. The file on the server changed after you opened it. Choose Show Differences, merge what you need, save and upload again; Upload Anyway replaces exactly the server version you were shown (topic Upload to the server).
- title was deleted on the server. Nothing was uploaded. The file is gone from the server. Upload Anyway puts your copy there again; Cancel leaves everything as it is.
- The certificate of host is not issued by a trusted authority, or does not match the name. Trust it only if you know this server. The window Server Identity shows the fingerprint. Choose Trust and Connect only if you know the server; otherwise cancel and ask the administrator (topic Remote connections).
- No plugin offers files from a URI scheme. Open from Source lists the sources that plugins offer, and a plugin that is turned off offers none. Start or turn on the plugin in the plugin manager (topic Plugin manager: install, update, roll back and remove plugins).
- title changed in name since you opened it. Nothing was written and your text is untouched. Choose Show differences to compare, Overwrite to replace exactly the version that was found, or Cancel (topic Open from a plugin source).
- title no longer exists in name. Nothing was written and your text is untouched. Overwrite writes your text to the source again; Cancel keeps the text in the tab, and Save As writes a copy to disk.
- The source cannot be reached, so its version cannot be shown. Show differences needs an answer of the source. Save again when it answers.
- Nothing found for “words” in name. Try other words, or type the address that starts with the scheme of the source.
- Search in name failed: reason. Nothing was opened. Fix the reason and search again; the log is in the Remote tab of the output window.

## [errors.plugins] Error messages: plugins
A plugin that cannot be installed or does not run is reported in a dialog, in a bar above the text or in the status bar. Each line quotes the message and says what to do.
- The file is not a readable ZIP. Nothing was installed. Download the ZIP of the plugin again or ask its author for a new one.
- The ZIP has a path that would be written outside the plugin folder. Nothing was installed. The author has to pack the plugin again.
- The ZIP unpacks to more than the allowed size (provisional limit). Nothing was installed. Ask the author for a smaller package; the limit is provisional.
- The ZIP has no nkspad-plugin.json in its root folder. The file has to lie directly in the root of the ZIP, not in a subfolder.
- The manifest is refused: The lines after it say which entry is wrong; the author has to fix nkspad-plugin.json.
- The plugin id belongs to a built-in plugin. A built-in id cannot be used; the author has to choose another id.
- A version of this plugin that is the same or newer is installed already. Nothing was installed. The manager can only roll back to the previous version it kept.
- This ZIP holds a different plugin (id), not an update of the selected one. Choose the plugin that the ZIP belongs to, or install the ZIP as a new plugin with Install plugin from ZIP.
- The plugin could not be installed: The reason follows the message. Nothing was installed.
- The plugin name could not be run: reason. Read the reason. If the program of the plugin is gone, install the plugin again from its ZIP.
- The plugin name stopped (reason). It starts again in N s. Wait: the plugin starts again by itself. Show output in the bar opens the last error lines of the plugin.
- The plugin name stopped (reason). After repeated crashes it is turned off. It stays turned off. Show output in the bar tells why; update or reinstall the plugin and turn it on again in the Plugin Manager.
- title was closed: its plugin closed it or stopped. Changes that were not saved are lost. Save the tab before the plugin stops: Save sends the text to the plugin (topic Tabs that a plugin supplies).
- The ZIP contains a symbolic link. Nothing was installed. The author has to pack the plugin again without symbolic links.
- The ZIP has more files than allowed (provisional limit). Nothing was installed. Ask the author for a package with fewer files.
- The ZIP lists the same file twice. Nothing was installed. The author has to pack the plugin again with every file once.
- Plugin name did not start: reason. Read the reason; the last lines of the plugin are in the Tasks output. Choose Start or Restart in the plugin manager again (topic Plugin processes: when they start, how to see them and how they end).
- keys for command does nothing: command (origin) has the same keys. A built-in, profile or user binding on the same keys wins over a plugin. Give the command of the plugin other keys in your keymap file, or change the other binding.
- keys (command) is the start of keys (command), so the longer one cannot be typed. Change the keys of one of the two commands so that neither sequence is the start of the other.
- Cannot be read: reason. Install the plugin again from its ZIP, or remove it (topic Plugin manager: install, update, roll back and remove plugins).
- This tab shows data a plugin supplies. It is read only and cannot be saved. Save and Save As say so in the status bar. Copy what you need into a tab of your own (topic Tabs that a plugin supplies).
- This tab belongs to a plugin. Save sends it to the plugin; it cannot be saved to a file. Use Save to send the text to the plugin; Save As is not available for this tab.

## [errors.agents] Error messages: MCP, /ide and agents
The messages of the MCP server, the /ide bridge and the agents appear in the status bar, in dialogs and in the agent panel. Each line quotes the message and says what to do. Nothing is applied to your tabs without your confirmation, so a failed step leaves them as they were.
- The MCP server could not start: reason. Fix the reason and choose MCP Server again. Nothing is shared meanwhile.
- Switch on the MCP server first (Tools, MCP Server). Switch the server on, then share the tab again (topic The editor as an MCP server).
- This tab cannot be shared. Only text tabs can be shared. A comparison or a list of search results cannot.
- The selection is larger than N bytes. Select less, or share the whole tab. Select less, or share the whole tab.
- The /ide bridge could not start: reason. Fix the reason and choose IDE Bridge /ide again.
- The /ide bridge was switched off: reason. The agent speaks a version of the protocol that Textida does not know. Nothing was changed in your tabs. Use a version of Claude Code that speaks a protocol Textida knows, then switch the bridge on again (topic The /ide bridge for Claude Code).
- No agent is connected to the /ide bridge. Run /ide in Claude Code, in a terminal in a folder of this window.
- name could not be tried: reason. The reason names the error and the last lines the program wrote to its standard error. Fix the setup of the server in the file of its agent (Textida never changes that file) and try again.
- No answer within N s. The server was told to stop that request. Check that the server starts and answers outside Textida, then try again.
- The snippet for agent does not pass the format check: reason. Nothing was written. The entry uses something the format of the other agent cannot hold; fix the entry in its source file and convert again.
- name failed: reason. The list closes. The last lines of the program are in the Tasks output.
- No value was entered for name; nothing was started. Connect again and type the value; it is kept in the credential store and nowhere else.
- name refused the key. The stored value of variables was dropped; you are asked again. Type the new key when you are asked.
- name refused the key. It does not come from the credential store of Textida (it is in the environment or written in the configuration); change it there. Change the key where it is, in the environment or in the configuration file; Textida does not change it there.
- Not a JSON object. Write the arguments as a JSON object, for example {"name": "value"}; {} means none.
- Shown are the first N of N characters. The rest is cut. Ask the tool for less (topic MCP servers of agents).
- agent could not be started: reason. Check that the program is installed and starts in a terminal; Textida installs no agents.
- Open a file or folder first; the agent works in its folder. Open a file or a folder first, then start the agent.
- An agent is already running. Stop it first with Stop Agent.
- Start an agent first. Start an agent with Start Agent, then share the tab (topic Agent over ACP).
- This tab cannot be shared with the agent. An agent names a file by its path: save the tab first. A comparison or a list of search results cannot be shared.
- A turn is still running. Cancel it first. Choose Cancel Agent Turn, or wait until the turn ends.
- The agent did not answer in time: reason. The turn ended. Send the message again, or stop the agent and start it again.
- The agent failed: reason. Read the reason in the panel; if the agent no longer runs, start it again.
- No MCP servers found in the files of Claude Code, Cursor, Codex and OpenCode. The command reads the configuration files of those agents. Add a server in the file of one agent, then run the command again (topic MCP servers of agents).
- Could not read: files. The named files are skipped and the others are listed. Fix the named file where the agent keeps it; Textida never changes it.
- name offers no tools and no resources. The server answered but offers nothing to call. Check its setup in the file of its agent.
- tool reports an error; its text is in a new tab. The call reached the server, and the server reports the error. Read the text in the new tab and call the tool again with other arguments.
- The value of name could not be saved (reason); it is used for this connection only. The next connection asks for the value again. Fix the cause named in the reason.
- The /ide bridge of this window is off. Switch it on with Tools, IDE Bridge /ide. Then repeat the command (topic The /ide bridge for Claude Code).
- agent version N (the table lists M). This is a note in the agent panel, not an error: the agent still starts, but this version has not been verified (topic Agent over ACP).
- agent: the version could not be read (reason). Not verified. This is a note in the agent panel too: the agent still starts, but its version is not verified.

## [errors.tools] Error messages: tools, linters, language servers and Git
These messages come from programs the editor starts for you. They appear in the status bar, in the Tasks output or in a bar above the text. Each line quotes the message and says what to do.
- title is still running. Stop it first. One tool runs at a time. Wait for it, or choose Stop Running Tool.
- title cannot run: reason. The reason names the variable or the program. An unknown variable, or one that does not apply to this document, stops the run before anything starts (topic Tools: run an external program).
- Stopped: the time limit of N s was reached. Raise timeoutSeconds in the definition of the tool, or the setting Default time limit of tools.
- name did not run: program was not found in vendor/bin, node_modules/.bin, .venv or PATH. Install it there or set its path as toolPaths in saveActions.json. Install the program, or give its path as toolPaths in saveActions.json (topic Linters).
- name did not finish in N s and was stopped. Findings of an earlier run stay. Raise lintTimeoutSeconds in saveActions.json.
- Saved without formatting: reason. The file was saved as it was. Fix the reason and save again, or switch the formatting off (topic Save actions: format, run a tool, upload).
- This tool has not been confirmed yet; run it once from Run Tool. Run the tool once with Run Tool and confirm its command and directory; then it can run on save.
- Uploading the files of folder to name has not been allowed; save a file of the project by hand and allow it. Save a file of the project by hand: the program asks, and you can allow the upload for that project.
- Lookups at registry are not possible: they are not allowed, or the network proxy needs a login. Choose Tools, Packages, Check Package Versions and agree for the registry. A proxy with a user name cannot be used for lookups (topic Package manifests: names, versions and newer-version marks).
- schema name: download not allowed. Choose Tools, Packages, Allow Schema Download and agree for the server (topic Schemas for JSON, YAML and TOML: check, completion and descriptions).
- No Makefile next to the open file or in a folder above it. Open a file in a folder that has a Makefile, or the Makefile itself (topic Makefile: run a target).
- A language server works with text documents only, not with large files shown in pages. A file in paged mode never goes to a language server (topic Large files (paged mode)).
- No language server for language was found: program. Install the server so that its program is found on the PATH (topic Language server: definition, references, rename, completion and diagnostics).
- The language server for language could not be started: reason. Read the reason, fix it and run a command of the group again.
- The language server for language stopped unexpectedly (detail). The editor starts it again by itself. If a notice says it gave up, run any command of the group again: the server starts and gets the open documents again.
- The language server for language could not be kept running after number attempts. The editor does not start it by itself any more. Run any command of the group to try again.
- Git could not be started: reason. The git program has to be installed and found on the PATH (topic Git: status, compare and stage a hunk).
- The file is not in a Git repository. Open a file from a Git repository.
- The index of file changed since it was last read (by another program). Nothing was written. Check the changes and stage the hunk again.
- The file has an unresolved merge conflict. Resolve it before staging hunks. Resolve the conflict with Git: Conflict, Take Ours / Take Theirs / Take Both, save the file, then choose Git: Mark File Resolved.
- The open file is not a package manifest. Open a package manifest and choose the command again (topic Package manifests: names, versions and newer-version marks).
- The open file is not a package.json or composer.json. Open a package.json or composer.json and choose the command again (topic Package scripts: npm and Composer).
- No scripts found in file. Add a scripts section to the file, or open another file.
- registry did not give a usable answer: reason. Nothing was changed. Try again later; the registry or the network proxy may be at fault (topic Network proxy).
- The manifest is too large to check. Check a smaller manifest; the limit is provisional.
- The document is in use by another command. Try again. Wait until the other command ends.
- The open file is not checked against a schema. Allow Schema Download works on a file that is checked against a schema (topic Schemas for JSON, YAML and TOML: check, completion and descriptions).
- No schema download of this file waits for your consent. Allow Schema Download asks only when the check of the file needs a schema from a server that you have not allowed yet.
- schema name: not loaded. The schema could not be read, was too large or invalid; the file is not checked against it. Fix or replace the schema.
- schema name: not checked (YAML form not supported). The file is not checked against this schema; nothing in the file changes.

## [limits] What this version cannot do
This help describes only what this version can do. These things are not in it, and this is what to do instead:
- Updates: the program updates itself only on Windows, in the installed Textida (topic Updates and return to the previous version). On macOS and Linux you install a new version yourself. A build without an update address and a signing key cannot look for updates and says so.
- Saved passwords: Windows (Credential Manager), macOS (Keychain) and Linux (Secret Service through libsecret). The macOS and Linux stores have not been tried on a real system yet. Where the store of the system is missing, the program keeps no password anywhere and asks for it at every connection (topic Remote connections).
- Plugins: a plugin is a program that runs with your user rights. Its permissions record what you allowed, they are not a sandbox, and the signatures of plugins are not checked (topic Plugin manager: install, update, roll back and remove plugins). Install only plugins you trust.
- Assistants: the /ide bridge and the agent over ACP have not been verified against the real programs (topics The /ide bridge for Claude Code and Agent over ACP). A connection that does not work is reported with its reason in the status bar or the Tasks output.
- Large files: editing in paged mode is limited (topic Large files (paged mode)). A language server, formatting and most commands that change text do not work there. To change the bytes of a file of any size, open it in hex.
- Hex: two revisions from the local history cannot be compared byte by byte, and structure templates are a subset of the .ksy format (topic Hex: structure template).
- Programs the editor only starts (git, linters, formatters, language servers, make, npm, Composer, HTML Tidy): they must be installed and found in PATH. The editor downloads none of them.
- The Explorer menu and the file associations: Windows only.
- Interface languages: Czech, English, Slovak and German. Old PSPad translation files cannot be imported.
- Numbers: the sizes, counts and waiting times behind the limits of plugins, transfers, exports, large files and the clipboard are provisional values. No final value has been decided yet, so a number in this help can change in a later version.

## [edit.clips] Clips
commands: nks.edit.insertClip, nks.edit.clipFromSelection, nks.clips.editVariables, nks.clips.editProjectVariables, nks.snippet.next, nks.snippet.previous, nks.snippet.end
A clip is a piece of text with places for variables and carets. Clips come from data plugins in the folder plugins of the profile; one subfolder per plugin, with a file nkspad-plugin.json and no program.
1. Choose Edit, Insert, Insert Clip. The list shows the clips with a preview of the text that would be inserted.
2. Pick a clip. If it has a form, fill it in: the preview follows what you type, a wrong value is marked and OK stays dimmed until every value fits its type.
3. The text goes into every caret and replaces every selection, as one Undo step. The carets end where the clip has the mark $|.
- Save Selection as Clip asks for a name and adds the selected text to your own library (the plugin user.clips, folder user-clips). The text is kept literally, so a dollar sign in it stays a dollar sign. The clip is in the list at once.
- Template language: ${name} is a variable, $| marks a caret (as many as you like), $$ is a dollar sign. Line breaks become the line break of the document.
- Snippets: a clip (and the plugin call snippet/insert with "format": "template") may use numbered tab stops: $1, ${1:default text}, ${1|one,two,three|}, $0 (the final caret). The same number in several places is edited together. After the insert the first stop is selected; Tab goes to the next one, Shift+Tab to the previous one, and after the last one the caret goes to $0. Escape or a click outside the snippet ends it. A stop with a list of values opens the list of choices. With several carets each has its own snippet and the stops are visited together.
- The lines of a snippet after the first take the indentation of the line it starts on; tabs in its own indentation become the indentation of the document (tabs or spaces). A clip without numbered stops is inserted as it is.
- In a snippet $| is not a caret, and a dollar sign before a digit is a stop: write $$ or \$ for a dollar sign. Besides ${name} the variables TM_SELECTED_TEXT, TM_FILENAME, TM_FILENAME_BASE, TM_DIRECTORY, TM_FILEPATH, UUID and CURRENT_YEAR, CURRENT_YEAR_SHORT, CURRENT_MONTH, CURRENT_DATE, CURRENT_HOUR, CURRENT_MINUTE, CURRENT_SECOND work (also as $NAME). Text that does not fit the syntax stays as it is.
- A default may hold more stops: ${1:for (${2:i} = 0)}. Tab visits the stops by number as usual; the place that holds other stops is the one you edit, and the other places of its stop show the text as it was. A transform makes a place show changed text: ${1/regex/format/options} for a stop (the place follows what you type into stop 1) and ${NAME/regex/format/options} for a variable. The format has $1, ${1:/upcase}, ${1:/downcase}, ${1:/capitalize}, ${1:/camelcase}, ${1:/pascalcase}, ${1:+text if}, ${1:?text if:text else} and ${1:-text else}; the options are g (every match, otherwise the first), i, m and s. Write \/ for a slash in the regular expression or the format; the expression has .NET syntax. Stops nested deeper than the limit stay as text; a snippet that grows past the length limit or whose transform runs too long is refused with a message.
- Where a variable gets its value, strongest first: 1. the value typed in the form of the clip, 2. the variables of the project, 3. your own variables (Edit, Insert, Edit User Variables opens the file variables.json of the profile: one JSON object of name and text), 4. the built-in ones. The same name gives the same value in a clip, a recipe and an external tool.
- Project variables: Edit, Insert, Edit Project Variables opens the file .textida/variables.json of the project of the open file, in the same shape as variables.json of the profile. The project is the nearest folder above the file that has such a file; when there is none, the file is created in the nearest folder with .textida or .git. Its names are for the files of that project only and win over your own variables of the same name. A damaged file stops clips, recipes and tools in that project and names the file. An external tool whose command uses a project variable asks for confirmation again when the value changes, as it does when the definition changes.
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
1. Choose Edit, Conversions and Recipes, Convert. The list shows the conversions with a preview on the selected text (on the first sample of the converter when nothing is selected).
2. Pick one. All selections change as one Undo step. If one of them cannot be converted, nothing is changed and the status bar says why.
- Repeat Last Command runs the conversion that ran last, without the list.
- Converters are data: contributes.converters in nkspad-plugin.json, with id, title, rules and optionally samples and command. A rule is {"find": "&", "replace": "&amp;", "regex": false, "ignoreCase": false}. Rules run in order, each on the result of the one before. With regex on, the dialect of the search applies: replace may use $1, ${name}, $& and the case codes \U \L \E \u \l.
- A sample is {"input": "...", "expected": "..."}. When the plugin is read, every sample is converted. A converter whose sample gives another text, one with no rules, with an empty find or with a wrong regular expression is refused, and the plugin is skipped with the reason in the status bar.
- A converter with "command" also gets a command of that name that converts at once, without the list.
- The special conversions of the editor itself are in the same list (topic Special conversions).

## [edit.specialConversion] Special conversions
commands: nks.edit.convert.decToHex, nks.edit.convert.hexToDec, nks.edit.convert.urlEncode, nks.edit.convert.urlDecode, nks.edit.convert.quotedPrintableEncode, nks.edit.convert.quotedPrintableDecode, nks.edit.convert.base64Encode, nks.edit.convert.base64Decode, nks.edit.convert.removeAccents, nks.edit.convert.tabsToSpaces
Conversions the editor has itself. Each is a command in the palette and in the menu Edit, Conversions and Recipes, and is in the Convert list too. It changes the text of every selection at once as one Undo step, or of the whole file when nothing is selected. The new text stays selected.
1. Select the text to convert, or select nothing to convert the whole file. Several selections are converted at once.
2. Choose Edit, Conversions and Recipes and then the conversion, or run it from the palette.
3. The new text replaces the old as one Undo step and stays selected.
- Decimal to Hexadecimal and back: the selection must be one whole number from 0 to 18446744073709551615 (FFFFFFFFFFFFFFFF); a hexadecimal one may start with 0x. Spaces and line ends around the number stay. 255 gives FF, ACE gives 2766.
- Text to URL Encoding writes everything except the letters A to Z, digits and - _ . ~ as %XX of the UTF-8 bytes, a space as %20 and a line end as %0D%0A, or %0A in a file with LF line ends. URL Encoding to Text reads %XX back; a % without two hexadecimal digits stays as it is.
- Text to Quoted-Printable keeps printable ASCII and spaces and writes every other UTF-8 byte, line ends and = included, as =XX. Quoted-Printable to Text reads =XX and drops = at a line end; any other = refuses the text.
- Text to Base64 encodes the UTF-8 bytes. Base64 to Text skips spaces and line ends inside and refuses text that is not Base64 or whose bytes are not UTF-8.
- Remove Accents takes acute accents, carons, rings and umlauts off the letters (č to c, ů to u). Letters without a decomposition (ł, ø, ß) and the marks of other scripts stay.
- Tabs to Spaces replaces every tab with spaces up to the next tab stop of the tab size of the document, counted from the column where the selection starts.
- When the conversion cannot read a selection, nothing changes and the status bar says so. Repeat Last Command runs the same conversion again.
- The conversion title did not run: the text is not in the form this conversion reads. Nothing was changed. Select text in the form the conversion reads, for example one whole number for Decimal to Hexadecimal.
- Nothing to change. The conversion would give the same text.

## [edit.recipes] Edit recipes
commands: nks.edit.runRecipe
A recipe chooses parts of the open file, changes them and shows the result before anything is written.
1. Choose Edit, Conversions and Recipes, Run Edit Recipe. The list shows each recipe with the changes it would make in this file.
2. Pick one. The preview window lists every change: line, column, old text and new text. Choose Apply to write them.
3. All changes are one Undo step; Undo brings the file back exactly as it was. The changed parts stay selected. Nothing is written when the file changed meanwhile or when the recipe cannot run (a variable without a value, a pattern that takes too long).
- A recipe works on the selections, or on the whole file when nothing is selected. Repeat Last Command applies the last recipe without the preview.
- Recipes are data: contributes.recipes in nkspad-plugin.json, with id, title, steps and optionally command. Steps that choose come first. {"kind": "matches", "find": "...", "regex": false, "ignoreCase": false, "wholeWord": false} keeps the matches inside the current parts. {"kind": "columns", "from": 3, "to": 5} keeps columns 3 to 5 of every line (counted in characters from 1; to 0 or left out means up to the end of the line; a shorter line gives nothing).
- Steps that change: {"kind": "convert", "converter": "id"} (a converter of the same plugin), {"kind": "replace", "find": "...", "replacement": "...", "regex": false} and {"kind": "set", "text": "..."}, which replaces the part with a text in the template language of clips. In it ${selection} is the part itself, and the variables have the same values as in a clip (topic Clips); carets $| are ignored.
- A recipe with no steps, an unknown kind, a bad pattern, an unknown converter, a choosing step after a changing one or no changing step is refused when the plugin is read.

## [edit.recipeFiles] Recipes over files
commands: nks.edit.runRecipeFiles
A saved recipe runs again over another set of files. You see every change first; nothing is written before you apply it.
1. Choose Edit, Conversions and Recipes, Run Recipe on Files and pick a recipe: one from a data plugin, or a file in the folder recipes of the profile.
2. Pick the files: a folder with a file mask (for example *.cs;*.txt; the ignore files of the search apply), the open tabs, or the files of the search results.
3. The Search Results tab lists the changes file by file, with the same ticks as Replace in Files. Apply writes the ticked ones. A file that was changed by someone else after the preview is not touched: it is named as a conflict and the other files are written. Open documents get one Undo step.
- A recipe file is JSON with id, title and steps, the same fields as a recipe in nkspad-plugin.json, plus an optional version and an optional converters list (the converters its convert steps name). A file without a version is version 1, the format of the first recipes, and loads as before. A file of a newer version is refused with a message.
- Steps: matches, columns, convert, replace and set work as in Edit Recipes (each file is its own whole text). A step {"kind": "format", "formatter": "id"} runs a formatter of Save Actions over the result of the steps before it; it must come last, its time limit is formatTimeoutSeconds of saveActions.json (10 seconds for now without it), and it works on named files of the type that formatter handles. A file where a step cannot run is left out and named with the reason.
- Every file keeps its encoding and its line endings; a new character the encoding cannot hold is shown as a change that cannot be applied. Format steps do not run on a single file in Run Edit Recipe.

## [edit.aiContext] Copy as AI context
commands: nks.edit.copyAiContext
Puts the selection, the file, the open tabs or the opened folder on the clipboard as one block of Markdown or XML, to paste into an AI assistant. Nothing is sent anywhere.
1. Choose Edit, Copy as AI Context and pick what to copy: the Selection (every selection), This file, the Open tabs or the Opened folder.
2. The preview shows how many files go in, the size as Markdown and as XML, and what was left out with the reason. Choose Copy as Markdown or Copy as XML.
- Each file has its path, the lines, the language, the encoding and the revision. A file with changes that are not saved is marked, and the text is the text in the editor now.
- Left out and named: .env files (their values are never copied, also when they are shown in the editor), files and folders the ignore files exclude, hidden ones, binary files, files over the size limit of the folder walk and files that cannot be read.
- A large file in paged mode is never copied whole: only its selection is. Without a selection it is listed as left out.
- A file or selection that does not fit in what is left of the size limit of the whole context is left out and listed with the limit; a smaller file after it may still fit.

## [edit.fragment] Edit a string as JSON, SQL or HTML
commands: nks.edit.fragmentJson, nks.edit.fragmentSql, nks.edit.fragmentHtml, nks.edit.fragmentWriteBack
A string in a PHP or JavaScript file opens in a tab of its own, in the language you pick, and goes back into the string with the escaping of its kind.
1. Put the caret in the string and choose Edit, Edit String As, Edit String as JSON, Edit String as SQL or Edit String as HTML. The new tab shows the text the string stands for, without the quotes and escapes.
2. Edit the text. Choose Edit, Edit String As, Write String Fragment Back: the text is escaped for the string and replaces its content. The tab stays open, so you can write again.
- Kinds: PHP single-quoted and double-quoted strings, heredoc and nowdoc; JavaScript strings in single quotes, double quotes and templates in backticks. In a double-quoted string and in a heredoc every dollar sign gets a backslash, so no variable can appear by accident.
- Refused with a message: a string with variables or expressions (`$name`, `{$x}`), a JavaScript template with `${...}`, an escape that is not text (a byte above 127, octal) and, when writing, a line that would end a heredoc or nowdoc.
- The tab follows the string through edits of the file elsewhere. If the file changed inside the string after the tab was opened, nothing is overwritten without asking: the dialog offers Overwrite or Cancel.
- When the file is closed, the fragment tab stays open as a detached text. Its title says so and Write String Fragment Back writes nothing.

## [tools.emmet] Emmet abbreviations
commands: nks.tools.emmetPreview, nks.tools.emmetWrap
1. In an HTML or CSS file, type an abbreviation and press Tab. `ul>li*3` becomes a list of three items with the caret in the first one; each further Tab jumps to the next item.
2. Tab indents as usual when no abbreviation stands in front of the caret, when text is selected, when the caret is inside an open tag, or when the abbreviation is not valid.
3. Tools, Structure and Language, Preview Emmet Abbreviation shows what the abbreviation in front of the caret would become (`|` marks the places Tab visits) and expands it when you agree.
4. Tools, Structure and Language, Wrap with Emmet Abbreviation (HTML only) asks for an abbreviation and wraps every selection in it. The text goes into the innermost last element; a bare `*` on that element (`ul>li*`) makes one element per line.
5. Every expansion goes into all carets at once and is one Undo step.
- HTML: `name#id.class[attr=value]{text}*3`; `>` child, `+` sibling, `^` one level up, `( )` group; in names, classes and text `$` is the number of the repeat (`$$` pads with zeros, `$@-` counts down, `$@3` starts at 3). A lone word expands only when it is an HTML tag name or a short name of the plugin (`bq`, `btn`, `a:link`, `input:checkbox`, `!` for a page skeleton). Inside an element the missing name is `li`, `tr`, `td`, `option`, `span` or `div`, as the parent needs.
- CSS: `m10-20` is `margin: 10px 20px;`, `w100p` is `width: 100%;`, `c#f00`, `pos:a`, `dn`, a trailing `!` adds `!important`, `+` joins several declarations. A whole number gets `px`, a fraction `em`; the letters `p`, `e`, `x`, `r` after a number mean `%`, `em`, `ex`, `rem`. It works only inside a `{ }` block.
- The short names are the plugin's own table of common properties and tags, not the full Emmet set.
- The expansion is a snippet: Tab visits its empty places one after another, first the empty elements, then the empty attribute values such as `href` of `a` (in a page skeleton `!` the title after the body). With several carets every expansion has its own places and the carets move on together. After the last place the caret goes to the end of the expansion. While Tab walks through the places it never expands: a tag name typed into a place stays as typed. Shift+Tab goes back, Esc ends the walk.
- The setting Emmet on Tab turns the expansion by Tab off for all languages or for one; the two commands keep working.
- The Emmet process starts with the first Tab in an HTML or CSS file.

## [tools.git] Git: status, compare and stage a hunk
commands: nks.git.status, nks.git.diffIndex, nks.git.diffHead, nks.git.stageHunk, nks.git.unstageHunk, nks.git.conflictOurs, nks.git.conflictTheirs, nks.git.conflictBoth, nks.git.markResolved, nks.git.conflictDelete
1. Open a file from a Git repository and choose Tools, Git, Git: File Status. The panel shows what `git status` reports for the whole repository: what is staged and what is changed in the working tree. The panel and the status bar refresh by themselves shortly after the last change made from outside. Every repository that has an open file is watched, also when files of several repositories are open; the watch of a repository ends when its last open file is closed. The status bar shows the repository of the last Git command or of the last change from outside; the panel stays with the repository of the last file status.
2. Git: Compare with Index and Git: Compare with HEAD open a comparison tab of the open text against its version in the index or in HEAD. The hunks are the same as in `git diff`.
3. Git: Stage Hunk at Caret puts only the change the caret is in into the index. The other changes of the file stay in the working tree only. The file on disk is not changed.
4. If another program changed the index in the meantime, nothing is written. The plugin reads the index again and reports a conflict; check the changes and repeat the command.
- The plugin writes to the repository only when you stage or unstage a hunk or resolve a conflict. Status and compare only read and do not even refresh the index file.
- The text open in the editor is staged, saved or not. A file with an unresolved merge conflict is not staged.
5. Git: Unstage Hunk at Caret takes back to HEAD only the staged change the caret is in (or points to in the file). The other staged changes stay.
6. For a merge conflict put the caret in the block between `<<<<<<<` and `>>>>>>>` and choose Git: Conflict, Take Ours / Take Theirs / Take Both. The block is replaced by the chosen side. When no block is left and the file is saved, choose Git: Mark File Resolved; the file is added to the index.
7. A double click or Enter on a file in the status panel opens it. The status bar shows the branch and counts: `+` staged, `~` changed, `?` untracked, `!` conflicts; it updates after every Git command.
8. In a file inside a Git repository the left margin marks the lines that differ from the index with a filled bar (blue changed, green added, a short red bar where lines were removed) and the lines that are staged already with an outlined bar. A click on a mark stages the change; a click on an outlined mark takes it back from the index. The setting Show Git changes in the gutter turns the marks off. A file above the size limit and a file with hidden values (.env) get none.
9. The tabs Git: Compare with Index and Git: Compare with HEAD have buttons Stage hunk and Unstage hunk for the difference the tab is on (from the keyboard the commands Compare: First Plugin Action and Second Plugin Action in the Tools menu do the same). After a button the comparison opens again in the place of the tab.
10. A conflict in which one side deleted the file (the panel says "conflict: deleted by us" or "deleted by them"), or both sides added a file that cannot be merged, has no markers. Git: Conflict, Take Ours / Take Theirs / Take Both then says so. Keep the file with Git: Mark File Resolved, or delete it with Git: Conflict, Delete File. A file both sides deleted is shown in the panel, but there is nothing to open for it; resolve it with `git rm`.
11. Conflict blocks are found in files saved as UTF-16 and UTF-32 as well.
12. With `core.autocrlf` (or an `eol` attribute) a hunk of a file with CRLF line endings is stored with LF, as `git add` stores it.
- Not there yet: commit and branches.
- The Git process starts with the first command, or when a file is opened while the marks in the margin are on. The `git` program must be on the PATH.

## [tools.lsp] Language server: definition, references, rename, completion and diagnostics
commands: nks.lsp.start, nks.lsp.goToDefinition, nks.lsp.findReferences, nks.lsp.rename, nks.lsp.restart, nks.lsp.stop
1. Open a text document and choose Tools, Language Server, Language Server: Start for This Document. The server for the language of the document starts and gets the text. From then on every edit goes to it, and what it finds wrong is underlined.
2. Language Server: Go to Definition jumps to the definition of the word at the caret, also in another file. Language Server: Find References lists every place in the panel on the left; Enter or a double click on an item opens it.
3. Language Server: Rename Symbol asks for the new name of the word at the caret. The server's changes in all files, with the creating, renaming and deleting of files it asks for, are shown together in the change review; what you accept is applied as one step. A new file is shown with its first lines as added text. The tab of an open file moves with the file when it is renamed; the tab of a deleted file is closed, and when it has unsaved changes the editor asks whether to close it or keep it open. A file that is open in another tab must have been started with the first command too, otherwise the rename is refused as a whole.
4. Ctrl+J (Edit, Insert, Complete Word or Clip) also lists what the running server offers at the caret. Choosing such an entry also makes the additional changes the server asks for, such as adding an import, in the same Undo step. An answer that arrives after you changed the text is not shown.
5. If the server stops by itself, a notice says so and the underlines it drew disappear. The editor then starts it again by itself, after a wait that gets longer with each attempt, and hands it every open document as it is now. After a few attempts in a row it gives up and a notice says so; a server that ran for a while before it stopped gets its attempts back. Language Server: Restart (or any other command of this group) starts it at once, also after the editor gave up. Language Server: Stop shuts it down and cancels a start that is waiting. Restart and Stop act on the server of the project the current document belongs to; the servers of other projects keep running.
- The editor does not start a server on its own: nothing runs until the first command of this group, and a server is not told about a file until then.
- Large files shown in pages and files over the size limit never go to a server.
- The server is looked up by name on the PATH. If it is not found, a notice names the program that was looked for.
- The status bar shows LSP: running, LSP: not found or LSP: off for the server of the last command; its tooltip names the language and the project folder.
- A server runs for one project folder. It is the nearest folder above the file that has a marker file of the language (C#: a solution `.sln` or `.slnx` first, then `.csproj`; Rust: `Cargo.toml`; PHP: `composer.json`; Dart: `pubspec.yaml`; JavaScript and TypeScript: `tsconfig.json` or `package.json`; C and C++: `compile_commands.json` or `CMakeLists.txt`), otherwise the nearest folder with `.git` or `.textida`, otherwise the folder of the file. A marker above a repository does not count. Only a limited number of folders is searched upwards. Documents of two projects get two server processes; documents of one project share one.
- Not there yet: choosing the project folder by hand, and a rename in files that are not UTF-8.

## [tools.lspPlugin] Language servers from a plugin
1. A plugin lists its servers under `languageServers` in `nkspad-plugin.json` and asks for the permission `language.server`. Each server has an `id`, `languages` (language mode ids, at least one), optional `globs` (such as `*.foo` or `src/**/*.fx`), `command`, `args`, optional `initializationOptions` (an object sent to the server in `initialize`) and `locate`.
2. `locate` is `path` (the default): `command` is a bare program name looked up on the PATH. With `plugin`, `command` is a path inside the plugin folder.
3. When you install the plugin, the editor shows the permission and waits for your answer. Without it the server is never started.
4. The server is used by the Language Server commands for every document whose language is listed or whose file name matches a glob. A server of a plugin is chosen before the one that comes with the editor.
- One server process runs per language, project folder and server setup (program, arguments, environment and `initializationOptions`) and is shared by all open documents of that project. A document that matches only by glob is served for the first language of the server.
- If the program is missing or cannot start, one notice names it and editing goes on. If it stops by itself, the editor starts it again as described in the topic Language server; after it gave up, run any command of the group.
- A server with no open document of its project is stopped after a time and started again by the next command. The servers of other projects keep running.
- When the server asks for settings (`workspace/configuration`), it gets the part of `initializationOptions` that the requested section names: `a.b` is the object `b` inside `a`, and a request without a section gets the whole object. Anything else is answered with `null`.
- A manifest with a wrong entry is refused as a whole, with the reason.

## [edit.complete] Completion of words and clips
commands: nks.edit.complete
1. Press Ctrl+J (Edit, Insert, Complete Word or Clip). The list offers the words that continue the word in front of the caret, then the clips that fit the language of the document.
2. Pick an entry. A word replaces the word in front of every caret; a clip is inserted like in the topic Clips and replaces that word. It is one Undo step.
- The words are taken from all open text tabs. The word that is being typed is not offered, and a word is offered only when it is longer than what is typed. Words of the document being edited come first, those that stand nearest to the caret before the others (within a limited number of characters on each side of it), then the more frequent ones.
- Clips come from the library of the topic Clips. A clip with no language fits every document; a clip with a language fits only documents of that language.
- The values of a secrets file (`.env`) are never offered while they are hidden. They are offered only in the file itself and only while shown.
- The list holds a limited number of words; type in the box to narrow it. Entries of a running language server come with them, see the topic Language server.

## [edit.sql] SQL: dialect and formatting
commands: nks.edit.formatSql, nks.edit.previewFormatSql, nks.tools.sqlDialect
1. Edit, Formatting, Format SQL lays out the selections, or the whole file when nothing is selected: a clause per line, one select item per line, sub-queries indented, keywords in one letter case. It is one undo step and nothing outside the selection changes.
2. Edit, Formatting, Preview SQL Formatting shows the same result in the change preview first.
3. The dialect decides the colors and which words count as keywords. The first of these that is set wins: Tools, Structure and Language, SQL Dialect (this file only); `sql_dialect = mysql` in an `.editorconfig` (every SQL file in that folder and below); the setting Default SQL dialect. With none of them the dialect is Generic, and nothing is reported as an error.
- Dialects: generic, mysql, postgres, sqlite, tsql. The editor never connects to a database.
- Strings, quoted names and comments are copied as written. Formatting a result again changes nothing.
- A selection larger than the limit of the plugin is refused with a message.
- Indentation (4 spaces) and keyword case (upper) are fixed for now; they are not settings yet.

## [edit.html] HTML: entities and HTML Tidy
commands: nks.edit.htmlEncode, nks.edit.htmlEncodeAll, nks.edit.htmlDecode, nks.edit.htmlTidy, nks.edit.htmlCloseTag, nks.edit.htmlMatchTag, nks.edit.htmlSelectElement, nks.edit.htmlRemoveTag, nks.edit.htmlWrapTag, nks.edit.htmlStripTags, nks.edit.htmlInsertImage, nks.edit.htmlInsertLink
1. Edit, Formatting, Convert to HTML Entities replaces & < > " ' in the selections (the whole file when nothing is selected) with entities. Convert from HTML Entities reads named, decimal and hexadecimal entities back. Converting an encoded text back gives the text you started from. Each is one undo step.
2. Edit, Formatting, Format with HTML Tidy sends the HTML to the HTML Tidy program and shows the result in the change preview. The messages of Tidy go to the output window.
3. Tidy is a separate program. The editor looks for it at the path in the setting HTML Tidy program, in the folder `tidy` next to the formatters plugin, in `tools/tidy` of the application folder and in the folders of PATH. If it is not found, the message lists the places.
- Tidy is not part of this build: install it and set the path, or put it in one of those places.
- Line numbers in the messages count from the start of the text that was sent to Tidy.
- Convert to HTML Entities Including Non-ASCII also writes every character outside ASCII as a numeric entity (`&#233;`); converting back gives the text again.
4. Tag tools (HTML, XML and PHP files): Edit, Formatting, Close Open Tag writes the end tag of the nearest tag that is still open at the caret. Go to Matching Tag moves the caret to the partner of the tag at the caret. Select Element selects the element around the selection; repeat it to select the next one out. Remove Tag deletes the start and end tag of the element at the caret and keeps its content. Wrap in Tag asks for a name and wraps each selection in it. Strip Tags removes all tags and comments from the selections (the whole file when nothing is selected).
- Tags are paired by nesting. Comments and the content of `script` and `style` hold no tags. The optional end tags of HTML (`li`, `p`) are not inferred, so such a tag counts as open.
5. Edit, Formatting, Insert Image asks for an image file and writes `<img src="…" width="…" height="…" alt="">` in place of every selection. The address is relative to the document (a file on another drive, or an unsaved document, gets a `file:///` address). The width and height are read from the header of PNG, GIF, JPEG, BMP and WebP files; for any other file, such as SVG, the tag has no size. Selected text on one line becomes the alt text; with nothing selected the caret stands in the empty alt.
6. Insert Link asks for an address and writes `<a href="…">` around every selection. When nothing is selected the link shows the address, or, for an image file, the picture with its width and height. An address that is the path of an existing file is written relative to the document.

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
1. Ctrl+K then Ctrl+T (View menu, Table, Toggle Table View) switches between table and text. A .tsv or .tab file is split on tabs, any other file on commas.
2. View, Table, Table Delimiter and Header chooses comma, semicolon, tab or vertical bar as the delimiter, whether a double quote encloses values, and whether the first row is the header. Until you choose, the delimiter is detected from the first rows of the file (the extension decides when they are unclear). A cell in double quotes that holds line breaks counts as part of its row, and a row cut off by the end of the examined part of a long file is left out.
- A large file opens at once: rows are counted in the background and the scroll range grows while the file is read. Until the end is reached the status bar shows the row count with a plus, like 12+.
- Arrows, Tab, PageUp and PageDown, Home and End (within the row), Ctrl+Home and Ctrl+End move the current cell; Shift extends the selection. Click a column header to select the column and a row number to select the row.
- A quoted cell may hold line breaks; the table shows its first line and the mark ↵.
- Copy puts the selected cells on the clipboard as tab-separated text; Paste writes tab-separated text from the current cell on.

## [table.edit] Table view: editing and filtering
commands: nks.table.insertRow, nks.table.deleteRow, nks.table.insertColumn, nks.table.deleteColumn, nks.table.filter, nks.table.clearFilter
1. Enter or F2 (or just typing) edits the current cell. Enter saves and moves down, Shift+Enter inserts a line break, Esc cancels. A cell that was quoted stays quoted; another value is quoted only when it holds the delimiter, a double quote or a line break.
2. View, Table, Insert Row adds an empty row after the current one. Insert Column asks for the name of the column and adds it after the current one. Delete Row and Delete Column remove the selected rows or columns. Each action is one Undo step.
3. View, Table, Filter Rows shows only the rows whose current column contains the text (upper and lower case do not matter). Clear Filter shows all rows again. A filter never changes the file, and row numbers stay those of the file.
- Delete clears the selected cells. A short row gets the missing delimiters when a cell behind its end is filled in.
- When the quote character is turned off in Table Delimiter and Header, a value with the delimiter or a line break is refused.
- Rows cannot be added while a filter is on.

## [table.copyAs] Copying cells as Markdown, SQL, JSON or HTML
commands: nks.table.copyAs
Select cells in the table view and choose View, Table, Copy Table As, then a form. The clipboard gets the selected columns and rows in that form.
1. Open a CSV or TSV file in the table view (topic Table view for CSV and TSV) and select the cells.
2. Choose View, Table, Copy Table As and then Markdown table, SQL INSERT, JSON or HTML table. SQL INSERT first asks for the name of the table.
3. The status bar names the form, for example The selected cells were copied as Markdown table. Paste them where you need them.
- Markdown table: a pipe table with the header (or the column letters A, B, ... when there is no header). Special characters such as the pipe, the backslash, asterisks, underscores and angle brackets get a backslash, and a line break in a cell becomes `<br>`.
- SQL INSERT: one statement per row with the values as text in single quotes (a quote is doubled). The header gives the column names. You are asked for the table name, starting with the file name.
- The SQL dialect decides the quoting: `sql_dialect` in an `.editorconfig` of the folder, else the setting Default SQL dialect (topic SQL: dialect and formatting). MySQL puts names in backticks and doubles a backslash in a value; T-SQL puts names in square brackets and writes values as N'...'; the other dialects use double quotes for names and leave a backslash as it is.
- JSON: an array of objects named by the header, or an array of arrays when the header is missing, has an empty name or repeats a name. All values are text.
- HTML table: a table element with entities for `& < > " '`; the clipboard holds the HTML as source text and as HTML for pasting into a document.
- A hidden value of a .env file is copied as stars, not as its text. Copy and cut with Ctrl+C and Ctrl+X put the placeholder in its place too, and a cut that touches one deletes nothing.
- If the file changes while the command runs, nothing is copied and a notice asks you to run it again.
- Your own form is a converter of a data plugin with "table": true (topic Convert text). It appears below the built-in forms. It gets the selected cells as Copy writes them: tab-separated, one row per line, the header row when it is selected, and a value with a tab, a quote or a line break in double quotes. Its result goes to the clipboard; if it fails, the clipboard is not changed.
- The table is over the limit: the clipboard takes at most N characters of one form. Nothing was copied and the clipboard was not changed. The limit is 1 Mi characters (a provisional value). Select fewer rows.
- The converter title did not run: reason. The clipboard was not changed. Your own form, the converter of a data plugin, failed. Fix the converter or use a built-in form.

## [table.region] Editing selected text as a table
commands: nks.table.editRegion
Select a part of a text, for example a block of comma-separated lines in a note or a log, and choose View, Table, Edit Selection as Table.
1. The dialog asks for the delimiter (comma, semicolon, tab or vertical bar), whether a double quote encloses values and whether the first selected row is the header. The preview shows the first rows as cells.
2. The selected text becomes the table. The rest of the file stays text: a cell edit changes the bytes of that cell only, and nothing outside the selection is read or changed.
- Everything of the table view works in it: moving, editing, adding and deleting rows and columns, filtering, copy and paste. The status bar says that only the selected text is the table.
- Rows added at the end go inside the selection. Toggle Table View returns to the text; the next Toggle Table View shows the whole file as a table.

## [compare.text] Comparing texts
commands: nks.compare.clipboard, nks.compare.file, nks.compare.texts, nks.compare.ignoreWhitespace, nks.compare.ignoreCase, nks.compare.action1, nks.compare.action2, nks.compare.action3, nks.compare.action4
Two texts are compared line by line in a tab of their own. Equal lines stand level, a changed block is tinted on both sides and marked in the middle strip, and the bar above says how many differences there are.
1. Compare with Clipboard (Tools menu, or the context menu of the editor) compares the tab with the text on the clipboard. With text selected in the editor, only the selection is compared.
2. Compare with File asks for a file and compares the tab with it. Compare with Disk is in the File menu and compares the tab with its own file.
3. Compare Two Texts opens two empty sides. Paste left text and Paste right text put the clipboard into a side.
- From the command line, textida --diff left right opens the comparison of two files: the left file opens in a tab and can take blocks of the right one, which is read-only.
- A plugin can open a comparison of any two sources (a revision of a document, a file, a virtual document, or text it supplies) and add buttons to it (Git adds Stage hunk and Unstage hunk). A button acts on the difference the tab is on. From the keyboard, press the digit 1 to 9 while the comparison has the focus (the tooltip of a button names its key), or use the commands Compare: First to Fourth Plugin Action in the Tools menu. The buttons work while the documents and files of the comparison are unchanged since it was made; otherwise open the comparison again. When the plugin refreshes the comparison, the new tab takes the place of the old one and stays on the same difference. A side larger than the size the editor allows for plugins is refused.
- The tab side is the document itself: when it changes, the comparison is made again after a short pause. The clipboard, a selection and a file on disk are read-only sides.
- A different line ending (CR, LF, CRLF) does not make a line differ. Spaces and letter case do, until you turn on Ignore Whitespace or Ignore Case in Tools, Compare. Then lines that differ only in spaces and tabs (anywhere in the line), or only in letter case, are equal. The choice applies to every comparison, open ones are compared again at once, and the bar above says it is on. Such equal lines are not tinted even where their spaces differ, and copying to the left or right takes the lines as they are.
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
commands: nks.tools.mcpServer, nks.tools.mcpShareTab, nks.tools.mcpShareSelection, nks.tools.mcpTools
An AI assistant that you start on this computer (for example a coding agent) can use Textida through the Model Context Protocol. It sees only the tabs you share, and it never writes: you see each proposal as a change preview and you decide.
1. Choose Tools, MCP Server. The dialog lists what the assistant gets. Switch on to start the server; choosing the command again switches it off and takes every tab back.
2. Configure the assistant to start the program textida-mcp.exe from the mcp folder next to the editor (the status bar shows the full path). It talks to the editor over a local pipe; there is no network port.
3. Open a tab and choose Tools, MCP, Share This Tab with MCP. The same command takes the tab back. A tab that is not shared is not even listed.
4. To give the assistant only a part of a tab, select the text and choose Tools, MCP, Share Selection with MCP. The selected text opens as a read-only excerpt in a tab of its own: a copy that does not follow the original, and the assistant cannot propose changes to it. Closing that tab takes it back.
5. Choose Tools, MCP, MCP Tools to switch single tools on or off. Choosing a tool in the list changes its state; Escape closes the list.
- The assistant reads the text as it is in the editor, also what is not saved, searches in it, asks where your cursor and selection are, reads the errors and warnings of plugins and the linter, asks for the outline, the encoding and the line endings, finds lines of a log level in a log of any size and reads or searches bytes (at most 64 KiB per read).
- A change is a proposal for one revision of the text. If the text changed in the meantime, the proposal is refused and the assistant has to read again. You see the proposal like other whole-document changes and choose to apply it or not; the assistant can ask how it ended.
- Values in .env files are shown to the assistant as *. When you share such a tab you are asked whether it may see the real values; the answer holds for that tab only.
- Withdrawing a tab or switching the server off stops the requests that are running, and a proposal that still waits for your decision is withdrawn and counts as rejected. A reload of a file also withdraws it.
- Only a limited number of proposals can wait for your decision at once (the limit is provisional); a further one is refused until you have decided. A result larger than the size limit is refused, and the assistant is told to ask for less.
- The program for the assistant speaks the current MCP version (2026-07-28) and, for assistants that still start with initialize, version 2025-11-25.

## [tools.ide] The /ide bridge for Claude Code
commands: nks.tools.ideBridge, nks.tools.ideDisconnect
Claude Code that runs in a terminal on this computer can connect to a window of Textida with its /ide command. The agent then learns where your selection is and which file is open, and the changes it wants to make to a shared tab appear in the change review. It sees only the tabs you share, and Textida applies only what you accept.
1. First switch on the MCP server (Tools, MCP Server) and share a tab (Tools, MCP, Share This Tab with MCP). The bridge uses the same shared tabs.
2. Choose Tools, IDE Bridge /ide. The dialog says what the agent gets. Only after you confirm does Textida open a port, on this computer only (127.0.0.1), and write a lock file with a token and the folders of the window to ~/.claude/ide (or to the ide folder of CLAUDE_CONFIG_DIR). Choosing the command again closes the port and deletes the file.
3. In the terminal, in a folder of this window, run /ide in Claude Code. A program without the token from the lock file is refused.
4. The status bar shows the connected agent. Click it, or choose Tools, IDE Bridge /ide: Disconnect Agent, to cut the agent off. The port stays open, so the agent can connect again with /ide; to stop that, switch the bridge off.
- Every window has its own bridge with its own port and token, and so has every instance of Textida. An agent connected to a window sees only the tabs shared in that window. A lock file that a crashed instance left behind is deleted when the next bridge starts.
- The agent is told about the selection and path of a shared tab each time the selection changes or you move to the tab. A tab that is not shared, or has no file yet, is not announced. Values in .env files are hidden as in every other read. Taking the tab back stops this at once.
- A change that Claude Code proposes for a shared tab opens in the change review (the Search Results tab). Applying all of it answers FILE_SAVED and rejecting it answers DIFF_REJECTED; applying only some hunks answers with an error, so the agent reads the file again. Textida applies what you accept to the tab and does not save the file. A file that is not open in the window, not shared, or an .env file is refused.
- getDiagnostics gives the errors and warnings of the shared tabs.
- Not verified: Claude Code does not publish a specification of this protocol. The bridge follows the official page on the built-in IDE server and a community description (coder/claudecode.nvim), and no Claude Code version has been confirmed to work with it yet. Not implemented: opening files from the agent, the list of open tabs, saving through the bridge, running notebook cells and @-mentions.

## [tools.agent] Agent over ACP
commands: nks.agent.start, nks.agent.panel, nks.agent.shareTab, nks.agent.cancel, nks.agent.stop
1. Tools, Agent, Start Agent... starts a coding agent that speaks the Agent Client Protocol (ACP): Gemini CLI, Codex, Claude, Junie, GitHub Copilot or OpenCode. The dialog shows the exact command and the folder it runs in; nothing starts before you confirm. The agent must be installed and found in PATH. A batch file (.cmd, .bat), the usual form of an npm-installed agent on Windows, is started through cmd.exe with every argument quoted for it. The list tells for each agent whether its program is found in PATH; for a missing one a message says where to install it from and nothing starts. The dialog shows the full path of the program that will run. After you confirm, Textida runs the program with --version and notes the version in the panel next to the version of the table; a program that prints none is only noted.
2. The Agent Panel under the editor shows the conversation. Type a message and press Enter. Cancel Agent Turn (or the Cancel turn button) sends the cancel to the agent and answers its open permission questions with "cancelled"; Stop Agent ends the process.
3. The agent reads only the tabs you share: Share This Tab with Agent. It then gets the text as it is in the editor, unsaved changes included; values of .env files stay hidden. Stopping the agent takes every shared tab back.
4. When the agent writes a shared file, the change is shown as hunks in the change review (the Search Results tab) and is written only when you apply it. If you reject it, the agent gets an error. A file that is not shared, or not open, is not written through the editor.
5. When the agent asks for permission, a dialog shows the choices the agent offers. Cancel or closing the dialog answers "cancelled". For a tool the agent runs by itself (a command, a delete, a move, a download) the dialog says that the editor can neither check nor undo it, and it warns that a choice marked "always" is remembered by the agent, not by the editor.
- Where the guarantee ends: the editor controls only what goes through it. The agent's own tools can change files, run programs and use the network without the editor, and the editor does not set the permission mode of any agent (each keeps its own default). Set what the agent may do in the agent's own settings. The panel shows this under the heading: the permission mode the editor sets (none), what the agent's own tools can do, and live counts. "Through the editor" counts what the editor itself served: reads, proposals you decided and those you applied. "Outside the editor" is only what the agent reported about its own tools (commands, deletes, moves, downloads), so the real number can be higher; such tool lines in the conversation are marked.
- Textida stores no keys or passwords for agents, and the table of agents holds only program names and options. Log in with the agent's own program; the agent gets the environment Textida runs in. The version in the table is the one the public ACP registry lists, not a tested one.
- Every agent is listed as "not verified": no run against the real program has passed the test scenarios yet. A turn in which the agent neither says nor asks anything for too long ends as timed out. The time limits and the size of a file handed to the agent are provisional.
- A plugin can host its own conversation in the same panel (permission agent.panel). The panel works from the keyboard: it opens with the cursor in the message box, Enter sends, Tab moves through Send, Cancel turn, Stop agent and Close, Space or Enter presses the button, and Escape closes the panel and returns to the editor. Cancel Agent Turn and Stop Agent are also commands in the menu and the palette. A screen reader gets a name for the panel, the conversation, the message box, the status and every button, and the status (starting, ready, working, ended) is announced when it changes. This is tested with the accessibility tree of the program, not yet with a real screen reader.

## [tools.hooks] Agent hooks editor
commands: nks.agent.hooks
Edits the hooks of Claude Code (settings.json, settings.local.json, the hooks.json of a plugin) and of Codex (hooks.json) in a form. It never runs a hook: a hook is a command, and nothing here starts one.
1. Open the settings file in a tab and choose Tools, Agent, Edit Agent Hooks... The tree lists the events, the matcher groups and the handlers of the file. Choose a handler to change it, or a group to add a handler to it.
2. Fill in the form: the event, the matcher, the type of the handler and its command (or URL, prompt, MCP server and tool), and if you want the timeout, the status message, the condition (if) and what happens when the hook fails. Apply to list changes the list in this window only.
3. Dry run shows what the hook would receive on standard input (a sample made from the public documentation of the product), whether the matcher fires for the value you type, and what each exit code does on that event. For Claude Code an exit code 1 is a non-blocking error and 2 blocks where the event can be blocked. For Codex only what its documentation says is shown.
4. Propose changes sends the new text to the change review (the Search Results tab). You see the changes as hunks and apply them as one undo step; then save the file as usual. The editor refuses the proposal while the list has errors, and when the tab changed after this window was opened.
- Findings: the file is checked against a JSON Schema made from the public documentation of the product. An event the product does not have, a missing command or a field of the wrong type is an error. A field the documentation does not list is a warning, and so is a setting that the documentation says has no effect (a matcher on an event without matchers, a condition on an event that is not a tool event).
- The file is strict JSON: a comment in it is a syntax error. Only the hooks entry and the places you change are touched; the rest of the file stays byte for byte.
- Codex keeps hooks in [hooks] tables of config.toml as well; the editor does not change those tables. The tables of exit codes come from the documentation read on the day the dry run names, and they are not assumed to hold for other versions or for the other product.

## [tools.transcript] Agent transcripts
commands: nks.agent.transcript
Opens the transcript of an agent session (a .jsonl file, for Claude Code under ~/.claude/projects, for Codex under ~/.codex/sessions) as a list of turns. It is read only: the file is never written, and nothing is sent anywhere.
1. Choose Tools, Agent, Open Agent Transcript... and pick the file, or open the .jsonl file in a tab first and run the command. The window lists the turns while the file is indexed; a file of many gigabytes opens at once and the list grows as the index is built.
2. Choose a turn to see the prompt, the answers, the tool calls with the lines they changed (red and green) and the results. The header shows the lines of the turn and an estimate of its tokens.
3. Search looks through the whole transcript, also while it is still being indexed, and lists the lines it finds. Choose a hit to jump to its turn and row. A line too long to be read whole is not searched; the status says how many were skipped.
4. Files in this turn lists the files the agent read, wrote or edited; Open file opens one in the editor. Open raw line opens the line behind the chosen row as a document that cannot be saved.
- Records that are not a user message, an answer or a tool call, and blocks the reader does not know, are shown as raw JSON. A Claude Code file is read only by what the public documentation of the Messages API and the Agent SDK defines, because Claude Code says the format of its file is internal and changes between versions. The format of a Codex file has no prose documentation either; its lines are read by the open-source protocol definitions of Codex, and only the lines of type response_item: messages (blocks input_text and output_text), reasoning summaries, function calls, shell calls and custom tool calls such as apply_patch, and their outputs. Events, session data and turn contexts of a Codex file are shown as raw JSON. A turn starts at a user message, so the instructions that Codex itself adds as user messages are turns of their own, and the files of a turn are those that the markers of an apply_patch call name.
- Values of .env files are hidden (topic Hidden values) in the rows, in raw lines, in the titles of the turns and in the hits. The editor learns them from the secrets files of the open tabs and of the opened folder, and from the transcript itself when the agent reads such a file. A value the transcript shows before it is first read cannot be hidden in what was shown already. In a Codex file a call counts as a read or a write of a secrets file when any text in its arguments names one, and a value is hidden also inside the arguments of a later call, where JSON is written inside JSON.
- A line longer than the limit is classified from its start and shown cut. The index never holds more than its memory limit: a line that would not fit is not registered and the status says that the list is cut. The size limits and the memory of the index are provisional.

## [tools.tokens] Token estimate
commands: nks.agent.tokens
Shows how large a text is in tokens, for a selection, the document or a copy for an AI assistant. The number is always an estimate (marked with ≈) counted on this computer; no text is sent anywhere.
1. While something is selected, the status bar shows its estimate, for example "≈ 1,240 tokens". Click it to choose the rule: a generic 4 characters per token, or the published rule of OpenAI, Gemini or Claude (before and after Opus 4.7).
2. Tools, Agent, Estimate Tokens... shows the selection and the document with the rule and where it comes from. In paged mode only a selection is counted.
3. Copy as AI Context shows the estimate for the Markdown and the XML text in its preview.
- The rules are rules of thumb for English text that the vendors publish, not the tokenizers of the models. Other languages and code can differ a lot. An exact count needs the tokenizer of the model or the service of the vendor; this editor asks for neither.
- The rule lasts until the window closes.

## [tools.mcpAgents] MCP servers of agents
commands: nks.tools.mcpServers
One list of the MCP servers your coding agents are set up with. Textida reads Claude Code (.mcp.json, ~/.claude.json), Cursor (.cursor/mcp.json), Codex (~/.codex/config.toml) and OpenCode (opencode.json, opencode.jsonc). Project files are looked for in the folder of the active file and its parent folders. Textida only reads these files; it never writes to them.
1. Choose Tools, MCP, MCP Servers of Agents. Each row shows the server, the agent and file it comes from, and its command or address with secrets hidden.
2. Choose a server, then Try. A dialog shows the exact command, the folder and the variables with their values hidden (***). For a server reached by an address it shows the address and the names of the headers instead. Nothing starts and nothing is sent until you press Start (Connect for an address).
3. After that Textida starts the program or connects to the address, asks for the tools (MCP versions 2026-07-28 and 2025-11-25 for a program, 2025-11-25 for an address) and ends the connection. The result says how many tools it offers, or gives the error and the last lines the program wrote to its standard error. The same lines are in the Tasks output.
4. Choose Connect to use the server. A list shows its tools and its resources. A resource opens in a new read-only tab. A tool asks for its arguments as a JSON object (while you type, the schema of the tool is shown), and then a question of its own shows the server, the tool, the arguments and whether the server says the tool only reads. Nothing is sent before you press Call; Cancel is the default button. The answer opens in a new read-only tab, and the Tasks output records that the tool was called and how many characters the arguments had, not the arguments themselves.
5. Choose Convert the entry to another agent to get the entry in the format of that agent in a new tab. The snippet is checked against that format first. Values of variables and headers are replaced by ***, so fill them in before you paste the snippet into the file of that agent.
- ${NAME} in a command, an argument, a variable, an address or a header is replaced from the environment of Textida, ${NAME:-text} gives a default. A name that is not set there is looked up in the Windows Credential Manager under textida:mcp:env:NAME. A name that is in neither is asked for, with the typing hidden, and kept in the Credential Manager and nowhere else; if the credential store is unavailable the value is used for this connection only. Cancelling the question starts nothing.
- A server at an address gets keys in its headers, so the address must be https:// unless it is on this computer. Textida speaks Streamable HTTP (MCP 2025-11-25). An entry of type sse is connected over the older transport with a separate event channel (MCP 2024-11-05): Textida opens the address as an event stream and sends its messages to the address the server names there, which must be on the same host as the stream. An address without a type whose server refuses the first message with an HTTP 4xx status other than 401 and 403 is tried that way as well. The network proxy of the program applies. A key the server refuses (HTTP 401 or 403) is dropped from the Credential Manager and asked for again once; a key that comes from the environment or is written in the configuration file is not changed by Textida, and the message says so. npx and npm are started as node with the script of the Node.js shim, so no cmd.exe stands between the editor and the server. Any other batch file is started through cmd.exe.
- Limits (provisional): an answer or a resource longer than 262144 characters is cut, and the status bar says how long it was. A list, a call or a read that takes longer than 120 seconds ends with a message, and the server is told to stop that request. A program that exits or is stopped is reported with its exit code and the last lines of its standard error, and the list closes.
- Secrets are hidden in the list, the dialog, the output and the snippets, also where one appears inside an argument or in the error output of the program. Values shorter than 4 characters are not searched for in text.

## [tools.shell] Explorer menu and file associations
commands: nks.tools.shellMenuAdd, nks.tools.shellAssociate, nks.tools.shellRemove
Windows only. Textida can add itself to the Explorer context menu of your user and to the Open with list. Nothing needs administrator rights, and only keys of Textida under HKEY_CURRENT_USER\Software\Classes are written. Keys of other programs, PSPad included, are not read or changed. Both commands work in the installed Textida.exe.
1. Choose Tools, System Integration, Add Textida to the Explorer Menu. Files get the entry Open with Textida; it starts Textida with the file, or hands the file to the Textida that is already running. On Windows 11 the entry is under Show more options.
2. Choose Tools, System Integration, File Associations to pick extensions. Choosing an extension in the list adds or removes Textida in its Open with list; Escape closes the list. No extension is associated until you choose it.
3. To make Textida the default program of an extension, use Open with, Choose another app in Explorer. Windows keeps that choice to itself.
4. Choose Tools, System Integration, Remove Textida from Explorer to delete the menu entry and every association that Textida added. The uninstaller does the same.
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
### Messages
- Updates work only in an installed Textida (Setup.ps1 next to Textida.exe on Windows). Updating works only on Windows, in the installed Textida. Elsewhere install the new version yourself (topic What this version cannot do).
- This build has no update address or signing key set, so it cannot check for updates. Use a build that comes with the update address and the signing key, or install the new version yourself.
- Update failed: reason. Nothing was installed and the current version keeps running. The reason names the cause (a signature or checksum that does not fit, a package that does not match, a network error); try again later.
- Save or close the documents with unsaved changes first: the journal is off, so an update could lose them. Save the documents, or turn on crash recovery (topic Crash recovery), then choose the update again.
- There is no previous version to return to. There is a previous version only after an update.

## [review.external] Review changes from outside
commands: nks.review.open, nks.review.folder, nks.review.next, nks.review.previous
When another program (a script, an assistant, a build tool) changes the file of a tab that has no unsaved edits, the bar offers Review changes.
1. Choose Review changes. The tab takes the new content, the earlier content is kept in the local history and cannot be deleted while the review waits, and the Search Results tab lists the changes line by line.
2. Tick the changes you want to keep. Unticked ones, or the whole file when its box is unticked, go back to the earlier content when you choose Apply.
3. Apply writes the reverted text into the tab as one undo step and saves the file; bytes outside the reverted changes stay as they are.
- The tab shows a badge with the number of waiting changes. Choose File and then Review Changes from Outside to open the list again; it also comes back after a restart.
- Alt+F7 and Alt+Shift+F7 jump to the next and previous change from outside.
- If you type in the tab during the review, the changes are shown again from the new text.
- Files that hold secret values (.env) and files with unsaved edits keep the older bar: Reload, Keep Mine, Compare.
- With Keep changes from outside without review in the settings, such a tab takes the new content at once. It is off by default.
- Files of the opened folder that no tab shows are watched too. When another program changes such a file and the local history holds the content from before (the file was opened or saved here once), that content is kept as the base and the file waits for review. File, Review Changes from Outside in Folder lists the waiting files; the chosen one opens with its badge and its changes by hunks, as above. The waiting files survive a restart.
- The watch leaves out .git, .codegraph, what the .gitignore and .ignore files of the folder ignore (an ignored folder takes its content with it) and the transcripts of agents (a projects folder below .claude* and a sessions folder below .codex*). It also leaves out files that hold secret values (.env), files too large for a tab, binary files, files whose new content the history already knows, files created by another program (there is no earlier content), and the files the editor wrote itself, for a few seconds after Replace in Files or a batch of encodings.
- At most 200 files wait at once and one look takes in at most 500 files; the status bar says when a limit is reached. These limits, the half second before a changed file is looked at and the few seconds after the editor's own writes are not decided values.
