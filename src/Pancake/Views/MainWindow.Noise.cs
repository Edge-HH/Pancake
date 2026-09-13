using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Pancake.Controls;
using Pancake.Platforms.Abstraction;
using Pancake.Platforms.Abstraction.Models;
using Pancake.Platforms.Abstraction.Services;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 噪音检测：麦克风采集、音量换算、吵闹报警与设置页。
/// 采集由平台服务提供（三端共用 SoundFlow 实现），音量换算与报警判断都在核心层。
/// </summary>
public sealed partial class MainWindow
{
    private readonly NoiseLevelMeter _noiseMeter = new();
    private readonly NoiseAlertGate _noiseAlertGate = new();
    private readonly Stopwatch _noiseAlertClock = Stopwatch.StartNew();
    private readonly INoiseCaptureService _noiseCapture = PlatformServices.NoiseCapture;
    private readonly INoiseAlertPlayback _noiseAlert = PlatformServices.NoiseAlert;
    private bool _noiseSuspended;
    private bool _refreshingDevices;
    private TextBlock? _microphoneStatusText;
    private TextBlock? _noiseCalibrationLabel;
    private TextBlock? _noiseThresholdLabel;
    private TextBlock? _noiseIntervalLabel;
    private ComboBox? _microphoneDeviceCombo;

    private bool ShouldSuspendNoise =>
        Settings.PauseNoiseWhenMinimized && WindowState == WindowState.Minimized;

    /// <summary>开始（或重新开始）噪音检测；暂停条件满足时保持停止。</summary>
    private void StartNoiseMonitoring()
    {
        if (!_noiseCapture.IsSupported) return;
        if (ShouldSuspendNoise)
        {
            StopNoiseMonitoring("窗口最小化，已暂停检测");
            return;
        }

        _noiseMeter.IntervalSeconds = Math.Clamp(Settings.NoiseIntervalSeconds, 0.1, 2);
        _noiseMeter.CalibrationOffsetDb = Settings.MicrophoneCalibrationDb;
        _noiseMeter.Reset();
        // 恢复检测时重新接受采集回调（停止过程中会临时挂起）。
        _noiseSuspended = false;
        _noiseCapture.SampleRate = Settings.MicrophoneSampleRate is >= 8000 and <= 96000
            ? Settings.MicrophoneSampleRate
            : 16000;
        _noiseAlertGate.Reset();
        _noiseAlert.Volume = Math.Clamp(Settings.NoiseAlertVolume, 0, 1);
        SetMicrophoneStatus("正在启动输入设备…", isWarning: false);
        _noiseCapture.Start(Settings.MicrophoneDeviceId);
    }

    private void StopNoiseMonitoring(string? status = null)
    {
        // 先挂起回调，再停止设备：设备停止可能仍投递最后一帧采样。
        _noiseSuspended = true;
        _noiseMeter.Reset();
        _noiseCapture.Stop();
        if (status is not null) SetMicrophoneStatus(status, isWarning: false);
    }

    /// <summary>采集回调在音频线程触发；音量换算在这里完成，界面更新回到 UI 线程。</summary>
    private void NoiseSamplesAvailable(object? sender, AudioSamplesEventArgs e)
    {
        double? level = _noiseMeter.Add(e.Samples, e.SampleRate, e.Channels);
        // 报警按单个采集块立即判断，不等待用户设置的显示统计窗口。
        double fast = _noiseMeter.FastLevel(e.Samples);
        Dispatcher.UIThread.Post(() =>
        {
            ProcessNoiseAlert(fast);
            if (level is double value) UpdateNoiseDisplay(value);
        });
    }

    private void NoiseCaptureFailed(object? sender, string message) =>
        Dispatcher.UIThread.Post(() => SetMicrophoneStatus($"输入设备不可用：{message}", isWarning: true));

    /// <summary>看板上的噪音读数与状态文字。</summary>
    private void UpdateNoiseDisplay(double level)
    {
        if (_noiseSuspended || ShouldSuspendNoise) return;
        bool noisy = level >= Settings.NoiseThresholdDb;
        string state = noisy
            ? "吵闹"
            : level < Math.Min(45, Settings.NoiseThresholdDb) ? "安静" : "适中";
        this.FindControl<TextBlock>("NoiseText")!.Text = $"{level:0} dB · {state}";
        SetMicrophoneStatus($"麦克风工作正常 · {level:0.0} dB", isWarning: noisy, prefix: noisy ? "吵闹：" : null);
    }

