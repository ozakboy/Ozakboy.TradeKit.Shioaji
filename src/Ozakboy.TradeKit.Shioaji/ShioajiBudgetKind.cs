namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 本地限流預算的類別,各自對應券商公告的一條上限。
/// The categories of the local rate budget, each matching one of the broker's published ceilings.
/// </summary>
public enum ShioajiBudgetKind
{
    /// <summary>
    /// 行情查詢:歷史 K 線、逐筆、快照、券源、資券餘額合計 10 秒 50 次。
    /// Market queries: historical bars, ticks, snapshots, short-stock sources and credit enquiries together,
    /// 50 per 10 seconds.
    /// </summary>
    MarketQuery = 0,

    /// <summary>
    /// 帳務查詢:5 秒 25 次。
    /// Account queries: 25 per 5 seconds.
    /// </summary>
    Portfolio = 1,

    /// <summary>
    /// 委託操作:下單、改價、改量、撤單,10 秒 250 次。
    /// Order operations: placing, repricing, resizing and cancelling, 250 per 10 seconds.
    /// </summary>
    Order = 2,

    /// <summary>
    /// 行情訂閱:不是時間視窗而是同時數量,上限 200。取得 +1,<see cref="ShioajiRateBudget.ReleaseSubscription"/> −1。
    /// Market-data subscriptions: a concurrent count rather than a time window, capped at 200. Acquiring adds
    /// one and <see cref="ShioajiRateBudget.ReleaseSubscription"/> removes one.
    /// </summary>
    Subscription = 3,
}
