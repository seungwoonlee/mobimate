using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MobiMate;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DispatcherUnhandledException", e.Exception);

        // 치명적 시스템 자원 고갈이 아닌 일반 UI 바인딩/렌더링 예외는 프로세스 종료를 방어
        if (e.Exception is not (OutOfMemoryException or StackOverflowException))
        {
            e.Handled = true;
        }
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash("CurrentDomain_UnhandledException", ex);
        }
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "MobiMate");
            Directory.CreateDirectory(dir);

            var logPath = Path.Combine(dir, "crash_ui.log");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex.GetType().FullName}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);

            var inner = ex.InnerException;
            while (inner != null)
            {
                sb.AppendLine($"  --> Inner: {inner.GetType().FullName}: {inner.Message}");
                sb.AppendLine(inner.StackTrace);
                inner = inner.InnerException;
            }
            sb.AppendLine();

            File.AppendAllText(logPath, sb.ToString());
        }
        catch
        {
            // 로깅 실패로 2차 예외 발생 방지
        }
    }
}

