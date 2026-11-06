using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MobiMate.Web.Hosting;

namespace MobiMate.Web.Voice;

/// <summary>
/// 인식 결과를 보내기 전에 한 번 다듬는다 (승운 2026-11-07): 잘못 알아들은 낱말을 고치고 띄어쓰기를 넣는다.
/// 실패·시간 초과·이상한 결과면 null — 호출한 쪽이 사전 방식 결과를 그대로 쓴다.
/// </summary>
public interface IVoiceRefiner
{
    /// <summary>켜져 있고 모델이 정해져 있는가</summary>
    bool Enabled { get; }
    Task<string?> RefineAsync(string text, CancellationToken ct);
    /// <summary>모델을 미리 메모리에 올린다 (첫 문장이 느리지 않게). 실패해도 조용히 넘어간다.</summary>
    Task WarmUpAsync();
    RefinerStatus Status { get; }
}

public sealed record RefinerStatus(bool Enabled, string? Model, bool Cloud, string? LastError, int? LastMs, bool Skipping);

/// <summary>AI가 돌려준 문장을 믿을지 판정한다. 말하지 않은 내용을 지어내거나 설명을 붙이면 버린다.</summary>
public static class RefineGuard
{
    public const int MaxInput = 120;
    private const double MaxLengthChange = 0.30;
    private const double MaxEditShare = 0.40;

    private static bool IsPunct(char c) => c is '.' or ',' or '?' or '!' or '~' or '…' or '"' or '\'' or '`' or '“' or '”' or '‘' or '’';

    private static string NoSpace(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c) && !IsPunct(c)));

    /// <summary>받아들이면 정리된 한 줄, 아니면 null</summary>
    public static string? Accept(string input, string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var line = output.Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line == null) return null;
        // 음성 인식 글자에는 문장부호가 없다. 모델이 붙인 것은 지운다.
        line = string.Concat(line.Where(c => !IsPunct(c)));
        line = string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (line.Length == 0) return null;

        // 입력에 없던 종류의 글자(영문 설명, 이모지, 마크다운 등)는 거부한다. 한글·숫자·공백만 허용하고, 입력에 있던 글자는 그대로 둔다.
        var allowed = new HashSet<char>(input);
        foreach (var c in line)
        {
            var hangul = c is >= '가' and <= '힣';
            if (!(hangul || char.IsDigit(c) || c == ' ' || allowed.Contains(c))) return null;
        }

        var a = NoSpace(input);
        var b = NoSpace(line);
        if (a.Length == 0 || b.Length == 0) return null;
        if (Math.Abs(b.Length - a.Length) > Math.Max(2, a.Length * MaxLengthChange)) return null;
        if (Distance(a, b) > Math.Max(2, Math.Ceiling(Math.Max(a.Length, b.Length) * MaxEditShare))) return null;
        return line;
    }

    /// <summary>글자 단위 편집 거리</summary>
    internal static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}

