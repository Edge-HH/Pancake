# Pancake 代码评审报告

> 评审对象：`D:\Files\Codes\Projects\Pancake\Pancake`（WinUI 3 / Windows App SDK 1.8 桌面应用，.NET 8，版本 2.0.6）
> 评审方式：纯静态阅读（未执行 `dotnet build`），覆盖 64 个 `.cs` + 3 个 `.xaml`（共 12,724 行）、6 个测试 `.cs`（约 2,958 行）、10 个 PowerShell 脚本、CI 工作流与项目配置。
> 评审维度：代码结构、命名规范、可读性、潜在缺陷、性能问题、安全性、测试覆盖。

---

## 0. 结论摘要

这是一个**工程质量明显高于同类个人桌面项目平均水准**的代码库：安全边界（路径穿越、符号链接、ZIP 炸弹、体积上限、SHA-256 校验、原子写、失败回滚）设计得相当完整，注释是统一的中文全量注释，逻辑层测试用例密度高且大量使用反例（`Reject`）。

主要短板集中在四处：

1. **崩溃韧性**：全局异常处理器不拦截异常，叠加 13 个 `async void` 事件处理器 → 任一逃逸异常直接杀进程。
2. **CI 空转**：约 670 行的核心逻辑断言（Project/Settings/Update）在 CI 中永不执行，只有启动烟测。
3. **结构性债务**：`MainWindow` 拆成 11 个 partial 仍有约 5,000 行；`BoardSettingsState` 是约 60 属性的"上帝设置对象"；字符串常量代替枚举遍布配置层。
4. **重复实现**：笔迹渲染/擦除有两套，对象映射有四套，其中一套（`AppDataStore.Save/Load`）已经是死代码。

按严重程度统计：**P0 3 项 / P1 10 项 / P2 12 项 / P3 15 项**。

---

## 1. 严重程度定义

| 级别 | 含义 | 处理建议 |
|---|---|---|
| **P0** | 会导致进程崩溃、数据静默丢失，或使质量门禁失效 | 尽快修复，优先于新功能 |
| **P1** | 明确缺陷或显著性能/维护风险，已有触发路径 | 下个迭代内修复 |
| **P2** | 设计债务、健壮性缺口、一致性缺失 | 排期处理，可随相关模块重构一并解决 |
| **P3** | 可读性/整洁度/微优化 | 顺手清理，不必单独排期 |

---

## 2. P0 — 严重

### P0-1　全局异常处理器不拦截异常，进程直接崩溃

**文件**：`src/Pancake/App.xaml.cs:12`

```csharp
UnhandledException += (_, args) => WriteCrashLog(new Exception(args.Message, args.Exception));
```

`args.Handled` 从未被设置为 `true`，因此该处理器只"记录"不"拦截"，异常仍会终止进程。`AppDomain.CurrentDomain.UnhandledException`（第 13 行）同样无法阻止终止。第 12 行还把 `args.Exception` 降级为 `InnerException`，并用 `args.Message` 当外层消息，日志里外层异常会丢失真实堆栈。

**放大效应**：代码库中有 **13 处 `async void`** 事件处理器（见 P0-2 与 P1-6），它们的异常无法被调用方捕获，只能走这条路径。

**建议**：
- 在处理器中按场景决定是否 `args.Handled = true`（至少对已知可恢复异常），或引入 `DispatcherQueue` 级兜底 + 用户可见的错误提示条。
- 直接 `WriteCrashLog(args.Exception)`，不要用 `args.Message` 重新包装。
- 把所有 `async void` 收敛为 `async void` 壳 + `async Task` 实体，壳内 `try/catch`（见 P0-2 的做法参考）。

---

### P0-2　`ClearInk_Click`：`async void` + 绕过重入守卫 + 绕过事务回滚

**文件**：`src/Pancake/MainWindow.Projects.cs:371-391`

```csharp
371: private async void ClearInk_Click(object sender, RoutedEventArgs e)
...
382:     ContentDialogResult result = await dialog.ShowAsync();   // ← 未走 _dialogOpen 守卫
...
387:     if (await ShowConfirmAsync("确认清空笔迹", ...) != ContentDialogResult.Primary) return;
```

三个问题叠加：

1. **绕过重入守卫**。同文件其余 6 个处理器（第 243/258/269/284/298/393 行）都通过 `RunProjectAction(...)` 包裹，而 `RunProjectAction` 内部的对话框走 `ShowConfirmAsync`，后者用 `_dialogOpen`（`MainWindow.xaml.cs:1200-1203`）防止并发。但第 382 行的 `dialog.ShowAsync()` 是裸调用。WinUI 同时只允许一个 `ContentDialog`，双击该按钮会抛 `InvalidOperationException`（"Only a single ContentDialog can be open at any time."），而 `async void` 无捕获 → 直接走 P0-1 的路径 → **崩溃**。
2. **绕过事务回滚**。其余项目操作在失败时会用 `before` 快照回滚（`RunProjectAction` 的 `_library = before` + `ReplaceSubjects` + `BuildTiles`）。`ClearInk_Click` 直接改模型（第 388-390 行），中途抛错会留下半清空状态。
3. **确认对话框出现两次**，交互上也需要用户点两次"确定"，体验异常。

**建议**：改为 `private async void ClearInk_Click(...) => await RunProjectAction(async () => { ... });`，并把第 382 行的对话框换成 `ShowConfirmAsync` / 带守卫的封装。

---

### P0-3　CI 不执行任何核心逻辑测试

**文件**：`.github/workflows/release.yml:72-76`

```yaml
- name: 🚀 验证成品启动
  run: |
    Expand-Archive ... 
    .\tests\verify-release-startup.ps1 -PublishDirectory .\artifacts\package-check
```

发布流水线**只**做"解压后能启动"的烟测。以下三个项目在 CI 中完全不运行：

