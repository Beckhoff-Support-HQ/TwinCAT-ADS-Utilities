using Microsoft.Extensions.Logging;
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

    public void RegisterEventListener(List<AdsLogEntry> adsLogs, Action<AdsLogEntry>? eventRaised)
    {
        //using var session = CreateSession((int)AmsPort.Logger);
        var session = CreateSession(132);
        var adsConnection = (AdsConnection)session.Connect();

        adsConnection.AdsNotification += (sender, e) => OnNotification(e);

        var settings = new NotificationSettings(AdsTransMode.Cyclic, 0, 0);
        byte[] userData = new byte[16];
        //uint handle = adsConnection.AddDeviceNotification(0x1, 0xffff, 1024, settings, null);   // ToDo: Clean up routine
        uint handle = adsConnection.AddDeviceNotification(777, 0, 0x2000, settings, userData);   // ToDo: Clean up routine
        Console.WriteLine("Port: " + adsConnection.Address.Port);
        Console.WriteLine("Client port: " + adsConnection.ClientAddress.Port); // returns _session's port instead of _client's port
        void OnNotification(AdsNotificationEventArgs e)
        {
            var adsEvent = ParseEvent(e);
            if (adsEvent.Message == string.Empty)
            {
                return;
            }
            adsLogs.Add(adsEvent);
            eventRaised?.Invoke(adsEvent);
        }
    }

    private static AdsLogEntry ParseEvent(AdsNotificationEventArgs e)
    {
        static int FindPattern(byte[] data, byte[] pattern)
        {
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        byte[] eventData = e.Data.ToArray();
        if (eventData.Length > 20)  // there are cyclic notifications with 16 byte data arrays - perhaps indications that event logger is running?
        {
            using AdsClient adsClient = new();
            adsClient.Connect(132);
            var readBuffer = new byte[0x810];
            var writeBuffer = new byte[0x44];
            var writeBufferList = new List<byte>();


            string eventMessage = string.Empty;
            byte[] pattern = new byte[] { 0x99, 0x00, 0x00, 0x00 };
            int startIndex = FindPattern(eventData.ToArray(), pattern);
            if (startIndex != -1)
            {
                int length = 24;
                byte[] extracted = new byte[length];
                Array.Copy(eventData.ToArray(), startIndex + pattern.Length, extracted, 0, length);

                

                writeBufferList.AddRange(new byte[] { 1, 0, 0, 0 });
                writeBufferList.AddRange(new byte[] { 0x64, 0, 0, 0 });
                writeBufferList.AddRange(new byte[] { 0x34, 0, 0, 0, 0, 0, 0, 0 });
                writeBufferList.AddRange(new byte[] { 0x9, 0x4, 0, 0 });
                writeBufferList.AddRange(extracted);
                writeBufferList.AddRange(new byte[] { 0, 0, 0, 0 });
                writeBufferList.AddRange(new byte[] { 0x34, 0, 0, 0, 0, 0, 0, 0 });
                writeBufferList.AddRange(new byte[] { 0x34, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

                var res = adsClient.ReadWrite(500, 0, readBuffer, writeBufferList.ToArray());

                int msgLen = readBuffer[24];

                eventMessage = Encoding.UTF8.GetString(readBuffer[28..(28 + msgLen)]);


            }
            else
            {
                // parsing error
            }

            byte logLevel = eventData.ToArray()[36];   // 0: VErbose, 1:Info, 2:Warning, 3:Error, 4:Critical

            AdsLogEntry log = new AdsLogEntry
            {
                TimeRaised = e.TimeStamp,   // There is a timestamp in the eventdata too but it is not transmitted when there are multiple events in a single cycle so were using the ADS timestamp
                LogLevel = (AdsLogLevel)logLevel,
                Message = eventMessage
            };
            return log;
        }
        AdsLogEntry logDefault = new AdsLogEntry
        {
            TimeRaised = e.TimeStamp,  
            LogLevel = 0,
            Message = string.Empty
        };
        return logDefault;

    }
}
public enum AdsLogLevel
{
    Verbose = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4,
}

public struct AdsLogEntry
{
    public DateTimeOffset TimeRaised;
    public AdsLogLevel LogLevel;
    //public int AdsPort;
    //public string Sender;
    public string Message;
}
public enum RegEditTypeCode
{
    REG_NONE,
    REG_SZ,
    REG_EXPAND_SZ,
    REG_BINARY,
    REG_DWORD,
    REG_DWORD_BIG_ENDIAN,
    REG_LINK,
    REG_MULTI_SZ,
    REG_RESOURCE_LIST,
    REG_FULL_RESOURCE_DESCRIPTOR,
    REG_RESOURCE_REQUIREMENTS_LIST,
    REG_QWORD
}

