using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using TwinCAT.Ads;
using TwinCAT.PlcOpen;

namespace AdsUtilities;

public class AdsSystemClient : AdsClientBase
{
    public AdsSystemClient(ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {

    }

    public async Task RebootAsync(uint delaySeconds = 0, CancellationToken cancel = default) 
    {

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var wcRes = await adsConnection.WriteControlAsync(
            AdsState.Shutdown,
            1,
            BitConverter.GetBytes(delaySeconds),
            cancel);

        adsConnection.Disconnect();

        wcRes.ThrowOnError();
    }

    public async Task ShutdownAsync(uint delaySeconds = 0, CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var wcRes = await adsConnection.WriteControlAsync(
            AdsState.Shutdown,
            0,
            BitConverter.GetBytes(delaySeconds),
            cancel);

        adsConnection.Disconnect();

        wcRes.ThrowOnError();
    }

    public async Task SetRegEntryAsync(
        string subKey, 
        string valueName, 
        RegEditTypeCode registryTypeCode, 
        byte[] value, 
        CancellationToken cancel)
    {
        WriteRequestHelper setRegRequest = new WriteRequestHelper()
            .AddStringUTF8(subKey)
            .AddStringUTF8(valueName)
            .Add(new byte[] { 0, (byte)registryTypeCode, 0, 0, 0 })
            .Add(value);

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var rwResult = await adsConnection.WriteAsync(
            (uint)AdsIndexGroups.SysServRegHklm,
            0,
            setRegRequest.GetBytes(), 
            cancel);

        adsConnection.Disconnect();

        rwResult.ThrowOnError();
    }

    public async Task<byte[]> QueryRegEntryAsync(
        string subKey, 
        string valueName, 
        uint byteSize, 
        CancellationToken cancel = default)
    {
        WriteRequestHelper readRegRequest = new WriteRequestHelper()
            .AddStringUTF8(subKey)
            .AddStringUTF8(valueName);

        byte[] readBuffer = new byte[byteSize];

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var rwResult = await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServRegHklm,
            0, 
            readBuffer, 
            readRegRequest.GetBytes(), 
            cancel);

        adsConnection.Disconnect();

        rwResult.ThrowOnError();
        return readBuffer;
    }

    public async Task ChangeNetIdOnWindowsAsync(string netIdNew, bool rebootNow = false, CancellationToken cancel = default)
    {
        string[] partsNetId = netIdNew.Split('.');
        byte[] bytesNetId = new byte[partsNetId.Length];

        for (int i = 0; i < partsNetId.Length; i++)
        {
            if (!byte.TryParse(partsNetId[i], out bytesNetId[i]))
            {
                _logger?.LogError("NetId contains invalid value.");
                return;
            }
        }

        await SetRegEntryAsync(
            @"Software\Beckhoff\TwinCAT3\System",
            "RequestedAmsNetId",
            RegEditTypeCode.REG_BINARY,
            bytesNetId,
            cancel);

        if (rebootNow)
        {
            await RebootAsync(0, cancel);
        }
    }

    public async Task<SystemInfo> GetSystemInfoAsync(CancellationToken cancel = default)
    {
        byte[] rdBfr = new byte[2048];

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var rRes = await adsConnection.ReadAsync((uint)AdsIndexGroups.SysServTcSystemInfo,1, rdBfr, cancel);

        adsConnection.Disconnect();

        rRes.ThrowOnError();

        string sysInfo = Encoding.UTF8.GetString(rdBfr);

        if (string.IsNullOrEmpty(sysInfo)) return new SystemInfo();

        SystemInfo devInfo = ParseSystemInfo(sysInfo);

        return devInfo;
    }

