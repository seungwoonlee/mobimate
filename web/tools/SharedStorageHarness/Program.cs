using MobiMate;
using MobiMate.Web.Hosting;

// 사용법: SharedStorageHarness <저장 폴더> <역할 문자> <반복 횟수>
// 반복마다 세 가지 쓰기를 번갈아 한다. 키는 모두 역할 문자와 번호로 유일해서, 끝난 뒤 파일에 전부 남아 있어야 한다.
//   i % 3 == 0: 숙제 수동 체크. 앞쪽 몇 번은 두 프로세스가 같은 캐릭터 shared_char의 서로 다른 숙제 id를 한 번씩 체크한다(같은 키 충돌).
//               역할 A는 짝수 번째, B는 홀수 번째 id를 쓴다. 그 뒤로는 새 캐릭터 장부 hw_{역할}_{i}에 체크한다.
//   i % 3 == 1: 캐릭터 기록 (서버_직업 = 류트_{역할}{i})
//   i % 3 == 2: 계정 지정 (acct_{역할}_{i} → 계정 {역할})
// 종료 코드: 0 정상, 1 쓰기 실패(예외·거부)
if (args.Length < 3) return 2;
if (args[1] == "verify-reset")
{
    var checkTime = DateTimeOffset.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
    var checkStore = new HomeworkStore(args[0]);
    var checkService = new HomeworkService(HomeworkCatalog.LoadEmbedded(), checkStore, () => checkTime);
    if (checkService.GetBoard("c").Cards.Single(x => x.Id == "raid_scars").IsDone) return 3;
    checkService.Set("c", "weekly_guild_regular", completed: true);
    var items = checkStore.Load().Characters["c"].Items;
    return !items.ContainsKey("raid_scars") && items["weekly_nightmare_drill"].Count == 22
        && items["weekly_nightmare_rank"].Count == 119 && items["weekly_memorial_saga"].Count == 8 ? 0 : 4;
}
var dir = args[0];
var role = args[1];
var count = int.Parse(args[2]);

var catalog = HomeworkCatalog.LoadEmbedded();
var defs = catalog.Items.Where(d => d.Active && d.Share == HomeworkShare.Character).ToList();
var def = defs[0];
var mine = defs.Where((_, idx) => idx % 2 == (role == "A" ? 0 : 1)).ToList();
var homework = new HomeworkService(catalog, new HomeworkStore(dir));
var snapshots = new SnapshotManager(dir);
var accounts = new AccountStore(dir);

var failures = 0;
for (var i = 0; i < count; i++)
{
    try
    {
        switch (i % 3)
        {
            case 0:
                var j = i / 3;
                if (j < mine.Count) homework.Set("shared_char", mine[j].Id, completed: true);
                else homework.Set($"hw_{role}_{i}", def.Id, completed: true);
                break;
            case 1:
                var job = $"{role}{i}";
                var info = new CharacterInfo("초보자", "류트", 50 + i % 7, job, new ScoreVal("전투력", 30000 + i), null, null, null, null, null, null, null,
                    null, null, null, null, null, null, new VitalsInfo(1000, 1000, 500.0, 1000.0, 100, 100, 0));
                snapshots.UpdateSnapshot(info, new List<CurrencyItem> { new("골드", 1_000_000 + i) }, null, $"류트_{job}");
                break;
            default:
                var key = $"acct_{role}_{i}";
                if (!accounts.Update(d => { d.Assign[key] = new AccountAssignment { Account = role, Manual = true }; return true; })) failures++;
                break;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{role}#{i}] {ex.GetType().Name}: {ex.Message}");
        failures++;
    }
}
Console.WriteLine($"done role={role} count={count} failures={failures}");
return failures == 0 ? 0 : 1;
