$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$windowCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.xaml.cs'))
$mediaCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.Media.cs'))
$mediaLibraryCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\MediaLibrary.cs'))
$windowXaml = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.xaml'))
$tileCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Controls\SubjectTileControl.cs'))
$attachmentImageCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Controls\AttachmentImageControl.cs'))
$dataCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\AppDataStore.cs'))
$weatherCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\XiaomiWeatherService.cs'))
$weatherCatalogCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\WeatherCityCatalog.cs'))
$updateCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\GitHubUpdateService.cs'))
$autofillServiceCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\AutofillService.cs'))
$autofillPopupCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Controls\AutofillPopup.cs'))
$autofillInputCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Controls\AutofillInputs.cs'))
$projectCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.Projects.cs'))
$settingsCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\MainWindow.Settings.cs'))
$stateCode = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'src\Pancake\Services\ProjectState.cs'))
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

if ($windowXaml -notmatch 'GlobalInkToolbar' -or $tileCode -notmatch 'SetInkMode' -or
    $tileCode -match 'Viewbox inkView' -or $tileCode -notmatch '_inkSettings.Eraser') {
    $failures.Add('全局画笔栏、共享工具状态或固定坐标画布缺失。')
}

if ($windowXaml -match 'x:Name="BoardScrollViewer"' -or $windowXaml -notmatch 'x:Name="BoardViewport"') {
    $failures.Add('右侧磁贴板仍由 ScrollViewer 承载，画布仍可能被拖动或在大视口中居中留白。')
}

