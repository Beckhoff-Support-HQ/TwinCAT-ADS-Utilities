namespace AdsUtilities;


public enum AdsPorts : int
{
    Router = 1,
    LicenseServer = 30,
    Logger = 100,
    EventLog = 110,
    R0RTime = 200,
    R0Io = 300,
    R0Plc = 800,
    SystemService = 10_000,
}

public enum AdsIndexGroups : uint
{
    // File system
    SysServOpenCreate = 100,
    SysServOpenRead = 101,
    SysServOpenWrite = 102,
    SysServCreateFile = 110,
    SysServCloseHandle = 111,
    SysServFOpen = 120,
    SysServFClose = 121,
    SysServFRead = 122,
    SysServFWrite = 123,
    SysServFSeek = 124,
    SysServFTell = 125,
    SysServFGets = 126,
    SysServFPuts = 127,
    SysServFScanF = 128,
    SysServFPrintF = 129,
    SysServFEof = 130,
    SysServFDelete = 131,
    SysServFRename = 132,
    SysServFFind = 133,
    SysServMkDir = 138,
    SysServRmDir = 139,

    // Routing 
    SysServBroadcast = 141,
    SysServTcSystemInfo = 700,
    SysServAddRemote = 801,
    SysServDelRemote = 802,
    SysServEnumRemote = 803,
    SysServIpHelperApi = 701,
    SysServIpHostName = 702,

    // NT 
    SysServRegHklm = 200,
    SysServSendEmail = 300,
    SysServTimeServices = 400,
    SysServStartProcess = 500,
    SysServChangeNetId = 600,

    // Device info
    DeviceData = 0xF100,

    // Symbol info
    AdsIGrpSymbolTab = 0,
    AdsIGrpSymbolName = 1,
    AdsIGrpSymbolVal = 2,
    AdsIGrpSymbolHandleByName = 3,
    AdsIGrpSymbolValByName = 4,
    AdsIGrpSymbolValByHandle = 5,
    AdsIGrpSymbolReleaseHandle = 6,
    AdsIGrpSymbolInfoByName = 7,
    AdsIGrpSymbolVersion = 8,
    AdsIGrpSymbolInfoByNameEx = 9,
    AdsIGrpSymbolDownload = 10,
    AdsIGrpSymbolUpload = 11,
    AdsIGrpSymbolUploadInfo = 12,
    AdsIGrpSymbolNote = 16,

    // IO
    IoDeviceStateBase = 0x5000,

    // CoE
    Coe = 0xF302,

    // License info
    LicenseInfo = 0x01010004,
}

public enum AdsIndexOffsets : uint
{
    // Routing
    SysServIpHelperAdapterInfo = 1,
    SysServIpHelperIpFromHostname = 4,

    // DeviceInfo
    DeviceDataAdsState = 0,
    DeviceDataDevState = 2,

    // IO Operations
    DeviceDataDeviceId = 1,
    DeviceDataDeviceName = 1,
    DeviceDataDeviceCount = 2,
    DeviceDataDeviceNetId = 5,
    DeviceDataDeviceType = 7,
    DeviceDataDeviceFullInfo = 8,

    // File system
    SysServFOpenReading = 1,
    SysServFOpenWriting = 2,
    SysServFOpenAppending = 4,
    SysServFOpenReadingWriting = 8,
    SysServFOpenAsBinary = 16,
    SysServFOpenAsText = 32,

    SysServFOpenPathGeneric = 1,
    SysServFOpenPathBootProject = 2,
    SysServFOpenPathBootData = 3,
    SysServFOpenPathBootPath = 4,
    SysServFOpenPathUserPath1 = 11,
}

public enum AdsDirectory : uint
{
    Generic = 1 << 16,
    BootProject = 2 << 16,
    BootData = 3 << 16,
    BootDir = 4 << 16,
    TargetDir = 5 << 16,
    ConfigDir = 6 << 16,
    InstallDir = 7 << 16,
}
