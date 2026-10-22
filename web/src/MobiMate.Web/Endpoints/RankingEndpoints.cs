using System.Text.Json;
using MobiMate.Web.Hosting;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Lan;
using MobiMate.Web.Services;

namespace MobiMate.Web.Endpoints;

public sealed record RankTokenRequest(string? Token);

/// <summary>서버 랭킹 API (v0.3). 앱은 넥슨 랭킹 페이지를 스스로 부르지 않는다: 값은 북마크릿(사용자가 자기 로그인 브라우저에서 직접 누름)으로만 들어온다.</summary>
public static class RankingEndpoints
{
    /// <summary>북마크릿이 돌아가는 페이지. 이 주소에서 온 요청만 CORS로 받는다.</summary>
    public const string NexonOrigin = "https://mabinogimobile.nexon.com";

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/rankings");

        // 화면용: 모든 캐릭터의 랭킹 보기와 북마크릿 안내
        api.MapGet("", (RankingService r, SnapshotManager snapshots) =>
        {
            var targets = r.Targets();
            var keys = snapshots.GetAllProfiles().Select(p => p.CharacterKey).ToList();
            return ApiResults.Ok(new
            {
                rankingUrl = RankingService.RankingUrl,
                freshHours = RankingService.FreshFor.TotalHours,
                targets = targets.Select(t => t.Key),
                characters = keys.ToDictionary(k => k, k => r.ViewOf(k), StringComparer.OrdinalIgnoreCase),
            });
        });

        // 북마크릿 코드 (사용자가 즐겨찾기 막대에 끌어 놓거나 복사한다)
        // 토큰이 들어 있어 이 PC의 브라우저에서만 준다 (LAN의 폰·태블릿에는 주지 않는다. 그쪽은 수동 입력)
        api.MapGet("/setup", (HttpContext ctx, RankingService r, ServerIdentity id) =>
            ClientId.IsLocal(ctx)
                ? ApiResults.Ok(new { rankingUrl = RankingService.RankingUrl, bookmarklet = RankingBookmarklet.Build(id.Port, r.Token) })
                : ApiResults.Error(StatusCodes.Status403Forbidden, "LOCAL_ONLY", "북마크릿은 게임을 켠 PC의 브라우저에서만 만들 수 있습니다."));

        // 북마크릿이 부르는 두 곳: 넥슨 랭킹 페이지에서 오므로 CORS를 열고, 토큰으로 막는다. 토큰은 본문에 담아 사전 요청 없는 단순 요청으로 받는다.
        api.MapMethods("/targets", new[] { "OPTIONS" }, (HttpContext ctx) => Preflight(ctx));
        api.MapPost("/targets", async (HttpContext ctx, RankingService r) =>
        {
            Cors(ctx);
            var req = await ReadBody<RankTokenRequest>(ctx);
            if (req == null || !r.TokenOk(req.Token)) return ApiResults.Error(StatusCodes.Status403Forbidden, "BAD_TOKEN", "북마크릿 토큰이 맞지 않습니다. 앱에서 북마크릿을 다시 만들어 주세요.");
            // 최신인 종류는 빼고 줄 일이 있는 것만 보낸다(넥슨에 한꺼번에 너무 많이 부르지 않게). all = 이름이 있는 전체 캐릭터 수
            var todo = r.TargetsByNeed().Select(t => new { t.Key, t.Name, t.ServerId, t.ServerName, t.Job, t.Combat, kinds = r.KindsNeeded(t) }).Where(t => t.kinds.Count > 0).ToList();
            return ApiResults.Ok(new { targets = todo, all = r.Targets().Count });
        });

