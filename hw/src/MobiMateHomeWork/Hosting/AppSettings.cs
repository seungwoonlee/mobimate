using System.Text.Json;

namespace MobiMateHomeWork.Hosting;

/// <summary>
/// homework_app.json — 숙제 앱만 쓰는 값. 웹앱과 공유하는 파일이 아니므로 공유 잠금은 쓰지 않는다(JsonFileStore의 원자 저장만).
/// CLI 경로, 창 위치·크기 자리, 항상 위, 닫으면 트레이, 캐릭터별 마지막 맵·가방 최대 무게.
/// </summary>
public sealed class AppSettingsData
{
    public string? CliPath { get; set; }

    // 창 위치·크기 (H3에서 채운다. 없으면 첫 실행 규칙으로 4분할 위치)
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    public bool AlwaysOnTop { get; set; }
    public bool CloseToTray { get; set; } = true;

    /// <summary>캐릭터 키별 부가 정보. 게임 기록(character_history_db.json)에 없는 값만 둔다.</summary>
    public Dictionary<string, CharacterAppInfo> Characters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>캐릭터 기록에 없는 값: 마지막으로 본 현재 맵(get_current_environment)과 가방 최대 무게(Vitals.InventoryWeightMax).</summary>
public sealed class CharacterAppInfo
{
    public string? LastMap { get; set; }
    public double? WeightMax { get; set; }
}

public sealed class AppSettingsStore
{
    public const string FileName = "homework_app.json";
    public const string WebSettingsFileName = "web_settings.json";

    private static readonly JsonSerializerOptions Compact = new();

    private readonly JsonFileStore _store = new();
    private readonly string _path;
    private readonly object _lock = new();
    private readonly ILogger<AppSettingsStore>? _log;
    private AppSettingsData _current;
    private string _json;

    public AppSettingsStore(string storageDir, ILogger<AppSettingsStore>? log = null)
    {
        _path = Path.Combine(storageDir, FileName);
        _log = log;
        _current = Normalize(_store.Load<AppSettingsData>(_path));
        _json = JsonSerializer.Serialize(_current, Compact);
    }

    public string FilePath => _path;

    /// <summary>현재 설정의 복사본.</summary>
    public AppSettingsData Current
    {
        get { lock (_lock) return Clone(_current); }
    }

    public CharacterAppInfo? CharacterInfo(string key)
    {
        lock (_lock) return _current.Characters.TryGetValue(key, out var c) ? new CharacterAppInfo { LastMap = c.LastMap, WeightMax = c.WeightMax } : null;
    }

    /// <summary>
    /// 변경을 적용하고 <b>바뀐 때만</b> 저장한다. 저장에 실패하면 false이고 메모리 값도 바꾸지 않는다. 바뀐 게 없으면 true(저장 없음).
    /// </summary>
    public bool Update(Action<AppSettingsData> change)
    {
        lock (_lock)
        {
            var next = Clone(_current);
            change(next);
            next = Normalize(next);
            var json = JsonSerializer.Serialize(next, Compact);
            if (json == _json) return true;
            if (!_store.Save(_path, next))
            {
                _log?.LogWarning("앱 설정을 저장하지 못했습니다: {Path}", _path);
                return false;
            }
            _current = next;
            _json = json;
            return true;
        }
    }

    /// <summary>
    /// 접속 중인 캐릭터를 읽을 때 현재 맵·가방 최대 무게를 남긴다. 빈 값은 무시한다(잠깐 비어 읽힌 값으로 기존 값을 지우지 않게).
    /// 바뀐 때만 저장한다.
    /// </summary>
    public bool RememberCharacter(string key, string? map, double? weightMax)
    {
        var newMap = string.IsNullOrWhiteSpace(map) ? null : map.Trim();
        var newMax = weightMax is > 0 ? weightMax : null;
        if (newMap == null && newMax == null) return true;
        return Update(d =>
        {
            if (!d.Characters.TryGetValue(key, out var info)) d.Characters[key] = info = new CharacterAppInfo();
            if (newMap != null) info.LastMap = newMap;
            if (newMax != null) info.WeightMax = newMax;
        });
    }

    /// <summary>
    /// CLI 경로를 정한다: 실행 인자·환경변수(options) → homework_app.json → web_settings.json(<b>읽기만</b>). 모두 없으면 null
    /// (GameCli가 환경변수 MABINOGI_MOBILE_CLI → 기본 경로 순으로 정한다).
    /// </summary>
    public static string? ResolveCliPath(AppOptions options, AppSettingsStore settings)
    {
        if (!string.IsNullOrWhiteSpace(options.CliPath)) return options.CliPath;
        if (!string.IsNullOrWhiteSpace(settings.Current.CliPath)) return settings.Current.CliPath;
        return ReadWebCliPath(Path.GetDirectoryName(settings._path)!);
    }

    /// <summary>웹앱 설정 파일의 CLI 경로. JsonFileStore.Load는 손상 파일을 격리(이름 변경)하므로 쓰지 않고, 직접 읽기만 한다.</summary>
    internal static string? ReadWebCliPath(string storageDir)
    {
        try
        {
            var path = Path.Combine(storageDir, WebSettingsFileName);
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Name.Equals("CliPath", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return string.IsNullOrWhiteSpace(p.Value.GetString()) ? null : p.Value.GetString();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static AppSettingsData Normalize(AppSettingsData? d)
    {
        d ??= new AppSettingsData();
        d.Characters = new Dictionary<string, CharacterAppInfo>(d.Characters ?? new(), StringComparer.OrdinalIgnoreCase);
        return d;
    }

    private static AppSettingsData Clone(AppSettingsData d) =>
        Normalize(JsonSerializer.Deserialize<AppSettingsData>(JsonSerializer.Serialize(d, Compact)));
}
