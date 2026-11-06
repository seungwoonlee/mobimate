namespace MobiMate.Tests;

/// <summary>
/// 게임 채팅 전송은 매번 게임 안 확인창에서 사용자가 눌러야 나간다 (CLI 명세, 2026-11-06 실사용에서 확인).
/// 호출은 사용자가 누를 때까지 막히고, 취소·시간 초과·호출 제한은 프로세스가 정상 종료한 채로 거절로 돌아온다.
/// </summary>
public class ChatConfirmTests
{
    [Fact]
    public void WriteChat_HasItsOwnLane_SoWaitingForTheUserNeverBlocksOtherQueries()
    {
        Assert.Equal(CliLane.Chat, CliLanes.For("write_chat"));
        Assert.NotEqual(CliLanes.For("get_items"), CliLanes.For("write_chat"));   // 조회와 같은 줄에 서지 않는다
    }

    [Fact]
    public void WriteChat_WaitsLongEnoughForAHumanToPressSend()
    {
        // 8초면 사용자가 확인창을 누르기 전에 앱이 CLI를 죽여 "시간 초과"로 기록한다
        Assert.True(CliLanes.DefaultTimeout("write_chat") >= TimeSpan.FromMinutes(2));
        Assert.True(CliLanes.DefaultTimeout("get_items") < TimeSpan.FromMinutes(1));   // 다른 명령은 그대로 짧다
    }

    [Theory]
    // 명세 형식: status + body
    [InlineData("""{"status":"rejected","body":{"error":"user_declined","message":"x"}}""", "취소")]
    [InlineData("""{"status":"rejected","body":{"error":"confirm_timeout"}}""", "확인하지 않아")]
    [InlineData("""{"status":"rejected","body":{"error":"rate_limited","message":"slow","retryAfterSeconds":7}}""", "7초 뒤")]
    [InlineData("""{"status":"rejected","body":{"error":"inappropriate_text"}}""", "문자 필터")]
    [InlineData("""{"status":"rejected","body":{"error":"blocked"}}""", "막혀")]
    [InlineData("""{"status":"rejected","body":{"error":"not_enough_currency"}}""", "재화")]
    [InlineData("""{"status":"invalid_body","body":{"error":"message_too_long","length":60}}""", "너무 깁니다")]
    // 오류 이름이 최상위에 오는 형식 (다른 명령의 실측 형식)
    [InlineData("""{"error":"user_declined","message":"declined"}""", "취소")]
    [InlineData("""{"error":"rate_limited","retryAfterSeconds":3}""", "3초 뒤")]
    // 처음 보는 오류도 삼키지 않는다
    [InlineData("""{"status":"rejected","body":{"error":"brand_new_error","message":"새 사유"}}""", "brand_new_error")]
    [InlineData("""{"status":"rejected"}""", "rejected")]
    public void Rejections_AreTranslated_NotRecordedAsSuccess(string stdout, string expectedPart)
    {
        var msg = CliEnvelope.ChatRejection(stdout);
        Assert.NotNull(msg);
        Assert.Contains(expectedPart, msg);
    }

    [Theory]
    [InlineData("""{"status":"accepted","body":{"message":"안녕하세요"}}""")]
    [InlineData("""{"status":"accepted"}""")]
    [InlineData("""{"status":"ok"}""")]   // 모르는 정상 값은 거절로 보지 않는다 (가짜 CLI·옛 형식)
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("그냥 글자")]
    [InlineData("[]")]
    [InlineData("{ 깨진 json")]
    public void AcceptedOrUnknownOutput_IsNotARejection(string stdout) => Assert.Null(CliEnvelope.ChatRejection(stdout));

    private sealed class StubCli(CliResult result) : IGameCli
    {
        public bool IsAvailable => true;
        public string CliPath => "stub";
        public CliCommand? Last { get; private set; }
        public Task<CliResult> RunAsync(CliCommand cmd, CancellationToken ct = default) { Last = cmd; return Task.FromResult(result); }
    }

    private static CliResult Exited(string stdout) => new(true, stdout, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));

    [Fact]
    public async Task SendGameChat_ReportsADeclinedPopupAsAFailure()
    {
        // 프로세스는 정상 종료했지만 사용자가 게임에서 취소했다 — 앱 로그에 "성공"으로 남으면 안 된다
        var cli = new StubCli(Exited("""{"status":"rejected","body":{"error":"user_declined"}}"""));
        var (ok, error) = await cli.SendGameChatAsync("안녕하세요");
        Assert.False(ok);
        Assert.Contains("취소", error);
        Assert.Equal("write_chat", cli.Last!.Name);
    }

    [Fact]
    public async Task SendGameChat_ReportsAcceptedAsSuccess()
    {
        var (ok, error) = await new StubCli(Exited("""{"status":"accepted","body":{"message":"안녕하세요"}}""")).SendGameChatAsync("안녕하세요");
        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public async Task SendGameChat_KeepsTheProcessLevelFailureMessage()
    {
        var cli = new StubCli(new CliResult(false, "", "게임 CLI 응답 시간 초과 (타임아웃)", TimeSpan.Zero, TimeSpan.FromMinutes(3), TimedOut: true));
        var (ok, error) = await cli.SendGameChatAsync("안녕하세요");
        Assert.False(ok);
        Assert.Contains("시간 초과", error);
    }
}
