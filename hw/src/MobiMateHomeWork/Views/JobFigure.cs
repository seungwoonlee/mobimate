using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MobiMateHomeWork.Images;

namespace MobiMateHomeWork.Views;

/// <summary>
/// 직업 표시: 직업 캐릭터 전신 이미지(세로형 썸네일, Stretch=Uniform, 아래 정렬)를 직업 색의 은은한 둥근 사각형 배경 위에 보여 준다.
/// 직업은 얼굴이 아니라 전체 실루엣으로 구분하므로 자르지 않는다. 이미지가 없으면(아직 내려받는 중이거나 모르는 직업) 직업 색 원+첫 글자.
/// 크기는 Width/Height로 정한다(단계별 리소스). 디코드는 ImageFiles가 캐시한다.
/// </summary>
public sealed class JobFigure : Grid
{
    public static readonly DependencyProperty ImageIdProperty = DependencyProperty.Register(nameof(ImageId), typeof(string), typeof(JobFigure), new PropertyMetadata(null, OnChanged));
    public static readonly DependencyProperty LetterProperty = DependencyProperty.Register(nameof(Letter), typeof(string), typeof(JobFigure), new PropertyMetadata("?", OnChanged));
    public static readonly DependencyProperty JobColorProperty = DependencyProperty.Register(nameof(JobColor), typeof(string), typeof(JobFigure), new PropertyMetadata(null, OnChanged));
    /// <summary>이미지가 없을 때 쓰는 원의 지름.</summary>
    public static readonly DependencyProperty FallbackSizeProperty = DependencyProperty.Register(nameof(FallbackSize), typeof(double), typeof(JobFigure), new PropertyMetadata(26.0, OnChanged));
    /// <summary>true면 카드용(큰 디코드), 아니면 표 줄용.</summary>
    public static readonly DependencyProperty LargeProperty = DependencyProperty.Register(nameof(Large), typeof(bool), typeof(JobFigure), new PropertyMetadata(false, OnChanged));

    private readonly Border _plate = new() { CornerRadius = new CornerRadius(6), Opacity = 0.24, Visibility = Visibility.Collapsed };
    private readonly Image _image = new() { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(1) };
    private readonly Border _circle = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _letter = new() { FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public string? ImageId { get => (string?)GetValue(ImageIdProperty); set => SetValue(ImageIdProperty, value); }
    public string Letter { get => (string)GetValue(LetterProperty); set => SetValue(LetterProperty, value); }
    public string? JobColor { get => (string?)GetValue(JobColorProperty); set => SetValue(JobColorProperty, value); }
    public double FallbackSize { get => (double)GetValue(FallbackSizeProperty); set => SetValue(FallbackSizeProperty, value); }
    public bool Large { get => (bool)GetValue(LargeProperty); set => SetValue(LargeProperty, value); }

    public JobFigure()
    {
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _circle.Child = _letter;
        Children.Add(_plate);
        Children.Add(_image);
        Children.Add(_circle);
        Loaded += (_, _) => { ImageFiles.Changed += OnFilesChanged; Update(); };
        Unloaded += (_, _) => ImageFiles.Changed -= OnFilesChanged;
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((JobFigure)d).Update();

    private void OnFilesChanged() => Dispatcher.BeginInvoke(new Action(Update));

    private static readonly Dictionary<string, Brush> Brushes = [];

    private static Brush? BrushOf(string? color)
    {
        if (string.IsNullOrEmpty(color)) return null;
        if (Brushes.TryGetValue(color, out var b)) return b;
        try
        {
            b = (Brush)new BrushConverter().ConvertFromString(color)!;
            if (b.CanFreeze) b.Freeze();
            return Brushes[color] = b;
        }
        catch (FormatException) { return null; }
    }

    private void Update()
    {
        var brush = BrushOf(JobColor);
        var img = string.IsNullOrEmpty(ImageId) ? null : ImageFiles.Load(ImageFiles.ClassImagePath(ImageId), decodePixelHeight: Large ? 192 : 112);
        if (img != null)
        {
            _image.Source = img;
            _image.Visibility = Visibility.Visible;
            _plate.Background = brush;
            _plate.Visibility = Visibility.Visible;
            _circle.Visibility = Visibility.Collapsed;
            return;
        }
        _image.Source = null;
        _image.Visibility = Visibility.Collapsed;
        _plate.Visibility = Visibility.Collapsed;
        _circle.Visibility = Visibility.Visible;
        var size = FallbackSize;
        _circle.Width = _circle.Height = size;
        _circle.CornerRadius = new CornerRadius(size);
        _circle.Background = brush;
        _letter.Text = Letter;
        _letter.FontSize = Math.Max(11, size * 0.5);
        _letter.Foreground = TryFindResource("Brush.OnAccent") as Brush ?? System.Windows.Media.Brushes.Black;
    }
}
