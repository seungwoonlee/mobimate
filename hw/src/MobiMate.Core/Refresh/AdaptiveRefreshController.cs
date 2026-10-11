using System;

namespace MobiMate;

/// <summary>
/// 탭 자동 새로고침 시 유저 인터랙션 여부에 따라 주기를 동적으로 조절하는 적응형 타이머 컨트롤러.
/// 기본값: 15초
/// 유저 활동 없음: 틱마다 +5초 점진 지연 (최대 30초)
/// 유저 활동 감지: 즉시 15초로 리셋
/// </summary>
public class AdaptiveRefreshController
{
    public const int BaseIntervalSec = 15;
    public const int StepIntervalSec = 5;
    public const int MaxIntervalSec = 30;

    public int CurrentIntervalSec { get; private set; } = BaseIntervalSec;
    public bool HasUserActivity { get; private set; } = false;

    /// <summary>
    /// 키보드 입력, 마우스 클릭 등 유저 활동이 감지되었을 때 호출하여 주기를 기본값(15초)으로 즉시 리셋
    /// </summary>
    public void RecordUserActivity()
    {
        HasUserActivity = true;
        CurrentIntervalSec = BaseIntervalSec;
    }

    /// <summary>
    /// 타이머 틱(새로고침 시점)에 호출되어 다음 대기 주기를 계산하여 반환
    /// </summary>
    public int OnTick()
    {
        if (HasUserActivity)
        {
            CurrentIntervalSec = BaseIntervalSec;
            HasUserActivity = false;
        }
        else
        {
            CurrentIntervalSec = Math.Min(MaxIntervalSec, CurrentIntervalSec + StepIntervalSec);
        }

        return CurrentIntervalSec;
    }

    /// <summary>
    /// 강제 리셋
    /// </summary>
    public void Reset()
    {
        CurrentIntervalSec = BaseIntervalSec;
        HasUserActivity = false;
    }
}
