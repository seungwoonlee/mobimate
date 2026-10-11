using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MobiMateHomeWork.Hosting;

/// <summary>
/// 트레이 아이콘 (웹앱 Tray.cs를 숙제 앱에 맞춰 줄였다): 메뉴 = 열기 / 항상 위(체크) / 종료, 더블클릭 = 열기.
/// WPF UI 스레드에서 만든다(NotifyIcon은 만든 스레드의 메시지 루프를 쓰고, WPF Dispatcher가 그 루프를 돈다).
/// 아이콘 그림은 일단 코드로 그린 단순한 것이다(정식 아이콘은 H5).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _topItem;
    private readonly Drawing.Icon _image;
    private bool _disposed;

    public TrayIcon(Action open, Func<bool> getAlwaysOnTop, Action<bool> setAlwaysOnTop, Action quit)
    {
        _image = Draw();
        var menu = new Forms.ContextMenuStrip
        {
            BackColor = Drawing.Color.FromArgb(0x1E, 0x22, 0x2B),
            ForeColor = Drawing.Color.FromArgb(0xE6, 0xE8, 0xEC),
            ShowImageMargin = false,
            ShowCheckMargin = true,
        };
        menu.Renderer = new DarkRenderer();
        menu.Items.Add("열기", null, (_, _) => open());
        _topItem = new Forms.ToolStripMenuItem("항상 위") { CheckOnClick = false };
        _topItem.Click += (_, _) => setAlwaysOnTop(!getAlwaysOnTop());
        menu.Items.Add(_topItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => quit());
        menu.Opening += (_, _) => _topItem.Checked = getAlwaysOnTop();

        _icon = new Forms.NotifyIcon { Icon = _image, Text = "MobiMate 숙제", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => open();
    }

    /// <summary>항상 위 체크 표시를 맞춘다(설정 창에서 바뀐 경우).</summary>
    public void SetAlwaysOnTop(bool value) => _topItem.Checked = value;

    /// <summary>풍선 알림.</summary>
    public void ShowHint(string title, string text)
    {
        if (_disposed) return;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = Forms.ToolTipIcon.None;
        _icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;   // 프로세스가 끝나도 아이콘이 남지 않게 먼저 숨긴다
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }

    /// <summary>강조색 둥근 사각형 안에 체크 표시. 16×16 한 장이면 트레이에서 충분하다.</summary>
    private static Drawing.Icon Draw()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var path = new Drawing.Drawing2D.GraphicsPath();
            const int r = 8, size = 30;
            path.AddArc(1, 1, r * 2, r * 2, 180, 90);
            path.AddArc(1 + size - r * 2, 1, r * 2, r * 2, 270, 90);
            path.AddArc(1 + size - r * 2, 1 + size - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(1, 1 + size - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x7C, 0x9C, 0xFF));
            g.FillPath(fill, path);
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(0x0F, 0x11, 0x15), 4f) { StartCap = Drawing.Drawing2D.LineCap.Round, EndCap = Drawing.Drawing2D.LineCap.Round, LineJoin = Drawing.Drawing2D.LineJoin.Round };
            g.DrawLines(pen, [new Drawing.PointF(9, 16.5f), new Drawing.PointF(14, 21.5f), new Drawing.PointF(23, 10.5f)]);
        }
        var handle = bmp.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();   // 복제본은 핸들을 따로 갖는다 → 원래 핸들은 바로 해제
        }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>어두운 메뉴: 올린 항목만 약간 밝게, 테두리는 선 색.</summary>
    private sealed class DarkRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        private sealed class DarkColors : Forms.ProfessionalColorTable
        {
            public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(0x2A, 0x2F, 0x3A);
            public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(0x2A, 0x2F, 0x3A);
            public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(0x2A, 0x2F, 0x3A);
            public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.FromArgb(0x1E, 0x22, 0x2B);
            public override Drawing.Color ImageMarginGradientBegin => Drawing.Color.FromArgb(0x1E, 0x22, 0x2B);
            public override Drawing.Color ImageMarginGradientMiddle => Drawing.Color.FromArgb(0x1E, 0x22, 0x2B);
            public override Drawing.Color ImageMarginGradientEnd => Drawing.Color.FromArgb(0x1E, 0x22, 0x2B);
            public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(0x2A, 0x2F, 0x3A);
            public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(0x2A, 0x2F, 0x3A);
            public override Drawing.Color CheckBackground => Drawing.Color.FromArgb(0x26, 0x7C, 0x9C, 0xFF);
            public override Drawing.Color CheckSelectedBackground => Drawing.Color.FromArgb(0x26, 0x7C, 0x9C, 0xFF);
        }
    }
}
