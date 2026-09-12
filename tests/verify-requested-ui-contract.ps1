$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$xaml = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.xaml'))
$window = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.xaml.cs'))
$settings = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.Settings.cs'))
$previews = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.SettingsPreviews.cs'))
$layout = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.Layout.cs'))
$widgetLayout = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\WidgetLayout.cs'))
$projects = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.Projects.cs'))
$tiles = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Controls\SubjectTileControl.cs'))
$state = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\ProjectState.cs'))
$alert = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\NoiseAlertPlayer.cs'))
$update = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\GitHubUpdateService.cs'))
$theme = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Themes\ThemeResources.xaml'))
$backdrop = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Controls\PersistentMicaBackdrop.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

if ($xaml -match 'Text="今日作业"' -or $xaml -match 'x:Name="BoardModeHint"') {
    $failures.Add('作业板上方的重复标题区域仍未删除。')
}
if ($xaml -notmatch 'x:Name="ProjectNameText"' -or $xaml -notmatch 'x:Name="SubjectCountText"[\s\S]*?BoardTextMutedBrush') {
    $failures.Add('项目名后没有显示浅灰色科目数量。')
}
if ($projects -match 'ProjectCommands\.Visibility\s*=\s*_isEditing') {
    $failures.Add('项目与文件选项仍只在编辑模式显示。')
}
$toolbar = [regex]::Match($xaml, 'x:Name="FloatingToolbar"[\s\S]*?</Border>').Value
$pen = $toolbar.IndexOf('x:Name="GlobalPenButton"')
$discard = $toolbar.IndexOf('x:Name="DiscardEditButton"')
$save = $toolbar.IndexOf('x:Name="EditBoardButton"')
if ($pen -lt 0 -or $discard -lt 0 -or $save -lt 0 -or $pen -gt $discard -or $discard -gt $save) {
    $failures.Add('底栏没有按画笔第一、放弃倒数第二、保存最后排列。')
}
if ($toolbar -notmatch 'GlobalPenButton[\s\S]*?FluentIcon[^>]+Symbol="Pen"') {
    $failures.Add('底栏画笔没有使用图标。')
}
if ($xaml -notmatch 'x:Name="AutoArrangeButton"' -or $window -notmatch 'AutoArrangeButton_Click') {
    $failures.Add('编辑模式缺少自动排列按钮。')
}
if ($window -notmatch '是否放弃本次更改') {
    $failures.Add('放弃修改前没有确认提示。')
}
$swatches = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Controls\ColorSwatchButton.cs'))
if ($tiles -notmatch 'ColorSwatchButton' -or $projects -notmatch 'ColorSwatchButton' -or $swatches -notmatch 'ButtonBackgroundPointerOver') {
    $failures.Add('富文本、磁贴和画笔色卡没有固定悬停颜色。')
}
if ($state -notmatch 'NoiseAlertVolume' -or $xaml -notmatch 'NoiseAlertVolumeSlider' -or $alert -notmatch 'public\s+float\s+Volume') {
    $failures.Add('吵闹提示音缺少持久化音量设置。')
}
if ($update -notmatch 'EndsWith\("\.zip"' -or $window -notmatch '便携版') {
    $failures.Add('更新器仍无法识别 Release 中的便携版 ZIP。')
}
if ($theme -notmatch 'NavigationViewContentGridCornerRadius">0</CornerRadius>' -or
    ([regex]::Matches($theme, 'NavigationView(Default|Expanded)PaneBackground" ResourceKey="LayerOnMicaBaseAltFillColorTransparentBrush"')).Count -ne 2 -or
    $xaml -notmatch 'x:Name="RootShell" Background="Transparent"' -or
    $xaml -notmatch 'x:Name="DisplayRoot"[^>]+Background="\{ThemeResource BoardBackgroundBrush\}"' -or
    $window -notmatch 'FindNamedDescendant<SplitView>\(SettingsRoot, "RootSplitView"\)' -or
    $window -notmatch 'splitView\.CornerRadius\s*=\s*new CornerRadius\(0\)' -or
    $window -notmatch 'SystemBackdrop\s*=\s*new PersistentMicaBackdrop\(\)' -or
    $backdrop -notmatch 'IsInputActive\s*=\s*true' -or
    ([regex]::Matches($backdrop, 'GetDefaultSystemBackdropConfiguration')).Count -ne 1 -or
    $backdrop -match 'ApplySystemConfiguration\(target,\s*xamlRoot\)') {
    $failures.Add('设置页没有透出系统 Mica，或导航栏与内容区仍有圆角。')
}
if ($xaml -notmatch 'Content="外观"[^>]+SelectsOnInvoked="False"[\s\S]*?NavigationViewItem\.MenuItems' -or
    $xaml -notmatch 'Content="磁贴" Tag="AppearanceTile"' -or
    $xaml -notmatch 'Content="磁贴" Tag="AppearanceTile"[^\r\n]*<icons:FluentIcon Symbol="Board"' -or
    $xaml -notmatch 'Content="背景板" Tag="AppearanceBackground"' -or
    $xaml -notmatch 'Content="天气" Tag="ComponentsWeather"' -or
    $xaml -match 'x:Name="AppearanceNavItem"[^>]+IsSelected=' -or
    $window -notmatch 'SettingsRoot\.SelectedItem\s*=\s*AppearanceNavItem' -or
    $settings -match 'SetTabs\(' -or
    $settings -match 'Orientation\s*=\s*Orientation\.Horizontal[\s\S]*?ToggleButton tab') {
    $failures.Add('设置子页面仍未移入左侧层级导航，或内容区仍保留横向分页按钮。')
}
if ($xaml -notmatch 'x:Name="ClockComponentsView"' -or
    $xaml -notmatch 'x:Name="ClockComponents" Orientation="Horizontal"' -or
    $layout -notmatch '_settings\.LayoutMode is "Split" or "Clock"' -or
    $layout -notmatch 'AddCenteredWidgetInteraction\(' -or
    $layout -notmatch 'ClockContentView\.Child\?\.DesiredSize' -or
    $xaml -notmatch 'SizeChanged="ClockContent_SizeChanged"' -or
    $widgetLayout -notmatch 'MoveVerticallyCentered' -or
    $widgetLayout -notmatch 'ResizeCentered') {
    $failures.Add('分屏和仅时钟模式缺少锁定中轴线的时钟与整排组件编辑。')
}
if ($xaml -notmatch 'Content="网格" Tag="AppearanceGrid"' -or
    $xaml -notmatch 'Content="网格" Tag="AppearanceGrid"[^\r\n]*<icons:FluentIcon Symbol="Grid"' -or
    $settings -notmatch '编辑时显示常规网格' -or
    $window -notmatch 'GridAppearance\.EffectiveStyle' -or
    $window -notmatch 'style == "Dots"' -or
    $window -notmatch 'style == "None"') {
    $failures.Add('外观设置缺少独立于吸附逻辑的网格、点阵和隐藏样式。')
}
if ($theme -notmatch 'x:Key="WidgetSelectionBorderStyle"' -or
    $theme -notmatch 'Glyph="&#xE0D1;"' -or
    $layout -notmatch 'WidgetSelectionBorderStyle' -or
    $layout -notmatch 'PointerEntered[\s\S]{0,160}SetSelected\(true\)' -or
    $layout -notmatch 'SetSelected\(pointerOver\)') {
    $failures.Add('组件被选中时没有显示选中框，或右下角缩放手柄没有显示指向右下角的箭头。')
}
if ($xaml -notmatch 'x:Name="SettingsContentHost"' -or
    $xaml -notmatch 'x:Name="SettingsPreviewHost" Grid\.Column="1"' -or
    $previews -notmatch 'SettingsPreviewHost\.Children\.Add' -or
    $previews -notmatch 'TileStickyAppearancePreview' -or
    $previews -notmatch 'available - panelWidth >= MinimumSettingsColumnWidth' -or
    $previews -notmatch 'Thickness\(16\)' -or
    $previews -notmatch 'TilePreviewLightOverlayOpacity' -or
    $previews -notmatch 'Through hardships to the stars\.' -or
    $previews -notmatch '当有一天你不再纠结于答案 当我们又重逢于天涯或沧海' -or
    $previews -notmatch 'ColorPalette\.Resolve\(TileSampleHighlightHex\)' -or
    $previews -notmatch 'ColorPalette\.IsMacaron\}"') {
    $failures.Add('磁贴设置页缺少右侧固定预览、放大铺满的背景、浅色提亮，或示例文案与配色没有跟随当前色系。')
}

