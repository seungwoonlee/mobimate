using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MobiMate
{
    public class HomeworkRepository
    {
        private readonly string _storageDir;
        private readonly string _saveFilePath;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public List<HomeworkDefinition> MasterList { get; } = new();

        public HomeworkRepository(string? customStorageDir = null)
        {
            if (string.IsNullOrWhiteSpace(customStorageDir))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _storageDir = Path.Combine(appData, "MobiMate");
            }
            else
            {
                _storageDir = customStorageDir;
            }

            Directory.CreateDirectory(_storageDir);
            _saveFilePath = Path.Combine(_storageDir, "homework_records.json");

            InitializeMasterList();
        }

        private void InitializeMasterList()
        {
            // 1. 일일 숙제 (Daily)
            MasterList.Add(new HomeworkDefinition
            {
                Id = "daily_connect",
                Category = HomeworkCategory.Daily,
                Period = HomeworkPeriod.Daily,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.DirectMission,
                Title = "에린 접속",
                Subtitle = "매일 에린 접속하기",
                Icon = "🌟",
                MatchKeywords = new() { "에린에 돌아왔습니다", "접속하기", "접속" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "daily_dungeon_3",
                Category = HomeworkCategory.Daily,
                Period = HomeworkPeriod.Daily,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.DirectMission,
                Title = "던전 3회 토벌",
                Subtitle = "일반/심층 던전 3회 클리어",
                Icon = "⚔️",
                GoalCount = 3,
                MatchKeywords = new() { "오늘도 던전 한 바퀴", "던전 3회 토벌", "던전" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "daily_gather_3",
                Category = HomeworkCategory.Daily,
                Period = HomeworkPeriod.Daily,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.DirectMission,
                Title = "재료 3회 채집",
                Subtitle = "생활 재료 아이템 3회 채집",
                Icon = "🍎",
                GoalCount = 3,
                MatchKeywords = new() { "자급자족의 삶", "재료 아이템 3회 채집", "채집" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "daily_black_hole",
                Category = HomeworkCategory.Daily,
                Period = HomeworkPeriod.Daily,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.DirectMission,
                Title = "검은 구멍 (일일)",
                Subtitle = "일일 검은 구멍 차원 정화",
                Icon = "🕳️",
                MatchKeywords = new() { "검은 구멍", "차원 정화" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "daily_barrier",
                Category = HomeworkCategory.Daily,
                Period = HomeworkPeriod.Daily,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.DirectMission,
                Title = "소환의 결계 (일일)",
                Subtitle = "일일 소환의 결계 방어",
                Icon = "🔮",
                MatchKeywords = new() { "소환의 결계", "결계" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "daily_tower",
                Category = HomeworkCategory.Daily,
                Period = HomeworkPeriod.Daily,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.DirectMission,
                Title = "망령의 탑",
                Subtitle = "망령의 탑 일일 도전",
                Icon = "🗼",
                MatchKeywords = new() { "망령의 탑", "망령" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "daily_altering",
                Category = HomeworkCategory.Daily,
                Period = HomeworkPeriod.Daily,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.AlteringFacility,
                Title = "가공시설 수거",
                Subtitle = "대기열 완료된 연금/가공물 수거",
                Icon = "⚗️",
                MatchKeywords = new() { "가공", "작업대", "연금" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "daily_cash_free_fashion",
                Category = HomeworkCategory.Shop,
                Period = HomeworkPeriod.Daily,
                ShareType = HomeworkShareType.Account,
                AutoMode = AutoDetectMode.ManualOnly,
                Title = "일일 무료 패션 소환",
                Subtitle = "캐시샵 매일 1회 무료 패션 소환",
                Icon = "👗",
                MatchKeywords = new() { "무료 패션", "패션 소환" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "daily_cash_gem_box",
                Category = HomeworkCategory.Shop,
                Period = HomeworkPeriod.Daily,
                ShareType = HomeworkShareType.Account,
                AutoMode = AutoDetectMode.ManualOnly,
                Title = "조각난 보석 보물상자",
                Subtitle = "캐시샵 일일 무료 보석 상자 수령",
                Icon = "💎",
                MatchKeywords = new() { "보물상자", "보석 상자" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "daily_cash_silver_coin",
                Category = HomeworkCategory.Shop,
                Period = HomeworkPeriod.Daily,
                ShareType = HomeworkShareType.Account,
                AutoMode = AutoDetectMode.ManualOnly,
                Title = "은동전 상자 교환",
                Subtitle = "데카/포인트 은동전 상자 일일 교환",
                Icon = "🪙",
                MatchKeywords = new() { "은동전", "데카" }
            });

            // 2. 주간 숙제 (Weekly)
            MasterList.Add(new HomeworkDefinition
            {
                Id = "weekly_missions_all",
                Category = HomeworkCategory.Weekly,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Account,
                AutoMode = AutoDetectMode.DirectMission,
                Title = "주간 미션 완수",
                Subtitle = "주간 미션 100% 달성하기",
                Icon = "📜",
                GoalCount = 15,
                MatchKeywords = new() { "주간 미션" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "weekly_guild_mission",
                Category = HomeworkCategory.Weekly,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.DirectMission,
                Title = "길드 미션 & 출석 기부",
                Subtitle = "주간 길드 미션 및 골드 기부",
                Icon = "🛡️",
                MatchKeywords = new() { "길드", "기부", "길드 미션" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "weekly_black_hole_1_7",
                Category = HomeworkCategory.Weekly,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.DirectMission,
                Title = "검은 구멍 (주간 1~7단계)",
                Subtitle = "주간 검은 구멍 7회 차원 정화",
                Icon = "🌌",
                GoalCount = 7,
                MatchKeywords = new() { "검은 구멍", "주간 검은 구멍" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "weekly_barrier_1_7",
                Category = HomeworkCategory.Weekly,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.DirectMission,
                Title = "불길한 소환의 결계 (주간 1~7)",
                Subtitle = "주간 소환의 결계 7회 정화",
                Icon = "🧿",
                GoalCount = 7,
                MatchKeywords = new() { "불길한 소환의 결계", "결계" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "weekly_heart_holy_water",
                Category = HomeworkCategory.Shop,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Account,
                AutoMode = AutoDetectMode.ManualOnly,
                Title = "하트 토큰 성수 교환",
                Subtitle = "하트 상점 주간 성수 교환",
                Icon = "💧",
                MatchKeywords = new() { "성수", "하트 토큰" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "weekly_cash_fashion_ticket",
                Category = HomeworkCategory.Shop,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Account,
                AutoMode = AutoDetectMode.ManualOnly,
                Title = "할인 패션 티켓 구매",
                Subtitle = "캐시샵 주간 할인 패션 티켓 교환",
                Icon = "🎟️",
                MatchKeywords = new() { "패션 티켓", "할인 티켓" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "weekly_cash_pet_ticket",
                Category = HomeworkCategory.Shop,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Account,
                AutoMode = AutoDetectMode.ManualOnly,
                Title = "할인 펫 티켓 구매",
                Subtitle = "캐시샵 주간 할인 펫 티켓 교환",
                Icon = "🐾",
                MatchKeywords = new() { "펫 티켓", "할인 티켓" }
            });

            // 3. 필드 보스 (Field Boss - 주간)
            MasterList.Add(new HomeworkDefinition
            {
                Id = "fieldboss_peri",
                Category = HomeworkCategory.FieldBoss,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.EnvironmentCombat,
                Title = "페리",
                Subtitle = "창백한 산 필드 보스 페리 토벌",
                Icon = "🐺",
                MatchKeywords = new() { "페리", "창백한 산" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "fieldboss_crabbach",
                Category = HomeworkCategory.FieldBoss,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.EnvironmentCombat,
                Title = "크라브바흐",
                Subtitle = "센마이 평원 필드 보스 크라브바흐 토벌",
                Icon = "🦀",
                MatchKeywords = new() { "크라브바흐", "센마이" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "fieldboss_krama",
                Category = HomeworkCategory.FieldBoss,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.EnvironmentCombat,
                Title = "크라마",
                Subtitle = "케오 섬 필드 보스 크라마 토벌",
                Icon = "🦎",
                MatchKeywords = new() { "크라마", "케오" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "fieldboss_angrbahan",
                Category = HomeworkCategory.FieldBoss,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.EnvironmentCombat,
                Title = "앙그르바한",
                Subtitle = "바라프 계곡 필드 보스 앙그르바한 토벌",
                Icon = "👹",
                MatchKeywords = new() { "앙그르바한", "앙그라바한", "바라프" }
            });

            // 4. 어비스 (Abyss - 주간)
            MasterList.Add(new HomeworkDefinition
            {
                Id = "abyss_gate_1",
                Category = HomeworkCategory.Abyss,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.EnvironmentCombat,
                Title = "첫 번째 관문 (가라앉은 유적)",
                Subtitle = "어비스 심층 1관문 토벌",
                Icon = "🏛️",
                MatchKeywords = new() { "가라앉은 유적", "첫 번째 관문", "어비스 1" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "abyss_gate_2",
                Category = HomeworkCategory.Abyss,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.EnvironmentCombat,
                Title = "두 번째 관문 (무너진 제단)",
                Subtitle = "어비스 심층 2관문 토벌",
                Icon = "⛩️",
                MatchKeywords = new() { "무너진 제단", "두 번째 관문", "어비스 2" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "abyss_gate_3",
                Category = HomeworkCategory.Abyss,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.EnvironmentCombat,
                Title = "세 번째 관문 (파멸의 전당)",
                Subtitle = "어비스 심층 3관문 토벌",
                Icon = "🔥",
                MatchKeywords = new() { "파멸의 전당", "세 번째 관문", "어비스 3" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "abyss_gate_4",
                Category = HomeworkCategory.Abyss,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.EnvironmentCombat,
                Title = "네 번째 관문 (심연의 중심)",
                Subtitle = "어비스 심층 최종 관문 토벌",
                Icon = "🌀",
                MatchKeywords = new() { "네 번째 관문", "어비스 4", "심연의 중심" }
            });

            // 5. 레이드 (Raid - 주간)
            MasterList.Add(new HomeworkDefinition
            {
                Id = "raid_white_succubus",
                Category = HomeworkCategory.Raid,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.QuestTracker,
                Title = "화이트 서큐버스",
                Subtitle = "주간 레이드 화서큐 토벌",
                Icon = "👑",
                MatchKeywords = new() { "화이트 서큐버스", "화서큐", "서큐버스" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "raid_tabartas",
                Category = HomeworkCategory.Raid,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.QuestTracker,
                Title = "타바르타스",
                Subtitle = "주간 레이드 거신 타바르타스 토벌",
                Icon = "🗿",
                MatchKeywords = new() { "타바르타스", "타르타로스", "골렘" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "raid_airel",
                Category = HomeworkCategory.Raid,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.QuestTracker,
                Title = "에이렐",
                Subtitle = "주간 레이드 얼음 여왕 에이렐 토벌",
                Icon = "❄️",
                MatchKeywords = new() { "에이렐", "아이렐" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "raid_cavrak",
                Category = HomeworkCategory.Raid,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.QuestTracker,
                Title = "카브락",
                Subtitle = "주간 레이드 흑룡 카브락 토벌",
                Icon = "🐲",
                MatchKeywords = new() { "카브락", "카브라크", "카록" }
            });

            MasterList.Add(new HomeworkDefinition
            {
                Id = "raid_vanguard_breach",
                Category = HomeworkCategory.Raid,
                Period = HomeworkPeriod.Weekly,
                ShareType = HomeworkShareType.Character,
                AutoMode = AutoDetectMode.QuestTracker,
                Title = "뱅가드 브리치",
                Subtitle = "주간 결전 뱅가드 브리치 돌파",
                Icon = "🚩",
                MatchKeywords = new() { "뱅가드 브리치", "뱅가드", "브리치" }
            });
        }

        public static DateTime GetLastDailyResetTime(DateTime now)
        {
            // 매일 오전 06:00
            var todayReset = new DateTime(now.Year, now.Month, now.Day, 6, 0, 0, now.Kind);
            if (now >= todayReset)
                return todayReset;
            return todayReset.AddDays(-1);
        }

        public static DateTime GetNextDailyResetTime(DateTime now)
        {
            return GetLastDailyResetTime(now).AddDays(1);
        }

        public static DateTime GetLastWeeklyResetTime(DateTime now)
        {
            // 매주 목요일 오전 06:00
            // DayOfWeek: Sunday(0), Monday(1), Tuesday(2), Wednesday(3), Thursday(4), Friday(5), Saturday(6)
            var currentDay = new DateTime(now.Year, now.Month, now.Day, 6, 0, 0, now.Kind);
            int diff = ((int)currentDay.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7;
            var candidateReset = currentDay.AddDays(-diff);

            if (now >= candidateReset)
                return candidateReset;

            return candidateReset.AddDays(-7);
        }

        public static DateTime GetNextWeeklyResetTime(DateTime now)
        {
            return GetLastWeeklyResetTime(now).AddDays(7);
        }

        public Dictionary<string, HomeworkCharacterRecord> LoadAllRecords()
        {
            if (!File.Exists(_saveFilePath))
                return new Dictionary<string, HomeworkCharacterRecord>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var json = File.ReadAllText(_saveFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, HomeworkCharacterRecord>>(json, JsonOptions);
                return data ?? new Dictionary<string, HomeworkCharacterRecord>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                try
                {
                    var backupPath = $"{_saveFilePath}.corrupted.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                    File.Copy(_saveFilePath, backupPath, true);
                }
                catch { }

                return new Dictionary<string, HomeworkCharacterRecord>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public HomeworkCharacterRecord GetOrCreateRecord(string characterKey, DateTime now)
        {
            var records = LoadAllRecords();
            if (!records.TryGetValue(characterKey, out var record) || record == null)
            {
                record = new HomeworkCharacterRecord
                {
                    CharacterKey = characterKey,
                    LastDailyReset = GetLastDailyResetTime(now),
                    LastWeeklyReset = GetLastWeeklyResetTime(now)
                };
                records[characterKey] = record;
            }

            CheckAndApplyResets(record, now);
            return record;
        }

        public void CheckAndApplyResets(HomeworkCharacterRecord record, DateTime now)
        {
            var lastDaily = GetLastDailyResetTime(now);
            var lastWeekly = GetLastWeeklyResetTime(now);

            bool needsSave = false;

            foreach (var def in MasterList)
            {
                if (!record.Items.TryGetValue(def.Id, out var itemState))
                {
                    itemState = new HomeworkItemState
                    {
                        Id = def.Id,
                        GoalCount = def.GoalCount,
                        IsCompleted = false
                    };
                    record.Items[def.Id] = itemState;
                    needsSave = true;
                }

                // 리셋 검사
                if (itemState.IsCompleted && itemState.CompletedAt.HasValue)
                {
                    if (def.Period == HomeworkPeriod.Daily && itemState.CompletedAt.Value < lastDaily)
                    {
                        itemState.IsCompleted = false;
                        itemState.CurrentCount = 0;
                        itemState.IsAutoDetected = false;
                        needsSave = true;
                    }
                    else if (def.Period == HomeworkPeriod.Weekly && itemState.CompletedAt.Value < lastWeekly)
                    {
                        itemState.IsCompleted = false;
                        itemState.CurrentCount = 0;
                        itemState.IsAutoDetected = false;
                        needsSave = true;
                    }
                }
            }

            record.LastDailyReset = lastDaily;
            record.LastWeeklyReset = lastWeekly;

            if (needsSave)
            {
                SaveRecord(record);
            }
        }

        public void SaveRecord(HomeworkCharacterRecord record)
        {
            var records = LoadAllRecords();
            records[record.CharacterKey] = record;

            var tmpFile = $"{_saveFilePath}.tmp";
            try
            {
                var json = JsonSerializer.Serialize(records, JsonOptions);
                File.WriteAllText(tmpFile, json);
                File.Move(tmpFile, _saveFilePath, true);
            }
            catch
            {
                if (File.Exists(tmpFile))
                {
                    try { File.Delete(tmpFile); } catch { }
                }
                throw;
            }
        }
    }
}
