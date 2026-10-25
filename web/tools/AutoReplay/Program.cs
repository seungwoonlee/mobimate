using System.Globalization;
using System.Text;
using System.Text.Json;
using MobiMate;

// 코퍼스 폴더: *.jsonl 관찰 기록(앱의 AutoWatchRecorder 형식) + labels.jsonl(정답)
var dir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "corpus");
dir = Path.GetFullPath(dir);
var labelsPath = Path.Combine(dir, "labels.jsonl");
if (!File.Exists(labelsPath)) { Console.WriteLine($"정답 파일이 없습니다: {labelsPath}"); return 1; }
Console.OutputEncoding = Encoding.UTF8;

var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var runs = File.ReadAllLines(labelsPath).Where(l => l.TrimStart().StartsWith('{')).Select(l => JsonSerializer.Deserialize<Run>(l, json)!).ToList();
int autoTotal = 0, autoHit = 0, humanTotal = 0, humanFalse = 0;
var rows = new List<string>();
foreach (var run in runs)
{
    var file = Path.Combine(dir, run.File);
    if (!File.Exists(file)) { Console.WriteLine($"기록 파일 없음: {run.File}"); continue; }
    var obs = ReadRun(file, run);
    var tr = new AutoSuspicionTracker();
    var seen = new Dictionary<string, Stat>();
    DateTime? last = null;
    foreach (var o in obs)
    {
        if (last != null && (o.T - last.Value).TotalSeconds > 20) tr.Reset();   // 기록이 끊긴 구간
        last = o.T;
        tr.Observe(new DateTimeOffset(o.T), true, o.Players.Select(p => p.W).ToList(), o.Me);
        foreach (var p in o.Players)
        {
            var s = seen.TryGetValue(p.W.Key, out var x) ? x : seen[p.W.Key] = new Stat(p.W.Job, p.W.Title ?? "", o.T);
            s.N++; s.Combat += p.W.InCombat ? 1 : 0;
        }
        foreach (var sus in tr.Suspects)
        {
            if (!seen.TryGetValue(sus.Key, out var s)) continue;
            if (sus.Intensity > s.MaxI) { s.MaxI = sus.Intensity; s.Reason = sus.Reason.ToString(); s.FlagSec = (o.T - s.First).TotalSeconds; }
        }
    }
    Console.WriteLine($"\n## {run.File} {run.From}~{run.To} {run.Space}{(run.MaxPlayers > 0 ? $" (≤{run.MaxPlayers}명)" : "")} — {run.Note}");
    Console.WriteLine($"   {"플레이어",-26}{"정답",-6}{"본",5}{"전투%",6}  앱 의심");
    foreach (var s in seen.Values.Where(v => v.N >= run.MinSeen).OrderBy(v => v.Job).ThenBy(v => v.Title))
    {
        var label = run.Auto.Any(a => a.Match(s)) ? "오토" : run.Human.Any(a => a.Match(s)) || run.OthersHuman ? "정상" : "?";
        var flagged = s.MaxI > 0;
        var mark = label switch { "오토" => flagged ? "✓ 잡음" : "✗ 놓침", "정상" => flagged ? "⚠ 오탐" : "✓ 정상", _ => flagged ? "· 표시(정답 모름)" : "·" };
        Console.WriteLine($"   {s.Job + "[" + s.Title + "]",-26}{label,-6}{s.N,5}{100 * s.Combat / Math.Max(1, s.N),5}%  {mark}{(flagged ? $"  {s.Reason} 짙기 {s.MaxI:F2} ({s.FlagSec:F0}초 시점)" : "")}");
        if (label == "오토") { autoTotal++; if (flagged) autoHit++; }
        if (label == "정상") { humanTotal++; if (flagged) humanFalse++; }
    }
}
Console.WriteLine($"\n===== 합계 =====\n오토 {autoHit}/{autoTotal}명 잡음 (재현율 {Pct(autoHit, autoTotal)})\n정상 {humanFalse}/{humanTotal}명 오탐 (오탐률 {Pct(humanFalse, humanTotal)})");
return 0;

static string Pct(int a, int b) => b == 0 ? "-" : $"{100.0 * a / b:F0}%";

static List<Obs> ReadRun(string file, Run run)
{
    var from = TimeSpan.Parse(run.From, CultureInfo.InvariantCulture); var to = TimeSpan.Parse(run.To, CultureInfo.InvariantCulture);
    var list = new List<Obs>();
    foreach (var line in File.ReadLines(file))
    {
        JsonElement d; try { d = JsonDocument.Parse(line).RootElement; } catch { continue; }
        var t = DateTime.Parse(d.GetProperty("t").GetString()!, CultureInfo.InvariantCulture);
        if (t.TimeOfDay < from || t.TimeOfDay > to) continue;
        var space = d.TryGetProperty("space", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : null;
        if (space == null || !space.StartsWith(run.Space)) continue;
        if (!d.TryGetProperty("p", out var pe) || pe.ValueKind != JsonValueKind.Array) continue;
        if (run.MaxPlayers > 0 && pe.GetArrayLength() > run.MaxPlayers) continue;
        var ps = new List<P>();
        foreach (var p in pe.EnumerateArray())
        {
            string S(string n) => p.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            int I(string n) => p.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
            long L(string n) => p.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
            var title = p.TryGetProperty("title", out var tt) && tt.ValueKind == JsonValueKind.String ? tt.GetString() : null;
            ps.Add(new P(new WatchedPlayer(S("k"), S("realm"), S("job"), I("lv"), L("cs"), title, I("c") == 1, p.GetProperty("d").GetDouble(), I("pl") == 1, I("rb") == 1, I("hd") == 1, I("wh") == 1, I("cl"))));
        }
        WorldPos? me = d.TryGetProperty("me", out var m) && m.ValueKind == JsonValueKind.Object ? new WorldPos(m.GetProperty("X").GetDouble(), m.GetProperty("Y").GetDouble()) : null;
        list.Add(new Obs(t, ps, me));
    }
    return list;
}

record Obs(DateTime T, List<P> Players, WorldPos? Me);
record P(WatchedPlayer W);
class Stat(string job, string title, DateTime first) { public string Job = job, Title = title; public DateTime First = first; public int N, Combat; public double MaxI, FlagSec; public string Reason = ""; }
record Who(string Job, string Title) { public bool Match(Stat s) => s.Job == Job && s.Title == Title; }
class Run
{
    public string File { get; set; } = "";
    public string From { get; set; } = "00:00:00";
    public string To { get; set; } = "23:59:59";
    public string Space { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>한 번에 보이는 사람이 이 수를 넘는 기록은 뺀다(마을·입구 인파). 0이면 제한 없음.</summary>
    public int MaxPlayers { get; set; }
    /// <summary>이 초 수 이상 본 사람만 채점한다.</summary>
    public int MinSeen { get; set; } = 20;
    /// <summary>목록에 없는 나머지 사람은 모두 정상(직접 모집 파티 등)이다.</summary>
    public bool OthersHuman { get; set; }
    public List<Who> Auto { get; set; } = new();
    public List<Who> Human { get; set; } = new();
}