    private static SystemInfo ParseSystemInfo(string xmlContent)
    {
        XmlDocument xmlDoc = new();
        xmlDoc.LoadXml(xmlContent);

        return new SystemInfo
        {
            TargetType = TryGetValue(xmlDoc, "//TargetType"),
            TargetVersion = string.Join(".",
                TryGetValue(xmlDoc, "//TargetVersion/Version"),
                TryGetValue(xmlDoc, "//TargetVersion/Revision"),
                TryGetValue(xmlDoc, "//TargetVersion/Build")),
            TargetLevel = TryGetValue(xmlDoc, "//TargetFeatures/Level"),
            TargetNetId = TryGetValue(xmlDoc, "//TargetFeatures/NetId"),
            HardwareModel = TryGetValue(xmlDoc, "//Hardware/Model"),
            HardwareSerialNumber = TryGetValue(xmlDoc, "//Hardware/SerialNo"),
            HardwareCpuVersion = TryGetValue(xmlDoc, "//Hardware/CPUVersion"),
            HardwareDate = TryGetValue(xmlDoc, "//Hardware/Date"),
            HardwareCpuArchitecture = TryGetValue(xmlDoc, "//Hardware/CPUArchitecture"),
            OsImageDevice = TryGetValue(xmlDoc, "//OsImage/ImageDevice"),
            OsImageVersion = TryGetValue(xmlDoc, "//OsImage/ImageVersion"),
            OsImageLevel = TryGetValue(xmlDoc, "//OsImage/ImageLevel"),
            OsName = TryGetValue(xmlDoc, "//OsImage/OsName"),
            OsVersion = TryGetValue(xmlDoc, "//OsImage/OsVersion")
        };

        static string TryGetValue(XmlDocument xmlDoc, string xpath)
        {
            try
            {
                return xmlDoc.SelectSingleNode(xpath)?.InnerText ?? string.Empty;
            }
            catch (XmlException)
            {
                //_logger?.LogWarning("Could not read property {xpath} from netId {netId}", xpath, _netId);
                return string.Empty;
            }
        }
    }


    public async Task<DateTime> GetSystemTimeAsync(CancellationToken cancel = default)
    {
        byte[] rdBfr = new byte[16];

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var rRes = await adsConnection.ReadAsync((uint)AdsIndexGroups.SysServTimeServices, 1,rdBfr, cancel);

        adsConnection.Disconnect();

        rRes.ThrowOnError();

        return ConvertByteArrayToDateTime(rdBfr);
    }

    private static DateTime ConvertByteArrayToDateTime(byte[] byteArray)
    {
        if (byteArray == null || byteArray.Length < 16)
            throw new ArgumentException("byte array has to contain 16 elements");

        int year = byteArray[0] + (byteArray[1] << 8);

        int month = byteArray[2];
        int day = byteArray[4];

        int hour = byteArray[8];
        int minute = byteArray[10];
        int second = byteArray[12];

        return new DateTime(year, month, day, hour, minute, second);
    }

    public async Task<List<CpuUsage>> GetTcCpuUsageAsync(CancellationToken cancel = default)
    {
        byte[] rdBfr = new byte[2400];                                              //Read buffer is sufficient for up to 100 CPU Cores (Increase size if needed)

        using var session = CreateSession((int)AdsPorts.R0RTime);
        using var adsConnection = (AdsConnection)session.Connect();

        var rRes = await adsConnection.ReadAsync(1, 15, rdBfr, cancel);                //ToDo: add idxGrp and idxOffs to constants

        adsConnection.Disconnect();

        rRes.ThrowOnError();

        List<CpuUsage> cpuInfo = new();
        for (int i = 0; i < rRes.ReadBytes / 24; i++)
        {
            int baseIdx = i * 24;
            int latencyWarning = (rdBfr[13 + baseIdx] << 8) + rdBfr[baseIdx + 12];
            int coreLatency = (rdBfr[baseIdx + 9] << 8) + rdBfr[baseIdx + 8];

            cpuInfo.Add(
                new CpuUsage { 
                    cpuNo = rdBfr[baseIdx], 
                    latencyWarning = (uint)latencyWarning, 
                    systemLatency = (uint)coreLatency, 
                    utilization = rdBfr[baseIdx + 16] 
                });
        }
        return cpuInfo;
    }

