# Design QA

- Source visual truth: `C:/Users/YANGTI~1/AppData/Local/Temp/codex-clipboard-45da20a7-5558-4965-b169-5997fa3ed0a9.png`
- Source pixels: 1854 x 1138
- Intended implementation viewport: 1440 x 900 CSS-equivalent window pixels at 1x density
- State: display mode, grid-aligned tile editing, border resizing, tile-ink toolbar, bottom floating toolbar, weather settings, and microphone settings
- Implementation screenshot: unavailable
- Browser-rendered evidence: not applicable; this is a native WinUI application
- Native UI interaction evidence: reserved for the user's own touchscreen-device acceptance run

**Findings**

- [P1] Visual and interaction comparison is blocked.
  - Location: main board, subject tiles, eight border/corner resize zones, in-tile ink toolbar, and bottom floating toolbar.
  - Evidence: the reference image was inspected, but no current native-window screenshot or pointer-interaction run is available.
  - Impact: build success confirms XAML and C# validity, but cannot prove final proportions, hit targets, drag feel, or pen/touch behavior.
  - Fix: on the target touchscreen, test edit, move, every border/corner resize direction, grid snapping, pen colors/thickness/eraser, and the non-edit scrolling full-screen hint.

**Required fidelity surfaces**

- Fonts and typography: statically aligned to the reference's light clock and large tile headings; rendered comparison blocked.
- Spacing and layout rhythm: implemented as a 2:3 clock/board split with 16 px grid alignment and a centered bottom floating toolbar; rendered comparison blocked.
- Colors and visual tokens: dark board, black clock field, and per-subject accent borders implemented; rendered sampling blocked.
- Image quality and asset fidelity: no raster assets are required by the selected UI; Fluent system icons are used for controls.
- Copy and content: subject names, homework text, attachment counts, and ink are presented directly on each tile.

**Comparison history**

- No visual iteration was possible because an implementation screenshot could not be captured under the current tool authorization.

**Implementation checklist**

- Exercise the direct-edit, border-resize, grid-snap, ink, and full-screen expansion states on the target touchscreen.
- Compare the resulting 1440 x 900 screen with the supplied reference.
- Report any P0/P1/P2 mismatch with a screenshot and exact gesture.

**Follow-up polish**

- Tune tile defaults after observing real classroom display density and touch target comfort.

final result: blocked

## 三端迁移（avalonia 分支）验收记录

本记录只覆盖 2026-09 开始的 Avalonia 三端迁移，与上面的 WinUI 交互验收互不影响。

**已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | `dotnet build Pancake.slnx -c Release` 通过，0 警告 0 错误 |
| 核心逻辑回归 | ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项，均通过 |
| Windows 真实启动 | `Pancake.exe --windowed` 进程存活，窗口标题为“Pancake 班级作业看板” |
| Linux 交叉发布 | `-r linux-x64 -p:PublishPlatform=linux` 生成自包含产物 |
| Linux 真实启动 | 在 WSL Ubuntu 26.04 中启动成功（WSLg），无错误输出，数据写入 `~/.local/share/pancake/projects.json` |
| macOS 交叉发布 | `-r osx-arm64 -p:PublishPlatform=macos` 生成自包含产物 |
| 数据目录策略 | Windows 下首次启动写入 `%LOCALAPPDATA%\Pancake\projects.json`，便携模式优先规则已实现 |

**尚未验证（不得当作已通过）**

- Linux 上的真实 GNOME/KDE/Wayland 窗口行为、缩放比例、触控与音频设备；WSLg 启动不等于桌面环境验收。
- macOS 上的窗口行为、麦克风授权弹窗、Gatekeeper 拦截与 `Pancake.app` 组装结果，需要在真实 Mac 或 macOS 运行器上验证。
- 富文本、图片裁切、手写、项目与导入导出、噪音检测与壁纸在三个平台的交互与视觉一致性（当时尚未迁入，后续轮次已实现并在各自记录中验收）。

## 看板显示与磁贴编辑验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | `dotnet build Pancake.slnx -c Release` 通过，0 警告 0 错误 |
| 核心逻辑回归 | ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Windows 空项目启动 | 进程存活、无异常输出、正常关闭 |
| Windows 双磁贴项目启动 | 载入带两个科目的项目后正常渲染并保存往返，无异常 |
| 项目数据损坏 | 无效 `projects.json` 会备份为 `projects.json.corrupt-*`，程序继续以空项目启动并提示，不再崩溃 |
| Linux 交叉发布与启动 | `linux-x64` 自包含产物在 WSL Ubuntu 26.04 启动成功，无错误输出 |
| macOS 交叉发布 | `osx-arm64` 自包含产物生成成功 |

**本轮发现的真实缺陷（已修复）**

- 手写 `InitializeComponent` 会让 Avalonia 名称生成器跳过字段赋值，导致主窗口控件字段为空并在启动时崩溃。
- 无效项目数据会在启动阶段抛出异常并终止进程，现已改为备份并降级启动。
- 图标控件收到字形码位而不是语义名会抛错，现已同时兼容两种写法。

**尚未验证**

- 磁贴拖动、八分量缩放在真实鼠标／触摸／触控笔下的手感与命中区域，需要窗口自动化或人工验收。
- 自由布局下组件移动、分隔条拖拽、控制窗八种停靠位置的实际视觉效果。

## 富文本子系统验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 富文本回归测试 | `tests/RichTextLogic` 25 项通过：模型、RTF 往返、旧存档迁移、末尾段落修复、编辑差异与混合选区 |
| 既有回归测试 | ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Windows 真实启动（含 RTF 内容） | 载入带字体表、颜色表、加粗、彩色文字与高光的旧格式 RTF，正常渲染并保存往返，无异常输出 |

**本轮修复的真实缺陷**

- RTF 颜色表首项应为 auto 占位，初版多算了一项，导致所有 `\cf`／`\highlight` 颜色索引错位。
- 尾部多余段落的裁剪条件写反，旧版累积的空段落会被保留下来。
- 截断的 RTF 会解析成空内容，现已回退到纯文本，避免把用户文字显示成空白。

**尚未验证**

- 中文输入法在富文本编辑器中的组合与上屏行为（编辑器复用系统 TextBox 的输入法实现，仍需真机确认）。
- 由本版本生成的 RTF 在旧版 WinUI 客户端中的显示效果（需要旧版客户端实物验证）。
- 富文本悬浮岛（旧版浮动工具栏）尚未迁移，当前使用行内格式工具条。

## 图片附件与手写验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Windows 真实启动（带图片附件） | 载入旋转 90°、缩放 1.2 的图片附件，正常渲染并保存往返；相对路径与变换字段保持不变 |
| Windows 真实启动（带笔迹） | 载入磁贴笔迹与时钟整屏笔迹，在仅时钟布局下正常渲染并保存往返 |
| Linux 交叉发布 + WSL 真实启动 | 无错误输出 |

**尚未验证**

- 图片八点缩放、裁切拖动与旋转按钮在真实鼠标／触摸／触控笔下的手感与命中区域。
- 手写笔迹在真实触控笔下的压感与线条平滑度（当前按固定线宽绘制，与旧版一致，不读取压感）。
- 橡皮擦采用按命中半径删除整条笔迹的策略，比旧版的直接命中更宽容，需真机确认手感。

## 项目与文件子系统验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项（含 `.pch` 打包／导入／迁移）、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Windows 无项目启动 | 启动、显示空状态引导面板并正常关闭，且**不会**自动创建项目（与旧版一致） |
| Windows 有项目启动 | 恢复上次使用的项目并正常关闭，用户数据未被改写 |
| Linux 交叉发布 + WSL 真实启动 | 无错误输出 |

**本轮修复的真实缺陷**

- 顶栏项目计数与作业区标题计数使用了相同的 `x:Name`，Avalonia 在注册控件名时直接抛异常导致启动崩溃，现已改名区分。
- 旧实现对全新安装会自动创建一个空项目，与旧版“显示引导面板”的行为不一致，现已改为不自动新建。
- 项目库加载后未执行旧配置迁移（停靠布局版本、网格取值钳制、自动填充集合补齐），现已补上。

**尚未验证**

- 文件选择器（导入 `.pch`／保存 `.pch`）在 Windows、Linux、macOS 三个桌面环境下的原生对话框行为，需要真机交互验收；`.pch` 的打包与导入逻辑本身已被 ProjectLogic 覆盖。
- 切换项目时的“应用项目外观”对话框需要实际点击确认。

## 设置页与关于页面验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 设置页运行时自检 | 以 `-p:EnableUiVerification=true` 发布后运行 `--verify-settings-pages`，依次构建布局、外观·主题、外观·网格、外观·控制窗、关于五个分类，写出 `ALL_SECTIONS_OK` |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Windows 真实启动 | 五个设置分类在真实窗口中构建成功，进程正常退出（退出码 0） |
| Linux 交叉发布 + WSL 真实启动 | 以 `--view=settings` 启动进入设置页，无错误输出 |

**本轮修复的真实缺陷**

- 控制窗停靠位置的取值写成了 `LeftCenter`/`RightCenter`，与设置里实际保存的 `CenterLeft`/`CenterRight` 不一致，导致左右居中停靠失效，现已统一。

**尚未验证**

- 设置项拖动滑块、点击色块后的即时视觉反馈需要人工验收（自检只覆盖控件能否真实构建，不覆盖观感）。
- 关于页的“检查更新”会真实访问网络；当前版本找到新版本后只打开发布页，下载与安装流程在更新里程碑接入。

## 天气与噪音检测验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 设置页运行时自检 | 七个分类（布局、外观·主题、外观·网格、外观·控制窗、组件·天气、组件·噪音检测、关于）全部构建成功，写出 `ALL_SECTIONS_OK`，进程退出码 0 |
| 音频设备生命周期 | 自检进程启动时真实初始化了 MiniAudio 采集设备，退出时干净停止并释放，未出现挂起或异常 |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Linux 交叉发布 + WSL 真实启动 | 进入设置页运行 15 秒无错误输出，SoundFlow 的 Linux 原生库正常加载 |

**尚未验证**

