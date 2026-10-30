using Ozakboy.TradeKit.Shioaji.Auth;

namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 一個流量日的累計(<see cref="ShioajiTrafficMeter"/> 的歷史桶)。
/// The tally of one traffic day, a history bucket of <see cref="ShioajiTrafficMeter"/>.
/// </summary>
public sealed record ShioajiTrafficBucket
{
    /// <summary>
    /// 這個流量日的日期標籤:該日台北 08:00 重置之後開始的那一桶,到次日台北 08:00 結束。
    /// The date label of the traffic day: the bucket that begins at that date's 08:00 Taipei reset and ends at
    /// the next day's 08:00.
    /// </summary>
    public required DateOnly Date { get; init; }

    /// <summary>
    /// 這一桶累計的回應位元組。
    /// The response bytes accumulated in this bucket.
    /// </summary>
    public required long Bytes { get; init; }

    /// <summary>
    /// 這一桶累計的回應數。
    /// The number of responses accumulated in this bucket.
    /// </summary>
    public required long Requests { get; init; }
}

/// <summary>
/// <see cref="ShioajiTrafficMeter.Snapshot"/> 的結果:今天這一桶的本地累計、server 最近一次回報的用量,以及前幾天的歷史桶。
/// What <see cref="ShioajiTrafficMeter.Snapshot"/> returns: today's local tally, the usage the server last reported,
/// and the buckets of previous days.
/// </summary>
public sealed record ShioajiTrafficSnapshot
{
    /// <summary>
    /// 今天這一桶的日期標籤,見 <see cref="ShioajiTrafficBucket.Date"/>。
    /// The date label of today's bucket; see <see cref="ShioajiTrafficBucket.Date"/>.
    /// </summary>
    public required DateOnly BucketDate { get; init; }

    /// <summary>
    /// 今天這一桶本地累計的回應位元組。
    /// The response bytes this process has accumulated in today's bucket.
    /// </summary>
    public required long Bytes { get; init; }

    /// <summary>
    /// 今天這一桶本地累計的回應數。
    /// The number of responses accumulated in today's bucket.
    /// </summary>
    public required long Requests { get; init; }

    /// <summary>
    /// <c>GET /api/v1/auth/usage</c> 回報的今日已用位元組;今天還沒查過時為 <see langword="null"/>。這個數字才是券商的帳,
    /// <see cref="Bytes"/> 只是本程序看到的回應本文大小。
    /// The bytes used today as <c>GET /api/v1/auth/usage</c> reported them, or <see langword="null"/> when it has
    /// not been asked today. This is the broker's own count; <see cref="Bytes"/> is only the size of the response
    /// bodies this process saw.
    /// </summary>
    public required long? ServerReportedBytes { get; init; }

    /// <summary>
    /// server 回報的今日位元組上限;沒查過時為 <see langword="null"/>。
    /// The byte ceiling the server reported for today, or <see langword="null"/> when not asked.
    /// </summary>
    public required long? ServerReportedLimitBytes { get; init; }

    /// <summary>
    /// server 回報的今日剩餘位元組;沒查過時為 <see langword="null"/>。
    /// The bytes the server reported as remaining today, or <see langword="null"/> when not asked.
    /// </summary>
    public required long? ServerReportedRemainingBytes { get; init; }

    /// <summary>
    /// server 回報用量的時刻;沒查過時為 <see langword="null"/>。
    /// When the server reported the usage, or <see langword="null"/> when not asked.
    /// </summary>
    public required DateTimeOffset? ServerReportedAt { get; init; }

    /// <summary>
    /// 估計的今日剩餘位元組:server 回報的剩餘量,減去回報之後本地又累計的位元組;沒查過時為 <see langword="null"/>。
    /// 可能是負數(估計用完了)。
    /// The estimated bytes remaining today: the remainder the server reported minus what was accumulated locally
    /// since; <see langword="null"/> when not asked. It can be negative, meaning the estimate says the allowance is
    /// spent.
    /// </summary>
    public required long? EstimatedRemainingBytes { get; init; }

    /// <summary>
    /// 下一次重置的時刻(UTC)。台北 08:00 剛好是 UTC 00:00。
    /// The next reset, in UTC. 08:00 Taipei is exactly 00:00 UTC.
    /// </summary>
    public required DateTimeOffset NextResetAt { get; init; }

    /// <summary>
    /// 前幾個流量日的歷史桶,最近的在前,最多 7 筆;沒有任何累計的日子不留桶。
    /// The buckets of previous traffic days, newest first, at most seven; a day with nothing accumulated leaves no
    /// bucket.
    /// </summary>
    public required IReadOnlyList<ShioajiTrafficBucket> History { get; init; }
}

