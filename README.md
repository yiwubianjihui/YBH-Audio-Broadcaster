# YBH 音频广播助手

把**电脑正在播放的声音**或**音频文件**送进一个虚拟麦克风，并用扬声器监听麦克风输入。
自带音频引擎，不依赖 Voicemeeter，不需要装 Python。

> 目标：换一台 Windows 电脑，拷一个 exe 就能用；虚拟声卡由本项目自己的驱动提供。

---

## 当前状态

| 模块 | 状态 | 说明 |
|---|---|---|
| 音频引擎（`src/YbhAudio.Engine`） | ✅ 可用 | WASAPI 回环捕获、文件播放、混音、重采样、电平、五条独立管道 |
| 桌面界面（`src/YbhAudio.App`） | ✅ 可用 | WPF 中文界面，单文件发布 |
| 虚拟声卡驱动（`driver/`） | 🚧 v0.1 源码 | 需 WDK 编译 + 测试签名，见 `driver/README.md` |
| 回退方案 | ✅ 可用 | 未装驱动时自动借用系统已有的虚拟声卡（Voicemeeter / VB-CABLE） |

引擎已在本机跑通冒烟测试：**6/6 通过**（文件→虚拟麦克风、文件→扬声器、系统回环→虚拟麦克风、监听虚拟麦克风→扬声器）。

---

## 三个功能

1. **电脑音频 → 虚拟麦克风**：捕获所选扬声器的 WASAPI 回环，送进虚拟麦克风。
2. **音频文件 → 虚拟麦克风**：支持 WAV / MP3 / M4A / AAC / WMA / FLAC / AIFF，可循环、可调音量。
3. **用扬声器监听麦克风输入**：监听虚拟麦克风实际送出的内容，或返听物理麦克风。

### 和 Voicemeeter 的区别

- **不改系统默认播放设备**：直接对指定扬声器做 loopback，零系统设置改动。
- **没有调音台**：三条独立管道，一个开关一件事。
- **引擎与设备分离**：设备层可替换——自研驱动没装时自动退回系统已有虚拟声卡。

---

## 快速开始

### 1. 构建（需要 .NET 8 SDK）

```powershell
dotnet build src\YbhAudio.App\YbhAudio.App.csproj -c Release
dotnet run --project src\YbhAudio.App
```

### 2. 发布成单文件 exe

```powershell
dotnet publish src\YbhAudio.App\YbhAudio.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

产物：`publish\YbhAudio.exe`（自包含，目标机不需要装 .NET）。

### 3. 使用

1. 顶部「目标虚拟麦克风」选 `YBH Virtual Speaker`（装了驱动）或已有的虚拟声卡。
2. 「扬声器」选你实际在听的设备。
3. 会议 / 直播软件里把麦克风选成 `YBH Virtual Mic`（或对应的 `Voicemeeter Out B1` / `CABLE Output`）。
4. 点「① 开始广播」/「② 播放」/「③ 开始监听」。

---

## 架构

```
┌─────────────────┐   WASAPI loopback   ┌──────────────────┐
│ 扬声器（真实）   │ ──────────────────▶ │ 引擎：重采样/音量 │
└─────────────────┘                     │  /电平           │
┌─────────────────┐   AudioFileReader   │                  │
│ 音频文件         │ ──────────────────▶ │  五条独立管道     │
└─────────────────┘                     └────────┬─────────┘
┌─────────────────┐   WasapiCapture              │
│ 麦克风 / 虚拟麦克风│ ──────────────────────────▶ │
└─────────────────┘                              ▼
                                        ┌──────────────────┐
                                        │ 虚拟扬声器端点    │
                                        │ （YBH 驱动 /     │
                                        │   Voicemeeter）  │
                                        └────────┬─────────┘
                                                 ▼
                                        YBH Virtual Mic（虚拟麦克风）
```

五条管道（`AudioEngine`）：

| 管道 | 源 | 目标 |
|---|---|---|
| `SystemBroadcast` | 所选扬声器 loopback | 虚拟扬声器 |
| `FileToVirtual` | 音频文件 | 虚拟扬声器 |
| `FileMonitor` | 同一个音频文件 | 真实扬声器 |
| `MicMonitor` | 物理麦克风 | 真实扬声器 |
| `VirtualMicMonitor` | 虚拟麦克风端点 | 真实扬声器 |

---

## 目录结构

```
src/YbhAudio.Engine/     # 音频引擎（NAudio / WASAPI）
src/YbhAudio.App/        # WPF 界面
driver/                  # 虚拟声卡驱动（WDK 工程 + 安装脚本）
tools/EngineSmoke/       # 引擎冒烟测试（6 项）
docs/                    # 设计说明
```

---

## 技术要点

- **WASAPI 回环**：捕获的是指定扬声器的共享模式混音流，不需要 Stereo Mix，也不需要改默认设备。
- **格式统一**：源先转双声道，再重采样到目标端点的混音采样率（`WdlResamplingSampleProvider`），最后转 16bit 输出。
- **设备占用**：若 Voicemeeter 正在运行并独占扬声器，会报 `AUDCLNT_E_DEVICE_IN_USE`；结束它即可，本项目的驱动不会独占设备。
- **DPI**：清单刻意只用 `system` DPI。`PerMonitorV2` 在无头 / 远程桌面会话里会触发 `0xC0000409`。

---

## 驱动

虚拟声卡必须是内核驱动（Windows 没有用户态创建音频端点的 API）。
`driver/` 下是基于微软 MSVAD 结构的 v0.1 工程，提供 `YBH Virtual Speaker` → `YBH Virtual Mic` 的环形缓冲直通。

编译与安装见 [driver/README.md](driver/README.md)。**需要 WDK + 测试签名（或 EV 证书），未在本仓库的 CI 中编译。**

---

## 许可证

[MIT](LICENSE)
