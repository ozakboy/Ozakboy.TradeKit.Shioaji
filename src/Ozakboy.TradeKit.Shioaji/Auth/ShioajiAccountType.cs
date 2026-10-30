namespace Ozakboy.TradeKit.Shioaji.Auth;

/// <summary>
/// 帳號類型,對應 server 的 <c>account_type</c>(<c>S</c> / <c>F</c> / <c>H</c>)。
/// The account type, matching the server's <c>account_type</c> (<c>S</c>, <c>F</c>, <c>H</c>).
/// </summary>
public enum ShioajiAccountType
{
    /// <summary>
    /// 讀不懂或未知的值。
    /// An unreadable or unknown value.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 證券帳號(<c>S</c>)。
    /// A securities account (<c>S</c>).
    /// </summary>
    Stock = 1,

    /// <summary>
    /// 期貨帳號(<c>F</c>)。
    /// A futures account (<c>F</c>).
    /// </summary>
    Futures = 2,

    /// <summary>
    /// 複委託帳號(<c>H</c>)。server 不支援用 API 對它下單。
    /// An overseas-trading account (<c>H</c>). The server does not support placing orders on it through the API.
    /// </summary>
    Foreign = 3,
}