if ($windowCode -notmatch 'GridSize\s*=>\s*Math.Clamp\(_settings.GridSize' -or $windowCode -notmatch 'IsGridSnappingEnabled') {
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

if (($windowCode + $mediaCode) -match 'FileTypeFilter\.Add\("\*"\)' -or
    $mediaCode -notmatch 'MediaPicker\(false\)' -or
    $mediaLibraryCode -notmatch 'ImageExtensions.*"\.png"' -or
    $tileCode -notmatch 'AttachmentImageControl') {
    $failures.Add('图片选择器仍允许非图片文件，或图片没有直接显示在磁贴中。')
}

if ($attachmentImageCode -notmatch 'ManipulationModes\.TranslateX' -or
    $attachmentImageCode -notmatch 'Microsoft\.UI\.Input\.PointerDeviceType\.Mouse') {
    $failures.Add('磁贴图片缺少中央拖拽移动交互。')
}

if (([regex]::Matches($attachmentImageCode, 'AddResizeHandle\(')).Count -lt 8 -or
    $attachmentImageCode -notmatch 'ResizeDelta' -or
    $attachmentImageCode -match 'PointerWheelChanged') {
    $failures.Add('图片必须通过四角和四边中点拖拽缩放，不能依赖滚轮缩放。')
}

if ($attachmentImageCode -notmatch 'Name = "SelectionBorder"' -or
    $attachmentImageCode -notmatch 'Grid\.SetRow\(_toolbar, 1\)' -or
    $attachmentImageCode -notmatch 'CreateIconButton' -or
    $attachmentImageCode -notmatch 'RotateImage\(90\)' -or
    $attachmentImageCode -notmatch 'RotateImage\(-90\)') {
    $failures.Add('点击图片后必须显示选中框和下方的图标控件。')
}
if ($dataCode -notmatch 'Scale = a\.Scale' -or
    $dataCode -notmatch 'OffsetX = a\.OffsetX' -or
    $dataCode -notmatch 'ViewportHeight = a\.ViewportHeight') {
    $failures.Add('图片缩放、位置或裁切视窗没有持久化。')
}

if ($dataCode -notmatch 'AppContext\.BaseDirectory' -or $dataCode -notmatch 'pancake\.json' -or $windowCode -notmatch 'ScheduleSave') {
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

if ($autofillPopupCode -notmatch 'public void AttachTo\(Panel host\)' -or
    $windowCode -notmatch '_autofillPopup\.AttachTo\(RootShell\)' -or
    $tileCode -match '_autofillPopup\.Parent') {
    $failures.Add('补全候选浮层没有挂在窗口根面板上，会被磁贴裁剪或缩放影响。')
}

if ($windowCode -notmatch 'RootShell_PreviewKeyDown' -or
    $windowCode -notmatch 'UIElement\.PreviewKeyDownEvent' -or
    $windowCode -notmatch '_autofillPopup\.HandleKey' -or
    $autofillPopupCode -notmatch 'VirtualKey\.Up' -or
    $autofillPopupCode -notmatch 'VirtualKey\.Tab' -or
    $autofillPopupCode -notmatch 'VirtualKey\.Escape' -or
    $autofillInputCode -notmatch 'WriteWatch') {
    $failures.Add('补全候选缺少窗口级上下键切换、Tab 采纳、Esc 关闭或输入法覆盖后的补写处理。')
}

# 同一个按键处理器同时挂 PreviewKeyDown 与 KeyDown 时，一次按键会被处理两遍，上下键先加一又减一，看起来完全切不动。
if (([regex]::Matches($windowCode, 'RootShell_PreviewKeyDown\)').Count) -ne 1 -or
    $windowCode -match 'AddHandler\(UIElement\.KeyDownEvent,\s*new KeyEventHandler\(RootShell_PreviewKeyDown\)') {
    $failures.Add('补全按键处理器被挂到多个按键事件上，一次按键会被处理多次。')
}

# 浮层是代码创建的控件，不会随主题资源引用自动换色：必须显式按主题字典取色，否则浅色模式会保留深色背景。
if ($autofillPopupCode -notmatch 'ApplyTheme\(' -or
    $autofillPopupCode -notmatch 'ThemeDictionaries' -or
    $autofillPopupCode -notmatch '_rowTextBrush') {
    $failures.Add('补全浮层没有按当前主题重新取色，浅色模式下会深底配黑字。')
}

if ($autofillInputCode -notmatch 'public void Flush\(\)' -or
    $tileCode -notmatch 'foreach \(HomeworkAutofillInput input in _entryAutofills\) input\.Flush\(\)' -or
    $autofillInputCode -notmatch 'string\.Equals\(content, _counted') {
    $failures.Add('作业名称统计没有在编辑结束时结算，或没有按文本变化去重。')
}

if ($autofillInputCode -notmatch 'DefaultSubjectTitle' -or
    $autofillServiceCode -notmatch 'RecommendSubjects' -or
    $settingsCode -notmatch 'AskHomeworkRenameAsync' -or
    ([regex]::Matches($settingsCode, '刷新列表')).Count -lt 2) {
    $failures.Add('默认标题清空推荐、作业名称编辑/屏蔽或两个列表的刷新入口尚未接入。')
}

if ($projectCode -match 'RegisterSubjectNames' -or $autofillServiceCode -match 'RegisterSubjectNames') {
    $failures.Add('学科库仍会自动收录项目里的科目。')
}

if ($settingsCode -notmatch 'RegisterSettingsPage\("AutofillSubject"' -or
    $settingsCode -notmatch 'RegisterSettingsPage\("AutofillHomework"' -or
    $windowXaml -notmatch 'AutofillSettingsPanel' -or
    $windowXaml -notmatch 'Tag="AutofillSubject"') {
    $failures.Add('设置的自动填充分组、学科补全或作业补全页面尚未接入。')
}

if ($settingsCode -notmatch 'ToggleButton randomToggle' -or
    $settingsCode -notmatch 'RandomSubjectColorBrush' -or
    $autofillServiceCode -notmatch 'ResolveSubjectColor' -or
    $stateCode -notmatch 'public bool RandomColor \{ get; set; \}' -or
    $windowCode -notmatch '_refreshSubjectAutofill\?\.Invoke\(\)') {
    $failures.Add('学科颜色没有始终按当前色系显示，或缺少随机配色按钮与切换色系后的刷新。')
}

if ($stateCode -notmatch 'public AutofillSettings Autofill \{ get; set; \}' -or
    $stateCode -notmatch 'public bool Enabled \{ get; set; \}\s*// 匹配程度' -or
    $stateCode -notmatch 'public bool Enabled \{ get; set; \}\r?\n\s*public bool AutoRecord' -or
    $autofillServiceCode -notmatch 'PendingExpiryDays = 14' -or
    $autofillServiceCode -notmatch 'PromotedExpiryDays = 90') {
    $failures.Add('自动填充设置没有默认关闭，或过期天数与约定不一致。')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Output 'PASS: interaction, rich text, local data, weather, and update contracts are present.'