    public async Task<RouterStatusInfo> GetRouterStatusInfoAsync(CancellationToken cancel = default)
    {
        ReadRequestHelper readRequest = new(32);

        using var session = CreateSession((int)AdsPorts.Router);
        using var adsConnection = (AdsConnection)session.Connect();

        var rRes = await adsConnection.ReadAsync(1, 1, readRequest.Bytes, cancel);

        rRes.ThrowOnError();

        RouterStatusInfo routerInfo = new()
        {
            RouterMemoryBytesReserved = readRequest.ExtractUint32(),
            RouterMemoryBytesAvailable = readRequest.ExtractUint32(),
            RegisteredPorts = readRequest.ExtractUint32(),
            RegisteredDrivers = readRequest.ExtractUint32()
        };
        return routerInfo;
    }

    public async Task<uint> GetAvailableRouterMemory(CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.Router);
        using var adsConnection = (AdsConnection)session.Connect();

        byte[] readBuffer = new byte[32];
        var readRes = await adsConnection.ReadAsync(1, 1, readBuffer, cancel);

        readRes.ThrowOnError();

        uint routerMemoryBytesReserved = BitConverter.ToUInt32(readBuffer, 0);
        uint routerMemoryBytesAvailable = BitConverter.ToUInt32(readBuffer, 4);
        uint registeredPorts = BitConverter.ToUInt32(readBuffer, 8);
        uint registeredDrivers = BitConverter.ToUInt32(readBuffer, 12);

