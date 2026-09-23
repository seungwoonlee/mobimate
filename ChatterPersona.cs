using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MobiMate;

/// <summary>
/// 인게임 "아무말 대잔치"에서 지원하는 페르소나 (기본 5종 + 사용자 정의 커스텀)
/// </summary>
public enum ChatterPersona
{
    Villainess,         // 🌹 악덕영애 스타일 (도도하고 콧대 높은 츤데레 귀족 영애)
    Scrooge,            // 💰 구두쇠 영감 스타일 (1골드도 아까워하는 잔소리 영감)
    MorningSpirit,      // ☀️ 안녕하닝 모닝이야 스타일 (마비노기 모바일 전문 유튜버 모닝이)
    GyeongsangAhjussi,  // 🌊 갱상도 아재 스타일 (투박하지만 정감 넘치는 사투리 아재)
    IdolDancer,         // ✨ 아이돌 댄서 스타일 (칼군무와 킬링 파트를 꿈꾸는 K-POP 댄서)
    Custom              // 🎭 사용자 생성 커스텀 페르소나
}

/// <summary>
/// 현재 캐릭터 및 게임 환경 스냅샷 (기존 호출부 호환을 위한 ErinnTime 디폴트 파라미터 포함)
/// </summary>
public record ChatterContext(
    string Realm,
    string Job,
    int Level,
    long CombatScore,
    string Activity,
    string Location,
    string WeightSummary,
    string GoldSummary,
    string ErinnTime = ""
);

/// <summary>
/// 12대 세분화 카테고리 기반 페르소나 템플릿 선택기
/// </summary>
public static class PersonaTemplates
{
    private static readonly Random _rnd = new();

    public static string GetRandomTemplate(ChatterPersona persona, ChatterContext ctx)
    {
        var category = DetermineCategory(ctx);
        var pool = persona switch
        {
            ChatterPersona.Villainess => PersonaTemplatesData.VillainessLines,
            ChatterPersona.Scrooge => PersonaTemplatesData.ScroogeLines,
            ChatterPersona.MorningSpirit => PersonaTemplatesData.MorningLines,
            ChatterPersona.GyeongsangAhjussi => PersonaTemplatesData.GyeongsangLines,
            ChatterPersona.IdolDancer => PersonaTemplatesData.IdolDancerLines,
            ChatterPersona.Custom => PersonaTemplatesData.VillainessLines, // 커스텀 페르소나의 기본 템플릿 폴백
            _ => PersonaTemplatesData.VillainessLines
        };

        if (!pool.TryGetValue(category, out var lines) || lines.Length == 0)
        {
            lines = pool["일상_숙제"];
        }

        return lines[_rnd.Next(lines.Length)];
    }

    /// <summary>
    /// 상황 인식 기반 12대 카테고리 우선순위 라우팅
    /// 우선순위: 긴급(가방과적 100%+) -> 직접행동(보스/전투/낚시/가공/채집) -> 재화(부족/부자) -> 마을휴식 -> 일상/시간
    /// </summary>
    public static string DetermineCategory(ChatterContext ctx)
    {
        // 1. 긴급 상태: 가방 무게 100% 초과 판정
        var weight = ctx.WeightSummary ?? "";
        var match = Regex.Match(weight, @"(\d+(?:\.\d+)?)\s*%");
        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
        {
            if (pct >= 100.0) return "가방_과적";
        }
        else if (weight.Contains("100% 초과") || weight.Contains("무게 초과") || weight.Contains("한도 초과"))
        {
            return "가방_과적";
        }

        var act = (ctx.Activity ?? "").ToLowerInvariant();
        var loc = (ctx.Location ?? "").ToLowerInvariant();

        // 2. 직접 행동 우선순위
        // 2-1. 보스 / 레이드 / 어비스 / 던전
        if (act.Contains("보스") || act.Contains("레이드") || act.Contains("어비스") ||
            loc.Contains("보스") || loc.Contains("레이드") || loc.Contains("어비스") ||
            loc.Contains("글라스기브넨") || loc.Contains("던전") || act.Contains("던전"))
        {
            return "전투_보스레이드";
        }

        // 2-2. 일반 전투 / 필드 사냥
        if (act.Contains("전투") || act.Contains("사냥") || act.Contains("몬스터"))
        {
            return "전투_일반";
        }

        // 2-3. 낚시
        if (act.Contains("낚시") || loc.Contains("낚시터") || loc.Contains("호수") || loc.Contains("해안"))
        {
            return "낚시";
        }

        // 2-4. 생산 / 가공 / 작업대
        if (act.Contains("가공") || act.Contains("작업대") || act.Contains("제작") ||
            act.Contains("요리") || act.Contains("방직") || act.Contains("제련"))
        {
            return "생산_가공";
        }

        // 2-5. 자연 채집 / 벌목 / 채광 / 약초
        if (act.Contains("채집") || act.Contains("벌목") || act.Contains("채광") ||
            act.Contains("약초") || act.Contains("수확") || act.Contains("사과"))
        {
            return "채집_자연";
        }

        // 3. 재화 상태 (골드 부족 또는 골드 부자)
        var gold = ctx.GoldSummary ?? "";
        if (gold.Contains("부족") || gold == "0" || gold.StartsWith("0 ") || gold.StartsWith("0골드"))
        {
            return "골드_부족";
        }

        var matchGold = Regex.Match(gold.Replace(",", ""), @"(\d+)");
        if (matchGold.Success && long.TryParse(matchGold.Groups[1].Value, out var goldVal))
        {
            if (goldVal >= 5_000_000)
            {
                return "골드_부자";
            }
        }

        // 4. 마을 휴식 / 광장
        if (act.Contains("휴식") || act.Contains("마을") || loc.Contains("티르코네일") ||
            loc.Contains("던바튼") || loc.Contains("이멘마하") || loc.Contains("반호르") ||
            loc.Contains("광장") || loc.Contains("모닥불"))
        {
            return "마을_휴식";
        }

        // 5. 시간 및 일상 숙제 분기 (에린 시간 낮/밤 vs 일상 숙제를 확률적 4:6으로 안배)
        var erinn = ctx.ErinnTime ?? "";
        var timeMatch = Regex.Match(erinn, @"(\d{1,2}):(\d{2})");
        var isDay = true;
        if (timeMatch.Success && int.TryParse(timeMatch.Groups[1].Value, out var hour))
        {
            isDay = hour >= 6 && hour < 18;
        }
        else if (erinn.Contains("밤") || erinn.Contains("🌙"))
        {
            isDay = false;
        }

        // 40% 확률로 시간대 감성 대사, 60% 확률로 일상/숙제 대사
        if (_rnd.Next(10) < 4)
        {
            return isDay ? "에린_낮" : "에린_밤";
        }

        return "일상_숙제";
    }
}
