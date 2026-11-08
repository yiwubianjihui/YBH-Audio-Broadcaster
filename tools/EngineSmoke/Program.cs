using System.Media;
using System.Text;
using NAudio.CoreAudioApi;
using YbhAudio.Engine;

try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch
{
}

var logPath = Path.Combine(AppContext.BaseDirectory, "smoke.log");
File.WriteAllText(logPath, string.Empty, Encoding.UTF8);

void Log(string line)
{
    Console.WriteLine(line);
    try
    {
        File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
    }
    catch
    {
    }
}

var pass = 0;
var fail = 0;

void Check(string name, Func<(bool Ok, string Detail)> action)
{
    try
    {
        var (ok, detail) = action();
        Log($"[{(ok ? "PASS" : "FAIL")}] {name} — {detail}");
        if (ok) { pass++; } else { fail++; }
    }
    catch (Exception ex)
    {
        Log($"[FAIL] {name} — {ex.GetType().Name}: {ex.Message}");
        fail++;
    }
}

var renders = AudioDeviceCatalog.RenderDevices();
var captures = AudioDeviceCatalog.CaptureDevices();
Log($"播放端点 {renders.Count} 个，录音端点 {captures.Count} 个");

var advice = VirtualDeviceAdvisor.Detect();
Log("设备建议：" + advice.Hint);

var targetInfo = advice.VirtualSpeaker;
var speakerInfo = renders.FirstOrDefault(d => d.IsDefault) ?? renders.FirstOrDefault();

var target = AudioDeviceCatalog.Open(targetInfo?.Id);
var speaker = AudioDeviceCatalog.Open(speakerInfo?.Id);

Check("打开虚拟输出设备", () => (target != null, targetInfo?.Name ?? "无"));
Check("打开扬声器", () => (speaker != null, speakerInfo?.Name ?? "无"));

if (target == null || speaker == null)
{
    Log("缺少必要设备，终止。");
    return 2;
}

var engine = new AudioEngine();
var wav = @"C:\Windows\Media\chimes.wav";
if (!File.Exists(wav))
{
    wav = @"C:\Windows\Media\Windows Ding.wav";
}

Check("文件 → 虚拟麦克风", () =>
{
    engine.FileToVirtual.StartFile(wav, target, false);
    Thread.Sleep(900);
    var level = engine.FileToVirtual.Level;
    var running = engine.FileToVirtual.IsRunning;
    engine.FileToVirtual.Stop();
    return (running && level > 0.001f, $"running={running} peak={level:F3}");
});

Check("文件 → 扬声器（本地试听）", () =>
{
    engine.FileMonitor.StartFile(wav, speaker, false);
    Thread.Sleep(900);
    var level = engine.FileMonitor.Level;
    engine.FileMonitor.Stop();
    return (level > 0.001f, $"peak={level:F3}");
});

Check("系统回环 → 虚拟麦克风", () =>
{
    engine.SystemBroadcast.StartSystemLoopback(speaker, target);
    Thread.Sleep(300);
    using (var player = new SoundPlayer(wav))
    {
        player.Play();
    }
    Thread.Sleep(1300);
    var level = engine.SystemBroadcast.Level;
    var running = engine.SystemBroadcast.IsRunning;
    engine.SystemBroadcast.Stop();
    return (running, $"running={running} peak={level:F3}");
});

Check("监听虚拟麦克风 → 扬声器", () =>
{
    var micInfo = advice.VirtualMic ?? captures.FirstOrDefault();
    var mic = AudioDeviceCatalog.Open(micInfo?.Id);
    if (mic == null)
    {
        return (false, "没有虚拟麦克风端点");
    }
    engine.VirtualMicMonitor.StartMicrophone(mic, speaker);
    Thread.Sleep(700);
    var running = engine.VirtualMicMonitor.IsRunning;
    engine.VirtualMicMonitor.Stop();
    return (running, $"running={running} mic={micInfo?.Name}");
});

engine.Dispose();
Log($"\n通过 {pass}，失败 {fail}");
return fail == 0 ? 0 : 1;
