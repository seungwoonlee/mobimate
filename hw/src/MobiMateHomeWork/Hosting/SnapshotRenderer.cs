using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MobiMateHomeWork.Hosting;

/// <summary>
/// 화면 확인용 숨은 실행 옵션 <c>--render-snapshots &lt;폴더&gt;</c>.
/// 메인 창을 화면 밖에서 지정한 크기로 띄워 PNG로 저장하고 끝난다. 반드시 <c>--storage</c>(임시 폴더)를 함께 줘야 하고(실제 %APPDATA%\MobiMateWeb는 쓰지 않는다),
/// 저장 폴더에 캐릭터 기록이 없으면 데모 데이터(계정 2개, 한 계정 6캐릭터)를 채운다. 게임은 <c>--cli</c>로 가짜 CLI를 가리켜 둔다.
/// PNG 한 픽셀 = 1 DIP(96dpi로 그린다).
/// </summary>
public static class SnapshotRenderer
{
    /// <summary>계획서 §6의 4분할 크기 5종 (DIP).</summary>
    public static readonly IReadOnlyList<(int Width, int Height)> Sizes =
        [(768, 405), (853, 458), (960, 510), (1024, 552), (1280, 690)];

    /// <summary>저장 폴더에 데모 캐릭터 기록을 채운다(이미 기록이 있으면 건드리지 않는다).</summary>
    public static void SeedDemo(string storageDir)
    {
        var path = Path.Combine(storageDir, "character_history_db.json");
        if (File.Exists(path)) return;
        var now = DateTime.Now;
        // 같은 계정 = 데카·M캐시가 같은 캐릭터들 (접속 중인 가짜 CLI 캐릭터 "아이라 격투가"는 데카 10285·M캐시 5619라 첫 계정에 묶인다)
        var profiles = new List<CharacterProfile>
        {
            Demo("바람", "도적", "그림자", 100, 99_000, 10285, 5619, now.AddHours(-3)),
            Demo("바람", "궁수", "화살비", 95, 84_000, 10285, 5619, now.AddHours(-9)),
            Demo("아이라", "마법사", "", 90, 76_000, 10285, 5619, now.AddDays(-1)),
            Demo("아이라", "성직자", "치유사", 88, 70_500, 10285, 5619, now.AddDays(-2)),
            Demo("바람", "전사", "", 85, 61_000, 10285, 5619, now.AddDays(-4)),
            Demo("류트", "음유시인", "노래꾼", 92, 80_000, 777, 888, now.AddHours(-30)),
            Demo("류트", "검사", "", 80, 52_000, 777, 888, now.AddDays(-5)),
            Demo("류트", "연금술사", "연금왕", 70, 40_000, 777, 888, now.AddDays(-9)),
        };
        Directory.CreateDirectory(storageDir);
        File.WriteAllText(path, JsonSerializer.Serialize(profiles));
    }

    private static CharacterProfile Demo(string realm, string job, string nick, int level, long combat, long deca, long mcash, DateTime lastSeen) => new()
    {
        CharacterKey = $"{realm}_{job}", RealmName = realm, JobName = job, CustomName = nick,
        FirstSeen = lastSeen.AddDays(-30), LastSeen = lastSeen,
        History =
        [
            new CharacterHistoryRecord
            {
                Timestamp = lastSeen, Level = level, Title = "", CombatScore = combat, ArcaneResistance = 3000, LivingScore = 1200, AttractivenessScore = 5000,
                Deca = deca, MCash = mcash, SilverCoin = 60, DemonTribute = 20, WeightCurrent = 420, Gold = 800_000,
            },
        ],
    };

    /// <summary>창을 크기마다 한 번씩 그려 <paramref name="outDir"/>에 <c>home-WxH.png</c>로 저장한다. 저장한 파일 경로를 돌려준다.</summary>
    public static async Task<IReadOnlyList<string>> RenderAsync(IServiceProvider services, string outDir, Action<string>? log = null)
    {
        Directory.CreateDirectory(outDir);
        var saved = new List<string>();
        var window = new MainWindow(services, preview: true)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        window.Show();
        try
        {
            await window.Started;
            foreach (var (w, h) in Sizes)
            {
                window.Width = w;
                window.Height = h;
                await SettleAsync(window);
                var frame = (FrameworkElement)window.Content;
                var pxW = (int)Math.Round(frame.ActualWidth);
                var pxH = (int)Math.Round(frame.ActualHeight);
                var bmp = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(frame);
                var file = Path.Combine(outDir, $"home-{w}x{h}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(file)) encoder.Save(fs);
                saved.Add(file);
                log?.Invoke($"{w}x{h} → 배치 단계 {window.ViewModel.Tier}, 그린 크기 {pxW}x{pxH}: {file}");
            }
        }
        finally { window.Close(); }
        return saved;
    }

    /// <summary>레이아웃·바인딩·리소스 교체가 끝날 때까지 UI 스레드가 한가해지기를 몇 번 기다린다.</summary>
    private static async Task SettleAsync(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }
}
