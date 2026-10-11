using System.Windows;
using MobiMateHomeWork.Layout;

namespace MobiMateHomeWork;

/// <summary>
/// 배치 단계별 리소스(Themes/Tier.*.xaml)를 앱 리소스의 마지막 병합 자리에 바꿔 끼운다.
/// 키는 세 파일이 같고 화면은 DynamicResource로 읽으므로, 바꿔 끼우면 글자 크기·카드 폭 같은 값이 곧바로 따라온다.
/// </summary>
public static class TierResources
{
    public static void Apply(Application app, LayoutTier tier)
    {
        var merged = app.Resources.MergedDictionaries;
        var uri = new Uri($"Themes/{LayoutTiers.ResourceFile(tier)}", UriKind.Relative);
        var next = new ResourceDictionary { Source = uri };
        // 마지막 항목이 단계 파일이다 (App.xaml 참고)
        if (merged.Count == 0) merged.Add(next);
        else merged[^1] = next;
    }
}