/// <summary>
/// 이 PC의 Ollama(127.0.0.1:11434)로 교정한다. 모델은 설정에서 고른다(기본 gemma4:e4b: 측정에서 내용 29/30, 중앙값 0.17초).
/// 이름이 cloud로 끝나는 모델은 Ollama 클라우드로 말한 내용이 나가므로 설정 화면이 경고하고, 자주 쓰는 낱말 목록은 보내지 않는다.
/// 시간 제한을 넘기거나 실패하면 잠시(20초) 쉬어 매 문장이 기다리지 않게 한다.
/// </summary>
public sealed class OllamaVoiceRefiner(WebSettingsStore settings, VoiceVocabulary vocab, ILogger<OllamaVoiceRefiner> log) : IVoiceRefiner
{
    public const string DefaultModel = "gemma4:e4b";
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(20);
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("http://127.0.0.1:11434/"), Timeout = Timeout.InfiniteTimeSpan };

    private const string SystemPrompt =
        "너는 마비노기 모바일 게임 파티 채팅용 음성 인식 결과 교정기다. 입력은 한국어 음성 인식 결과이며 띄어쓰기가 거의 없고 낱말이 잘못 인식된 곳이 있다.\n" +
        "규칙: 1) 띄어쓰기를 자연스럽게 넣는다. 2) 게임 용어를 잘못 알아들은 낱말만 고친다(힐러, 탱커, 딜러, 보스, 장판, 레이드, 파티, 페이즈, 광역기, 쿨타임, 버프, 거래소, 경매, 길드, 드롭, 클리어, 주간 숙제). " +
        "3) 없는 내용을 더하거나 말투를 바꾸거나 문장을 줄이지 않는다. 4) 마침표·쉼표·물음표 같은 문장부호를 넣지 않는다. 5) 어미(~요, ~니다 등)를 바꾸지 않는다. " +
        "6) 교정한 문장 한 줄만 출력한다. 설명·따옴표·이모지 금지.";

    private readonly object _lock = new();
    private DateTime _pausedUntil = DateTime.MinValue;
    private string? _lastError;
    private int? _lastMs;

    public static bool IsCloud(string? model) => model != null && (model.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) || model.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase));

    private string Model => string.IsNullOrWhiteSpace(settings.Current.SttRefineModel) ? DefaultModel : settings.Current.SttRefineModel!;
    public bool Enabled => settings.Current.SttRefine;

    public RefinerStatus Status
    {
        get
        {
            lock (_lock) return new(Enabled, Model, IsCloud(Model), _lastError, _lastMs, DateTime.UtcNow < _pausedUntil);
        }
    }

    public async Task<string?> RefineAsync(string text, CancellationToken ct)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(text) || text.Length > RefineGuard.MaxInput) return null;
        lock (_lock) { if (DateTime.UtcNow < _pausedUntil) return null; }
        var model = Model;
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(settings.Current.SttRefineMs, 500, 10000));
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var messages = new List<object>
            {
                new { role = "system", content = SystemPrompt + Glossary(model) },
                new { role = "user", content = "탄커님어디계세요" }, new { role = "assistant", content = "탱커님 어디 계세요" },
                new { role = "user", content = "딜러가부족해요" }, new { role = "assistant", content = "딜러가 부족해요" },
                new { role = "user", content = text },
            };
            var body = new { model, stream = false, think = false, keep_alive = "30m", options = new { temperature = 0, num_predict = 120 }, messages };
            using var res = await Http.PostAsJsonAsync("api/chat", body, cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                Fail(res.StatusCode == System.Net.HttpStatusCode.NotFound ? $"모델 '{model}'이 설치되어 있지 않습니다" : $"Ollama 오류 {(int)res.StatusCode}", pause: true);
                return null;
            }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cts.Token));
            var content = doc.RootElement.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c) ? c.GetString() : null;
            var accepted = RefineGuard.Accept(text, content);
            lock (_lock) { _lastMs = (int)sw.ElapsedMilliseconds; _lastError = accepted == null ? "AI 결과가 믿을 수 없어 사전 방식으로 보냈습니다" : null; }
            return accepted;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Fail($"{timeout.TotalSeconds:0.#}초 안에 끝나지 않았습니다", pause: true);
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException)
        {
            Fail("Ollama가 켜져 있지 않습니다", pause: true);
            return null;
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "음성 교정 오류");
            Fail("교정 중 오류가 났습니다", pause: true);
            return null;
        }
    }

    public async Task WarmUpAsync()
    {
        if (!Enabled) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var res = await Http.PostAsJsonAsync("api/generate", new { model = Model, prompt = "", keep_alive = "30m", stream = false }, cts.Token);
            lock (_lock)
            {
                if (res.IsSuccessStatusCode) { _lastError = null; _pausedUntil = DateTime.MinValue; }
                else _lastError = res.StatusCode == System.Net.HttpStatusCode.NotFound ? $"모델 '{Model}'이 설치되어 있지 않습니다" : $"Ollama 오류 {(int)res.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "음성 교정 모델 미리 올리기 실패");
            lock (_lock) _lastError = "Ollama가 켜져 있지 않습니다";
        }
    }

    private void Fail(string why, bool pause)
    {
        lock (_lock)
        {
            _lastError = why;
            if (pause) _pausedUntil = DateTime.UtcNow + Pause;
        }
        log.LogInformation("음성 교정을 건너뜁니다: {Why}", why);
    }

    /// <summary>자주 쓰는 낱말(학습)을 힌트로 준다. 클라우드 모델에는 보내지 않는다.</summary>
    private string Glossary(string model)
    {
        if (IsCloud(model)) return "";
        try
        {
            var words = vocab.WordCounts().OrderByDescending(w => w.Value).Where(w => w.Value >= 3 && w.Key.Length is >= 2 and <= 6).Take(30).Select(w => w.Key).ToList();
            return words.Count == 0 ? "" : "\n자주 쓰는 낱말: " + string.Join(", ", words);
        }
        catch { return ""; }
    }
}
