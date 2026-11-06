using System.Net.Http.Json;
using System.Text.Json;
using MobiMate.Web.Hosting;
using MobiMate.Web.Voice;

namespace MobiMate.Web.Services;

/// <param name="Tone">페르소나 말투로 바꿀지</param>
/// <param name="Emoji">문장에 맞는 이모지를 고를지</param>
/// <param name="PersonaDesc">페르소나 설명(말투 바꿀 때만)</param>
public sealed record ChatStyleRequest(bool Tone, bool Emoji, string PersonaDesc);

/// <param name="Text">말투를 바꾼 문장. null이면 원문을 쓴다(실패·검증 탈락)</param>
/// <param name="Emoji">AI가 고른 이모지(허용 목록 안). null이면 정하지 못함</param>
/// <param name="EmojiNone">AI가 "이모지 없음"을 골랐다 (감정이 뚜렷하지 않은 문장)</param>
public sealed record ChatStyleResult(string? Text, string? Emoji, bool EmojiNone)
{
    public static readonly ChatStyleResult Nothing = new(null, null, false);
    public bool EmojiDecided => Emoji != null || EmojiNone;
}

/// <summary>
/// 보낼 문장을 문맥에 맞는 이모지로 꾸미고 페르소나 말투로 바꾼다 (승운 2026-11-07).
/// 실패·시간 초과·검증 탈락이면 해당 부분은 비워 두고(null) 호출한 쪽이 원문과 키워드 규칙을 쓴다.
/// </summary>
public interface IChatStylist
{
    Task<ChatStyleResult> StyleAsync(string text, ChatStyleRequest req, CancellationToken ct);
}

/// <summary>AI가 돌려준 말투 문장·이모지를 믿을지 판정한다. 팀원에게 가는 말이므로 뜻을 바꾸거나 비꼬면 버린다.</summary>
public static class StyleGuard
{
    private const double MaxGrowth = 1.6;
    private const double MinKeptSyllables = 0.45;

    // 감사·사과·축하처럼 뜻이 뒤집히면 안 되는 표현: 입력에 있으면 같은 무리의 표현이 출력에도 있어야 한다
    private static readonly string[][] MeaningGroups =
    {
        new[] { "감사", "고마", "고맙", "땡큐", "thanks" },
        new[] { "죄송", "미안", "사과" },
        new[] { "축하", "ㅊㅋ" },
        new[] { "수고", "고생" },
        new[] { "부탁" },
        new[] { "안녕", "반가", "환영" },
    };

    // 입력에 있으면 출력에도 그대로 있어야 하는 게임 용어
    private static readonly string[] Glossary =
    {
        "힐러", "탱커", "딜러", "보스", "장판", "레이드", "파티", "페이즈", "광역기", "쿨타임", "버프", "거래소", "경매", "길드", "드롭", "클리어", "포션", "숙제",
    };

    // 입력에 없던 모욕·비하 표현
    private static readonly string[] Insults = { "유치", "천박", "멍청", "바보", "한심", "꺼져", "닥쳐", "못난", "무능" };

    private static bool IsAllowedPunct(char c) => ".,!?~…'\"-:;()".IndexOf(c) >= 0;

    /// <summary>말투를 바꾼 문장을 받아들이면 한 줄, 아니면 null</summary>
    public static string? AcceptTone(string input, string? output, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var line = output.Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line == null) return null;
        foreach (var label in new[] { "문장:", "문장 :" })
            if (line.StartsWith(label, StringComparison.Ordinal)) line = line[label.Length..].Trim();
        line = line.Trim('"', '\'', '`', '“', '”');
        line = string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (line.Length == 0) return null;

        // 이모지·마크다운 등 이질적인 글자 거부 (이모지는 따로 붙인다)
        foreach (var c in line)
            if (!(char.IsLetterOrDigit(c) || c == ' ' || IsAllowedPunct(c))) return null;
        if (ChatText.Count(line) > maxLength) return null;

        var a = Compact(input);
        var b = Compact(line);
        if (a.Length == 0 || b.Length == 0) return null;
        if (b.Length > Math.Max(12, (int)Math.Ceiling(a.Length * MaxGrowth) + 6)) return null;

        // 질문으로 바꾸면 뜻이 틀어진다: "마을에 다녀올게요"가 "다녀오시나요?"(상대에게 묻는 말)가 되거나 감사가 비꼬는 말로 읽힌다.
        if (line.Contains('?') && !input.Contains('?')) return null;

