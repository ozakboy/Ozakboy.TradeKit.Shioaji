namespace Ozakboy.TradeKit.Shioaji.Contracts;

/// <summary>
/// 商品類別,對應 server 的 <c>security_type</c>。
/// The security type, matching the server's <c>security_type</c>.
/// </summary>
public enum ShioajiSecurityType
{
    /// <summary>
    /// 讀不懂或未知的值。
    /// An unreadable or unknown value.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 股票(<c>STK</c>)。
    /// A stock (<c>STK</c>).
    /// </summary>
    Stock = 1,

    /// <summary>
    /// 期貨(<c>FUT</c>)。
    /// A future (<c>FUT</c>).
    /// </summary>
    Future = 2,

    /// <summary>
    /// 選擇權(<c>OPT</c>)。
    /// An option (<c>OPT</c>).
    /// </summary>
    Option = 3,

    /// <summary>
    /// 指數(<c>IND</c>)。
    /// An index (<c>IND</c>).
    /// </summary>
    Index = 4,

    /// <summary>
    /// 權證(<c>WRT</c>)。
    /// A warrant (<c>WRT</c>).
    /// </summary>
    Warrant = 5,
}

/// <summary>
/// 股票的現股當沖資格,對應商品詳細資訊的 <c>day_trade</c>。
/// A stock's day-trading eligibility, matching <c>day_trade</c> in the contract info.
/// </summary>
public enum ShioajiDayTradeEligibility
{
    /// <summary>
    /// 沒有這個欄位(非股票)或讀不懂。<b>不可當成「可以當沖」</b>。
    /// The field is absent (not a stock) or unreadable. <b>Never read it as "may day-trade".</b>
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 可當沖(<c>Yes</c>)。
    /// May day-trade (<c>Yes</c>).
    /// </summary>
    Yes = 1,

    /// <summary>
    /// 只能先買後賣的當沖(<c>OnlyBuy</c>)。
    /// Day-trading is allowed only buy-first (<c>OnlyBuy</c>).
    /// </summary>
    OnlyBuy = 2,

    /// <summary>
    /// 不可當沖(<c>No</c>)。
    /// May not day-trade (<c>No</c>).
    /// </summary>
    No = 3,
}
