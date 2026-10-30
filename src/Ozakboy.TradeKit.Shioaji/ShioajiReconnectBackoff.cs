namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 串流重連的退避設定:指數成長、有上限、含抖動。
/// The reconnection backoff of the stream: exponential, capped, and jittered.
/// </summary>
/// <remarks>
/// 抖動的目的是打散多個用戶端同時斷線後同時重連的時機;本套件通常只有一個用戶端,但 server 重啟時
/// 同一台機器上的其他行程也會一起重連,抖動仍然有意義。
/// Jitter spreads out the reconnection of several clients that dropped together. This package usually runs one
/// client, but other processes on the machine reconnect when the server restarts, so it still matters.
/// </remarks>
public sealed class ShioajiReconnectBackoff
{
    /// <summary>
    /// 第一次重連前等待的時間,預設 1 秒。
    /// The wait before the first reconnection attempt; 1 second by default.
    /// </summary>
    public TimeSpan Initial { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 單次等待的上限,預設 30 秒。
    /// The ceiling of a single wait; 30 seconds by default.
    /// </summary>
    public TimeSpan Max { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 抖動比例(0 到 1),預設 0.2:實際等待落在計算值的 ±20% 之內。
    /// The jitter ratio, 0 to 1; 0.2 by default, so the actual wait falls within ±20% of the computed value.
    /// </summary>
    public double JitterRatio { get; set; } = 0.2d;

    /// <summary>
    /// 檢查設定是否可用。
    /// Checks that the settings are usable.
    /// </summary>
    /// <returns>可用時為成功。Success when usable.</returns>
    public Result Validate()
    {
        if (Initial <= TimeSpan.Zero)
        {
            return ShioajiErrors.InvalidOptions("重連退避的初始等待必須為正值。The initial reconnect backoff must be positive.");
        }

        if (Max < Initial)
        {
            return ShioajiErrors.InvalidOptions("重連退避的上限不可小於初始等待。The reconnect backoff ceiling must not be below the initial wait.");
        }

        if (double.IsNaN(JitterRatio) || JitterRatio < 0d || JitterRatio > 1d)
        {
            return ShioajiErrors.InvalidOptions("重連退避的抖動比例必須介於 0 到 1。The reconnect jitter ratio must be between 0 and 1.");
        }

        return Result.Success();
    }

    /// <summary>
    /// 計算第 <paramref name="attempt"/> 次重連前要等多久。
    /// Computes how long to wait before reconnection attempt <paramref name="attempt"/>.
    /// </summary>
    /// <param name="attempt">第幾次重連(從 1 起算)。The reconnection attempt, counted from 1.</param>
    /// <param name="jitterSample">抖動樣本,0 到 1;0.5 代表不偏移。A jitter sample from 0 to 1, where 0.5 means no offset.</param>
    /// <returns>等待時間。The wait.</returns>
    public TimeSpan GetDelay(int attempt, double jitterSample)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(jitterSample, 0d);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(jitterSample, 1d);

        // 指數成長很快就會超出可表示範圍,先在指數上封頂再換算。
        // Exponential growth overflows quickly, so the exponent is capped before converting.
        var exponent = Math.Min(attempt - 1, 30);
        var milliseconds = Initial.TotalMilliseconds * Math.Pow(2d, exponent);
        var ceiling = Max.TotalMilliseconds;

        if (milliseconds > ceiling)
        {
            milliseconds = ceiling;
        }

        milliseconds *= 1d + (((jitterSample * 2d) - 1d) * JitterRatio);

        return TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 0d, ceiling));
    }
}
