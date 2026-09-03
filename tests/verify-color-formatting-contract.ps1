$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tileCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Controls\SubjectTileControl.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

$sizedSwatches = [regex]::Matches($tileCode, 'Content\s*=\s*new Ellipse\s*\{\s*Width\s*=')
if ($sizedSwatches.Count -lt 1 -or ([regex]::Matches($tileCode, 'CreateColorSwatch\(hex').Count -lt 2)) {
    $failures.Add('字体、高光或磁贴主题色卡中的 Ellipse 没有明确尺寸，色块会渲染为空。')
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

Write-Output 'PASS: color swatches render and rich-text colors target the preserved selection.'