| 测试项目 | 行数 | 覆盖内容 |
|---|---|---|
| `tests/ProjectLogic` | 166 | 项目存取、`.pch` 包往返、附件收纳、路径穿越、恶意 ZIP、迁移幂等 |
| `tests/SettingsLogic` | 347 | 颜色/RTF 转换、天气解析、噪音门限、媒体库、播放列表、自动填充 130+ 断言 |
| `tests/UpdateLogic` | 160 | 分卷 ZIP 构造与合并、7z 归一化、更新端到端、备份与回滚 |

README 也明确承认"设置、排版、更新回滚和交互契约等更完整的测试仍可在本地运行，**不纳入此工作流**"。这意味着：**更新链路（`ApplyUpdate.ps1` 覆盖文件 + 回滚）这种最危险、最不可回退的逻辑，在发版时零验证**。

**建议**：在 `publish` 之前插入一步

```yaml
- name: 🧪 逻辑测试
  run: |
    dotnet run --project .\tests\ProjectLogic\ProjectLogic.csproj -c Release
    dotnet run --project .\tests\SettingsLogic\SettingsLogic.csproj -c Release
    dotnet run --project .\tests\UpdateLogic\UpdateLogic.csproj -c Release
```

三个项目都是控制台可执行 + 非零退出码语义，接入成本极低。

---

## 3. P1 — 高

### P1-1　`github.Result!` / `gitee.Result` 空引用风险

**文件**：`src/Pancake/MainWindow.Updates.cs:19-20`

```csharp
string message = ReleaseUpdateService.RecommendGitHub(gitee.Result, github.Result)
    ? $"Gitee 尚未同步 GitHub 的 {github.Result!.Tag}，建议将更新源切换到 GitHub。"
```

`Task.WhenAll` 后访问 `.Result` 本身是安全的，但 `github.Result` 的返回类型是可空的（`GetLatestAsync` 在无 Release / 无资产时返回 `null`）。第 19 行 `RecommendGitHub` 已经判定为 `true`，说明 `github.Result` 非空——这个隐式依赖没有编译期保护，第 20 行的 `!` 抑制告警只是掩盖风险。

**建议**：改用显式模式匹配，让编译器参与校验：

```csharp
if (gitee.Result is { } g && github.Result is { } h && ReleaseUpdateService.RecommendGitHub(g, h))
    message = $"Gitee 尚未同步 GitHub 的 {h.Tag}，建议将更新源切换到 GitHub。";
```

### P1-2　`Assembly.GetName().Version!` 空引用风险

**文件**：`src/Pancake/MainWindow.xaml.cs:1005`

```csharp
ReleaseUpdateService.IsNewer(latest.Version, typeof(MainWindow).Assembly.GetName().Version!)
```

`AssemblyName.Version` 是可空的；同文件第 106-108 行读取同一个值时是带 `?? "未知版本"` 兜底的，这里却用了 `!`。两处对同一数据源的处理不一致。

**建议**：提取一个 `private static Version CurrentVersion` 属性，内部做好兜底，两处共用。

### P1-3　空 `catch { }` 静默吞掉颜色解析异常

**文件**：`src/Pancake/MainWindow.Settings.cs:992`

```csharp
try { color.Color = ViewModels.MainViewModel.BrushFromHex(string.IsNullOrEmpty(style.Color) ? "#202024" : style.Color).Color; } catch { }
```

全库唯一的空 `catch`（另一处 `NoiseMonitorService.cs:76` 有注释说明，可接受）。此处用户配置里一个畸形颜色值会导致背景色选择器静默回退，用户看不到任何提示。同一行还把两个语句塞进一行，可读性差。

**建议**：至少记录到 `crash.log` 或设置 `color.Color` 的显式兜底值，并把 `try/catch` 展开为多行。

### P1-4　笔迹擦除是 O(笔画数 × 点数)，且每帧重新分配列表

**文件**：`src/Pancake/Controls/SubjectTileControl.cs:889-900`、`src/Pancake/Controls/ClockInkLayer.cs:155-165`

```csharp
// SubjectTileControl.cs:891
var hit = _renderedStrokes.LastOrDefault(pair => InkGeometry.HitTest(
    pair.Value.Points.Select(p => (p.X, p.Y)).ToList(), point.X, point.Y, ...));
```

每次指针移动（第 836、867 行调用）都要：
- 对**每一条**笔迹执行 `Points.Select(...).ToList()` —— 一次完整列表分配；
- 对**每一条**笔迹做逐点命中测试。

在"整屏笔迹 + 时钟模式"场景（`ClockInkStrokes` 上限 20000 条）下，这是明确的卡顿来源。同一算法在 `ClockInkLayer` 里被复制了一份。

**建议**：
- 用空间索引（网格桶 / 四叉树）按坐标预筛候选笔迹，只对候选做精确命中测试；
- 把 `Points` 的 `(double X, double Y)` 视图缓存为 `List<(double,double)>` 或直接让 `InkGeometry.HitTest` 接受 `IReadOnlyList<Point>`，消除每帧分配；
- 把两处实现合并到一个 `InkStrokeEditor` 辅助类（见 P2-2）。

### P1-5　自动排列对富文本做 O(列数 × 磁贴数) 次 `Measure`

**文件**：`src/Pancake/MainWindow.xaml.cs:447-472`

```csharp
454: for (int columns = maxColumns; columns >= 1 && placements is null; columns--)
455: {
457:     content = tiles.Select(tile => tile.MeasureContentSize(cap)).ToList();
```

`MeasureContentSize`（`SubjectTileControl.cs`）内部会调用富文本编辑器的 `Measure`，而 README 与代码注释都指出"正文格式写入 RichEditBox 很贵"。外层 `maxColumns` 可达磁贴总数，内层每轮重新测量**全部**磁贴 → 最坏情况是 `n²` 次富文本测量。第 466 行的缩放兜底循环（`.9 → .4`，6 轮）还会再乘一次。

**建议**：把 `MeasureContentSize` 结果按 `cap` 做记忆化；或先测量一次得到无约束宽度，再按比例推算不同 `cap` 下的高度，避免每轮全量重测。

