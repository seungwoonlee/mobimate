namespace MobiMate;

/// <summary>
/// 생활력·매력은 캐릭터를 바꾼 직후 게임이 0(또는 빈 값)으로 줄 때가 있다. 이 값은 줄지 않으므로(CharacterIdentity)
/// 0은 "아직 못 읽음"으로 보고 마지막으로 기록한 값을 쓴다. 기록도 없으면 0이다.
/// </summary>
public static class ScoreReading
{
    public static long OrLast(long? read, long? last) => read is > 0 ? read.Value : last ?? 0;
}
