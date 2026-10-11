using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MobiMateHomeWork.ViewModels;

namespace MobiMateHomeWork.Hosting;

/// <summary>
/// 화면 확인용 숨은 실행 옵션 <c>--render-snapshots &lt;폴더&gt;</c>.
/// 메인 창을 화면 밖에서 지정한 크기로 띄워 PNG로 저장하고 끝난다. 반드시 <c>--storage</c>(임시 폴더)를 함께 줘야 하고(실제 %APPDATA%\MobiMateWeb는 쓰지 않는다),
/// 저장 폴더에 기록이 없으면 데모 데이터(계정 2개: 6캐릭터 + 8캐릭터, 숙제 상태가 섞인 기록)를 채운다. 게임은 <c>--cli</c>로 가짜 CLI를 가리켜 둔다.
/// PNG 한 픽셀 = 1 DIP(96dpi로 그린다).
/// </summary>
public static class SnapshotRenderer
{
    /// <summary>계획서 §6의 4분할 크기 5종 + 4K 125%의 4분할(약 1536×840) (DIP).</summary>
    public static readonly IReadOnlyList<(int Width, int Height)> Sizes =
        [(768, 405), (853, 458), (960, 510), (1024, 552), (1280, 690), (1536, 840)];

    /// <summary>필드 보스 팝업·이름 바꾸기 화면을 찍는 크기(보통 단계).</summary>
    public static readonly (int Width, int Height) ExtrasSize = (960, 510);

    /// <summary>
    /// 저장 폴더에 데모 기록을 채운다(이미 있는 파일은 건드리지 않는다): 캐릭터 기록(계정 둘 — 한 계정 6명(접속 중 가짜 CLI 캐릭터 포함), 다른 계정 8명),
    /// 숙제 기록(완료·미완료·미확인·남은 횟수·제안·필드 보스가 섞이게), 앱 설정(마지막 맵·가방 최대 무게 일부).
    /// </summary>
    public static void SeedDemo(string storageDir)
    {
        Directory.CreateDirectory(storageDir);
        var now = DateTime.Now;
        // 같은 계정 = 데카·M캐시가 같은 캐릭터들 (접속 중인 가짜 CLI 캐릭터 "아이라 격투가"는 데카 10285·M캐시 5619라 첫 계정에 묶인다)
        var profiles = new List<CharacterProfile>
        {
            Demo("바람", "도적", "그림자", "잠입의 달인", 100, 99_000, 10285, 5619, now.AddHours(-3), silver: 98, tribute: 8, weight: 980),
            Demo("바람", "궁수", "화살비", "", 95, 84_000, 10285, 5619, now.AddHours(-9), silver: 140, tribute: 10, weight: 610),
            Demo("아이라", "마법사", "", "별을 모으는 자", 90, 76_000, 10285, 5619, now.AddDays(-1), silver: 20, tribute: 2, weight: 300),
            Demo("아이라", "성직자", "치유사", "", 88, 70_500, 10285, 5619, now.AddDays(-2), silver: 60, tribute: 5, weight: 1010),
            Demo("바람", "전사", "", "", 85, 61_000, 10285, 5619, now.AddDays(-4), silver: 100, tribute: 12, weight: 420),
            Demo("류트", "음유시인", "노래꾼", "봄바람", 92, 80_000, 777, 888, now.AddHours(-30), silver: 45, tribute: 3, weight: 500),
            Demo("류트", "검사", "", "", 80, 52_000, 777, 888, now.AddDays(-5), silver: 70, tribute: 6, weight: 150),
            Demo("류트", "연금술사", "연금왕", "", 70, 40_000, 777, 888, now.AddDays(-9), silver: 10, tribute: 1, weight: 90),
            Demo("에린", "힐러", "", "", 78, 48_000, 777, 888, now.AddHours(-6), silver: 130, tribute: 9, weight: 700),
            Demo("에린", "듀얼블레이드", "쌍검", "", 76, 45_500, 777, 888, now.AddHours(-12), silver: 55, tribute: 4, weight: 410),
            Demo("에린", "화염술사", "", "", 74, 43_000, 777, 888, now.AddDays(-2), silver: 25, tribute: 7, weight: 260),
            Demo("에린", "석궁사수", "", "", 72, 41_500, 777, 888, now.AddDays(-3), silver: 80, tribute: 2, weight: 330),
            Demo("류트", "수도사", "", "", 66, 36_000, 777, 888, now.AddDays(-6), silver: 90, tribute: 10, weight: 140),
        };
        var path = Path.Combine(storageDir, "character_history_db.json");
        if (!File.Exists(path)) File.WriteAllText(path, JsonSerializer.Serialize(profiles));
        SeedHomework(storageDir, profiles.Select(p => p.CharacterKey).Append("아이라_격투가").ToList());

        var settings = new AppSettingsStore(storageDir);
        foreach (var (key, map, max) in new (string, string?, double)[]
        {
            ("바람_도적", "콜헨", 1200), ("바람_궁수", "던바튼", 1000), ("바람_전사", null, 1000), ("아이라_마법사", "티르 코네일", 900),
            ("류트_음유시인", "탈틴", 1000), ("에린_힐러", "발레스", 1100),
        })
            settings.RememberCharacter(key, map, max);
    }