- 真实教室环境下的音量读数准确性、提示音时延与回授抑制效果，需要带麦克风与扬声器的设备验收；`NoiseLevelMeter` 的换算公式与门限逻辑已被 SettingsLogic 覆盖，但没有真实声压计对照。
- macOS 的麦克风权限弹窗与 CoreAudio 设备枚举，需要在真实 Mac 上验证（`Info.plist` 已声明 `NSMicrophoneUsageDescription`）。
- 天气接口是第三方整理的非正式接口，实际刷新结果需要联网环境验收。

## 自动填充验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 设置页运行时自检 | 十个分类（布局、外观×3、组件×2、自动填充×2、关于）全部构建成功，写出 `ALL_SECTIONS_OK`，退出码 0 |
| 自动填充逻辑回归 | SettingsLogic 覆盖学科库登记、三档拼音匹配、随机配色取色、作业切词与阈值、分学科隔离、过期清理与屏蔽词 |
| Linux 交叉发布 + WSL 真实启动 | 设置页运行 15 秒无错误输出 |

**本轮修复的真实缺陷**

- 设置导航里此前遗漏了“组件 · 噪音检测”分类（该分类的页面代码已存在但从未挂到导航上），本轮补齐并新增自动填充两页。

**尚未验证**

- 候选浮层在真实输入下的表现：输入法组合期间按拼音给候选、采纳后不被上屏覆盖、浮层随输入位置跟随、滚轮与触屏滚动——这些需要人工在中文输入法下逐项验收。
- 浮层当前锚定在输入框下方（旧版跟随光标位置），长文本换行时的贴合度可能略有差异。

## 背景与磁贴外观验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 设置页运行时自检 | 十二个分类（布局、外观×5、组件×2、自动填充×2、关于）全部构建成功，写出 `ALL_SECTIONS_OK`，退出码 0 |
| Windows 真实启动（带背景设置） | 载入「跨区底图 + 模糊 18、磁贴底图拉伸、标题字号 36」的项目，正常渲染并保存往返，设置项全部保留 |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Linux 交叉发布 + WSL 真实启动 | 设置页运行 15 秒无错误输出 |

**尚未验证**

- 背景图片的视觉效果（铺满/拉伸/适应的取舍、毛玻璃强度、颜色层叠加观感）需要人工目视验收。
- 视频背景与网页壁纸仍未迁移：当前背景媒体只支持图片，选择器也只列出图片扩展名。
- 背景播放队列（定时切换、随机播放、视频结束切换）与「最近使用的图像」入口尚未迁移。

## 图片导出验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 导出链路运行时验证 | 以 `-p:EnableUiVerification=true` 发布后运行 `--verify-export`，真实渲染并写出 `EXPORT_OK 800x450 tiles=1 bytes=14154` |
| PNG 内容检查 | 采样 14400 个像素得到 113 种颜色：底色 `#1F1F1F`、磁贴面 `#161616`、主题绿 `#65D46E`、文字白 `#FFFFFF`，确认不是空白图 |
| 设置页运行时自检 | 十二个分类全部构建成功（`ALL_SECTIONS_OK`） |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Linux 交叉发布 + WSL 真实启动 | 无错误输出 |

**尚未验证**

- 导出预览的拖动排版、选中缩放与红色越界提示需要人工交互验收（自动验证只覆盖"能渲染出正确的 PNG"）。
- 「最近使用的图像」入口与导出页的标题字体选择（旧版支持自定义字体）尚未迁移：当前标题固定使用随包字体，背景图片沿用了看板的显示模式设置。

## 作业板缩放与无限作业板验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 缩放运行时验证 | `--verify-zoom` 在 20%／100%／250%／400% 四档下检查滚动范围：864 → 2160 → 3456，全部 `match=True scrollable=True`，结果 `ZOOM_OK` |
| 设置页运行时自检 | 十二个分类全部构建成功（`ALL_SECTIONS_OK`） |
| 导出运行时验证 | `EXPORT_OK 800x450 tiles=1 bytes=14154` |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Linux 交叉发布 + WSL 真实启动 | 无错误输出 |

**本轮修复的真实缺陷**

- 首版缩放把基准画布尺寸写成「视口 ÷ 缩放」，导致放大后滚动范围恰好等于视口、永远滚不动。首轮自检的断言只比对了两个同源数值，差点让这个缺陷漏过去——把断言改成「放大后滚动范围必须大于视口」后才暴露并修复。这也是当前缩放验证新增 `scrollable` 判定的原因。

**尚未验证**

- 缩放后的实际观感（文字与笔迹清晰度、网格线宽随缩放的视觉变化）需要人工目视验收。
- 触摸双指捏合缩放与触摸平移尚未实现（旧版依赖系统手势），当前提供缩放岛、Ctrl + 滚轮与鼠标中键平移。
- 缩放悬浮岛当前固定在控制窗上方，旧版会按控制窗的停靠位置与尺寸贴合在它旁边；精确的浮岛定位属于待迁移的浮岛布局工作。

## 浮岛定位与控制窗动画验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 浮岛运行时验证 | `--verify-islands`：横置控制窗时画笔栏在左（pen=162 ≤ 642）、缩放岛在右（zoom=808 ≥ 右侧）；竖置时画笔栏在上（bottom=410 ≤ 420）、缩放岛在下（top=490 ≥ 底部）；结果 `ISLANDS_OK` |
| 自动隐藏验证 | 空闲 1.6 秒（设定 1 秒）后 `hidden=True opacity=0.00`；模拟操作后 `visible=True opacity=1.00` |
| 其余自检 | 设置页 11 个分类 `ALL_SECTIONS_OK`、导出 `EXPORT_OK 800x450`、缩放 `ZOOM_OK` |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Linux 交叉发布 + WSL 真实启动 | 无错误输出 |

**本轮修复的真实缺陷**

- 控制窗按钮「无字模式」包装图标时，先把图标放进新面板再替换按钮内容，触发 Avalonia 逻辑父级冲突（`AttachedToLogicalTreeCore called for 'FluentIcon' but control has no logical parent`）。首次调用恰好能通过，是切换控制窗位置时再次刷新才暴露；现已改为先解除按钮与图标的关联再包装。

**尚未验证**

- 飞出动效的动画观感与不同停靠位置下的飞出方向需要人工目视验收（自动验证只覆盖淡出/恢复的最终状态）。
- 富文本工具条已在后续轮次迁到控制窗旁的悬浮岛，见本文件末尾的验收记录。

## 自动更新验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 更新链路端到端验证（真实 Release） | `PANCAKE_UPDATE_VERIFY_DOWNLOAD=1` 下运行 `--verify-update`：查到最新版 `v2.0.7`、判定为更新、下载 `Pancake-win-x64-2.0.7.zip`（90,352,339 字节）、校验并解压成功（304 个文件）、生成覆盖配置 `update.json`，结果 `UPDATE_OK` |
| 更新检查（不下载） | 同一验证不带环境变量时只做网络检查，结果为 `UPDATE_OK`；已接入 CI |
| 其余自检 | 设置页 `ALL_SECTIONS_OK`、浮岛 `ISLANDS_OK`、导出 `EXPORT_OK 800x450`、缩放 `ZOOM_OK` |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项（含解压、覆盖、数据保留、锁定文件回滚与危险路径拒绝） |
| Linux 交叉发布 + WSL 真实启动 | 无错误输出 |

**本轮修复的真实缺陷**

- 验证代码里 `Path` 与 `Assembly` 的引用不完整（`Avalonia.Controls.Shapes.Path` 与 `System.IO.Path` 冲突、缺少 `System.Reflection`），导致验证构建失败——这类问题只在开启 `EnableUiVerification` 时出现，正式构建不会暴露，因此每次加验证入口都要重新跑一次验证构建。

**尚未验证**

- 真实覆盖更新的最后一步（退出程序、脚本替换文件并重启）没有在本机执行：验证只到"生成覆盖配置"，实际覆盖由旧版的 `ApplyUpdate.ps1` 与 `tests/UpdateLogic` 的隔离测试覆盖，但**当前程序目录的整体替换没有实测**，需要在一份可丢弃的副本目录里验证一次。
- 下载中断、磁盘空间不足、目录不可写等失败路径的用户提示需要人工验收。

## 背景媒体进阶验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 播放队列运行时验证 | `--verify-playlist` 每 200 毫秒采样当前媒体，得到序列 `bg0 → bg1 → bg0 → bg1 → bg0`；中途删除 `bg2` 后每次轮播都正确跳过，未加载已删除文件，结果 `PLAYLIST_OK` |
| 其余自检 | 设置页 `ALL_SECTIONS_OK`、浮岛 `ISLANDS_OK`（含自动隐藏与恢复） |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS（含混合队列、随机播放与持久化）、UpdateLogic 32 项 |
| Linux 交叉发布 + WSL 真实启动 | 无错误输出 |

**本轮修复的真实缺陷**

- 验证脚本最初依赖 `Assets/Settings/tile-preview-background.png`，但 Avalonia 把界面资源编译成嵌入资源、不会复制到输出目录，脚本因此找不到示例图。改为运行时用 `RenderTargetBitmap` 生成三张纯色临时图片，验证不再依赖打包布局。

**尚未验证**

- Wallpaper Engine 导入需要本机安装 Wallpaper Engine 并下载过视频壁纸才能实测；当前只做到"检测到安装才显示入口"，未在真实 WE 环境验证导入结果。
- 队列的视觉效果（切换时的衔接、随机播放不连续重复的观感）需要人工目视验收。
- 视频背景见下一节；当时尚未实现，本轮已补上并单独验收。

## 视频背景验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 视频解码运行时验证 | 用 ffmpeg 生成 4 秒测试片段后运行 `--verify-video`：`libvlcSupported=True`、`playing=True position=901ms`、1.5 秒后 `position=2151ms advanced=True`，结果 `VIDEO_OK`——证明原生库可用且解码真正在推进 |
| 原生库随包分发 | Windows 发布目录包含 `libvlc/win-x64/libvlc.dll`（Windows 与 macOS 由 NuGet 原生包提供，Linux 使用发行版 libvlc） |
| 其余自检 | 设置页 `ALL_SECTIONS_OK`、播放队列 `PLAYLIST_OK` |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Linux 交叉发布 + WSL 真实启动 | 无错误输出（Linux 未安装 libvlc 时按设计降级为图片背景） |

**本轮修复的真实缺陷**

