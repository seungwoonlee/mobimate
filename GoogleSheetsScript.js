/**
 * [마비노기 모바일] 캐릭터 육성 구글 스프레드시트 자동 동기화 & 안전 백업 스크립트
 * 
 * [설치 및 배포 방법]
 * 1. 스프레드시트 상단 메뉴 [확장 프로그램] -> [Apps Script] 클릭
 * 2. 기존 코드를 모두 지우고 이 스크립트 전체를 붙여넣기
 * 3. 상단 [저장(디스켓 아이콘)] 클릭
 * 4. 우측 상단 파란색 [배포] -> [새 배포] 클릭
 *    - 유형 선택: [웹 앱] (톱니바퀴 아이콘)
 *    - 설명: MobiMate Sync
 *    - 다음 사용자로 실행: 나 (내 계정)
 *    - 액세스 권한이 있는 사용자: [모든 사용자] (Anyone)  <-- 중요!
 * 5. [배포] 버튼 클릭 후 생성되는 "웹 앱 URL"(https://script.google.com/macros/s/.../exec) 복사
 * 6. MobiMate 앱의 [📊 구글 시트 연동] 창에 해당 URL을 붙여넣기
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
      return responseJson({ status: "error", message: "'캐릭터 육성' 탭을 찾을 수 없습니다." });
    }

    // 1. 당일 1회 안전 자동 백업 (요청 시 구글 드라이브에 안전 사본 복제)
    if (data.backup === true) {
      makeDailyBackupOnce(ss);
    }

    // 2. B열(인덱스 1)에서 캐릭터 이름 탐색
    var values = sheet.getDataRange().getValues();
    var targetRow = -1;
    var searchName = (data.name || "").toString().trim();

    if (!searchName) {
      return responseJson({ status: "error", message: "캐릭터 이름이 지정되지 않았습니다." });
    }

    // 3행(배열 인덱스 2)부터 캐릭터 데이터 검색
    for (var i = 2; i < values.length; i++) {
      var cellName = (values[i][1] || "").toString().trim();
      if (cellName === searchName) {
        targetRow = i + 1; // 1-based row index
        break;
      }
    }

    if (targetRow === -1) {
      return responseJson({ 
        status: "error", 
        message: "시트에서 캐릭터를 찾을 수 없습니다: " + searchName + " (B열 이름을 확인하세요)" 
      });
    }

    // 3. 핀포인트 셀 값 갱신 (서식, 배경색, 수기 메모, 기타 열 100% 보존)
    // C열 (3): 클래스
    if (data.job !== undefined && data.job !== null) {
      sheet.getRange(targetRow, 3).setValue(data.job);
    }
    // E열 (5): 전투력
    if (data.combatScore !== undefined && data.combatScore !== null) {
      sheet.getRange(targetRow, 5).setValue(data.combatScore);
    }
    // F열 (6): 생활력
    if (data.livingScore !== undefined && data.livingScore !== null) {
      sheet.getRange(targetRow, 6).setValue(data.livingScore);
    }
    // G열 (7): 매력
    if (data.attractiveness !== undefined && data.attractiveness !== null) {
      sheet.getRange(targetRow, 7).setValue(data.attractiveness);
    }
    // H열 (8): 마도저항
    if (data.arcaneResist !== undefined && data.arcaneResist !== null) {
      sheet.getRange(targetRow, 8).setValue(data.arcaneResist);
    }

    // 주간 숙제 완료 여부 (L, M, N, Q, R열)
    // L열 (12): 카브락 레이드
    if (data.raidCavrak !== undefined) {
      sheet.getRange(targetRow, 12).setValue(data.raidCavrak ? "O" : "");
    }
    // M열 (13): 에이렐 레이드
    if (data.raidEirel !== undefined) {
      sheet.getRange(targetRow, 13).setValue(data.raidEirel ? "O" : "");
    }
    // N열 (14): 화이트 서큐버스 레이드
    if (data.raidWhiteSuccubus !== undefined) {
      sheet.getRange(targetRow, 14).setValue(data.raidWhiteSuccubus ? "O" : "");
    }
    // Q열 (17): 필드보스 (주간 택1)
    if (data.fieldBoss !== undefined) {
      sheet.getRange(targetRow, 17).setValue(data.fieldBoss ? "O" : "");
    }
    // R열 (18): 뱅가드 브리치
    if (data.vanguard !== undefined) {
      sheet.getRange(targetRow, 18).setValue(data.vanguard ? "O" : "");
    }

    return responseJson({
      status: "success",
      row: targetRow,
      character: searchName,
      updatedAt: new Date().toISOString()
    });

  } catch (err) {
    return responseJson({ status: "error", message: err.toString() });
  }
}

/**
 * 당일 1회 안전 사본 생성 (드라이브 용량 급증 방지)
 */
function makeDailyBackupOnce(ss) {
  try {
    var todayStr = Utilities.formatDate(new Date(), "Asia/Seoul", "yyyyMMdd");
    var props = PropertiesService.getScriptProperties();
    var lastBackup = props.getProperty("LAST_BACKUP_DATE");

    if (lastBackup !== todayStr) {
      var file = DriveApp.getFileById(ss.getId());
      var backupName = "[MobiMate_백업] " + ss.getName() + "_" + todayStr;
      file.makeCopy(backupName);
      props.setProperty("LAST_BACKUP_DATE", todayStr);
    }
  } catch (e) {
    // 백업 권한/오류가 나더라도 본 동기화는 정상 진행
    console.error("Backup error: " + e.toString());
  }
}

function responseJson(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj))
    .setMimeType(ContentService.MimeType.JSON);
}

// 브라우저에서 직접 접속 테스트용 GET 핸들러
function doGet(e) {
  return ContentService.createTextOutput("MobiMate Google Sheets Webhook is Active.");
}
