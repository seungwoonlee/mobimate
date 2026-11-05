using System.Text;

namespace MobiMate.Web.Voice;

/// <summary>
/// 띄어쓰기 복원 (FR-VC-09). 인식 모델은 띄어쓰기 없이 글자만 내놓는다("지금레이드들어갑니다준비해주세요").
/// 한국어 띄어쓰기 전용 모델은 찾지 못해, **낱말 사전 + 비용이 가장 적은 나누기**(동적 계획법)로 복원한다.
/// - 사전에 있는 낱말은 한 덩어리 비용이 적고, 모르는 구간은 글자 수만큼 비싸다 → 아는 낱말 사이에서만 나눈다.
/// - **모르는 구간은 쪼개지 않고 한 덩어리로 붙여 둔다.** 틀린 곳에 공백을 넣는 것보다 붙어 있는 편이 읽기 낫고, 입력창에서 고치기 쉽다.
/// - 숫자·영문·부호는 그대로 두고 한글 연속 구간 안에서만 나눈다.
/// 사전은 ① 대사 말뭉치(띄어쓰기가 된 게임 문장) ② 파티·레이드 채팅 낱말 ③ 사용자가 보낸 낱말(누적 학습)로 만든다 — 쓸수록 늘어난다.
/// </summary>
public sealed class SpacingRestorer
{
    private const double SegmentCost = 1.0;          // 낱말 하나(경계 하나)마다 드는 비용: 덜 쪼개는 쪽이 유리하다
    private const double UnknownPerSyllable = 1.6;   // 모르는 글자는 한 음절마다 이만큼 비싸다
    private const int MaxWordLength = 12;

    private readonly Dictionary<string, double> _cost;
    private readonly int _maxKnown;
    private readonly int _glue;

    /// <summary>모르는 짧은 조각을 앞 낱말에 붙이는 기본 길이(음절). 조사·어미("이", "에서", "해야")는 거의 항상 앞 낱말에 붙는다</summary>
    public const int DefaultGlue = 2;

    /// <param name="weights">낱말 → 가중치(많이 보일수록 큼). 가중치가 클수록 선택될 가능성이 높다</param>
    /// <param name="glueSyllables">이 길이 이하의 모르는 조각은 앞 낱말에 붙인다(0이면 끈다)</param>
    public SpacingRestorer(IReadOnlyDictionary<string, double> weights, int glueSyllables = DefaultGlue)
    {
        _glue = glueSyllables;
        _cost = new Dictionary<string, double>(weights.Count);
        foreach (var (word, weight) in weights)
        {
            if (word.Length is < 1 or > MaxWordLength) continue;
            _cost[word] = Math.Max(0.35, SegmentCost - Math.Min(0.6, 0.12 * Math.Log(1 + weight)));
        }
        _maxKnown = _cost.Count == 0 ? 0 : _cost.Keys.Max(k => k.Length);
    }

    public int WordCount => _cost.Count;

    /// <summary>
    /// 사전을 만든다. <paramref name="spacedSentences"/>는 띄어쓰기가 된 문장(낱말은 공백 단위), <paramref name="curated"/>는 직접 정리한 낱말,
    /// <paramref name="learned"/>는 사용자가 보낸 낱말과 횟수. 한 글자 낱말은 정리한 목록(<see cref="curated"/>)에 있는 것만 쓴다 —
    /// 말뭉치에서 자동으로 뽑으면 조사("이", "가")까지 낱말이 되어 엉뚱한 곳에서 쪼개진다.
    /// </summary>
    public static SpacingRestorer Build(IEnumerable<string> spacedSentences, IEnumerable<string> curated, IEnumerable<KeyValuePair<string, int>>? learned = null, int glueSyllables = DefaultGlue)
    {
        var w = new Dictionary<string, double>(StringComparer.Ordinal);
        void Add(string word, double weight)
        {
            if (word.Length == 0) return;
            w[word] = w.TryGetValue(word, out var old) ? old + weight : weight;
        }

        foreach (var s in spacedSentences)
            foreach (var raw in s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var h = HangulOnly(raw);
                if (h.Length >= 2) Add(h, 1);
            }
        foreach (var c in curated) Add(HangulOnly(c), 12);                         // 정리한 낱말은 가중치를 크게
        if (learned != null)
            foreach (var (word, count) in learned) Add(HangulOnly(word), 6 + count);   // 내가 쓴 낱말은 더 크게
        return new SpacingRestorer(w, glueSyllables);
    }

