namespace Ozakboy.TradeKit.Shioaji.History;

/// <summary>
/// 逐筆成交的查詢方式(<c>query_type</c>)。
/// How ticks are queried (<c>query_type</c>).
/// </summary>
public enum ShioajiTicksQueryType
{
    /// <summary>
    /// 未指定。查詢會在本地被拒絕。
    /// Unspecified. The query is refused locally.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 整個交易日(<c>AllDay</c>)。
    /// The whole trading day (<c>AllDay</c>).
    /// </summary>
    AllDay = 1,

    /// <summary>
    /// 一段時間(<c>RangeTime</c>),要帶 <see cref="ShioajiTicksQuery.TimeStart"/> 與 <see cref="ShioajiTicksQuery.TimeEnd"/>。
    /// A time range (<c>RangeTime</c>); <see cref="ShioajiTicksQuery.TimeStart"/> and
    /// <see cref="ShioajiTicksQuery.TimeEnd"/> are required.
    /// </summary>
    RangeTime = 2,

    /// <summary>
    /// 最後 N 筆(<c>LastCount</c>),要帶 <see cref="ShioajiTicksQuery.LastCount"/>。
    /// The last N ticks (<c>LastCount</c>); <see cref="ShioajiTicksQuery.LastCount"/> is required.
    /// </summary>
    LastCount = 3,
}

/// <summary>
/// 排行掃描的種類(<c>scanner_type</c>)。
/// The kind of ranking scan (<c>scanner_type</c>).
/// </summary>
public enum ShioajiScannerType
{
    /// <summary>
    /// 未指定。查詢會在本地被拒絕。
    /// Unspecified. The query is refused locally.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 漲跌幅排行(<c>ChangePercentRank</c>)。
    /// Ranking by percentage change (<c>ChangePercentRank</c>).
    /// </summary>
    ChangePercentRank = 1,

    /// <summary>
    /// 漲跌價排行(<c>ChangePriceRank</c>)。
    /// Ranking by price change (<c>ChangePriceRank</c>).
    /// </summary>
    ChangePriceRank = 2,

    /// <summary>
    /// 振幅排行(<c>DayRangeRank</c>)。
    /// Ranking by the day's range (<c>DayRangeRank</c>).
    /// </summary>
    DayRangeRank = 3,

    /// <summary>
    /// 成交量排行(<c>VolumeRank</c>)。
    /// Ranking by volume (<c>VolumeRank</c>).
    /// </summary>
    VolumeRank = 4,

    /// <summary>
    /// 成交金額排行(<c>AmountRank</c>)。
    /// Ranking by turnover (<c>AmountRank</c>).
    /// </summary>
    AmountRank = 5,

    /// <summary>
    /// 成交筆數排行(<c>TickCountRank</c>)。
    /// Ranking by the number of ticks (<c>TickCountRank</c>).
    /// </summary>
    TickCountRank = 6,
}

/// <summary>
/// 監管清單的種類。
/// The kind of regulatory list.
/// </summary>
public enum ShioajiRegulatoryKind
{
    /// <summary>
    /// 未指定(預設值,本套件回傳的項目不會是這個值)。
    /// Unspecified, the default; an entry this package returns is never this value.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 處置股(<c>regulatory_punish</c>)。
    /// Disposition stocks (<c>regulatory_punish</c>).
    /// </summary>
    Punish = 1,

    /// <summary>
    /// 注意股(<c>regulatory_notice</c>)。
    /// Attention stocks (<c>regulatory_notice</c>).
    /// </summary>
    Notice = 2,
}
