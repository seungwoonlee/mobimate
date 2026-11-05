using System.Text;
using System.Text.RegularExpressions;

namespace MobiMate.Web.Voice;

/// <summary>학습한 낱말 하나 (개수와 마지막 사용일만 — 문장·문맥은 저장하지 않는다)</summary>
public sealed class VocabWord
{
    public int Count { get; set; }
    public DateTime Last { get; set; }
}

/// <summary>고친 쌍 "인식 글자 → 고친 글자"</summary>
public sealed class VocabPair
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int Count { get; set; }

    /// <summary>적용된 치환을 사용자가 되돌려 보낸 횟수. 2회가 되면 쌍을 끈다</summary>
    public int Reverts { get; set; }

    /// <summary>끔 (사용자가 껐거나 되돌림 때문에 자동으로 꺼짐)</summary>
    public bool Off { get; set; }

    public DateTime Last { get; set; }
}

/// <summary>vocab.json 내용 (FR-VC-08)</summary>
public sealed class VocabData
{
    public int Version { get; set; } = 1;
    public bool LearningOn { get; set; } = true;
    public Dictionary<string, VocabWord> Words { get; set; } = new();
    public List<VocabPair> Pairs { get; set; } = new();
}

/// <summary>
/// 누적 학습 (요구사양 FR-VC-08, 상세설계 §3.9). 쓸수록 내 말투와 게임 용어에 맞춰진다.
/// - **낱말 빈도**: 보낸 채팅(직접 입력·음성)의 낱말을 센다. 숫자가 섞인 낱말·너무 짧거나 긴 낱말은 제외한다.
/// - **고친 쌍**: 음성 초안을 고쳐 보냈을 때 "인식 글자 → 고친 글자"를 센다. 양쪽이 모두 있는 **맞바꿈**만 쌍으로 인정하고
///   삽입·삭제만 있는 경우와 너무 많이 고친(다시 쓴) 경우는 버린다. 같은 쌍이 2회 확인되면 인식 결과에 치환한다.
/// - **문장 원문과 앞뒤 문맥은 저장하지 않는다.** 저장하는 것은 낱말·쌍의 개수와 마지막 사용일뿐이다(NFR-15).
/// - 90일 넘게 안 쓴 항목은 정리한다. 낱말은 최대 2000개.
/// 저장은 <see cref="JsonFileStore"/>(원자적 쓰기, 손상 파일은 격리).
/// </summary>
public sealed class VoiceVocabulary
{
    public const int ActivateAt = 2;           // 같은 쌍이 이만큼 확인되면 적용
    public const int AutoOffReverts = 2;       // 이만큼 되돌리면 자동으로 끈다
    public const int MinApplySyllables = 2;    // 인식 글자가 이보다 짧은 쌍은 적용하지 않는다
    public const int MaxHunkSyllables = 6;
    public const int MaxEditDistance = 8;      // 이보다 많이 고쳤으면 다시 쓴 것으로 보고 배우지 않는다
    public const int MaxHunks = 3;
    public const int MaxWords = 2000;
    public const int HotwordMinCount = 3;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private readonly object _lock = new();
    private readonly string _path;
    private readonly JsonFileStore _store = new();
    private readonly Func<DateTime> _now;
    private VocabData _data;

    /// <summary>파일이 손상되어 격리했다면 그 사실 (설정 화면에서 알린다 — 학습이 조용히 사라지면 안 된다)</summary>
    public string? LoadWarning { get; private set; }

    public VoiceVocabulary(string storageDir, Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        _path = Path.Combine(storageDir, "stt", "vocab.json");
        var status = _store.TryLoad<VocabData>(_path, out var loaded);
        _data = loaded ?? new VocabData();
        if (status == LoadStatus.Quarantined) LoadWarning = "학습 파일이 손상되어 따로 보관하고 새로 시작했습니다.";
        else if (status == LoadStatus.Failed) LoadWarning = "학습 파일을 읽지 못했습니다(다른 프로그램이 쓰는 중일 수 있습니다). 이번 실행에서는 새로 시작합니다.";
        Prune();
    }

    public bool LearningOn { get { lock (_lock) return _data.LearningOn; } }

    private int _version;

