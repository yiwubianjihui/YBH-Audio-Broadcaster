namespace YbhAudio.Engine;

/// <summary>
/// 虚拟设备探测：优先找本项目自研驱动创建的端点，
/// 找不到时退回系统里已装的虚拟声卡（Voicemeeter / VB-CABLE），保证软件先能用。
/// </summary>
public static class VirtualDeviceAdvisor
{
    public const string YbhSpeakerKeyword = "YBH Virtual Speaker";
    public const string YbhMicKeyword = "YBH Virtual Mic";

    private static readonly string[] SpeakerFallbacks =
    {
        "Voicemeeter Input", "CABLE Input", "VoiceMeeter Input",
    };

    private static readonly string[] MicFallbacks =
    {
        "Voicemeeter Out B1", "CABLE Output", "VoiceMeeter Output",
    };

    public sealed record Result(
        AudioDeviceInfo? VirtualSpeaker,
        AudioDeviceInfo? VirtualMic,
        IReadOnlyList<AudioDeviceInfo> AllSpeakers,
        string Hint);

    public static Result Detect()
    {
        var renders = AudioDeviceCatalog.RenderDevices();
        var captures = AudioDeviceCatalog.CaptureDevices();

        var ybhSpeaker = renders.FirstOrDefault(d =>
            d.Name.Contains(YbhSpeakerKeyword, StringComparison.OrdinalIgnoreCase));
        var ybhMic = captures.FirstOrDefault(d =>
            d.Name.Contains(YbhMicKeyword, StringComparison.OrdinalIgnoreCase));

        if (ybhSpeaker != null && ybhMic != null)
        {
            return new Result(ybhSpeaker, ybhMic, renders,
                "已检测到 YBH 虚拟声卡驱动，可直接使用。");
        }

        var fallbackSpeaker = renders.FirstOrDefault(d =>
            SpeakerFallbacks.Any(k => d.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));
        var fallbackMic = captures.FirstOrDefault(d =>
            MicFallbacks.Any(k => d.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));

        if (fallbackSpeaker != null)
        {
            var micName = fallbackMic?.Name ?? "（未找到配对的虚拟麦克风）";
            return new Result(fallbackSpeaker, fallbackMic, renders,
                $"未安装 YBH 虚拟声卡，暂时借用系统已有虚拟声卡：{fallbackSpeaker.Name} → {micName}。");
        }

        return new Result(null, null, renders,
            "没有找到可用的虚拟麦克风。请安装 YBH 虚拟声卡驱动，或先安装 VB-CABLE。");
    }
}
