using AdsUtilities;
using AdsUtilitiesUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
using TwinCAT.Ads;
using System.Windows;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Windows.Data;

namespace AdsUtilitiesUI;

public class DeviceInfoViewModel : ViewModelTargetAccessPage
{
    private ILogger _logger;

    private ILoggerFactory _LoggerFactory;

    public ObservableCollection<NetworkInterfaceInfo> NetworkInterfaces { get; set; }

    public ObservableCollection<StaticRoutesInfo> RouteEntries { get; set; }

    private SystemInfo _systemInfo;
    public SystemInfo SystemInfo
    {
        get => _systemInfo;
        set
        {
            _systemInfo = value;
            OnPropertyChanged(nameof(SystemInfo));
        }
    }
    
    private readonly System.Timers.Timer _updateTimer;

    private string _TcState;
    public string TcState
    {
        get => _TcState;
        set
        {
            _TcState = value;
            OnPropertyChanged(nameof(TcState));
        }
    }

    private RouterStatusInfo _routerStatusInfo;
    public RouterStatusInfo RouterStatusInfo
    {
        get => _routerStatusInfo;
        set
        {
            _routerStatusInfo = value;
            OnPropertyChanged(nameof(RouterStatusInfo));
            OnPropertyChanged(nameof(TargetRouterMemoryDisplay));
        }
    }

    private DateTime _targetTime;

    public DateTime TargetTime
    {
        get => _targetTime;
        set
        {
            _targetTime = value;
            OnPropertyChanged(nameof(TargetTime));
            OnPropertyChanged(nameof(TargetTimeDisplay));
        }
    }

    public string TargetTimeDisplay
    {
        get => TargetTime.ToString("yyyy/MM/dd-HH:mm");
    }

    public string TargetRouterMemoryDisplay =>
        $"{FormatBytes(RouterStatusInfo.RouterMemoryBytesAvailable)} available of {FormatBytes(RouterStatusInfo.RouterMemoryBytesReserved)}";


    private string _systemId;
    public string SystemId
    {
        get => _systemId;
        set
        {
            _systemId = value;
            OnPropertyChanged(nameof(SystemId));
        }
    }

    private uint _volumeNumber;
    public uint VolumeNumber
    {
        get => _volumeNumber;
        set
        {
            _volumeNumber = value;
            OnPropertyChanged(nameof(VolumeNumber));
        }
    }

    private short _platformLevel;
    public short PlatformLevel
    {
        get => _platformLevel;
        set
        {
            _platformLevel = value;
            OnPropertyChanged(nameof(PlatformLevel));
        }
    }

    private bool _netIdChangePending;
    public bool NetIdChangePending
    { 
        get => _netIdChangePending;
        set 
        {
            _netIdChangePending = value;
            OnPropertyChanged(nameof(NetIdChangePending));
        }
    }

    private string _netIdPending;
    public string NetIdPending
    {
        get => _netIdPending;
        set
        {
            _netIdPending = value;
            NetIdChangePending = _netIdPending != Target.NetId;
            OnPropertyChanged(nameof(NetIdPending));
        }
    }

    public ICommand InstallRteDriverCommand { get; }

    public ICommand SetTickCommand { get; }

    public ICommand DeleteRouteEntryCommand { get; }

    public ICommand SetNetIdAndRebootCommand { get; }

    public ICommand SetNetIdCommand { get; }

    public DeviceInfoViewModel(TargetService targetService, ILoggerFactory loggerFactory)
    {
        _TargetService = targetService;
        InitTargetAccess(_TargetService);

        _LoggerFactory = loggerFactory;
        _logger = _LoggerFactory.CreateLogger<DeviceInfoViewModel>();

        _TargetService.OnTargetChanged += async (sender, args) => await UpdateDeviceInfo();

        SystemInfo = new();

        _updateTimer = new System.Timers.Timer(10_000);
        _updateTimer.Elapsed += async (sender, e) => await UpdateLiveValues();
        _updateTimer.AutoReset = true;
        _updateTimer.Start();
        
        InstallRteDriverCommand = new AsyncRelayCommand(InstallRteDriver);
        SetTickCommand = new AsyncRelayCommand(ExecuteSetTick);
        DeleteRouteEntryCommand = new AsyncRelayCommand(DeleteRouteEntry);

        SetNetIdAndRebootCommand = new AsyncRelayCommand(SetNetIdAndReboot);
        SetNetIdCommand = new AsyncRelayCommand(SetNetId);


        NetworkInterfaces = [];
        RouteEntries = [];


        // Logs View
        LogsView = CollectionViewSource.GetDefaultView(Logs);
        LogsView.Filter = LogFilter;
        ClearCommand = new RelayCommand(Clear);
        ReapplyFilterCommand = new RelayCommand(ApplyFilter);
        EnableAllLevelsCommand = new RelayCommand(() => SetAllLevels(true));
        DisableAllLevelsCommand = new RelayCommand(() => SetAllLevels(false));
    }

