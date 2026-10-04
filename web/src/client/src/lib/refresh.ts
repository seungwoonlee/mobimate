/**
 * 적응형 갱신 주기 (FR-RF-01). Core AdaptiveRefreshController와 같은 규칙이다:
 * 기본 15초, 활동이 없으면 틱마다 +5초(최대 30초까지), 활동이 있으면 다음 틱에 15초로 돌아간다.
 */
export class AdaptiveRefresh {
  static readonly Base = 15;
  static readonly Step = 5;
  /** 서버 설정과 무관한 절대 상한: 아무리 길어도 이 간격마다 한 번은 갱신한다 */
  static readonly Cap = 30;

  current = AdaptiveRefresh.Base;
  private active = false;

  constructor(private max = AdaptiveRefresh.Cap) { this.max = Math.min(AdaptiveRefresh.Cap, Math.max(AdaptiveRefresh.Base, max)); }

  setMax(max: number) {
    this.max = Math.min(AdaptiveRefresh.Cap, Math.max(AdaptiveRefresh.Base, max));
    this.current = Math.min(this.current, this.max);
  }

  recordActivity() {
    this.active = true;
    this.current = AdaptiveRefresh.Base;
  }

  onTick(): number {
    if (this.active) {
      this.current = AdaptiveRefresh.Base;
      this.active = false;
    } else {
      this.current = Math.min(this.max, this.current + AdaptiveRefresh.Step);
    }
    return this.current;
  }

  reset() {
    this.current = AdaptiveRefresh.Base;
    this.active = false;
  }
}