### P1-6　`async void` 事件处理器（13 处）

**文件与行号**：

| 文件 | 行号 |
|---|---|
| `MainWindow.Projects.cs` | 243, 258, 269, 284, 298, **371**（见 P0-2）, 393 |
| `MainWindow.xaml.cs` | 292, 413, 488, 933, 965, 994 |

其中 6 处（243/258/269/284/298/393）已被 `RunProjectAction` 的 try/catch 覆盖，属可接受；其余需要确认：
- `MainWindow.xaml.cs:292` `DiscardEditButton_Click` —— 无 try/catch，`ShowConfirmAsync` 之后的 `BuildTiles()` 若抛错则逃逸。
- `MainWindow.xaml.cs:413` `AutoArrangeButton_Click` —— 有 try/catch，但 catch 内部又 `await ShowMessageAsync`，二次异常仍会逃逸。
- `MainWindow.xaml.cs:488` `DeleteSubject` —— 作为回调传给 `SubjectTileControl`（`MainWindow.xaml.cs:344`），无捕获。
- `MainWindow.xaml.cs:965` `ChooseWeatherCityButton_Click` —— `await _weatherCityCatalog.SearchAsync(...)` 在 `TextChanged` 中（第 970 行）被调用，搜索服务抛错即崩溃。

**建议**：统一引入一个 `SafeAsync(Func<Task>)` 包装器，或在 `RunProjectAction` 同级增加通用的 `RunGuardedAsync`，所有 `async void` 处理器只做"解包 + 转交"。

### P1-7　`MainWindow` 拆分后仍有约 5,000 行

**文件与规模**：

| 文件 | 行数 |
|---|---|
| `MainWindow.xaml.cs` | 1214 |
| `MainWindow.Settings.cs` | 1199 |
| `MainWindow.Islands.cs` | 658 |
| `MainWindow.SettingsPreviews.cs` | 591 |
| `MainWindow.Projects.cs` | 404 |
| `MainWindow.xaml` | 400 |
| `MainWindow.Layout.cs` | 388 |
| `MainWindow.Media.cs` | 195 |
| `MainWindow.Presentation.cs` | 130 |
| `MainWindow.ClockInk.cs` | 58 |
| `MainWindow.BoardNavigation.cs` | 47 |

`MainWindow` 承担了：窗口生命周期、时钟、天气、噪音监测、更新检查、项目 CRUD、笔迹、设置页构建、预览渲染、悬浮岛排版、全屏手势、工具栏外观、自动填充宿主。11 个 partial 只是把代码搬了位置，没有降低耦合——所有文件共享同一个 `_settings`、`_library`、`_isEditing` 等约 40 个可变字段。

**建议**（按收益排序）：
1. 先抽 `SettingsPageBuilder`（`MainWindow.Settings.cs` 的 `Toggle`/`Range`/`Choice`/`Note`/`SettingsRow`/`CreateAppearancePreview` 等纯构建函数，约 1,199 行中的大部分可迁出）；
2. 再抽 `NoiseController`（`StartNoiseMonitoring` / `UpdateNoiseDisplay` / `ProcessNoiseAlert` / `PrepareNoiseAlert` / `RefreshMicrophoneDevices`，`MainWindow.xaml.cs:781-912`）；
3. 再抽 `WeatherController`（`MainWindow.xaml.cs:914-986`）；
4. 最后抽 `UpdateController`（`CheckForUpdatesAsync`，`MainWindow.xaml.cs:996-1039`）。

### P1-8　`BoardSettingsState` 是约 60 属性的上帝对象，且用字符串冒充枚举

**文件**：`src/Pancake/Services/ProjectState.cs`

`BoardSettingsState` 把布局、网格、磁贴、工具栏、背景、噪音、天气、自动填充、更新等所有子系统的配置扁平堆在一个类里。更严重的是**所有"枚举"都是裸字符串**：

| 语义 | 字段 | 取值 |
|---|---|---|
| 布局模式 | `LayoutMode` | `"Split"` / `"Board"` / `"Clock"` / `"Free"` |
| 主题 | `Theme` | `"Dark"` / `"Light"` / `"Default"` |
| 色系 | `Palette` | `"Vivid"` / `"Macaron"` |
| 网格样式 | `GridStyle` | `"Grid"` / `"Dots"` / `"None"` |
| 图片模式 | `ImageMode` | `"Zoom"` / `"Stretch"` / `"Fit"` |
| 工具栏位置 | `ToolbarPosition` | `"TopLeft"` … `"Center"` |
| 隐藏动画 | `ToolbarHideAnimation` | `"Fade"` / `"Fly"` |
| 匹配程度 | `MatchLevel` | `"Loose"` / `"Normal"` / `"Strict"` |
| 记录阈值 | `RecordLevel` | `"Loose"` / `"Normal"` / `"Strict"` |
| 记录隔离 | `Isolation` | `"Global"` / `"Subject"` |
| 更新源 | `UpdateSource` | `"Gitee"` / `"GitHub"` |
| 附件类型 | `Kind` | `"图片"` |
| 来源 | `Source` | 内置/手动 |

这些字符串在 `MainWindow.Settings.cs:30-31`、`MainWindow.xaml.cs:102`、`MainWindow.Settings.cs:1073`、`MainWindow.xaml.cs:374` 等处被**裸字面量**反复比较（例如 `_settings.LayoutMode == "Split"` 在 `MainWindow.Settings.cs:73/74/78/79/83/84/85` 出现 7 次），拼错不会报错，重命名无 IDE 支持。

**建议**：
- 把上述字段改为 `enum`，在 JSON 序列化层用 `JsonStringEnumConverter` 保持文件格式兼容；
- 若必须保留字符串（向后兼容成本考虑），至少提取 `public static class LayoutModes { public const string Split = "Split"; ... }` 常量类；
- 把 `BoardSettingsState` 按子系统拆为 `LayoutSettings` / `GridSettings` / `TileSettings` / `ToolbarSettings` / `BackgroundSettings` / `NoiseSettings` / `WeatherSettings` / `AutofillSettings` / `UpdateSettings`，`BoardSettingsState` 只做聚合。