    private static CharacterProfile Demo(string realm, string job, string nick, string title, int level, long combat, long deca, long mcash, DateTime lastSeen,
        long silver, long tribute, double weight) => new()
    {
        CharacterKey = $"{realm}_{job}", RealmName = realm, JobName = job, CustomName = nick,
        FirstSeen = lastSeen.AddDays(-30), LastSeen = lastSeen,
        History =
        [
            new CharacterHistoryRecord
            {
                Timestamp = lastSeen, Level = level, Title = title, CombatScore = combat, ArcaneResistance = 3000 + combat / 100, LivingScore = 1200, AttractivenessScore = 5000,
                Deca = deca, MCash = mcash, SilverCoin = silver, DemonTribute = tribute, WeightCurrent = weight, Gold = 800_000,
            },
        ],
    };

    /// <summary>
    /// 숙제 기록 데모(없을 때만): 캐릭터마다 칸 상태가 달라지게 짠다 — 모두 완료한 줄, 일부 완료·미완료·미확인, 뱅가드 남은 횟수, 완료 제안, 필드 보스 완료/미완료.
    /// 이번 주기의 리셋 기록을 달아 두어 "이번 주기에 확인한 값"으로 읽힌다. 장부가 없는 캐릭터는 전부 미확인이다.
    /// </summary>
    private static void SeedHomework(string storageDir, IReadOnlyList<string> keys)
    {
        var path = Path.Combine(storageDir, "homework_records.json");
        if (File.Exists(path)) return;
        var now = DateTimeOffset.UtcNow;
        var file = new HomeworkFile();
        file.Account.LastDailyResetUtc = KstClock.LastDailyReset(now);
        file.Account.LastWeeklyResetUtc = KstClock.LastWeeklyReset(now);
        const string Vanguard = "weekly_vanguard_breach", Guild = "weekly_guild_regular", Day = "daily_day_dungeon";
        const string Cavrak = "raid_cavrak", Airel = "raid_airel", White = "raid_white_succubus";
        const string Anchor = "abyss_illusory_anchorage", Cave = "abyss_madness_cave", Water = "abyss_scattered_waterway";

        HomeworkLedger NewLedger() => new() { LastDailyResetUtc = KstClock.LastDailyReset(now), LastWeeklyResetUtc = KstClock.LastWeeklyReset(now) };
        void Done(HomeworkLedger l, string id, string? evidence = null) => l.Items[id] = new HomeworkItemState
        {
            Completed = true, Count = 1, Goal = 1, CompletedAtUtc = now.AddHours(-2), Auto = evidence != null, Evidence = evidence, ManualOverride = evidence == null,
        };
        void Sighted(HomeworkLedger l, string id, int? left = null)
        {
            (l.QuestSightings ??= new())[id] = now.AddMinutes(-20);
            l.Items[id] = new HomeworkItemState { RemainingCount = left };
        }
        void Tokens(HomeworkLedger l) => l.RaidTokensObservedUtc = now.AddMinutes(-10);
        void GuildTodo(HomeworkLedger l, int n) => l.Items[Guild] = new HomeworkItemState { Evidence = $"[주간 목표] 모험가 길드의 정기 의뢰 ({n})" };
        void Suggest(HomeworkLedger l, string id) => l.Items[id] = new HomeworkItemState { Suggestion = new HomeworkSuggestion("raidTokenIncreased", "원정의 증거: 에이렐 레이드", 3, 4) };
        HomeworkLedger Everything()
        {
            var l = NewLedger();
            Done(l, Day, "퀘스트가 사라짐"); Done(l, Guild, "정기 의뢰 (3)"); Done(l, Vanguard, "퀘스트가 사라짐");
            Done(l, Cavrak, "원정의 증거 +20"); Done(l, Airel, "하프 조각 +1"); Done(l, White, "거울 조각 +1");
            Done(l, Anchor, "마물 퇴치 증표 +230"); Done(l, Cave, "마물 퇴치 증표 +250"); Done(l, Water, "마물 퇴치 증표 +210");
            Done(l, "fieldboss_krama");
            return l;
        }

        foreach (var key in keys)
        {
            var l = NewLedger();
            switch (key)
            {
                case "바람_도적": l = Everything(); break;                              // 모두 완료 → 흐린 줄
                case "바람_궁수":
                    Done(l, Day, "퀘스트가 사라짐"); GuildTodo(l, 2); Sighted(l, Vanguard, 2); Tokens(l);
                    Done(l, Cavrak, "원정의 증거 +20"); Suggest(l, Airel); Done(l, Anchor, "마물 퇴치 증표 +230");
                    break;
                case "아이라_성직자":
                    Done(l, Day); Sighted(l, Vanguard, 3); Tokens(l); Done(l, "fieldboss_tormog");
                    break;
                case "바람_전사":
                    Sighted(l, Day); Sighted(l, Vanguard, 1); GuildTodo(l, 1); Tokens(l); Done(l, Cavrak, "원정의 증거 +18"); Done(l, Water);
                    break;
                case "아이라_격투가":                                                     // 접속 중(가짜 CLI): 허상의 정박지에 있어 진행 중
                    Sighted(l, Day); Sighted(l, Vanguard, 3); Tokens(l);
                    break;
                case "류트_음유시인":
                    l = Everything(); l.Items.Remove(Water); l.Items.Remove("fieldboss_krama"); Done(l, "fieldboss_peri");
                    break;
                case "류트_검사":
                    Sighted(l, Day); Tokens(l); Done(l, White, "거울 조각 +1");
                    break;
                case "에린_힐러": l = Everything(); break;
                case "에린_듀얼블레이드":
                    Done(l, Day, "퀘스트가 사라짐"); Done(l, Vanguard, "퀘스트가 사라짐"); GuildTodo(l, 2); Tokens(l); Done(l, "fieldboss_angrbahan");
                    break;
                case "에린_화염술사":
                    Sighted(l, Vanguard, 2); Tokens(l); Suggest(l, Cavrak);
                    break;
                default: continue;                                                      // 장부가 없는 캐릭터(아이라_마법사 등): 전부 미확인
            }
            file.Characters[key] = l;
        }
        File.WriteAllText(path, JsonSerializer.Serialize(file));
    }

