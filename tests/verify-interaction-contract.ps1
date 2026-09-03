$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$windowCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\PancakeBoard\MainWindow.xaml.cs'))
$windowXaml = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\PancakeBoard\MainWindow.xaml'))
$tileCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\PancakeBoard\Controls\SubjectTileControl.cs'))
$dataCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\PancakeBoard\Services\AppDataStore.cs'))
$weatherCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\PancakeBoard\Services\XiaomiWeatherService.cs'))
$weatherCatalogCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\PancakeBoard\Services\WeatherCityCatalog.cs'))
$updateCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\PancakeBoard\Services\GitHubUpdateService.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

if ($windowXaml -match 'x:Name="BoardScrollViewer"' -and
    $windowCode -notmatch 'ScrollMode\.Disabled') {
    $failures.Add('若使用 ScrollViewer，编辑期间必须禁用滚动，避免触控被页面滑动抢走。')
}

foreach ($edge in @('Left', 'Right', 'Bottom', 'BottomLeft', 'BottomRight')) {
    if ($tileCode -notmatch "ResizeEdge\.$edge") {
        $failures.Add("磁贴缺少 $edge 边框缩放命中区。")
    }
}

foreach ($edge in @('Top', 'TopLeft', 'TopRight')) {
    if ($tileCode -match "AddResizeHandle\(ResizeEdge\.$edge,") {
        $failures.Add("磁贴顶部仍包含 $edge 高度缩放命中区。")
    }
}

if ($windowCode -notmatch 'SnapToGrid' -or $windowCode -notmatch 'GridSize') {
    $failures.Add('磁贴移动和缩放尚未通过统一网格吸附。')
}

if ($tileCode -notmatch 'internal\s+const\s+double\s+MinimumTileHeight\s*=\s*96\s*;' -or
    ([regex]::Matches($windowCode, 'SubjectTileControl\.MinimumTileHeight')).Count -lt 2) {
    $failures.Add('磁贴最小高度未统一降低到两个 48px 网格单元。')
}

if ($tileCode -notmatch 'PenModeToolbar' -or
    $tileCode -notmatch 'InkColor' -or
    $tileCode -notmatch 'InkThickness' -or
    $tileCode -notmatch 'InkTool\.Eraser') {
    $failures.Add('磁贴下方缺少居中的笔模式栏，或颜色、粗细、橡皮擦状态不完整。')
}

if ($windowXaml -match 'x:Name="BoardScrollViewer"' -or $windowXaml -notmatch 'x:Name="BoardViewport"') {
    $failures.Add('右侧磁贴板仍由 ScrollViewer 承载，画布仍可能被拖动或在大视口中居中留白。')
}

if ($windowCode -notmatch 'GridSize\s*=\s*(4[8-9]|[5-9][0-9])' -or $windowCode -notmatch 'IsGridSnappingEnabled') {
    $failures.Add('粗网格和可关闭的吸附状态尚未实现。')
}

if ($windowXaml -notmatch 'x:Name="GridSnapToggleButton"' -or $windowCode -notmatch 'GridSnapToggleButton_Checked') {
    $failures.Add('底部编辑栏缺少磁贴图标吸附开关。')
}

if ($tileCode -match 'CreateThumbHandle' -or
    $tileCode -notmatch 'HeaderMoveThumb' -or
    $tileCode -notmatch 'Height\s*=\s*EdgeHitTarget' -or
    $tileCode -notmatch 'Children\.Add\(_headerMoveThumb\)') {
    $failures.Add('磁贴移动仍依赖独立按钮，而不是顶部拖动区域。')
}

if ($windowCode -notmatch 'RootShell\.AddHandler' -or $windowCode -notmatch 'ShowFullScreenExitHint' -or $windowCode -notmatch 'FullScreenHintBrush') {
    $failures.Add('全屏退出提示尚未监听全屏幕滑动，或缺少红底白字强调状态。')
}

if ($tileCode -notmatch 'RichEditBox' -or $tileCode -notmatch 'FormatEffect\.Toggle' -or $tileCode -notmatch 'ForegroundColor' -or $tileCode -notmatch 'BackgroundColor' -or $tileCode -notmatch 'UnderlineType') {
    $failures.Add('磁贴内富文本编辑缺少加粗、斜体、下划线、颜色或高光。')
}

if ($tileCode -notmatch 'CreateThemeButton' -or $tileCode -notmatch 'AccentHex') {
    $failures.Add('磁贴主题色切换尚未接入。')
}

if ($dataCode -notmatch 'AppContext\.BaseDirectory' -or $dataCode -notmatch 'pancakeboard\.json' -or $windowCode -notmatch 'ScheduleSave') {
    $failures.Add('布局与配置没有自动保存到软件目录。')
}

if ($weatherCode -notmatch 'weatherapi\.market\.xiaomi\.com' -or $windowCode -notmatch 'ChooseWeatherCityButton_Click') {
    $failures.Add('小米天气或可搜索地区选择窗口尚未接入。')
}

if ($weatherCatalogCode -match 'city\.Code\.Contains' -or
    $weatherCatalogCode -notmatch 'ToString\(\)\s*=>\s*Name' -or
    $windowCode -match '搜索地区名称或编码' -or
    $windowCode -match 'WeatherCityTextBox\.Text\s*=\s*\$"\{_settings\.WeatherCityName\}.*WeatherCityCode') {
    $failures.Add('天气地区选择仍允许按内部编码搜索，或仍向用户显示内部编码。')
}

if ($updateCode -notmatch 'api\.github\.com/repos' -or $windowCode -notmatch 'CheckForUpdatesAsync') {
    $failures.Add('GitHub Release 自动更新尚未接入。')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Output 'PASS: interaction, rich text, local data, weather, and update contracts are present.'
