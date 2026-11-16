using System.Text;
using Microsoft.Extensions.Logging;

namespace VocabularyCoach.Infrastructure.Logging;

/// <summary>
/// 轻量滚动文件日志：按天写入 app-yyyyMMdd.log，启动时清理过期文件。
/// 不依赖第三方日志框架，满足常驻托盘应用排查问题的需要。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly int _retentionDays;
    private readonly object _sync = new();

    private StreamWriter? _writer;
    private DateOnly _currentDate = DateOnly.MinValue;
    private bool _disposed;

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information, int retentionDays = 14)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        _retentionDays = retentionDays;

        Directory.CreateDirectory(_directory);
        CleanupExpiredLogs();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _minimumLevel;

    internal void Write(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var builder = new StringBuilder(160)
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(LevelText(logLevel)).Append("] ")
            .Append(categoryName).Append(": ")
            .Append(message);

        if (exception is not null)
        {
            builder.AppendLine().Append(exception);
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            EnsureWriter(today);
            _writer?.WriteLine(builder.ToString());
            _writer?.Flush();
        }
    }

    private void EnsureWriter(DateOnly today)
    {
        if (_writer is not null && _currentDate == today)
        {
            return;
        }

        _writer?.Dispose();
        _writer = null;
        _currentDate = today;

        try
        {
            var path = Path.Combine(_directory, $"app-{today:yyyyMMdd}.log");
            _writer = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = false };
        }
        catch (IOException)
        {
            // 日志写入失败不能影响应用运行。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void CleanupExpiredLogs()
    {
        if (_retentionDays <= 0)
        {
            return;
        }

        try
        {
            var threshold = DateTime.Now.AddDays(-_retentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "app-*.log"))
            {
                if (File.GetLastWriteTime(file) < threshold)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _categoryName;

        public FileLogger(FileLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _provider.Write(_categoryName, logLevel, formatter(state, exception), exception);
        }
    }
}
