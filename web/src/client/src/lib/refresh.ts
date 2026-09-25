/**
 * 적응형 갱신 주기 (FR-RF-01). Core AdaptiveRefreshController와 같은 규칙이다:
 * 기본 15초, 활동이 없으면 틱마다 +15초(최대값까지), 활동이 있으면 다음 틱에 15초로 돌아간다.
 */
export class AdaptiveRefresh {
  static readonly Base = 15;
  static readonly Step = 15;

  current = AdaptiveRefresh.Base;
  private active = false;

  constructor(private max = 300) {}

  setMax(max: number) {
    this.max = Math.max(AdaptiveRefresh.Base, max);
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
