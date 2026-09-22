using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MobiMate;

/// <summary>
/// 인게임 "아무말 대잔치"에서 지원하는 5대 개성 페르소나
/// </summary>
public enum ChatterPersona
{
    Villainess,         // 🌹 악덕영애 스타일 (도도하고 콧대 높은 츤데레 귀족 영애)
    Scrooge,            // 💰 구두쇠 영감 스타일 (1골드도 아까워하는 잔소리 영감)
    MorningSpirit,      // ☀️ 안녕하닝 모닝이야 스타일 (마비노기 모바일 전문 유튜버 모닝이)
    GyeongsangAhjussi,  // 🌊 갱상도 아재 스타일 (투박하지만 정감 넘치는 사투리 아재)
    IdolDancer          // ✨ 아이돌 댄서 스타일 (칼군무와 킬링 파트를 꿈꾸는 K-POP 댄서)
}

/// <summary>
/// 현재 캐릭터 및 게임 환경 스냅샷
/// </summary>
public record ChatterContext(
    string Realm,
    string Job,
    int Level,
    long CombatScore,
    string Activity,
    string Location,
    string WeightSummary,
    string GoldSummary
);

/// <summary>
/// AI 툴 미설치(0원 무설치) 환경 및 오프라인/타임아웃 시 100% 무중단 동작을 보장하는 내장 페르소나 템플릿 풀
/// </summary>
public static class PersonaTemplates
{
    private static readonly Random _rnd = new();

    // [악덕영애 스타일] 대사 풀
    private static readonly Dictionary<string, string[]> VillainessLines = new()
    {
        ["채집"] =
        [
            "고작 이런 풀때기나 흙덩이를 줍는데 날 부르다니, 오호호!",
            "이런 흙먼지 날리는 곳은 내 우아함과 어울리지 않사와요!",
            "흥, 이 정도 채집물로 날 만족시키려 들다니 어림없사와요!",
            "손끝에 흙이 묻었잖아요! 하녀를 시키지 않고 왜 내가...",
            "이 풀뿌리가 그렇게 귀한 건가요? 뭐, 나쁘진 않네요!"
        ],
        ["가방무거움"] =
        [
            "가방이 왜 이리 묵직한 거죠? 당장 짐꾼을 대령하세요!",
            "우아한 귀족 영애에게 이런 짐더미를 들게 하다니 무례하군요!",
            "짐이 너무 무거워서 걸을 수가 없사와요! 창고에 맡기세요!",
            "어머, 가방이 터질 지경이에요! 품위를 지킬 수가 없네요!"
        ],
        ["골드부족"] =
        [
            "내 지갑이 이토록 가볍다니... 이게 가문의 수치가 아니면 뭐죠?!",
            "골드가 부족하다니요?! 당장 영지를 팔아서라도 채워넣으세요!",
            "흥, 돈 따위 얼마든지 벌 수 있사와요. 잠깐 비운 것뿐이에요!"
        ],
        ["전투"] =
        [
            "감히 내 앞을 가로막다니, 주제를 알게 해 주겠사와요!",
            "오호호! 내 발밑에서 엎드려 자비를 구해보시지요!",
            "이 천박한 몬스터들이 감히 내 드레스에 손을 대려 하다니!"
        ],
        ["일반"] =
        [
            "흥! 날 만족시킬 만한 모험은 에린 어디에 있는 건가요?",
            "어머, 나를 쳐다보는 저 무례한 시선들은 다 뭐죠?",
            "오늘도 내 아름다움 때문에 티르코네일이 눈부시겠군요, 오호호!",
            "지루하네요. 어디 신선하고 짜릿한 사건이라도 안 터지나요?",
            "차 한 잔의 여유도 없이 돌아다니다니, 정말 야만스러워요!"
        ]
    };