    /// <summary>학습 내용이 바뀔 때마다 올라간다 (이 값으로 띄어쓰기 사전을 다시 만들지 판단한다)</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>낱말과 횟수 (띄어쓰기 사전용). 학습이 꺼져 있으면 비어 있다</summary>
    public IReadOnlyList<KeyValuePair<string, int>> WordCounts()
    {
        lock (_lock)
        {
            return _data.LearningOn
                ? _data.Words.Select(kv => new KeyValuePair<string, int>(kv.Key, kv.Value.Count)).ToList()
                : Array.Empty<KeyValuePair<string, int>>();
        }
    }

    public void SetLearning(bool on)
    {
        lock (_lock) { _data.LearningOn = on; Save(); }
    }

    // ───────── 학습 ─────────

    /// <summary>
    /// 보낸 문장에서 배운다. <paramref name="sttOriginal"/>은 음성 초안을 고쳐 보낸 경우의 고치기 전 인식 글자(없으면 낱말만 센다).
    /// 학습이 꺼져 있으면 아무것도 하지 않는다.
    /// </summary>
    public void Learn(string sent, string? sttOriginal = null)
    {
        if (string.IsNullOrWhiteSpace(sent)) return;
        lock (_lock)
        {
            if (!_data.LearningOn) return;
            var now = _now();
            foreach (var w in Words(sent))
            {
                if (_data.Words.TryGetValue(w, out var e)) { e.Count++; e.Last = now; }
                else _data.Words[w] = new VocabWord { Count = 1, Last = now };
            }
            if (!string.IsNullOrWhiteSpace(sttOriginal)) LearnPairs(sttOriginal, sent, now);
            Prune();
            Save();
        }
    }

    private void LearnPairs(string original, string sent, DateTime now)
    {
        var a = Normalize(original);
        var b = Normalize(sent);
        if (a.Length == 0 || b.Length == 0) return;

        // 적용되었던 치환을 되돌려 보냈는지: 인식 글자에 from이 있었는데 보낸 글에는 to 없이 from이 남아 있다
        foreach (var p in _data.Pairs)
        {
            if (p.Off || p.Count < ActivateAt) continue;
            if (a.Contains(p.From, StringComparison.Ordinal) && !b.Contains(p.To, StringComparison.Ordinal) && b.Contains(p.From, StringComparison.Ordinal))
            {
                p.Reverts++;
                if (p.Reverts >= AutoOffReverts) p.Off = true;
            }
        }

        foreach (var (from, to) in ExtractPairs(a, b))
        {
            var pair = _data.Pairs.FirstOrDefault(x => x.From == from && x.To == to);
            if (pair == null) _data.Pairs.Add(pair = new VocabPair { From = from, To = to });
            pair.Count++;
            pair.Last = now;
        }
    }

    // ───────── 치환 ─────────