        return routerMemoryBytesAvailable;
    }

    public async Task<short> GetPlatformLevelAsync(CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.LicenseServer);
        using var adsConnection = (AdsConnection)session.Connect();

        var rRes = await adsConnection.ReadAnyAsync<short>((uint)AdsIndexGroups.LicenseInfo, 0x2, cancel);

        adsConnection.Disconnect();

        rRes.ThrowOnError();

        return rRes.Value;
    }

    private async Task<byte[]> GetSystemIdBytesAsync(CancellationToken cancel = default)
    {
        byte[] rdBfr = new byte[16];

        using var session = CreateSession((int)AdsPorts.LicenseServer);
        using var adsConnection = (AdsConnection)session.Connect();

        var rRes = await adsConnection.ReadAsync((uint)AdsIndexGroups.LicenseInfo, 0x1, rdBfr, cancel);

        adsConnection.Disconnect();

        rRes.ThrowOnError();

        return rdBfr;
    }

    public async Task<Guid> GetSystemIdGuidAsync(CancellationToken cancel = default)
    {
        byte[] sysId = await GetSystemIdBytesAsync(cancel);
        return new Guid(sysId);
    }

    public async Task<string> GetSystemIdStringAsync(CancellationToken cancel = default)
    {
        var systemId = await GetSystemIdGuidAsync(cancel);
        return systemId.ToString();
    }

    public async Task<uint> GetVolumeNumberAsync(CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.LicenseServer);
        using var adsConnection = (AdsConnection)session.Connect();

        var rRes = await adsConnection.ReadAnyAsync<uint>((uint)AdsIndexGroups.LicenseInfo, 0x5, cancel);

        adsConnection.Disconnect();

        rRes.ThrowOnError();

        return rRes.Value;
    }

    public async Task<List<LicenseOnlineInfo>> GetOnlineLicensesAsync(CancellationToken cancel=default)
    {
        using var session = CreateSession((int)AdsPorts.LicenseServer);
        using var adsConnection = (AdsConnection)session.Connect();

        var countResult = await adsConnection.ReadAnyAsync<uint>(0x01010006, 0, cancel);
        countResult.ThrowOnError();

        uint licenseCount = countResult.Value;
        if (licenseCount == 0)
            return new();

        int structSize = Marshal.SizeOf<LicenseOnlineInfoMapped>();
        byte[] buffer = new byte[licenseCount * structSize];

        var readResult = await adsConnection.ReadAsync(0x01010006, 0, buffer, cancel);
        readResult.ThrowOnError();

        if (buffer.Length != readResult.ReadBytes)
            throw new ArgumentException("Unexpected Ads read return size");

        var licenses = new List<LicenseOnlineInfo>((int)licenseCount);
        for (int i = 0; i < licenseCount; i++)
        {
            LicenseOnlineInfo licenseInfo = (LicenseOnlineInfo)StructConverter.MarshalToStructure< LicenseOnlineInfoMapped>(buffer.AsMemory()[(i * structSize)..((i+1)*structSize)]);
            licenses.Add(licenseInfo);
        }
        adsConnection.Disconnect();
        return licenses;
    }

    public async Task<string> GetLicenseNameAsync(Guid licenseId, CancellationToken cancel=default)
    {
        using var session = CreateSession((int)AdsPorts.LicenseServer);
        using var adsConnection = (AdsConnection)session.Connect();

        byte[] readBuffer = new byte[64];
        byte[] writeBuffer = licenseId.ToByteArray();

        var result = await adsConnection.ReadWriteAsync(0x0101000C, 0, readBuffer, writeBuffer, cancel);
        result.ThrowOnError();

        adsConnection.Disconnect();

        return Encoding.UTF8.GetString(readBuffer).TrimEnd('\0');
    }





    // Action<T> --> Progress<T> for UI-Thread/SyncContext 
    public IDisposable RegisterEventListener(Action<AdsLogEntry> onEvent)
    {
        if (onEvent is null) throw new ArgumentNullException(nameof(onEvent));

        return RegisterEventListener(new Progress<AdsLogEntry>(onEvent));
    }

    // Actual API: IProgress<T>
    public IDisposable RegisterEventListener(IProgress<AdsLogEntry> progress)
    {
        if (_netId is null) throw new InvalidOperationException("NetId must be set before creating a session.");

        return AdsEventSubscription.Create(_netId, progress, _loggerFactory);
    }
    // ----------------- Internal Subscription Implementation -----------------
    private sealed class AdsEventSubscription : IDisposable
    {

        private static readonly NotificationSettings Settings =
            new(AdsTransMode.Cyclic, cycleTime: 0, maxDelay: 0);

        // Parser-Konstanten 
        private const int CyclicIgnoreLengthThreshold = 20;
        private static readonly byte[] PatternSender = new byte[] { 0x20, 0x50, 0x08 };

        private readonly IAdsSession _session;
        private readonly AdsConnection _conn;
        private readonly uint _handle;
        private readonly EventHandler<AdsNotificationEventArgs> _onNotify;
        private readonly IProgress<AdsLogEntry> _progress;

        private readonly AdsClient _adsClient;

        // Decoupling Producer/Consumer
        private readonly BlockingCollection<AdsNotificationEventArgs> _queue = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _workerTask;

        private bool _disposed;
        private readonly object _disposeLock = new();

        private AdsEventSubscription(
            IAdsSession session,
            AdsConnection conn,
            uint handle,
            IProgress<AdsLogEntry> progress,
            AdsClient adsClient)
        {
            _session = session;
            _conn = conn;
            _handle = handle;
            _progress = progress;
            _adsClient = adsClient;

            _onNotify = (s, e) =>
            {
                if (!_queue.IsAddingCompleted) _queue.Add(e);
            };
            _conn.AdsNotification += _onNotify;

            _workerTask = Task.Run(() => WorkerLoop(_cts.Token), _cts.Token);
        }

        public static AdsEventSubscription Create(  
            AmsNetId netId,
            IProgress<AdsLogEntry> progress,
            ILoggerFactory? loggerFactory)
        {
            // Connect
            var sessionSettings = SessionSettings.Default;
            AmsAddress amsAddress = new(netId, (int) AmsPort.EventLogPublisher);
            var session = new AdsSession(amsAddress, sessionSettings, loggerFactory);
            var conn = (AdsConnection)session.Connect();

            // Add Device-Notification on events
            var handle = conn.AddDeviceNotification(777, 0, 0x2000, Settings, new byte[16]);

            // AdsClient only for den Worker-Thread
            var logger = loggerFactory?.CreateLogger<AdsClient>() ?? NullLogger<AdsClient>.Instance;
            var adsClient = new AdsClient(logger);
            adsClient.Connect(netId, AmsPort.EventLogPublisher);

            return new AdsEventSubscription(session, conn, handle, progress, adsClient);
        }

        private void WorkerLoop(CancellationToken ct)
        {
            try
            {
                foreach (var e in _queue.GetConsumingEnumerable(ct))
                {
                    if (TryParseEvent(e, out var entry))
                        _progress.Report(entry);
                }
            }
            catch (OperationCanceledException) { /* Disposing */ }
            catch { /* TODO: Logging */ }
        }


        private bool TryParseEvent(AdsNotificationEventArgs e, out AdsLogEntry entry)
        {
            entry = default;
            var data = e.Data.ToArray();

            if (data.Length <= CyclicIgnoreLengthThreshold)
                return false; // ignore cyclic notifications

            try
            {
                // --- extrahierte 24 Bytes ab Offset 12 ---
                const int startIndex = 12;
                const int length = 24;
                if (data.Length < startIndex + length) return false;

                var eventAddr = new byte[length];
                Array.Copy(data, startIndex, eventAddr, 0, length);

                var writeBufferList = new List<byte>(0x100);
                writeBufferList.AddRange(new byte[] { 1, 0, 0, 0 });
                writeBufferList.AddRange(new byte[] { 0x64, 0, 0, 0 });
                writeBufferList.AddRange(new byte[] { 0x34, 0, 0, 0, 0, 0, 0, 0 });
                writeBufferList.AddRange(new byte[] { 0x9, 0x4, 0, 0 });
                writeBufferList.AddRange(eventAddr);
                writeBufferList.AddRange(new byte[] { 0, 0, 0, 0 });
                writeBufferList.AddRange(new byte[] { 0x34, 0, 0, 0, 0, 0, 0, 0 });
                writeBufferList.AddRange(new byte[] { 0x34, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

                var readBuffer = new byte[0x810];
                _adsClient.ReadWrite(500, 0, readBuffer, writeBufferList.ToArray());

                if (readBuffer.Length < 28) return false;

                int msgLen = readBuffer[24];
                int msgStart = 28;
                int msgEnd = msgStart + msgLen;
                if (msgLen <= 0 || msgEnd > readBuffer.Length) return false;

                string eventMessage = Encoding.UTF8.GetString(readBuffer, msgStart, msgLen);
                if (string.IsNullOrEmpty(eventMessage)) return false;

                // Sender anhand Pattern suchen, bis zum nächsten 0-Byte lesen
                string sender = string.Empty;
                int p = FindPattern(data, PatternSender);
                if (p != -1)
                {
                    int s = p + PatternSender.Length;
                    if (s < data.Length)
                    {
                        int zero = Array.IndexOf<byte>(data, 0, s);
                        int end = (zero >= 0) ? zero : data.Length;
                        if (end > s) sender = Encoding.UTF8.GetString(data, s, end - s);
                    }
                }

                byte lvl = (data.Length > 36) ? data[36] : (byte)AdsLogLevel.Info;
                var logLevel = (AdsLogLevel)Math.Clamp(lvl, (byte)AdsLogLevel.Verbose, (byte)AdsLogLevel.Critical);

                entry = new AdsLogEntry
                {
                    TimeRaised = e.TimeStamp,
                    LogLevel = logLevel,
                    Sender = sender,
                    Message = eventMessage
                };
                return true;
            }
            catch
            {
                // Parsing error - TODO: Logging
                return false;
            }
        }

        private static int FindPattern(byte[] data, byte[] pattern)
        {
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;

                // stop Worker 
                _cts.Cancel();
                _queue.CompleteAdding();
                try { _workerTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }

                // Disposing routine
                try { _conn.AdsNotification -= _onNotify; } catch { }
                try { _conn.DeleteDeviceNotification(_handle); } catch { }
                try { _conn.Disconnect(); } catch { }
                try { _conn.Dispose(); } catch { }
                try { _session.Disconnect(); } catch { }
                try { _adsClient.Dispose(); } catch { }

                _cts.Dispose();
                _queue.Dispose();
            }
        }
    }
}
