namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 歷史資料、查詢與帳務端點的路徑常數。
/// Path constants of the historical-data, query and portfolio endpoints.
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

    /// <summary>
    /// 庫存(未實現損益,<c>POST</c>,證券與期貨共用,以 <c>account_type</c> 區分)。
    /// Positions with unrealized profit and loss (<c>POST</c>, shared by securities and futures, told apart by
    /// <c>account_type</c>).
    /// </summary>
    public const string PortfolioPositionUnit = "/api/v1/portfolio/position_unit";

    /// <summary>
    /// 庫存明細(<c>POST</c>,body 帶 <c>detail_id</c>)。
    /// Position details (<c>POST</c>, the body carries <c>detail_id</c>).
    /// </summary>
    public const string PortfolioPositionDetail = "/api/v1/portfolio/position_detail";

    /// <summary>
    /// 已實現損益(<c>POST</c>,body 帶 <c>begin_date</c> 與 <c>end_date</c>)。
    /// Realized profit and loss (<c>POST</c>, the body carries <c>begin_date</c> and <c>end_date</c>).
    /// </summary>
    public const string PortfolioProfitLoss = "/api/v1/portfolio/profit_loss";

    /// <summary>
    /// 已實現損益明細(<c>POST</c>,body 帶 <c>detail_id</c>)。
    /// Realized profit and loss details (<c>POST</c>, the body carries <c>detail_id</c>).
    /// </summary>
    public const string PortfolioProfitLossDetail = "/api/v1/portfolio/profit_loss_detail";

    /// <summary>
    /// 已實現損益彙總(<c>POST</c>)。
    /// Realized profit and loss summary (<c>POST</c>).
    /// </summary>
    public const string PortfolioProfitLossSummary = "/api/v1/portfolio/profitloss_sum";

    /// <summary>
    /// 交割款(<c>POST</c>,證券帳號)。
    /// Settlements (<c>POST</c>, securities account).
    /// </summary>
    public const string PortfolioSettlements = "/api/v1/portfolio/settlements";

    /// <summary>
    /// 交易額度(<c>POST</c>,證券帳號,交易日 08:30 至 15:00 可查)。
    /// Trading limits (<c>POST</c>, securities account, queryable 08:30 to 15:00 on trading days).
    /// </summary>
    public const string PortfolioTradingLimits = "/api/v1/portfolio/trading_limits";

    /// <summary>
    /// 期貨保證金(<c>POST</c>,期貨帳號)。
    /// Futures margin (<c>POST</c>, futures account).
    /// </summary>
    public const string PortfolioMargin = "/api/v1/portfolio/margin";

    /// <summary>
    /// 銀行餘額(<c>POST</c>,證券帳號)。
    /// Bank balance (<c>POST</c>, securities account).
    /// </summary>
    public const string PortfolioAccountBalance = "/api/v1/portfolio/account_balance";
}
