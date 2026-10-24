namespace MobiMate;

/// <summary>내 위치 (get_current_environment의 WorldPosition). 거리(get_near_pcs의 Distance)와 같은 단위·2차원이다(2026-10-24 실측: 정지한 사람은 오차 0에 가깝게 맞는다).</summary>
public sealed record WorldPos(
    [property: System.Text.Json.Serialization.JsonPropertyName("X")] double X,
    [property: System.Text.Json.Serialization.JsonPropertyName("Y")] double Y);

/// <summary>한 관찰: 시각, 그때 내 위치, 그 사람과의 거리.</summary>
public readonly record struct RangeSample(double T, double Mx, double My, double D);

/// <summary>
/// 주변 사람이 <b>제자리</b>인지 판정한다(삼각측량, 2026-10-24 시범). 거리만으로는 내가 움직이면 정지한 사람의 거리도 변해 알 수 없다.
/// 내 위치와 거리 표본 여러 개를 모아, "그 사람이 한 점에 가만히 있다"는 가정으로 위치를 맞추고 맞지 않는 정도(잔차)를 본다.
/// 정지한 사람은 표본이 한 점으로 잘 맞고(잔차 작음), 움직이는 사람은 안 맞는다(잔차 큼).
/// 내가 거의 안 움직이면(이동 폭이 작으면) 맞춤이 불안정하므로 거리가 그대로인지만 본다.
/// </summary>
public static class Stationary
{
    /// <summary>판정에 필요한 표본 수와 기간(초).</summary>
    public const int MinSamples = 6;
    public const double MinSeconds = 8;
    /// <summary>내 이동 폭(x폭+y폭, m)이 이 값 이상이어야 삼각측량이 안정적이다.</summary>
    public const double MinMySpan = 10;
    /// <summary>잔차(m)가 이 값 미만이면 제자리, 이 값 이상이면 움직임. 사이는 모른다.</summary>
    public const double StillRms = 1.0, MovingRms = 2.0;
    /// <summary>내가 가만히 있을 때: 거리 변화 폭이 이 값(m) 미만이면 제자리, 이상이면 움직임.</summary>
    public const double FixedRange = 0.3;

    /// <returns>true = 제자리, false = 움직임, null = 모름(표본·정보 부족)</returns>
    public static bool? Judge(IReadOnlyList<RangeSample> s)
    {
        if (s.Count < MinSamples || s[^1].T - s[0].T < MinSeconds) return null;
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue, minD = double.MaxValue, maxD = double.MinValue;
        foreach (var p in s)
        {
            minX = Math.Min(minX, p.Mx); maxX = Math.Max(maxX, p.Mx); minY = Math.Min(minY, p.My); maxY = Math.Max(maxY, p.My);
            minD = Math.Min(minD, p.D); maxD = Math.Max(maxD, p.D);
        }
        var mySpan = (maxX - minX) + (maxY - minY);
        if (mySpan < MinMySpan)
        {
            // 내가 제자리: 거리가 그대로면 상대도 제자리, 변하면 움직임(내가 움직인 게 아니므로)
            return maxD - minD < FixedRange ? true : (maxD - minD >= FixedRange * 2 ? false : null);
        }
        var rms = FitRms(s);
        return rms < StillRms ? true : rms >= MovingRms ? false : null;
    }

    /// <summary>한 점에 가만히 있다는 가정으로 맞춘 잔차의 제곱평균제곱근(m). 여러 시작점에서 풀어 가장 좋은 해를 쓴다.</summary>
    public static double FitRms(IReadOnlyList<RangeSample> s)
    {
        var n = s.Count;
        double cx = 0, cy = 0, dm = 0;
        foreach (var p in s) { cx += p.Mx; cy += p.My; dm += p.D; }
        cx /= n; cy /= n; dm /= n;
        var best = double.MaxValue;
        for (var k = 0; k < 8; k++)
        {
            var ang = k * Math.PI / 4;
            double x = cx + dm * Math.Cos(ang), y = cy + dm * Math.Sin(ang), lambda = 1e-3;
            var cost = Cost(s, x, y);
            for (var it = 0; it < 40; it++)
            {
                double a11 = 0, a12 = 0, a22 = 0, b1 = 0, b2 = 0;
                foreach (var p in s)
                {
                    var dx = x - p.Mx; var dy = y - p.My; var len = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
                    var jx = dx / len; var jy = dy / len; var r = len - p.D;
                    a11 += jx * jx; a12 += jx * jy; a22 += jy * jy; b1 += jx * r; b2 += jy * r;
                }
                var m11 = a11 + lambda; var m22 = a22 + lambda; var det = m11 * m22 - a12 * a12;
                if (Math.Abs(det) < 1e-12) break;
                var sx = (b1 * m22 - a12 * b2) / det; var sy = (m11 * b2 - a12 * b1) / det;
                var nx = x - sx; var ny = y - sy; var nc = Cost(s, nx, ny);
                if (nc < cost) { x = nx; y = ny; cost = nc; lambda = Math.Max(1e-9, lambda / 3); if (Math.Abs(sx) + Math.Abs(sy) < 1e-6) break; }
                else lambda *= 4;
            }
            best = Math.Min(best, cost);
        }
        return Math.Sqrt(best / n);
    }

    private static double Cost(IReadOnlyList<RangeSample> s, double x, double y)
    {
        double c = 0;
        foreach (var p in s) { var dx = x - p.Mx; var dy = y - p.My; var r = Math.Sqrt(dx * dx + dy * dy) - p.D; c += r * r; }
        return c;
    }
}
