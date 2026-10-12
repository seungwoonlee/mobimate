using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MobiMate.IconGen;

/// <summary>
/// 앱 아이콘 생성: 마비노기 모바일 공식 아이콘의 문양을 변형해 쓴다 (아래 LoadKnot 설명 참고).
/// 크기마다 직접 그려(DrawingVisual) 작은 크기에서도 선이 뭉개지지 않게 하고, 16~256px를 한 ico로 묶는다.
/// 작은 크기는 BMP(DIB), 256px는 PNG 항목으로 담는다(오래된 도구와도 호환).
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 48, 64, 256];
    private static readonly int[] ExtraPngSizes = [180, 192, 512];   // 웹앱 PWA·홈 화면 아이콘

    private static readonly Color Tile = Color.FromRgb(0x17, 0x1A, 0x21);
    private static readonly Color Accent = Color.FromRgb(0x7C, 0x9C, 0xFF);
    private static readonly Color Done = Color.FromRgb(0x3F, 0xB9, 0x84);
    private static readonly Color CheckInk = Color.FromRgb(0x0F, 0x11, 0x15);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("사용법: IconGen app.ico경로 [미리보기PNG폴더] [hw|web] 원본PNG"); return 2; }
        var icoPath = Path.GetFullPath(args[0]);
        var previewDir = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
        if (args.Length > 2) _variant = args[2];
        if (args.Length < 4) { Console.Error.WriteLine("원본 아이콘 PNG 경로(네 번째 인자)가 필요합니다."); return 2; }
        LoadKnot(Path.GetFullPath(args[3]));
        if (previewDir != null) Directory.CreateDirectory(previewDir);

        var frames = new List<(int Size, byte[] Png, byte[] Bgra)>();
        foreach (var s in Sizes)
        {
            var bmp = Render(s);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            var png = ms.ToArray();
            var bgra = new byte[s * s * 4];
            bmp.CopyPixels(bgra, s * 4, 0);   // Pbgra32 → 투명 영역은 0이라 BMP 항목에는 비미리곱으로 바꿔야 한다
            Unpremultiply(bgra);
            frames.Add((s, png, bgra));
            if (previewDir != null) File.WriteAllBytes(Path.Combine(previewDir, $"icon-{s}.png"), png);
        }

        if (previewDir != null)
            foreach (var s in ExtraPngSizes)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(Render(s)));
                using var fs = File.Create(Path.Combine(previewDir, $"icon-{s}.png"));
                enc.Save(fs);
            }
        Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);
        File.WriteAllBytes(icoPath, BuildIco(frames));
        Console.WriteLine($"{icoPath} ({new FileInfo(icoPath).Length} bytes, {string.Join("/", Sizes)}px)");
        return 0;
    }

    private static string _variant = "hw";
    private static BitmapSource? _knot;   // 공식 아이콘의 흰 문양만 뽑아 금빛 그라데이션으로 다시 칠한 비트맵

    /// <summary>
    /// 마비노기 모바일 공식 앱 아이콘(주황 바탕 + 흰 매듭 문양)에서 문양만 마스크로 뽑아 변형한다:
    /// 남색 타일 + 금빛 그라데이션 문양 + 테두리. 숙제 앱(hw)은 초록 테두리와 완료 배지(체크)를 더해 웹앱과 구분한다.
    /// 원본 PNG 경로는 네 번째 인자다(저장소에는 넣지 않는다).
    /// </summary>
    private static void LoadKnot(string sourcePng)
    {
        var src = new BitmapImage();
        src.BeginInit();
        src.UriSource = new Uri(sourcePng);
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.EndInit();
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);
        // 문양 가장자리 상자를 구해 가운데 맞춘다
        int minX = w, minY = h, maxX = 0, maxY = 0;
        var alpha = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                // 주황(255,102,0) → 흰(255,255,255): 파랑 채널이 0에서 255로 올라간다. 투명 모서리는 알파 0.
                int a = px[i + 3] == 0 ? 0 : px[i];   // B
                alpha[y * w + x] = (byte)a;
                if (a > 40) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            }
        int bw = maxX - minX + 1, bh = maxY - minY + 1, side = Math.Max(bw, bh);
        int ox = minX - (side - bw) / 2, oy = minY - (side - bh) / 2;
        var outPx = new byte[side * side * 4];
        var top = (R: 0xFF, G: 0xE0, B: 0x8A);   // 금빛 위
        var bot = (R: 0xFF, G: 0x8A, B: 0x1E);   // 주황 아래
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int sx = ox + x, sy = oy + y;
                int a = sx < 0 || sy < 0 || sx >= w || sy >= h ? 0 : alpha[sy * w + sx];
                double t = y / (double)(side - 1);
                int r = (int)(top.R + (bot.R - top.R) * t), g = (int)(top.G + (bot.G - top.G) * t), b = (int)(top.B + (bot.B - top.B) * t);
                int o = (y * side + x) * 4;
                outPx[o] = (byte)(b * a / 255); outPx[o + 1] = (byte)(g * a / 255); outPx[o + 2] = (byte)(r * a / 255); outPx[o + 3] = (byte)a;   // 미리곱
            }
        var bmp = BitmapSource.Create(side, side, 96, 96, PixelFormats.Pbgra32, null, outPx, side * 4);
        bmp.Freeze();
        _knot = bmp;
    }

    private static RenderTargetBitmap Render(int px)
    {
        double u = px / 64.0;   // 64 단위 격자로 설계
        bool tiny = px <= 24;
        var ringColor = _variant == "hw" ? Color.FromRgb(0x3F, 0xB9, 0x84) : Color.FromRgb(0xFF, 0x6A, 0x00);
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.Fant);
        using (var dc = dv.RenderOpen())
        {
            var bg = new LinearGradientBrush(Color.FromRgb(0x2B, 0x3A, 0x7A), Color.FromRgb(0x10, 0x14, 0x2E), 90);
            double ring = tiny ? 4.5 : 3.5;
            var tileRect = new Rect(0.5 * u, 0.5 * u, 63 * u, 63 * u);
            dc.DrawRoundedRectangle(bg, new Pen(new SolidColorBrush(ringColor), ring * u), Inset(tileRect, ring * u / 2), 14 * u, 14 * u);

            // 문양: 타일 안에 크게. 숙제 앱은 배지 자리를 위해 살짝 왼쪽 위로 올린다.
            double size = (tiny ? 46 : 42) * u;
            double off = _variant == "hw" ? -1.5 * u : 0;
            double x0 = (64 * u - size) / 2 + off, y0 = (64 * u - size) / 2 + off;
            dc.DrawImage(_knot!, new Rect(x0, y0, size, size));

            if (_variant == "hw")
            {
                double r = (tiny ? 10.5 : 9.5) * u;
                var center = new Point(49 * u, 49 * u);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x84)), new Pen(new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x2E)), 2.4 * u), center, r, r);
                var cp = new Pen(new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x15)), (tiny ? 4.6 : 3.8) * u) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                var ck = new StreamGeometry();
                using (var c = ck.Open())
                {
                    c.BeginFigure(new Point(44.4 * u, 49.2 * u), false, false);
                    c.LineTo(new Point(47.6 * u, 52.4 * u), true, true);
                    c.LineTo(new Point(54 * u, 45 * u), true, true);
                }
                ck.Freeze();
                dc.DrawGeometry(null, cp, ck);
            }
        }
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private static Rect Inset(Rect r, double d) => new(r.X + d, r.Y + d, r.Width - 2 * d, r.Height - 2 * d);

    private static void Unpremultiply(byte[] bgra)
    {
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            if (a is 0 or 255) continue;
            bgra[i] = (byte)Math.Min(255, bgra[i] * 255 / a);
            bgra[i + 1] = (byte)Math.Min(255, bgra[i + 1] * 255 / a);
            bgra[i + 2] = (byte)Math.Min(255, bgra[i + 2] * 255 / a);
        }
    }

    private static byte[] BuildIco(List<(int Size, byte[] Png, byte[] Bgra)> frames)
    {
        var images = new List<byte[]>();
        foreach (var (size, png, bgra) in frames)
        {
            if (size >= 256) { images.Add(png); continue; }
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = size - 1; y >= 0; y--) w.Write(bgra, y * size * 4, size * 4);   // 아래에서 위로
            int maskRow = ((size + 31) / 32) * 4;
            w.Write(new byte[maskRow * size]);                                            // AND 마스크(알파를 쓰므로 0)
            images.Add(ms.ToArray());
        }

        using var o = new MemoryStream();
        using var bw = new BinaryWriter(o);
        bw.Write((short)0); bw.Write((short)1); bw.Write((short)frames.Count);
        int offset = 6 + 16 * frames.Count;
        for (int i = 0; i < frames.Count; i++)
        {
            int s = frames[i].Size;
            bw.Write((byte)(s >= 256 ? 0 : s)); bw.Write((byte)(s >= 256 ? 0 : s));
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
            bw.Write(images[i].Length); bw.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images) bw.Write(img);
        return o.ToArray();
    }
}
