using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using WinForms = System.Windows.Forms;

namespace MobiMate.Web.Voice;

/// <summary>전역 단축키 하나: 수정키 비트(Alt=1·Ctrl=2·Shift=4·Win=8)와 가상 키 코드</summary>
public sealed record HotkeySpec(uint Modifiers, uint VirtualKey, string Display);

/// <summary>"Ctrl+Alt+V", "F9" 같은 글을 단축키로 바꾼다 (FR-VC-02)</summary>
public static class HotkeyParser
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;

    private static readonly Dictionary<string, (uint Vk, string Name)> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = (0x20, "Space"), ["Insert"] = (0x2D, "Insert"), ["Delete"] = (0x2E, "Delete"), ["Home"] = (0x24, "Home"), ["End"] = (0x23, "End"),
        ["PageUp"] = (0x21, "PageUp"), ["PageDown"] = (0x22, "PageDown"),
        ["Up"] = (0x26, "Up"), ["Down"] = (0x28, "Down"), ["Left"] = (0x25, "Left"), ["Right"] = (0x27, "Right"),
    };

    /// <summary>
    /// 규칙: 주 키는 하나. 기능키(F1~F24)가 아니면 **Ctrl·Alt·Win 중 하나 이상**이 있어야 한다 —
    /// 수정키 없이, 또는 Shift만으로 글자·숫자·스페이스를 등록하면 그 키(대문자·기호 입력)가 모든 프로그램에서 가로채인다.
    /// 올바르지 않으면 null.
    /// </summary>
    public static HotkeySpec? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint mods = 0, vk = 0;
        string keyName = "";
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Ctrl; break;
                case "alt": mods |= Alt; break;
                case "shift": mods |= Shift; break;
                case "win" or "meta" or "cmd": mods |= Win; break;
                default:
                    if (vk != 0 || !TryKey(part, out vk, out keyName)) return null;
                    break;
            }
        }
        if (vk == 0) return null;
        var isFunctionKey = vk is >= 0x70 and <= 0x87;
        if (!isFunctionKey && (mods & (Ctrl | Alt | Win)) == 0) return null;

        var display = string.Concat(
            (mods & Ctrl) != 0 ? "Ctrl+" : "", (mods & Alt) != 0 ? "Alt+" : "",
            (mods & Shift) != 0 ? "Shift+" : "", (mods & Win) != 0 ? "Win+" : "", keyName);
        return new HotkeySpec(mods, vk, display);
    }

    private static bool TryKey(string s, out uint vk, out string name)
    {
        vk = 0; name = "";
        if (s.Length == 1 && char.IsAsciiLetter(s[0])) { vk = char.ToUpperInvariant(s[0]); name = ((char)vk).ToString(); return true; }
        if (s.Length == 1 && char.IsAsciiDigit(s[0])) { vk = s[0]; name = s; return true; }
        if ((s[0] == 'F' || s[0] == 'f') && int.TryParse(s.AsSpan(1), out var n) && n is >= 1 and <= 24) { vk = (uint)(0x70 + n - 1); name = "F" + n; return true; }
        if (Named.TryGetValue(s, out var k)) { vk = k.Vk; name = k.Name; return true; }
        return false;
    }
}

public sealed record HotkeyStatus(string? Key, bool Registered, string? Error);

/// <summary>운영체제에 전역 단축키를 등록한다. 테스트에서는 가짜로 바꾼다.</summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>등록한 단축키가 눌렸다(누름만 온다 — 뗌은 <see cref="IKeyState"/>로 확인한다). 호출 스레드는 짧게 처리해야 한다.</summary>
    event Action? Pressed;

    bool Register(HotkeySpec spec, out string? error);
    void Unregister();
}

public interface IKeyState
{
    bool IsDown(uint virtualKey);
}