- 首次运行视频验证时程序直接崩溃：Avalonia 的 `NativeControlHost`（LibVLC 的渲染面基于它）在 Windows 上创建子窗口时要求应用清单声明 `supportedOS`，否则抛出 `Unable to create child window for native control host`。原 `app.manifest` 只有 DPI 与长路径设置，现已补上 Windows 7/8/8.1/10 的兼容性声明。**这个缺陷会让任何视频背景在 Windows 上直接崩溃，属于必须修的启动级问题。**

**尚未验证**

- 视频背景的实际观感（画面铺满方式、与颜色层叠加、切换衔接）需要人工目视验收；毛玻璃对原生渲染面不生效，当前实现会在视频时跳过媒体层模糊。
- Linux 端需要发行版安装 `libvlc`（如 `vlc-plugin-base`）才能播放视频；未安装时自动降级为图片背景，这条降级路径已在 WSL 中启动验证，但**未在装有 libvlc 的 Linux 桌面验证过真实播放**。
- 多显示器与高负载下的流畅度、视频解码失败时的降级提示需要人工验收。
- 网页壁纸见下一节；本轮已补上并单独验收。

## 网页壁纸验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 网页壁纸运行时验证 | 在临时目录里生成最小网页壁纸项目（`project.json` + `index.html`）后运行 `--verify-web`：`hostSupported=True`、宿主创建承载控件并完成加载 `ready=True`，结果 `WEB_OK` |
| 其余自检 | 设置页 `ALL_SECTIONS_OK`、播放队列 `PLAYLIST_OK`、视频 `VIDEO_OK`（position 901ms → 2400ms 推进） |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Linux 交叉发布 + WSL 真实启动 | 无错误输出（网页壁纸入口在非 Windows 平台隐藏） |

**实现说明**：官方 `Avalonia.Controls.WebView` 依赖 `AvaloniaUI.Licensing`（商业授权），社区方案要么依赖约 200MB 的 CEF、要么没有明确授权信息，因此这里改为直接用微软 WebView2 核心 API，并由控件自行创建承载用的 Win32 子窗口（Avalonia 的 `NativeControlHost` 要求），不引入任何商业授权依赖。

**尚未验证**

- 网页壁纸的实际观感（页面缩放、动画帧率、与颜色层的叠加）需要人工目视验收。
- WebView2 运行时缺失的 Windows 环境、页面加载失败时的降级提示需要人工验收；当前实现会报告失败但不自动回退到图片背景。
- Linux 与 macOS 的网页壁纸入口按方案保持隐藏（不打包 WebKitGTK）。

## 富文本悬浮岛验收记录（2026-09-13）

**本轮已验证**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 悬浮岛运行时验证 | `--verify-richtext-island`：聚焦作业编辑器后 `visible=True`、工具数 `tools=5`、首个工具为加粗 `boldFirst=True`、贴在控制窗左侧（island=328 ≤ toolbar=570），结果 `RICHTEXT_ISLAND_OK` |
| 与画笔互斥 | 开启画笔后 `penModeHidesIsland=True`（格式工具让位给画笔栏） |
| 其余自检 | 设置页 `ALL_SECTIONS_OK`、浮岛 `ISLANDS_OK` |
| 既有回归测试 | RichTextLogic 25 项、ProjectLogic 120 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Linux 交叉发布 + WSL 真实启动 | 无错误输出 |

**本轮修复的真实缺陷**

- 首版把富文本悬浮岛直接复用画笔栏的坐标，但两块岛宽度不同，导致它们互相压住（验证报 `leftOfToolbar=False`）。改为每块岛按自身尺寸贴合控制窗后通过。

**尚未验证**

- 悬浮岛在竖置控制窗（左右居中）下改为上下摆放后的观感需要人工目视验收。
- 格式工具对选区的实际作用效果（加粗/颜色/高光）仍需在真实输入下人工确认；本轮只验证了工具存在、位置正确且与画笔互斥。

## 旧版功能对齐轮：控制窗与浮岛外观、颜色编辑器、自动填充页（2026-09-13）

本轮的起点是一次系统对齐：把 2.x 的设置页控件与用户可见文案逐项与 Avalonia 版本比对（`legacy/Pancake.WinUI` 快照对比 `src/Pancake` 的字符串与控件清单），找出真正缺失的行为并补齐。

**本轮补齐的旧版行为**

| 位置 | 补齐内容 |
| --- | --- |
| 启动路径 | 之前启动时从不调用 `ApplyToolbarAppearance()`，保存的控制窗停靠位置、缩放、无字模式与背景在重启后不生效，直到用户改动任一设置才应用；现在启动、回到看板时都会重新应用 |
| 控制窗与浮岛 | 三块浮岛（画笔栏、缩放岛、富文本岛）改为与控制窗共用同一套外观：圆角、颜色层、毛玻璃背衬、缩放比例、无字模式名称；左右居中停靠时控制窗与浮岛一起改成竖排，缩放滑条改成竖向。浮岛定位改为按缩放后的实际占位计算，修掉了缩放控制窗后浮岛位置偏移的问题 |
| 控制窗毛玻璃 | 旧版的毛玻璃背衬在 Avalonia 版本里没有实现（设置里存在但界面未暴露）。现在把作业板拍成快照后模糊，作为控制窗与三块浮岛的背衬，颜色层单独叠加；设置页补上「毛玻璃效果」「模糊程度」，清除颜色后毛玻璃仍然保留 |
| 颜色编辑器 | 背景颜色面板补上取色器与「应用颜色」，透明度改为按「背景颜色透明度（%）」显示，并补上「恢复主题背景色」 |
| 自动填充 · 学科 | 颜色浮层补上「自定义颜色」取色器与「随机」互斥逻辑，色卡改为「色卡 + 默认颜色／随机颜色」并显示预设色渐变 |
| 自动填充 · 作业 | 补上生效范围（全局或任选学科）、条目改名、被屏蔽词列表与「恢复收录」、手动「添加作业类型」，以及三条切词与过期规则说明 |
| 图片导出 | 补上旧版的「色系」选择：只重算导出副本的预设主题色与富文本预设色，看板数据不受影响 |
| 背景媒体 | 队列的「添加多个媒体」现在允许图片与视频混合（之前只列图片扩展名）；「视频播放完成时切换」的说明改为描述真实行为（之前写着"当前只支持图片"），无视频能力的平台该行禁用 |
| 查看模式提示 | 补上全屏查看时拖动看板后短暂高亮并展开「退出全屏」的提示（2.2 秒后自动恢复）；网格吸附按钮补上显示当前状态与网格步长的气泡 |

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | `dotnet build Pancake.slnx -c Release` 通过，0 错误（`NU1900` 仅在还原时出现，见下） |
| 核心逻辑回归 | ProjectLogic 120 项、RichTextLogic 25 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| 浮岛自检 `--verify-islands` | `ISLANDS_OK`：横置时画笔栏在左（pen=162 ≤ 642）、缩放岛在右（zoom=808 ≥ 右侧）；竖置时画笔栏在上（bottom=362 ≤ 372）、缩放岛在下（zoomTop=538）；`appearance radiusOk=True backgroundOk=True verticalItems=True`；`labels iconOnlyOk=True labelOk=True`；`glass snapshotOk=True clearedOk=True`；自动隐藏 `hidden=True opacity=0.00` 与恢复 `visible=True opacity=1.00` |
| 启动应用设置 | 把数据文件里的控制窗位置改成 `TopLeft` 后启动，自检读到的启动状态就是 `TopLeft applied=True`；恢复到默认 `BottomCenter` 后同样为 `applied=True` |
| 导出自检 `--verify-export` | `EXPORT_OK 800x450 tiles=1 bytes=14872 titleFonts=138 paletteRoundTrip=True`：切成马卡龙再切回后，导出副本的主题色发生变化、看板的主题色未变、PNG 仍然渲染成功 |
| 其余自检 | 设置页 `ALL_SECTIONS_OK`（11 个分类）、缩放 `ZOOM_OK`（20%／100%／250%／400% 与捏合 1.00→1.50）、富文本悬浮岛 `RICHTEXT_ISLAND_OK`、播放队列 `PLAYLIST_OK`、网页壁纸 `WEB_OK`、视频 `VIDEO_OK`（900ms → 2400ms 推进）、更新检查 `UPDATE_OK`（查到 v2.0.7） |
| Linux / macOS 交叉发布 | `-r linux-x64 -p:PublishPlatform=linux` 与 `-r osx-arm64 -p:PublishPlatform=macos` 均生成自包含产物 |
| Linux 真实启动 | 把 Linux 产物复制到 WSL 的 `/tmp` 后启动，12 秒内进程持续运行（`timeout` 返回 124），`run.log` 为 0 字节，说明没有启动崩溃或异常输出 |
| Linux 真实启动 | 在 WSL Ubuntu 中把 Linux 产物复制到 `/tmp` 后启动，运行 12 秒无任何错误输出、进程被超时结束（exit=124），说明没有启动崩溃 |

**本轮修复的验证基础设施问题**

- 自检会改动控制窗位置、缩放、无字模式、毛玻璃等设置，而退出时会触发保存，导致用户数据被自检结果污染。现在所有 `--verify-*` 入口在启动时记下 `projects.json` 原文，退出前原样写回；用哈希比对确认自检前后文件完全一致。
- 发布目录里若预先存在 `data` 文件夹，程序会按便携模式读取该目录的数据，自检读到的就不是用户数据（上一轮的产物目录里遗留了 `data`，导致启动位置自检一度误判）。现在自检报告里会带上实际读到的设置值，便于发现这类环境问题。

**尚未验证（不得当作已通过）**

- 毛玻璃的观感（背衬模糊强度、颜色层叠加、视频背景下的背衬延迟）需要人工目视验收；本轮只验证了开关生效、快照存在与关闭后撤下。
- 浮岛在竖版控制窗下竖排后的观感、以及浮岛按钮名称在真实字号下的换行与对齐需要人工目视验收。
- 全屏退出提示与网格吸附气泡需要真实鼠标／触摸滑动触发才能看到；本轮只验证了实现与状态映射，没有做真实手势回放。
- 自动填充作业页的生效范围浮层、改名对话框、恢复收录与手动添加需要真实点击验收；本轮只保证这些控件能在自检构建中创建。
- 导出色系在真实项目上的观感（预设色与自定义色混用）需要人工确认。

