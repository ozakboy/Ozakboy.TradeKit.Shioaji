using Ozakboy.TradeKit.Shioaji.Contracts;

namespace Ozakboy.TradeKit.Shioaji.MarketData;

/// <summary>
/// 一筆行情訂閱(<c>POST /api/v1/stream/subscribe</c> 的本文)。
/// One market-data subscription, the body of <c>POST /api/v1/stream/subscribe</c>.
/// </summary>
public sealed record ShioajiSubscription
{
    /// <summary>
    /// 商品類別。
    /// The security type.
    /// </summary>
    public required ShioajiSecurityType SecurityType { get; init; }

    /// <summary>
    /// 交易所代號(<c>TSE</c>、<c>OTC</c>、<c>TAIFEX</c>、指數用 <c>TSE</c> 等)。
    /// The exchange code (<c>TSE</c>, <c>OTC</c>, <c>TAIFEX</c>, <c>TSE</c> for an index, and so on).
    /// </summary>
    public required string Exchange { get; init; }

    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 訂閱種類。指數只支援 <see cref="ShioajiQuoteType.Quote"/>。
    /// The kind of subscription. An index supports only <see cref="ShioajiQuoteType.Quote"/>.
    /// </summary>
    public required ShioajiQuoteType QuoteType { get; init; }

    /// <summary>
    /// 連續月合約(代碼以 R1 或 R2 結尾)對應的實際合約代碼。<b>連續月合約必填</b>,否則 server 回 200 卻只送心跳。
    /// The actual contract code a continuous-month contract (code ending in R1 or R2) maps to. <b>Required for a
    /// continuous-month contract</b>, or the server answers 200 and sends only heartbeats.
    /// </summary>
    public string? TargetCode { get; init; }

    /// <summary>
    /// 是否訂閱盤中零股。
    /// Whether to subscribe to intraday odd lots.
    /// </summary>
    public bool IntradayOdd { get; init; }

    /// <summary>
    /// 識別這筆訂閱的鍵,用於本地追蹤。
    /// The key identifying this subscription for local tracking.
    /// </summary>
    internal string Key =>
        string.Join('|', (int)SecurityType, Exchange.Trim().ToUpperInvariant(), Code.Trim().ToUpperInvariant(), (int)QuoteType, IntradayOdd ? 1 : 0);
}
