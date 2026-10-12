using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MobiMateHomeWork.Hosting;

/// <summary>
/// 트레이 아이콘 (웹앱 Tray.cs를 숙제 앱에 맞춰 줄였다): 메뉴 = 열기 / 항상 위(체크) / 종료, 더블클릭 = 열기.
/// WPF UI 스레드에서 만든다(NotifyIcon은 만든 스레드의 메시지 루프를 쓰고, WPF Dispatcher가 그 루프를 돈다).
/// 아이콘은 앱 프로젝트의 app.ico(hw/tools/IconGen이 그린 것)를 임베디드 리소스로 읽는다.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _topItem;
    private readonly Drawing.Icon _image;
    private bool _disposed;

    public TrayIcon(Action open, Func<bool> getAlwaysOnTop, Action<bool> setAlwaysOnTop, Action quit)
    {
        _image = Load();
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

        _icon = new Forms.NotifyIcon { Icon = _image, Text = "MobiMate Homework", ContextMenuStrip = menu, Visible = true };
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

    /// <summary>
    /// 임베디드 app.ico에서 트레이 크기(DPI 반영)에 맞는 그림을 고른다. 읽지 못하면 시스템 기본 아이콘.
    /// </summary>
    internal static Drawing.Icon Load()
    {
        try
        {
            using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("app.ico");
            if (stream != null) return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception) { /* 아이콘을 못 읽어도 트레이는 떠야 한다 */ }
        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

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
