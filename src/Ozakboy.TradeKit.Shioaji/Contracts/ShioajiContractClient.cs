using System.Globalization;
using System.Text;

namespace Ozakboy.TradeKit.Shioaji.Contracts;

/// <summary>
/// 商品相關的端點。商品檔由 server 在第一次查該類別時載入,每日更新。
/// The contract endpoints. The server loads the contract file the first time a category is queried and refreshes
/// it daily.
/// </summary>
/// <remarks>
/// <para>
/// <b>連續月合約。</b><c>TXFR1</c>(近月)與 <c>TXFR2</c>(次月)是浮動的別名,訂閱時必須用
/// <see cref="ShioajiContract.TargetCode"/>(例如 <c>TXFF6</c>)。先用 <see cref="GetContractAsync"/> 查到它。
/// <b>Continuous-month contracts.</b> <c>TXFR1</c> (near month) and <c>TXFR2</c> (next month) are floating
/// aliases, and a subscription must use the <see cref="ShioajiContract.TargetCode"/> (such as <c>TXFF6</c>).
/// Look it up first with <see cref="GetContractAsync"/>.
/// </para>
/// <para>
/// 商品查詢是 server 本機的商品檔,不算券商的行情查詢,所以不計入 <see cref="ShioajiBudgetKind.MarketQuery"/>。
/// Contract lookups read the server's local contract file and are not the broker's market queries, so they do
/// not count against <see cref="ShioajiBudgetKind.MarketQuery"/>.
/// </para>
/// </remarks>
public sealed class ShioajiContractClient
{
    private readonly ShioajiApiClient _api;

    /// <summary>
    /// 建立用戶端。
    /// Creates the client.
    /// </summary>
    /// <param name="api">低階用戶端。The low-level client.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="api"/> 為 <see langword="null"/> 時擲出。Thrown when <paramref name="api"/> is <see langword="null"/>.
    /// </exception>
    public ShioajiContractClient(ShioajiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
    }

    /// <summary>
    /// 查一個商品(<c>GET /api/v1/data/contracts/{code}</c>)。
    /// Looks up one contract (<c>GET /api/v1/data/contracts/{code}</c>).
    /// </summary>
    /// <param name="code">商品代碼。The contract code.</param>
    /// <param name="securityType">
    /// 商品類別;不確定時留空,由 server 判斷。傳入 <see cref="ShioajiSecurityType.Unknown"/> 視同留空。
    /// The security type; leave empty when unsure and the server decides. <see cref="ShioajiSecurityType.Unknown"/>
    /// counts as empty.
    /// </param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>商品,或已對映的失敗。The contract, or a mapped failure.</returns>
    public async Task<Result<ShioajiContract>> GetContractAsync(
        string code,
        ShioajiSecurityType? securityType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ShioajiErrors.BlankArgument(nameof(code));
        }

        var path = new StringBuilder("/api/v1/data/contracts/").Append(Uri.EscapeDataString(code.Trim()));

        if (securityType is { } type
            && type != ShioajiSecurityType.Unknown
            && ShioajiWire.SecurityType.TryGetWire(type, out var wire))
        {
            path.Append("?security_type=").Append(Uri.EscapeDataString(wire));
        }

        var body = await _api.GetStringAsync(path.ToString(), cancellationToken).ConfigureAwait(false);

