using System.Collections.Concurrent;
using System.Text;

namespace MobiMateHomeWork.Hosting;

/// <summary>
/// 일 단위 파일 로그: &lt;저장 폴더&gt;\logs\homework-yyyyMMdd.log, 7일 보관. 웹앱 로그(mobimate-*.log)와 같은 폴더에 두되 파일 이름으로 구분한다.
/// 쓰기 실패는 무시한다(로그 때문에 앱이 멈추지 않게).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const int KeepDays = 7;
    public const string Prefix = "homework-";

    private readonly string _dir;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string dir)
    {
        _dir = dir;
        try
        {
            Directory.CreateDirectory(_dir);
            foreach (var f in Directory.GetFiles(_dir, Prefix + "*.log"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-KeepDays)) File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public string LogDirectory => _dir;

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
            try { File.AppendAllText(Path.Combine(_dir, $"{Prefix}{DateTime.Now:yyyyMMdd}.log"), line.ToString()); }
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
