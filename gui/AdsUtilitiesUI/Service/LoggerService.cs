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

public class LoggerService(ObservableCollection<LogMessage> logMessages, Dispatcher dispatcher) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new GuiLogger(categoryName, logMessages, dispatcher);
    }

    public void Dispose()
    {

    }
}


public class GuiLogger(string categoryName, ObservableCollection<LogMessage> logMessages, Dispatcher dispatcher) : ILogger
{
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

        dispatcher.Invoke(() => logMessages.Add(logEntry));
    }
}