namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 歷史資料與查詢端點的路徑常數。
/// Path constants of the historical-data and query endpoints.
/// </summary>
public static class ShioajiApiPaths
{
    /// <summary>
    /// 1 分 K(<c>POST</c>)。
    /// One-minute bars (<c>POST</c>).
    /// </summary>
    public const string Kbars = "/api/v1/data/kbars";

    /// <summary>
    /// 逐筆成交(<c>POST</c>)。
    /// Tick-by-tick trades (<c>POST</c>).
    /// </summary>
    public const string Ticks = "/api/v1/data/ticks";

    /// <summary>
    /// 快照(<c>POST</c>)。
    /// Snapshots (<c>POST</c>).
    /// </summary>
    public const string Snapshots = "/api/v1/data/snapshots";

    /// <summary>
    /// 排行掃描(<c>POST</c>)。
    /// Ranking scanner (<c>POST</c>).
    /// </summary>
    public const string Scanner = "/api/v1/data/scanner";

    /// <summary>
    /// 借券券源(<c>POST</c>)。
    /// Short-stock sources (<c>POST</c>).
    /// </summary>
    public const string ShortStockSources = "/api/v1/data/short_stock_sources";

    /// <summary>
    /// 資券餘額(<c>POST</c>)。
    /// Margin and short balances (<c>POST</c>).
    /// </summary>
    public const string CreditEnquire = "/api/v1/data/credit_enquire";

    /// <summary>
    /// 處置股(<c>GET</c>)。
    /// Disposition stocks (<c>GET</c>).
    /// </summary>
    public const string RegulatoryPunish = "/api/v1/data/regulatory_punish";

    /// <summary>
    /// 注意股(<c>GET</c>)。
    /// Attention stocks (<c>GET</c>).
    /// </summary>
    public const string RegulatoryNotice = "/api/v1/data/regulatory_notice";

    /// <summary>
    /// 全市場日 OHLCV(<c>POST</c>,body <c>{date, exclude_warrant}</c>)。
    /// <b>這個路徑未驗證,待實機 OpenAPI 確認。</b>文件只提到命令列的 <c>shioaji data daily-quotes</c>,沒有寫 HTTP 路徑,
    /// 這裡是依其他端點的命名慣例推定的預設值;可用 <see cref="ShioajiOptions.DailyQuotesPath"/> 覆寫。
    /// The all-market daily OHLCV (<c>POST</c>, body <c>{date, exclude_warrant}</c>). <b>This path is unverified and
    /// awaits confirmation against a real server's OpenAPI.</b> The documentation mentions only the command line's
    /// <c>shioaji data daily-quotes</c> and gives no HTTP path; this default is inferred from the naming of the other
    /// endpoints and can be overridden with <see cref="ShioajiOptions.DailyQuotesPath"/>.
    /// </summary>
    public const string DailyQuotes = "/api/v1/data/daily_quotes";
}