**本轮仍未实现（下一轮待办，已写入 README 的「当前限制」）**

- 设置页的实时外观预览：2.x 在布局、磁贴、背景板、网格与控制窗页都有真实控件或快照预览，Avalonia 版本当时只有即时生效、没有预览。**已在下一轮实现，见本文件末尾的「设置页实时预览与自动排列收紧」记录。**
- 竖版控制窗下三块浮岛与控制窗的「等宽／等高」约束：**已在后续轮次实现（富文本岛与缩放岛对齐控制窗截面，画笔栏按旧版保持自然尺寸），见本文件末尾的「窗口材质、自动隐藏例外与平台能力收尾」记录。**

## 设置页实时预览与自动排列收紧（2026-09-13）

上一轮列出的待办「设置页实时预览」在本轮实现；同时补上了另一处功能差距：自动排列过去只按磁贴当前尺寸摆放，没有 2.x 的「按内容收紧、必要时收窄换行」。

**本轮补齐的旧版行为**

| 位置 | 补齐内容 |
| --- | --- |
| 设置页预览（新增） | 布局页、磁贴页、网格页、控制窗页、背景板页各自带一块实时预览：固定设计尺寸 640×300 经 Viewbox 等比缩放，窄窗口不会撑宽设置页；预览只读、不接保存回调、不占用键盘导航 |
| 布局预览 | 用真实的 `SubjectTileControl` 放四个示例科目，按当前网格大小、吸附、自动对齐、自动调整大小、间隔走真实的 `BoardLayout.Arrange`，角标显示「网格 X px」与「自动排列 · 间隔 X px」 |
| 磁贴预览 | 随包分发的示例底图（浅色模式加一层白色提亮）+ 固定示例磁贴：预设黄色主题色、三行正文（加粗、白色、`astra` 带当前色系高光），标题字号与磁贴背景改动即时生效 |
| 网格预览 | 左「查看模式」右「编辑模式」两块对照，绘制逻辑与看板共用新的 `GridRenderer`，样式、颜色、粗细、点直径完全同源 |
| 控制窗预览 | 按当前停靠、缩放、圆角、颜色层、无字模式与边距搭一块迷你控制窗，并从窗口坐标里裁出控制窗附近一块，保持宽高比又不会缩得太小 |
| 背景板预览 | 跨区、时钟区、作业板三块预览：底色、图片、毛玻璃、显示模式与分屏比例都是真实层级（复用 `BackgroundVisual`），时钟与天气文本实时绑定到看板上的控件 |
| 自动排列收紧 | `SubjectTileControl` 新增 `MeasureContentSize`：测量标题与每条作业，按列宽上限收窄后重新测量高度，并把笔迹范围计入；`ArrangeTiles` 改成「从最多列开始试 → 按列宽收窄让文字换行 → 仍放不下等比缩小 → 最后提示开启无限作业板」，关闭「自动调整磁贴大小」时保持用户尺寸 |
| 网格绘制抽公共实现 | 新增 `GridRenderer`，看板网格与设置页预览共用一份绘制逻辑（含预览的偏移与线宽补偿），避免两边样式漂移 |

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 设置页自检 `--verify-settings-pages` | `ALL_SECTIONS_OK previews=Layout:274/16012,Tile:61/109195,Grid:33/3652,Toolbar:23/1492,Shared:75/11291,Clock:22/14324,Board:56/4105`：11 个分类都能构建，七块预览都渲染出非空内容（数字为「元素数 / PNG 字节数」，纯色底只有几百字节） |
| 预览跟随设置 | 自检里把网格大小从 48 改成 120，布局预览角标随之变化；把控制窗停靠改成左上／右下，控制窗预览的坐标随之变化——两处断言在接入前都会失败，接线后才通过 |
| 预览不改数据 | 预览里的磁贴用独立模型副本（`CaptureSubjects`/`RestoreSubjects` 往返）与示例科目，不写入项目；自检结束时用哈希比对确认 `projects.json` 与验证前完全一致 |
| 自动排列自检 `--verify-autolayout` | `AUTOLAYOUT_OK`；`content viewport=948x768 tightened=True insideBoard=True noOverlap=True`，`widths=280,280,316,280`（示例原本统一 900 宽）；关闭自动调整后 `kept=320,320,320,320` 保持不变；窗口收到 760×560 后 `wide=280x96,280x181,360x96,280x130` → `narrow=280x96,280x181,316x119,280x130`，最长的一条收窄并换行（高度 96→119） |
| 其余自检 | 导出 `EXPORT_OK … paletteRoundTrip=True`、缩放 `ZOOM_OK`、浮岛 `ISLANDS_OK`、富文本悬浮岛 `RICHTEXT_ISLAND_OK`、播放队列 `PLAYLIST_OK`、网页壁纸 `WEB_OK`、视频 `VIDEO_OK`、更新 `UPDATE_OK` |
| 核心逻辑回归 | ProjectLogic 120 项、RichTextLogic 25 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Linux / macOS 交叉发布 | `-r linux-x64 -p:PublishPlatform=linux` 与 `-r osx-arm64 -p:PublishPlatform=macos` 均生成自包含产物 |

**尚未验证（不得当作已通过）**

- 预览的观感需要人工目视验收：本轮只能确认预览里有真实元素、渲染出的 PNG 不是空白，以及关键设置改动会被反映出来；配色搭配、字号比例、裁切位置是否好看没有人工确认。
- 控制窗预览不含毛玻璃背衬（预览里没有可采样的背板），实际毛玻璃观感仍以看板为准，这一点已写进 README 的限制。
- 自动排列的收紧效果只在程序化断言（宽度被收紧、不重叠、不越界、窄看板下换行）层面确认，真实教室内容下的排版观感需要人工验收。

## 窗口材质、自动隐藏例外与平台能力收尾（2026-09-13）

这一轮用「设置字段是否真的被界面使用」和「平台接口是否真的被调用」两条线做了一次系统排查：135 个设置项里只有版本号、序列化辅助字段等 4 个属于误报，其余全部接线；平台接口里只有 `IBackdropService`（窗口材质）是一直没被调用的死代码。顺着这条线补了三处行为。

**本轮补齐的旧版行为**

| 位置 | 补齐内容 |
| --- | --- |
| 窗口材质（Windows） | 2.x 通过 `PersistentMicaBackdrop` 给窗口挂 Mica（不支持时回退 Desktop Acrylic），材质只在设置页透出来。Avalonia 版本此前只在平台层实现了 `WindowsBackdropService`，界面从未调用。现在启动时按 Mica → AcrylicBlur → Blur 申请材质，并在**实际生效**（`ActualTransparencyLevel != None`）时把窗口底色设为透明、设置页底色改为同主题色 86% 不透明度，材质不再生效时立即回到纯色 |
| 设置页底色取值 | 主题色来自 `ThemeResources.axaml` 的 ThemeDictionaries。代码里必须用 `Application.TryGetResource(key, 主题变体, out value)` 才能取到；不带变体或在窗口上查找都会返回 null（这两种写法都在自检里被证伪），因此实现里固定用应用 + 实际主题变体查一次 |
| 自动隐藏的例外·弹出层 | 2.x 把「打开的弹出层」算作正在交互，弹出层开着时不隐藏控制窗。Avalonia 版本此前只判断磁贴拖动、指针悬停浮岛、画笔与对话框。现在补上弹出层判断：弹出层由框架托管在独立宿主里、不在窗口可视树中，因此用框架的轻量消失层（`LightDismissOverlayLayer.IsVisible`）加上自己的候选浮层状态一起判断 |
| 自动隐藏的例外·按下的指针 | 2.x 记录“按下的指针”，按住不放（例如拖动分隔条、滚动条）时不会隐藏控制窗。现在同样记录按下的指针，释放或指针捕获丢失时移除，窗口失焦时清空，避免收不到释放事件导致永远不隐藏 |
| 浮岛截面尺寸对齐 | 2.x 让富文本岛与缩放岛在横置控制窗下与控制窗等高、竖版控制窗下等宽。Avalonia 版本此前只做到竖排与整体缩放。现在按缩放后的实际控制窗截面换算回布局尺寸并对齐两块浮岛（画笔栏内容更高，与旧版一样不参与对齐），并让布局稳定后自动重新对齐——切回横置时若只依赖切换瞬间的旧尺寸，浮岛会停在竖版的高度（自检一度报 156 而不是 60） |
| 磁贴预览的固定区 | 2.x 在窗口够宽时把磁贴预览固定在设置页右侧，窄窗口回到页内位置。Avalonia 版本此前只有页内预览。现在设置页多了第三列作为固定区，够宽时把同一个预览场景整体搬过去铺满，窄了再搬回页内；判定宽度用「设置页总宽 − 左侧导航列」而不是滚动区宽度，否则固定区出现会把滚动区压窄、判定又会翻转（自检分别抓到过“宽窗口不固定”和“窄窗口不收回”两种抖动） |

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告 0 错误 |
| 窗口材质自检（并入 `--verify-settings-pages`） | `backdrop=True/Mica/translucent=True/theme=#ff0e0e12`：平台支持材质、实际生效 Mica、设置页底色为半透明、主题色取自主题字典；自检同时断言“有材质必须半透明、没有材质必须纯色”，两者不一致直接失败 |
| 弹出层挡自动隐藏（`--verify-islands`） | `popupGuard detected=True visible=True dismissLayers=1`：打开一个真实浮层后等待 1.6 秒（超过 1 秒的自动隐藏时间），控制窗仍然可见；关闭浮层后紧接着的断言 `autoHide hidden=True opacity=0.00` 说明自动隐藏恢复正常。这条断言在接入前失败过一次（当时检测不到弹出层、控制窗被藏了起来），修好检测后通过 |
| 浮岛截面尺寸（`--verify-islands`） | `crossSize vertical expected=60 zoom=60 rich=60 ok=True` 与 `crossSize horizontal expected=60 zoom=60 rich=60 ok=True`：竖版控制窗（缩放 1.25）下两块浮岛与控制窗等宽、高度保持自动；切回横置后与控制窗等高、宽度保持自动。第一版自检用未缩放的占位做基准，得到 48/60 的假差异，改成按缩放后的实际占位换算后一致 |
| 磁贴预览固定区（`--verify-settings-pages`） | `previews=…,TileHosting:sticky+inline,…`：窗口 1680 宽时预览挂在右侧固定区（`SettingsPreviewHost.IsVisible=True`、场景父级是固定区、页内框隐藏），窗口收到 1000 宽后回到页内位置（固定区隐藏、场景父级是页内 Viewbox）；两种状态共用同一个场景实例 |
| 其余自检 | 设置页 11 个分类 + 七块预览 `ALL_SECTIONS_OK previews=…`、自动排列 `AUTOLAYOUT_OK`、导出 `EXPORT_OK … paletteRoundTrip=True`、缩放 `ZOOM_OK`、浮岛 `ISLANDS_OK`、富文本悬浮岛 `RICHTEXT_ISLAND_OK`、播放队列 `PLAYLIST_OK`、网页壁纸 `WEB_OK`、视频 `VIDEO_OK`、更新 `UPDATE_OK` |
| 核心逻辑回归 | ProjectLogic 120 项、RichTextLogic 25 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| Linux / macOS 交叉发布 | `-r linux-x64 -p:PublishPlatform=linux` 与 `-r osx-arm64 -p:PublishPlatform=macos` 均生成自包含产物；Linux 产物在 WSL 中启动 12 秒无异常输出 |
| 验证数据隔离 | 全部自检结束后用哈希比对确认 `projects.json` 与验证前完全一致 |

