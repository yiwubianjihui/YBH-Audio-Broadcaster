using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using YbhAudio.Engine;

namespace YbhAudio.App;

public partial class MainWindow : Window
{
    private readonly AudioEngine _engine = new();
    private readonly DispatcherTimer _meterTimer;

    private VirtualDeviceAdvisor.Result? _advice;
    private MMDevice? _virtualDevice;
    private MMDevice? _speakerDevice;
    private MMDevice? _micDevice;

    private string? _filePath;
    private bool _systemOn;
    private bool _micOn;

    public MainWindow()
    {
        InitializeComponent();

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _meterTimer.Tick += (_, _) => UpdateMeters();
        _meterTimer.Start();

        _engine.SystemBroadcast.Failed += (_, msg) => ReportError("系统音频", msg);
        _engine.FileToVirtual.Failed += (_, msg) => ReportError("文件播放", msg);
        _engine.MicMonitor.Failed += (_, msg) => ReportError("麦克风监听", msg);
        _engine.FileToVirtual.FileEnded += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            StopFile();
            Log("文件播放结束");
        }));

        Loaded += (_, _) =>
        {
            RefreshDevices();
            Log("就绪。先选设备，再点对应功能的按钮。");
        };
        Closing += (_, _) =>
        {
            _meterTimer.Stop();
            _engine.StopAll();
            _virtualDevice?.Dispose();
            _speakerDevice?.Dispose();
            _micDevice?.Dispose();
        };
        Closed += (_, _) => Application.Current.Shutdown();
    }

    // ---------------- 设备 ----------------

    private void RefreshDevices()
    {
        _advice = VirtualDeviceAdvisor.Detect();
        var renders = _advice.AllSpeakers;
        var captures = AudioDeviceCatalog.CaptureDevices();

        VirtualSpeakerBox.ItemsSource = renders;
        VirtualSpeakerBox.SelectedItem = _advice.VirtualSpeaker ?? renders.FirstOrDefault();

        SpeakerBox.ItemsSource = renders;
        SpeakerBox.SelectedItem = renders.FirstOrDefault(d => d.IsDefault) ?? renders.FirstOrDefault();

        MicBox.ItemsSource = captures;
        MicBox.SelectedItem = _advice.VirtualMic ?? captures.FirstOrDefault();

        HintText.Text = _advice.Hint;
        Log($"设备已刷新：{renders.Count} 个播放端点，{captures.Count} 个录音端点");
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_systemOn || _micOn || _engine.FileToVirtual.IsRunning)
        {
            Log("请先停止正在运行的功能，再刷新设备。");
            return;
        }
        ReleaseDevices();
        RefreshDevices();
    }

    private void ReleaseDevices()
    {
        _virtualDevice?.Dispose();
        _virtualDevice = null;
        _speakerDevice?.Dispose();
        _speakerDevice = null;
        _micDevice?.Dispose();
        _micDevice = null;
    }

    private MMDevice? ResolveVirtual()
    {
        if (_virtualDevice != null)
        {
            return _virtualDevice;
        }
        _virtualDevice = AudioDeviceCatalog.Open((VirtualSpeakerBox.SelectedItem as AudioDeviceInfo)?.Id);
        return _virtualDevice;
    }

    private MMDevice? ResolveSpeaker()
    {
        if (_speakerDevice != null)
        {
            return _speakerDevice;
        }
        _speakerDevice = AudioDeviceCatalog.Open((SpeakerBox.SelectedItem as AudioDeviceInfo)?.Id);
        return _speakerDevice;
    }

    private MMDevice? ResolveMic()
    {
        if (_micDevice != null)
        {
            return _micDevice;
        }
        _micDevice = AudioDeviceCatalog.Open((MicBox.SelectedItem as AudioDeviceInfo)?.Id);
        return _micDevice;
    }

    // ---------------- ① 系统音频 ----------------

    private void OnSystemToggle(object sender, RoutedEventArgs e)
    {
        if (_systemOn)
        {
            _engine.SystemBroadcast.Stop();
            _systemOn = false;
            SystemButton.Content = "开始广播";
            SystemState.Text = "未开启";
            SystemState.Foreground = Brushes.Gray;
            Log("① 已停止：电脑音频不再送入虚拟麦克风");
            return;
        }

        var source = ResolveSpeaker();
        var target = ResolveVirtual();
        if (source == null || target == null)
        {
            Log("请先在顶部选择「目标虚拟麦克风」和「扬声器」。");
            return;
        }

        try
        {
            _engine.SystemBroadcast.StartSystemLoopback(source, target);
            _systemOn = true;
            SystemButton.Content = "停止广播";
            SystemState.Text = "正在广播";
            SystemState.Foreground = Brushes.SeaGreen;
            Log($"① 开始：{source.FriendlyName} 的声音 → {target.FriendlyName}");
        }
        catch (Exception ex)
        {
            Log("① 启动失败：" + ex.Message);
        }
    }

    private void OnSystemVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
        => _engine.SystemBroadcast.Volume = (float)e.NewValue;

    // ---------------- ② 音频文件 ----------------

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要广播的音频文件",
            Filter = "音频文件|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac;*.aiff;*.aif|所有文件|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            _filePath = dialog.FileName;
            FilePathBox.Text = Path.GetFileName(_filePath);
            Log("已选择：" + _filePath);
        }
    }

    private void OnPlayFile(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            OnBrowse(sender, e);
            if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
            {
                return;
            }
        }

        var target = ResolveVirtual();
        if (target == null)
        {
            Log("请先选择目标虚拟麦克风。");
            return;
        }

        try
        {
            var loop = LoopBox.IsChecked == true;
            _engine.FileToVirtual.StartFile(_filePath, target, loop);

            var speaker = ResolveSpeaker();
            if (MonitorFileBox.IsChecked == true && speaker != null)
            {
                _engine.FileMonitor.StartFile(_filePath, speaker, loop);
            }

            FileState.Text = "正在播放";
            FileState.Foreground = Brushes.SeaGreen;
            Log("② 开始播放 → " + target.FriendlyName + "：" + Path.GetFileName(_filePath));
        }
        catch (Exception ex)
        {
            Log("② 播放失败：" + ex.Message);
        }
    }

    private void OnStopFile(object sender, RoutedEventArgs e)
    {
        StopFile();
        Log("② 已停止播放");
    }

    private void StopFile()
    {
        _engine.FileToVirtual.Stop();
        _engine.FileMonitor.Stop();
        FileState.Text = "未播放";
        FileState.Foreground = Brushes.Gray;
    }

    private void OnFileVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _engine.FileToVirtual.Volume = (float)e.NewValue;
        _engine.FileMonitor.Volume = (float)e.NewValue;
    }

    // ---------------- ③ 监听 ----------------

    private void OnMicToggle(object sender, RoutedEventArgs e)
    {
        if (_micOn)
        {
            _engine.MicMonitor.Stop();
            _micOn = false;
            MicButton.Content = "开始监听";
            MicState.Text = "未开启";
            MicState.Foreground = Brushes.Gray;
            Log("③ 监听已停止");
            return;
        }

        var mic = ResolveMic();
        var speaker = ResolveSpeaker();
        if (mic == null || speaker == null)
        {
            Log("请先选择监听对象和扬声器。");
            return;
        }

        try
        {
            _engine.MicMonitor.StartMicrophone(mic, speaker);
            _micOn = true;
            MicButton.Content = "停止监听";
            MicState.Text = "监听中";
            MicState.Foreground = Brushes.SeaGreen;
            Log($"③ 监听：{mic.FriendlyName} → {speaker.FriendlyName}");
        }
        catch (Exception ex)
        {
            Log("③ 监听失败：" + ex.Message);
        }
    }

    private void OnMicVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
        => _engine.MicMonitor.Volume = (float)e.NewValue;

    // ---------------- 其它 ----------------

    private void OnStopAll(object sender, RoutedEventArgs e)
    {
        _engine.StopAll();
        _systemOn = false;
        _micOn = false;
        SystemButton.Content = "开始广播";
        SystemState.Text = "未开启";
        SystemState.Foreground = Brushes.Gray;
        MicButton.Content = "开始监听";
        MicState.Text = "未开启";
        MicState.Foreground = Brushes.Gray;
        StopFile();
        Log("已全部停止");
    }

    private void OnHelp(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "使用方法\n\n" +
            "1. 顶部「目标虚拟麦克风」选 YBH Virtual Speaker（或系统里已有的虚拟声卡）。\n" +
            "   「扬声器」选你实际在听的设备。\n\n" +
            "2. 在会议 / 直播 / 游戏软件里，把麦克风选成 YBH Virtual Mic\n" +
            "   （如果用的是回退方案，就选对应的 Voicemeeter Out B1 / CABLE Output）。\n\n" +
            "3. 点「① 开始广播」→ 电脑里正在播放的声音进入虚拟麦克风。\n" +
            "   「② 播放」→ 音频文件进入同一个虚拟麦克风。\n" +
            "   「③ 开始监听」→ 用扬声器听虚拟麦克风的内容，或返听物理麦克风。\n\n" +
            "说明\n" +
            "• 本工具直接捕获所选扬声器的 WASAPI 回环，不需要修改系统默认播放设备。\n" +
            "• 建议戴耳机，否则对方的声音可能被再次采集形成回声。\n" +
            "• 会议软件的降噪 / 自动增益可能把音乐当噪声吃掉，必要时关掉。\n" +
            "• 退出时会自动停止所有音频管道。",
            "YBH 音频广播助手 — 使用说明");
    }

    private void UpdateMeters()
    {
        SystemLevel.Value = _engine.SystemBroadcast.Level;
        FileLevel.Value = _engine.FileToVirtual.Level;
        MicLevel.Value = _engine.MicMonitor.Level;
    }

    private void ReportError(string scope, string message)
        => Dispatcher.BeginInvoke(new Action(() => Log($"{scope} 出错：{message}")));

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        LogBox.ScrollToEnd();
    }
}
