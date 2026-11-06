namespace MobiMate.Web.Voice;

/// <summary>후처리 결과. <see cref="Applied"/>는 적용된 치환 설명(화면에 작게 보인다, FR-VC-08).</summary>
public sealed record VoicePostResult(string Text, IReadOnlyList<string> Applied);

/// <summary>인식 글자 → ① 학습된 치환 ② 띄어쓰기 복원 ③ 정리 (상세설계 §3.9). 순서 고정.</summary>
public interface IVoicePostProcessor
{
    VoicePostResult Process(string raw);
}

/// <summary>V1: 아직 아무것도 하지 않는다(치환·띄어쓰기는 V3·V4에서 붙인다).</summary>
public sealed class PassThroughPostProcessor : IVoicePostProcessor
{
    public VoicePostResult Process(string raw) => new(raw.Trim(), Array.Empty<string>());
}
