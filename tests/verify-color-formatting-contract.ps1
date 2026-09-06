$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tileCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Controls\SubjectTileControl.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

$swatchBlock = [regex]::Match($tileCode, 'private static Button CreateColorSwatch[\s\S]*?private static TextBox CreateInlineEditor').Value
if ($swatchBlock -notmatch 'Background\s*=\s*new SolidColorBrush\(BoardTheme\.DisplayContentColor\(MainViewModel\.BrushFromHex\(hex\)\.Color\)\)' -or
    $swatchBlock -notmatch 'Width\s*=\s*size' -or
    $swatchBlock -notmatch 'Height\s*=\s*size') {
    $failures.Add('字体、高光或磁贴主题色卡没有使用铺满按钮的完整方形色块。')
}

$clearHighlightBlock = [regex]::Match($tileCode, 'Button clearHighlight[\s\S]*?colors\.Children\.Add\(clearHighlight\);').Value
if ($clearHighlightBlock -notmatch 'BackgroundColor\s*=\s*TextConstants\.AutoColor') {
    $failures.Add('取消高光没有恢复 RichEdit 自动背景色，可能显示成黑色底。')
}
if ($clearHighlightBlock -notmatch 'CreateColorSwatch\("#FFFFFF"' -or
    $clearHighlightBlock -notmatch 'new Line' -or
    $clearHighlightBlock -notmatch 'X1\s*=\s*2' -or
    $clearHighlightBlock -notmatch 'Y1\s*=\s*2' -or
    $clearHighlightBlock -notmatch 'X2\s*=\s*28' -or
    $clearHighlightBlock -notmatch 'Y2\s*=\s*28' -or
    $clearHighlightBlock -notmatch 'BrushFromHex\("#EF4444"\)') {
    $failures.Add('取消高光色卡缺少默认底色和左上到右下的红色斜线。')
}

$richEditorBlock = [regex]::Match($tileCode, 'private RichEditBox CreateRichEditor[\s\S]*?private StackPanel BuildFormattingToolbar').Value
if ($richEditorBlock -match 'Foreground\s*=') {
    $failures.Add('RichEditBox 设置了控件级白色 Foreground，会覆盖字符级颜色。')
}

if ($tileCode -notmatch 'GetRange\(' -or $tileCode -notmatch 'StartPosition' -or $tileCode -notmatch 'EndPosition') {
    $failures.Add('打开色卡前没有保存并恢复富文本选区，点击色卡后颜色会作用到错误位置。')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Output 'PASS: color swatches render, clear highlighting correctly, and target the preserved selection.'
