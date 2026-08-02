using System;
using System.Text;

namespace Engineering_Watch.BLE;

// ============================================================
// BLE プロトコル (docs/PROTOCOL.md と一致させること)
// ============================================================

public static class GattUuids
{
    public static readonly Guid WatchService = new("0000ff00-0000-1000-8000-00805f9b34fb");
    public static readonly Guid WatchFaceConfig = new("0000ff01-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Control = new("0000ff02-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Status = new("0000ff03-0000-1000-8000-00805f9b34fb");
    public static readonly Guid WatchData = new("0000ff04-0000-1000-8000-00805f9b34fb");
    public static readonly Guid NotifService = new("0000ff10-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Notification = new("0000ff11-0000-1000-8000-00805f9b34fb");
}

public static class FaceFiles
{
    public const byte Bg = 1;     // bg.png
    public const byte Hour = 2;   // hour.png
    public const byte Min = 3;    // min.png
    public const byte Sec = 4;    // sec.png

    public const string BgName = "bg.png";
    public const string HourName = "hour.png";
    public const string MinName = "min.png";
    public const string SecName = "sec.png";
}

// チャンクフレーム:
//   BEGIN [0x01][fileId][total:u32 LE][name...]
//   DATA  [0x02][fileId][offset:u32 LE][data...]
//   END   [0x03][fileId][crc32:u32 LE]
//   APPLY [0x04][0][len:u32 LE][dynamic.json]
public static class Wire
{
    public static byte[] BeginFrame(byte fileId, int total, string name)
    {
        var nameB = Encoding.UTF8.GetBytes(name);
        var buf = new byte[6 + nameB.Length];
        buf[0] = 0x01; buf[1] = fileId;
        WriteU32(buf, 2, (uint)total);
        Buffer.BlockCopy(nameB, 0, buf, 6, nameB.Length);
        return buf;
    }

    public static byte[] DataFrame(byte fileId, int offset, byte[] payload, int count)
    {
        var buf = new byte[6 + count];
        buf[0] = 0x02; buf[1] = fileId;
        WriteU32(buf, 2, (uint)offset);
        Buffer.BlockCopy(payload, 0, buf, 6, count);
        return buf;
    }

    public static byte[] EndFrame(byte fileId, uint crc)
    {
        var buf = new byte[6];
        buf[0] = 0x03; buf[1] = fileId;
        WriteU32(buf, 2, crc);
        return buf;
    }

    public static byte[] ApplyFrame(string json)
    {
        var jsonB = Encoding.UTF8.GetBytes(json);
        var buf = new byte[6 + jsonB.Length];
        buf[0] = 0x04; buf[1] = 0;
        WriteU32(buf, 2, (uint)jsonB.Length);
        Buffer.BlockCopy(jsonB, 0, buf, 6, jsonB.Length);
        return buf;
    }

    private static void WriteU32(byte[] buf, int off, uint v)
    {
        buf[off] = (byte)(v & 0xFF);
        buf[off + 1] = (byte)((v >> 8) & 0xFF);
        buf[off + 2] = (byte)((v >> 16) & 0xFF);
        buf[off + 3] = (byte)((v >> 24) & 0xFF);
    }
}

// zlib互換 CRC32 (ファームウェアと同じ)
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Update(uint crc, byte[] data, int offset, int count)
    {
        crc ^= 0xFFFFFFFFu;
        for (int i = 0; i < count; i++)
            crc = Table[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    public static uint Compute(byte[] data, int count) => Update(0, data, 0, count);
}