### P1-9　解决方案不包含测试项目

**文件**：`Pancake.slnx`

```xml
<Solution>
  <Project Path="src/Pancake/Pancake.csproj" DefaultStartup="true" />
</Solution>
```

三个测试项目（`tests/ProjectLogic`、`tests/SettingsLogic`、`tests/UpdateLogic`）都不在解决方案里。后果：`dotnet build Pancake.slnx` 不编译测试；IDE 里看不到测试；重构改坏被链接的源文件时没有即时反馈。

**建议**：把三个 `.csproj` 加入 `Pancake.slnx`（这是它们唯一的集成障碍）。

### P1-10　更新脚本依赖系统 PowerShell + `-ExecutionPolicy Bypass`

**文件**：`src/Pancake/Services/PortableUpdateService.cs`（`Launch` 方法）

硬编码 `Environment.SpecialFolder.System + "WindowsPowerShell/v1.0/powershell.exe"`，并传 `-ExecutionPolicy Bypass`。风险：
- Windows PowerShell 5.1 在未来的 Windows 版本中属于"可选功能"，可能不存在；
- 企业策略若通过 GPO 禁用 `ExecutionPolicy` 覆盖，`Bypass` 会被拒绝，更新静默失败；
- `Bypass` 意味着脚本不做签名检查，而 `ApplyUpdate.ps1` 正是会**覆盖程序文件**的脚本。

**建议**：把覆盖逻辑移到 `Pancake.exe` 自身的隐藏启动参数（例如 `--apply-update <config>`），彻底摆脱 PowerShell 依赖；这是可消除的依赖，而不是必须接受的风险。

### P1-11　更新包无代码签名

**文件**：`src/Pancake/Services/ReleaseUpdateService.cs`（`DownloadAsync` 中的 `sha256:` 校验）、`PortableUpdateService.Prepare`

当前信任链是：HTTPS 取 Release 元数据 → 从同一元数据取 SHA-256 → 校验下载包。这只防传输损坏，**不防上游账户被入侵**（攻击者改 Release 和摘要即可）。同时 `PortableUpdateService` 会以 `-ExecutionPolicy Bypass` 执行覆盖。

**建议**：至少对便携版 ZIP 做一次签名（可用 `signtool` + 自签名证书，或发布 `.sha256` + `.sig` 并离线核对指纹）；若短期不可行，请在 README 明确写出"更新包未签名"这一限制。

---

## 4. P2 — 中

### P2-1　`AppDataStore` 中 `Save` / `Load` 已是死代码，且实现与 `AtomicWrite` 不一致

**文件**：`src/Pancake/Services/AppDataStore.cs:15-27`

```csharp
15: public AppState? Load() { ... }           // 无调用点
21: public void Save(AppState state)
22: {
23:     Directory.CreateDirectory(DataDirectory);
24:     string temporaryPath = StatePath + ".tmp";        // 固定文件名，无 GUID
25:     File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));  // 无 Flush(true)
26:     File.Move(temporaryPath, StatePath, true);
27: }
```

全库检索确认：`AppDataStore.Save` 与 `AppDataStore.Load` **没有任何调用点**。旧配置迁移现在由 `ProjectStore.Load()` 自己完成（`ProjectStore.cs:64-79`，含 `.before-projects.bak` 备份），`AppDataStore.StatePath`（第 13 行）与 `ProjectStore.cs:64` 的字面量 `"pancake.json"` 也是重复定义。

更值得注意的是：这个死实现与 `ProjectStore.AtomicWrite` 的**正确做法不一致**（`AtomicWrite` 用随机临时名 + `Flush(true)` 落盘）。留着它等于给未来的调用者埋坑。

**建议**：删除 `AppDataStore.Load/Save/StatePath`，`DataDirectory` 保留（被 `MainWindow.xaml.cs:34`、`ExportImageView.cs:65` 等使用）。

### P2-2　笔迹逻辑在两处重复实现

**文件**：`src/Pancake/Controls/SubjectTileControl.cs:889-931` 与 `src/Pancake/Controls/ClockInkLayer.cs:47-180`

两者都实现了：`_renderedStrokes` 字典、`BeginStroke` / `EndStroke`、`EraseStrokeAt`、`RenderStoredStrokes`、`ScaleOut`（`/ TipScaleX` 反缩放）、`ClearStrokes`。差异仅在容器与坐标系。P1-4 的性能修复因此要做两遍，未来任一处改 bug 都可能漏改另一处。

**建议**：抽 `InkStrokeEditor`（持有 `Canvas` + `IList<InkStrokeData>` + `Action onChanged`），两个控件改为组合使用。

### P2-3　对象映射有四套并行实现

| 实现 | 位置 |
|---|---|
| 手写 `Clone()` | `src/Pancake/Models/BoardModels.cs`（`HomeworkEntry.Clone`、`AttachmentItem` 逐属性复制、`SubjectBoard.Clone`） |
| JSON 往返 `Clone` | `src/Pancake/Services/ProjectStore.cs:32`（`JsonOptions` + `JsonSerializer`） |
| `CaptureSubjects` / `RestoreSubjects` | `src/Pancake/Services/AppDataStore.cs:29-64`、`87-107` |
| `CaptureInk` / `RestoreInk` | `src/Pancake/Services/AppDataStore.cs:67-85` |

`AttachmentItem` 有 12 个属性，在 `AppDataStore.cs:49-56` 与 `AppDataStore.cs:94-104` 被逐一手写两遍。新增一个属性需要改 4 处，漏改会导致静默丢字段——这正是"复制粘贴映射"最典型的失败模式。