/// <summary>
/// 流量計:累計每個 HTTP 回應的位元組,按台北流量日分桶(每日 08:00 重置)。
/// The traffic meter: accumulates the bytes of every HTTP response, bucketed by Taipei traffic day (reset daily at
/// 08:00).
/// </summary>
/// <remarks>
/// <para>
/// <b>為什麼要有它。</b>券商每日流量依近 30 日成交金額分級(500 MB / 2 GB / 10 GB),08:00 重置;<b>超量時行情查詢不報錯、
/// 只回空值</b>。流量是下單之外最容易被默默耗盡的資源,所以每個經過 <see cref="ShioajiApiClient"/> 的回應都記進來,
/// 呼叫端隨時能問「今天這個程序大概用了多少」。
/// <b>Why it exists.</b> The broker's daily traffic allowance is tiered by the last 30 days' turnover (500 MB /
/// 2 GB / 10 GB) and resets at 08:00; <b>once it is exceeded market queries do not fail, they answer with empty
/// values</b>. Traffic is the resource most easily drained silently apart from orders, so every response that goes
/// through <see cref="ShioajiApiClient"/> is recorded, and the caller can ask at any time how much this process
/// roughly used today.
/// </para>
/// <para>
/// <b>這是估計,不是券商的帳。</b>計入的是回應本文的大小(回應的 <c>Content-Length</c>,沒有時用實際讀到的 UTF-8 位元組數),
/// 不含請求本文、標頭,也不含 SSE 串流;同一個帳號若有別的程式在用,它們的流量這裡也看不到。券商實際的帳以
/// <see cref="ShioajiAuthClient.GetUsageAsync"/> 回報的為準,每次查到都會寫進 <see cref="ServerReportedBytes"/>。
/// <b>This is an estimate, not the broker's ledger.</b> What is counted is the size of the response body (the
/// response's <c>Content-Length</c>, or the UTF-8 bytes actually read when there is none); request bodies, headers
/// and the SSE stream are not, and traffic from other programs on the same account is invisible here. The broker's
/// real count is what <see cref="ShioajiAuthClient.GetUsageAsync"/> reports, and every answer is written into
/// <see cref="ServerReportedBytes"/>.
/// </para>
/// <para>
/// <b>流量日。</b>每日台北 08:00 重置,也就是 UTC 00:00。時間來源是注入的 <see cref="TimeProvider"/>,測試可以用假時鐘
/// 走過重置的那一刻。實例是單例(<see cref="ShioajiServiceCollectionExtensions.AddShioaji"/> 註冊成單例),執行緒安全。
/// <b>The traffic day.</b> The allowance resets daily at 08:00 Taipei, which is 00:00 UTC. The time source is an
/// injected <see cref="TimeProvider"/>, so a test can step over the reset on a fake clock. The instance is a
/// singleton (registered so by <see cref="ShioajiServiceCollectionExtensions.AddShioaji"/>) and thread-safe.
/// </para>
/// </remarks>
public sealed class ShioajiTrafficMeter
{
    private const int MaxHistoryBuckets = 7;

    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly List<ShioajiTrafficBucket> _history = [];
    private DateOnly _day;
    private long _bytes;
    private long _requests;
    private ServerReport? _report;

    /// <summary>
    /// 建立流量計。
    /// Creates the meter.
    /// </summary>
    /// <param name="timeProvider">時間來源;<see langword="null"/> 時用系統時間。The time source, or system time when <see langword="null"/>.</param>
    public ShioajiTrafficMeter(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _day = DayOf(_timeProvider.GetUtcNow());
    }

    /// <summary>
    /// 今天這一桶本地累計的回應位元組。
    /// The response bytes accumulated locally in today's bucket.
    /// </summary>
    public long TodayBytes
    {
        get
        {
            lock (_gate)
            {
                Roll();
                return _bytes;
            }
        }
    }

    /// <summary>
    /// 今天這一桶累計的回應數。
    /// The number of responses accumulated in today's bucket.
    /// </summary>
    public long TodayRequests
    {
        get
        {
            lock (_gate)
            {
                Roll();
                return _requests;
            }
        }
    }

    /// <summary>
    /// server 今天最近一次回報的已用位元組(<c>GET /api/v1/auth/usage</c> 的 <c>bytes</c>);今天還沒查過時為
    /// <see langword="null"/>。跨過 08:00 重置之後上一個流量日的回報不會留下來。
    /// The used bytes the server last reported today (the <c>bytes</c> of <c>GET /api/v1/auth/usage</c>), or
    /// <see langword="null"/> when it has not been asked today. A report from the previous traffic day does not
    /// survive the 08:00 reset.
    /// </summary>
    public long? ServerReportedBytes
    {
        get
        {
            lock (_gate)
            {
                Roll();
                return _report?.Bytes;
            }
        }
    }

    /// <summary>
    /// 記下一個回應的位元組。負數與零視為沒有流量,只計入回應數。
    /// Records the bytes of one response. Zero and negative values count as no traffic and only add to the response
    /// count.
    /// </summary>
    /// <param name="bytes">回應的位元組數。The number of bytes of the response.</param>
    public void Record(long bytes)
    {
        lock (_gate)
        {
            Roll();
            _requests++;

            if (bytes > 0)
            {
                _bytes += bytes;
            }
        }
    }

