using Xunit;

namespace MobiMate.Tests;

/// <summary>변경 명령 응답 읽기(2026-11-12): 프로세스는 정상 종료해도 게임이 거절·중단했을 수 있다. 이것을 성공으로 알리면 게임은 가만있는데 앱만 "했습니다"가 된다.</summary>
public class CliActionErrorTests
{
    [Theory]
    [InlineData("""{"status":"rejected","body":{"error":"not_completed_yet","message":"still working"}}""", "아직 완료되지 않은 가공물입니다")]
    [InlineData("""{"status":"accepted","body":{"error":"blocked","message":"x","kind":"dialog"}}""", "게임 화면에 확인이 필요한 창이 떠 있습니다. 게임에서 해결한 뒤 다시 해 주세요")]
    [InlineData("""{"status":"accepted","body":{"error":"timeout","message":"unconfirmed"}}""", "게임에서 확인되지 않아 중단되었습니다. 게임에서 직접 확인해 주세요")]
    [InlineData("""{"status":"accepted","body":{"error":"overweight"}}""", "가방이 가득 찼습니다")]
    [InlineData("""{"status":"accepted","body":{"result":"stopped_by_user"}}""", "게임에서 직접 정지해 중단되었습니다")]
    [InlineData("""{"status":"rejected","body":{"error":"invalid_state","message":"nothing"}}""", "지금 멈출 수 있는 행동이 없습니다")]
    [InlineData("""{"error":"blocked","message":"top level"}""", "게임 CLI 오류(blocked): top level")]
    public void Failure_IsReadFromEnvelopeOrTopLevel(string stdout, string expected) => Assert.Equal(expected, CliEnvelope.ActionError(stdout));

    [Fact]
    public void Disconnected_IsAFailure() =>
        Assert.Contains("게임이 꺼져 있거나", CliEnvelope.ActionError("""{"pipe":"disconnected","reason":"game_off"}"""));

    [Fact]
    public void RejectedWithoutAnError_IsStillAFailure() =>
        Assert.Equal("게임이 요청을 거절했습니다", CliEnvelope.ActionError("""{"status":"rejected"}"""));

    [Theory]
    [InlineData("""{"status":"accepted","body":{"collected":"가죽","rewards":[]}}""")]
    [InlineData("""{"status":"accepted","body":{"result":"started"}}""")]
    [InlineData("""{"status":"accepted","body":{"message":"stopped"}}""")]
    [InlineData("""{"result":"ok"}""")]
    [InlineData("")]
    [InlineData("not json")]
    public void Success_OrUnknownShape_IsNotAFailure(string stdout) => Assert.Null(CliEnvelope.ActionError(stdout));
}
