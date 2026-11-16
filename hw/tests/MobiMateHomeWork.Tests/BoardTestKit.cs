using MobiMateHomeWork.ViewModels;

namespace MobiMateHomeWork.Tests;

/// <summary>숙제 표 화면 모델 테스트용 손으로 만든 현황(서비스·창 없이 CharactersSnapshot을 직접 짓는다)과 가짜 쓰기 동작.</summary>
internal static class BoardKit
{
    public static readonly DateTimeOffset Now = new(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);   // 수요일 12:00 KST

    public static HomeworkAutoStatus St(string id, string state, int? remaining = null, string? evidence = null, HomeworkSuggestion? suggestion = null) =>
        new(id, id, "weekly", state, evidence, remaining, "", suggestion);

    public static CoinView Coin(MeterKind kind, long held, DateTimeOffset lastSeen, bool member = false, bool live = false)
    {
        var cap = kind == MeterKind.Silver ? CoinForecast.SilverCap(member) : CoinForecast.TributeCap(member);
        var step = kind == MeterKind.Silver ? CoinForecast.SilverStep : CoinForecast.TributeStep;
        return CoinView.From(CoinForecast.Forecast(held, (live ? Now : lastSeen).UtcDateTime, Now.UtcDateTime, step, cap));
    }

    public static CharacterView Char(string key, string job = "격투가", string realm = "아이라", long combat = 1000, bool current = false, string? nick = null,
        IEnumerable<HomeworkAutoStatus>? hw = null, HomeworkPoolStatus? boss = null, string[]? inProgress = null, DateTimeOffset? lastSeen = null, int level = 100,
        string title = "", string? map = null, WeightView? weight = null, long silver = 10, long tribute = 1, bool stale = false)
    {
        var seen = lastSeen ?? Now.AddHours(-3);
        return new CharacterView(key, 1, realm, job, nick, current, level, title, map, combat, 3000, 1200, 5000,
            Coin(MeterKind.Silver, silver, seen, live: current), Coin(MeterKind.Tribute, tribute, seen, live: current), weight, current ? Now : seen, stale,
            [.. hw ?? []], boss ?? new HomeworkPoolStatus("unknown", null), inProgress ?? [], 0);
    }

    public static AccountView Acct(string id, params CharacterView[] members) =>
        new(id, members[0].Nickname ?? members[0].Realm, false, new MembershipView(null, false), new CoinCaps(100, 10),
            members.Any(m => m.IsCurrent), -1, members.Max(m => m.Combat), members);

    public static AccountView Acct(string id, int manualRank, params CharacterView[] members) => Acct(id, members) with { ManualRank = manualRank };

    public static CharactersSnapshot Snap(params AccountView[] accounts) => new(Now, accounts.FirstOrDefault(a => a.HasCurrent)?.Id, accounts);

    /// <summary>열 칸 중 지정한 칸만 done인 현황.</summary>
    public static HomeworkAutoStatus[] Done(params string[] ids) => [.. ids.Select(i => St(i, "done"))];

    public static HomeworkAutoStatus[] AllNineDone() => [.. HomeworkBoardLayout.Cells.Where(c => !c.IsBoss).Select(c => St(c.Id, "done"))];
}

/// <summary>쓰기 동작 호출을 기록하는 가짜. 결과는 시나리오가 바꾼다.</summary>
internal sealed class FakeActions : IHomeworkActions
{
    private readonly object _lock = new();
    private readonly List<(string Id, bool? Completed, int? Count, string? Key)> _sets = [];
    private readonly List<(string Key, string? Nickname)> _renames = [];

    public Func<string, HomeworkCommandResult>? OnSet { get; set; }
    public Func<CommandResult>? OnRename { get; set; }

    public IReadOnlyList<(string Id, bool? Completed, int? Count, string? Key)> Sets { get { lock (_lock) return [.. _sets]; } }
    public IReadOnlyList<(string Key, string? Nickname)> Renames { get { lock (_lock) return [.. _renames]; } }

    public HomeworkCommandResult SetHomework(string id, bool? completed, int? count, string? characterKey = null)
    {
        lock (_lock) _sets.Add((id, completed, count, characterKey));
        return OnSet?.Invoke(id) ?? new HomeworkCommandResult(CommandError.None, null, null);
    }

    public HomeworkCommandResult SetSeasonValue(string id, int count, int expected, string characterKey) => SetHomework(id, null, count, characterKey);

    public int SeasonResets { get; private set; }
    public SeasonResetResult ResetSeasonAll(bool saga) { SeasonResets++; return new SeasonResetResult(CommandError.None, null, 2); }

    public CommandResult RenameCharacter(string characterKey, string? nickname)
    {
        lock (_lock) _renames.Add((characterKey, nickname));
        return OnRename?.Invoke() ?? CommandResult.Success;
    }
}
