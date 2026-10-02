using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using MobiMate.Web.Lan;
using WinForms = System.Windows.Forms;

namespace MobiMate.Web.Hosting;

/// <summary>기본 브라우저로 화면을 연다. 트레이·두 번째 실행·기동 작업이 같이 쓴다.</summary>
public sealed class BrowserLauncher(ServerIdentity identity, ILogger<BrowserLauncher> log)
{
    public string Url(string fragment = "") => $"http://127.0.0.1:{identity.Port}/{fragment}";

    public void Open(string fragment = "")
    {
        var url = Url(fragment);
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { log.LogWarning(ex, "브라우저를 열지 못했습니다. 직접 여세요: http://127.0.0.1:{Port}", identity.Port); }
    }
}

/// <summary>
/// 단일 인스턴스 (상세설계 §3.1): 명명 뮤텍스로 첫 인스턴스를 가리고, 두 번째 실행은 명명 파이프로 "open-browser"를 보내고 끝난다.
/// 호스트를 만들기 전에 판단한다.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string OpenBrowserCommand = "open-browser";
    private static readonly string Suffix = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    public static string MutexName => $@"Local\MobiMateWeb.Single.{Suffix}";
    /// <summary>파이프는 세션 구분이 없으므로 뮤텍스(Local\, 세션별)와 맞추려고 세션 번호를 붙인다.</summary>
    public static string PipeName => $"MobiMateWeb.Pipe.{Suffix}.{Process.GetCurrentProcess().SessionId}";

    private readonly Mutex _mutex;
    public bool IsFirst { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        IsFirst = created;
    }

    /// <summary>이미 실행 중인 인스턴스에 브라우저 열기를 부탁한다. 실패하면 false.</summary>
    public static bool SignalExisting()
    {
        // 첫 인스턴스가 아직 파이프를 열기 전일 수 있으므로 몇 번 다시 시도한다. 응답은 받지 않는다(기동 코드를 파이프로 넘기지 않음, SEC-06).
        for (var i = 0; i < 5; i++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                pipe.Connect(1000);
                pipe.Write(Encoding.UTF8.GetBytes(OpenBrowserCommand));
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                Thread.Sleep(500);
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (IsFirst) try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}

/// <summary>두 번째 실행이 보낸 명령을 받는다 (첫 인스턴스에서만 동작).</summary>
public sealed class InstancePipeListener(BrowserLauncher browser, MobiMateOptions options, ILogger<InstancePipeListener> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.SingleInstance) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(SingleInstance.PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                var buf = new byte[64];
                var n = await pipe.ReadAsync(buf, ct);
                if (Encoding.UTF8.GetString(buf, 0, n) == SingleInstance.OpenBrowserCommand) browser.Open();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // 다른 계정이 이름을 선점했거나(UnauthorizedAccess) 인스턴스가 바쁘면 잠시 쉬고 다시 연다. 앱을 멈추지 않는다.
                log.LogDebug(ex, "인스턴스 파이프 오류");
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}

/// <summary>
/// 트레이 아이콘 (상세설계 §3.1): 브라우저 열기 · 📱 폰으로 보기 · LAN 모드 · 로그 폴더 · 종료.
/// WinForms 메시지 루프는 전용 STA 스레드에서 돈다. 서버 종료 시 아이콘을 치운다.
/// </summary>
public sealed class TrayHost(
    BrowserLauncher browser, LanService lan, ServerIdentity identity, MobiMateOptions options,
    IHostApplicationLifetime lifetime, ILogger<TrayHost> log) : IHostedService
{
    private Thread? _thread;
    private WinForms.ApplicationContext? _context;
    private WinForms.NotifyIcon? _icon;
    private WinForms.ToolStripMenuItem? _lanItem;
    private readonly ManualResetEventSlim _ready = new();

    public Task StartAsync(CancellationToken ct)
    {
        if (!options.Tray) return Task.CompletedTask;
        _thread = new Thread(Run) { IsBackground = true, Name = "MobiMate Tray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5), ct);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        var ctx = _context;
        var icon = _icon;
        if (ctx != null && icon != null)
        {
            try
            {
                icon.ContextMenuStrip?.Invoke(() =>
                {
                    icon.Visible = false;
                    icon.Dispose();
                    ctx.ExitThread();
                });
            }
            catch (InvalidOperationException) { }
        }
        _thread?.Join(TimeSpan.FromSeconds(2));
        return Task.CompletedTask;
    }

    private void Run()
    {
        try
        {
            WinForms.Application.EnableVisualStyles();
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("브라우저 열기", null, (_, _) => browser.Open());
            menu.Items.Add("📱 폰으로 보기", null, async (_, _) => await Safe(async () =>
            {
                await lan.SetEnabledAsync(true, CancellationToken.None);
                browser.Open("#/pair");
            }));
            _lanItem = new WinForms.ToolStripMenuItem("LAN 모드 (폰·태블릿 접속)") { CheckOnClick = false };
            _lanItem.Click += async (_, _) => await Safe(() => lan.SetEnabledAsync(!lan.Status.Enabled, CancellationToken.None));
            menu.Items.Add(_lanItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("로그 폴더 열기", null, (_, _) => OpenFolder(Path.Combine(options.StorageDir, "logs")));
            menu.Items.Add("종료", null, (_, _) => RequestExit());
            menu.Opening += (_, _) => RefreshMenu();
            _ = menu.Handle;   // 이 스레드에 핸들을 만들어 두어 Invoke가 동작하게 한다

            _icon = new WinForms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = TooltipText(),
                ContextMenuStrip = menu,
                Visible = true,
            };
            _icon.DoubleClick += (_, _) => browser.Open();
            _context = new WinForms.ApplicationContext();
            _ready.Set();
            WinForms.Application.Run(_context);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "트레이 아이콘을 만들지 못했습니다.");
            _ready.Set();
        }
    }

    /// <summary>
    /// 종료 요청. 정상 종료가 10초 안에 끝나지 않으면(응답 없는 작업·연결이 붙잡을 때) 프로세스를 강제로 끝낸다.
    /// 저장은 변경 때마다 하므로 잃는 데이터는 없다.
    /// </summary>
    private void RequestExit()
    {
        log.LogInformation("트레이에서 종료를 요청했습니다.");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            log.LogWarning("정상 종료가 끝나지 않아 프로세스를 강제로 끝냅니다.");
            Environment.Exit(0);
        });
        lifetime.StopApplication();
    }

    /// <summary>WinForms 이벤트의 async void 처리기에서 예외가 새면 프로세스가 죽는다. 로그로만 남긴다.</summary>
    private async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { log.LogWarning(ex, "트레이 메뉴 동작 실패"); }
    }

    private void RefreshMenu()
    {
        var s = lan.Status;
        if (_lanItem != null)
        {
            _lanItem.Checked = s.Enabled;
            _lanItem.Text = s.Active ? $"LAN 모드 · {s.MdnsName ?? s.Addresses.FirstOrDefault()}:{s.Port}" : s.Enabled ? $"LAN 모드 (대기: {Reason(s.Error)})" : "LAN 모드 (폰·태블릿 접속)";
        }
        if (_icon != null) _icon.Text = TooltipText();
    }

    private string TooltipText()
    {
        var text = $"MobiMate Web {identity.Version} · 127.0.0.1:{identity.Port}{(lan.Status.Active ? " · LAN" : "")}";
        return text.Length > 63 ? text[..63] : text;   // NotifyIcon 제한
    }

    internal static string Reason(LanError e) => e switch
    {
        LanError.NotPrivate => "공용 네트워크",
        LanError.ProfileUnknown => "네트워크 확인 실패",
        LanError.NoAddress => "네트워크 없음",
        LanError.PortInUse => "포트 사용 중",
        LanError.PortReserved => "포트 예약됨",
        LanError.BindFailed => "연결 실패",
        LanError.Pending => "네트워크 확인 중",
        LanError.PortChanged => "포트 변경됨",
        _ => "준비 중",
    };

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon) return icon;
        }
        catch (Exception) { }
        return System.Drawing.SystemIcons.Application;
    }

    private void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex) { log.LogWarning(ex, "폴더를 열지 못했습니다: {Dir}", dir); }
    }
}