    // [구두쇠 영감 스타일] 대사 풀
    private static readonly Dictionary<string, string[]> ScroogeLines = new()
    {
        ["채집"] =
        [
            "땅에 떨어진 건 돌멩이 하나도 다 돈이다, 주워라 주워!",
            "공짜로 채집할 수 있을 때 바짝 챙겨둬야 남는 장사여!",
            "어이쿠 이 귀한 걸 남들이 채가기 전에 싹 쓸어가야제!",
            "낫질 한번 곡괭이질 한번에 다 내 노후 자금이다, 에헴!",
            "채집 도구 내구도 닳는다! 살살 쳐라, 살살!"
        ],
        ["가방무거움"] =
        [
            "가방이 무거워도 버릴 건 하나도 없다! 다 팔면 골드여!",
            "무겁다고 길바닥에 버리기만 해봐라, 내 눈에 흙이 들어가도 안 된다!",
            "수수료 아까우니 창고에 차곡차곡 쟁여둬야제, 암!",
            "가방 무게 80%? 99%까지 꽉꽉 눌러 담아야 제맛이제!"
        ],
        ["골드부족"] =
        [
            "아이고 내 골드! 수리비로 100골드나 뜯어가다니 도둑놈들이여!",
            "지갑에 먼지만 날리는구먼... 오늘 저녁은 굶어야 쓰겄다!",
            "골드가 바닥났어! 물약도 아껴먹고 장작도 주워 써라!"
        ],
        ["전투"] =
        [
            "장비 내구도 닳는다! 한 방에 급소만 팍팍 찔러 끝내!",
            "몬스터 녀석들, 주머니에 골드 두둑하게 챙겨왔겠지?!",
            "물약 비싸다! 맞지 말고 굴러라, 굴러!"
        ],
        ["일반"] =
        [
            "요즘 물가가 왜 이리 비싸? 1골드도 허투루 쓰면 안 돼!",
            "아이고 허리야... 에린 바닥엔 왜 이리 돈 쓸 일만 많누!",
            "포션 값 아까워서 피 채우러 모닥불 피워놓고 쉰다, 에헴.",
            "젊은 놈들이 절약을 몰라요, 절약을! 쯧쯧쯧.",
            "상점 엔피시들 폭리가 아주 그냥 도둑놈 심보여, 도둑놈!"
        ]
    };

    // [안녕하닝 모닝이야 스타일] 대사 풀 (유튜버 모닝이 @morning2studio 고증)
    private static readonly Dictionary<string, string[]> MorningLines = new()
    {
        ["채집"] =
        [
            "안녕하닝 모닝이야! 채집 재료 싹 쓸어담아서 쌀먹 가자닝~",
            "형들! 이 채집물 경매장 시세 잘 보고 팔아야 하닝!",
            "채집할 때도 도구 내구도 아끼는 꿀팁 잊지 말라닝!",
            "오늘 채집 대박 나서 대성공 뜨길 모닝이가 응원하닝!"
        ],
        ["가방무거움"] =
        [
            "가방 무게 100% 넘었닝! 형들, 마을 창고로 런해야 하닝!",
            "이러다 캐릭터 기어다니닝! 잡템 빨리 상점에 던지자닝!",
            "가방 다이어트 시급하닝! 무게 페널티 받으면 답 없닝!"
        ],
        ["골드부족"] =
        [
            "지갑에 골드가 바닥났닝? 무소과금 모닝이 꿀팁 영상 보라닝!",
            "수리비 때문에 눈물 나닝... 오늘도 일퀘 뛰어서 골드 벌자닝!"
        ],
        ["전투"] =
        [
            "보스 브레이크 타이밍에 극딜 넣어야 하닝! 지금이닝!",
            "장판 피하고 뒤잡기 필수닝! 형들 컨트롤 보여주라닝!",
            "룬 세팅 제대로 하면 전투력 떡상하닝! 얍얍 물리치자닝!"
        ],
        ["일반"] =
        [
            "안녕하닝 모닝이야! 오늘도 즐거운 마비노기 모바일 되라닝~",
            "밀레시안 형들! 오늘 일일 미션이랑 숙제 다 끝냈닝?",
            "모닝이 채널 구독과 좋아요는 큰 힘이 된다닝! 파이팅이닝!",
            "에린 날씨 완전 화창하닝! 오늘도 득템 가득하길 바라닝!",
            "무소과금도 꾸준히 하면 랭커 될 수 있닝! 포기하지 말라닝!"
        ]
    };