**尚未验证（不得当作已通过）**

- 材质观感需要人工目视验收：本轮确认的是「申请了材质、平台回报 Mica、界面切到半透明」，但半透明设置页在真实桌面壁纸上的可读性与观感没有人工确认；在 Windows 10（无 Mica）上系统回退 Acrylic 的表现也未实测。
- 「按住指针拖动时不隐藏」只做了代码接线与阈值判断，没有真实的按住拖动回放（自检无法合成带按键状态的指针），这一条只有代码审查级别的证据。
- 浮岛与控制窗等宽/等高的判定按像素容差（1.5px）比较，观感上是否真的“像同一块面板”仍需要人工目视验收。

**有意保留的差异（已写进 README 的「当前限制」）**

- 窗口标题栏：2.x 使用自绘标题栏（`ExtendsContentIntoTitleBar` + 顶栏作为拖动区），Avalonia 版本保留系统标题栏。三条平台各自的系统窗口行为（拖动、缩放、贴边、窗口按钮位置）由系统提供，跨平台一致性更好；顶栏的 `BrandRegion` 因此只作为品牌区使用，不再承担拖动职责。

## Avalonia Headless 界面测试（2026-09-13）

方案里约定的「Avalonia Headless 测试」这一项此前没有落地，界面检查只能在 Windows 真实窗口里跑；上一轮又留下两条「只有代码审查级证据」的行为（全屏退出提示、按住指针不隐藏）。这一轮补上了 Headless 测试工程，并顺手修掉它暴露出的一个跨平台崩溃点。

**本轮新增**

| 内容 | 说明 |
| --- | --- |
| `tests/UiHeadless`（新增工程） | Avalonia Headless 宿主：关闭无头桩渲染、改用真实 Skia（随包字体与文本排版才和正式运行一致），把真实输入管线交给界面层检查；不需要显示设备，三个平台都能跑，已加入 CI 矩阵的每个平台 |
| 界面层检查 `HeadlessUiChecks` | 放在 Pancake.Ui 里、只在 `EnableUiVerification=true` 时编译：主窗口与全部设置分类构建、深色／浅色往返切换（含线条画刷颜色）、磁贴尺寸与模型一致性、磁贴不越出作业板、命中测试在磁贴内外落在正确元素上、全屏退出提示经真实拖拽出现并在编辑／窗口模式保持安静、按住指针时不自动隐藏 |
| 验证用入口 `MainWindow.Verification.cs` | 只暴露自检需要的状态与操作（切换设置分类、进入／结束编辑、全屏、退出提示是否展开、控制窗是否隐藏、按下的指针数量、推进空闲时间）；正式包在关闭验证开关时不编译这些成员 |
| 定时器行为的覆盖方式 | Headless 环境不推进调度器定时器，因此「提示自动收起」「空闲自动隐藏」仍在 Windows 真实窗口自检里验证：`--verify-islands` 新增 `fullScreenHintTimer shown=True cleared=True` 一行 |

**本轮修复的真实缺陷**

- `VideoPlayerHost.IsSupported` 在没有原生 libvlc 的机器上会**抛异常**而不是返回「不支持」：LibVLCSharp 抛的是 `VLCException`，而原来的 `catch` 只捕获了 `DllNotFoundException`／`InvalidOperationException`／`TypeInitializationException`。任何构建背景设置卡片的代码路径（外观·磁贴页、背景板页）都会因此直接崩溃——这正是 Headless 测试在 Linux 上第一次运行时暴露出来的问题。现在改为任何加载失败都返回 null，视频背景按设计降级为图片背景。

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| Headless 界面测试（Windows） | `HEADLESS_OK`：`window+sections:ok(11 个分类,2 个科目)`、`theme:light:#ffd9d9e6->dark:#2a2a35->light:#d9d9e6`、`tiles:layout+hit:ok(2 块,888x768)`、`fullScreenHint:shownByDrag+silent(editing,windowed)`、`pressedPointer:keptByPress+hiddenAfterRelease` |
| Headless 界面测试（Linux） | 用 `-r linux-x64 -p:PublishPlatform=linux` 发布自包含产物后在 WSL 中直接运行（无显示设备），输出与 Windows 完全一致的 `HEADLESS_OK` 六行——这就是方案里要求的「非 Windows 平台用 headless 冒烟测试替代」 |
| 退出提示自动收起（真实窗口） | `--verify-islands` 输出 `fullScreenHintTimer shown=True cleared=True`：真实调度器定时器 2.2 秒后确实收起了提示 |
| 其余真实窗口自检 | 浮岛 `ISLANDS_OK`（含等宽／等高、外观跟随、毛玻璃、弹层挡自动隐藏）、设置页 `ALL_SECTIONS_OK`（含七块预览与固定区切换、材质一致）、自动排列 `AUTOLAYOUT_OK`、导出 `EXPORT_OK … paletteRoundTrip=True`、缩放 `ZOOM_OK`、富文本悬浮岛、播放队列、网页壁纸、更新检查全部通过 |
| 核心逻辑回归 | ProjectLogic 120 项、RichTextLogic 25 项、SettingsLogic 全部 PASS、UpdateLogic 32 项 |
| 数据隔离 | 所有自检结束后哈希比对确认 `projects.json` 与验证前一致；Headless 测试使用独立的临时数据目录并在结束时删除 |

**文档更正**：此前多处把设置分类写成 12 个，实际是 11 个（布局、外观×5、组件×2、自动填充×2、关于）；Headless 自检会把真实数量打印出来，本轮已把 README 与本文档中的数字更正为 11。

**尚未验证（不得当作已通过）**

- Headless 使用真实 Skia 渲染，但仍然是离屏渲染：真实触屏与触控笔的压感/滑动手感仍需真机验收。
- Headless 无法合成带按键状态的复杂手势（例如多指捏合、长按拖动磁贴并同时滚动），这类交互仍以真实窗口与真机验收为准。

## 缩放档位与命中区域检查（2026-09-13）

方案里要求 Headless 测试「在 100%／125%／150%／200% 缩放下校验尺寸与命中区域」。Headless 平台不提供渲染缩放开关（`AvaloniaHeadlessPlatformOptions` 只有 `UseHeadlessDrawing`），因此这一轮用「同一份 DIP 布局按不同像素密度渲染」的方式覆盖，并补上截图证据。顺带发现设置页截图必须先把设置视图切出来（`SettingsRoot` 在 XAML 里默认隐藏，直接构建分类页时根容器是 0×0）。

**本轮新增与修复**

| 内容 | 说明 |
| --- | --- |
| 缩放与命中区域检查 | 取控制窗里真实显示的第一个按钮中心作为探针：在 100%／125%／150%／200% 四档下分别渲染一帧，断言像素尺寸等于 DIP 尺寸 × 倍率（容差 2px）、四档之间该控件的 DIP 布局宽度不变、同一 DIP 坐标每次都命中同一个按钮 |
| 命中目标尺寸 | 断言查看模式下控制窗可见按钮的最小边不小于 40 DIP（实测 44 DIP），并按「真实显示的按钮」筛选（收起状态按钮的 Bounds 为 0，不能参与统计） |
| 截图证据 | 除渲染断言外，把四档看板截图与设置页截图写到 `headless-evidence/`：Windows 上分别是 60KB／85KB／102KB／158KB 与 171KB（PNG 体积随分辨率增长，说明真的渲染了内容而不是空白）；Linux 上为 67KB／88KB／109KB／151KB 与 198KB |
| 设置视图入口 | 自检补充 `ShowSettingsForVerification()`，让设置页真的显示出来再截图与断言（此前只构建页面内容，根容器仍是隐藏状态） |
| CI | Headless 步骤在失败时上传 `headless-evidence` 目录，便于直接看截图定位问题 |

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| Headless 界面测试（Windows） | `HEADLESS_OK` 六行；其中缩放行：`scaling+hitTargets:minTarget=44dip frames=100%:1440x832,125%:1800x1040,150%:2160x1248,200%:2880x1664 settingsShot=ok` |
| Headless 界面测试（Linux） | 自包含发布后在 WSL 中运行（无显示设备），输出与 Windows 完全一致，并在 `headless-evidence/` 生成五张截图 |
| 其余检查 | 主窗口与 11 个设置分类、主题切换、磁贴布局与命中测试、全屏退出提示、按住指针不隐藏全部通过 |

**尚未验证（不得当作已通过）**

- 这里验证的是「同一份 DIP 布局在不同像素密度下渲染正确、命中测试按 DIP 工作」；真实显示器缩放（系统 DPI 125%/150%）下窗口尺寸、字体渲染与触屏坐标换算仍需真机验收，Headless 平台无法设置该缩放。

## 富文本字体链路补齐（2026-09-13）

这一轮换了一个审计角度：找「模型里有、界面从不读取」的成员。结果命中三个真实缺陷——2.x 的富文本工具栏第一个就是字体选择器，而 Avalonia 版本既没有入口、渲染也不读字体格式，保存时还会把字体丢掉。

