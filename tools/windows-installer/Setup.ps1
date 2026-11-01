<#
.SYNOPSIS
Per-user installer of Textida: Setup.ps1 installs or updates, Setup.ps1 -Uninstall removes. No administrator rights are needed.

.DESCRIPTION
Install copies the package to -Dir, adds a Start menu shortcut and an entry in Apps & features, all under the current user.
-ApplyUpdate DIR switches the installed folder to the verified, unpacked DIR (written by Textida, not by hand): the old folder is kept as <Dir>.previous.
-Rollback swaps the installed folder and <Dir>.previous. Both wait for the process -WaitPid to end, keep the portable profile, and with -Restart start Textida again, also after a failure.
Uninstall asks Textida.exe to remove its Explorer menu entry and file associations (--unregister-shell), then removes what Install added.
The settings and the journal of the user (%APPDATA%\Textida) are kept. Nothing of PSPad is read or changed.
-Dir, -UninstallKey and -ShortcutDir default to the real locations; tests point them elsewhere.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [string]$Dir = (Join-Path $env:LOCALAPPDATA 'Programs\Textida'),
    [string]$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Textida',
    [string]$ShortcutDir = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'),
    [ValidateSet('', 'cs', 'en', 'sk', 'de')][string]$Language = '',
    [string]$ApplyUpdate = '',
    [switch]$Rollback,
    [int]$WaitPid = 0,
    [switch]$Restart
)

$ErrorActionPreference = 'Stop'

$text = @{
    en = @{ Installed = 'Textida {0} was installed to {1}.'; Updated = 'Textida was updated to {0} in {1}.'; Removed = 'Textida was removed. Your settings in {0} were kept.'
            Running = 'Textida is running. Close it and run the setup again.'; BadPackage = 'Textida.exe is missing next to Setup.ps1, so this is not a complete package.'
            UnknownDir = '{0} exists and is not a Textida folder. Nothing was changed.'; NotInstalled = 'Textida is not installed in {0}. Nothing was changed.'
            Switched = 'Textida {0} is now in {1}; the previous version is kept in {2}.'; NoPrevious = 'There is no previous version in {0}. Nothing was changed.'; BadStaging = '{0} is not a complete Textida package. Nothing was changed.' }
    cs = @{ Installed = 'Textida {0} byla nainstalována do {1}.'; Updated = 'Textida byla aktualizována na {0} v {1}.'; Removed = 'Textida byla odebrána. Vaše nastavení v {0} zůstalo.'
            Running = 'Textida běží. Zavřete ji a spusťte instalaci znovu.'; BadPackage = 'Vedle Setup.ps1 chybí Textida.exe, balíček tedy není úplný.'
            UnknownDir = '{0} existuje a není to složka Textidy. Nic se nezměnilo.'; NotInstalled = 'Textida není nainstalována v {0}. Nic se nezměnilo.'
            Switched = 'Textida {0} je nyní v {1}; předchozí verze zůstala v {2}.'; NoPrevious = 'V {0} není předchozí verze. Nic se nezměnilo.'; BadStaging = '{0} není úplný balíček Textidy. Nic se nezměnilo.' }
    sk = @{ Installed = 'Textida {0} bola nainštalovaná do {1}.'; Updated = 'Textida bola aktualizovaná na {0} v {1}.'; Removed = 'Textida bola odstránená. Vaše nastavenia v {0} zostali.'
            Running = 'Textida beží. Zatvorte ju a spustite inštaláciu znova.'; BadPackage = 'Vedľa Setup.ps1 chýba Textida.exe, balík teda nie je úplný.'
            UnknownDir = '{0} existuje a nie je to priečinok Textidy. Nič sa nezmenilo.'; NotInstalled = 'Textida nie je nainštalovaná v {0}. Nič sa nezmenilo.'
            Switched = 'Textida {0} je teraz v {1}; predchádzajúca verzia zostala v {2}.'; NoPrevious = 'V {0} nie je predchádzajúca verzia. Nič sa nezmenilo.'; BadStaging = '{0} nie je úplný balík Textidy. Nič sa nezmenilo.' }
    de = @{ Installed = 'Textida {0} wurde nach {1} installiert.'; Updated = 'Textida wurde in {1} auf {0} aktualisiert.'; Removed = 'Textida wurde entfernt. Ihre Einstellungen in {0} blieben erhalten.'
            Running = 'Textida läuft. Schließen Sie es und starten Sie das Setup erneut.'; BadPackage = 'Neben Setup.ps1 fehlt Textida.exe, das Paket ist also unvollständig.'
            UnknownDir = '{0} existiert und ist kein Textida-Ordner. Es wurde nichts geändert.'; NotInstalled = 'Textida ist in {0} nicht installiert. Es wurde nichts geändert.'
            Switched = 'Textida {0} liegt jetzt in {1}; die vorherige Version bleibt in {2}.'; NoPrevious = 'In {0} gibt es keine vorherige Version. Es wurde nichts geändert.'; BadStaging = '{0} ist kein vollständiges Textida-Paket. Es wurde nichts geändert.' }
}
if (-not $Language) { $Language = (Get-UICulture).TwoLetterISOLanguageName }
if (-not $text.ContainsKey($Language)) { $Language = 'en' }

