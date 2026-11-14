/**
 * [마비노기 모바일] 캐릭터 육성 구글 스프레드시트 맞춤 자동 동기화 & 안전 백업 스크립트
 * 
 * [承雲 스프레드시트 실측 레이아웃 100% 일치 매핑]
 * - C열 (3): 캐릭터 이름 (빅클라우드, 빅콜라 등)
 * - E열 (5): 클래스 (전사, 듀얼블레이드 등)
 * - G열 (7): 전투력 (= 전투력 =)
 * - H열 (8): 생활력 (= 생활력 =)
 * - I열 (9): 매력 (= 매력 =)
 * - J열 (10): 마도 저항 (= 마도 저항 =)
 * - L~R열: 주간 숙제 (헤더 자동 감지 및 핀포인트 갱신)
 */

function doPost(e) {
  try {
    if (!e || !e.postData || !e.postData.contents) {
      return responseJson({ status: "error", message: "전송된 데이터가 없습니다." });
    }

    var data = JSON.parse(e.postData.contents);
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var sheet = ss.getSheetByName("캐릭터 육성");
    
    if (!sheet) {
      sheet = ss.getSheets()[0];
    }

    // 1. 당일 1회 안전 자동 백업 (요청 시 구글 드라이브에 안전 사본 복제)
    if (data.backup === true) {
      makeDailyBackupOnce(ss);
    }

    var values = sheet.getDataRange().getValues();
    var targetRow = -1;
    var rawSearchName = (data.name || "").toString();
    var searchClean = cleanStr(rawSearchName);

    if (!searchClean) {
      return responseJson({ status: "error", message: "캐릭터 이름이 지정되지 않았습니다." });
    }

    // 2. 캐릭터 행(Row) 탐색
    // [1순위] C열 (인덱스 2)에서 캐릭터 이름 탐색
    for (var r = 0; r < values.length; r++) {
      var cellVal = cleanStr(values[r][2]);
      if (cellVal && (cellVal === searchClean || cellVal.indexOf(searchClean) !== -1 || searchClean.indexOf(cellVal) !== -1)) {
        targetRow = r + 1;
        break;
      }
    }

    // [2순위] C열에서 못 찾으면 A~E열(인덱스 0~4) 전체 스캔
    if (targetRow === -1) {
      for (var r = 0; r < values.length; r++) {
        for (var c = 0; c < Math.min(values[r].length, 6); c++) {
          var cellVal = cleanStr(values[r][c]);
          if (cellVal && (cellVal === searchClean || cellVal.indexOf(searchClean) !== -1 || searchClean.indexOf(cellVal) !== -1)) {
            targetRow = r + 1;
            break;
          }
        }
        if (targetRow !== -1) break;
      }
    }

    if (targetRow === -1) {
      return responseJson({ 
        status: "error", 
        message: "시트 C열에서 '" + rawSearchName + "' 캐릭터를 찾을 수 없습니다." 
      });
    }

    // 3. 열 번호 감지 (실측 레이아웃 기준)
    var cols = detectColumns(values);

    // 4. 핀포인트 셀 값 갱신 (D, F열 공백 및 서식, 배경색, 수식 100% 무훼손 보존)
    // E열: 클래스
    if (data.job !== undefined && data.job !== null) {
      sheet.getRange(targetRow, cols.job).setValue(data.job);
    }
    // G열: 전투력
    if (data.combatScore !== undefined && data.combatScore !== null) {
      sheet.getRange(targetRow, cols.combat).setValue(data.combatScore);
    }
    // H열: 생활력
    if (data.livingScore !== undefined && data.livingScore !== null) {
      sheet.getRange(targetRow, cols.living).setValue(data.livingScore);
    }
    // I열: 매력
    if (data.attractiveness !== undefined && data.attractiveness !== null) {
      sheet.getRange(targetRow, cols.attract).setValue(data.attractiveness);
    }
    // J열: 마도 저항
    if (data.arcaneResist !== undefined && data.arcaneResist !== null) {
      sheet.getRange(targetRow, cols.arcane).setValue(data.arcaneResist);
    }

    // 주간 숙제 완료 여부 (L~R열)
    if (data.raidCavrak !== undefined && cols.raidCavrak > 0) {
      sheet.getRange(targetRow, cols.raidCavrak).setValue(data.raidCavrak ? "O" : "");
    }
    if (data.raidEirel !== undefined && cols.raidEirel > 0) {
      sheet.getRange(targetRow, cols.raidEirel).setValue(data.raidEirel ? "O" : "");
    }
    if (data.raidWhiteSuccubus !== undefined && cols.raidSuccubus > 0) {
      sheet.getRange(targetRow, cols.raidSuccubus).setValue(data.raidWhiteSuccubus ? "O" : "");
    }
    if (data.fieldBoss !== undefined && cols.fieldBoss > 0) {
      sheet.getRange(targetRow, cols.fieldBoss).setValue(data.fieldBoss ? "O" : "");
    }
    if (data.vanguard !== undefined && cols.vanguard > 0) {
      sheet.getRange(targetRow, cols.vanguard).setValue(data.vanguard ? "O" : "");
    }

    return responseJson({
      status: "success",
      message: "[" + rawSearchName + "] 동기화 완료 (" + targetRow + "행 갱신)",
      updatedRow: targetRow,
      character: rawSearchName
    });

  } catch (err) {
    return responseJson({ status: "error", message: err.toString() });
  }
}

