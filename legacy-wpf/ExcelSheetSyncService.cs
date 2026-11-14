using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace MobiMate;

public class CharacterSheetMasterEntry
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string DefaultJob { get; set; } = "";
}

public class ExcelSheetSyncService
{
    private string _targetFilePath;
    private readonly object _lock = new();

    public string TargetFilePath => _targetFilePath;

    public void SetTargetFilePath(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return;
        lock (_lock)
        {
            _targetFilePath = newPath;
            var dir = Path.GetDirectoryName(_targetFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }

    public static readonly List<CharacterSheetMasterEntry> DefaultMasterCharacters = new()
    {
        new() { Index = 1, Name = "GALAXYZ", DefaultJob = "힐러" },
        new() { Index = 2, Name = "GalaxyMagic", DefaultJob = "빙결술사" },
        new() { Index = 3, Name = "GalaxyZFlip", DefaultJob = "댄서" },
        new() { Index = 4, Name = "GalaxyEdge", DefaultJob = "검술사" },
        new() { Index = 5, Name = "GalaxyUltra", DefaultJob = "석궁사수" },
        new() { Index = 6, Name = "GalaxyFight", DefaultJob = "격투가" },
        new() { Index = 7, Name = "이그엘", DefaultJob = "궁수" },
        new() { Index = 8, Name = "이그나이", DefaultJob = "대검전사" },
        new() { Index = 9, Name = "윈클라우드", DefaultJob = "화염술사" },
        new() { Index = 10, Name = "o윈클라우드o", DefaultJob = "사제" },
        new() { Index = 11, Name = "빅클라우드", DefaultJob = "전사" },
        new() { Index = 12, Name = "빅콜라", DefaultJob = "듀얼블레이드" }
    };

    public ExcelSheetSyncService(string? customFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _targetFilePath = customFilePath;
        }
        else
        {
            _targetFilePath = ResolveDefaultSavePath();
        }

        var dir = Path.GetDirectoryName(_targetFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public static string ResolveDefaultSavePath()
    {
        // 1순위: 구글 드라이브 Games\Mabinogi\
        var gDriveMabinogi = @"G:\내 드라이브\Games\Mabinogi";
        if (Directory.Exists(gDriveMabinogi))
        {
            return Path.Combine(gDriveMabinogi, "마비노기_모바일_캐릭터육성.xlsx");
        }

        // 2순위: 구글 드라이브 루트
        var gDriveRoot = @"G:\내 드라이브";
        if (Directory.Exists(gDriveRoot))
        {
            var targetDir = Path.Combine(gDriveRoot, "Mabinogi");
            Directory.CreateDirectory(targetDir);
            return Path.Combine(targetDir, "마비노기_모바일_캐릭터육성.xlsx");
        }

        // 3순위: 내 문서\Mabinogi\
        var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var docsMabinogi = Path.Combine(myDocs, "Mabinogi");
        Directory.CreateDirectory(docsMabinogi);
        return Path.Combine(docsMabinogi, "마비노기_모바일_캐릭터육성.xlsx");
    }

    public (bool success, string message, int updatedRow) SyncCharacter(
        string characterName,
        CharacterInfo charInfo,
        HomeworkRepository? homeworkRepo)
    {
        if (string.IsNullOrWhiteSpace(characterName))
        {
            return (false, "캐릭터 이름이 비어 있습니다.", 0);
        }

        lock (_lock)
        {
            try
            {
                XLWorkbook workbook;
                IXLWorksheet worksheet;

                if (File.Exists(_targetFilePath))
                {
                    try
                    {
                        workbook = new XLWorkbook(_targetFilePath);
                        worksheet = workbook.Worksheet("캐릭터 육성") ?? workbook.Worksheets.FirstOrDefault() ?? workbook.AddWorksheet("캐릭터 육성");
                    }
                    catch (IOException)
                    {
                        return (false, "엑셀 파일이 다른 프로그램(Excel 등)에서 열려 있어 저장할 수 없습니다. 엑셀을 닫고 다시 시도해 주세요.", 0);
                    }
                    catch (Exception)
                    {
                        // 손상된 파일 등의 경우 새 워크북으로 복구
                        workbook = CreateNewWorkbook();
                        worksheet = workbook.Worksheet("캐릭터 육성");
                    }
                }
                else
                {
                    workbook = CreateNewWorkbook();
                    worksheet = workbook.Worksheet("캐릭터 육성");
                }

                using (workbook)
                {
                    var targetRow = FindOrCreateNameRow(worksheet, characterName);

                    // 1. 스탯 핀포인트 갱신 (B=번호, C=이름, D=클래스, E=전투력, F=생활력, G=매력, H=마도저항)
                    var jobName = !string.IsNullOrWhiteSpace(charInfo.JobName) ? charInfo.JobName : "";
                    if (!string.IsNullOrWhiteSpace(jobName))
                    {
                        worksheet.Cell(targetRow, 4).Value = jobName; // D열: 클래스
                    }

                    if (charInfo.CombatScore != null)
                    {
                        worksheet.Cell(targetRow, 5).Value = charInfo.CombatScore.Value; // E열: 전투력
                        worksheet.Cell(targetRow, 5).Style.NumberFormat.Format = "#,##0";
                    }

                    if (charInfo.LivingScore != null)
                    {
                        worksheet.Cell(targetRow, 6).Value = charInfo.LivingScore.Value; // F열: 생활력
                        worksheet.Cell(targetRow, 6).Style.NumberFormat.Format = "#,##0";
                    }

                    if (charInfo.AttractivenessScore != null)
                    {
                        worksheet.Cell(targetRow, 7).Value = charInfo.AttractivenessScore.Value; // G열: 매력
                        worksheet.Cell(targetRow, 7).Style.NumberFormat.Format = "#,##0";
                    }

                    if (charInfo.ArcaneResistance != null)
                    {
                        worksheet.Cell(targetRow, 8).Value = charInfo.ArcaneResistance.Value; // H열: 마도 저항
                        worksheet.Cell(targetRow, 8).Style.NumberFormat.Format = "#,##0";
                    }

                    // 2. 주간 숙제 달성 현황 (I=카브락, J=에이렐, K=화서큐, L=필드보스, M=뱅가드, N=최근 동기화)
                    if (homeworkRepo != null)
                    {
                        var realm = string.IsNullOrEmpty(charInfo.RealmName) ? "에린" : charInfo.RealmName;
                        var job = string.IsNullOrEmpty(charInfo.JobName) ? "밀레시안" : charInfo.JobName;
                        var charKey = $"{realm}_{job}";
                        var now = DateTime.Now;
                        var record = homeworkRepo.GetOrCreateRecord(charKey, now);

                        // I열: 카브락
                        bool cavrak = record.Items.TryGetValue("raid_cavrak", out var s1) && s1.IsCompleted;
                        SetHomeworkCell(worksheet.Cell(targetRow, 9), cavrak);

                        // J열: 에이렐
                        bool eirel = record.Items.TryGetValue("raid_airel", out var s2) && s2.IsCompleted;
                        SetHomeworkCell(worksheet.Cell(targetRow, 10), eirel);

                        // K열: 화서큐
                        bool succubus = record.Items.TryGetValue("raid_white_succubus", out var s3) && s3.IsCompleted;
                        SetHomeworkCell(worksheet.Cell(targetRow, 11), succubus);

                        // L열: 필드보스 (주간 택1)
                        bool fieldBoss = record.Items.Values.Any(s => s.Id.StartsWith("fieldboss_") && s.IsCompleted);
                        SetHomeworkCell(worksheet.Cell(targetRow, 12), fieldBoss);

                        // M열: 뱅가드 브리치
                        bool vanguard = record.Items.TryGetValue("weekly_vanguard_breach", out var s4) && s4.IsCompleted;
                        SetHomeworkCell(worksheet.Cell(targetRow, 13), vanguard);
                    }

                    // N열: 최근 동기화 시각
                    worksheet.Cell(targetRow, 14).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    worksheet.Cell(targetRow, 14).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // 열 너비 자동 맞춤
                    worksheet.Columns().AdjustToContents();

                    // 파일 저장
                    workbook.SaveAs(_targetFilePath);

                    return (true, $"[{characterName}] 엑셀 동기화 완료 ({targetRow}행)", targetRow);
                }
            }
            catch (IOException ex)
            {
                return (false, $"엑셀 파일 접근 오류 (파일이 열려 있을 수 있습니다): {ex.Message}", 0);
            }
            catch (Exception ex)
            {
                return (false, $"동기화 오류: {ex.Message}", 0);
            }
        }
    }

    private static void SetHomeworkCell(IXLCell cell, bool isCompleted)
    {
        cell.Value = isCompleted ? "O" : "-";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Font.Bold = isCompleted;
        if (isCompleted)
        {
            cell.Style.Font.FontColor = XLColor.FromHtml("#16A34A"); // Accent Green
        }
        else
        {
            cell.Style.Font.FontColor = XLColor.FromHtml("#94A3B8"); // Subtle Gray
        }
    }

    private static int FindOrCreateNameRow(IXLWorksheet ws, string characterName)
    {
        var cleanSearch = characterName.Trim().Replace(" ", "").ToLowerInvariant();

        // 3행부터 마지막 행까지 C열(캐릭터 이름) 검색
        var lastRow = Math.Max(ws.LastRowUsed()?.RowNumber() ?? 14, 14);
        for (var r = 3; r <= lastRow; r++)
        {
            var cellVal = ws.Cell(r, 3).GetString().Trim().Replace(" ", "").ToLowerInvariant();
            if (!string.IsNullOrEmpty(cellVal) && cellVal != "평균" && cellVal != "최고값")
            {
                if (cellVal == cleanSearch || cellVal.Contains(cleanSearch) || cleanSearch.Contains(cellVal))
                {
                    return r;
                }
            }
        }

        // 못 찾으면 요약행 이전 또는 새 행 추가
        var newRow = 15;
        while (!ws.Cell(newRow, 3).IsEmpty() && newRow < 100)
        {
            newRow++;
        }

        ws.Cell(newRow, 2).Value = newRow - 2; // 번호
        ws.Cell(newRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(newRow, 3).Value = characterName; // 이름
        ws.Cell(newRow, 3).Style.Font.Bold = true;
        return newRow;
    }

    public static XLWorkbook CreateNewWorkbook()
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("캐릭터 육성");

        ws.ShowGridLines = true;

        // 1행: 타이틀 배너
        ws.Range("B1:N1").Merge();
        var titleCell = ws.Cell("B1");
        titleCell.Value = "마비노기 모바일 캐릭터 종합 육성 현황판 (MobiMate 자동 동기화)";
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 14;
        titleCell.Style.Font.FontColor = XLColor.White;
        titleCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79"); // 비즈니스 다크 네이비
        titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 36;

        // 2행: 헤더
        var headers = new[]
        {
            "번호", "캐릭터 이름", "클래스", "전투력", "생활력", "매력", "마도 저항",
            "카브락", "에이렐", "화서큐", "필드보스", "뱅가드", "최근 동기화"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var col = i + 2; // B열(2)부터 시작
            var cell = ws.Cell(2, col);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 11;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2F5597");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#D9D9D9");
        }
        ws.Row(2).Height = 28;

        // 3~14행: 12개 마스터 캐릭터 기본 데이터 배치
        for (var i = 0; i < DefaultMasterCharacters.Count; i++)
        {
            var charData = DefaultMasterCharacters[i];
            var r = i + 3; // 3행부터 시작

            ws.Cell(r, 2).Value = charData.Index; // B열: 번호
            ws.Cell(r, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(r, 3).Value = charData.Name; // C열: 이름
            ws.Cell(r, 3).Style.Font.Bold = true;

            ws.Cell(r, 4).Value = charData.DefaultJob; // D열: 클래스
            ws.Cell(r, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // 숫자 기본 0 및 서식
            for (var col = 5; col <= 8; col++)
            {
                ws.Cell(r, col).Value = 0;
                ws.Cell(r, col).Style.NumberFormat.Format = "#,##0";
                ws.Cell(r, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }

            // 숙제 미완료 기본값 '-'
            for (var col = 9; col <= 13; col++)
            {
                SetHomeworkCell(ws.Cell(r, col), false);
            }

            ws.Cell(r, 14).Value = "-";
            ws.Cell(r, 14).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // 짝수행 은은한 음영
            if (i % 2 == 1)
            {
                ws.Range(r, 2, r, 14).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F4F8");
            }

            // 테두리
            ws.Range(r, 2, r, 14).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(r, 2, r, 14).Style.Border.OutsideBorderColor = XLColor.FromHtml("#E2E8F0");
            ws.Row(r).Height = 22;
        }

        // 16행: 평균 통계행
        ws.Cell(16, 3).Value = "평균";
        ws.Cell(16, 3).Style.Font.Bold = true;
        ws.Cell(16, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        for (var col = 5; col <= 8; col++)
        {
            var colLetter = ws.Column(col).ColumnLetter();
            ws.Cell(16, col).FormulaA1 = $"AVERAGE({colLetter}3:{colLetter}14)";
            ws.Cell(16, col).Style.NumberFormat.Format = "#,##0";
            ws.Cell(16, col).Style.Font.Bold = true;
        }
        ws.Range("B16:N16").Style.Fill.BackgroundColor = XLColor.FromHtml("#EDF2F7");
        ws.Range("B16:N16").Style.Border.TopBorder = XLBorderStyleValues.Thin;
        ws.Range("B16:N16").Style.Border.BottomBorder = XLBorderStyleValues.Double;

        // 17행: 최고 전투력 행
        ws.Cell(17, 3).Value = "최고값";
        ws.Cell(17, 3).Style.Font.Bold = true;
        ws.Cell(17, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        for (var col = 5; col <= 8; col++)
        {
            var colLetter = ws.Column(col).ColumnLetter();
            ws.Cell(17, col).FormulaA1 = $"MAX({colLetter}3:{colLetter}14)";
            ws.Cell(17, col).Style.NumberFormat.Format = "#,##0";
            ws.Cell(17, col).Style.Font.Bold = true;
        }
        ws.Range("B17:N17").Style.Fill.BackgroundColor = XLColor.FromHtml("#EDF2F7");
        ws.Range("B17:N17").Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        // 컬럼 너비 기본 여유 맞춤
        ws.Columns().AdjustToContents();
        ws.Column(2).Width = 8;   // 번호
        ws.Column(3).Width = 16;  // 이름
        ws.Column(4).Width = 14;  // 클래스
        ws.Column(5).Width = 14;  // 전투력
        ws.Column(6).Width = 12;  // 생활력
        ws.Column(7).Width = 12;  // 매력
        ws.Column(8).Width = 12;  // 마도 저항
        ws.Column(14).Width = 20; // 최근 동기화

        return wb;
    }
}