function Say([string]$key, [object[]]$values = @()) { Write-Output ([string]::Format($text[$Language][$key], $values)) }
function Fail([string]$key, [object[]]$values = @()) { Say $key $values; exit 1 }

# the profile of a portable setup lives next to the program and must survive an update and an uninstall
$keep = @('profile', 'textida.portable')

function Clear-InstallDir {
    Get-ChildItem -LiteralPath $Dir -Force | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Recurse -Force
}

$exe = Join-Path $Dir 'Textida.exe'
if ($WaitPid -gt 0) { Wait-Process -Id $WaitPid -Timeout 60 -ErrorAction SilentlyContinue }
$previous = "$Dir.previous"
$aside = "$Dir.aside"

if ($ApplyUpdate -or $Rollback) {
    # TxF commits all renames together; an interrupted helper leaves the installed path intact.
    $code = 0
    $incoming = if ($Rollback) { $previous } else { $ApplyUpdate }
    try {
        if (-not (Test-Path -LiteralPath $Dir) -and (Test-Path -LiteralPath $aside)) { Move-Item -LiteralPath $aside -Destination $Dir }
        if (Get-Process -Name Textida -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { Say 'Running'; $code = 1 }
        elseif ($Rollback -and -not (Test-Path -LiteralPath (Join-Path $previous 'Textida.exe'))) { Say 'NoPrevious' @($Dir); $code = 1 }
        elseif (-not $Rollback -and -not (Test-Path -LiteralPath (Join-Path $incoming 'Textida.exe'))) { Say 'BadStaging' @($incoming); $code = 1 }
        else {
            Set-Location -LiteralPath $env:TEMP
            foreach ($name in $keep) {
                $item = Join-Path $Dir $name
                if (Test-Path -LiteralPath $item) {
                    Remove-Item -LiteralPath (Join-Path $incoming $name) -Recurse -Force -ErrorAction SilentlyContinue
                    Copy-Item -LiteralPath $item -Destination (Join-Path $incoming $name) -Recurse -Force
                }
            }
            Remove-Item -LiteralPath $aside -Recurse -Force -ErrorAction SilentlyContinue
            Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class TextidaDirectorySwap
{
    public static void Apply(string dir, string incoming, bool rollback, Action<string> checkpoint)
    {
        string previous = dir + ".previous", retired = dir + ".retired", aside = dir + ".aside";
        RemoveRetired(retired);
        using (SafeFileHandle transaction = CreateTransaction(IntPtr.Zero, IntPtr.Zero, 0, 0, 0, 0, "Textida installation switch"))
        {
            if (transaction.IsInvalid) throw Failure("create the directory transaction");
            bool committed = false;
            try
            {
                if (!rollback && Directory.Exists(previous)) Move(transaction, previous, retired);
                Move(transaction, dir, aside);
                if (checkpoint != null) checkpoint("after-current-move");
                Move(transaction, incoming, dir);
                if (checkpoint != null) checkpoint("after-incoming-move");
                Move(transaction, aside, previous);
                if (checkpoint != null) checkpoint("before-commit");
                if (!CommitTransaction(transaction)) throw Failure("commit the directory transaction");
                committed = true;
            }
            finally
            {
                if (!committed) RollbackTransaction(transaction);
            }
        }
        RemoveRetired(retired);
    }

    private static void Move(SafeFileHandle transaction, string source, string destination)
    {
        if (!MoveFileTransacted(source, destination, IntPtr.Zero, IntPtr.Zero, 0, transaction))
            throw Failure("move an installation directory atomically");
    }

    private static IOException Failure(string operation)
    {
        return new IOException("Windows could not " + operation + ". The installation was kept; this update requires an NTFS volume with transaction support.",
            new Win32Exception(Marshal.GetLastWin32Error()));
    }

    private static void RemoveRetired(string retired)
    {
        // A committed switch stays valid; a later update retries removal of locked retired files.
        try { if (Directory.Exists(retired)) Directory.Delete(retired, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [DllImport("KtmW32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateTransaction(IntPtr attributes, IntPtr unitOfWork, uint options, uint isolationLevel, uint isolationFlags, uint timeout, string description);

    [DllImport("Kernel32.dll", EntryPoint = "MoveFileTransactedW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileTransacted(string source, string destination, IntPtr progress, IntPtr data, uint flags, SafeFileHandle transaction);

    [DllImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CommitTransaction(SafeFileHandle transaction);

    [DllImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RollbackTransaction(SafeFileHandle transaction);
}
'@
            [TextidaDirectorySwap]::Apply($Dir, $incoming, $Rollback.IsPresent, $null)
            $versionFile = Join-Path $Dir 'version.txt'
            if ((Test-Path -LiteralPath $UninstallKey) -and (Test-Path -LiteralPath $versionFile)) {
                New-ItemProperty -Path $UninstallKey -Name DisplayVersion -Value (Get-Content -LiteralPath $versionFile -Raw).Trim() -PropertyType String -Force | Out-Null
            }
            Say 'Switched' @($(if (Test-Path -LiteralPath $versionFile) { (Get-Content -LiteralPath $versionFile -Raw).Trim() } else { '?' }), $Dir, $previous)
        }
    }
    catch { Write-Output $_.Exception.Message; $code = 1 }
    if ($Restart -and (Test-Path -LiteralPath $exe)) { Start-Process -FilePath $exe -WorkingDirectory $Dir }
    exit $code
}

$running = Get-Process -Name Textida -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) { Fail 'Running' }
$shortcut = Join-Path $ShortcutDir 'Textida.lnk'
Set-Location -LiteralPath $env:TEMP

if ($Uninstall) {
    if (-not (Test-Path -LiteralPath $exe)) { Fail 'NotInstalled' @($Dir) }
    Start-Process -FilePath $exe -ArgumentList '--unregister-shell' -Wait
    Remove-Item -LiteralPath $shortcut -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $UninstallKey -Recurse -Force -ErrorAction SilentlyContinue
    Clear-InstallDir
    if (-not (Get-ChildItem -LiteralPath $Dir -Force)) { Remove-Item -LiteralPath $Dir -Force }
    Say 'Removed' @((Join-Path $env:APPDATA 'Textida'))
    exit 0
}

if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Textida.exe'))) { Fail 'BadPackage' }
$updating = Test-Path -LiteralPath $exe
if (-not $updating -and (Test-Path -LiteralPath $Dir) -and (Get-ChildItem -LiteralPath $Dir -Force)) { Fail 'UnknownDir' @($Dir) }

$versionFile = Join-Path $PSScriptRoot 'version.txt'
$version = if (Test-Path -LiteralPath $versionFile) { (Get-Content -LiteralPath $versionFile -Raw).Trim() } else { '0' }

New-Item -ItemType Directory -Path $Dir -Force | Out-Null
if ($updating) { Clear-InstallDir }
Get-ChildItem -LiteralPath $PSScriptRoot -Force | Where-Object { $_.Name -ne 'Install.cmd' } | Copy-Item -Destination $Dir -Recurse -Force

New-Item -ItemType Directory -Path $ShortcutDir -Force | Out-Null
$link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $Dir
$link.Save()

New-Item -Path $UninstallKey -Force | Out-Null
$entries = @{
    DisplayName = 'Textida'; DisplayVersion = $version; Publisher = 'NKS Hub'; InstallLocation = $Dir; DisplayIcon = $exe
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $Dir 'Setup.ps1')`" -Uninstall -Dir `"$Dir`" -Language $Language"
}
foreach ($name in $entries.Keys) { New-ItemProperty -Path $UninstallKey -Name $name -Value $entries[$name] -PropertyType String -Force | Out-Null }
foreach ($name in 'NoModify', 'NoRepair') { New-ItemProperty -Path $UninstallKey -Name $name -Value 1 -PropertyType DWord -Force | Out-Null }

Say $(if ($updating) { 'Updated' } else { 'Installed' }) @($version, $Dir)
