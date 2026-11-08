namespace YbhAudio.Engine;

/// <summary>
/// 引擎持有所有独立的音频管道。每条管道只做一件事：
/// ① 系统声音 → 虚拟麦克风
/// ② 音频文件 → 虚拟麦克风
/// ③ 音频文件 → 扬声器（本地试听）
/// ④ 物理麦克风 → 扬声器（返听）
/// ⑤ 虚拟麦克风内容 → 扬声器（监听实际送出的声音）
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public AudioPipeline SystemBroadcast { get; } = new();
    public AudioPipeline FileToVirtual { get; } = new();
    public AudioPipeline FileMonitor { get; } = new();
    public AudioPipeline MicMonitor { get; } = new();
    public AudioPipeline VirtualMicMonitor { get; } = new();

    public IReadOnlyList<AudioPipeline> All { get; }

    public AudioEngine()
    {
        All = new[]
        {
            SystemBroadcast, FileToVirtual, FileMonitor, MicMonitor, VirtualMicMonitor,
        };
    }

    public void StopAll()
    {
        foreach (var pipeline in All)
        {
            try
            {
                pipeline.Stop();
            }
            catch
            {
                // 单条管道停止失败不影响其它管道
            }
        }
    }

    public void Dispose() => StopAll();
}
