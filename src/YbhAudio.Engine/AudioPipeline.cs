using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace YbhAudio.Engine;

/// <summary>
/// 一条独立的音频管道：源 → 重采样 → 音量 → 电平 → 指定输出设备。
/// 每条管道只做一件事，互不耦合，避免 Voicemeeter 那种复杂总线。
/// </summary>
public sealed class AudioPipeline : IDisposable
{
    private readonly object _gate = new();
    private IWaveIn? _capture;
    private BufferedWaveProvider? _captureBuffer;
    private AudioFileReader? _fileReader;
    private WasapiOut? _output;
    private MeteringSampleProvider? _meter;
    private VolumeSampleProvider? _volume;
    private System.Timers.Timer? _fileWatch;
    private bool _loop;
    private float _volumeValue = 1f;
    private volatile float _level;

    public float Level => _level;

    public bool IsRunning { get; private set; }

    public string? CurrentFile { get; private set; }

    public float Volume
    {
        get => _volumeValue;
        set
        {
            _volumeValue = Math.Clamp(value, 0f, 2f);
            if (_volume != null)
            {
                _volume.Volume = _volumeValue;
            }
        }
    }

    /// <summary>后台线程触发的错误，界面需要切回 UI 线程。</summary>
    public event EventHandler<string>? Failed;

    /// <summary>文件播放到结尾（非循环）。</summary>
    public event EventHandler? FileEnded;

    public void StartSystemLoopback(MMDevice sourceDevice, MMDevice outputDevice)
        => StartCapture(sourceDevice, outputDevice, loopback: true);

    public void StartMicrophone(MMDevice sourceDevice, MMDevice outputDevice)
        => StartCapture(sourceDevice, outputDevice, loopback: false);

    private void StartCapture(MMDevice sourceDevice, MMDevice outputDevice, bool loopback)
    {
        lock (_gate)
        {
            StopCore();

            IWaveIn capture = loopback
                ? new WasapiLoopbackCapture(sourceDevice)
                : new WasapiCapture(sourceDevice);

            var buffer = new BufferedWaveProvider(capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(600),
            };

            capture.DataAvailable += (_, e) =>
            {
                try
                {
                    buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                }
                catch
                {
                    // 忽略采集缓冲区异常
                }
            };
            capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null)
                {
                    Failed?.Invoke(this, e.Exception.Message);
                }
            };

            var chain = BuildChain(buffer.ToSampleProvider(), outputDevice);
            _capture = capture;
            _captureBuffer = buffer;
            _output = chain.Output;
            _volume = chain.Volume;
            _meter = chain.Meter;

            capture.StartRecording();
            chain.Output.Play();
            IsRunning = true;
        }
    }

    public void StartFile(string path, MMDevice outputDevice, bool loop)
    {
        lock (_gate)
        {
            StopCore();

            var reader = new AudioFileReader(path);
            var source = new LoopingAudioFileSampleProvider(reader, loop);
            var chain = BuildChain(source, outputDevice);

            _fileReader = reader;
            _loop = loop;
            CurrentFile = path;
            _output = chain.Output;
            _volume = chain.Volume;
            _meter = chain.Meter;

            chain.Output.Play();
            IsRunning = true;

            _fileWatch = new System.Timers.Timer(250) { AutoReset = true };
            _fileWatch.Elapsed += (_, _) =>
            {
                try
                {
                    if (!_loop && reader.Length > 0 && reader.Position >= reader.Length)
                    {
                        _fileWatch?.Stop();
                        FileEnded?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch
                {
                    // 播放器已释放时忽略
                }
            };
            _fileWatch.Start();
        }
    }

    private (WasapiOut Output, VolumeSampleProvider Volume, MeteringSampleProvider Meter) BuildChain(
        ISampleProvider source, MMDevice outputDevice)
    {
        var stereo = SampleProviderHelpers.EnsureStereo(source);
        var targetRate = AudioDeviceCatalog.MixSampleRate(outputDevice);

        ISampleProvider resampled = stereo.WaveFormat.SampleRate == targetRate
            ? stereo
            : new WdlResamplingSampleProvider(stereo, targetRate);

        var volume = new VolumeSampleProvider(resampled) { Volume = _volumeValue };
        var meter = new MeteringSampleProvider(volume);
        meter.StreamVolume += (_, e) =>
        {
            var peak = 0f;
            foreach (var value in e.MaxSampleValues)
            {
                if (value > peak)
                {
                    peak = value;
                }
            }
            _level = peak;
        };

        var wave = new SampleToWaveProvider16(meter);
        var output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 80);
        output.Init(wave);
        return (output, volume, meter);
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    private void StopCore()
    {
        try
        {
            _fileWatch?.Stop();
            _fileWatch?.Dispose();
        }
        catch
        {
        }
        _fileWatch = null;

        try
        {
            _output?.Stop();
        }
        catch
        {
        }
        try
        {
            _output?.Dispose();
        }
        catch
        {
        }
        _output = null;

        try
        {
            _capture?.StopRecording();
        }
        catch
        {
        }
        try
        {
            _capture?.Dispose();
        }
        catch
        {
        }
        _capture = null;

        try
        {
            _fileReader?.Dispose();
        }
        catch
        {
        }
        _fileReader = null;

        _captureBuffer = null;
        _volume = null;
        _meter = null;
        _level = 0f;
        _loop = false;
        IsRunning = false;
        CurrentFile = null;
    }

    public void Dispose() => Stop();
}
