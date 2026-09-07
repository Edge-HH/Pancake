param([Parameter(Mandatory = $true)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path.TrimEnd('\')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
if ($publish -eq $repository -or $publish -eq [IO.Path]::GetPathRoot($publish).TrimEnd('\') -or
    -not (Test-Path -LiteralPath (Join-Path $publish 'Pancake.exe')) -or (Test-Path -LiteralPath (Join-Path $publish 'data'))) {
    throw '仅能精简不含用户数据的独立发布目录。'
}
$before = (Get-ChildItem -LiteralPath $publish -Recurse -File | Measure-Object Length -Sum).Sum
$cultures = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
[Globalization.CultureInfo]::GetCultures([Globalization.CultureTypes]::AllCultures) | ForEach-Object { $null = $cultures.Add($_.Name) }
# Windows SDK 的这三个旧名称不一定存在于当前 .NET/ICU 的语言清单中。
@('ca-ES-VALENCIA', 'pa-IN', 'quz-PE') | ForEach-Object { $null = $cultures.Add($_) }
$keep = @('en', 'en-US', 'en-GB', 'zh', 'zh-CN', 'zh-TW', 'zh-HK', 'zh-Hans', 'zh-Hant')
$removed = 0
# 只处理 SDK 生成的已知语言子目录；应用资源、运行库、字体和许可证原样保留。
foreach ($directory in Get-ChildItem -LiteralPath $publish -Directory) {
    if (-not $cultures.Contains($directory.Name) -or $directory.Name -in $keep) { continue }
    $resolved = [IO.Path]::GetFullPath($directory.FullName)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $publish -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '语言目录校验失败。' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
    $removed++
}
$after = (Get-ChildItem -LiteralPath $publish -Recurse -File | Measure-Object Length -Sum).Sum
"已精简 $removed 个语言目录：$([math]::Round($before / 1MB, 1)) → $([math]::Round($after / 1MB, 1)) MiB。"
