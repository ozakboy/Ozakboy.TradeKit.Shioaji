namespace Ozakboy.TradeKit.Shioaji.Contracts;

/// <summary>
/// 歷史資料與快照請求裡指稱一個商品的方式:<c>{security_type, exchange, code}</c>。
/// How a historical-data or snapshot request names a contract: <c>{security_type, exchange, code}</c>.
/// </summary>
/// <remarks>
/// 連續月合約(<c>TXFR1</c>)直接用它的 <see cref="Code"/> 查歷史資料,不需要 <see cref="ShioajiContract.TargetCode"/>;
/// 那是行情「訂閱」才要的。
/// A continuous-month contract (<c>TXFR1</c>) is queried for history by its own <see cref="Code"/>; the
/// <see cref="ShioajiContract.TargetCode"/> is needed only to <i>subscribe</i> to market data.
/// </remarks>
public sealed record ShioajiContractRef
{
    /// <summary>
    /// 商品類別。<see cref="ShioajiSecurityType.Unknown"/> 的請求會在本地被拒絕。
    /// The security type. A request with <see cref="ShioajiSecurityType.Unknown"/> is refused locally.
    /// </summary>
    public required ShioajiSecurityType SecurityType { get; init; }

    /// <summary>
    /// 交易所代號(例如 <c>TSE</c>、<c>OTC</c>、<c>TAIFEX</c>)。
    /// The exchange code, such as <c>TSE</c>, <c>OTC</c> or <c>TAIFEX</c>.
    /// </summary>
    public required string Exchange { get; init; }

    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 由一個 <see cref="ShioajiContract"/> 建立。
    /// Builds a reference from a <see cref="ShioajiContract"/>.
    /// </summary>
    /// <param name="contract">商品。The contract.</param>
    /// <returns>指稱同一個商品的參照。A reference naming the same contract.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="contract"/> 為 <see langword="null"/> 時擲出。Thrown when <paramref name="contract"/> is <see langword="null"/>.
    /// </exception>
    public static ShioajiContractRef From(ShioajiContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return new ShioajiContractRef
        {
            SecurityType = contract.SecurityType,
            Exchange = contract.Exchange,
            Code = contract.Code,
        };
    }
}
