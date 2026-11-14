using System.Windows;
using MobiMateHomeWork.Layout;

namespace MobiMateHomeWork;

/// <summary>
/// 배치 단계별 리소스(Themes/Tier.*.xaml)를 앱 리소스의 마지막 병합 자리에 바꿔 끼운다.
/// 키는 세 파일이 같고 화면은 DynamicResource로 읽으므로, 바꿔 끼우면 글자 크기·카드 폭 같은 값이 곧바로 따라온다.
/// 넓은 단계에서 창이 1280을 넘으면(4K 4분할 등) 칸·줄·카드 크기를 창 폭에 비례해 키운다 — 앱 리소스 맨 위 키로 덮어쓴다(<see cref="ApplyScale"/>).
/// </summary>
public static class TierResources
{
    /// <summary>비율로 키우는 값(Wide 단계 파일의 키). 글자 크기는 키우지 않는다.</summary>
    private static readonly string[] ScaledDoubles = ["Metrics.RowHeight", "Metrics.CellWidth", "Metrics.CellHeight", "Metrics.BadgeSize", "Metrics.JobFigureWidth", "Metrics.JobFigureHeight", "Metrics.CardFigureWidth", "Metrics.CardFigureHeight", "Metrics.CharInfoWidth", "Metrics.TableMaxWidth"];
    private static (LayoutTier Tier, double Scale)? _applied;   // 마지막으로 적용한 (단계, 비율): 같으면 리소스를 다시 쓰지 않는다
    private const string CardWidthKey = "Metrics.CardWidth";

    public static void Apply(Application app, LayoutTier tier, double widthDip = 0)
    {
        var merged = app.Resources.MergedDictionaries;
        var uri = new Uri($"Themes/{LayoutTiers.ResourceFile(tier)}", UriKind.Relative);
        var next = new ResourceDictionary { Source = uri };
        // 마지막 항목이 단계 파일이다 (App.xaml 참고)
        if (merged.Count == 0) merged.Add(next);
        else merged[^1] = next;
        ClearScale(app);
        _applied = (tier, 1.0);
        ApplyScale(app, tier, widthDip);
    }

    /// <summary>창 폭이 바뀔 때: Wide 단계이고 1280보다 넓으면 비율 값을 덮어쓰고, 아니면 덮어쓴 것을 치운다.</summary>
    public static void ApplyScale(Application app, LayoutTier tier, double widthDip)
    {
        var scale = LayoutTiers.WideScale(tier, widthDip);
        if (_applied == (tier, scale)) return;   // 크기를 끄는 동안 같은 값을 다시 쓰지 않는다
        _applied = (tier, scale);
        if (scale <= 1.0)
        {
            ClearScale(app);
            return;
        }
        var baseDict = app.Resources.MergedDictionaries.Count > 0 ? app.Resources.MergedDictionaries[^1] : null;
        if (baseDict == null) return;
        foreach (var key in ScaledDoubles)
            if (baseDict[key] is double d) app.Resources[key] = Math.Round(d * scale);
        if (baseDict[CardWidthKey] is GridLength g) app.Resources[CardWidthKey] = new GridLength(Math.Round(g.Value * scale));
    }

    private static void ClearScale(Application app)
    {
        foreach (var key in ScaledDoubles) app.Resources.Remove(key);
        app.Resources.Remove(CardWidthKey);
    }
}