/// <summary>
/// 전역 단축키 (FR-VC-02). "누르는 동안 듣기"(기본)는 누름 이벤트만 오므로 뗌을 키 상태 확인으로 감지한다(저수준 훅을 쓰지 않는다).
/// "눌러서 켜고 끄기"는 눌 때마다 시작/중지를 번갈아 한다. 시작에 실패하면(모델 없음·마이크 문제) 모든 기기에 토스트로 알린다 —
/// 게임이 전체 화면이면 사용자가 다른 화면을 볼 수 없다.
/// 교착 방지: 등록기(<see cref="IHotkeyRegistrar"/>) 호출은 창 스레드의 응답을 기다리므로 절대 락 안에서 하지 않는다.
/// 창 스레드가 부르는 <see cref="OnPressed"/>는 락 없이 현재 단축키만 읽는다.
/// </summary>
public sealed class HotkeyService(
    IHotkeyRegistrar registrar, IKeyState keys, SttService stt, ISttEngineProvider engines, WebSettingsStore settings, SseHub hub, ILogger<HotkeyService> log) : IHostedService
{
    private readonly SemaphoreSlim _applyGate = new(1, 1);   // 등록 변경은 한 번에 하나씩
    private readonly object _statusLock = new();             // 상태 값만 지키는 짧은 락 (등록기 호출은 이 안에서 하지 않는다)
    private volatile HotkeySpec? _spec;
    private HotkeyStatus _status = new(null, false, null);

    public HotkeyStatus Status { get { lock (_statusLock) return _status; } }

    /// <summary>단축키를 (다시) 등록한다. 빈 값이면 해제. 등록하지 못해도 예외 없이 상태에 이유를 담는다.</summary>
    public HotkeyStatus Apply(string? key)
    {
        HotkeyStatus status;
        _applyGate.Wait();
        try
        {
            registrar.Unregister();
            _spec = null;
            if (string.IsNullOrWhiteSpace(key)) status = new(null, false, null);
            else if (HotkeyParser.Parse(key) is not { } spec) status = new(key, false, "단축키 형식이 올바르지 않습니다. (예: Ctrl+Alt+V, F9)");
            else if (!registrar.Register(spec, out var error)) status = new(spec.Display, false, error ?? "단축키를 등록하지 못했습니다.");
            else
            {
                _spec = spec;
                status = new(spec.Display, true, null);
            }
            lock (_statusLock) _status = status;
        }
        finally
        {
            _applyGate.Release();
        }
        // 처음 누를 때 모델을 올리느라 첫 문장을 놓치지 않게 미리 올려 둔다 (등록에 성공한 단축키가 있을 때만)
        if (status.Registered) _ = engines.WarmUpAsync();
        return status;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        registrar.Pressed += OnPressed;
        Apply(settings.Current.SttHotkey);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        registrar.Pressed -= OnPressed;
        registrar.Unregister();
        return Task.CompletedTask;
    }

    private void OnPressed()
    {
        var spec = _spec;   // 락 없이 읽는다: 창 스레드를 붙잡지 않는다
        if (spec == null) return;
        var mode = settings.Current.SttHotkeyMode;
        _ = Task.Run(() => HandleAsync(spec, mode));
    }

    internal async Task HandleAsync(HotkeySpec spec, string mode)
    {
        try
        {
            if (mode == "toggle")
            {
                if (stt.Snapshot().State == SttState.Idle) ReportFailure(stt.Start("hotkey", stopAtEndpoint: true));
                else stt.Stop();
                return;
            }

            // 누르는 동안 듣기: 키를 뗄 때까지 이어 가고, 그 사이 여러 문장을 말해도 각각 확정된다
            if (!keys.IsDown(spec.VirtualKey)) return;   // 눌렀다 바로 뗐다: 시작하지 않는다
            var r = stt.Start("hotkey", stopAtEndpoint: false);
            if (r.Status != SttStartStatus.Started)
            {
                ReportFailure(r);
                return;
            }
            while (keys.IsDown(spec.VirtualKey) && stt.Snapshot().SessionId == r.SessionId) await Task.Delay(15);
            stt.Stop(r.SessionId);   // 내가 시작한 세션만 끝낸다 (그 사이 다른 기기가 시작한 세션은 건드리지 않는다)
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "단축키 처리 중 오류");
        }
    }

    private void ReportFailure(SttStartResult r)
    {
        if (r.Status is SttStartStatus.Started or SttStartStatus.Cancelled or SttStartStatus.Busy) return;
        hub.Broadcast("toast", new { level = "warn", message = SttService.DescribeStartFailure(r) });
    }
}

// ───────── Windows 구현 ─────────

public sealed class Win32KeyState : IKeyState
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public bool IsDown(uint virtualKey) => (GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;
}

