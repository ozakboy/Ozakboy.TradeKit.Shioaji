namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 本地限流預算的設定。預設值就是券商公告的上限,調大等於把處置線的風險交給呼叫端。
/// The local rate budget settings. The defaults are the broker's published ceilings; raising them hands the
/// risk of the broker's penalty line to the caller.
/// </summary>
/// <remarks>
/// 券商對超量的處置是停 IP 與 ID,而且反覆登入無法解除,所以預算的方向是「寧可在本地多等」。
/// The broker's penalty for overuse is to suspend the IP and the ID, and logging in again does not lift it, so
/// the budget errs towards waiting locally.
/// </remarks>
public sealed class ShioajiRateBudgetOptions
{
    /// <summary>
    /// 行情查詢視窗內的次數上限,預設 50(10 秒)。
    /// Market-query calls per window; 50 per 10 seconds by default.
    /// </summary>
    public int MarketQueryLimit { get; set; } = 50;

    /// <summary>
    /// 行情查詢的視窗長度,預設 10 秒。
    /// The market-query window; 10 seconds by default.
    /// </summary>
    public TimeSpan MarketQueryWindow { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 帳務查詢視窗內的次數上限,預設 25(5 秒)。
    /// Account-query calls per window; 25 per 5 seconds by default.
    /// </summary>
    public int PortfolioLimit { get; set; } = 25;

    /// <summary>
    /// 帳務查詢的視窗長度,預設 5 秒。
    /// The account-query window; 5 seconds by default.
    /// </summary>
    public TimeSpan PortfolioWindow { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 委託操作視窗內的次數上限,預設 250(10 秒)。
    /// Order operations per window; 250 per 10 seconds by default.
    /// </summary>
    public int OrderLimit { get; set; } = 250;

    /// <summary>
    /// 委託操作的視窗長度,預設 10 秒。
    /// The order-operation window; 10 seconds by default.
    /// </summary>
    public TimeSpan OrderWindow { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 同時訂閱數上限,預設 200。
    /// The ceiling on concurrent subscriptions; 200 by default.
    /// </summary>
    public int SubscriptionLimit { get; set; } = ShioajiConstants.MaxSubscriptions;

    /// <summary>
    /// 等待下一個視窗的時間上限,預設 10 秒。需要等的時間超過它就直接失敗,不會先睡一陣子才失敗。
    /// The longest to wait for the next window; 10 seconds by default. A wait that would exceed it fails at once
    /// rather than sleeping first.
    /// </summary>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 檢查設定是否可用。
    /// Checks that the settings are usable.
    /// </summary>
    /// <returns>可用時為成功。Success when usable.</returns>
    public Result Validate()
    {
        if (MarketQueryLimit < 1 || PortfolioLimit < 1 || OrderLimit < 1 || SubscriptionLimit < 1)
        {
            return ShioajiErrors.InvalidOptions("限流預算的次數上限必須至少為 1。Every rate-budget limit must be at least 1.");
        }

        if (MarketQueryWindow <= TimeSpan.Zero || PortfolioWindow <= TimeSpan.Zero || OrderWindow <= TimeSpan.Zero)
        {
            return ShioajiErrors.InvalidOptions("限流預算的視窗長度必須為正值。Every rate-budget window must be positive.");
        }

        if (MaxWait < TimeSpan.Zero)
        {
            return ShioajiErrors.InvalidOptions("限流預算的最長等待不可為負值。The maximum rate-budget wait must not be negative.");
        }

        return Result.Success();
    }
}