        return body.IsFailure
            ? body.ToFailure<ShioajiContract>()
            : ShioajiContractParser.ParseContract(body.GetValueOrThrow(), "/api/v1/data/contracts/{code}");
    }

    /// <summary>
    /// 查一個商品的詳細資訊(<c>GET /api/v1/data/contracts/{code}/info</c>),股票的 <c>day_trade</c> 在這裡。
    /// Looks up a contract's details (<c>GET /api/v1/data/contracts/{code}/info</c>); a stock's <c>day_trade</c>
    /// is here.
    /// </summary>
    /// <param name="code">商品代碼。The contract code.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>詳細資訊,或已對映的失敗。The details, or a mapped failure.</returns>
    public async Task<Result<ShioajiContractInfo>> GetContractInfoAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ShioajiErrors.BlankArgument(nameof(code));
        }

        var path = string.Concat("/api/v1/data/contracts/", Uri.EscapeDataString(code.Trim()), "/info");
        var body = await _api.GetStringAsync(path, cancellationToken).ConfigureAwait(false);

        return body.IsFailure
            ? body.ToFailure<ShioajiContractInfo>()
            : ShioajiContractParser.ParseInfo(body.GetValueOrThrow(), "/api/v1/data/contracts/{code}/info");
    }

    /// <summary>
    /// 列出某個類別的商品(<c>GET /api/v1/data/contracts</c>)。
    /// Lists the contracts of a category (<c>GET /api/v1/data/contracts</c>).
    /// </summary>
    /// <param name="securityType">商品類別。The security type.</param>
    /// <param name="page">頁碼(從 1 起算);<see langword="null"/> 表示不分頁,server 一次全回。The page, counted from 1; <see langword="null"/> means no paging and the server returns everything.</param>
    /// <param name="pageSize">每頁筆數。The page size.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>一頁商品,或已對映的失敗。A page of contracts, or a mapped failure.</returns>
    public async Task<Result<ShioajiContractPage>> ListContractsAsync(
        ShioajiSecurityType securityType,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        if (!ShioajiWire.SecurityType.TryGetWire(securityType, out var wire))
        {
            return ShioajiErrors.InvalidArgument("必須指定商品類別。A security type must be given.");
        }

        if (page is < 1 || pageSize is < 1)
        {
            return ShioajiErrors.InvalidArgument("page 與 pageSize 必須至少為 1。page and pageSize must be at least 1.");
        }

        var path = new StringBuilder("/api/v1/data/contracts?security_type=").Append(Uri.EscapeDataString(wire));

        if (page is { } pageNumber)
        {
            path.Append("&page=").Append(pageNumber.ToString(CultureInfo.InvariantCulture));
        }

        if (pageSize is { } size)
        {
            path.Append("&page_size=").Append(size.ToString(CultureInfo.InvariantCulture));
        }

        var body = await _api.GetStringAsync(path.ToString(), cancellationToken).ConfigureAwait(false);

        return body.IsFailure
            ? body.ToFailure<ShioajiContractPage>()
            : ShioajiContractParser.ParsePage(body.GetValueOrThrow(), "/api/v1/data/contracts", page, pageSize);
    }

    /// <summary>
    /// 列出期貨商品(<c>GET /api/v1/data/contracts/futures</c>)。
    /// Lists futures contracts (<c>GET /api/v1/data/contracts/futures</c>).
    /// </summary>
    /// <param name="root">商品根代碼(例如 <c>TXF</c>);<see langword="null"/> 表示不限。The root code such as <c>TXF</c>; <see langword="null"/> for any.</param>
    /// <param name="underlyingCode">標的代碼(例如指數 <c>IX0001</c>);<see langword="null"/> 表示不限。The underlying code such as the index <c>IX0001</c>; <see langword="null"/> for any.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>期貨商品清單,或已對映的失敗。The futures contracts, or a mapped failure.</returns>
    public async Task<Result<IReadOnlyList<ShioajiContract>>> ListFuturesAsync(
        string? root = null,
        string? underlyingCode = null,
        CancellationToken cancellationToken = default)
    {
        var path = new StringBuilder("/api/v1/data/contracts/futures");
        var separator = '?';

        if (!string.IsNullOrWhiteSpace(root))
        {
            path.Append(separator).Append("root=").Append(Uri.EscapeDataString(root.Trim()));
            separator = '&';
        }

        if (!string.IsNullOrWhiteSpace(underlyingCode))
        {
            path.Append(separator).Append("underlying_code=").Append(Uri.EscapeDataString(underlyingCode.Trim()));
        }

        var body = await _api.GetStringAsync(path.ToString(), cancellationToken).ConfigureAwait(false);

        return body.IsFailure
            ? body.ToFailure<IReadOnlyList<ShioajiContract>>()
            : ShioajiContractParser.ParseContractList(body.GetValueOrThrow(), "/api/v1/data/contracts/futures");
    }
}
