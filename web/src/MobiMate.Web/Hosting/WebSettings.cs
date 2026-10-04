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
    };
}
