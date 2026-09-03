$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$themePath = Join-Path $projectRoot 'src\PancakeBoard\Themes\ThemeResources.xaml'
$sourcePaths = @(
    (Join-Path $projectRoot 'src\PancakeBoard\MainWindow.xaml'),
    (Join-Path $projectRoot 'src\PancakeBoard\Controls\SubjectTileControl.cs')
)

$theme = Get-Content -Raw $themePath
if ($theme -notmatch '<FontFamily x:Key="BoardIconFontFamily">Segoe MDL2 Assets</FontFamily>') {
    throw 'BoardIconFontFamily must use the Windows 10-compatible Segoe MDL2 Assets font.'
}

$source = ($sourcePaths | ForEach-Object { Get-Content -Raw $_ }) -join "`n"
if ($source -match 'Segoe Fluent Icons') {
    throw 'A Windows 11-only Segoe Fluent Icons dependency remains in the icon source.'
}

$glyphHexValues = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($match in [regex]::Matches($source, '(?:&#x|\\u)([EeFf][0-9A-Fa-f]{3})')) {
    [void]$glyphHexValues.Add($match.Groups[1].Value)
}

if ($glyphHexValues.Count -eq 0) {
    throw 'No icon glyphs were found; the contract check is not exercising the icon source.'
}

Add-Type -AssemblyName PresentationCore
$fontPath = Join-Path $env:WINDIR 'Fonts\segmdl2.ttf'
if (-not (Test-Path -LiteralPath $fontPath)) {
    throw "Windows 10-compatible icon font was not found at $fontPath."
}

$typeface = [System.Windows.Media.GlyphTypeface]::new([Uri]::new($fontPath))
$missing = foreach ($hex in $glyphHexValues) {
    $codePoint = [Convert]::ToInt32($hex, 16)
    if (-not $typeface.CharacterToGlyphMap.ContainsKey($codePoint)) {
        "U+$hex"
    }
}

if ($missing) {
    throw "Segoe MDL2 Assets does not contain these icon glyphs: $($missing -join ', ')."
}

Write-Host "ICON_FONT_CONTRACT_OK ($($glyphHexValues.Count) glyphs)"
