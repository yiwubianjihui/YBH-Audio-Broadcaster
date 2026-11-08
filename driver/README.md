# YBH 虚拟声卡驱动（v0.1 骨架）

> ⚠️ **状态说明**：本目录是驱动的 v0.1 工程骨架与核心组件，**尚未在本仓库的 CI 或开发机上编译验证**（开发机未安装 WDK / MSVC 工具链）。它给出了完整的端点契约、环形缓冲实现、INF 模板与构建/安装脚本；适配 MSVAD 示例的 adapter/miniport 需要在装有 WDK 的机器上补齐。请把这里当作「可执行的实现路线」，而不是已经签好名的成品驱动。

## 为什么必须是内核驱动

Windows 没有用户态 API 可以「创建一个麦克风端点」。`IMMDevice` / `IAudioClient` 都是客户端接口，不能注册设备。虚拟声卡只有一条路：写一个 PortCls/WDM 音频驱动，让 AudioEndpointBuilder 把它枚举成真实端点。

## 设计

一对端点，一条环形缓冲直通：

```
应用播放 ──▶ YBH Virtual Speaker (render) ──┐
                                            ├─▶ 环形缓冲（无锁 SPSC）
会议录音 ◀── YBH Virtual Mic (capture)   ◀──┘
```

- **格式**：48 kHz / 16 bit / 2 ch PCM（与 VB-CABLE 默认一致）
- **缓冲**：200 ms，2 的幂字节数
- **不需要私有 IOCTL**：用户态引擎和普通应用一样，直接 `WasapiOut` 到 Speaker 端点、`WasapiCapture` 从 Mic 端点读。驱动只负责搬运。
- **多客户端**：render 侧由 PortCls 混音，capture 侧广播给所有打开的客户端。

## 与用户态引擎的契约

| 项 | 值 |
|---|---|
| Render 端点名 | `YBH Virtual Speaker` |
| Capture 端点名 | `YBH Virtual Mic` |
| 采样率 | 48000 Hz |
| 位深 | 16 bit |
| 声道 | 2 |
| 缓冲深度 | 200 ms |
| 端点角色 | Speaker = render，Mic = capture |

引擎里的 `VirtualDeviceAdvisor` 就是按这两个名字做前缀匹配的（`YbhSpeakerKeyword` / `YbhMicKeyword`）。

## 实现路线（基于微软 MSVAD `simple` 示例）

1. 从 [Windows-driver-samples/audio/sysvad](https://github.com/microsoft/Windows-driver-samples/tree/main/audio/sysvad) 或 MSVAD `simple` 复制工程。
2. 把 `CMiniportWaveCyclic` 的 `NewStream` 改成：
   - render 流：`CopyTo` 里调用 `YbhRingBuffer::Write`
   - capture 流：`CopyFrom` 里调用 `YbhRingBuffer::Read`
3. 用 `ringbuffer.h` 替换示例里的缓冲区（或保留示例的 `CSimpleBuffer`，二者等价）。
4. 修改端点名（`adapter.cpp` 里的 `KSDATARANGE` / 属性），改成 `YBH Virtual Speaker` / `YBH Virtual Mic`。
5. 按 `YbhVirtualAudio.inf` 的模板改 INF 的硬件 ID 与端点注册。
6. 构建、签名、安装（见下）。

关键文件（需从示例补全）：

```
adapter.cpp/.h       CAdapter、设备枚举、接口注册
miniport.cpp/.h      CMiniportWaveCyclic / CMiniportTopology
common.cpp/.h        端口/引脚描述符
ringbuffer.h         ← 本仓库已提供
YbhVirtualAudio.inf  ← 本仓库提供模板
```

## 构建

前置：

- Visual Studio 2022，勾选「使用 C++ 的桌面开发」
- Windows Driver Kit（与 SDK 版本匹配，例如 10.0.26100）
- 管理员权限

```powershell
# 在 WDK 的 Developer Command Prompt 里
msbuild YbhVirtualAudio\YbhVirtualAudio.vcxproj /p:Configuration=Release /p:Platform=x64
```

产物：`YbhVirtualAudio.sys`、`YbhVirtualAudio.inf`、`YbhVirtualAudio.cat`。

## 签名与安装（测试）

```powershell
# 管理员 PowerShell
.\install-test.ps1
```

脚本会：

1. 生成自签名测试证书（`makecert` / `New-SelfSignedCertificate`）
2. 签名 `.sys` / `.cat`
3. `bcdedit /set testsigning on`
4. `pnputil /add-driver YbhVirtualAudio.inf /install`
5. 提示重启

重启后，声音设置里会出现：

- 播放：`YBH Virtual Speaker`
- 录音：`YBH Virtual Mic`

## 已知限制

1. **必须测试签名或购买微软生产签名**。测试签名要关闭 Secure Boot、`bcdedit /set testsigning on`、重启；部分反作弊（EAC / BattlEye）会拒绝启动。
2. 生产签名需要 EV 证书 + 微软 attestation（Partner Center），个人项目成本较高。
3. 固定 48 kHz / 16 bit / stereo，暂不支持运行时切换。
4. 没有多总线 / 混音矩阵（Voicemeeter 的复杂功能不在本项目范围内）。
5. 未做电源管理与低功耗优化。

## 卸载

```powershell
pnputil /delete-driver oemXX.inf /uninstall
bcdedit /set testsigning off
```

重启后恢复。
