﻿using AdsUtilities;
using AdsUtilitiesUI.Model;
using AdsUtilitiesUI.ViewModels;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Windows.Input;
using TwinCAT.Ads;
using System.Windows.Threading;


namespace AdsUtilitiesUI;

class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<TabViewModel> Tabs { get; set; }
    private TabViewModel _selectedTab;

    public TargetService _targetService { get; }

    private ILogger _logger;

    private ILoggerFactory _LoggerFactory;

    public ObservableCollection<LogMessage> LogMessages { get; set; }

    public TabViewModel SelectedTab
    {
        get => _selectedTab;
        set
        {
            _selectedTab = value;
            OnPropertyChanged();
        }
    }

    public MainWindowViewModel() 
    {
        _targetService = new TargetService();
        _targetService.OnTargetChanged += TargetService_OnTargetChanged;

        RemoteConnectCommand = new(SetupRemoteConnection);
        ReloadRoutesCommand = new(ReloadRoutes);
        ShutdownCommand = new(Shutdown);
        RebootCommand = new(Reboot);

        LogMessages = new();
        _LoggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information)
                .AddProvider(new LoggerService(LogMessages, Dispatcher.CurrentDispatcher));
        });

        _logger = _LoggerFactory.CreateLogger<MainWindowViewModel>();

        Tabs = new ObservableCollection<TabViewModel>
        {
            new ("ADS Routing", new AdsRoutingViewModel(_targetService, _LoggerFactory)),
            new ("File Access", new FileHandlingViewModel(_targetService, _LoggerFactory)),
            new ("Device Info", new DeviceInfoViewModel(_targetService, _LoggerFactory)),
        };
        SelectedTab = Tabs[0];
    }


    private void TargetService_OnTargetChanged(object sender, StaticRouteStatus newTarget)
    {
        OnPropertyChanged(nameof(_targetService.CurrentTarget));
        //if (CurrentTarget?.NetId != newTarget.NetId)
        //{
        //    CurrentTarget = newTarget;
        //}
    }
    
    public AsyncRelayCommand ReloadRoutesCommand { get; }

    public async Task ReloadRoutes()
    {
        await _targetService.Reload_Routes();
    }

    public AsyncRelayCommand RemoteConnectCommand { get; }
    public async Task SetupRemoteConnection()
    {
        // Cancel if route is local or invalid
        if (string.IsNullOrEmpty(_targetService.CurrentTarget.NetId))
            return;

        if (IPAddress.TryParse(_targetService.CurrentTarget.IpAddress, out IPAddress? address))
        {
            if (address is not null && address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();

                if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0)
                    return;
            }
        }
        

        // Check OS
        AdsSystemClient systemClient = new (_LoggerFactory);
        await systemClient.Connect(_targetService.CurrentTarget.NetId);
        SystemInfo sysInfo = await systemClient.GetSystemInfoAsync();
        string os = sysInfo.OsName;

        if (os.Contains("Windows"))
        {
            if (os.Contains("CE"))
            {
                // Windows CE
                await RemoteConnector.CerhostConnect(_targetService.CurrentTarget.IpAddress);
            }
            else
            {
                // Big Windows
                await Task.Run(() => RemoteConnector.RdpConnect(_targetService.CurrentTarget.IpAddress));
            }
        }
        else if (os.Contains("BSD"))
        {
            // TC/BSD
            await RemoteConnector.SshPowerShellConnect(_targetService.CurrentTarget.IpAddress, _targetService.CurrentTarget.Name);
        }
        else
        {
            // For RTOS or unknown OS -> display error message
            throw new ArgumentException("The selected system does not support remote control or there is no implementation currently. This error should be replaced with a message box."); // ToDo
        }          
    } 

    public AsyncRelayCommand ShutdownCommand { get; }

    public async Task Shutdown()
    {
        if (_targetService.CurrentTarget?.NetId == AmsNetId.Local.ToString())
        {
            _logger.LogInformation("Please select a target other than local");
            return;
        }

        try
        {
            AdsSystemClient systemClient = new(_LoggerFactory);
            await systemClient.Connect(_targetService.CurrentTarget?.NetId);
            await systemClient.ShutdownAsync();                                    
        }
        catch
        {
            _logger.LogError("Could not shutdown target");
        }
        _logger.LogInformation("Shutting down now...");
    }

    public AsyncRelayCommand RebootCommand { get; }

    public async Task Reboot()
    {
        if (_targetService.CurrentTarget?.NetId == AmsNetId.Local.ToString())
        {
            _logger.LogInformation("Please select a target other than local");
            return;
        }

        try
        {
            AdsSystemClient systemClient = new(_LoggerFactory);
            await systemClient.Connect(_targetService.CurrentTarget?.NetId);
            await systemClient.RebootAsync();
        }
        catch
        {
            _logger.LogError("Could not reboot target");
        }
        _logger.LogInformation("Rebooting now...");
    }
}