**建议**：让 `AttachmentState` / `InkStrokeState` 与领域模型之间只保留**一套**映射（推荐显式手写，保留在 `AppDataStore` 里），其余用泛型 `Clone<T>(T)`（JSON 往返）统一替代；`BoardModels.Clone()` 系列可删除。

### P2-4　4 处 `TaskCompletionSource` 未指定 `RunContinuationsAsynchronously`

**文件与行号**：

- `src/Pancake/Controls/AttachmentImageControl.cs:49` — `private readonly TaskCompletionSource<bool> _imageReady = new();`
- `src/Pancake/Controls/ExportTileVisual.cs:25` — `private readonly TaskCompletionSource _loaded = new();`
- `src/Pancake/Controls/ExportImageView.cs:58` — `private readonly TaskCompletionSource _loaded = new();`
- `src/Pancake/Controls/ExportImageView.cs:414` — `TaskCompletionSource laidOut = new();`

未指定 `TaskCreationOptions.RunContinuationsAsynchronously` 时，`SetResult` 会在**调用线程上同步执行**所有续接。这三处都在 UI 线程 `SetResult`，续接若做布局或 `UpdateLayout`（`ExportImageView` 正是这么用的），会在非预期时机重入 UI 树。

**建议**：统一加 `TaskCreationOptions.RunContinuationsAsynchronously`。

### P2-5　窗口尺寸变化路径做了大量重复工作

**文件**：`src/Pancake/MainWindow.xaml.cs:1184-1190`

```csharp
private void RootShell_SizeChanged(object sender, SizeChangedEventArgs e)
{
    ApplyDisplayLayout();      // 含 _layingOut 守卫 + ApplyFreeWidgets 搬运 UIElement
    ApplyToolbarSettings();    // 末尾调用 RefreshIslands()
    RecordToolbarActivity(false);
    RefreshAppearancePreviews();  // 重建预览场景
}
```

而 `ApplyToolbarSettings`（`MainWindow.Settings.cs:1114-1198`）会遍历全部工具栏按钮重算尺寸/圆角/内容树（第 1129-1163 行），再在末尾调 `RefreshIslands()`（第 1197 行）做完整的悬浮岛测量与摆放。拖拽改变窗口大小时，这条链每帧执行一次，包含多次 `Measure`、`Children.Clear()` + `Add` 和字符串拼接（`RefreshAppearancePreviews` 的 key 含 `RootShell.ActualWidth/ActualHeight`，见 `MainWindow.SettingsPreviews.cs`）。

**建议**：对 `ApplyToolbarSettings` + `RefreshAppearancePreviews` 做 `DispatcherQueue.TryEnqueue(Low, ...)` 合并（同一帧只跑一次），并缓存不随尺寸变化的部分。

### P2-6　命令行解析用子串匹配，且两处风格不一致

**文件**：`src/Pancake/App.xaml.cs:32-53`

```csharp
32: string commandLine = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
34: if (Environment.GetCommandLineArgs().Contains("--smoke-test"))            // 数组精确匹配
46: bool startFullScreen = !commandLine.Contains("--windowed", ...);          // 子串匹配
47:     ... commandLine.Contains("--view=editor", ...)
```

同一个方法里两种语义：第 34 行是数组 `Contains`（精确元素匹配），第 46 行起是字符串子串匹配。后者意味着 `--windowedX`、`--view=editorial` 都会命中。此外把参数 `Join(' ')` 再 `Contains` 会让带引号的路径参数产生歧义。

**建议**：用一个简单的 `ParseArgs(string[])` 返回 `Dictionary<string,string?>`，全部改为精确匹配（前缀 `--view=` 做前缀匹配）。

### P2-7　`SubjectTileControl` 中的死代码

**文件**：`src/Pancake/Controls/SubjectTileControl.cs:769`、`:918`

```csharp
769: private static ToggleButton CreateIconToggle(string glyph, string tooltip)   // 无调用点
918: private void UndoLastStroke()                                                // 无调用点
```

全库检索确认两处均只有定义、没有引用（`UndoLastStroke` 是"撤销上一笔"，但 UI 上没有任何入口，`ClearStrokes` 才是被使用的）。

**建议**：删除；若 `UndoLastStroke` 是计划中的功能，改为 TODO 注释 + issue，不要留孤立实现。

### P2-8　缺少 `.editorconfig` / `Directory.Build.props` / 分析器 / `TreatWarningsAsErrors`

仓库根目录没有 `.editorconfig`，没有 `Directory.Build.props`，`Pancake.csproj` 也未启用 `EnableNETAnalyzers` 之外的任何强制项，`TreatWarningsAsErrors` 未设置。这就是为什么 `Nullable=enable` 已经打开，却仍然能在 `MainWindow.Updates.cs:20`、`MainWindow.xaml.cs:1005` 看到 `!` 抑制——告警只提示不阻断。

**建议**：加 `.editorconfig`（至少统一缩进/换行/`var` 偏好）+ `Directory.Build.props` 打开 `TreatWarningsAsErrors`（可先对 `CS86xx` 可空性告警单独开），并在 CI 加 `dotnet format --verify-no-changes`。

### P2-9　UI 文案全中文硬编码，本地化不可行

**文件**：`src/Pancake/MainWindow.xaml`（全文件）、`MainWindow.Settings.cs`（全部 `Toggle("…")` / `Note("…")` / `Heading("…")` 字面量）

XAML 中没有任何 `x:Uid`，代码后置里的设置项文案也全是中文字面量。但 `Pancake.csproj` 却声明了 `SatelliteResourceLanguages = zh-Hans;zh-Hant;en`，暗示支持三种语言。当前状态下切换语言不会有任何效果。

**建议**：二选一——要么移除 `SatelliteResourceLanguages` 里的 `en` 并明确"仅中文"，要么把文案迁到 `.resw` 资源文件并加 `x:Uid`（工作量大，建议先明确决策再动手）。

### P2-10　自动填充容量上限只在 UI 层校验