**本轮修复的真实缺陷**

| 缺陷 | 影响 | 修复 |
| --- | --- | --- |
| 显示层在格式改动后不重绘 | `ApplyFormat` 与输入同步都把**同一个** `RichTextDocument` 实例赋给显示层，属性值没变就不会触发变化通知，因此加粗／颜色／高光／字体要等到下一次输入才在界面上生效 | 套用格式与输入同步后显式调用显示层重绘；这是本轮最重要的修复，它同时影响此前所有格式工具的即时反馈 |
| RTF 写入只有单一字体表 | `RtfCodec.Write` 固定写 `\f0 HarmonyOS Sans SC` 并且每个片段都写 `\f0`，按片段设置的字体保存后消失（解析器本身支持字体表） | 按片段收集字体表并逐个编号，片段切换字体时写 `\fN`；新增回归用例覆盖非 ASCII 字体名（`阿里妈妈东方大楷`）与两个字体混排的往返 |
| 字体既无入口也不参与渲染 | 富文本悬浮岛只有 5 个工具（加粗／斜体／下划线／文字颜色／高光），`RichTextPresenter` 忽略 `FontFamily`，`FontFallbacks` 只做数据往返 | 新增 `FontService.ResolveRichTextFamily`（缺失字体退回随包字体）与 `SelectableFamilies`（随包字体 + 系统字体），显示层按片段套用字体，悬浮岛新增可搜索的字体选择器并排在第一位（与旧版顺序一致），输入层字体跟随光标所在片段 |

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| Headless 字体链路检查（新增） | `richTextFonts:installed=阿里妈妈东方大楷+missingFallback+inputFollows+rtfRoundTrip`：套用本机真实安装的中文系统字体后，RTF 往返仍是该家族名、显示层对该片段使用了它；套用未安装字体时显示层退回随包字体（不出现缺字方框）而原始家族名仍保留；输入层字体跟随光标片段 |
| 富文本回归测试 | `PASS: 28 rich-text model, RTF round-trip, migration and editing checks`（新增 3 项字体往返检查） |
| 悬浮岛顺序 | `--verify-richtext-island` → `RICHTEXT_ISLAND_OK visible=True tools=6 fontFirst=True boldSecond=True`：工具数由 5 变 6，字体在第一位、加粗第二，与旧版一致 |
| 其余检查 | Headless `HEADLESS_OK`（含缩放与截图）、设置页 `ALL_SECTIONS_OK`、浮岛 `ISLANDS_OK`、自动排列 `AUTOLAYOUT_OK`、导出 `EXPORT_OK … paletteRoundTrip=True`、缩放 `ZOOM_OK`、播放队列 `PLAYLIST_OK`、网页壁纸 `WEB_OK` 全部通过；更新检查本轮遇到 GitHub 匿名 API 限流（403 rate limit exceeded），此前同一检查为 `UPDATE_OK`，属于外部环境而非代码问题 |
| 核心回归 | ProjectLogic 120、RichTextLogic 28、SettingsLogic 全部 PASS、UpdateLogic 32 |

**尚未验证（不得当作已通过）**

- 字体选择器的实际交互（搜索、点击、竖版控制窗下的浮层位置）需要人工点击验收；本轮只验证了链路与顺序。
- 「透明输入层 + 格式显示层」在**同一段里混用多种字体或粗细**时光标可能有轻微偏差（字宽不同），这是该方案的固有限制，已写进 README 的限制；旧版 RichEditBox 没有这个问题。
- 旧数据里的 `FontFallbacks` 现在只做序列化往返（原始家族名已经存在格式里，不再需要单独记录），需要用 2.x 项目文件做一次实际导入确认观感一致。

## Wallpaper Engine 导入窗口与限流处置（2026-09-13）

这一轮继续用「模型里有、界面没读」的线索排查：`WallpaperProject.PreviewUri` 与 `TypeLabel` 在模型里存在，但 Avalonia 版本的导入窗口只列了一行「标题（文件名）」文本，而 2.x 的导入窗口是「预览图 + 标题 + 类型」的列表。

**本轮补齐与修复**

| 内容 | 说明 |
| --- | --- |
| WE 导入窗口 | 列表改为数据模板渲染：左侧 96×54 的预览缩略图（按宽度解码，只从项目目录内读取），右侧标题（超长省略）与类型标签「视频壁纸／网页壁纸」；没有预览图时显示「无预览图」占位 |
| Core 侧回归 | `SettingsLogic` 增加预览图与类型标签的检查：读取 `preview` 字段、拒绝项目目录外的预览图、网页项目类型标签为「网页壁纸」 |
| Headless 端到端检查 | 在临时目录里搭一个假 Wallpaper Engine 安装（`wallpaper64.exe` + `projects/myprojects` 下两个项目，一个带预览图、一个不带），替换 `IPlatformFeatures` 与 `IWallpaperEnginePathSource` 两个平台服务后点击真实入口，断言对话框打开、列表两行、缩略图 1 张、占位 1 处、类型标签为「视频壁纸」，最后关闭对话框并还原平台服务 |
| 更新自检的限流处置 | 本轮更新检查连续遇到 GitHub 匿名 API 限流（403 rate limit exceeded）。这是外部配额问题，之前会让自检写 `UPDATE_FAILED` 从而让 CI 步骤失败；现在区分开来：限流写 `UPDATE_SKIPPED_RATE_LIMIT`（CI 跳过该步骤），其它任何异常仍写 `UPDATE_FAILED`。应用自身的行为不变——遇到网络错误照实提示，不会误报「已是最新版本」 |

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| Headless（Windows） | `wallpaperEngineDialog:rows=2 thumbs=1 placeholders=1 typeLabel=视频壁纸`；整套输出仍为 `HEADLESS_OK`（9 行） |
| Headless（Linux） | 自包含发布后在 WSL 中运行，输出与 Windows 完全一致——包括这条 Windows 专属界面路径 |
| 核心回归 | ProjectLogic 120、RichTextLogic 28、SettingsLogic 全部 PASS（新增预览图/类型标签检查）、UpdateLogic 32 |
| 真实窗口自检 | 设置页 `ALL_SECTIONS_OK`、浮岛 `ISLANDS_OK`、富文本悬浮岛 `RICHTEXT_ISLAND_OK`、自动排列 `AUTOLAYOUT_OK`、导出、缩放、播放队列、网页壁纸全部通过；更新检查按预期写 `UPDATE_SKIPPED_RATE_LIMIT` |
| 构建与跨平台 | 解决方案 0 警告 0 错误；Linux 与 macOS 交叉发布成功；Linux 上 Headless 全绿、正式程序启动 12 秒无异常输出；自检后哈希比对确认用户数据未被改动 |

**审计结论（本轮）**

- 模型成员审计（`Pancake.Core` 全部 157 个候选成员）：除私有字段等误报外，只剩 `SchemaVersion`、`IsEmpty`、`End`、`HasHandwriting` 属于「仅核心内部或仅序列化兼容」用途，`PreviewUri`／`TypeLabel` 已在本轮接上界面。
- 控件成员名对照（`SubjectTileControl` 与 2.x 快照）：2.x 独有名字均可一一对应到新实现（改名或移入 `InkCanvasLayer`、`RichTextContent`、`MainWindow` 的悬浮岛），没有发现漏实现的行为。

**尚未验证（不得当作已通过）**

- 真实 Wallpaper Engine 安装下的导入观感（缩略图比例、长标题省略、多选与队列导入）仍需在装有 WE 的 Windows 上人工验收；本轮用的是假安装与假项目。

## 手写链路审计与端到端验证（2026-09-13）

手写此前是三端唯一「有实现但完全没有自动化界面验证」的子系统：2.x 的自检里有一个绕过指针事件直接落笔的入口，Avalonia 版本连这个入口都没有，而 README 描述的「在任意磁贴书写」因此从未被程序化确认。

**本轮修复**

| 问题 | 说明 |
| --- | --- |
| 渲染时折线点被重复添加 | `InkStrokeVisual.Create` 已经按笔迹填充折线点，`InkCanvasLayer` 的渲染与落笔路径又各加了一遍，导致每条笔迹的折线点翻倍（视觉无差异，但白占内存与渲染量）。现在点只在创建时填充一次，落笔时只追加新点 |
| 橡皮擦命中判定与旧版不一致 | 之前用「点的横纵坐标差值都小于半径」的方形判定；旧版用核心层 `InkGeometry.HitTest`（点到线段距离）且半径为 `max(8, 粗细 × 笔尖缩放)`。现在改成同一套判定与半径，仍保留「一次采样删除所有命中笔迹」这一对触屏更友好的行为 |
| 缺少手写端到端验证 | 新增 Headless 检查：真实指针在磁贴上落笔 → 笔迹进入模型且颜色／粗细沿用画笔栏 → 所有采样点都在磁贴内（越界部分不记录）→ 第二笔后可撤销且只撤最后一笔 → 橡皮擦整条删除 → 清空生效 |

**自检搭台修正（重要）**：Headless 检查此前只替换了科目列表、没有建立真实项目。没有当前项目时回到看板会显示空状态引导面板并盖住看板内容，于是磁贴在窗口里「看得见却点不到」——这条路径把我的手写检查卡了整整一轮。现在自检会先 `CreateProject`，并新增一条回归守卫：从设置页回到看板后磁贴必须仍然可命中。

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| Headless 手写检查（Windows） | `ink:draw+inBounds+undo+erase+clear(redrawn=1)+boardHitAfterSettings` |
| Headless 手写检查（Linux） | 自包含发布后在 WSL 中运行，输出与 Windows 完全一致 |
| 整套 Headless 输出 | `HEADLESS_OK` 共 10 行（窗口与设置页、主题、磁贴布局与命中、手写、缩放与截图、富文本字体、WE 导入窗口、全屏提示、按指针不隐藏） |
| 真实窗口自检 | 设置页 `ALL_SECTIONS_OK`、浮岛 `ISLANDS_OK`、富文本悬浮岛 `RICHTEXT_ISLAND_OK`、自动排列 `AUTOLAYOUT_OK`、导出、缩放、播放队列、网页壁纸全部通过；更新检查因 GitHub 匿名配额限流按设计写 `UPDATE_SKIPPED_RATE_LIMIT` |
| 核心回归 | ProjectLogic 120（含笔迹几何与命中）、RichTextLogic 28、SettingsLogic 全部 PASS、UpdateLogic 32 |
| 构建与跨平台 | 解决方案 0 警告 0 错误；Linux 与 macOS 交叉发布成功；Linux 上 Headless 全绿、正式程序启动 12 秒无异常输出；自检后哈希比对确认用户数据未被改动 |

