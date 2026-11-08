#pragma once
#include <ntddk.h>

//
// 单生产者 / 单消费者无锁环形缓冲（字节流）。
//   - 生产者：render 线程（CMiniportWaveCyclicStream::CopyTo）
//   - 消费者：capture 线程（CMiniportWaveCyclicStream::CopyFrom）
//
// 计数器使用 64 位，避免长时间运行后 32 位回绕把取模算错。
//
class YbhRingBuffer
{
public:
    void Init(_Out_writes_bytes_(bytes) PUCHAR memory, _In_ ULONG bytes)
    {
        _data = memory;
        _size = bytes;
        _write = 0;
        _read = 0;
    }

    ULONG AvailableRead() const
    {
        return static_cast<ULONG>(_write - _read);
    }

    ULONG AvailableWrite() const
    {
        return _size - AvailableRead();
    }

    ULONG Write(_In_reads_bytes_(bytes) const void* source, _In_ ULONG bytes)
    {
        ULONG space = AvailableWrite();
        if (bytes > space)
        {
            bytes = space;
        }
        if (bytes == 0)
        {
            return 0;
        }

        ULONG offset = static_cast<ULONG>(_write % _size);
        ULONG first = (bytes < (_size - offset)) ? bytes : (_size - offset);

        RtlCopyMemory(_data + offset, source, first);
        if (bytes > first)
        {
            RtlCopyMemory(_data, static_cast<const UCHAR*>(source) + first, bytes - first);
        }

        KeMemoryBarrier();
        InterlockedExchangeAdd64(&_write, static_cast<LONGLONG>(bytes));
        return bytes;
    }

    ULONG Read(_Out_writes_bytes_(bytes) void* destination, _In_ ULONG bytes)
    {
        ULONG available = AvailableRead();
        if (bytes > available)
        {
            bytes = available;
        }
        if (bytes == 0)
        {
            return 0;
        }

        ULONG offset = static_cast<ULONG>(_read % _size);
        ULONG first = (bytes < (_size - offset)) ? bytes : (_size - offset);

        RtlCopyMemory(destination, _data + offset, first);
        if (bytes > first)
        {
            RtlCopyMemory(static_cast<UCHAR*>(destination) + first, _data, bytes - first);
        }

        KeMemoryBarrier();
        InterlockedExchangeAdd64(&_read, static_cast<LONGLONG>(bytes));
        return bytes;
    }

    // 没有数据可读时由 capture 侧补静音，避免爆音。
    void FillSilence(_Out_writes_bytes_(bytes) void* destination, _In_ ULONG bytes)
    {
        RtlZeroMemory(destination, bytes);
    }

    void Reset()
    {
        _write = 0;
        _read = 0;
    }

private:
    PUCHAR _data = nullptr;
    ULONG _size = 0;
    volatile LONGLONG _write = 0;
    volatile LONGLONG _read = 0;
};