    // [갱상도 아재 스타일] 대사 풀
    private static readonly Dictionary<string, string[]> GyeongsangLines = new()
    {
        ["채집"] =
        [
            "마! 단디 캐라, 땅 파믄 돈이 나오나 채집물이 나오제!",
            "어이쿠야, 곡괭이질 팍팍 해라! 오늘 안에 다 캐겠나!",
            "나무 벨 때는 허리 힘으로 빡! 알긋나, 행님아!",
            "마! 낚싯대 단디 쥐고 있어라, 대물이 물었삣다!"
        ],
        ["가방무거움"] =
        [
            "아이구야 억수로 무겁네! 가방 터지겠다, 마!",
            "마! 짐이 와 이리 많노? 마을 창고에 좀 쳐넣고 온나!",
            "다리 몽디 부러지겠다! 잡템은 상점에 고마 확 팔아삐라!"
        ],
        ["골드부족"] =
        [
            "마! 내 지갑에 땡전 한 푼 없다 아이가! 우짜노!",
            "수리비가 와 이리 비싸노?! 내 눈탱이 밤탱이 맞았네!"
        ],
        ["전투"] =
        [
            "마! 쫄지 마라! 몬스터 대가리 고마 콱 때리뿌라!",
            "장판 온다! 옆으로 싹 피해야제, 뭐하노 임마!",
            "오늘 저녁은 보스 잡고 회 한 사라 무러 가자!"
        ],
        ["일반"] =
        [
            "마! 밥은 든든하게 묵고 댕기나? 건강이 최고다!",
            "에린 날씨 쥑이네! 바람도 솔솔 부는 기 딱 좋다!",
            "행님아! 오늘도 억수로 수고가 많다, 힘내자!"
        ]
    };

    // [아이돌 댄서 스타일] 대사 풀
    private static readonly Dictionary<string, string[]> IdolDancerLines = new()
    {
        ["채집"] =
        [
            "원 투 쓰리 포! 채집도 리듬 타면서 그루브하게~ ✨",
            "땀 흘리는 내 모습, 카메라 원샷 잡히는 중인가요?!",
            "자연 속에서도 멈추지 않는 댄스 본능! 얍얍~"
        ],
        ["가방무거움"] =
        [
            "가방이 무거워서 스텝이 꼬이잖아요! 무게 다이어트 필수!",
            "이렇게 무거운 짐을 메고 춤출 순 없어요! 창고로 무브!",
            "칼군무를 방해하는 가방 무게! 당장 비워줄게요~"
        ],
        ["골드부족"] =
        [
            "내 통장 잔고 왜 이래요? 음원 정산 언제 들어오나요?!",
            "골드가 부족해도 무대 위 열정은 백만 볼트라구요!"
        ],
        ["전투"] =
        [
            "오늘 레이드 무대의 킬링 파트는 바로 나! 센터 고정!",
            "보스 장판 회피도 턴 앤 턴~ 완벽한 댄스 브레이크!",
            "엔딩 요정 포즈는 몬스터 쓰러질 때 찰칵! ✨"
        ],
        ["일반"] =
        [
            "에린 팬 여러분 안녕하세요! 오늘도 비트 타볼까요?",
            "언제 어디서나 자세는 곧게! 무대 매너 장착 완료~",
            "오늘도 반짝반짝 빛나는 하루 만들어봐요, 레츠 고!"
        ]
    };

    public static string GetRandomTemplate(ChatterPersona persona, ChatterContext ctx)
    {
        var category = DetermineCategory(ctx);
        var pool = persona switch
        {
            ChatterPersona.Villainess => VillainessLines,
            ChatterPersona.Scrooge => ScroogeLines,
            ChatterPersona.MorningSpirit => MorningLines,
            ChatterPersona.GyeongsangAhjussi => GyeongsangLines,
            ChatterPersona.IdolDancer => IdolDancerLines,
            _ => VillainessLines
        };

        if (!pool.TryGetValue(category, out var lines) || lines.Length == 0)
        {
            lines = pool["일반"];
        }

        return lines[_rnd.Next(lines.Length)];
    }

    private static string DetermineCategory(ChatterContext ctx)
    {
        var act = (ctx.Activity ?? "").ToLowerInvariant();
        if (act.Contains("채집") || act.Contains("벌목") || act.Contains("채광") || act.Contains("수확") || act.Contains("낚시"))
        {
            return "채집";
        }

        // 가방 무게 타령은 100% 이상/초과일 때만 제한적으로 발동
        var weight = ctx.WeightSummary ?? "";
        var match = Regex.Match(weight, @"(\d+)%");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
        {
            if (pct >= 100)
            {
                return "가방무거움";
            }
        }
        else if (weight.Contains("100% 초과") || weight.Contains("무게 초과") || weight.Contains("한도 초과"))
        {
            return "가방무거움";
        }

        if (act.Contains("전투") || act.Contains("던전") || act.Contains("사냥") || act.Contains("레이드") || act.Contains("보스"))
        {
            return "전투";
        }

        var gold = ctx.GoldSummary ?? "";
        if (gold.Contains("부족") || gold == "0" || gold.StartsWith("0"))
        {
            return "골드부족";
        }

        return "일반";
    }
}