    /// <summary>창을 크기마다 한 번씩 그려 <paramref name="outDir"/>에 <c>home-WxH.png</c>로 저장한다. 보통 크기에서 필드 보스 팝업·이름 바꾸기 화면도 한 장씩. 저장한 파일 경로를 돌려준다.</summary>
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
                var file = Capture(window, outDir, $"home-{w}x{h}.png");
                saved.Add(file);
                log?.Invoke($"{w}x{h} → 배치 단계 {window.ViewModel.Tier}, 저장: {file}");
            }

            // 보통 크기에서 추가 두 장: 필드 보스 팝업이 열린 상태, 카드 이름을 바꾸는 중
            window.Width = ExtrasSize.Width;
            window.Height = ExtrasSize.Height;
            await SettleAsync(window);
            var board = window.ViewModel.Board;
            var bossRow = board.Rows.FirstOrDefault(r => r.Cells.Any(c => c.IsBoss && c.State != CellState.Done)) ?? board.Rows.FirstOrDefault();
            if (bossRow != null)
            {
                board.OpenBossPicker(bossRow.Key);
                await SettleAsync(window);
                saved.Add(Capture(window, outDir, $"home-boss-popup-{ExtrasSize.Width}x{ExtrasSize.Height}.png"));
                board.CloseBossPicker();
            }
            board.BeginRename();
            board.Card.RenameText = "새 이름 입력";
            await SettleAsync(window);
            saved.Add(Capture(window, outDir, $"home-rename-{ExtrasSize.Width}x{ExtrasSize.Height}.png"));
            board.CancelRename();

            // 두 번째 계정(8명)을 눌러 본다: 6줄이 넘으면 줄 영역만 세로 스크롤 (좁게·보통)
            if (board.Accounts.Count > 1)
            {
                board.SelectedAccount = board.Accounts[1];
                foreach (var (w, h) in new[] { (768, 405), (960, 510) })
                {
                    window.Width = w;
                    window.Height = h;
                    await SettleAsync(window);
                    saved.Add(Capture(window, outDir, $"home-account2-{w}x{h}.png"));
                }
            }
        }
        finally { window.Close(); }
        return saved;
    }

    /// <summary>창 안쪽을 1 DIP = 1 픽셀 PNG로 저장한다.</summary>
    private static string Capture(MainWindow window, string outDir, string name)
    {
        var frame = (FrameworkElement)window.Content;
        var pxW = (int)Math.Round(frame.ActualWidth);
        var pxH = (int)Math.Round(frame.ActualHeight);
        var bmp = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(frame);
        var file = Path.Combine(outDir, name);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(file)) encoder.Save(fs);
        return file;
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
