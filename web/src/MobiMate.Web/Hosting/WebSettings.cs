namespace MobiMate.Web.Hosting;

/// <summary>web_settings.json (FR-ST-02). 서버 공통 설정. 기기별 설정(테마·글자 크기)은 브라우저에 둔다.</summary>
public sealed class WebSettings
{
    public string? AiEngineId { get; set; }
    public int MaxRefreshSec { get; set; } = 30;
    public bool AutoEmoteDefault { get; set; } = true;
    public string? CliPath { get; set; }
    public bool LanEnabled { get; set; }

    /// <summary>현재 아무말 페르소나: 기본 5종 이름 또는 "custom:&lt;id&gt;" (FR-MB-15, 기기 사이 공유)</summary>
    public string ChatterPersona { get; set; } = "Villainess";

    // ── 음성 입력 (FR-VC) ──
    /// <summary>확정 즉시 게임 채팅으로 보낸다 (기본 끔: 오인식이 그대로 나가므로)</summary>
    public bool SttAutoSend { get; set; }

    /// <summary>폰·태블릿(LAN 기기)이 음성 듣기를 시작할 수 있는가 (기본 끔: 무인증 LAN에서 마이크를 켜는 것을 막는다, FR-VC-10)</summary>
    public bool SttAllowLanStart { get; set; }

    /// <summary>한 번 듣는 최대 시간(초)</summary>
    public int SttMaxSeconds { get; set; } = 15;

    /// <summary>마이크 고유 ID. 없으면 시스템 기본 마이크</summary>
    public string? SttDeviceId { get; set; }

    /// <summary>인식 스레드 수 (기본 1: 게임 부하를 줄인다, FR-VC-06)</summary>
    public int SttThreads { get; set; } = 1;

    /// <summary>전역 단축키 (예: "Ctrl+Alt+V", "F9"). 없으면 단축키 없음</summary>
    public string? SttHotkey { get; set; }

    /// <summary>"hold"(누르는 동안 듣기, 기본) 또는 "toggle"(눌러서 켜고 끄기)</summary>
    public string SttHotkeyMode { get; set; } = "hold";

    /// <summary>듣기 시작·문장 확정 신호음 (게임 전체 화면에서 소리로 알린다)</summary>
    public bool SttCue { get; set; } = true;

    /// <summary>실험: 자주 쓰는 낱말(3회 이상)을 인식기의 핫워드로 넣는다. 기본 끔 — P0 측정에서 일부 낱말만 고쳐졌고 점수를 올리면 문장이 깨졌다. 인식기를 만들 때 한 번 읽으므로 앱을 다시 켜야 적용된다</summary>
    public bool SttHotwords { get; set; }

    /// <summary>인식 글자에 띄어쓰기를 넣어 본다 (FR-VC-09). 기본 끔 — 처음 보는 문장에서는 정확도가 높지 않아(경계 정밀도 약 83%) 선택 기능이다</summary>
    public bool SttSpacing { get; set; }
}

public sealed class WebSettingsStore
{
    public const string FileName = "web_settings.json";
    private readonly JsonFileStore _store = new();
    private readonly string _path;
    private readonly object _lock = new();
    private WebSettings _current;

    public WebSettingsStore(string storageDir)
    {
        _path = Path.Combine(storageDir, FileName);
        _current = _store.Load<WebSettings>(_path) ?? new WebSettings();
    }

    public WebSettings Current
    {
        get { lock (_lock) return Clone(_current); }
    }

    /// <summary>변경을 적용하고 저장한다. 저장에 실패하면 false이고 메모리 값도 바꾸지 않는다.</summary>
    public bool Update(Action<WebSettings> change)
    {
        lock (_lock)
        {
            var next = Clone(_current);
            change(next);
            if (!_store.Save(_path, next)) return false;
            _current = next;
            return true;
        }
    }

    private static WebSettings Clone(WebSettings s) => new()
    {
        AiEngineId = s.AiEngineId,
        MaxRefreshSec = s.MaxRefreshSec,
        AutoEmoteDefault = s.AutoEmoteDefault,
        CliPath = s.CliPath,
        LanEnabled = s.LanEnabled,
        ChatterPersona = s.ChatterPersona,
        SttAutoSend = s.SttAutoSend,
        SttAllowLanStart = s.SttAllowLanStart,
        SttMaxSeconds = s.SttMaxSeconds,
        SttDeviceId = s.SttDeviceId,
        SttThreads = s.SttThreads,
        SttHotkey = s.SttHotkey,
        SttHotkeyMode = s.SttHotkeyMode,
        SttCue = s.SttCue,
        SttHotwords = s.SttHotwords,
        SttSpacing = s.SttSpacing,
    };
}
