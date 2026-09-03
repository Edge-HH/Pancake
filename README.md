# Pancake

> 面向教室大屏与触控设备的 Windows 班级作业看板。

![Platform](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows11&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![WinUI](https://img.shields.io/badge/UI-WinUI%203-0078D4)

Pancake 将时间、日期、天气、教室噪音和各科作业集中在一块适合远距离阅读的深色看板中。它使用 WinUI 3 构建，支持鼠标、触控笔与触摸操作，并为教室大屏提供默认全屏展示。

## ✨ 功能

- 大屏展示：实时显示时钟、日期、天气、噪音水平与今日作业。
- 看板编辑：新增、重命名、移动、缩放和删除科目磁贴。
- 网格布局：磁贴默认吸附到 48 px 网格，也可在编辑时关闭吸附。
- 作业内容：在磁贴内直接编辑，支持加粗、斜体、下划线、文字颜色和高光，并可添加附件。
- 手写标注：支持多种颜色、粗细和橡皮擦，笔迹完整显示在磁贴上并自动保存。
- 磁贴主题：每个科目可独立更换主题色。
- 编辑保护：进入编辑前创建快照，可完成编辑或放弃本轮修改。
- 噪音检测：通过麦克风实时估算环境音量，支持采样率和校准偏移设置。
- 天气信息：按小米天气接口文档读取当前温度和天气状态，内置 2566 个可按名称搜索的地区。
- 本地数据：布局、富文本、笔迹和全部设置自动保存到可执行文件旁的 `data` 目录。
- 自动更新：启动时检查 GitHub Release，下载可安装资产后由用户确认启动安装。
- 显示设置：支持深色、浅色和跟随系统主题，以及全屏和窗口模式。
- 自适应布局：窄窗口下自动切换为上下排列。

## 🖥️ 使用方式

程序默认以全屏展示模式启动。底部浮动工具栏提供编辑看板、设置和全屏切换入口。

编辑模式下可以：

1. 拖动磁贴顶部来移动磁贴。
2. 拖动四条边或四个角来调整磁贴大小。
3. 直接修改科目名和作业文字，或添加附件与手写标注。
4. 使用底部按钮完成编辑，或放弃本轮全部修改。

按 `Esc` 会先结束当前编辑；未在编辑时按下则退出全屏。

## 🚀 构建与运行

### 环境要求

- Windows 10 1809（版本 17763）或更高版本
- x64 设备
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022（推荐），并安装“使用 .NET 的 Windows 应用 SDK”相关工作负载
- Windows App SDK Runtime 1.8 与 .NET 8 Desktop Runtime（直接运行框架依赖产物时需要）

### 命令行

```powershell
git clone https://github.com/MEMZ-Edge01/Pancake.git
cd Pancake
dotnet restore .\Pancake.slnx
dotnet build .\Pancake.slnx -c Release -p:Platform=x64
dotnet run --project .\src\Pancake\Pancake.csproj -c Release -p:Platform=x64
```

也可以使用 Visual Studio 打开 `Pancake.slnx`，选择 `x64` 后启动 `Pancake` 项目。

### 启动参数

| 参数 | 作用 |
| --- | --- |
| `--windowed` | 使用普通窗口启动，而不是默认全屏 |
| `--view=editor` | 启动后直接进入看板编辑模式 |
| `--view=ink` | 启动后直接进入可手写的编辑模式 |
| `--view=settings` | 启动后直接打开设置页 |

例如：

```powershell
dotnet run --project .\src\Pancake\Pancake.csproj -- --windowed --view=editor
```

## 🌤️ 天气配置

在设置页点击“选择地区”，输入地区名称搜索并从结果中选择。实现依据社区维护的 [XiaomiWeather.md](https://github.com/huanghui0906/API/blob/master/XiaomiWeather.md) 及其配套地区数据库；该接口不是小米公开承诺稳定性的正式开放 API，若服务端变更可能需要同步适配。

## 🔒 隐私说明

- 麦克风数据只用于实时计算音量，不录音，也不保存音频。
- 添加附件时当前只记录文件路径，不会上传文件。
- 数据默认写入程序所在目录的 `data/pancake.json`，移动整个程序目录即可一并迁移。
- 如果把程序放在无写入权限的目录（例如受保护的系统安装目录），自动保存会在设置页报告失败。

## 📁 项目结构

```text
Pancake/
├─ src/Pancake/
│  ├─ Controls/       # 科目磁贴、拖动、缩放与手写交互
│  ├─ Models/         # 看板、作业、附件与笔迹模型
│  ├─ Services/       # 数据保存、噪音、天气、地区搜索与更新
│  ├─ Themes/         # WinUI 主题资源
│  ├─ ViewModels/     # 主看板状态与编辑快照
│  └─ MainWindow.*    # 主界面与窗口交互
├─ tests/             # 交互契约检查脚本
├─ design-qa.md       # 设计验收记录
└─ Pancake.slnx
```

## 🧪 验证

运行静态交互契约检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\verify-interaction-contract.ps1
```

该脚本检查触控交互、八方向缩放、网格吸附、手写工具栏和全屏退出提示等关键实现是否存在。它不能代替真实触控设备上的手势、比例和命中区域验收。

## 🚧 当前限制

- “导出备份包”和“导入备份包”目前是界面占位功能，尚未实际读写数据。
- 当前 GitHub 仓库必须先发布带 `.exe`、`.msix` 或 `.msixbundle` 资产的 Release，自动更新才有可安装内容。
- 小米天气来自第三方整理的非正式接口文档，服务端兼容性不由本项目控制。
- 噪音数值是基于 PCM 电平和校准偏移的估算值，不等同于经过认证的声级计读数。
- 原生触屏手势和视觉比例仍需在目标教室设备上完成最终验收。

## 🤝 参与开发

欢迎通过 Issue 报告问题或提出建议。提交代码前，请至少完成 Release 构建和交互契约检查，并说明是否在真实触控设备上验证过相关操作。

## 📄 许可证

本项目采用 [GNU General Public License v3.0](LICENSE) 开源许可证。你可以在遵守 GPL-3.0 条款的前提下使用、修改和分发本项目；分发衍生作品时需以 GPL-3.0 提供对应源代码。
