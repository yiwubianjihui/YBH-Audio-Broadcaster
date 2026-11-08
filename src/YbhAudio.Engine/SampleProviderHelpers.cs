using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace YbhAudio.Engine;

internal static class SampleProviderHelpers
{
    /// <summary>把任意声道数统一成双声道，供 WASAPI 输出使用。</summary>
    public static ISampleProvider EnsureStereo(ISampleProvider source)
    {
        var channels = source.WaveFormat.Channels;
        if (channels == 2)
        {
            return source;
        }
        if (channels == 1)
        {
            return new MonoToStereoSampleProvider(source);
        }

        // 多声道：只取前两路
        var multiplex = new MultiplexingSampleProvider(new[] { source }, 2);
        multiplex.ConnectInputToOutput(0, 0);
        multiplex.ConnectInputToOutput(0, 1);
        return multiplex;
    }
}

/// <summary>让 AudioFileReader 支持循环播放（NAudio 没有内置实现）。</summary>
internal sealed class LoopingAudioFileSampleProvider : ISampleProvider
{
    private readonly AudioFileReader _reader;
    private readonly bool _loop;

    public LoopingAudioFileSampleProvider(AudioFileReader reader, bool loop)
    {
        _reader = reader;
        _loop = loop;
    }

    public WaveFormat WaveFormat => _reader.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _reader.Read(buffer, offset, count);
        if (read == 0 && _loop && _reader.Length > 0)
        {
            try
            {
                _reader.Position = 0;
                read = _reader.Read(buffer, offset, count);
            }
            catch
            {
                // 文件被占用 / 已释放时按播放结束处理
            }
        }
        return read;
    }
}