**文件**：`src/Pancake/MainWindow.Settings.cs:217`、`:449`

```csharp
217: if (_settings.Autofill.Subject.Subjects.Count >= AutofillService.MaxSubjectEntries) { ...提示... }
449: if (_settings.Autofill.Homework.Items.Count(item => item.Source == AutofillService.ManualSource) >= AutofillService.MaxManualItems) { ...提示... }
```

`AutofillService` 自身（`src/Pancake/Services/AutofillService.cs`）在 `RecordHomework` / `EnsureBuiltIns` 路径上**不检查**这两个上限，只有 `TrimAutoItems` 处理 `MaxAutoItems`。因此：手改 `projects.json`、或经 `.pch` 导入含大量手动条目的项目后，服务层会接受超限数据，UI 才在下一次点击"添加"时提示。

**建议**：把上限校验下沉到 `AutofillService`（`Prune` 或写入前统一裁剪），UI 只负责提示。

### P2-11　`design-qa.md` 的视觉验收结论是 `blocked`

**文件**：`design-qa.md`

```
final result: blocked
implementation screenshot: unavailable
Browser-rendered evidence: not applicable; this is a native WinUI application
[P1] Visual and interaction comparison is blocked.
```

引用的是一个临时剪贴板路径 `C:/Users/YANGTI~1/AppData/Local/Temp/codex-clipboard-*.png`（早已失效），目标视口 1440×900。也就是说**没有任何可复现的视觉回归证据**。

**建议**：用 `tests/verify-ui.ps1` 的真实窗口路径补一张固定尺寸（1440×900）的截图并提交到仓库，作为后续视觉比对的基线；把 `design-qa.md` 里的临时路径替换为仓库内相对路径。

### P2-12　发布版本号有两个来源

**文件**：`src/Pancake/Pancake.csproj`（`<Version>2.0.6</Version>`）与 `.github/workflows/release.yml:56`（`-p:Version=${{ steps.version.outputs.version }}`）

CI 用 tag 推导版本覆盖 csproj 的值，本地构建则用 csproj 的硬编码值。两者容易漂移，且 `MainWindow.xaml.cs:106-108` 显示的"关于"版本会随之不同。

**建议**：csproj 里改用 `<Version Condition="'$(Version)' == ''">0.0.0-local</Version>` 之类的显式本地默认值，或引入 `Directory.Build.props` 单一来源。

---

## 5. P3 — 低 / 整洁度

| # | 问题 | 位置 |
|---|---|---|
| P3-1 | 三处独立 `HttpClient`（无 `IHttpClientFactory`，无共享 `SocketsHttpHandler`） | `Services/XiaomiWeatherService.cs:14`、`Services/ReleaseUpdateService.cs:13`、`Services/GitHubUpdateService.cs:17` |
| P3-2 | 每帧用 `Math.Pow` + `Math.Sqrt` 算距离（`Math.Pow` 比乘法慢一个量级） | `MainWindow.xaml.cs:1136` |
| P3-3 | `GridSize` 是"属性 + `Math.Round` + `Math.Clamp`"，却在 `DrawGrid` 的双层循环条件里反复求值 | `MainWindow.xaml.cs:24`、`644-664` |
| P3-4 | `UpdateBoardBounds` 每次都拼一个 6 字段插值字符串做缓存键（`UpdateBoardBounds` 在尺寸/布局变化时高频调用） | `MainWindow.xaml.cs:587` |
| P3-5 | `IsAsciiLetters`（复数）与 `IsAsciiLetter`（单数）语义不同、名字几乎一样 | `Services/AutofillService.cs:452`、`:461` |
| P3-6 | 错误消息带尾随空格 | `Services/GitHubUpdateService.cs:24`（`"更新仓库格式应为 owner/repository。 "`） |
| P3-7 | 孤儿构建产物目录（只剩 `bin`/`obj`，无源文件、无 csproj） | `tests/RichTextLogic/`、`tests/UiHeadless/` |
| P3-8 | `legacy/Pancake.WinUI/obj/` 下 5 个构建产物被 Git 跟踪 | `legacy/Pancake.WinUI/obj/...`（`*.json`/`*.props`/`*.targets`/`*.cache`） |
| P3-9 | `.gitignore` 只有 4 行，未忽略 `legacy/**/obj` | `.gitignore` |
| P3-10 | 魔法数：`94`（dB 参考值）、`0.0000001`（下限） | `Services/NoiseMonitorService.cs`（dB 计算与窗口判断） |
| P3-11 | 魔法数：亮度阈值 `145`；`_appearanceTask` fire-and-forget 未 await | `Controls/ExportImageView.cs`（`light` 判定、`_appearanceTask = RefreshBackdropAsync(...)`） |
| P3-12 | 排版魔法数密集：`7 * scale`、`gap = Round(8 * scale)`、`DockedComponentsAspectRatio = 6.5`、`DockedClockFallbackAspectRatio = 1.8`、`Math.Max(720/760/520/560)` | `MainWindow.Islands.cs`、`MainWindow.SettingsPreviews.cs`、`MainWindow.Layout.cs` |
| P3-13 | `ParseColor` 对畸形输入无防御（`Convert.ToByte` / `Substring` 会抛）。当前被 `ProjectValidation.Color`（`ProjectStore.cs:227-231`）挡住，属纵深防御缺失 | `Services/AppDataStore.cs:109-115` |
| P3-14 | `FloatingToolbar.MaxWidth/MaxHeight` 每次都在 `ApplyToolbarSettings` 里从 `RootShell.ActualWidth/ActualHeight` 重算 | `MainWindow.Settings.cs:1194-1195` |
| P3-15 | 单文件 2101 行的 UI 验证脚本；无覆盖率统计；无 `CONTRIBUTING.md` | `tests/UiVerification.cs` |

补充：全库 **`TODO` / `FIXME` / `HACK` / `XXX` 均为 0 处**——这本身是好事，但也意味着 P2-7 那类死代码没有被任何标记"认领"。

