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
/// 唯一的例外是 <see cref="ListAllContractsAsync"/>:它一口氣連續翻很多頁,每一頁都先過
/// <see cref="ShioajiBudgetKind.MarketQuery"/> 預算(有給預算時)。
/// The one exception is <see cref="ListAllContractsAsync"/>: it turns many pages in a row, and each page first goes
/// through the <see cref="ShioajiBudgetKind.MarketQuery"/> budget (when one was given).
/// </para>
/// </remarks>
public sealed class ShioajiContractClient
{
    /// <summary>
    /// <see cref="ListAllContractsAsync"/> 每頁要幾筆。
    /// How many contracts <see cref="ListAllContractsAsync"/> asks for per page.
    /// </summary>
    private const int AllContractsPageSize = 500;

    /// <summary>
    /// <see cref="ListAllContractsAsync"/> 最多翻幾頁。全市場的商品數遠低於 500 × 這個數字,超過代表分頁沒有結束,
    /// 寧可失敗也不要無窮翻頁。
    /// The most pages <see cref="ListAllContractsAsync"/> turns. The whole market holds far fewer contracts than 500
    /// times this, so going past it means the paging does not end, and failing beats paging forever.
    /// </summary>
    private const int MaxAllContractsPages = 200;

    private readonly ShioajiApiClient _api;
    private readonly ShioajiRateBudget? _budget;

    /// <summary>
    /// 建立用戶端。
    /// Creates the client.
    /// </summary>
    /// <param name="api">低階用戶端。The low-level client.</param>
    /// <param name="budget">
    /// 本地限流預算,只有 <see cref="ListAllContractsAsync"/> 的每一頁會用到;<see langword="null"/> 表示不限。
    /// The local rate budget, used only by each page of <see cref="ListAllContractsAsync"/>;
    /// <see langword="null"/> means unlimited.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="api"/> 為 <see langword="null"/> 時擲出。Thrown when <paramref name="api"/> is <see langword="null"/>.
    /// </exception>
    public ShioajiContractClient(ShioajiApiClient api, ShioajiRateBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _budget = budget;
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
    /// 列出某個類別的<b>全部</b>商品:以每頁 500 筆走完 <see cref="ListContractsAsync"/> 的所有分頁,每一頁先過
    /// <see cref="ShioajiBudgetKind.MarketQuery"/> 預算。
    /// Lists <b>every</b> contract of a category: walks all pages of <see cref="ListContractsAsync"/> at 500 per
    /// page, each page first going through the <see cref="ShioajiBudgetKind.MarketQuery"/> budget.
    /// </summary>
    /// <param name="securityType">商品類別。The security type.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 全部商品,或第一個失敗(中途失敗時已翻的頁不會回傳,不給缺半邊的清單)。
    /// Every contract, or the first failure; pages already turned are not returned when one fails midway, so no
    /// half-filled list is handed out.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>怎麼判斷翻完了。</b>依序符合任一條就停:這一頁沒有商品;這一頁少於 500 筆;累計筆數達到 server 回報的
    /// <c>total</c>;這一頁多於 500 筆(server 忽略了分頁、一次全回)。
    /// <b>How the end is detected.</b> The first of these stops the walk: a page with no contracts; a page with
    /// fewer than 500; a running count that reached the <c>total</c> the server reported; a page with more than 500
    /// (the server ignored paging and answered with everything).
    /// </para>
    /// <para>
    /// <b>分頁沒前進就失敗。</b>某一頁(滿 500 筆)的第一筆和上一頁的第一筆相同,代表 server 一直在回同一頁,
    /// 回 <see cref="ShioajiErrorCodes.MalformedResponse"/>;翻到 200 頁還沒結束同樣失敗。
    /// <b>Paging that does not advance fails.</b> A full page whose first contract equals the previous page's means
    /// the server keeps answering the same page, and that is <see cref="ShioajiErrorCodes.MalformedResponse"/>; so is
    /// reaching page 200 without an end.
    /// </para>
    /// </remarks>
    public async Task<Result<IReadOnlyList<ShioajiContract>>> ListAllContractsAsync(
        ShioajiSecurityType securityType,
        CancellationToken cancellationToken = default)
    {
        if (!ShioajiWire.SecurityType.TryGetWire(securityType, out _))
        {
            return ShioajiErrors.InvalidArgument("必須指定商品類別。A security type must be given.");
        }

        var all = new List<ShioajiContract>();
        string? previousFirst = null;

        for (var page = 1; page <= MaxAllContractsPages; page++)
        {
            if (_budget is not null)
            {
                var permit = await _budget.AcquireAsync(ShioajiBudgetKind.MarketQuery, cancellationToken).ConfigureAwait(false);

                if (permit.IsFailure)
                {
                    return permit.Error;
                }
            }

            var result = await ListContractsAsync(securityType, page, AllContractsPageSize, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result.ToFailure<IReadOnlyList<ShioajiContract>>();
            }

            var current = result.GetValueOrThrow();

            if (current.Items.Count == 0)
            {
                return all;
            }

            var first = string.Concat(current.Items[0].Exchange, "/", current.Items[0].Code);

            if (page > 1 && current.Items.Count == AllContractsPageSize && string.Equals(first, previousFirst, StringComparison.Ordinal))
            {
                return ShioajiErrors.MalformedResponse(
                    $"商品列表的第 {page} 頁和上一頁一樣(第一筆都是 {first}),分頁沒有前進。Page {page} of the contract list equals the previous one (both start at {first}); the paging does not advance.");
            }

            previousFirst = first;
            all.AddRange(current.Items);

            if (current.Items.Count != AllContractsPageSize || (current.Total is { } total && all.Count >= total))
            {
                return all;
            }
        }

        return ShioajiErrors.MalformedResponse(
            $"商品列表翻了 {MaxAllContractsPages} 頁還沒結束。The contract list did not end after {MaxAllContractsPages} pages.");
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
