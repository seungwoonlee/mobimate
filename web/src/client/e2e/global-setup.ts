import { type FullConfig } from '@playwright/test';
import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, writeFileSync } from 'node:fs';
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
