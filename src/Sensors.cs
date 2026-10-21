using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Pulse.Remote;

public sealed record Reading(string Id, string Group, string Name, string Unit, int Type,
    double? Value, double? Minimum, double? Maximum, double? Average);
public sealed record Snapshot(string Status, string Message, DateTimeOffset Time, long PollTime, Reading[] Readings)
{
    public int RefreshSeconds { get; init; } = 2;
}

// HWiNFO SM2's published packed binary format. Reads only; no driver or hardware access.
public static class SensorParser
{
    public static Snapshot Parse(byte[] bytes)
    {
        uint U(int p) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p, 4));
        double? D(int p)
        {
            var v = BitConverter.ToDouble(bytes, p);
            return double.IsFinite(v) ? v : null;
        }
        string S(int p, int length)
        {
            var end = Array.IndexOf(bytes, (byte)0, p, length);
            return Encoding.Latin1.GetString(bytes, p, end < 0 ? length : end - p).Trim();
        }
        if (bytes.Length < 44 || U(0) != 0x53695748 || U(4) != 2)
            throw new InvalidDataException("HWiNFO sharing is stopped or its format is unsupported.");
        var so = U(20); var ss = U(24); var sc = U(28);
        var ro = U(32); var rs = U(36); var rc = U(40);
        if (ss < 264 || rs < 316 || sc > 4096 || rc > 100000 ||
            so < 44 || ro < 44 || (ulong)so + (ulong)ss * sc > (ulong)bytes.Length ||
            (ulong)ro + (ulong)rs * rc > (ulong)bytes.Length)
            throw new InvalidDataException("HWiNFO shared memory contains an invalid section.");
        var groups = new (uint Id, uint Instance, string Name)[sc];
        for (uint i = 0; i < sc; i++)
        {
            int p = checked((int)(so + i * ss));
            string name = S(p + 136, 128);
            groups[i] = (U(p), U(p + 4), name.Length > 0 ? name : S(p + 8, 128));
        }
        var readings = new List<Reading>();
        for (uint i = 0; i < rc; i++)
        {
            int p = checked((int)(ro + i * rs));
            var index = U(p + 4);
            if (index >= sc) throw new InvalidDataException("Invalid HWiNFO sensor index.");
            var g = groups[index];
            string name = S(p + 140, 128);
            readings.Add(new($"{g.Id:X8}-{g.Instance:X8}-{U(p + 8):X8}", g.Name,
                name.Length > 0 ? name : S(p + 12, 128), S(p + 268, 16), (int)U(p),
                D(p + 284), D(p + 292), D(p + 300), D(p + 308)));
        }
        return new("live", "Connected to HWiNFO", DateTimeOffset.UtcNow,
            BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(12, 8)), readings.ToArray());
    }
}

public sealed class SensorService : BackgroundService
{
    private Snapshot current = new("waiting", "Open HWiNFO in Sensors-only mode and enable Shared Memory Support in its general settings.", DateTimeOffset.UtcNow, 0, []);
    public Snapshot Current => Volatile.Read(ref current);
    private long lastPoll = long.MinValue;
    private long changedAt = Environment.TickCount64;

    private static byte[] CopySharedData()
    {
        using var mutex = Mutex.OpenExisting(@"Global\HWiNFO_SM2_MUTEX");
        bool held = false;
        try
        {
            try { held = mutex.WaitOne(100); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("HWiNFO is busy updating shared memory.");
            using var map = MemoryMappedFile.OpenExisting(@"Global\HWiNFO_SENS_SM2", MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if (view.Capacity < 44 || view.Capacity > 64 * 1024 * 1024)
                throw new InvalidDataException("Unexpected HWiNFO shared memory size.");
            var sensorEnd = (ulong)view.ReadUInt32(20) + (ulong)view.ReadUInt32(24) * view.ReadUInt32(28);
            var readingEnd = (ulong)view.ReadUInt32(32) + (ulong)view.ReadUInt32(36) * view.ReadUInt32(40);
            var length = Math.Max(44UL, Math.Max(sensorEnd, readingEnd));
            if (length > (ulong)view.Capacity || length > 64 * 1024 * 1024)
                throw new InvalidDataException("Invalid HWiNFO shared-memory bounds.");
            var data = new byte[(int)length];
            view.ReadArray(0, data, 0, data.Length);
            return data;
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    public Snapshot ReadOnce()
    {
        try
        {
            var data = CopySharedData();
            var result = SensorParser.Parse(data);
            if (result.PollTime != lastPoll)
            {
                lastPoll = result.PollTime;
                changedAt = Environment.TickCount64;
            }
            if (Environment.TickCount64 - changedAt > 15000)
                result = result with { Status = "stale", Message = "HWiNFO readings have stopped updating. Check Sensors and Shared Memory Support on your PC.", Readings = [] };
            Volatile.Write(ref current, result);
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException or ArgumentException or WaitHandleCannotBeOpenedException)
        {
            string message = ex is InvalidDataException ? ex.Message : ex is UnauthorizedAccessException
                ? "Windows denied access to HWiNFO sharing. Run HWiNFO and Pulse in the same Windows account."
                : "Waiting for HWiNFO. Open Sensors-only mode and enable Shared Memory Support in Settings → General / User Interface. Free sharing switches off after 12 hours.";
            Volatile.Write(ref current, new("waiting", message, DateTimeOffset.UtcNow, 0, []));
            lastPoll = long.MinValue;
        }
        return Current;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ReadOnce();
            await Task.Delay(TimeSpan.FromSeconds(Program.Settings.RefreshSeconds), stoppingToken);
        }
    }
}