# 编辑模式的富文本 / 画笔悬浮岛、作业板缩放悬浮岛都挂在控制窗旁边，尺寸与样式跟随控制窗。
$islands = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.Islands.cs'))
if ($xaml -notmatch 'x:Name="RichTextIsland"' -or
    $xaml -notmatch 'x:Name="RichTextToolbarHost"' -or
    $xaml -notmatch 'x:Name="ZoomIsland"' -or
    $xaml -notmatch 'x:Name="ZoomIslandItems"' -or
    $xaml -match 'x:Name="RichTextToolbar"[\s\S]{0,200}?Grid\.Column="2"' -or
    $window -notmatch 'ShowRichTextToolbar\(toolbar\)' -or
    $window -notmatch 'InitializeFloatingIslands\(\)' -or
    $settings -notmatch 'RefreshIslands\(\)' -or
    $settings -notmatch 'ToolbarCrossSize\(') {
    $failures.Add('富文本编辑选项没有以悬浮岛的形式移到控制窗旁边，或控制窗尺寸没有同步到悬浮岛。')
}
if ($islands -notmatch 'ApplyIslandPlacement' -or
    $islands -notmatch 'ControlBounds\(\)' -or
    $islands -notmatch 'FitVerticalBand' -or
    $islands -notmatch 'IslandBorderThickness' -or
    $islands -notmatch 'EditIsland' -or
    $islands -notmatch 'label\.Visibility = _settings\.ToolbarIconOnly' -or
    $islands -notmatch 'AutomationProperties\.SetName\(button, name\)' -or
    $islands -notmatch 'MinimumBoardZoom' -or
    $islands -notmatch 'MaximumBoardZoom' -or
    $islands -notmatch 'SetBoardZoom' -or
    $islands -notmatch 'BoardScroller\.ChangeView\(null, null') {
    $failures.Add('悬浮岛缺少左侧 / 上方贴边、与控制窗等高、图下说明字、画笔替换或作业板缩放逻辑。')
}
# 缩放岛要有连续缩放滑条、只在编辑模式出现，并固定在控制窗右侧（竖版控制窗时下方）。
if ($islands -notmatch 'private Slider\? _zoomSlider' -or
    $islands -notmatch 'ZoomIslandItems\.Children\.Add\(_zoomSlider\)' -or
    $islands -notmatch '_zoomSlider\.ValueChanged' -or
    $islands -notmatch 'ZoomIsland\.Visibility = boardVisible && _isEditing' -or
    $islands -notmatch 'bool zoomBefore = false;') {
    $failures.Add('缩放悬浮岛缺少滑条、没有限制在编辑模式，或没有固定在控制窗右侧（竖版控制窗时下方）。')
}
# 竖版控制窗：编辑工具在上、缩放在下；放不下时先挪动控制窗让位，富文本工具条仍放不下就整块退回左右排布。
# 让位逻辑用 smallestTop / largestTop 表示可用区间，富文本工具条放不下时返回 false（退回左右），否则按居中位置挪动控制窗。
if ($islands -notmatch 'ApplyIslandControlShift' -or
    $islands -notmatch 'double smallestTop' -or
    $islands -notmatch 'double largestTop' -or
    $islands -notmatch 'smallestTop > largestTop && ReferenceEquals\(EditIsland, RichTextIsland\)\) return false' -or
    $islands -notmatch 'ApplyIslandControlShift\(top - centered\)' -or
    $islands -notmatch 'ApplyFontPickerAppearance' -or
    $islands -notmatch 'CreateFontPickerButton' -or
    $islands -notmatch 'nameof\(FluentGlyphs\.TextFont\)' -or
    $islands -notmatch 'flyout\.Content = picker' -or
    $islands -notmatch 'flyout\.Content = null') {
    $failures.Add('竖版悬浮岛没有按上编辑下缩放排布、没有挪动控制窗让位，或字体选择框没有收成图标浮窗。')
}
$icons = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Controls\FluentIcon.cs'))
if ($icons -notmatch 'nameof\(ZoomIn\) => ZoomIn' -or $icons -notmatch 'nameof\(ZoomOut\) => ZoomOut') {
    $failures.Add('缩放悬浮岛缺少随字体分发的放大 / 缩小图标映射。')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Output 'PASS: requested toolbar, palette, settings navigation, project header, noise volume, discard, arrange, and update contracts are present.'