    public async Task UpdateDeviceInfo()
    {
        await Task.WhenAll(
            LoadNetworkInterfacesAsync(),
            LoadSystemInfoAsync(),
            UpdateTcState(),
            UpdateRouterUsage(),
            UpdateTime(),
            UpdateSystemId(),
            UpdateVolumeNumber(),
            UpdatePlatformLevel(),
            ReloadRouteEntries()
        );

        IsListening = false;
        StopListening();
        Clear();

        _ = Task.Run(LoadLicensesAsync);
        NetIdPending = Target.NetId;
    }

    private bool _isUpdating = false;

    public async Task UpdateLiveValues()
    {
        if (_isUpdating) return;

        _isUpdating = true;

        try
        {
            await Task.WhenAll(UpdateTcState(), UpdateRouterUsage(), UpdateTime());
        }
        finally
        {
            _isUpdating = false;
        }
    }

    public async Task LoadSystemInfoAsync(CancellationToken cancel = default)
    {
        try
        {
            AdsSystemClient systemClient = new(_LoggerFactory);
            await systemClient.Connect(Target?.NetId, cancel);
            SystemInfo = await systemClient.GetSystemInfoAsync(cancel);
        }
        catch (Exception ex) 
        {
            _logger.LogError(ex, "Failed to load system info for target {NetId}", Target?.NetId);
        }
    }

    public async Task UpdateTime(CancellationToken cancel = default)
    {
        try
        {
            AdsSystemClient systemClient = new(_LoggerFactory);
            await systemClient.Connect(Target?.NetId, cancel);
            TargetTime = await systemClient.GetSystemTimeAsync(cancel);
        }
        catch (Exception ex) 
        {
            _logger.LogError(ex, "Failed to update target time for {NetId}", Target?.NetId);
            TargetTime = DateTime.MinValue; 
        }
    }

    public async Task UpdateSystemId(CancellationToken cancel = default)
    {
        try
        {
            AdsSystemClient systemClient = new(_LoggerFactory);
            await systemClient.Connect(Target?.NetId, cancel);
            SystemId = await systemClient.GetSystemIdStringAsync(cancel);
        }
        catch (Exception ex) 
        {
            _logger.LogError(ex, "Failed to update system ID for target {NetId}", Target?.NetId);
            SystemId = string.Empty;  // Set to empty if failed
        }
    }

