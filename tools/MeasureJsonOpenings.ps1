# Run in an interactive Windows test session. Each sample uses a fresh app and profile.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApplicationPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$CapturePath,
    [Parameter(Mandatory)][string]$PythonPath,
    [Parameter(Mandatory)][string]$JoinScript,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ApplicationSha256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$CaptureSha256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$FixtureSha256,
    [Parameter(Mandatory)][ValidateCount(4, 4)][int[]]$Points,
    [ValidateRange(1, 1000)][int]$Count = 30,
    [ValidateRange(1, 2147483647)][long]$Affinity = 15
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$controller = [Diagnostics.Process]::GetCurrentProcess()
if ($controller.SessionId -eq 0) { throw 'Use a logged-on interactive session, not session 0' }
foreach ($name in @('ApplicationPath', 'FixturePath', 'CapturePath', 'PythonPath', 'JoinScript')) {
    Set-Variable -Name $name -Value (Get-Item -LiteralPath (Get-Variable $name -ValueOnly)).FullName
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'The output directory already exists' }
if ((Get-Item -LiteralPath $FixturePath).Length -lt 1GB) { throw 'The JSON fixture must contain at least 1 GiB' }
foreach ($pair in @(@($ApplicationPath, $ApplicationSha256), @($CapturePath, $CaptureSha256), @($FixturePath, $FixtureSha256))) {
    if ((Get-FileHash -LiteralPath $pair[0]).Hash -ne $pair[1]) { throw 'An input hash differs from the requested artifact' }
}
if (-not (Get-Item -LiteralPath $ApplicationPath).VersionInfo.ProductVersion.Contains($Commit)) {
    throw 'The application version does not contain the requested source commit'
}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$OutputDirectory = (Get-Item -LiteralPath $OutputDirectory).FullName
$originalAffinity = $controller.ProcessorAffinity
$savedEnvironment = @{}
Get-ChildItem Env: | Where-Object { $_.Name -like 'TEXTIDA_*' -or $_.Name -in @('TEMP', 'TMP') } | ForEach-Object {
    $savedEnvironment[$_.Name] = $_.Value
}
$rows = [Collections.Generic.List[object]]::new()
$application = $capture = $null
$applicationForced = $captureForced = $false
$exitCode = 1

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class JsonOpeningWindow {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    public static bool FocusWithClick(IntPtr hwnd, uint expectedPid) {
        RECT rect;
        if (!GetWindowRect(hwnd, out rect) || rect.Right - rect.Left < 500) return false;
        var point = new POINT { X = (rect.Left + rect.Right) / 2, Y = rect.Top + 16 };
        uint owner;
        var target = WindowFromPoint(point);
        GetWindowThreadProcessId(target, out owner);
        if (owner != expectedPid || GetAncestor(target, 2) != hwnd || !SetCursorPos(point.X, point.Y)) return false;
        target = WindowFromPoint(point);
        GetWindowThreadProcessId(target, out owner);
        if (owner != expectedPid || GetAncestor(target, 2) != hwnd) return false;
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        return true;
    }
}
'@

function Write-Json($value, [string]$path) {
    $value | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path -Encoding UTF8
}

function Finish-OwnedProcess([Diagnostics.Process]$process, [string]$expectedPath, [bool]$closeWindow) {
    if ($null -eq $process -or $process.HasExited) { return $false }
    if ($process.MainModule.FileName -ne $expectedPath) { throw 'The owned process identity changed' }
    if ($closeWindow) {
        if ($process.CloseMainWindow() -and $process.WaitForExit(5000)) { return $false }
    } elseif ($process.WaitForExit(10000)) { return $false }
    $process.Kill()
    if (-not $process.WaitForExit(5000)) { throw ('The owned process is still running: ' + $process.Id) }
    return $true
}