    /// <summary>띄어쓰기를 복원한다. 이미 공백이 있는 글은 공백 단위로 나눠 각각 처리한다(다시 붙이지 않는다).</summary>
    public string Restore(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _cost.Count == 0) return text;
        var sb = new StringBuilder(text.Length + 8);
        var first = true;
        foreach (var part in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!first) sb.Append(' ');
            first = false;
            sb.Append(RestoreToken(part));
        }
        return sb.ToString();
    }

    /// <summary>공백 없는 한 덩어리: 한글 구간만 나누고 나머지(숫자·영문·부호)는 그대로 붙인다</summary>
    private string RestoreToken(string token)
    {
        var sb = new StringBuilder(token.Length + 4);
        var i = 0;
        while (i < token.Length)
        {
            var j = i;
            var hangul = IsHangul(token[i]);
            while (j < token.Length && IsHangul(token[j]) == hangul) j++;
            var run = token[i..j];
            sb.Append(hangul ? Segment(run) : run);
            i = j;
        }
        return sb.ToString();
    }

    private string Segment(string run)
    {
        var n = run.Length;
        if (n < 2) return run;
        var best = new double[n + 1];
        var from = new int[n + 1];
        var known = new bool[n + 1];
        for (var i = 1; i <= n; i++)
        {
            best[i] = double.MaxValue;
            // 아는 낱말로 끝나는 경우
            for (var len = 1; len <= Math.Min(_maxKnown, i); len++)
            {
                var j = i - len;
                if (best[j] == double.MaxValue) continue;
                if (!_cost.TryGetValue(run.Substring(j, len), out var c)) continue;
                var total = best[j] + c;
                if (total < best[i]) { best[i] = total; from[i] = j; known[i] = true; }
            }
            // 모르는 구간(쪼개지 않는 한 덩어리)으로 끝나는 경우
            for (var j = 0; j < i; j++)
            {
                if (best[j] == double.MaxValue) continue;
                var total = best[j] + SegmentCost + UnknownPerSyllable * (i - j);
                if (total < best[i]) { best[i] = total; from[i] = j; known[i] = false; }
            }
        }

        var cuts = new List<(int Start, int End, bool Known)>();
        for (var i = n; i > 0; i = from[i]) cuts.Add((from[i], i, known[i]));
        cuts.Reverse();

        // 이웃한 모르는 구간은 한 덩어리로 합치고(모르는 곳에 공백을 넣지 않는다),
        // 앞 낱말 뒤의 짧은 모르는 조각은 그 낱말에 붙인다(조사·어미일 가능성이 높다)
        var sb = new StringBuilder(n + 4);
        var prevUnknown = false;
        for (var k = 0; k < cuts.Count; k++)
        {
            var (s, e, kn) = cuts[k];
            var glueToPrevious = k > 0 && !kn && (e - s) <= _glue;
            if (k > 0 && !(prevUnknown && !kn) && !glueToPrevious) sb.Append(' ');
            sb.Append(run, s, e - s);
            prevUnknown = !kn;
        }
        return sb.ToString();
    }

    internal static string HangulOnly(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) if (IsHangul(ch)) sb.Append(ch);
        return sb.ToString();
    }

    private static bool IsHangul(char c) => c is >= '가' and <= '힣';

    /// <summary>
    /// 파티·레이드·사냥·거래 채팅에서 자주 쓰는 낱말. 사전을 처음부터 풍부하게 하려는 것이며, 쓰면서 사용자의 낱말이 더해진다.
    /// 한 글자 낱말은 여기 있는 것만 사전에 들어간다.
    /// </summary>
    public static readonly IReadOnlyList<string> Curated = new[]
    {
        // 모집·초대
        "파티", "파티장", "파티원", "초대", "부탁드립니다", "부탁드려요", "부탁해요", "구합니다", "구해요", "모집합니다", "모십니다", "받습니다", "받아요",
        "같이", "함께", "가요", "갑니다", "가실", "가세요", "오세요", "하실", "계신가요", "계세요", "계시나요", "있나요", "있어요", "없어요", "있습니다", "없습니다",
        // 역할·콘텐츠
        "레이드", "던전", "사냥", "채집", "낚시", "보스", "필드보스", "어비스", "힐러", "딜러", "탱커", "서포터", "근딜", "원딜", "마법사", "궁수", "전사",
        // 전투 지시
        "패턴", "조심하세요", "조심해요", "조심하십시오", "바닥", "장판", "나옵니다", "나와요", "피하세요", "피해요", "막아주세요", "끌어주세요", "넣어주세요", "써주세요", "눌러주세요",
        "광폭화", "쿨타임", "어그로", "버프", "디버프", "힐", "딜", "뒤로", "앞으로", "왼쪽", "오른쪽", "위로", "아래로", "이쪽", "저쪽", "여기", "저기", "거기",
        // 상태·진행
        "지금", "바로", "빨리", "천천히", "먼저", "다시", "또", "잠깐", "잠시", "기다려", "기다려주세요", "준비해", "준비됐나요", "준비됐습니다", "준비됐어요", "시작합니다", "시작해요",
        "들어갑니다", "들어가요", "들어갈게요", "이동하겠습니다", "이동해요", "이동합니다", "다음", "다음에", "다음층", "층으로", "층", "마을", "마을에", "자리", "비우겠습니다", "비울게요",
        "다녀올게요", "다녀오겠습니다", "올게요", "돌아왔습니다", "돌아왔어요", "사망", "부활", "부족해서", "부족합니다", "부족해요", "포션", "장비", "아이템", "드랍", "분배", "주사위", "굴려주세요",
        // 인사·예의
        "안녕하세요", "반갑습니다", "수고하셨습니다", "수고했어요", "수고하세요", "고맙습니다", "감사합니다", "죄송합니다", "죄송해요", "잘", "부탁", "도와주세요", "도와줘요", "확인", "해주세요", "하세요",
        "해요", "합니다", "하겠습니다", "했습니다", "했어요", "주세요", "드립니다", "드려요", "괜찮아요", "괜찮습니다", "알겠습니다", "알겠어요", "네", "아니요", "맞아요", "그래요",
        // 수 세기·한 글자
        "한", "두", "세", "네", "명", "분", "분만", "번", "더", "개", "마리", "시", "초", "몇", "안", "못", "꼭", "좀",
    };
}
