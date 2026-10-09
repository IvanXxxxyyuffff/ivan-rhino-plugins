param(
    [string]$RhinoExe = 'D:\Rhino 8\System\Rhino.exe',
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$smokeScript = Join-Path $root 'native-panel-smoke.py'
$reportPath = Join-Path $root 'native-panel-smoke.json'
$extendedPath = Join-Path $root 'native-smoke-extended-audit.json'
$ledgerPath = Join-Path $root 'native-run.jsonl'
$checkpointPath = Join-Path $root 'checkpoint.json'
$verificationPath = Join-Path (Split-Path $root -Parent) 'VERIFICATION.txt'

function Write-Checkpoint {
    $checkpointLiteral = [string](Get-Content -LiteralPath $checkpointPath -Raw -Encoding UTF8)
    $state = ConvertFrom-Json -InputObject $checkpointLiteral
    $state.ACTIVE_OBJECT = (Join-Path $root 'MODIFIED_FILE')
    $state.LAST_CONFIRMED_RESULT = 'Native panel smoke launch is the next executable action.'
    $state.NEXT_EXECUTABLE_ACTION = @{
        command = @('powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass',
                    '-File', (Join-Path $root 'run-native-smoke.ps1'),
                    '-RhinoExe', $RhinoExe, '-TimeoutSeconds', [string]$TimeoutSeconds)
    }
    $state.INPUT_PATHS = @($smokeScript, $reportPath, $extendedPath,
                           (Join-Path $root 'native-panel-smoke-serialize-error.txt'))
    $state.ACCEPTANCE_EVENT = 'Fresh 5-panel smoke; WPF Vape style/actions; VapeVolumeSelfTest A/C/D plus E/F and cleanup, preserving B as completed or explicitly skipped with coverage status.'
    $json = $state | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($checkpointPath, $json + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}

function Add-RunEvidence([System.Collections.IDictionary]$Event, [string]$LiteralText) {
    $LiteralText = [string]$LiteralText
    $line = $Event | ConvertTo-Json -Depth 12 -Compress
    [IO.File]::AppendAllText($ledgerPath, $line + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
    $block = @"

NATIVE PANEL SMOKE $($Event.label)
COMMAND: $($Event.command -join ' ')
INPUT: $($Event.input)
PID: $($Event.pid)
PARENT PID: $($Event.parentPid)
RHINO HANDLE: $($Event.rhinoHandle)
EXIT: $($Event.exit)
EXIT CODE CAPTURED: $($Event.exitCodeCaptured) ($($Event.exitCodeSource))
VAPE SELFTEST: $($Event.vapeVolumeSelfTestStatus); fullPass=$($Event.vapeVolumeSelfTestPassed); accepted=$($Event.vapeVolumeSelfTestAcceptanceSatisfied); coverage=$($Event.vapeVolumeAlgorithmCoverage)
STDOUT (literal):
$($Event.stdout)
STDERR (literal):
$($Event.stderr)
REPORT: $($Event.report)
REPORT FRESH: $($Event.reportFresh)
REPORT LITERAL:
$LiteralText
SERIALIZATION ERROR REPORT: $($Event.serializationErrorReport)
SERIALIZATION ERROR LITERAL:
$($Event.serializationErrorLiteral)
"@
    [IO.File]::AppendAllText($verificationPath, $block + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}

$event = [ordered]@{
    label = 'MODIFIED-native-panel-smoke'
    command = @($RhinoExe, '/nosplash', '/runscript=<empty-doc macro: _-New _None _Enter; _-RunPythonScript native-panel-smoke.py _Enter>')
    input = $smokeScript
    pid = $null
    parentPid = $PID
    rhinoHandle = $null
    exit = $null
    exitCodeCaptured = $false
    exitCodeSource = $null
    stdout = ''
    stderr = ''
    report = $reportPath
    reportLiteral = ''
    reportFresh = $false
    serializationErrorReport = (Join-Path $root 'native-panel-smoke-serialize-error.txt')
    serializationErrorLiteral = ''
    serializationErrorFresh = $false
    elapsedSeconds = $null
    timedOut = $false
    freshFiveScreenshots = $false
    vapeVolumeSelfTestPassed = $false
    vapeVolumeSelfTestAcceptanceSatisfied = $false
    vapeVolumeSelfTestStatus = $null
    vapeVolumeAlgorithmCoverage = $null
}

$start = Get-Date
$process = $null
$stdoutTask = $null
$stderrTask = $null
$errorText = $null

try {
    if (-not (Test-Path -LiteralPath $RhinoExe -PathType Leaf)) { throw "Rhino executable not found: $RhinoExe" }
    if (-not (Test-Path -LiteralPath $smokeScript -PathType Leaf)) { throw "Smoke script not found: $smokeScript" }
    if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) {
        throw 'An existing Rhino process is running; refusing to interfere or overlap tests.'
    }
    if ($TimeoutSeconds -lt 60) { throw 'TimeoutSeconds must be at least 60.' }

    # Persist the pending command before launching so a control-only resume can
    # dispatch this exact gate, with the same script and executable paths.
    Write-Checkpoint

    $macro = "_-New _None _Enter _-RunPythonScript $smokeScript _Enter"
    $runScriptArgument = '/runscript="' + $macro + '"'
    $event.command = @($RhinoExe, '/nosplash', $runScriptArgument)
    Write-Output ("START native smoke: {0} {1} {2}" -f $RhinoExe, '/nosplash', $runScriptArgument)

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $RhinoExe
    $startInfo.Arguments = '/nosplash ' + $runScriptArgument
    $startInfo.WorkingDirectory = $root
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    # Force acquisition of the owned process handle before waiting, then drain
    # both redirected streams asynchronously to avoid pipe-buffer deadlocks.
    $event.rhinoHandle = $process.Handle.ToInt64()
    $event.pid = $process.Id
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    while (-not $process.WaitForExit(1000)) {
        if (((Get-Date) - $start).TotalSeconds -ge $TimeoutSeconds) {
            $event.timedOut = $true
            # Only kill the exact Process object this runner created.
            $process.Kill()
            $process.WaitForExit()
            $event.exit = 124
            $event.exitCodeCaptured = $false
            $event.exitCodeSource = 'runner timeout (synthetic status 124; not a child exit code)'
            break
        }
    }
    if (-not $event.timedOut) {
        $process.WaitForExit()
        # Capture ExitCode before Refresh; null is never coerced to success.
        try {
            $capturedExitCode = $process.ExitCode
            if ($null -eq $capturedExitCode) { throw 'Process.ExitCode was null after WaitForExit().' }
            $event.exit = [int]$capturedExitCode
            $event.exitCodeCaptured = $true
            $event.exitCodeSource = 'System.Diagnostics.Process.ExitCode captured before Refresh()'
        } catch {
            $event.exitCodeCaptureError = $_.Exception.Message
            $event.exitCodeCaptured = $false
            $event.exitCodeSource = 'unavailable; no default exit code applied'
            throw
        }
    }
    try { $event.stdout = [string]$stdoutTask.Result } catch { $event.stdoutReadError = $_.Exception.Message }
    try { $event.stderr = [string]$stderrTask.Result } catch { $event.stderrReadError = $_.Exception.Message }
    if (-not $event.timedOut) { try { $process.Refresh() } catch { } }

    if (Test-Path -LiteralPath $reportPath) {
        $reportItem = Get-Item -LiteralPath $reportPath
        if ($reportItem.LastWriteTime -ge $start) {
            $event.reportLiteral = [string](Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8)
            $event.reportFresh = $true
        }
    }
    if ($event.reportLiteral) {
        $reportLiteral = [string]$event.reportLiteral
        $smokeReport = ConvertFrom-Json -InputObject $reportLiteral
        $expectedNames = @('VoronoiTexture', 'StripeOnSurface', 'ParametricTexture', 'RadialDots', 'VapeVolume')
        $pluginNames = @($smokeReport.plugins | ForEach-Object { $_.name })
        $statusesPassed = (@($smokeReport.plugins | Where-Object { $_.status -ne 'passed' }).Count -eq 0)
        $failuresEmpty = (@($smokeReport.failures).Count -eq 0)
        $screenshots = @($expectedNames | ForEach-Object { Join-Path $root ('native-smoke-' + $_.ToLowerInvariant() + '.png') })
        $event.freshFiveScreenshots = ($pluginNames.Count -eq 5 -and
            (@($expectedNames | Where-Object { $pluginNames -notcontains $_ }).Count -eq 0) -and
            $statusesPassed -and
            (@($screenshots | Where-Object { -not (Test-Path -LiteralPath $_) -or (Get-Item -LiteralPath $_).LastWriteTime -lt $start }).Count -eq 0))
        $vapeResult = $smokeReport.vapeVolumeSelfTest
        $vapeSelfTestPassed = ($vapeResult -and $vapeResult.passed -eq $true)
        $vapeSelfTestAcceptable = ($vapeResult -and $vapeResult.acceptanceSatisfied -eq $true)
        $event.vapeVolumeSelfTestPassed = [bool]$vapeSelfTestPassed
        $event.vapeVolumeSelfTestAcceptanceSatisfied = [bool]$vapeSelfTestAcceptable
        $event.vapeVolumeSelfTestStatus = [string]$vapeResult.status
        $event.vapeVolumeAlgorithmCoverage = [string]$vapeResult.algorithmCoverage
        $event.reportFailuresEmpty = [bool]$failuresEmpty
        $event.acceptancePassed = ($event.reportFresh -and $event.freshFiveScreenshots -and $vapeSelfTestAcceptable -and
            $failuresEmpty -and $event.exitCodeCaptured -and $event.exit -eq 0 -and -not $event.timedOut)
    } else {
        $event.freshFiveScreenshots = $false
        $event.vapeVolumeSelfTestPassed = $false
        $event.reportFailuresEmpty = $false
        $event.vapeVolumeSelfTestAcceptanceSatisfied = $false
        $event.acceptancePassed = $false
    }
} catch {
    $errorText = $_.Exception.Message
    $event.launchError = $errorText
    if ($process) {
        try {
            if (-not $process.HasExited) {
                $event.timedOut = $true
                $process.Kill()
                $process.WaitForExit()
                if ($null -eq $event.exit) { $event.exit = 124 }
                $event.exitCodeSource = 'runner terminated its owned process after an exception (synthetic status 124)'
            } elseif (-not $event.exitCodeCaptured) {
                $capturedExitCode = $process.ExitCode
                if ($null -ne $capturedExitCode) {
                    $event.exit = [int]$capturedExitCode
                    $event.exitCodeCaptured = $true
                    $event.exitCodeSource = 'System.Diagnostics.Process.ExitCode captured in catch before disposal'
                }
            }
        } catch {
            $event.exitCodeCaptureError = $_.Exception.Message
        }
    }
    try { if ($stdoutTask) { $event.stdout = [string]$stdoutTask.Result } } catch { $event.stdoutReadError = $_.Exception.Message }
    try { if ($stderrTask) { $event.stderr = [string]$stderrTask.Result } } catch { $event.stderrReadError = $_.Exception.Message }
    if (-not $event.Contains('acceptancePassed')) { $event.acceptancePassed = $false }
} finally {
    $event.elapsedSeconds = [Math]::Round(((Get-Date) - $start).TotalSeconds, 2)
    if (-not $event.reportLiteral -and (Test-Path -LiteralPath $reportPath)) {
        try {
            $reportItem = Get-Item -LiteralPath $reportPath
            if ($reportItem.LastWriteTime -ge $start) {
                $event.reportLiteral = [string](Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8)
                $event.reportFresh = $true
            }
        } catch { }
    }
    if (Test-Path -LiteralPath $event.serializationErrorReport) {
        try {
            $serializationErrorItem = Get-Item -LiteralPath $event.serializationErrorReport
            if ($serializationErrorItem.LastWriteTime -ge $start) {
                $event.serializationErrorLiteral = [string](Get-Content -LiteralPath $event.serializationErrorReport -Raw -Encoding UTF8)
                $event.serializationErrorFresh = $true
            }
        } catch { }
    }
    Add-RunEvidence $event ([string]$event.reportLiteral)
}

if ($errorText) { throw $errorText }
if ($null -eq $event.exit) { $displayExit = '<unavailable>' } else { $displayExit = [string]$event.exit }
Write-Output ("EXIT {0} (captured={1}); freshScreenshots={2}; VapeVolumeSelfTest={3}, fullPass={4}, accepted={5}, coverage={6}; acceptancePassed={7}" -f `
    $displayExit, $event.exitCodeCaptured, $event.freshFiveScreenshots, $event.vapeVolumeSelfTestStatus,
    $event.vapeVolumeSelfTestPassed, $event.vapeVolumeSelfTestAcceptanceSatisfied,
    $event.vapeVolumeAlgorithmCoverage, $event.acceptancePassed)
if (-not $event.acceptancePassed) { throw 'Native smoke did not meet all acceptance checks; inspect native-run.jsonl and VERIFICATION.txt.' }
