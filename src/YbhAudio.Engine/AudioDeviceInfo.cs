namespace YbhAudio.Engine;

/// <summary>一个 Windows 音频端点的展示信息。</summary>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault)
{
    public override string ToString() => IsDefault ? Name + "（当前默认）" : Name;
}