    /// <summary>报警判定：新一轮超阈值立即提醒，持续吵闹每两秒一次，并屏蔽扬声器回授。</summary>
    private void ProcessNoiseAlert(double level)
    {
        if (_noiseSuspended || ShouldSuspendNoise) return;
        if (_noiseAlertGate.ShouldPlay(
                level,
                Settings.NoiseThresholdDb,
                Settings.NoiseAlertEnabled,
                _noiseAlertClock.Elapsed.TotalSeconds))
        {
            PlayNoiseAlert();
        }
    }

    private void PlayNoiseAlert()
    {
        if (!_noiseAlert.IsSupported) return;
        _noiseAlert.Volume = Math.Clamp(Settings.NoiseAlertVolume, 0, 1);
        _noiseAlert.Play(NoiseAlertTone.CreatePcm(), NoiseAlertTone.SampleRate);
    }

    private void SetMicrophoneStatus(string message, bool isWarning, string? prefix = null)
    {
        if (_microphoneStatusText is null) return;
        _microphoneStatusText.Text = message;
        _microphoneStatusText.Foreground = new SolidColorBrush(
            (isWarning ? BoardColor.FromRgb(251, 191, 36) : BoardTheme.TextColor).ToColor());
        _ = prefix;
    }

    /// <summary>
    /// 刷新输入设备列表。已选择但失联的设备会保留在列表里并标注不可用，
    /// 避免程序悄悄切换到另一支未经校准的麦克风。
    /// </summary>
    private void RefreshMicrophoneDevices()
    {
        if (_microphoneDeviceCombo is null) return;
        _refreshingDevices = true;
        try
        {
            List<AudioInputDevice> devices = [.. _noiseCapture.GetDevices()];
            if (!devices.Any(device => device.Id == Settings.MicrophoneDeviceId))
            {
                devices.Add(new AudioInputDevice(Settings.MicrophoneDeviceId, "已选择的设备不可用（请选择其他输入设备）"));
            }

            _microphoneDeviceCombo.ItemsSource = devices;
            _microphoneDeviceCombo.SelectedItem = devices.First(device => device.Id == Settings.MicrophoneDeviceId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            SetMicrophoneStatus($"无法枚举输入设备：{ex.Message}", isWarning: true);
        }
        finally
        {
            _refreshingDevices = false;
        }
    }

    /// <summary>组件 · 噪音检测设置页。</summary>
    private void BuildNoiseSettings()
    {
        StackPanel panel = new() { Spacing = 18 };
        if (!_noiseCapture.IsSupported)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "当前平台没有可用的音频后端，噪音检测不可用。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(BoardColor.FromRgb(251, 191, 36).ToColor())
            });
            SettingsContent.Children.Add(CreateCard("麦克风噪音检测", panel));
            return;
        }

        TextBlock intervalLabel = new() { Text = $"检测间隔 · {Settings.NoiseIntervalSeconds:0.0} 秒" };
        _noiseIntervalLabel = intervalLabel;
        Slider interval = CreateSlider(0.1, 2, 0.1, Settings.NoiseIntervalSeconds, snapToTick: true);
        interval.ValueChanged += (_, _) =>
        {
            intervalLabel.Text = $"检测间隔 · {interval.Value:0.0} 秒";
            Settings.NoiseIntervalSeconds = Math.Round(interval.Value, 1);
            _noiseMeter.IntervalSeconds = Settings.NoiseIntervalSeconds;
            ScheduleSave();
        };

        ComboBox devices = new() { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
        _microphoneDeviceCombo = devices;
        devices.SelectionChanged += (_, _) =>
        {
            if (!_isLoaded || _refreshingDevices || devices.SelectedItem is not AudioInputDevice device) return;
            Settings.MicrophoneDeviceId = device.Id;
            // 更换设备后旧偏移不再成立，必须重新校准。
            Settings.MicrophoneCalibrationDb = 0;
            if (_noiseCalibrationLabel is not null) _noiseCalibrationLabel.Text = "校准偏移 · 0 dB（请重新校准）";
            StartNoiseMonitoring();
            ScheduleSave();
        };
        RefreshMicrophoneDevices();
        Button refreshDevices = CreateActionButton("刷新输入设备", () =>
        {
            RefreshMicrophoneDevices();
            StartNoiseMonitoring();
        });

        TextBlock thresholdLabel = new() { Text = $"吵闹阈值 · {Settings.NoiseThresholdDb:0} dB" };
        _noiseThresholdLabel = thresholdLabel;
        Slider threshold = CreateSlider(20, 120, 1, Settings.NoiseThresholdDb, snapToTick: true);
        threshold.ValueChanged += (_, _) =>
        {
            thresholdLabel.Text = $"吵闹阈值 · {threshold.Value:0} dB";
            Settings.NoiseThresholdDb = threshold.Value;
            ScheduleSave();
        };

        ToggleSwitch alertToggle = CreateToggle(Settings.NoiseAlertEnabled);
        alertToggle.IsCheckedChanged += (_, _) =>
        {
            Settings.NoiseAlertEnabled = alertToggle.IsChecked == true;
            _noiseAlertGate.Reset();
            ScheduleSave();
        };
        Button preview = CreateActionButton("试听", () =>
        {
            _noiseAlertGate.Reset();
            PlayNoiseAlert();
        });
        StackPanel alertRow = new() { Orientation = Orientation.Horizontal, Spacing = 16 };
        alertRow.Children.Add(alertToggle);
        alertRow.Children.Add(preview);

        Slider volume = CreateSlider(0, 100, 1, Settings.NoiseAlertVolume * 100, snapToTick: true);
        TextBlock volumeLabel = new() { Text = $"提示音音量 · {Settings.NoiseAlertVolume * 100:0}%" };
        volume.ValueChanged += (_, _) =>
        {
            volumeLabel.Text = $"提示音音量 · {volume.Value:0}%";
            Settings.NoiseAlertVolume = Math.Clamp(volume.Value / 100, 0, 1);
            _noiseAlert.Volume = Settings.NoiseAlertVolume;
            ScheduleSave();
        };

        TextBlock calibrationLabel = new()
        {
            Text = $"校准偏移 · {Settings.MicrophoneCalibrationDb:+0.0;-0.0;0} dB"
        };
        _noiseCalibrationLabel = calibrationLabel;
        NumericUpDown target = new()
        {
            Minimum = 20,
            Maximum = 120,
            Increment = 1,
            Value = (decimal)Math.Clamp(Settings.CalibrationTargetDb, 20, 120),
            Width = 180
        };
        Button calibrate = CreateActionButton("校准到这个音量", () =>
        {
            if (!_noiseMeter.TryCalibrate((double)(target.Value ?? 40)))
            {
                SetMicrophoneStatus("请填写 20–120 dB 的环境音量，并等待麦克风产生有效读数后再校准。", isWarning: true);
                return;
            }

            Settings.CalibrationTargetDb = (double)(target.Value ?? 40);
            Settings.MicrophoneCalibrationDb = _noiseMeter.CalibrationOffsetDb;
            calibrationLabel.Text = $"校准偏移 · {Settings.MicrophoneCalibrationDb:+0.0;-0.0;0} dB";
            ScheduleSave();
        });

        ToggleSwitch pauseWhenMinimized = CreateToggle(Settings.PauseNoiseWhenMinimized);
        pauseWhenMinimized.IsCheckedChanged += (_, _) =>
        {
            Settings.PauseNoiseWhenMinimized = pauseWhenMinimized.IsChecked == true;
            StartNoiseMonitoring();
            ScheduleSave();
        };

        TextBlock status = new()
        {
            Text = "正在等待麦克风启动",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor())
        };
        _microphoneStatusText = status;

        panel.Children.Add(intervalLabel);
        panel.Children.Add(interval);
        panel.Children.Add(new TextBlock { Text = "输入设备", FontSize = 14, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(devices);
        panel.Children.Add(refreshDevices);
        panel.Children.Add(thresholdLabel);
        panel.Children.Add(threshold);
        panel.Children.Add(alertRow);
        panel.Children.Add(volumeLabel);
        panel.Children.Add(volume);
        panel.Children.Add(new TextBlock
        {
            Text = "先选择当前环境的实际音量，再点击校准；更换麦克风后请重新校准。",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520
        });
        panel.Children.Add(target);
        panel.Children.Add(calibrate);
        panel.Children.Add(calibrationLabel);
        panel.Children.Add(pauseWhenMinimized);
        panel.Children.Add(status);

        SettingsContent.Children.Add(CreateCard(
            "麦克风噪音检测",
            new TextBlock
            {
                Text = "只计算实时音量，不录音、不保存音频；系统拒绝麦克风权限时会停止检测。",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
                Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.7 }
            },
            panel));
    }
}