    /// <summary>
    /// 활성 쌍을 공백 없는 인식 글자에 적용한다. 긴 "from"부터, 한 번만(치환 결과를 다시 치환하지 않는다).
    /// "from"이 <see cref="MinApplySyllables"/>음절 미만이면 건너뛴다. 반환: 바뀐 글자와 적용 설명("일러→힐러").
    /// </summary>
    public (string Text, IReadOnlyList<string> Applied) Apply(string raw)
    {
        List<VocabPair> active;
        lock (_lock)
        {
            if (!_data.LearningOn) return (raw, Array.Empty<string>());
            active = _data.Pairs.Where(IsActive).OrderByDescending(p => p.From.Length).ThenByDescending(p => p.Count).ToList();
        }
        if (active.Count == 0 || string.IsNullOrEmpty(raw)) return (raw, Array.Empty<string>());

        var map = new Dictionary<string, string>();
        foreach (var p in active) map.TryAdd(p.From, p.To);   // 같은 from이 둘이면 더 많이 확인된 쪽
        var regex = new Regex(string.Join("|", map.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)));
        var applied = new List<string>();
        var text = regex.Replace(raw, m =>
        {
            applied.Add($"{m.Value}→{map[m.Value]}");
            return map[m.Value];
        });
        return (text, applied.Distinct().ToList());
    }

    private static bool IsActive(VocabPair p) => !p.Off && p.Count >= ActivateAt && p.From.Length >= MinApplySyllables;

    // ───────── 핫워드 ─────────

    /// <summary>자주 쓰는 낱말(3회 이상) 상위 N개</summary>
    public IReadOnlyList<string> HotwordCandidates(int max)
    {
        lock (_lock)
        {
            if (!_data.LearningOn) return Array.Empty<string>();
            return _data.Words.Where(kv => kv.Value.Count >= HotwordMinCount)
                .OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(max).Select(kv => kv.Key).ToList();
        }
    }

    // ───────── 보기·고치기 (설정 화면) ─────────

    public VocabView View(int maxWords = 200)
    {
        lock (_lock)
        {
            return new VocabView(
                _data.LearningOn, LoadWarning,
                _data.Words.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(maxWords)
                    .Select(kv => new VocabWordView(kv.Key, kv.Value.Count, kv.Value.Last, kv.Value.Count >= HotwordMinCount)).ToList(),
                _data.Pairs.OrderByDescending(p => p.Count).ThenBy(p => p.From, StringComparer.Ordinal)
                    .Select(p => new VocabPairView(p.From, p.To, p.Count, p.Reverts, p.Off, IsActive(p), p.Last)).ToList());
        }
    }

    public bool SetPairOff(string from, string to, bool off)
    {
        lock (_lock)
        {
            var p = _data.Pairs.FirstOrDefault(x => x.From == from && x.To == to);
            if (p == null) return false;
            p.Off = off;
            if (!off) p.Reverts = 0;   // 다시 켜면 되돌림 횟수도 처음부터
            Save();
            return true;
        }
    }

    public bool RemovePair(string from, string to)
    {
        lock (_lock)
        {
            var n = _data.Pairs.RemoveAll(x => x.From == from && x.To == to);
            if (n > 0) Save();
            return n > 0;
        }
    }

    public bool RemoveWord(string word)
    {
        lock (_lock)
        {
            var ok = _data.Words.Remove(word);
            if (ok) Save();
            return ok;
        }
    }

    /// <summary>전체 초기화 (학습 켜짐/꺼짐 설정은 유지)</summary>
    public void Clear()
    {
        lock (_lock)
        {
            var on = _data.LearningOn;
            _data = new VocabData { LearningOn = on };
            Save();
        }
    }

    // ───────── 내부 ─────────

    private void Prune()
    {
        var limit = _now() - Retention;
        foreach (var k in _data.Words.Where(kv => kv.Value.Last < limit).Select(kv => kv.Key).ToList()) _data.Words.Remove(k);
        _data.Pairs.RemoveAll(p => p.Last < limit);
        if (_data.Words.Count > MaxWords)
            foreach (var k in _data.Words.OrderBy(kv => kv.Value.Count).ThenBy(kv => kv.Value.Last).Take(_data.Words.Count - MaxWords).Select(kv => kv.Key).ToList()) _data.Words.Remove(k);
    }

    private void Save()
    {
        Interlocked.Increment(ref _version);
        _store.Save(_path, _data);
    }

    private static readonly Regex NotLetters = new(@"[^\p{L}]", RegexOptions.Compiled);

    /// <summary>학습할 낱말: 공백으로 나눠 글자(한글·영문)만 남기고 2~8자, 숫자가 섞이면 제외</summary>
    internal static IEnumerable<string> Words(string sent)
    {
        foreach (var raw in sent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Any(char.IsDigit)) continue;                       // 숫자·ID·전화번호 같은 것
            if (raw.Contains('@') || raw.Contains("://")) continue;    // 주소·링크
            var w = NotLetters.Replace(raw, "");                        // 이모지·문장부호 제거
            if (w.Length is >= 2 and <= 8) yield return w;
        }
    }

    /// <summary>공백·문장부호·이모지를 뺀 글자열 (띄어쓰기와 무관하게 비교한다)</summary>
    internal static string Normalize(string s) => NotLetters.Replace(s, "").ToLowerInvariant();

    /// <summary>
    /// 두 글자열을 편집 거리로 맞춰 바뀐 덩어리를 찾는다. 양쪽이 모두 1~6음절인 맞바꿈만 쌍으로 돌려준다.
    /// 한 음절짜리 덩어리는 이웃 글자 하나를 붙여 2음절 이상으로 만든다(예: 일→힐 은 "일러→힐러").
    /// 편집 거리가 8을 넘거나 덩어리가 3개를 넘으면 다시 쓴 것으로 보고 아무것도 돌려주지 않는다.
    /// </summary>
    internal static IReadOnlyList<(string From, string To)> ExtractPairs(string a, string b)
    {
        var n = a.Length; var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        if (d[n, m] == 0 || d[n, m] > MaxEditDistance) return Array.Empty<(string, string)>();

        // 뒤에서부터 되짚어 일치하지 않는 구간(덩어리)을 모은다. 같은 비용이면 맞바꿈을 먼저 택한다.
        var hunks = new List<(int AStart, int AEnd, int BStart, int BEnd)>();
        int ai = n, bj = m, endA = -1, endB = -1;
        while (ai > 0 || bj > 0)
        {
            bool match = ai > 0 && bj > 0 && a[ai - 1] == b[bj - 1] && d[ai, bj] == d[ai - 1, bj - 1];
            if (match)
            {
                if (endA >= 0) { hunks.Add((ai, endA, bj, endB)); endA = -1; }
                ai--; bj--;
                continue;
            }
            if (endA < 0) { endA = ai; endB = bj; }
            if (ai > 0 && bj > 0 && d[ai, bj] == d[ai - 1, bj - 1] + 1) { ai--; bj--; }
            else if (ai > 0 && d[ai, bj] == d[ai - 1, bj] + 1) ai--;
            else bj--;
        }
        if (endA >= 0) hunks.Add((0, endA, 0, endB));
        hunks.Reverse();
        if (hunks.Count > MaxHunks) return Array.Empty<(string, string)>();

        var result = new List<(string, string)>();
        foreach (var (aS, aE, bS, bE) in hunks)
        {
            var from = a[aS..aE]; var to = b[bS..bE];
            if (from.Length == 0 || to.Length == 0) continue;                       // 삽입·삭제만: 버린다
            if (from.Length > MaxHunkSyllables || to.Length > MaxHunkSyllables) continue;
            if (from.Length < MinApplySyllables || to.Length < MinApplySyllables)
            {
                // 이웃 글자를 붙여 2음절 이상으로: 오른쪽 이웃이 있으면 오른쪽, 아니면 왼쪽 (두 글자열에서 같은 글자)
                if (aE < n && bE < m && a[aE] == b[bE]) { from += a[aE]; to += b[bE]; }
                else if (aS > 0 && bS > 0 && a[aS - 1] == b[bS - 1]) { from = a[aS - 1] + from; to = b[bS - 1] + to; }
                else continue;
            }
            if (from != to) result.Add((from, to));
        }
        return result;
    }
}