/// <summary>
/// RegisterHotKey는 메시지 루프가 있는 스레드에서 등록·해제해야 하므로 전용 STA 스레드에서 숨은 메시지 창으로 받는다.
/// 등록한 조합은 시스템이 가져가므로 게임이 같은 키를 쓸 수 없다. 다른 프로그램이 이미 쓰는 조합이면 등록에 실패한다.
/// 창 스레드가 5초 안에 응답하지 않으면 실패로 보고하고, 늦게 성공했을 수 있는 등록은 해제를 한 번 더 시도한다.
/// </summary>
public sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    private const int WmHotkey = 0x0312, WmApp = 0x8000, HotkeyId = 0x4D4D, ErrorHotkeyAlreadyRegistered = 1409;
    private const uint ModNoRepeat = 0x4000;
    private static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(5);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _ready = new();
    private volatile Window? _window;
    private Thread? _thread;
    private volatile bool _disposed;

    public event Action? Pressed;

    public bool Register(HotkeySpec spec, out string? error)
    {
        var window = EnsureWindow();
        if (window == null) { error = "단축키 창을 만들지 못했습니다."; return false; }
        var ok = false;
        var code = 0;
        var completed = window.Run(() =>
        {
            ok = RegisterHotKey(window.Handle, HotkeyId, spec.Modifiers | ModNoRepeat, spec.VirtualKey);
            if (!ok) code = Marshal.GetLastWin32Error();
        });
        if (!completed)
        {
            // 창 스레드가 늦게 처리해 등록이 뒤늦게 성공했을 수 있다: 결과를 알 수 없으니 해제를 시도하고 실패로 알린다
            window.Run(() => UnregisterHotKey(window.Handle, HotkeyId));
            error = "단축키 창이 응답하지 않아 등록하지 못했습니다. 앱을 다시 켜 보세요.";
            return false;
        }
        error = ok ? null : code == ErrorHotkeyAlreadyRegistered
            ? "다른 프로그램이 이 단축키를 이미 쓰고 있습니다. 다른 조합을 골라 주세요."
            : $"단축키를 등록하지 못했습니다 (오류 {code}).";
        return ok;
    }

    /// <summary>등록 여부와 무관하게 항상 해제를 시도한다(없으면 무해한 실패)</summary>
    public void Unregister()
    {
        var window = _window;
        if (window == null) return;
        window.Run(() => UnregisterHotKey(window.Handle, HotkeyId));
    }

    private Window? EnsureWindow()
    {
        if (_disposed) return null;
        lock (_gate)
        {
            _thread ??= StartThread();
        }
        _ready.Wait(WindowWait);
        return _window;
    }

    private Thread StartThread()
    {
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window(this);
                window.CreateHandle(new WinForms.CreateParams { Parent = new IntPtr(-3) });   // 메시지 전용 창(HWND_MESSAGE)
                _window = window;
                _ready.Set();
                WinForms.Application.Run();
            }
            catch
            {
                _window = null;   // 창을 만들지 못했다: Register가 실패로 알린다
            }
            finally
            {
                _ready.Set();     // 기다리는 쪽이 5초를 다 쓰지 않게
            }
        }) { IsBackground = true, Name = "stt-hotkey" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    public void Dispose()
    {
        if (_disposed) return;
        var window = _window;
        _disposed = true;
        if (window == null) return;
        try
        {
            window.Run(() => UnregisterHotKey(window.Handle, HotkeyId), force: true);
            window.Run(WinForms.Application.ExitThread, force: true);
        }
        catch { }
    }

    private sealed class Window(Win32HotkeyRegistrar owner) : WinForms.NativeWindow
    {
        private readonly ConcurrentQueue<(Action Work, ManualResetEventSlim Done)> _queue = new();

        /// <summary>창 스레드에서 실행하고 끝날 때까지 기다린다. 제때 끝나지 않으면 false.</summary>
        public bool Run(Action work, bool force = false)
        {
            if (owner._disposed && !force) return false;
            if (Environment.CurrentManagedThreadId == owner._thread?.ManagedThreadId) { work(); return true; }
            // 기다리다 시간이 지나도 창 스레드가 나중에 Set()하므로, 이 이벤트는 버리지 않고 GC에 맡긴다(해제하면 늦은 Set이 예외를 낸다)
            var done = new ManualResetEventSlim();
            _queue.Enqueue((work, done));
            if (!PostMessage(Handle, WmApp, IntPtr.Zero, IntPtr.Zero)) return false;
            return done.Wait(WindowWait);
        }

        protected override void WndProc(ref WinForms.Message m)
        {
            if (m.Msg == WmHotkey) owner.Pressed?.Invoke();
            else if (m.Msg == WmApp)
            {
                while (_queue.TryDequeue(out var item))
                {
                    try { item.Work(); }
                    catch { }
                    finally { item.Done.Set(); }
                }
            }
            base.WndProc(ref m);
        }
    }
}
