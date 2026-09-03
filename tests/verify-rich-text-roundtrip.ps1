$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tilePath = Join-Path $projectRoot 'src\Pancake\Controls\SubjectTileControl.cs'
$tileCode = [System.IO.File]::ReadAllText($tilePath)
$failures = [System.Collections.Generic.List[string]]::new()

# RichEditBox 的完整 Document 含一个控件自带的末尾段落标记。保存时必须从 range 中排除它，
# 否则 RTF 每重建一次就会把这个标记固化成一个新的空段落。
if ($tileCode -notmatch 'Document\.GetRange\(0,\s*int\.MaxValue\)' -or
    $tileCode -notmatch 'contentRange\.EndPosition\s*-=' -or
    $tileCode -notmatch 'contentRange\.GetText\(TextGetOptions\.FormatRtf') {
    $failures.Add('富文本保存仍包含 RichEditBox 自动生成的末尾段落标记。')
}

if ($tileCode -notmatch 'RemoveGeneratedTrailingParagraphs' -or
    $tileCode -notmatch 'expectedPlainText') {
    $failures.Add('历史数据中已累积的末尾空段落没有按纯文本内容修复。')
}

$captureMethod = [regex]::Match(
    $tileCode,
    'private void CaptureRichText\(RichEditBox editor, HomeworkEntry homework\)(?<body>[\s\S]*?)\n\s*private static bool RemoveGeneratedTrailingParagraphs')
if (-not $captureMethod.Success -or $captureMethod.Groups['body'].Value -match 'editor\.Document\.GetText') {
    $failures.Add('CaptureRichText 仍在读取完整 Document，切换编辑模式后会再次累积空段落。')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Output 'PASS: rich-text round trips exclude generated trailing paragraphs.'