**尚未验证（不得当作已通过）**

- 真实触控笔的压感、倾斜与线条平滑度：当前按固定线宽绘制（与 2.x 一致，不读取压感），笔尖缩放在数据里保留但界面不产生非 1 的值；需要在真机上确认手感。
- 长按拖动磁贴、多指手势与手写叠加的场景无法在无头环境合成，仍需真机验收。
- 橡皮擦「一次删除所有命中」与 2.x「一次删一条」的差异需要在真机上确认是否更顺手（已记录差异）。

## 关键缺陷：拖动把手拿不到模板，磁贴拖动/缩放与分隔条全部失效（2026-09-13）

这是本轮、也很可能是整个迁移过程中影响最大的一处缺陷。补「手写端到端验证」之后我顺手把拖动类交互也纳入 Headless 检查，结果第一次运行就发现：**编辑模式下按住磁贴顶部拖动条拖动，模型坐标毫无变化**。

**根因**：所有拖动把手都是 `Thumb`（模板化控件）。主题只为滚动条、滑条内部的 Thumb 提供样式（选择器带父级限定），直接放在面板里的裸 `Thumb` **拿不到模板 → 不渲染、也收不到指针**。受影响的交互全部依赖于它：

| 受影响交互 | 说明 |
| --- | --- |
| 磁贴拖动 | 编辑模式下按住顶部移动条移动磁贴 |
| 磁贴八方向缩放 | 四条边与四个角的缩放把手 |
| 分隔条 | 分屏比例调整与拖到端点切换单区 |
| 自由布局组件移动/缩放 | 时钟、天气、噪音组件的移动层与右下角缩放手柄 |
| 分屏/仅时钟模式下的组件移动缩放 | 共用同一套交互层 |

这些交互在 README 与本文档里一直描述为「已完成」，此前的记录也把它们列为「需要人工验收」——正因为没有人真正拖过一次，缺陷一直没被发现。

**修复**：新增 `Controls/ThumbVisuals.Apply`，给每个把手套一个只画背景的最小模板（用绑定跟随把手的背景、边框与圆角），命中区域与外观因此不再依赖主题。落点覆盖全部五处创建位置：磁贴移动条、八个缩放把手、分隔条、组件移动层与缩放手柄。

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| Headless 看板交互（新增，Windows） | `board:drag(snap)+resize(edge)+splitter(ratio→clock→split)`：拖动磁贴后模型坐标变化且落在 48px 网格线上、控件位置与模型一致；拖右边缘后宽度增加并吸附到网格；拖动分隔条改变分屏比例，拖到端点切换成仅时钟，再拖回恢复分屏 |
| Headless（Linux） | 自包含发布后在 WSL 中运行，输出与 Windows 完全一致 |
| 失败前的证据 | 修复前同一检查报「拖动磁贴后模型坐标没有变化」，诊断信息显示按下点命中链是 `Border < SubjectTileControl`（把手被跳过），把手自身 `IsVisible=True/IsHitTestVisible=True` 却不在命中链里——这正是「有模板才可命中」的直接证据 |
| 其余检查 | Headless `HEADLESS_OK`（11 行，含手写、缩放与截图、富文本字体、WE 导入窗口、全屏提示、按指针不隐藏）、真实窗口自检全部通过、核心回归 ProjectLogic 120／RichTextLogic 28／SettingsLogic／UpdateLogic 32 全 PASS、构建 0 警告 0 错误、Linux 与 macOS 交叉发布成功、自检后用户数据哈希不变 |

**尚未验证（不得当作已通过）**

- 修复后的拖动/缩放**手感**（阻尼、吸附的视觉反馈、触屏长按与拖动的冲突）仍需真机验收；本轮验证的是「指针确实驱动了模型」，不是手感。
- 把手模板用绑定跟随外观，缩放控件时圆角/边框的像素对齐需要人工目视确认。

## 关键缺陷：图片附件的编辑态从来没有下发，附件操作全部点不动（2026-09-13）

上一轮修掉「裸 Thumb 没有模板」之后，我顺手把图片附件与自由布局组件也纳入 Headless 验证。附件检查第一次运行就报：**点击图片后没有任何缩放手柄出现**。

**根因**：磁贴只在创建富文本编辑器时同步了编辑态（`editor.IsEditing = _isEditing`），**从来没有给 `AttachmentImageControl` 调用 `SetEditing`**。附件控件因此一直停在 `_isEditing = false`：

| 受影响操作 | 说明 |
| --- | --- |
| 点击选中图片 | `ImageFrame_PointerPressed` 第一行就因非编辑态直接返回，选中框与工具条永远不出现 |
| 八点缩放 | 手柄依赖选中状态，永远不可见 |
| 拖动画面 | 同上，且拖动逻辑同样要求编辑态 |
| 裁切 / 顺逆时针旋转 / 复位 / 删除 | 工具条整体不显示 |

另外，附件的八个缩放手柄同样是**裸 `Thumb`**（上一轮只修了磁贴、分隔条与组件这三处），因此即使选中态正常也点不到——属于同一类缺陷的漏网之处。

**修复**：

1. 磁贴新增 `_attachmentControls` 列表，在 `RebuildEntries` 重建时登记附件控件，创建时立即 `SetEditing(_isEditing)`，并在 `SetEditing`、`SetInkMode` 里与富文本编辑器一起同步编辑态（画笔模式下附件同样退出可编辑，避免书写时误选中图片）。
2. 附件的八个缩放手柄改用 `ThumbVisuals.Apply` 套最小模板。

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| Headless 附件交互（新增，Windows） | `attachment:select+resize(360→390)+move+rotate(90°)`：点击图片后出现可点中的缩放手柄、拖动后框架宽度写回模型、拖动画面后位置偏移写回模型、点工具条顺时针旋转后角度为 90° |
| Headless 自由布局组件（新增） | `freeWidgets:move+resize(400→448)`：拖动时钟组件的移动层后布局记录变化、拖动右下角手柄后宽度变化 |
| 失败前证据 | 修复前同一检查的失败信息里 `state=(False, False)`，即附件控件认为「非编辑、未选中」，而窗口此时 `editing=True`——直接指向编辑态没有下发 |
| Headless（Linux） | 自包含发布后在 WSL 中运行，输出与 Windows 完全一致（12 行） |
| 其余检查 | 真实窗口自检全部通过（设置页、浮岛、富文本悬浮岛、自动排列、导出、缩放、播放队列、网页壁纸）；核心回归 ProjectLogic 120／RichTextLogic 28／SettingsLogic／UpdateLogic 32 全 PASS；构建 0 警告 0 错误；Linux 与 macOS 交叉发布成功；Linux 正式程序启动 12 秒无异常；自检后用户数据哈希不变 |
| 导出页审计 | 逐项核对标题测量与预留、自动排版、越界/重叠校验、选中缩放上限、比例联动、背景图片失败提示、保存守卫，均与 2.x 一致；唯一差异是导出页默认色系取「看板当前色系」而不是「项目记录色系」，这是有意保留（导出应与看到的看板一致） |

**尚未验证（不得当作已通过）**

- 附件的裁切模式（进入裁切后拖动画面改变取景、退出时的边界收敛）没有纳入本轮自动检查，需要人工或后续补检查。
- 真实触屏上「点击图片选中」与「拖动图片」的手势区分（先行距离阈值）需真机确认。

## 自动填充键盘导航与附件裁切验证（2026-09-13）

上一轮把「没人真正点过的交互」都补上了自动检查之后，这一轮补完剩下两项：**自动填充候选浮层的键盘导航**（2.x 用 Win32 `SendInput` 验证过，Avalonia 版本从未验证）与**图片附件的裁切模式**。

**本轮新增验证**

| 检查 | 覆盖内容 |
| --- | --- |
| 自动填充键盘导航 | 编辑模式下聚焦磁贴标题，输入全拼 `yu` → 候选浮层出现且默认选中第一项 → 按下键切换选中 → Esc 只收起浮层、输入文字不变 → 继续输入后回车采纳（标题变成候选名、并套用该学科主题色）→ 再输入 `shu` 后用 Tab 采纳第二个学科 |
| 图片附件裁切 | 进入裁切模式后拖动画面改的是**取景偏移**、图片框位置不变；再点一次裁切退出裁切模式 |

**自检本身的修正**：附件裁切检查第一次通过时其实**证明力不足**——示例图片（4:3）与取景框比例相同，没有多余画面可以移动，偏移恒为 0 也能通过。改成「取景框 1:1 + 图片 4:3」后给出明确的溢出余量，断言收紧为「偏移必须变化」，这才真正验证了裁切拖动。

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| Headless（Windows） | `HEADLESS_OK` 共 13 行，新增两行：`autofillKeys:open+down(True)+esc+enter(语文)+tab(数学)`、`attachment:select+resize(360→390)+move+rotate(90°)+crop` |
| Headless（Linux） | 自包含发布后在 WSL 中运行，输出与 Windows 完全一致 |
| 真实窗口自检 | 设置页 `ALL_SECTIONS_OK`、浮岛 `ISLANDS_OK`、富文本悬浮岛 `RICHTEXT_ISLAND_OK`、自动排列 `AUTOLAYOUT_OK`、导出 `EXPORT_OK … paletteRoundTrip=True`、缩放 `ZOOM_OK`、播放队列、网页壁纸全部通过；更新检查本轮恢复为 `UPDATE_OK` |
| 核心回归 | ProjectLogic 120、RichTextLogic 28、SettingsLogic 全部 PASS、UpdateLogic 32 |
| 构建与跨平台 | 解决方案 0 警告 0 错误；Linux 与 macOS 交叉发布成功；Linux 正式程序启动 12 秒无异常；自检后哈希比对确认用户数据未被改动 |

**尚未验证（不得当作已通过）**