// 문자열 정규화 (공백/특수공백/등호 제거, NFC 정규화, 소문자화)
function cleanStr(s) {
  if (!s) return "";
  return s.toString()
    .normalize("NFC")
    .replace(/[=\s\u00A0\u3000\t\r\n]+/g, "")
    .toLowerCase();
}

// 실측 기반 열 번호 감지
function detectColumns(values) {
  // 承雲 시트 실측 기본값 (1-based index)
  // C열(3)=이름, E열(5)=클래스, G열(7)=전투력, H열(8)=생활력, I열(9)=매력, J열(10)=마도저항
  var mapping = {
    job: 5,
    combat: 7,
    living: 8,
    attract: 9,
    arcane: 10,
    raidCavrak: 14,
    raidEirel: 15,
    raidSuccubus: 16,
    fieldBoss: 19,
    vanguard: 20
  };

  // 1~3행 헤더 스캔하여 텍스트 매칭
  for (var r = 0; r < Math.min(values.length, 3); r++) {
    for (var c = 0; c < values[r].length; c++) {
      var header = cleanStr(values[r][c]);
      if (!header) continue;
      var colIdx = c + 1;

      if (header.indexOf("클래스") !== -1 || header.indexOf("직업") !== -1) mapping.job = colIdx;
      else if (header.indexOf("전투력") !== -1 || header.indexOf("투급") !== -1) mapping.combat = colIdx;
      else if (header.indexOf("생활력") !== -1) mapping.living = colIdx;
      else if (header.indexOf("매력") !== -1) mapping.attract = colIdx;
      else if (header.indexOf("마도") !== -1 || header.indexOf("마도저항") !== -1) mapping.arcane = colIdx;
      else if (header.indexOf("카브락") !== -1) mapping.raidCavrak = colIdx;
      else if (header.indexOf("에이렐") !== -1 || header.indexOf("아이렐") !== -1) mapping.raidEirel = colIdx;
      else if (header.indexOf("화서큐") !== -1 || header.indexOf("서큐버스") !== -1) mapping.raidSuccubus = colIdx;
      else if (header.indexOf("필드보스") !== -1 || header.indexOf("필보") !== -1) mapping.fieldBoss = colIdx;
      else if (header.indexOf("뱅가드") !== -1 || header.indexOf("브리치") !== -1) mapping.vanguard = colIdx;
    }
  }

  return mapping;
}

// 당일 1회 안전 백업
function makeDailyBackupOnce(ss) {
  try {
    var todayStr = Utilities.formatDate(new Date(), Session.getScriptTimeZone() || "Asia/Seoul", "yyyyMMdd");
    var propKey = "LAST_BACKUP_DATE";
    var props = PropertiesService.getScriptProperties();
    if (props.getProperty(propKey) === todayStr) return;

    var file = DriveApp.getFileById(ss.getId());
    var backupName = "[자동백업] " + ss.getName() + "_" + todayStr;
    file.makeCopy(backupName);
    props.setProperty(propKey, todayStr);
  } catch (e) {
  }
}

function responseJson(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj))
    .setMimeType(ContentService.MimeType.JSON);
}
