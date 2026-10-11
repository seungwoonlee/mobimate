using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MobiMate.IconGen;

/// <summary>
/// "체크된 숙제 칸" 아이콘: 어두운 둥근 타일 + 강조색(#7C9CFF) 테두리 + 안쪽 완료 초록(#3FB984) 칸에 체크.
/// 크기마다 직접 그려(DrawingVisual) 작은 크기에서도 선이 뭉개지지 않게 하고, 16~256px를 한 ico로 묶는다.
/// 작은 크기는 BMP(DIB), 256px는 PNG 항목으로 담는다(오래된 도구와도 호환).
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 48, 64, 256];

    private static readonly Color Tile = Color.FromRgb(0x17, 0x1A, 0x21);
    private static readonly Color Accent = Color.FromRgb(0x7C, 0x9C, 0xFF);
    private static readonly Color Done = Color.FromRgb(0x3F, 0xB9, 0x84);
    private static readonly Color CheckInk = Color.FromRgb(0x0F, 0x11, 0x15);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("사용법: IconGen app.ico경로 [미리보기PNG폴더]"); return 2; }
        var icoPath = Path.GetFullPath(args[0]);
        var previewDir = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
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

        Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);
        File.WriteAllBytes(icoPath, BuildIco(frames));
        Console.WriteLine($"{icoPath} ({new FileInfo(icoPath).Length} bytes, {string.Join("/", Sizes)}px)");
        return 0;
    }

    private static RenderTargetBitmap Render(int px)
    {
        double u = px / 64.0;   // 64 단위 격자로 설계
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // 작은 크기일수록 선을 굵게, 장식은 줄인다
            bool tiny = px <= 24;
            double ring = tiny ? 5 : 4;
            double inset = tiny ? 14 : 15;
            double r = tiny ? 8 : 9;

            // 타일(전체) + 강조 테두리
            var tileRect = new Rect(0.5 * u, 0.5 * u, 63 * u, 63 * u);
            dc.DrawRoundedRectangle(new SolidColorBrush(Tile), new Pen(new SolidColorBrush(Accent), ring * u), Inset(tileRect, ring * u / 2), 13 * u, 13 * u);

            // 완료 칸
            double box0 = inset * u, boxSize = (64 - 2 * inset) * u;
            var box = new Rect(box0, box0, boxSize, boxSize);
            dc.DrawRoundedRectangle(new SolidColorBrush(Done), null, box, r * u, r * u);

            // 체크 표시
            double sw = (tiny ? 6.5 : 5.5) * u;
            var pen = new Pen(new SolidColorBrush(CheckInk), sw) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(24.5 * u, 33 * u), false, false);
                c.LineTo(new Point(29.5 * u, 38.5 * u), true, true);
                c.LineTo(new Point(40 * u, 26 * u), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        RenderOptions.SetEdgeMode(dv, EdgeMode.Unspecified);
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
