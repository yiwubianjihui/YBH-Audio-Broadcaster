using NAudio.CoreAudioApi;

namespace YbhAudio.Engine;

/// <summary>枚举 Windows 的播放 / 录音端点。</summary>
public static class AudioDeviceCatalog
{
    public static IReadOnlyList<AudioDeviceInfo> RenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = SafeDefaultId(enumerator, DataFlow.Render);
        return Enumerate(enumerator, DataFlow.Render, defaultId);
    }

    public static IReadOnlyList<AudioDeviceInfo> CaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = SafeDefaultId(enumerator, DataFlow.Capture);
        return Enumerate(enumerator, DataFlow.Capture, defaultId);
    }

    public static string? DefaultRenderId()
    {
        using var enumerator = new MMDeviceEnumerator();
        return SafeDefaultId(enumerator, DataFlow.Render);
    }

    /// <summary>按 Id 打开设备（找不到时返回 null）。</summary>
    public static MMDevice? Open(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDevice(deviceId);
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeDefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID;
        }
        catch
        {
            return null;
        }
    }

    private static List<AudioDeviceInfo> Enumerate(MMDeviceEnumerator enumerator, DataFlow flow, string? defaultId)
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                var id = device.ID;
                var name = device.FriendlyName;
                list.Add(new AudioDeviceInfo(id, name, id == defaultId));
            }
        }
        catch
        {
            // 音频服务未启动时枚举会抛异常，返回空列表由界面提示
        }
        return list;
    }

    /// <summary>获取端点的混音格式采样率；失败时退回 48000。</summary>
    public static int MixSampleRate(MMDevice device)
    {
        try
        {
            return device.AudioClient.MixFormat.SampleRate;
        }
        catch
        {
            return 48000;
        }
    }
}
