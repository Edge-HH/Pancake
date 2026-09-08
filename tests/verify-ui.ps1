param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [switch]$FullScreenHintOnly
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Pancake-ui-test-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
try {
    Get-ChildItem -LiteralPath $source | Where-Object { $_.Name -notin @('data', 'verification', 'startup-ok.txt') } | Copy-Item -Destination $testRoot -Recurse
    $verificationArgument = if ($FullScreenHintOnly) { '--verify-fullscreen-hint' } else { '--verify-ui' }
    $process = Start-Process -FilePath (Join-Path $testRoot 'Pancake.exe') -ArgumentList $verificationArgument -WorkingDirectory $testRoot -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw '隔离 UI 验证在 60 秒内未结束。' }
    $resultName = if ($FullScreenHintOnly) { 'fullscreen-result.txt' } else { 'result.txt' }
    $successMarker = if ($FullScreenHintOnly) { 'FULLSCREEN_HINT_VERIFICATION_OK' } else { 'UI_VERIFICATION_OK' }
    $result = Join-Path $testRoot "verification/$resultName"
    $null = New-Item -ItemType Directory -Path $evidence -Force
    if (Test-Path -LiteralPath (Join-Path $testRoot 'verification')) {
        Copy-Item -Path (Join-Path $testRoot 'verification/*') -Destination $evidence -Force
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result)) {
        $crash = Join-Path $testRoot 'data/crash.log'
        if (Test-Path -LiteralPath $crash) { Get-Content -LiteralPath $crash -Tail 15 }
        throw "隔离 UI 验证启动失败，退出码 $($process.ExitCode)。"
    }
    $lines = Get-Content -LiteralPath $result
    if ($lines[-1] -ne $successMarker) { $lines | Select-Object -Last 12; throw '隔离 UI 回归检查失败。' }
    "PASS: $(@($lines | Where-Object { $_ -like 'PASS:*' }).Count) real-window checks; evidence: $evidence"
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolved) -like 'Pancake-ui-test-*') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
