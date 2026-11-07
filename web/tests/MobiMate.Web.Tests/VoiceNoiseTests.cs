using MobiMate.Web.Voice;
using Xunit;

namespace MobiMate.Web.Tests;

public class VoiceNoiseTests
{
    [Theory]
    [InlineData("NO:)안녕", "안녕")]                               // 승운 2026-11-07: 채팅 앞에 붙던 글자
    [InlineData("NO:)뭐라 고말했어", "뭐라 고말했어")]
    [InlineData("안녕하세요 :)", "안녕하세요")]
    [InlineData("OK;( 알겠어요", "알겠어요")]
    [InlineData("힐러 구해요", "힐러 구해요")]                      // 평범한 문장은 그대로
    [InlineData("PC 방에서 AI 쓸게요", "PC 방에서 AI 쓸게요")]       // 영문 낱말은 지우지 않는다
    [InlineData("3:30에 모여요", "3:30에 모여요")]                   // 시각의 ":"도 그대로
    [InlineData("", "")]
    [InlineData("NO:)", "")]
    public void Strip_RemovesEmoticonLikeNoise_AndNothingElse(string input, string expect) => Assert.Equal(expect, VoiceNoise.Strip(input));
}
