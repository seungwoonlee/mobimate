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

    /// <summary>"넓게" 단계의 기준 폭(1280)을 넘으면 칸·줄·카드가 이 비율로 늘어난다. 최대 1.2배(4K 125%의 4분할 약 1536 DIP).</summary>
    public const double WideScaleBase = 1280;
    public const double WideScaleMax = 1.2;

    /// <summary>
    /// 창 폭(DIP)에서 구하는 확대 비율. 넓게 단계이고 폭이 1280을 넘을 때만 1보다 크고, 최대 1.2다. 1/100 단위로 맞춘다(크기를 끌 때 리소스를 자주 바꾸지 않게).
    /// </summary>
    public static double WideScale(LayoutTier tier, double widthDip)
    {
        if (tier != LayoutTier.Wide || double.IsNaN(widthDip) || widthDip <= WideScaleBase) return 1.0;
        return Math.Round(Math.Min(WideScaleMax, widthDip / WideScaleBase), 2);
    }

    /// <summary>단계별 리소스 파일 이름(Themes 폴더). 키는 세 파일이 같다.</summary>
    public static string ResourceFile(LayoutTier tier) => tier switch
    {
        LayoutTier.Narrow => "Tier.Narrow.xaml",
        LayoutTier.Wide => "Tier.Wide.xaml",
        _ => "Tier.Normal.xaml",
    };
}
