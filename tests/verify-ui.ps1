param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [switch]$FullScreenHintOnly,
    [switch]$BackgroundMediaOnly,
    [switch]$MediaPerformanceOnly
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Pancake-ui-test-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
try {
    Get-ChildItem -LiteralPath $source | Where-Object { $_.Name -notin @('data', 'verification', 'startup-ok.txt') } | Copy-Item -Destination $testRoot -Recurse
    if ($BackgroundMediaOnly -or $MediaPerformanceOnly) {
        # 生成可重复的视频样本，不依赖网络或用户的媒体库。
        $duration = if ($MediaPerformanceOnly) { 8 } else { 2 }
        & ffmpeg -hide_banner -loglevel error -f lavfi -i 'testsrc=size=1280x720:rate=60' -t $duration -c:v libx264 -pix_fmt yuv420p -y (Join-Path $testRoot 'media-test.mp4')
        if ($LASTEXITCODE -ne 0) { throw '测试视频生成失败，需要可用的 ffmpeg。' }
    }
    $verificationArgument = if ($MediaPerformanceOnly) { '--verify-media-performance' } elseif ($BackgroundMediaOnly) { '--verify-background-media' } elseif ($FullScreenHintOnly) { '--verify-fullscreen-hint' } else { '--verify-ui' }
    $process = Start-Process -FilePath (Join-Path $testRoot 'Pancake.exe') -ArgumentList $verificationArgument -WorkingDirectory $testRoot -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw '隔离 UI 验证在 60 秒内未结束。' }
    $resultName = if ($MediaPerformanceOnly) { 'performance-result.txt' } elseif ($BackgroundMediaOnly) { 'media-result.txt' } elseif ($FullScreenHintOnly) { 'fullscreen-result.txt' } else { 'result.txt' }
    $successMarker = if ($MediaPerformanceOnly) { 'MEDIA_PERFORMANCE_VERIFICATION_OK' } elseif ($BackgroundMediaOnly) { 'BACKGROUND_MEDIA_VERIFICATION_OK' } elseif ($FullScreenHintOnly) { 'FULLSCREEN_HINT_VERIFICATION_OK' } else { 'UI_VERIFICATION_OK' }
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