try {
    $controller.ProcessorAffinity = [IntPtr]$Affinity
    Get-ChildItem Env: | Where-Object Name -like 'TEXTIDA_*' | Remove-Item
    $os = Get-CimInstance Win32_OperatingSystem
    $computer = Get-CimInstance Win32_ComputerSystem
    Write-Json @{
        commit = $Commit; application_sha256 = $ApplicationSha256; capture_sha256 = $CaptureSha256
        fixture_sha256 = $FixtureSha256; fixture_bytes = (Get-Item -LiteralPath $FixturePath).Length
        join_script_sha256 = (Get-FileHash -LiteralPath $JoinScript).Hash
        script_sha256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash
        os = $os.Caption; os_version = $os.Version; architecture = $os.OSArchitecture
        session = $controller.SessionId; logical_processors = $computer.NumberOfLogicalProcessors
        physical_memory_bytes = $computer.TotalPhysicalMemory; affinity = $Affinity; requested_count = $Count
        cache = 'Fixture is hashed before every opening; guest cache warm, host cache unknown'
        method = 'DXGI LastPresentTime joined to the first state-bound JSON frame marker'
        scope = 'File open to guest desktop image update; excludes app startup, physical monitor and RDP transport'
        first_screen_limit_ms = 1000; composition_trace = $false
    } (Join-Path $OutputDirectory 'metadata.json')

    for ($sample = 0; $sample -lt $Count; $sample++) {
        $folder = Join-Path $OutputDirectory ('run-{0:D3}' -f ($sample + 1))
        [void][IO.Directory]::CreateDirectory((Join-Path $folder 'profile'))
        [void][IO.Directory]::CreateDirectory((Join-Path $folder 'tmp'))
        $env:TEXTIDA_PROFILE = Join-Path $folder 'profile'
        $env:TEXTIDA_CHANNEL = 'json-open-' + [Guid]::NewGuid().ToString('N')
        $env:TEXTIDA_MEASURE = Join-Path $folder 'measure.jsonl'
        $env:TEXTIDA_MEASURE_FILE = $FixturePath
        $env:TEXTIDA_MEASURE_BACKEND = 'json'
        $env:TEXTIDA_MEASURE_KEYS = '1'
        $env:TEXTIDA_MEASURE_QUICK = '1'
        $env:TEXTIDA_MEASURE_OSINPUT = '0'
        $env:TEXTIDA_MEASURE_TRACE = '0'
        $env:TEXTIDA_MEASURE_PRESENT = 'external'
        $env:TEMP = Join-Path $folder 'tmp'
        $env:TMP = $env:TEMP

        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $ApplicationPath
        $start.Arguments = '--smoke'
        $start.WorkingDirectory = Split-Path -Parent $ApplicationPath
        $start.UseShellExecute = $false
        $application = [Diagnostics.Process]::Start($start)
        $application.Id | Set-Content -LiteralPath (Join-Path $folder 'app-pid.txt')

        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $CapturePath
        $start.Arguments = $application.Id.ToString() + ' ' + ($Points -join ' ') + ' 180 "' + (Join-Path $folder 'desktop.csv') + '"'
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $capture = [Diagnostics.Process]::Start($start)
        $capture.Id | Set-Content -LiteralPath (Join-Path $folder 'capture-pid.txt')
        $captureOutput = $capture.StandardOutput.ReadToEndAsync()
        $captureError = $capture.StandardError.ReadToEndAsync()

        $until = [DateTime]::UtcNow.AddSeconds(30)
        $focused = $false
        $clickedForFocus = $false
        $focusQpc = $clickQpc = 0L
        do {
            $application.Refresh()
            $hwnd = $application.MainWindowHandle
            if ($hwnd -ne [IntPtr]::Zero) {
                [uint32]$owner = 0
                [void][JsonOpeningWindow]::GetWindowThreadProcessId($hwnd, [ref]$owner)
                if ($owner -ne $application.Id) { throw 'The application window belongs to another process' }
                [void][JsonOpeningWindow]::SetForegroundWindow($hwnd)
                $focused = [JsonOpeningWindow]::GetForegroundWindow() -eq $hwnd
                if ($focused) { $focusQpc = [Diagnostics.Stopwatch]::GetTimestamp(); break }
                if (-not $clickedForFocus) {
                    $attemptedAt = [Diagnostics.Stopwatch]::GetTimestamp()
                    $clickedForFocus = [JsonOpeningWindow]::FocusWithClick($hwnd, $application.Id)
                    if ($clickedForFocus) { $clickQpc = $attemptedAt }
                }
            }
            Start-Sleep -Milliseconds 50
        } while (-not $application.HasExited -and [DateTime]::UtcNow -lt $until)
        if (-not $focused) { throw 'The owned application window did not become foreground' }
        if (-not $application.WaitForExit(120000)) { throw 'The opening measurement timed out' }
        $application.ExitCode | Set-Content -LiteralPath (Join-Path $folder 'app-exit.txt')
        if (-not $capture.WaitForExit(10000)) { throw 'The desktop collector did not stop after the application exited' }
        $capture.ExitCode | Set-Content -LiteralPath (Join-Path $folder 'capture-exit.txt')
        [IO.File]::WriteAllText((Join-Path $folder 'desktop-meta.txt'), $captureOutput.Result)
        [IO.File]::WriteAllText((Join-Path $folder 'desktop-error.txt'), $captureError.Result)
        if ($application.ExitCode -ne 0 -or $capture.ExitCode -ne 0) { throw 'The application or desktop collector failed' }
        if ($captureOutput.Result -notmatch 'display_request_restored=1') { throw 'The collector did not confirm restoration of its display request' }

        $joined = Join-Path $folder 'joined.csv'
        & $PythonPath -I $JoinScript (Join-Path $folder 'measure.jsonl') (Join-Path $folder 'measure.samples.csv') `
            (Join-Path $folder 'desktop.csv') $joined --app-exit (Join-Path $folder 'app-exit.txt') `
            --capture-exit (Join-Path $folder 'capture-exit.txt') --capture-meta (Join-Path $folder 'desktop-meta.txt') `
            1> (Join-Path $folder 'join-summary.jsonl') 2> (Join-Path $folder 'join-error.txt')
        if ($LASTEXITCODE -ne 0) { throw 'The frame join failed; the failed run is preserved and has not been replaced' }
        $records = @(Get-Content -LiteralPath (Join-Path $folder 'measure.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
        $identity = @($records | Where-Object t -eq 'samples')
        if ($identity.Count -ne 1 -or $identity[0].commit -ne $Commit.Substring(0, 8)) { throw 'The app run reports a different source commit' }
        $inputRecord = @($records | Where-Object t -eq 'json-input')
        $edit = @($records | Where-Object t -eq 'json-edit')
        $probe = @($records | Where-Object t -eq 'desktop-probe')
        $memory = @($records | Where-Object t -eq 'mem')
        if ($inputRecord.Count -ne 1 -or $inputRecord[0].sha256 -ne $FixtureSha256) { throw 'The app read a different fixture' }
        if ($edit.Count -ne 1 -or $edit[0].undoClean -ne $true -or $edit[0].inserted -ne 1) { throw 'The edit and undo check did not complete' }
        $opening = @(Import-Csv -LiteralPath $joined | Where-Object scenario -eq 'json-open')
        if ($opening.Count -ne 1 -or $opening[0].presentation_status -ne 'matched') { throw 'The opening has no verified desktop frame' }
        $row = $opening[0]
        if ($focusQpc -le 0 -or $focusQpc -ge [long]$row.start_qpc) { throw 'Focus was not confirmed before the measured opening began' }
        $rows.Add([pscustomobject]@{
            sample = $sample; run_id = $row.run_id; start_qpc = $row.start_qpc; qpc_frequency = $row.qpc_frequency
            ui_ms = $row.ui_ms; present_qpc = $row.present_qpc; present_ms = $row.present_ms
            capture_read_ms = $row.capture_read_ms; capture_delivery_ms = $row.capture_delivery_ms
            marker = $row.desktop_marker; presentation_status = $row.presentation_status
            clicked_for_focus = $clickedForFocus
            focus_qpc = $focusQpc; focus_click_qpc = $clickQpc
            transparency = $probe[0].transparency
            peak_private_mib = (($memory | Measure-Object peakCommitMiB -Maximum).Maximum).ToString('R', $culture)
            peak_working_set_mib = (($memory | Measure-Object peakWorkingSetMiB -Maximum).Maximum).ToString('R', $culture)
            first_screen_passed = [double]::Parse($row.present_ms, $culture) -le 1000
        })
        $rows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'openings.csv') -NoTypeInformation -Encoding UTF8
        Write-Output ('OPEN {0}/{1} present_ms={2}' -f ($sample + 1), $Count, $row.present_ms)
        $application.Dispose(); $application = $null
        $capture.Dispose(); $capture = $null
    }
    if ((Get-FileHash -LiteralPath $FixturePath).Hash -ne $FixtureSha256) { throw 'The shared fixture changed' }
    $values = @($rows | ForEach-Object { [double]::Parse($_.present_ms, $culture) } | Sort-Object)
    $latencyPassed = @($rows | Where-Object first_screen_passed -eq $false).Count -eq 0
    Write-Json @{
        completed = $rows.Count; requested = $Count; protocol_repetitions_satisfied = $rows.Count -ge 30
        first_screen_passed = $latencyPassed; first_screen_limit_ms = 1000
        p50_ms = $values[[Math]::Ceiling($values.Count * 0.5) - 1]
        p95_ms = $values[[Math]::Ceiling($values.Count * 0.95) - 1]; max_ms = $values[-1]
        fixture_unchanged = $true; scope = 'Opening only; input latency and other R8 machines are separate gates'
    } (Join-Path $OutputDirectory 'result.json')
    if ($latencyPassed) { $exitCode = 0 } else { $exitCode = 2 }
} catch {
    Write-Json @{ error = $_.ToString(); completed = $rows.Count; requested = $Count } (Join-Path $OutputDirectory 'error.json')
    Write-Error $_ -ErrorAction Continue
} finally {
    try { $applicationForced = Finish-OwnedProcess $application $ApplicationPath $true } catch { Write-Warning $_; $exitCode = 1 }
    try { $captureForced = Finish-OwnedProcess $capture $CapturePath $false } catch { Write-Warning $_; $exitCode = 1 }
    if ($applicationForced -or $captureForced) { $exitCode = 1 }
    try { $controller.ProcessorAffinity = $originalAffinity }
    finally {
        Get-ChildItem Env: | Where-Object { $_.Name -like 'TEXTIDA_*' -or $_.Name -in @('TEMP', 'TMP') } | Remove-Item
        foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
        Write-Json @{
            affinity_before = $originalAffinity.ToInt64(); affinity_after = $controller.ProcessorAffinity.ToInt64()
            application_running = $null -ne $application -and -not $application.HasExited
            capture_running = $null -ne $capture -and -not $capture.HasExited
            application_forced = $applicationForced; capture_forced = $captureForced
        } (Join-Path $OutputDirectory 'cleanup.json')
        $controller.Dispose()
    }
}
exit $exitCode
