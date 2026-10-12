using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using MobiMateHomeWork.Hosting;
using MobiMateHomeWork.Images;

namespace MobiMateHomeWork.Views;

/// <summary>
/// 앱 배경 그림 만들기. 고른 그림(공식 일러스트 / 내 그림)을 한 번만 가공해 얼린 비트맵 하나로 캐시한다 — 창을 키우거나 값이 갱신돼도 다시 그리지 않고 GPU가 늘려 그린다.
/// 가공: (공식 1280×720만) 살짝 블러(반경 5, 4K 4분할에서 늘어나도 흐릿함이 거슬리지 않게) + 본문 가독성을 위한 배경색 그라데이션(위·아래 진하게, 가운데 연하게).
/// 표시는 MainWindow의 Rectangle(Stretch=UniformToFill, 가운데 정렬, 투명도 5~50%).
/// 그림이 없으면(받는 중·끔·읽기 실패) null — 배경 없음.
/// </summary>
public static class Backdrop
{
    private const int MaxDecodeWidth = 1920;
    private static (string Key, ImageBrush? Brush)? _cached;

    /// <summary>설정에 맞는 브러시. 같은 입력이면 캐시된 것을 돌려준다. 내 그림을 못 읽으면 공식 기본 그림으로 대신한다.</summary>
    public static ImageBrush? Create(string mode, string? officialId, string? customPath)
    {
        if (mode == BackgroundSettings.Off) return null;
        var (path, blur) = ResolvePath(mode, officialId, customPath);
        if (path == null) return null;
        var key = $"{path}|{(File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0)}";
        if (_cached is { } c && c.Key == key && c.Brush != null) return c.Brush;
        var source = ImageFiles.Load(path, decodePixelWidth: blur ? 0 : MaxDecodeWidth);
        if (source == null && mode == BackgroundSettings.Custom)   // 내 그림을 못 읽음 → 기본 공식 그림
            return Create(BackgroundSettings.Official, BackgroundCatalog.DefaultId, null);
        if (source == null) return null;
        var brush = new ImageBrush(Compose(source, blur)) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        _cached = (key, brush);
        return brush;
    }

    /// <summary>쓸 파일 경로와 블러 여부. 내 그림 경로가 비었거나 없으면 공식 기본 그림.</summary>
    internal static (string? Path, bool Blur) ResolvePath(string mode, string? officialId, string? customPath)
    {
        if (mode == BackgroundSettings.Custom && !string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath)) return (customPath, false);
        var id = mode == BackgroundSettings.Official ? officialId : BackgroundCatalog.DefaultId;
        var p = ImageFiles.BackgroundPath(BackgroundCatalog.Find(id) != null ? id : BackgroundCatalog.DefaultId);
        return (string.IsNullOrEmpty(p) ? null : p, true);
    }

    /// <summary>원본을 블러·그라데이션으로 가공해 한 장의 얼린 비트맵으로 만든다(UI 스레드).</summary>
    private static BitmapSource Compose(BitmapSource src, bool blur)
    {
        var w = Math.Max(1, src.PixelWidth);
        var h = Math.Max(1, src.PixelHeight);
        const double pad = 14;   // 블러가 가장자리에서 투명으로 번지지 않게 원본을 살짝 키워 가장자리를 가린다
        var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x15)) };
        var image = new Image { Source = src, Stretch = Stretch.Fill, Width = w + pad * 2, Height = h + pad * 2 };
        if (blur) image.Effect = new BlurEffect { Radius = 5, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(image, -pad);
        Canvas.SetTop(image, -pad);
        canvas.Children.Add(image);
        var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xD9, 0x0F, 0x11, 0x15), 0.0));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0x0F, 0x11, 0x15), 0.35));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0x0F, 0x11, 0x15), 0.62));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0x0F, 0x11, 0x15), 1.0));
        canvas.Children.Add(new Rectangle { Width = w, Height = h, Fill = shade });
        canvas.Measure(new Size(w, h));
        canvas.Arrange(new Rect(0, 0, w, h));
        canvas.UpdateLayout();
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(canvas);
        bmp.Freeze();
        return bmp;
    }
}