    public async Task UpdateVolumeNumber(CancellationToken cancel = default)
    {
        try
        {
            AdsSystemClient systemClient = new(_LoggerFactory);
            await systemClient.Connect(Target?.NetId, cancel);
            VolumeNumber = await systemClient.GetVolumeNumberAsync(cancel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update volume number for target {NetId}", Target?.NetId);
            VolumeNumber = 0;  // Set to 0 if failed
        }
    }

    public async Task UpdatePlatformLevel(CancellationToken cancel = default)
    {
        try
        {
            AdsSystemClient systemClient = new(_LoggerFactory);
            await systemClient.Connect(Target?.NetId, cancel);
            PlatformLevel = await systemClient.GetPlatformLevelAsync(cancel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update platform level for target {NetId}", Target?.NetId);
            PlatformLevel = 0;  // Set to 0 if failed
        }
    }

    public async Task UpdateTcState(CancellationToken cancel = default)
    {
        try
        {
            using AdsClient adsClient = new(default, _LoggerFactory.CreateLogger<AdsClient>());
            adsClient.Connect(Target?.NetId, 10_000);
            ResultReadDeviceState state = await adsClient.ReadStateAsync(cancel);
            TcState = state.State.AdsState.ToString();
        }
        catch (Exception ex) 
        {
            TcState = ex.Message;
        }
    }

    public async Task UpdateRouterUsage(CancellationToken cancel = default)
    {
        try
        {
            AdsSystemClient systemClient = new(_LoggerFactory);
            await systemClient.Connect(Target?.NetId, cancel);
            var routerInfo = await systemClient.GetRouterStatusInfoAsync(cancel);
            RouterStatusInfo = routerInfo;
        }
        catch (Exception ex) 
        {
            _logger.LogError(ex, "Failed to update router usage for target {NetId}", Target?.NetId);
            RouterStatusInfo = new RouterStatusInfo
            {
                RouterMemoryBytesAvailable = 0,
                RouterMemoryBytesReserved = 0
            };  // Set to default if failed
        }
    }  

    public async Task LoadNetworkInterfacesAsync(CancellationToken cancel = default)
    {
        try
        {
            AdsRoutingClient routingClient = new(_LoggerFactory);
            await routingClient.Connect(Target?.NetId, cancel);
            var interfaces = await routingClient.GetNetworkInterfacesAsync(cancel);

            NetworkInterfaces.Clear();
            foreach (var nic in interfaces)
            {
                NetworkInterfaces.Add(nic);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load network interfaces for target {NetId}", Target?.NetId);
        }
    }
    private string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        if (bytes >= GB)
            return $"{(bytes / (double)GB):0.##} GB";
        if (bytes >= MB)
            return $"{(bytes / (double)MB):0.##} MB";
        if (bytes >= KB)
            return $"{(bytes / (double)KB):0.##} KB";

        return $"{bytes} B";
    }

    private async Task InstallRteDriver(object networkInterface)
    {
        if (networkInterface is NetworkInterfaceInfo nic)
        {
            var rteInstallerPath = @"C:\TwinCAT\3.1\System\TcRteInstall.exe";   // ToDo: Get path and dir at runtime
            var directory = @"C:\TwinCAT\3.1\System";

            var installCommand = $"-r installnic \"{nic.Name}\"";

            AdsFileClient fileClient = new (_LoggerFactory);
            await fileClient.Connect(Target?.NetId);
            await fileClient.StartProcessAsync(rteInstallerPath, directory, installCommand);
            return;
        }
        _logger.LogError("Unexpected Error occured");
    }

    private async Task DeleteRouteEntry(object routeEntry)
    {
        // RTE drivers are preinstalled on WinCE, BSD and RTOS
        if (!SystemInfo.OsName.Contains("Win") || SystemInfo.OsName.Contains("CE"))
        {
            _logger.LogInformation("Drivers are preinstalled on the selected target");
            return;
        }

        // There is no cli to install drivers remotely on TC2 systems
        if (SystemInfo.TargetVersion.StartsWith("2."))
        {
            _logger.LogWarning("Cannot install drivers on TC2 systems remotely");
            return;
        }

        // ToDo: Check if driver is installed already

        if (routeEntry is StaticRoutesInfo routeInfo)
        {
            AdsRoutingClient routingClient = new(_LoggerFactory);
            await routingClient.Connect(Target?.NetId);
            await routingClient.RemoveLocalRouteEntryAsync(routeInfo.Name);
            await ReloadRouteEntries();
            await _TargetService.Reload_Routes();           
            return;
        }
    }

    private async Task ReloadRouteEntries()
    {
        AdsRoutingClient adsRoutingClient = new(_LoggerFactory);
        bool connected = await adsRoutingClient.Connect(Target?.NetId);

        List<StaticRoutesInfo> routes = await adsRoutingClient.GetRoutesListAsync();

        Application.Current.Dispatcher.Invoke(() =>
        {
            RouteEntries.Clear();

            foreach (var route in routes)
            {
                RouteEntries.Add(route);
            }
        });
    }


    public class LicenseInfoViewModel
    {
        public string Name { get; set; }
        public Guid Id { get; set; }
        public string Used { get; set; }
        public string Status { get; set; }
        public uint VolumeNumber { get; set; }
    }

    public ObservableCollection<LicenseInfoViewModel> Licenses { get; } = [];

    public async Task LoadLicensesAsync()
    {
        AdsSystemClient systemClient = new(_LoggerFactory);
        await systemClient.Connect(Target.NetId);

        var licenseList = await systemClient.GetOnlineLicensesAsync();

        var licenseListFiltered = licenseList
            .Where(lic => lic.State != LicenseState.ValidPending)
            .ToList();

        var nameTasks = licenseListFiltered.Select(async lic =>
        {
            try
            {
                // Timeout für jede Lizenzabfrage
                var nameTask = systemClient.GetLicenseNameAsync(lic.LicenseId);
                if (await Task.WhenAny(nameTask, Task.Delay(5_000)) == nameTask)
                {
                    return (lic, nameTask.Result);
                }
                else
                {
                    // Timeout
                    return (lic, "Timeout");
                }
            }
            catch (Exception)
            {
                return (lic, string.Empty);
            }
        }).ToArray();


        var results = await Task.WhenAll(nameTasks);

          
        // UI-Thread for Collection-Update
        Application.Current.Dispatcher.Invoke(() =>
        {
            Licenses.Clear();
            foreach (var (lic, name) in results)
            {
                Licenses.Add(new LicenseInfoViewModel
                {
                    Name = name,
                    Id = lic.LicenseId,
                    Status = FormatLicenseState(lic.State, lic.ExpireTime),
                    Used = FormatUsed(lic.Count, lic.Used),
                    VolumeNumber = lic.VolumeNo
                });
            }
        });

        static string FormatUsed(uint count, uint used)
        {
            if (count == 0)
                return string.Empty;
            return $"{used} of {count}";
        }

        static string FormatLicenseState(LicenseState state, DateTime expiration)
        {
            return state switch
            {
                LicenseState.Valid => "Valid",
                LicenseState.ValidTrial => $"Valid Trial, expires {expiration:MM/dd/yyyy}",
                LicenseState.ValidPending => "Valid Pending",
                LicenseState.ValidOem => "Valid OEM",
                _ => "Unknown"
            };
        }
    }

    private async Task SetNetIdAndReboot()
    {
        AdsSystemClient systemClient = new(_LoggerFactory);
        await systemClient.Connect(Target.NetId);
        var systemInfo = await systemClient.GetSystemInfoAsync();
        if (!systemInfo.OsName.Contains("Win") || systemInfo.OsName.Contains("CE"))
        {
            _logger.LogError("Changing NetId Currently only supported for Big Windows");     // ToDO
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"Edit NetId in local route entry for {Target.Name}?\nThe route must be re-added manually otherwise", 
            "Edit route entry?", 
            MessageBoxButton.YesNo, 
            MessageBoxImage.Question);

        bool editRouteEntry = result == MessageBoxResult.Yes;


        await systemClient.ChangeNetIdOnWindowsAsync(NetIdPending, true);

        if(editRouteEntry)
        {
            AdsRoutingClient localRoutingClient = new(_LoggerFactory);

            await localRoutingClient.Connect(AmsNetId.Local.ToString());

            var routes = await localRoutingClient.GetRoutesListAsync();

            var routeToEdit = routes.Where(r => r.NetId == Target.NetId && r.Name == Target.Name).First();

            await localRoutingClient.RemoveLocalRouteEntryAsync(Target.NetId);

            await localRoutingClient.AddLocalRouteEntryByIpAsync(NetIdPending, routeToEdit.IpAddress, routeToEdit.Name);

        }
    }

    private async Task SetNetId()
    {
        AdsSystemClient systemClient = new(_LoggerFactory);
        await systemClient.Connect(Target.NetId);
        var systemInfo = await systemClient.GetSystemInfoAsync();
        if (!systemInfo.OsName.Contains("Win") || systemInfo.OsName.Contains("CE"))
        {
            _logger.LogError("Changing NetId Currently only supported for Big Windows");     // ToDo
            return;
        }

        await systemClient.ChangeNetIdOnWindowsAsync(NetIdPending, false);
    }

    private async Task ExecuteSetTick()
    {
        if (!SystemInfo.OsName.Contains("Win") || SystemInfo.OsName.Contains("CE"))
        {
            _logger.LogInformation("No need to set tick on selected target");
            return;
        }

        string path;
        string dir;

        // There is no cli to install drivers remotely on TC2 systems
        if (SystemInfo.TargetVersion.StartsWith("2."))
        {
            path = @"C:\TwinCAT\Io\win8settick.bat";  
            dir = @"C:\TwinCAT\Io";
        }
        else
        {
            path = @"C:\TwinCAT\3.1\System\win8settick.bat";
            dir = @"C:\TwinCAT\3.1\System";
        }

        try
        {
            AdsFileClient fileClient = new();
            await fileClient.Connect(Target?.NetId);
            await fileClient.StartProcessAsync(path, dir, string.Empty);
        }
        catch (Exception)
        {
            _logger.LogError("Execution of win8settick.bat failed");
            return;
        }
        _logger.LogInformation("Set tick successfully. Please reboot target");
    }



    // Event Logging -------------------------------------------------------------

    public ObservableCollection<AdsLogEntry> Logs { get; } = new();
    public ICollectionView LogsView { get; }

    private bool _isListening;
    public bool IsListening
    {
        get => _isListening;
        set
        {
            if (_isListening == value) return;
            _isListening = value;
            OnPropertyChanged(); 
            _ = ToggleListeningAsync(_isListening);
        }
    }

    // Filter-Flags
    public bool ShowVerbose { get => _showVerbose; set { _showVerbose = value; OnPropertyChanged(); ApplyFilter(); } }
    public bool ShowInfo { get => _showInfo; set { _showInfo = value; OnPropertyChanged(); ApplyFilter(); } }
    public bool ShowWarning { get => _showWarning; set { _showWarning = value; OnPropertyChanged(); ApplyFilter(); } }
    public bool ShowError { get => _showError; set { _showError = value; OnPropertyChanged(); ApplyFilter(); } }
    public bool ShowCritical { get => _showCritical; set { _showCritical = value; OnPropertyChanged(); ApplyFilter(); } }

    private bool _showVerbose = true, _showInfo = true, _showWarning = true, _showError = true, _showCritical = true;

    // Commands
    public ICommand ClearCommand { get; }
    public ICommand ReapplyFilterCommand { get; }            
    public ICommand EnableAllLevelsCommand { get; }
    public ICommand DisableAllLevelsCommand { get; }

    // ---- Listener/ADS ----
    private AdsSystemClient? _adsSystemClient;
    private IDisposable? _eventSub;
    private IProgress<AdsLogEntry>? _progress;
    private CancellationTokenSource? _cts;


    //public LogsViewModel()
    //{
    //    // View
    //    LogsView = CollectionViewSource.GetDefaultView(Logs);
    //    LogsView.Filter = LogFilter;

    //    // Commands (nimm deine RelayCommand-Implementierung)
    //    ClearCommand = new RelayCommand(Clear);
    //    ReapplyFilterCommand = new RelayCommand(ApplyFilter);
    //    EnableAllLevelsCommand = new RelayCommand(() => SetAllLevels(true));
    //    DisableAllLevelsCommand = new RelayCommand(() => SetAllLevels(false));
    //}

    // ---------- Commands ----------
    private void Clear()
    {
        Logs.Clear();
    }

    private void ApplyFilter()
    {
        LogsView.Refresh();
    }

    private void SetAllLevels(bool value)
    {
        ShowVerbose = ShowInfo = ShowWarning = ShowError = ShowCritical = value;
    }

    private bool LogFilter(object o)
    {
        if (o is not AdsLogEntry e) return false;
        return e.LogLevel switch
        {
            AdsLogLevel.Verbose => ShowVerbose,
            AdsLogLevel.Info => ShowInfo,
            AdsLogLevel.Warning => ShowWarning,
            AdsLogLevel.Error => ShowError,
            AdsLogLevel.Critical => ShowCritical,
            _ => true
        };
    }

    // ---------- Listener ----------
    private async Task ToggleListeningAsync(bool enable)
    {
        if (enable)
            await StartListeningAsync();
        else
            StopListening();
    }

    private async Task StartListeningAsync()
    {
        if (_eventSub != null) return;

        _cts = new CancellationTokenSource();
        _adsSystemClient = new AdsSystemClient();

        _progress = new Progress<AdsLogEntry>(entry =>
        {
            // UI-Thread:
            App.Current.Dispatcher.Invoke(() => Logs.Add(entry));
        });

        try
        {
            await _adsSystemClient.Connect(Target?.NetId).ConfigureAwait(false);

            _eventSub = _adsSystemClient.RegisterEventListener(_progress);
        }
        catch
        {
            StopListening();
            IsListening = false;
            throw;
        }
    }

    private void StopListening()
    {
        try
        {
            _eventSub?.Dispose();
            _eventSub = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        finally
        {
            _adsSystemClient = null;
        }
    }

    public void Dispose()
    {
        StopListening();
    }

}
