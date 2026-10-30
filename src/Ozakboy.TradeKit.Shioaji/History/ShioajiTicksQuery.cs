namespace Ozakboy.TradeKit.Shioaji.History;

/// <summary>
/// 逐筆成交的查詢條件。
/// The conditions of a tick query.
/// </summary>
/// <remarks>
/// <para>
/// 用靜態成員建立最不容易寫錯:<see cref="AllDay"/>、<see cref="Range(TimeOnly, TimeOnly)"/>、<see cref="Last(int)"/>。
/// The static members are the hardest to get wrong: <see cref="AllDay"/>, <see cref="Range(TimeOnly, TimeOnly)"/> and
/// <see cref="Last(int)"/>.
/// </para>
/// <para>
/// 與 <see cref="Type"/> 無關的欄位不會送出(例如 <see cref="ShioajiTicksQueryType.AllDay"/> 帶了 <see cref="LastCount"/>,
/// 後者被忽略)。
/// Fields unrelated to <see cref="Type"/> are not sent; a <see cref="LastCount"/> on an
/// <see cref="ShioajiTicksQueryType.AllDay"/> query, for instance, is ignored.
/// </para>
/// <para>
/// <b>盤中禁止用 <c>ticks</c> 當即時報價。</b>券商明文規定,違規可能停 IP 與 ID 且反覆登入無法解除;
/// 本套件不替你判斷現在是不是盤中,即時行情請訂閱串流。
/// <b>Never use <c>ticks</c> as a live quote during the session.</b> The broker forbids it explicitly, and a breach
/// can suspend the IP and the ID with repeated logins not lifting it; this package does not decide for you whether
/// the market is open, so subscribe to the stream for live data.
/// </para>
/// </remarks>
public sealed record ShioajiTicksQuery
{
    /// <summary>
    /// 查詢方式。
    /// How the ticks are queried.
    /// </summary>
    public required ShioajiTicksQueryType Type { get; init; }

    /// <summary>
    /// 起始時間(台北牆上時間);<see cref="ShioajiTicksQueryType.RangeTime"/> 必填。
    /// The start time, Taipei wall-clock; required for <see cref="ShioajiTicksQueryType.RangeTime"/>.
    /// </summary>
    public TimeOnly? TimeStart { get; init; }

    /// <summary>
    /// 結束時間(台北牆上時間);<see cref="ShioajiTicksQueryType.RangeTime"/> 必填,且必須晚於 <see cref="TimeStart"/>。
    /// The end time, Taipei wall-clock; required for <see cref="ShioajiTicksQueryType.RangeTime"/> and it must be
    /// later than <see cref="TimeStart"/>.
    /// </summary>
    public TimeOnly? TimeEnd { get; init; }

    /// <summary>
    /// 要最後幾筆;<see cref="ShioajiTicksQueryType.LastCount"/> 必填,至少為 1。
    /// How many of the last ticks; required for <see cref="ShioajiTicksQueryType.LastCount"/> and at least 1.
    /// </summary>
    public int? LastCount { get; init; }

    /// <summary>
    /// 整個交易日。
    /// The whole trading day.
    /// </summary>
    public static ShioajiTicksQuery AllDay { get; } = new() { Type = ShioajiTicksQueryType.AllDay };

    /// <summary>
    /// 一段時間。
    /// A time range.
    /// </summary>
    /// <param name="start">起始時間。The start time.</param>
    /// <param name="end">結束時間。The end time.</param>
    /// <returns>查詢條件。The query.</returns>
    public static ShioajiTicksQuery Range(TimeOnly start, TimeOnly end) =>
        new() { Type = ShioajiTicksQueryType.RangeTime, TimeStart = start, TimeEnd = end };

    /// <summary>
    /// 最後 N 筆。
    /// The last N ticks.
    /// </summary>
    /// <param name="count">筆數。The number of ticks.</param>
    /// <returns>查詢條件。The query.</returns>
    public static ShioajiTicksQuery Last(int count) =>
        new() { Type = ShioajiTicksQueryType.LastCount, LastCount = count };
}
