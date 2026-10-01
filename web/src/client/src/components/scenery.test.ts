import { erinnDateText, parseErinn, skyProgress, weatherKind } from './Scenery';

// 게임은 에린 월을 0부터 센다("2961-0-18"). 사람이 보기에 0월은 말이 안 되므로 +1 해서 보여 준다.
describe('에린 날짜·시각', () => {
  it('월은 +1 해서 "N월 D일"로 보여 준다 (0월이 나오지 않는다)', () => {
    const t = parseErinn('에린 시간 2961-0-18 21:14 🌙 (밤)')!;
    expect(t).toMatchObject({ year: 2961, month: 0, dayOfMonth: 18, hh: 21, mm: 14, day: false });
    expect(erinnDateText(t)).toBe('1월 18일');
    expect(erinnDateText(parseErinn('2959-4-23 15:51')!)).toBe('5월 23일');
    expect(erinnDateText(parseErinn('2959-11-1 06:00')!)).toBe('12월 1일');
  });

  it('낮은 6시부터 18시 전까지', () => {
    expect(parseErinn('2959-4-23 05:59')!.day).toBe(false);
    expect(parseErinn('2959-4-23 06:00')!.day).toBe(true);
    expect(parseErinn('2959-4-23 17:59')!.day).toBe(true);
    expect(parseErinn('2959-4-23 18:00')!.day).toBe(false);
  });

  it('해석하지 못하면 null', () => {
    expect(parseErinn('')).toBeNull();
    expect(parseErinn(null)).toBeNull();
    expect(parseErinn('에린 시간')).toBeNull();
  });

  it('해·달은 하늘 호를 왼쪽에서 오른쪽으로 지난다', () => {
    expect(skyProgress(parseErinn('1-1-1 06:00')!)).toBeCloseTo(0);
    expect(skyProgress(parseErinn('1-1-1 12:00')!)).toBeCloseTo(0.5);
    expect(skyProgress(parseErinn('1-1-1 18:00')!)).toBeCloseTo(0);    // 밤이 시작: 달이 처음 위치로
    expect(skyProgress(parseErinn('1-1-1 02:00')!)).toBeCloseTo(8 / 12);
  });

  it('날씨 원문을 그림 종류로 바꾼다', () => {
    expect(weatherKind('Sunny')).toBe('sunny');
    expect(weatherKind('맑음')).toBe('sunny');
    expect(weatherKind('Rain')).toBe('rain');
    expect(weatherKind('Snow')).toBe('snow');
    expect(weatherKind('Storm')).toBe('storm');
    expect(weatherKind('Fog')).toBe('fog');
    expect(weatherKind('알 수 없음')).toBe('cloudy');
    expect(weatherKind(null)).toBeNull();
  });
});
