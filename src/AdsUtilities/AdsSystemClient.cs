using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using TwinCAT.Ads;

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



    // ToDo: Redo this. There already is an ads command to enable remote control on CE. Using file access in not necessary
    /*public static void EnableCeRemoteDisplay()
    {
        FileHandler.RenameFile(netId, @"\Hard Disk\RegFiles\CeRemoteDisplay_Disable.reg", @"\Hard Disk\RegFiles\CeRemoteDisplay_Enable.reg");
        Reboot(netId, 0);
    }*/

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
            registeredPorts = readRequest.ExtractUint32(),
            registeredDrivers = readRequest.ExtractUint32()
        };
        return routerInfo;
    }

    public async Task<uint> GetAvailableRouterMemory(string netId, CancellationToken cancel = default)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
        });

        using AdsClient adsClient = new(loggerFactory.CreateLogger("AdsClient"));
        adsClient.Connect(netId, 1);

        byte[] readBuffer = new byte[32];
        var readRes = await adsClient.ReadAsync(1, 1, readBuffer, cancel);

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
            LicenseOnlineInfo licenseInfo = (LicenseOnlineInfo)StructConverter.MarshalToStructure< LicenseOnlineInfoMapped>(buffer[(i * structSize)..((i+1)*structSize)]);
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
