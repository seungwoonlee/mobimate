namespace MobiMate.Tests;

/// <summary>게임 CLI 업데이트 감지: 새 명령·사라진 명령·출력 필드 변화 (2026-10-22 승운 요청)</summary>
public class CliCapabilityDiffTests
{
    private static string Json(params (string Cmd, string Desc, string Out)[] cmds) =>
        System.Text.Json.JsonSerializer.Serialize(new { commands = cmds.Select(c => new { Command = c.Cmd, Description = c.Desc, BodyExample = "", OutputExample = c.Out, Note = (string?)null }) });

    private static Dictionary<string, CliCommandInfo> P(params (string, string, string)[] c) => CliCapabilityDiff.Parse(Json(c))!;

    [Fact]
    public void Parse_ReadsCommands_AndRejectsGarbage()
    {
        var map = P(("get_a", "A를 읽는다", "array of { X, Y }"), ("get_b", "B", "{ Z }"));
        Assert.Equal(2, map.Count);
        Assert.Equal("A를 읽는다", map["get_a"].Description);
        Assert.Null(CliCapabilityDiff.Parse(null));
        Assert.Null(CliCapabilityDiff.Parse("not json"));
        Assert.Null(CliCapabilityDiff.Parse("{\"commands\": []}"));
        Assert.Null(CliCapabilityDiff.Parse("[1,2]"));
    }

    [Fact]
    public void Compare_SameThing_HasNoChange()
    {
        var a = P(("get_a", "A", "{ X, Y }"));
        var change = CliCapabilityDiff.Compare(a, P(("get_a", "A", "{ X, Y }")));
        Assert.False(change.Any);
        Assert.Equal("바뀐 것 없음", change.Summary());
    }

    [Fact]
    public void Compare_FindsAddedAndRemovedCommands()
    {
        var change = CliCapabilityDiff.Compare(P(("get_a", "A", "{ X }"), ("get_old", "O", "{ Q }")), P(("get_a", "A", "{ X }"), ("get_new", "N", "{ R }")));
        Assert.Equal(new[] { "get_new" }, change.Added);
        Assert.Equal(new[] { "get_old" }, change.Removed);
        Assert.Empty(change.Changed);
        Assert.Contains("새 명령 1개: get_new", change.Summary());
        Assert.Contains("사라진 명령 1개: get_old", change.Summary());
    }

    [Fact]
    public void Compare_FindsNewAndRemovedOutputFields_AndTextChanges()
    {
        var change = CliCapabilityDiff.Compare(
            P(("get_activity", "활동", "{ IsDead, IsInCombat }"), ("get_x", "설명 1", "{ A }")),
            P(("get_activity", "활동", "{ IsDead, IsInCombat, IsReviving }"), ("get_x", "설명 2", "{ A }")));
        var act = Assert.Single(change.Changed, c => c.Command == "get_activity");
        Assert.Equal(new[] { "IsReviving" }, act.AddedFields);
        Assert.Empty(act.RemovedFields);
        var x = Assert.Single(change.Changed, c => c.Command == "get_x");
        Assert.True(x.TextChanged);
        Assert.Empty(x.AddedFields);
        Assert.Contains("get_activity: 새 필드 IsReviving", change.Summary());
        Assert.Contains("get_x: 설명·메모가 바뀜", change.Summary());
    }
}