        api.MapMethods("/import", new[] { "OPTIONS" }, (HttpContext ctx) => Preflight(ctx));
        api.MapPost("/import", async (HttpContext ctx, RankingService r, SseHub hub, ILogger<RankingService> log) =>
        {
            Cors(ctx);
            var req = await ReadBody<RankImportRequest>(ctx);
            if (req == null || !r.TokenOk(req.Token)) return ApiResults.Error(StatusCodes.Status403Forbidden, "BAD_TOKEN", "북마크릿 토큰이 맞지 않습니다. 앱에서 북마크릿을 다시 만들어 주세요.");
            if (req.Items is not { Count: > 0 and <= 200 }) return ApiResults.Error(StatusCodes.Status400BadRequest, "VALIDATION", "저장할 순위가 없거나 너무 많습니다.");
            var result = r.Import(req.Items);
            log.LogInformation("랭킹 가져오기: {Accepted}건 저장, {Rejected}건 제외{Why}", result.Accepted, result.Rejected, result.Problems.Count == 0 ? "" : " — " + string.Join(" / ", result.Problems));
            if (result.Accepted > 0) hub.Broadcast("state.changed", new { keys = new[] { "rankings", "characters", "header" } });
            return ApiResults.Ok(result);
        });
    }

    private static void Cors(HttpContext ctx)
    {
        if (ctx.Request.Headers.Origin == NexonOrigin)
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = NexonOrigin;
            ctx.Response.Headers["Vary"] = "Origin";
        }
    }

    private static IResult Preflight(HttpContext ctx)
    {
        Cors(ctx);
        var h = ctx.Response.Headers;
        h["Access-Control-Allow-Methods"] = "POST, OPTIONS";
        h["Access-Control-Allow-Headers"] = "Content-Type";
        h["Access-Control-Allow-Private-Network"] = "true";   // 공개 페이지에서 로컬 주소로 가는 요청 (Chrome 사설망 접근)
        h["Access-Control-Max-Age"] = "600";
        return Results.StatusCode(StatusCodes.Status204NoContent);
    }

    private static async Task<T?> ReadBody<T>(HttpContext ctx) where T : class
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var text = await reader.ReadToEndAsync();
            if (text.Length > 2_000_000) return null;
            return JsonSerializer.Deserialize<T>(text, ApiResults.Json);
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// 넥슨 랭킹 페이지에서 사용자가 직접 누르는 북마크릿. 앱에서 대상(별칭·서버)을 받아 와, 사용자 브라우저가 그 페이지의 검색을
/// 캐릭터당 4건(종합·전투력·생활력·매력), 1.2초 간격으로 부르고, 찾은 항목만 로컬 앱으로 돌려준다. 앱이 직접 부르는 것이 아니라서
/// 보안 검사를 우회하지 않는다. 사용자가 누를 때만 실행된다.
/// </summary>
public static class RankingBookmarklet
{
    private const string Template = """
(async()=>{const A='http://127.0.0.1:__PORT__',T='__TOKEN__';
if(location.hostname!=='mabinogimobile.nexon.com'){alert('넥슨 마비노기 모바일 랭킹 페이지(mabinogimobile.nexon.com/Ranking)에서 눌러 주세요.');return}
const msg=m=>{let e=document.getElementById('mm-rank-msg');if(!e){e=document.createElement('div');e.id='mm-rank-msg';e.style.cssText='position:fixed;z-index:99999;top:12px;right:12px;background:#222;color:#fff;padding:10px 14px;border-radius:8px;font:14px sans-serif;max-width:320px';document.body.appendChild(e)}e.textContent=m};
try{
const r=await fetch(A+'/api/rankings/targets',{method:'POST',headers:{'Content-Type':'text/plain'},body:JSON.stringify({token:T})});
const j=await r.json();if(!r.ok)throw new Error(j.error&&j.error.message||'앱에 연결하지 못했습니다');
const ts=j.data.targets;if(!ts.length){msg(j.data.all?'모든 캐릭터의 순위가 최신이에요(6시간 이내). 가져올 것이 없습니다.':'순위를 가져올 캐릭터가 없습니다. 앱에서 캐릭터 이름(별칭)을 먼저 입력하세요.');return}
const nap=ms=>new Promise(s=>setTimeout(s,ms));
const get=async(t,k)=>{const f=new FormData();f.append('t',k);f.append('pageno',1);f.append('s',t.serverId);f.append('c','0');f.append('search',t.name);
const x=await fetch('/Ranking/List/rankdata',{method:'POST',body:f,headers:{'X-Requested-With':'XMLHttpRequest'}});
const h=await x.text();if(h.indexOf('결과가 없습니다')>-1)return '결과가 없습니다';
const li=new DOMParser().parseFromString(h,'text/html').querySelector('li.item.on');return li?li.outerHTML:null};
let n=0,acc=0,rej=0,blind=0,done=0;const total=ts.reduce((a,t)=>a+t.kinds.length,0);const probs=[];
for(const t of ts){const items=[];for(const k of t.kinds){
msg('순위 가져오는 중 '+(++n)+'/'+total+' · '+t.name);
let html=await get(t,k);
if(html===null){msg('잠깐 쉬었다가 다시 시도해요 · '+t.name);await nap(6000);html=await get(t,k)}
items.push({key:t.key,name:t.name,kind:k,html:html});
await nap(2000)}
const o=await fetch(A+'/api/rankings/import',{method:'POST',headers:{'Content-Type':'text/plain'},body:JSON.stringify({token:T,items:items})});
const oj=await o.json();if(!o.ok)throw new Error(oj.error&&oj.error.message||'저장하지 못했습니다');
acc+=oj.data.accepted;rej+=oj.data.rejected;(oj.data.problems||[]).forEach(p=>{if(probs.length<5)probs.push(p)});done++;
blind=items.every(i=>i.html===null)?blind+1:0;
if(blind>=2){msg('넥슨 보안 검사 등으로 읽지 못해 멈췄어요. '+done+'명까지 저장했어요. 잠시 뒤 다시 눌러 주세요.');return}}
msg('완료: '+acc+'건 저장'+(rej?(', '+rej+'건 제외 — '+probs.join(' / ')):''))
}catch(e){msg('실패: '+e.message)}})()
""";

    public static string Build(int port, string token)
    {
        var oneLine = string.Join("", Template.Split('\n').Select(l => l.Trim()));
        return "javascript:" + oneLine.Replace("__PORT__", port.ToString()).Replace("__TOKEN__", token);
    }
}
