using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AdsUtilitiesUI;

public class LogMessage
{
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
    public LogLevel LogLevel { get; set; } // LogLevel for filtering
}

internal class LoggerService : ILoggerProvider
{
    private readonly MainWindowViewModel _vm;

    public LoggerService(MainWindowViewModel vm)
    {
        _vm = vm;
    }

    public ILogger CreateLogger(string categoryName) => new GuiLogger(_vm);

    public void Dispose() { }
}

internal class GuiLogger : ILogger
{
    private readonly MainWindowViewModel _vm;

    public GuiLogger(MainWindowViewModel vm)
    {
        _vm = vm;
    }

    public IDisposable BeginScope<TState>(TState state) => null!;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var msg = formatter(state, exception);
        if (exception != null)
            msg += $" → {exception.Message}";

        _vm.AddLog(new LogMessage
        {
            Timestamp = DateTime.Now,
            Message = msg,
            LogLevel = logLevel
        });
    }
}