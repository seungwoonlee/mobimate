import { type FullConfig } from '@playwright/test';
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { createConnection } from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));   // ESM에는 __dirname이 없다

/** 이 포트에서 누가 듣고 있는가 */
const listening = (port: number) => new Promise<boolean>(res => {
  const s = createConnection({ host: '127.0.0.1', port });
  s.once('connect', () => { s.destroy(); res(true); });
  s.once('error', () => res(false));
});

/**
 * E2E용 서버: 빌드된 MobiMateWeb.dll을 가짜 CLI·임시 저장 폴더로 띄운다. 인증이 없어(요구사양 Q8) 로그인 단계는 없다.
 */
export default async function globalSetup(config: FullConfig) {
  const web = resolve(here, '../../..');   // web/
  const dll = join(web, 'src/MobiMate.Web/bin/Debug/net8.0-windows/MobiMateWeb.dll');
  const cli = join(web, 'tools/FakeCli/bin/Debug/net8.0/MabinogiMobile_CLI.exe');
  if (!existsSync(dll) || !existsSync(cli)) throw new Error('먼저 dotnet build web/MobiMate.Web.sln 을 실행하세요.');

  const baseURL = config.projects[0].use.baseURL!;
  const port = new URL(baseURL).port;
  // 포트가 이미 쓰이면 서버가 다른 포트로 떠 테스트가 엉뚱한 서버를 보게 된다: 먼저 알린다
  if (await listening(Number(port))) throw new Error(`포트 ${port}를 다른 프로그램이 쓰고 있습니다. 이전 E2E 서버를 끄거나 E2E_PORT를 바꾸세요.`);
  const storage = mkdtempSync(join(tmpdir(), 'mm-e2e-'));
  seedCharacters(storage);
  seedClassImage(storage);

  const server = spawn('dotnet', [dll], {
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      MobiMate__StorageDir: storage,
      MobiMate__WpfStorageDir: '',
      MobiMate__OpenBrowser: 'false',
      MobiMate__Tray: 'false',
      MobiMate__SingleInstance: 'false',
      MobiMate__Mdns: 'false',
      MobiMate__ClassImageDownload: 'false',   // 네트워크로 내려받지 않는다
      MobiMate__RecordInterval: '00:00:00',   // 자동 기록은 끈다 (화면이 읽을 때만 기록)
      MobiMate__Port: port,
      MobiMate__CliPath: cli,
    },
    stdio: 'ignore',
  });
  writeFileSync(join(storage, 'server.pid'), String(server.pid));
  process.env.MM_E2E_STORAGE = storage;
  process.env.MM_E2E_PID = String(server.pid);

  // 서버가 응답할 때까지 기다린다 (최대 20초)
  for (let i = 0; i < 100; i++) {
    try { if ((await fetch(`${baseURL}/api/ping`)).ok) return; } catch { /* 아직 안 떴다 */ }
    await new Promise(r => setTimeout(r, 200));
  }
  server.kill();
  throw new Error('서버가 뜨지 않았습니다.');
}

/**
 * 가짜 CLI의 현재 캐릭터(아이라_격투가: 데카 10,285 · M캐시 5,619 · 은동전 120 · 마족 공물 9)와 함께 보일 다른 캐릭터들:
 * 도적 = 같은 계정(예전에 같은 데카·M캐시), 3일 전 접속 → 충전이 가득 차 빨강
 * 마법사 = 같은 계정, 값이 어긋남(동기화 전), 2시간 전 접속, 충전은 여유 → 진회색
 * 사제 = 다른 계정, 은동전이 80%를 넘어 노랑
 */
function seedCharacters(dir: string) {
  // 서버는 기록 시각을 PC의 현지 시각(표준시 표기 없이)으로 저장한다: 같은 방식으로 쓴다
  const ago = (ms: number) => new Date(Date.now() - ms).toLocaleString('sv-SE').replace(' ', 'T');
  const H = 3_600_000, D = 24 * H;
  const rec = (at: number, level: number, combat: number, deca: number, mcash: number, silver: number, tribute: number) =>
    ({ Timestamp: ago(at), Level: level, Title: '', CombatScore: combat, ArcaneResistance: 3000, LivingScore: 1000, AttractivenessScore: 5000, Gold: 500000, Deca: deca, MCash: mcash, SilverCoin: silver, DemonTribute: tribute });
  const prof = (realm: string, job: string, lastSeen: number, history: object[]) =>
    ({ CharacterKey: `${realm}_${job}`, RealmName: realm, JobName: job, CustomName: '', FirstSeen: ago(30 * D), LastSeen: ago(lastSeen), History: history });
  const db = [
    prof('바람', '도적', 3 * D, [rec(5 * D, 100, 99000, 10285, 5619, 90, 3), rec(3 * D, 100, 99000, 10000, 5619, 100, 4)]),
    prof('바람', '마법사', 2 * H, [rec(D, 100, 80000, 10285, 5619, 5, 1), rec(2 * H, 100, 80000, 10000, 5619, 10, 1)]),
    prof('바람', '사제', 1 * H, [rec(1 * H, 95, 60000, 777, 888, 82, 1)]),
  ];
  writeFileSync(join(dir, 'character_history_db.json'), JSON.stringify(db));
}

/** 직업 이미지가 이미 내려받아져 있는 상태(격투가 = thief_3)를 만든다: 1x1 PNG */
function seedClassImage(dir: string) {
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==', 'base64');
  mkdirSync(join(dir, 'class-images'), { recursive: true });
  writeFileSync(join(dir, 'class-images', 'thief_3.png'), png);
}
