import { chromium, type FullConfig } from '@playwright/test';
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));   // ESM에는 __dirname이 없다

/**
 * E2E용 서버: 빌드된 MobiMateWeb.dll을 가짜 CLI·임시 저장 폴더로 띄운다.
 * 개발 환경에서만 되는 BootUrlFile로 1회용 기동 URL을 받아 로그인 상태(쿠키)를 저장해 둔다.
 */
export default async function globalSetup(config: FullConfig) {
  const web = resolve(here, '../../..');   // web/
  const dll = join(web, 'src/MobiMate.Web/bin/Debug/net8.0-windows/MobiMateWeb.dll');
  const cli = join(web, 'tools/FakeCli/bin/Debug/net8.0/MabinogiMobile_CLI.exe');
  if (!existsSync(dll) || !existsSync(cli)) throw new Error('먼저 dotnet build web/MobiMate.Web.sln 을 실행하세요.');

  const baseURL = config.projects[0].use.baseURL!;
  const port = new URL(baseURL).port;
  const storage = mkdtempSync(join(tmpdir(), 'mm-e2e-'));
  const bootFile = join(storage, 'boot.txt');

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
      MobiMate__BootUrlFile: bootFile,
      MobiMate__CliPath: cli,
    },
    stdio: 'ignore',
  });
  writeFileSync(join(storage, 'server.pid'), String(server.pid));
  process.env.MM_E2E_STORAGE = storage;
  process.env.MM_E2E_PID = String(server.pid);

  // 서버가 기동 URL을 남길 때까지 기다린다 (최대 20초)
  for (let i = 0; i < 100 && !existsSync(bootFile); i++) await new Promise(r => setTimeout(r, 200));
  if (!existsSync(bootFile)) { server.kill(); throw new Error('서버가 뜨지 않았습니다.'); }

  const bootUrl = readFileSync(bootFile, 'utf8').trim();
  if (new URL(bootUrl).port !== port) {
    server.kill();
    throw new Error(`포트 ${port}를 다른 프로그램이 쓰고 있어 서버가 ${new URL(bootUrl).port}로 떴습니다. 이전 E2E 서버를 끄거나 E2E_PORT를 바꾸세요.`);
  }
  const browser = await chromium.launch({ channel: 'msedge' });
  const page = await browser.newPage();
  await page.goto(bootUrl);
  await page.waitForURL(`${baseURL}/`);
  mkdirSync(join(here, '.auth'), { recursive: true });
  await page.context().storageState({ path: join(here, '.auth/state.json') });
  await browser.close();
}