- 中文输入法**组合态**下的补全行为（组合期间按拼音给候选、采纳后被输入法上屏覆盖再补写）无法在无头环境合成，仍需真机 + 真实输入法验收；本轮覆盖的是「已上屏文本」的键盘导航。
- 候选浮层的实际观感（宽度跟随输入框、长候选滚动、浅色主题配色）需要人工目视确认。
- 真实触屏上「点击图片选中」与「拖动图片」的手势区分仍需真机确认。

## 项目与文件菜单验证（2026-09-13）

**本轮新增**：Headless 检查覆盖项目与文件菜单里不依赖系统文件选择器的四条链路——**新建（重置作业，保留科目与布局并清空内容）→ 重命名（对话框改文本）→ 切换项目（含「应用项目外观」对话框）→ 删除（确认后自动切到剩余项目）**，每一步都通过真实的对话框按钮完成，并核对视图模型里的项目集合、当前项目与科目内容。导入与保存 `.pch` 依赖系统文件选择器（无头环境没有选择器），这两条由核心层 `ProjectLogic`（120 项，含打包/导入/迁移）覆盖。

**本轮已验证（可复现证据）**

| 项目 | 结果 |
| --- | --- |
| Headless（Windows） | `HEADLESS_OK` 共 14 行，新增 `projects:new(keepLayout)+rename+switch+delete(count=1)` |
| Headless（Linux） | 自包含发布后在 WSL 中运行，输出与 Windows 完全一致 |
| 既有检查 | 窗口与 11 个设置分类、主题切换、磁贴布局与命中、手写全链路、看板拖动/缩放/分隔条、自由布局组件、图片附件（含裁切）、自动填充键盘导航、四档缩放与截图、富文本字体、WE 导入窗口、全屏提示、按指针不隐藏全部通过 |
| 真实窗口自检与回归 | 设置页/浮岛/富文本悬浮岛/自动排列/导出/缩放/播放队列/网页壁纸全部通过，更新检查 `UPDATE_OK`；ProjectLogic 120、RichTextLogic 28、SettingsLogic 全部 PASS、UpdateLogic 32；构建 0 警告 0 错误；Linux 与 macOS 交叉发布成功 |

**说明**：项目检查会新建（清空作业内容）并删除项目，因此排在所有依赖作业内容的检查之后运行。

## 三端人工验收清单（截至 2026-09-13）

下面把此前各轮记录里散落的「尚未验证」集中成一份可直接照做的清单。**已由自检程序覆盖的项目不再列入**，这里只留必须靠人或真机才能确认的部分。每项都写明操作、判定与所需设备；验收结果请直接填在清单末尾的表格里，并在结论列写明「通过 / 不通过 / 有差异（描述）」。

### A. 输入与文本

| # | 操作 | 判定 | 需要 |
| --- | --- | --- | --- |
| A1 | 在磁贴标题与作业正文里用中文输入法输入（含拼音组合、候选上屏、上屏后删改） | 文字与光标位置正确；组合期间不出候选窗；上屏后不残留半截拼音 | 中文输入法 |
| A2 | 在标题里输入学科全拼（如 `yuwen`）后立即用空格/回车上屏 | 上屏后候选浮层仍给出该学科，且采纳后标题不再是拼音 | 中文输入法 |
| A3 | 切换「字体」为系统字体（如宋体、微软雅黑/思源），再切回内置字体 | 文字随即改变字形；文档重开后仍保留该字体 | 三端各一 |
| A4 | 在同一段里混用内置字体与系统字体，再把光标移到不同字体的片段上 | 光标位置与渲染文字基本重合；若有明显偏移（几个像素以上）请记录 | 三端各一 |
| A5 | 打开一个引用了本机未安装字体的旧 `.pch` | 文字仍可读（退回内置字体），不出现缺字方框；重新保存后字体名仍保留 | 旧项目文件 |

### B. 触控与触控笔

| # | 操作 | 判定 | 需要 |
| --- | --- | --- | --- |
| B1 | 编辑模式下拖动磁贴顶部、拖四条边与四个角 | 能拖动/八方向缩放；吸附到网格；辅助线只在关闭网格吸附时出现 | 触屏 + 笔 |
| B2 | 拖动分屏分隔条到最右、再拖回 | 拖到端点切换为仅时钟；拖回恢复分屏；比例跟手 | 触屏 |
| B3 | 自由布局下移动/缩放时钟、天气、噪音组件 | 拖动跟手；松手后位置保存；重开项目后位置不变 | 触屏 |
| B4 | 开启画笔，在磁贴内书写；越出磁贴边界继续划 | 越界部分不落笔，回到磁贴内不会连出一条跨过空隙的直线 | 触控笔 |
| B5 | 用橡皮擦扫过笔迹 | 整条笔迹被删除；手感是否合适（一次删多条是否比一次删一条更顺手） | 触控笔 |
| B6 | 双指捏合缩放作业板、中键/触摸平移 | 缩放锚点跟手；放大后可平移到四周磁贴 | 触屏 |
| B7 | 点击图片附件、拖动它、拖手柄、裁切、旋转 | 选中框与工具条出现；各部操作立即生效 | 触屏 |
| B8 | 在图片上轻点（不移动）与按住拖动 | 轻点是选中、拖动是移动，两者不误触发 | 触屏 |

### C. 窗口、显示与主题

| # | 操作 | 判定 | 需要 |
| --- | --- | --- | --- |
| C1 | 系统缩放设为 100%/125%/150%/200%，重启程序 | 界面不重叠、不裁切；控制窗按钮仍可点按；看板文字清晰 | 三端各一 |
| C2 | 全屏查看时在看板上滑动 | 控制窗短暂展开「退出全屏」并高亮，随后自动恢复 | 触屏 |
| C3 | 按 Esc（编辑中先结束编辑，再退出全屏） | 行为与说明一致 | 键盘 |
| C4 | 查看模式无操作到自动隐藏时间 | 控制窗与浮岛淡出/飞出；任何操作立刻回来；菜单打开时不隐藏 | 三端各一 |
| C5 | 切换深色/浅色/跟随系统 | 文字对比度正常；浅色下默认白色文字自动变黑；已设置的彩色内容保持原色 | 三端各一 |
| C6 | Windows：打开设置页 | 设置页透出系统材质（Mica/Acrylic），文字仍清晰；Win10 回退成 Acrylic 时也正常 | Windows 10/11 |
| C7 | 控制窗设为左右居中（竖版），进入编辑 | 控制窗按钮竖排；富文本岛与缩放岛等宽并分列上下；画笔栏保持自然尺寸 | 三端各一 |

### D. 音频（噪音检测）

| # | 操作 | 判定 | 需要 |
| --- | --- | --- | --- |
| D1 | 首次开启噪音检测 | macOS 弹出麦克风授权；授权后开始读数；拒绝授权时给出提示而不是静默失败 | 麦克风 |
| D2 | 对着麦克风说话/拍手 | 读数明显上升；越过阈值后播放三声提示音；持续吵闹每 2 秒重复一次 | 麦克风 + 扬声器 |
| D3 | 校准：填写环境实际音量后点「校准到这个音量」 | 偏移显示正确；更换设备后偏移重置 | 声级计（可选） |
| D4 | 最小化窗口（开启「最小化时暂停监测」） | 停止采集与提示音；恢复窗口后继续，校准保留 | Windows |

### E. 背景与媒体

| # | 操作 | 判定 | 需要 |
| --- | --- | --- | --- |
| E1 | 设置图片背景（缩放/拉伸/适应三种模式）+ 毛玻璃 | 三种模式铺满正确；毛玻璃只糊媒体层，颜色层与文字清晰 | 三端各一 |
| E2 | 设置视频背景（MP4） | 静音循环播放；切换队列项正常；Linux 未装 libvlc 时降级为图片背景并给出说明 | 三端各一 |
| E3 | 背景播放队列：定时切换、随机播放、视频结束切换 | 到点切换；随机不连续重复；视频结束切下一项 | 三端各一 |
| E4 | 从 Wallpaper Engine 导入（Windows） | 导入窗口显示预览图与类型标签；导入后不依赖 WE 运行 | 已装 WE 的 Windows |
| E5 | 网页壁纸（Windows） | 网页正常渲染；不能跨源跳转、不弹窗、不下载 | Windows + WebView2 |
| E6 | 控制窗/磁贴毛玻璃观感 | 背衬模糊强度合适；视频背景下延迟可接受 | 三端各一 |

### F. 项目、导出与更新

| # | 操作 | 判定 | 需要 |
| --- | --- | --- | --- |
| F1 | 文件菜单：新建（两种）、重命名、删除、切换项目 | 对话框与结果符合说明；切换项目时外观不同会询问是否应用 | 三端各一（新建/重命名/删除/切换已由自检覆盖逻辑，这里看对话框观感） |
| F2 | 导入/保存 `.pch`、从旧版项目迁移 | 原生文件对话框可用；导入后内容完整（含图片与笔迹） | 三端各一 |
| F3 | 图片导出：拖动排版、选中缩放、越界提示、导出 PNG | 预览与 PNG 一致；越界时按钮禁用并提示；导出不改看板 | 三端各一 |
| F4 | 自动更新（在可丢弃的副本目录里做一次完整覆盖） | 检查到新版 → 下载校验 → 退出替换 → 重启后版本更新、项目与设置保留 | Windows |

### 结果记录

| 平台 | 版本/构建 | 验收人 | 日期 | 未通过项编号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| Windows |  |  |  |  |  |
| Linux（GNOME/KDE，X11 与 Wayland 各一次） |  |  |  |  |  |
| macOS |  |  |  |  |  |

**验收前准备**：三端各准备一份可丢弃的安装目录、一个含中文输入法与触控设备的机器（A/B 组）、一台带麦克风与扬声器的机器（D 组）。Linux 还需在文档写明依赖（X11/Wayland、fontconfig、libICE/libSM，视频背景需发行版 libvlc）。

**构建环境说明**：本轮开发机无法访问 `api.nuget.org`，`dotnet restore` 会写入 `NU1900`（无法获取包漏洞数据）警告并缓存到还原结果里；用 `dotnet restore -p:NuGetAudit=false` 离线还原后重新构建即为 0 警告 0 错误，警告来源是网络而不是代码。
