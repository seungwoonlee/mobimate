namespace MobiMate.Web.Voice;

/// <summary>지난 음성 인식 한 건. <see cref="Original"/>은 인식기가 낸 글자(띄어쓰기 없음), <see cref="Text"/>는 후처리·AI 다듬기를 거친 문장이다.</summary>
public sealed class HistoryEntry
{
    public string Id { get; set; } = "";
    public DateTime At { get; set; }
    public string Original { get; set; } = "";
    public string Text { get; set; } = "";
    public bool AutoSent { get; set; }
    /// <summary>사용자가 고친 문장 (고쳐서 배운 뒤에 채워진다)</summary>
    public string? Corrected { get; set; }
}

public sealed class HistoryData
{
    public List<HistoryEntry> Entries { get; set; } = new();
}

/// <summary>
/// 지난 음성 기록 (승운 2026-11-07, FR-VC-16): 사용자가 지난 인식을 보고 직접 고쳐서 배우게 한다.
/// 누적 학습(FR-VC-08)은 문장 원문을 저장하지 않는 것이 원칙이라, 이것은 **명시적인 예외**다:
/// 최근 <see cref="MaxEntries"/>개·<see cref="Retention"/>까지만 이 PC의 `stt/history.json`에 두고, 설정에서 끄거나 지울 수 있으며 폰에는 보이지 않는다.
/// </summary>
public sealed class VoiceHistory
{
    public const int MaxEntries = 100;
    public const int MaxTextChars = 200;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly object _lock = new();
    private readonly string _path;
    private readonly JsonFileStore _store = new();
    private readonly Func<DateTime> _now;
    private HistoryData _data = new();

    public VoiceHistory(string storageDir, Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        _path = Path.Combine(storageDir, "stt", "history.json");
        if (_store.TryLoad<HistoryData>(_path, out var loaded) == LoadStatus.Loaded && loaded != null) _data = loaded;
        lock (_lock) Prune();
    }

    private static string Clip(string s) => s.Length <= MaxTextChars ? s : s[..MaxTextChars];

    /// <summary>새 기록을 더한다. 같은 문장을 연달아 인식한 것도 따로 남는다.</summary>
    public HistoryEntry Add(string original, string text, bool autoSent)
    {
        var e = new HistoryEntry { Id = Guid.NewGuid().ToString("N")[..10], At = _now(), Original = Clip(original), Text = Clip(text), AutoSent = autoSent };
        lock (_lock)
        {
            _data.Entries.Add(e);
            Prune();
            Save();
        }
        return e;
    }

    /// <summary>최근 것이 앞에 오도록 돌려준다.</summary>
    public IReadOnlyList<HistoryEntry> List()
    {
        lock (_lock)
        {
            Prune();
            return _data.Entries.OrderByDescending(x => x.At).Select(Copy).ToList();
        }
    }

    public HistoryEntry? Get(string id)
    {
        lock (_lock) return _data.Entries.FirstOrDefault(x => x.Id == id) is { } e ? Copy(e) : null;
    }

    public bool MarkCorrected(string id, string corrected)
    {
        lock (_lock)
        {
            var e = _data.Entries.FirstOrDefault(x => x.Id == id);
            if (e == null) return false;
            e.Corrected = Clip(corrected);
            Save();
            return true;
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var n = _data.Entries.RemoveAll(x => x.Id == id);
            if (n > 0) Save();
            return n > 0;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _data.Entries.Clear();
            Save();
            // 지운 문장이 남을 수 있는 백업·격리 파일도 함께 지운다
            foreach (var f in new[] { _path + ".bak" })
                try { File.Delete(f); } catch { /* 지우지 못해도 앱은 계속 */ }
        }
    }

    private void Prune()
    {
        var limit = _now() - Retention;
        _data.Entries.RemoveAll(x => x.At < limit);
        var extra = _data.Entries.Count - MaxEntries;
        if (extra > 0) _data.Entries.RemoveRange(0, extra);
    }

    private void Save() => _store.Save(_path, _data);

    private static HistoryEntry Copy(HistoryEntry e) => new() { Id = e.Id, At = e.At, Original = e.Original, Text = e.Text, AutoSent = e.AutoSent, Corrected = e.Corrected };
}
