$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$fonts = Join-Path $projectRoot 'src/Pancake/Assets/Fonts'
$source = Get-Content -Raw (Join-Path $projectRoot 'src/Pancake/Controls/FluentIcon.cs')
$mapping = Get-Content -Raw (Join-Path $fonts 'FluentSystemIcons-Resizable.json') | ConvertFrom-Json -AsHashtable
Add-Type -AssemblyName PresentationCore
$typeface = [System.Windows.Media.GlyphTypeface]::new([Uri]::new((Join-Path $fonts 'FluentSystemIcons-Resizable.ttf')))
$count = 0
foreach ($match in [regex]::Matches($source, 'const string (\w+) = "\\u([0-9A-F]+)"; // (\w+)')) {
    $code = [Convert]::ToInt32($match.Groups[2].Value, 16)
    if ($mapping[$match.Groups[3].Value] -ne $code -or -not $typeface.CharacterToGlyphMap.ContainsKey($code)) { throw "图标映射与内置字体不一致：$($match.Groups[1].Value)" }
    $count++
}
if ($count -lt 20) { throw '图标检查没有覆盖实际映射。' }
$xaml = Get-Content -Raw (Join-Path $projectRoot 'src/Pancake/MainWindow.xaml')
foreach ($match in [regex]::Matches($xaml, 'Symbol="(\w+)"')) {
    if ($source -notmatch ('const string ' + $match.Groups[1].Value + ' =')) { throw 'XAML 引用了未知图标。' }
}
foreach ($name in @('HarmonyOS_Sans_SC_Regular.ttf', 'HarmonyOS_Sans_SC_Bold.ttf', 'HarmonyOS-LICENSE.txt', 'Fluent-LICENSE.txt')) {
    if (-not (Test-Path (Join-Path $fonts $name))) { throw "缺少字体或许可：$name" }
}
Write-Output "PASS: $count Fluent semantic glyphs exist in the bundled font; font licenses are present."