public sealed record VocabWordView(string Word, int Count, DateTime Last, bool Hotword);
public sealed record VocabPairView(string From, string To, int Count, int Reverts, bool Off, bool Active, DateTime Last);
public sealed record VocabView(bool LearningOn, string? Warning, IReadOnlyList<VocabWordView> Words, IReadOnlyList<VocabPairView> Pairs);

/// <summary>
/// 띄어쓰기 사전을 필요할 때 만들어 두고 쓴다 (FR-VC-09). 학습 내용이 바뀌면(<see cref="VoiceVocabulary.Version"/>) 다시 만든다 —
/// 내가 보낸 낱말이 사전에 들어가므로 쓸수록 좋아진다.
/// </summary>
public sealed class SpacingProvider(VoiceVocabulary vocab)
{
    private static readonly Lazy<List<string>> Corpus = new(() =>
    {
        var all = new List<string>();
        foreach (var dict in new[] { PersonaTemplatesData.VillainessLines, PersonaTemplatesData.ScroogeLines, PersonaTemplatesData.MorningLines,
                                     PersonaTemplatesData.GyeongsangLines, PersonaTemplatesData.IdolDancerLines, NeutralPersonaLines.Lines })
            foreach (var lines in dict.Values) all.AddRange(lines);
        return all;
    });

    private readonly object _lock = new();
    private SpacingRestorer? _restorer;
    private int _builtFor = -1;

    public SpacingRestorer Get()
    {
        lock (_lock)
        {
            var v = vocab.Version;
            if (_restorer == null || _builtFor != v)
            {
                _restorer = SpacingRestorer.Build(Corpus.Value, SpacingRestorer.Curated, vocab.WordCounts());
                _builtFor = v;
            }
            return _restorer;
        }
    }
}

/// <summary>
/// 인식 글자 후처리 (상세설계 §3.9): ① 학습된 치환 ② 띄어쓰기 복원(설정에서 켠 경우) ③ 정리.
/// 치환은 공백 없는 글자열에서 하므로 띄어쓰기 복원보다 먼저 한다.
/// </summary>
public sealed class LearnedPostProcessor(VoiceVocabulary vocab, SpacingProvider spacing, Hosting.WebSettingsStore settings) : IVoicePostProcessor
{
    public VoicePostResult Process(string raw)
    {
        var (text, applied) = vocab.Apply(raw.Trim());
        if (settings.Current.SttSpacing) text = spacing.Get().Restore(text);
        return new VoicePostResult(text.Trim(), applied);
    }
}