        foreach (var group in MeaningGroups)
            if (group.Any(k => input.Contains(k, StringComparison.OrdinalIgnoreCase)) && !group.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase))) return null;
        foreach (var term in Glossary)
            if (input.Contains(term, StringComparison.Ordinal) && !line.Contains(term, StringComparison.Ordinal)) return null;
        foreach (var bad in Insults)
            if (line.Contains(bad, StringComparison.Ordinal) && !input.Contains(bad, StringComparison.Ordinal)) return null;
        if (!SameNumbers(input, line)) return null;

        // 원문의 글자가 어느 정도는 남아 있어야 한다 (전혀 다른 문장 거부)
        var pool = new Dictionary<char, int>();
        foreach (var c in b) pool[c] = pool.GetValueOrDefault(c) + 1;
        var kept = 0;
        foreach (var c in a)
            if (pool.TryGetValue(c, out var n) && n > 0) { kept++; pool[c] = n - 1; }
        if (kept < a.Length * MinKeptSyllables) return null;
        return line;
    }

    /// <summary>AI의 이모지 답을 해석한다: 허용 목록의 이모지 / "없음" / 모름(null)</summary>
    public static (string? Emoji, bool None) AcceptEmoji(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return (null, false);
        var line = output.Trim();
        foreach (var e in ChatPlanService.AllowedEmojis)
            if (line.Contains(e, StringComparison.Ordinal)) return (e, false);
        if (line.Contains("없음", StringComparison.Ordinal)) return (null, true);
        return (null, false);
    }

    private static string Compact(string s) => string.Concat(s.Where(c => char.IsLetterOrDigit(c)));

    private static bool SameNumbers(string a, string b)
    {
        static IEnumerable<string> Nums(string s) => System.Text.RegularExpressions.Regex.Matches(s, @"\d+").Select(m => m.Value);
        var nb = Nums(b).ToList();
        foreach (var n in Nums(a))
        {
            var i = nb.IndexOf(n);
            if (i < 0) return false;
            nb.RemoveAt(i);
        }
        return true;
    }
}