---

## 6. 安全性专项（整体评价：扎实）

这一维度值得单独说明，因为它是本项目最突出的优点。已核对且**设计正确**的点：

| 防线 | 位置 | 说明 |
|---|---|---|
| ZIP 条目数上限 | `PortableUpdateService.Prepare` | `Entries.Count > 10000` 拒绝 |
| 解压总体积上限 | 同上 | 累计 `entry.Length > 2 GiB` 拒绝 |
| 重复路径检测 | 同上 | `paths` 集合去重 |
| 符号链接/硬链接拒绝 | `PortableUpdateService`、`SplitUpdatePackage` | `((attrs >> 16) & 0xF000) == 0xA000`、`FileAttributes.ReparsePoint` |
| 路径穿越拒绝 | `ValidateRelativePath` | 拒绝根路径、`.`/`..`、结尾点/空格、`<>:"\|?*`、控制字符、Windows 保留名（`CON`/`PRN`/`AUX`/`NUL`/`COM1-9`/`LPT1-9`） |
| 用户数据保护 | `ValidateRelativePath` | 显式拒绝 `data` 段（`segments[0].Equals("data", OrdinalIgnoreCase)`） |
| 目标越界检查 | `ValidateTarget` | `StartsWith(root + DirectorySeparatorChar, OrdinalIgnoreCase)` + 沿祖先链查 `ReparsePoint` |
| 写权限预探针 | `PortableUpdateService.Prepare` | 关闭应用前用 `FileOptions.DeleteOnClose` 试写，提前发现不可写 |
| 摘要校验 | `ReleaseUpdateService.DownloadAsync` | `sha256:` 前缀强制校验 |
| 分卷连续性校验 | `ReleaseUpdateService.ParseRelease` | `.001` + `^\d{3,}$`、`z01` + `^z\d{2,}$`，缺卷拒绝 |
| 资产名过滤 | 同上 | 排除 `arm64`、`/archive/`、含路径分隔符、含非法文件名字符 |
| HTTPS 强制 | 同上 | 要求 `uri.Scheme == "https"` |
| 原子写 | `ProjectStore.AtomicWrite` | 随机临时名 + `Flush(true)` + `File.Move(..., true)` |
| 包内附件路径白名单 | `PchPackageService.IsAssetPath` | `\Aassets/[0-9a-fA-F]{32}(?:\.[a-zA-Z0-9]{1,10})?\z` |
| 包体积上限 | `PchPackageService` | 512 MB / manifest 16 MB / 10000 文件 |
| 覆盖前拒绝覆盖导出图 | `PchPackageService.Save` | `files.ContainsKey(Path.GetFullPath(destination))` |
| 失败回滚 | `ApplyUpdate.ps1` | 逆序恢复备份，新文件 `Remove-Item`；失败写 `UPDATE_FAILED` |
| 发布目录保护 | `installer/Optimize-Publish.ps1` | 拒绝等于仓库根/盘根、拒绝含 `data`、拒绝 `ReparsePoint` |
| 项目校验 | `ProjectStore.ProjectValidation.Validate` | 版本、GUID、名称长度、主题/色系枚举、数量上限、`double.IsFinite`、颜色格式（`ProjectStore.cs:227-231`） |

**剩余可改进项**（已在上文展开）：P1-10（`ExecutionPolicy Bypass`）、P1-11（无签名）、P2-1（`AppDataStore.Save` 的非原子写残留）、P3-13（`ParseColor` 纵深防御）。另需注意 Gitee API 无鉴权调用（`ReleaseUpdateService.GetLatestAsync`），存在被限流的可能。

---

## 7. 测试覆盖专项

**做得好的部分**：
- 逻辑层用例密度高、反例充分。`tests/ProjectLogic/Program.cs` 覆盖了 7 个危险资源路径、恶意 ZIP（`CreateEntry("../escape")`）、损坏包、未知版本、`MigrateInk` 幂等性；`tests/UpdateLogic/Program.cs` 甚至**手工构造 PKZIP 多磁盘结构**来测分卷合并，并用 `Path.Combine(root, "程序 ' & 空格")` 验证特殊字符/引号/空格路径；`tests/SettingsLogic/Program.cs` 有 130+ 条自动填充断言。
- 通过 `<Compile Include="../../src/Pancake/...">` 链接源文件复用被测代码，避免逻辑重复实现。
- 断言采用 `Check` / `Reject` 双语义（`Reject` 捕获 `InvalidDataException or IOException or JsonException or UnauthorizedAccessException or InvalidOperationException`），能表达"必须被拒绝"的期望。

**主要缺口**（按影响排序）：

1. **CI 不跑**（见 P0-3）——最严重，等于这些测试在生产流程中不存在。
2. **不在解决方案里**（见 P1-9）。
3. **UI 验证依赖条件编译 + 真实窗口**：`EnableUiVerification=true` 定义 `PANCAKE_UI_TESTS`（`Pancake.csproj`），再经 `App.xaml.cs:54-104` 的 8 个 `--verify-*` 分支 + PowerShell 脚本驱动。这需要交互式桌面会话，无法在无头 CI 运行，且失败信息只有日志文本。
4. **无覆盖率统计**，无法判断哪些分支未被触达。
5. **视觉验收 `blocked`**（见 P2-11）。
6. **`tests/UiVerification.cs` 2101 行单文件**，新增契约的成本随文件增长而上升。

**建议**：
- 先把 1、2 解决（成本极低，收益最大）；
- 再考虑把 UI 验证里"纯数据契约"的部分（`verify-color-formatting-contract.ps1`、`verify-icon-font-contract.ps1`、`verify-rich-text-roundtrip.ps1` 中不依赖真实窗口的断言）迁入 `SettingsLogic`，让它们进 CI；
- 引入 `coverlet` 生成覆盖率并在 PR 上展示趋势（不设硬门禁，先看趋势）。

---

## 8. 值得肯定的设计

