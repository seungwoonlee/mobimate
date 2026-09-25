import { rmSync } from 'node:fs';

/** E2E 서버를 멈추고 임시 저장 폴더를 지운다 (테스트가 만든 것만). */
export default async function globalTeardown() {
  const pid = Number(process.env.MM_E2E_PID);
  if (pid) { try { process.kill(pid); } catch { /* 이미 끝남 */ } }
  const dir = process.env.MM_E2E_STORAGE;
  if (dir && dir.includes('mm-e2e-')) {
    await new Promise(r => setTimeout(r, 500));
    try { rmSync(dir, { recursive: true, force: true }); } catch { /* 잠김: 다음 정리 때 */ }
  }
}