/// <summary>
/// 이 PC의 Ollama로 말투·이모지를 만든다. 말투와 이모지는 서로 다른 호출을 동시에 보낸다(이모지는 원문으로 고른다: 말투가 섞이지 않아 더 정확하다).
/// 모델은 ChatStyleModel → 음성 다듬기 모델 → gemma4:e4b 순으로 쓴다. 실패 뒤 20초는 시도하지 않는다.
/// </summary>
public sealed class OllamaChatStylist(WebSettingsStore settings, ILogger<OllamaChatStylist> log) : IChatStylist
{
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("http://127.0.0.1:11434/"), Timeout = Timeout.InfiniteTimeSpan };
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(20);

    private const string ToneSystem =
        "너는 마비노기 모바일 게임 전체 채팅에 보낼 문장을 페르소나 말투로 바꿔 쓰는 도우미다.\n" +
        "페르소나: {0}\n" +
        "규칙: 1) 문장의 뜻과 방향을 절대 바꾸지 않는다. 감사·사과·축하·부탁은 그대로 유지한다(감사를 비꼬거나 거절로 바꾸지 않는다). " +
        "2) 숫자·이름·아이템·시간 같은 정보는 그대로 둔다. 3) 상대를 직접 모욕하거나 깎아내리지 않는다. 도도하고 투덜거리는 페르소나라도 감사는 감사로, 축하는 축하로 전하고 내용은 호의적으로 쓴다. " +
        "4) 의문문으로 바꾸지 않는다. 말하는 사람이 \"~할게요\"라고 하면 그대로 말하는 사람의 말로 쓰고 상대에게 묻는 말로 바꾸지 않는다. 평서문은 페르소나의 평서 어미로 끝낸다. 5) 없는 내용을 더하지 않는다. 추임새(예: 오호호, 마, 형들)는 많아야 하나만 쓴다. " +
        "6) 원문보다 너무 길게 늘이지 않는다(원문의 1.5배, 40자 이하). 7) 이모지·이모티콘은 쓰지 않는다. 8) 바꾼 문장 한 줄만 출력한다. 설명 금지.";

    private const string EmojiSystem =
        "너는 게임 채팅 문장에 어울리는 이모지를 고르는 도우미다. 고를 수 있는 것: 😊(인사·친근) 😓(사과·곤란·힘듦) 🥳(축하·기쁨) 😍(감사·애정) 😉(작별·장난) 🤣(웃음) 😢(슬픔·아쉬움) 😎(자랑·대박). " +
        "파티 모집, 공략·정보 전달, 지시, 질문, 단순 상황 보고처럼 감정이 뚜렷하지 않은 문장에는 '없음'을 고른다. 이모지 하나 또는 '없음'만 출력한다.";

    private static readonly (string In, string Out)[] EmojiShots =
    {
        ("지금 레이드 들어갑니다", "없음"), ("힐러 한 분만 더 구해요", "없음"), ("죄송합니다 제가 실수했어요", "😓"), ("도와주셔서 정말 감사합니다", "😍"),
        ("드디어 클리어했어요 축하해요", "🥳"), ("장판 밟지 마세요", "없음"), ("안녕하세요 처음 왔어요", "😊"), ("오늘 고생 많으셨어요 다음에 봬요", "😉"), ("아 이걸 놓쳤네요 너무 아쉬워요", "😢"),
    };

    private readonly object _lock = new();
    private DateTime _pausedUntil = DateTime.MinValue;

    private string Model
    {
        get
        {
            var c = settings.Current;
            if (!string.IsNullOrWhiteSpace(c.ChatStyleModel)) return c.ChatStyleModel!;
            return string.IsNullOrWhiteSpace(c.SttRefineModel) ? OllamaVoiceRefiner.DefaultModel : c.SttRefineModel!;
        }
    }

    public async Task<ChatStyleResult> StyleAsync(string text, ChatStyleRequest req, CancellationToken ct)
    {
        if ((!req.Tone && !req.Emoji) || string.IsNullOrWhiteSpace(text)) return ChatStyleResult.Nothing;
        lock (_lock) { if (DateTime.UtcNow < _pausedUntil) return ChatStyleResult.Nothing; }
        var model = Model;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(settings.Current.ChatStyleMs, 500, 10000)));
        try
        {
            var toneTask = req.Tone ? ToneAsync(model, req.PersonaDesc, text, ChatText.MaxLength - (req.Emoji ? 2 : 0), cts.Token) : Task.FromResult<string?>(null);
            var emojiTask = req.Emoji ? ChatAsync(model, EmojiMessages(text), 0, 12, cts.Token) : Task.FromResult<string?>(null);
            await Task.WhenAll(toneTask, emojiTask);
            var tone = req.Tone ? await toneTask : null;
            var (emoji, none) = req.Emoji ? StyleGuard.AcceptEmoji(await emojiTask) : (null, false);
            return new ChatStyleResult(tone, emoji, none);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Fail("시간 초과");
            return ChatStyleResult.Nothing;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Fail(ex is HttpRequestException ? "Ollama가 켜져 있지 않습니다" : "오류");
            log.LogDebug(ex, "채팅 꾸미기 오류");
            return ChatStyleResult.Nothing;
        }
    }

    private void Fail(string why)
    {
        lock (_lock) _pausedUntil = DateTime.UtcNow + Pause;
        log.LogInformation("채팅 꾸미기를 건너뜁니다: {Why}", why);
    }

    /// <summary>
    /// 말투 바꾸기. 검증에서 떨어지면(뜻이 틀어짐·질문으로 바뀜 등) 온도를 높여 한 번 더 시도한다. 둘 다 떨어지면 null = 원문.
    /// 페르소나 설명이 너무 길면 앞부분만 쓴다(사용자가 만든 커스텀 페르소나 지시문).
    /// </summary>
    private static async Task<string?> ToneAsync(string model, string desc, string text, int maxLength, CancellationToken ct)
    {
        if (desc.Length > 600) desc = desc[..600];
        foreach (var temperature in new[] { 0.3, 0.9 })
        {
            var ok = StyleGuard.AcceptTone(text, await ChatAsync(model, ToneMessages(desc, text), temperature, 120, ct), maxLength);
            if (ok != null) return ok;
        }
        return null;
    }

    private static List<object> ToneMessages(string desc, string text) => new()
    {
        new { role = "system", content = string.Format(ToneSystem, desc) },
        new { role = "user", content = text },
    };

    private static List<object> EmojiMessages(string text)
    {
        var m = new List<object> { new { role = "system", content = EmojiSystem } };
        foreach (var (i, o) in EmojiShots) { m.Add(new { role = "user", content = i }); m.Add(new { role = "assistant", content = o }); }
        m.Add(new { role = "user", content = text });
        return m;
    }

    private static async Task<string?> ChatAsync(string model, List<object> messages, double temperature, int numPredict, CancellationToken ct)
    {
        var body = new { model, stream = false, think = false, keep_alive = "30m", options = new { temperature, num_predict = numPredict }, messages };
        using var res = await Http.PostAsJsonAsync("api/chat", body, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c) ? c.GetString() : null;
    }
}
