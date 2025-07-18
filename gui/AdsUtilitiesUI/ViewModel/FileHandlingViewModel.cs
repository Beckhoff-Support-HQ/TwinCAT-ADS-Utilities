using AdsUtilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdsUtilitiesUI.ViewModels;

class FileHandlingViewModel : ViewModelTargetAccessPage
{
    private ILogger _logger;

    private ILoggerFactory _LoggerFactory;

    public FileHandlingViewModel(TargetService targetService, ILoggerFactory loggerFactory)
    {
        _TargetService = targetService;    
        InitTargetAccess(_TargetService);

        // Set secondary route to local as soon as target service has loaded all routes
        _TargetService.OnTargetChanged += (sender, e) =>
        {
            if (SecondaryTarget is null)
            {
                SecondaryTarget = _TargetService.CurrentTarget;
            }
            _TargetService.OnTargetChanged -= InitSecondaryRoute;   // Remove this event after initial execution
        };

        _LoggerFactory = loggerFactory;
        _logger = _LoggerFactory.CreateLogger<FileHandlingViewModel>();
    }

    private void InitSecondaryRoute(object? sender, StaticRouteStatus e)
    {
        if(SecondaryTarget is null)
        {
            SecondaryTarget = _TargetService.CurrentTarget;
        }
    }

    private StaticRoutesInfo _SecondaryTarget;

    public StaticRoutesInfo SecondaryTarget
    {
        get => _SecondaryTarget;
        set
        {
            _SecondaryTarget = value;
            OnPropertyChanged();
        }
    }

}
