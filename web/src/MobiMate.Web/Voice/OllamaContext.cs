namespace MobiMate.Web.Voice;

/// <summary>
/// 음성 다듬기·채팅 꾸미기가 Ollama를 부를 때 쓰는 문맥 길이. 기본값(128k)으로 올리면 모델을 올리는 데 오래 걸리고 메모리도 크게 잡는다
/// (승운 2026-11-07: 다듬기가 2.5초 제한을 자주 넘김). 프롬프트(낱말 목록·예시 포함)가 1천 토큰 안쪽이라 4096이면 충분하다.
/// 호출마다 값이 다르면 Ollama가 모델을 다시 올리므로 예열·다듬기·꾸미기가 모두 이 값을 쓴다.
/// </summary>
public static class OllamaContext
{
    public const int NumCtx = 4096;
}
