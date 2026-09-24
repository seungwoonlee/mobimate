using System.Collections.Concurrent;
using System.Text;

namespace MobiMate.Web.Hosting;

/// <summary>
/// 일 단위 파일 로그 (NFR-15): %APPDATA%\MobiMateWeb\logs\mobimate-yyyyMMdd.log, 7일 보관.
/// 채팅·AI 대화 본문은 로그에 쓰지 않는다(호출하는 쪽 규칙). 쓰기 실패는 무시한다.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    public const int KeepDays = 7;

    public FileLoggerProvider(string dir)
    {
        _dir = dir;
        try
        {
            Directory.CreateDirectory(_dir);
            foreach (var f in Directory.GetFiles(_dir, "mobimate-*.log"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-KeepDays)) File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, n => new FileLogger(this, n));

    internal void Write(string category, LogLevel level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ')
            .Append(level.ToString()[..4].ToUpperInvariant()).Append(' ')
            .Append(category).Append(": ").Append(message);
        if (ex != null) line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        line.Append(Environment.NewLine);

        lock (_lock)
        {
            try { File.AppendAllText(Path.Combine(_dir, $"mobimate-{DateTime.Now:yyyyMMdd}.log"), line.ToString()); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose() { }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
