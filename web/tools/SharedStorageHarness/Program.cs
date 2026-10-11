using MobiMate;
using MobiMate.Web.Hosting;

// 사용법: SharedStorageHarness <저장 폴더> <역할 문자> <반복 횟수>
// 반복마다 세 가지 쓰기를 번갈아 한다. 키는 모두 역할 문자와 번호로 유일해서, 끝난 뒤 파일에 전부 남아 있어야 한다.
//   i % 3 == 0: 숙제 수동 체크 (새 캐릭터 장부 hw_{역할}_{i})
//   i % 3 == 1: 캐릭터 기록 (서버_직업 = 류트_{역할}{i})
//   i % 3 == 2: 계정 지정 (acct_{역할}_{i} → 계정 {역할})
// 종료 코드: 0 정상, 1 쓰기 실패(예외·거부)
if (args.Length < 3) return 2;
var dir = args[0];
var role = args[1];
var count = int.Parse(args[2]);

var catalog = HomeworkCatalog.LoadEmbedded();
var def = catalog.Items.First(d => d.Active && d.Share == HomeworkShare.Character);
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
                homework.Set($"hw_{role}_{i}", def.Id, completed: true);
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
