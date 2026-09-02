$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$windowCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\PancakeBoard\MainWindow.xaml.cs'))
$tileCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\PancakeBoard\Controls\SubjectTileControl.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

if ($windowCode -notmatch 'ApplyViewportInteractionMode' -or
    $windowCode -notmatch 'ScrollMode\.Disabled') {
    $failures.Add('编辑、缩放或绘画时没有禁用外层 ScrollViewer，触控会被页面滑动抢走。')
}

foreach ($edge in @('Left', 'Top', 'Right', 'Bottom', 'TopLeft', 'TopRight', 'BottomLeft', 'BottomRight')) {
    if ($tileCode -notmatch "ResizeEdge\.$edge") {
        $failures.Add("磁贴缺少 $edge 边框缩放命中区。")
    }
}

if ($windowCode -notmatch 'SnapToGrid' -or $windowCode -notmatch 'GridSize') {
    $failures.Add('磁贴移动和缩放尚未通过统一网格吸附。')
}

if ($tileCode -notmatch 'PenModeToolbar' -or
    $tileCode -notmatch 'InkColor' -or
    $tileCode -notmatch 'InkThickness' -or
    $tileCode -notmatch 'InkTool\.Eraser') {
    $failures.Add('磁贴下方缺少居中的笔模式栏，或颜色、粗细、橡皮擦状态不完整。')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Output 'PASS: touch interaction, border resizing, grid snapping, and pen toolbar contracts are present.'
