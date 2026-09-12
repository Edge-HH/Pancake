using Microsoft.UI.Xaml;
using Pancake.Models;
using Pancake.Services;

namespace Pancake;

public sealed partial class MainWindow
{
    // 仅时钟模式的整屏笔迹：编辑在内存列表里完成，保存时写回当前项目，和磁贴内容同一套节奏。
    private List<InkStrokeData> _clockInk = [];
    private List<InkStrokeData>? _clockInkEditSnapshot;
    private List<InkStrokeData>? _attachedClockInk;

    /// <summary>切换项目或首次读盘后，用当前项目保存的整屏笔迹替换内存内容。</summary>
    private void LoadClockInk()
    {
        _clockInk = AppDataStore.RestoreInk(CurrentProject?.ClockInkStrokes ?? []);
        _attachedClockInk = null;
        RefreshClockInk();
    }

    /// <summary>
    /// 仅时钟模式下把整屏笔迹挂到画布上；离开该模式时收起画布，笔迹仍留在项目里，
    /// 下次回到仅时钟模式原样恢复。画笔打开时整块屏幕都可以书写。
    /// </summary>
    private void RefreshClockInk()
    {
        if (ClockInkLayer is null) return;
        bool clockOnly = _settings.LayoutMode == "Clock";
        ClockInkLayer.Visibility = clockOnly ? Visibility.Visible : Visibility.Collapsed;
        if (!ReferenceEquals(_clockInk, _attachedClockInk))
        {
            _attachedClockInk = _clockInk;
            ClockInkLayer.Attach(_clockInk);
        }
        ClockInkLayer.SetInkMode(clockOnly && _isEditing && GlobalPenButton.IsChecked == true, _inkSettings);
    }

    /// <summary>进入编辑前记录整屏笔迹快照，供“放弃修改”回滚。</summary>
    private void SnapshotClockInkForEditing() =>
        _clockInkEditSnapshot = _clockInk.Select(stroke => stroke.Clone()).ToList();

    /// <summary>放弃本轮编辑：把整屏笔迹恢复成进入编辑前的样子。</summary>
    private void RestoreClockInkSnapshot()
    {
        if (_clockInkEditSnapshot is null) return;
        _clockInk = _clockInkEditSnapshot.Select(stroke => stroke.Clone()).ToList();
        _clockInkEditSnapshot = null;
        _attachedClockInk = null;
        RefreshClockInk();
    }

    /// <summary>保存时把整屏笔迹写回项目，与磁贴笔迹一起进入项目文件和 .pch 作业包。</summary>
    private void CaptureClockInk()
    {
        if (CurrentProject is { } project) project.ClockInkStrokes = AppDataStore.CaptureInk(_clockInk);
    }
}