写在前面：这些不是客套，而是确实做得比多数同类项目好的地方。

1. **安全边界完整**：见第 6 节。`ValidateRelativePath` 连 Windows 保留设备名（`CON`/`NUL`/`COM1`）都考虑了，这在个人项目里非常罕见。
2. **原子写 + 事务快照 + 失败回滚**：`ProjectStore.AtomicWrite` 用随机临时名 + `Flush(true)`；`MainWindow.Projects.cs` 的 `RunProjectAction` 在失败时恢复 `before` 快照并重建 UI；`ApplyUpdate.ps1` 逆序回滚。三层回滚设计一致。
3. **资源生命周期管理**：`BackgroundVisual` 用 `EffectiveViewportChanged` 在视口不可见时暂停播放器/停止计时器；`MediaEnded`/`MediaFailed` 回调里用 `ReferenceEquals(_player, player)` 核对身份，正确处理了跨线程与晚到回调。`ReleaseMedia` 的释放顺序（`SetMediaPlayer(null)` → `Dispose` → 清子元素）也是正确的。
4. **性能意识**：无限画板只绘制视口附近网格（`MainWindow.xaml.cs:626-631`）；`_boardLayoutPending` 延迟应用避免滑块拖动卡顿；`RefreshBackdropAsync` 用 `revision` + `Task.Delay(100)` 做防抖；`AutofillPopup.SameEntries` 用 `ReferenceEquals(Payload)` 避免闪烁；`_refreshSettingAvailability` 用 `signature` 字符串比较避免重建列表。
5. **注释质量**：全中文、解释"为什么"而非"是什么"，例如 `MainWindow.xaml.cs:85-86` 解释"只挂 PreviewKeyDown 是因为同时挂 KeyDown 会让一次按键被处理两遍"，`MainWindow.Settings.cs:900-902` 解释拖动网格为何不重绘看板。这类注释对后续维护价值很高。
6. **向后兼容处理细致**：`DockedWidgetLayoutVersion < 2` 的迁移（`MainWindow.xaml.cs:1049-1055`）、`Autofill` 字段的逐级 `??=` 补默认值（`1058-1063`）、`ClearLegacyPlaceholder`（`AppDataStore.cs:43`）、`MigrateInk` 的非等比 Viewbox 映射、`.before-projects.bak` 备份——旧数据路径考虑得比新数据路径还周全。
7. **`ProjectValidation.Validate`** 对 `double.IsFinite` 的全面检查（`ProjectStore.cs:219`），能挡住 `NaN`/`Infinity` 导致的布局崩溃——这是很多人会漏的。

---

## 9. 优先级路线图

### 第一批（建议立刻处理，1 个工作日内）
1. **P0-1** 全局异常处理器：`args.Handled` + 正确记录 `args.Exception`。
2. **P0-2** `ClearInk_Click` 改走 `RunProjectAction` + 统一对话框守卫。
3. **P0-3** CI 增加三个逻辑测试项目的执行步骤。
4. **P1-9** 把三个测试项目加入 `Pancake.slnx`。

> 这一批全部是低风险、低工作量的改动，但把"崩溃"和"零回归验证"两个最大风险同时消掉。

### 第二批（下个迭代）
5. **P1-1 / P1-2 / P1-3**：三处空引用与静默吞异常。
6. **P1-4**：笔迹擦除改空间索引（先做 `SubjectTileControl`，再同步 `ClockInkLayer`）。
7. **P1-6**：统一 `async void` 包装器。
8. **P2-1**：删除 `AppDataStore.Load/Save/StatePath`。
9. **P2-4**：`TaskCompletionSource` 补 `RunContinuationsAsynchronously`。
10. **P2-8**：`.editorconfig` + `Directory.Build.props` + `dotnet format` 进 CI。

### 第三批（结构性重构，需要专门排期）
11. **P1-8**：字符串枚举 → `enum`（先做 `LayoutMode`/`Theme`/`Palette` 三个影响面最大的），`BoardSettingsState` 按子系统拆分。
12. **P2-2 / P2-3**：合并两套笔迹实现、四套映射实现。
13. **P1-7**：从 `MainWindow` 抽 `SettingsPageBuilder` → `NoiseController` → `WeatherController` → `UpdateController`。
14. **P1-5**：自动排列的富文本测量记忆化。
15. **P1-10 / P1-11**：更新流程去 PowerShell 化 + 包签名。

### 第四批（随手清理）
16. **P2-5 / P2-6 / P2-7 / P2-9 / P2-10 / P2-11 / P2-12** 与全部 P3 项。

---

## 10. 附录：量化事实

| 指标 | 数值 |
|---|---|
| 源文件 | 64 个 `.cs` + 3 个 `.xaml`，共 12,724 行 |
| 最大源文件 | `MainWindow.xaml.cs`（1214 行） |
| 测试文件 | 6 个 `.cs`（约 2,958 行）+ 10 个 `.ps1` |
| 最大测试文件 | `tests/UiVerification.cs`（2101 行） |
| `async void` | 13 处（`MainWindow.Projects.cs` 7 处、`MainWindow.xaml.cs` 6 处） |
| 空 `catch` | 1 处（`MainWindow.Settings.cs:992`） |
| `TODO`/`FIXME`/`HACK` | 0 处 |
| `.Result` / `.Wait()` | 2 处（`MainWindow.Updates.cs:19-20`） |
| 独立 `HttpClient` | 3 处 |
| 未指定 `RunContinuationsAsynchronously` 的 `TaskCompletionSource` | 4 处 |
| 死代码 | `AppDataStore.Load/Save`、`SubjectTileControl.CreateIconToggle/UndoLastStroke` |
| Git 跟踪文件 | 114 个 |
| `.gitignore` 行数 | 4 |
| 解决方案包含的项目 | 1（不含任何测试项目） |
| CI 执行的测试项目 | 0（仅启动烟测） |

---

*报告结束。本报告为只读分析，未修改任何源代码。*
