namespace MobiMateHomeWork.Layout;

/// <summary>창 폭으로 정하는 배치 단계 (계획서 §6): 좁게 &lt; 900 ≤ 보통 &lt; 1200 ≤ 넓게.</summary>
public enum LayoutTier { Narrow, Normal, Wide }

public static class LayoutTiers
{
    public const double NormalFrom = 900;
    public const double WideFrom = 1200;

    /// <summary>창 폭(DIP)에서 단계를 구한다. 폭을 모르는 값(0 이하·NaN)은 가장 좁은 단계로 본다.</summary>
    public static LayoutTier For(double widthDip)
    {
        if (double.IsNaN(widthDip) || widthDip < NormalFrom) return LayoutTier.Narrow;
        return widthDip < WideFrom ? LayoutTier.Normal : LayoutTier.Wide;
    }

    /// <summary>단계별 리소스 파일 이름(Themes 폴더). 키는 세 파일이 같다.</summary>
    public static string ResourceFile(LayoutTier tier) => tier switch
    {
        LayoutTier.Narrow => "Tier.Narrow.xaml",
        LayoutTier.Wide => "Tier.Wide.xaml",
        _ => "Tier.Normal.xaml",
    };
}