    /// <summary>
    /// 清掉今天這一桶(本地累計與 server 回報值)。歷史桶不動。
    /// Clears today's bucket, the local tally and the server's report together. The history buckets are left alone.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            Roll();
            _bytes = 0;
            _requests = 0;
            _report = null;
        }
    }

    /// <summary>
    /// 取得目前的快照。
    /// Takes a snapshot of the current state.
    /// </summary>
    /// <returns>今天的累計、server 回報值與歷史桶。Today's tally, the server's report and the history buckets.</returns>
    public ShioajiTrafficSnapshot Snapshot()
    {
        lock (_gate)
        {
            Roll();

            var report = _report;

            return new ShioajiTrafficSnapshot
            {
                BucketDate = _day,
                Bytes = _bytes,
                Requests = _requests,
                ServerReportedBytes = report?.Bytes,
                ServerReportedLimitBytes = report?.LimitBytes,
                ServerReportedRemainingBytes = report?.RemainingBytes,
                ServerReportedAt = report?.At,
                EstimatedRemainingBytes = EstimateRemaining(),
                NextResetAt = ResetInstantAfter(_day),
                History = [.. _history],
            };
        }
    }

    /// <summary>
    /// 把 <c>GET /api/v1/auth/usage</c> 的回報記下來,供 <see cref="ShioajiTrafficSnapshot.EstimatedRemainingBytes"/> 與
    /// <see cref="IsPossiblyExhausted"/> 使用。
    /// Records what <c>GET /api/v1/auth/usage</c> reported, for
    /// <see cref="ShioajiTrafficSnapshot.EstimatedRemainingBytes"/> and <see cref="IsPossiblyExhausted"/>.
    /// </summary>
    internal void ReportServerUsage(ShioajiUsage usage)
    {
        lock (_gate)
        {
            Roll();
            _report = new ServerReport(
                usage.Bytes,
                usage.LimitBytes,
                usage.RemainingBytes,
                _timeProvider.GetUtcNow(),
                _bytes);
        }
    }

    /// <summary>
    /// 一個歷史資料查詢回了空值時,「是不是流量用完了」的最佳猜測:今天沒有 server 回報就無從排除,回 <see langword="true"/>;
    /// 有回報時看估計的剩餘量是否已經不大於 0。
    /// The best guess, when a historical query came back empty, at whether the allowance is spent: with no server
    /// report today nothing can be ruled out and the answer is <see langword="true"/>; with one, whether the
    /// estimated remainder is no longer above zero.
    /// </summary>
    internal bool IsPossiblyExhausted()
    {
        lock (_gate)
        {
            Roll();

            return EstimateRemaining() is not { } remaining || remaining <= 0;
        }
    }

    private long? EstimateRemaining() =>
        _report is { } report ? report.RemainingBytes - Math.Max(0L, _bytes - report.LocalBytesAtReport) : null;

    /// <summary>
    /// 跨過重置時刻就把上一桶放進歷史、開新的一桶。呼叫端須已持有鎖。
    /// Moves the previous bucket into the history and starts a new one once the reset instant has passed. The
    /// caller must hold the lock.
    /// </summary>
    private void Roll()
    {
        var today = DayOf(_timeProvider.GetUtcNow());

        // 時鐘倒退(NTP 校時)不回滾桶,免得同一天被拆成兩桶。
        // A clock stepping backwards (an NTP correction) does not roll the bucket back, so one day is never split
        // into two.
        if (today <= _day)
        {
            return;
        }

        if (_bytes > 0 || _requests > 0)
        {
            _history.Insert(0, new ShioajiTrafficBucket { Date = _day, Bytes = _bytes, Requests = _requests });

            if (_history.Count > MaxHistoryBuckets)
            {
                _history.RemoveRange(MaxHistoryBuckets, _history.Count - MaxHistoryBuckets);
            }
        }

        _day = today;
        _bytes = 0;
        _requests = 0;
        _report = null;
    }

    /// <summary>
    /// 一個時刻屬於哪個流量日:台北時間減掉 8 小時(重置時刻)之後的日期。
    /// Which traffic day an instant belongs to: the date of Taipei time minus the 8 hours of the reset hour.
    /// </summary>
    private static DateOnly DayOf(DateTimeOffset instant)
    {
        var taipei = instant.ToOffset(ShioajiConstants.TaipeiUtcOffset);

        return DateOnly.FromDateTime((taipei - TimeSpan.FromHours(ShioajiConstants.TrafficResetHour)).DateTime);
    }

    private static DateTimeOffset ResetInstantAfter(DateOnly day) =>
        new DateTimeOffset(
                day.AddDays(1).ToDateTime(new TimeOnly(ShioajiConstants.TrafficResetHour, 0)),
                ShioajiConstants.TaipeiUtcOffset)
            .ToUniversalTime();

    /// <summary>
    /// server 的一次用量回報,連同回報當下本地的累計量(用來算「回報之後又用了多少」)。
    /// One usage report from the server, with the local tally at the moment it arrived (to work out how much was
    /// used since).
    /// </summary>
    private sealed record ServerReport(
        long Bytes,
        long LimitBytes,
        long RemainingBytes,
        DateTimeOffset At,
        long LocalBytesAtReport);
}
