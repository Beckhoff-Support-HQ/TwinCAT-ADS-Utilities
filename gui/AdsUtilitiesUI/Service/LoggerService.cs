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

public class LoggerService : ILoggerProvider
{
    private readonly ObservableCollection<LogMessage> _logMessages;
    private readonly Dispatcher _dispatcher;

    public LoggerService(ObservableCollection<LogMessage> logMessages, Dispatcher dispatcher)
    {
        _logMessages = logMessages;
        _dispatcher = dispatcher;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new GuiLogger(categoryName, _logMessages, _dispatcher);
    }

    public void Dispose()
    {
        // Optional: Clean-up if needed
    }
}


public class GuiLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ObservableCollection<LogMessage> _logMessages;
    private readonly Dispatcher _dispatcher;

    public GuiLogger(string categoryName, ObservableCollection<LogMessage> logMessages, Dispatcher dispatcher)
    {
        _categoryName = categoryName;
        _logMessages = logMessages;
        _dispatcher = dispatcher;
    }

    public IDisposable BeginScope<TState>(TState state) => null!;

    public bool IsEnabled(LogLevel logLevel) => true; // Optional: Filter 

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                            Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (exception != null)
            message += $" → {exception.Message}";

        var logEntry = new LogMessage
        {
            Timestamp = DateTime.Now,
            Message = message,
            LogLevel = logLevel,
        };

        _dispatcher.Invoke(() => _logMessages.Add(logEntry));
    }
}