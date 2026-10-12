using System.Text.Json;

namespace MobiMateHomeWork.Hosting;

/// <summary>
/// homework_app.json — 숙제 앱만 쓰는 값. 웹앱과 공유하는 파일이 아니므로 공유 잠금은 쓰지 않는다(JsonFileStore의 원자 저장만).
/// CLI 경로, 창 위치·크기 자리, 항상 위, 닫으면 트레이, 캐릭터별 마지막 맵·가방 최대 무게.
/// </summary>
public sealed class AppSettingsData
{
    public string? CliPath { get; set; }

    // 창 위치·크기: 물리 픽셀(가상 화면 좌표), 일반(최대화 아닌) 상태의 값. 없으면 첫 실행 규칙(작업 영역 4분할, 오른쪽 위).
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    public bool AlwaysOnTop { get; set; }
    public bool CloseToTray { get; set; } = true;

    /// <summary>"트레이에서 계속 확인합니다" 안내 풍선을 보여 줬는가(처음 숨길 때 한 번만).</summary>
    public bool TrayHintShown { get; set; }

    // 앱 배경 그림: official(공식 일러스트 6개 중 BackgroundId) / custom(내 그림 BackgroundImagePath) / off. 투명도는 5~40%.
    public string BackgroundMode { get; set; } = BackgroundSettings.Official;
    public string BackgroundId { get; set; } = Images.BackgroundCatalog.DefaultId;
    public string? BackgroundImagePath { get; set; }
    public double BackgroundOpacity { get; set; } = BackgroundSettings.DefaultOpacity;

    /// <summary>캐릭터 키별 부가 정보. 게임 기록(character_history_db.json)에 없는 값만 둔다.</summary>
    public Dictionary<string, CharacterAppInfo> Characters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>배경 그림 설정의 상수와 보정.</summary>
public static class BackgroundSettings
{
    public const string Official = "official", Custom = "custom", Off = "off";
    public const double MinOpacity = 0.05, MaxOpacity = 0.40, DefaultOpacity = 0.35;

    public static double ClampOpacity(double v) => double.IsNaN(v) || double.IsInfinity(v) ? DefaultOpacity : Math.Clamp(v, MinOpacity, MaxOpacity);
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
    private AppSettingsData _current = null!;

    /// <param name="retries">잠김(Failed)일 때 다시 읽는 횟수</param>
    /// <param name="retryDelay">재시도 간격(기본 50ms)</param>
    public AppSettingsStore(string storageDir, ILogger<AppSettingsStore>? log = null, int retries = 3, TimeSpan? retryDelay = null)
    {
        _path = Path.Combine(storageDir, FileName);
        _log = log;
        var delay = retryDelay ?? TimeSpan.FromMilliseconds(50);
        for (var i = 0; ; i++)
        {
            var status = _store.TryLoad<AppSettingsData>(_path, out var data);
            if (status != LoadStatus.Failed)
            {
                _current = Normalize(data);
                IsLoaded = true;
                break;
            }
            if (i >= retries)
            {
                // 계속 못 읽는다: 기본값으로 보여 주되 "로드 안 됨"으로 두고, 저장할 때 파일을 다시 읽어 병합한다(읽기 실패면 저장 보류)
                _current = Normalize(null);
                _log?.LogWarning("앱 설정 파일을 읽지 못해 기본값으로 시작합니다(저장은 읽을 수 있을 때까지 보류): {Path}", _path);
                break;
            }
            if (delay > TimeSpan.Zero) Thread.Sleep(delay);
        }
    }

    public string FilePath => _path;

    /// <summary>파일을 읽어 메모리에 올렸는가. false면 기본값이 보이는 상태이고, 저장은 파일을 다시 읽을 수 있을 때까지 보류된다.</summary>
    public bool IsLoaded { get; private set; }

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
    /// 변경을 적용하고 <b>바뀐 때만</b> 저장한다. 저장하기 전에 파일을 다시 읽어 그 위에 변경을 얹는다(다시 읽지 못하면 저장하지 않고 false).
    /// 저장에 실패해도 false이고 메모리 값은 그대로다. 바뀐 게 없으면 true(저장 없음).
    /// </summary>
    public bool Update(Action<AppSettingsData> change)
    {
        lock (_lock)
        {
            AppSettingsData baseData;
            switch (_store.TryLoad<AppSettingsData>(_path, out var disk))
            {
                case LoadStatus.Failed:
                    _log?.LogDebug("앱 설정 파일을 읽지 못해 저장을 보류합니다: {Path}", _path);
                    return false;
                case LoadStatus.Loaded:
                    baseData = Normalize(disk);
                    break;
                default:   // 없음·격리: 읽은 적이 있으면 메모리 값을 유지, 처음이면 기본값
                    baseData = IsLoaded ? Clone(_current) : Normalize(null);
                    break;
            }
            var baseJson = JsonSerializer.Serialize(baseData, Compact);
            var next = Clone(baseData);
            change(next);
            next = Normalize(next);
            var json = JsonSerializer.Serialize(next, Compact);
            if (json != baseJson && !_store.Save(_path, next))
            {
                _log?.LogWarning("앱 설정을 저장하지 못했습니다: {Path}", _path);
                return false;
            }
            _current = next;
            IsLoaded = true;
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

    /// <summary>캐릭터 합치기: from의 부가 정보(마지막 맵·가방 최대 무게)를 into에 옮기고 from을 지운다. into에 이미 있으면 into 값을 둔다.</summary>
    public bool MoveCharacter(string from, string into) => Update(d =>
    {
        if (!d.Characters.Remove(from, out var info)) return;
        if (!d.Characters.TryGetValue(into, out var cur)) d.Characters[into] = info;
        else
        {
            cur.LastMap ??= info.LastMap;
            cur.WeightMax ??= info.WeightMax;
        }
    });

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
        if (d.BackgroundMode is not (BackgroundSettings.Official or BackgroundSettings.Custom or BackgroundSettings.Off)) d.BackgroundMode = BackgroundSettings.Official;
        if (Images.BackgroundCatalog.Find(d.BackgroundId) == null) d.BackgroundId = Images.BackgroundCatalog.DefaultId;
        d.BackgroundOpacity = BackgroundSettings.ClampOpacity(d.BackgroundOpacity);
        d.Characters = new Dictionary<string, CharacterAppInfo>(d.Characters ?? new(), StringComparer.OrdinalIgnoreCase);
        return d;
    }

    private static AppSettingsData Clone(AppSettingsData d) =>
        Normalize(JsonSerializer.Deserialize<AppSettingsData>(JsonSerializer.Serialize(d, Compact)));
}
