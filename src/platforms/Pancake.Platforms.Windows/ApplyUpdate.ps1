param([Parameter(Mandatory = $true)][string]$Configuration, [switch]$SkipRestart)
$ErrorActionPreference = 'Stop'
$configPath = [IO.Path]::GetFullPath($Configuration)
$stage = Split-Path -Parent $configPath
$log = Join-Path $stage 'update.log'
$changed = [Collections.Generic.List[object]]::new()
$installed = $false
$canRestart = $false
try {
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $target = [IO.Path]::GetFullPath($config.Target).TrimEnd('\')
    $payload = [IO.Path]::GetFullPath($config.Payload).TrimEnd('\')
    if ($payload -ne (Join-Path $stage 'payload')) { throw 'Invalid payload directory.' }
    $parent = Get-Process -Id $config.ParentProcessId -ErrorAction SilentlyContinue
    if ($parent -and -not $parent.WaitForExit(60000)) { throw 'The application did not exit; no files were replaced.' }
    $canRestart = $true
    $files = @(Get-ChildItem -LiteralPath $payload -Recurse -File)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($payload.Length + 1)
        $destination = [IO.Path]::GetFullPath((Join-Path $target $relative))
        if (-not $destination.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase) -or $relative -match '^data([\/]|$)') { throw 'Invalid update path.' }
        # 不追踪安装目录内的目录联接，也不覆盖用户数据。
        $ancestor = $destination
        while ($ancestor.Length -ge $target.Length) {
            if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Update target contains a link.' }
            $ancestor = [IO.Path]::GetDirectoryName($ancestor)
        }
        $backup = Join-Path (Join-Path $stage 'backup') $relative
        $existed = Test-Path -LiteralPath $destination -PathType Leaf
        if ($existed) {
            $null = New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force
            Copy-Item -LiteralPath $destination -Destination $backup
        }
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        $changed.Add([pscustomobject]@{ Destination = $destination; Backup = $backup; Existed = $existed })
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    $installed = $true
    'UPDATE_OK' | Set-Content -LiteralPath $log -Encoding UTF8
}
catch {
    $failure = $_.ToString()
    # 只恢复本次已触及的文件；保留备份和日志供恢复，不递归删除安装目录。
    for ($index = $changed.Count - 1; $index -ge 0; $index--) {
        $item = $changed[$index]
        try {
            if ($item.Existed) { Copy-Item -LiteralPath $item.Backup -Destination $item.Destination -Force }
            elseif (Test-Path -LiteralPath $item.Destination -PathType Leaf) { Remove-Item -LiteralPath $item.Destination }
        }
        catch { $failure += "`nRollback: $_" }
    }
    ("UPDATE_FAILED`n" + $failure) | Set-Content -LiteralPath $log -Encoding UTF8
}
if (-not $SkipRestart -and $canRestart -and $target) {
    try { Start-Process -FilePath (Join-Path $target 'Pancake.exe') -WorkingDirectory $target -WindowStyle Hidden }
    catch { ("Restart: " + $_) | Add-Content -LiteralPath $log -Encoding UTF8 }
}
if (-not $installed) { exit 1 }
