using System.Globalization;
using System.Text.Json;
using Ozakboy.TradeKit.Shioaji.Auth;

namespace Ozakboy.TradeKit.Shioaji.Portfolio;

/// <summary>
/// 帳務查詢:庫存、已實現損益、交割款、交易額度、期貨保證金與銀行餘額。
/// Portfolio queries: positions, realized profit and loss, settlements, trading limits, futures margin and the bank
/// balance.
/// </summary>
/// <remarks>
/// <para>
/// <b>這些端點目前只對本機假伺服器驗證,尚未對真的 <c>shioaji server</c> 實測。</b>請求本文的欄位與回應的欄位名依官方文件
/// 的帳務章節(<c>https://sinotrade.github.io/llms-full.txt</c>);但文件沒有逐字的回應範例,所以回應的外形(裸陣列、
/// 包著陣列的物件、欄位陣列)是推定的,三種都收,見 <c>ShioajiPortfolioParser</c> 的說明。第一次對真 server 使用時,
/// 請先在模擬環境確認回應,並留意文件寫的限制:交易額度只在交易日 08:30 至 15:00 能查、銀行餘額只支援永豐銀行、
/// 分戶帳與 LINE Bank、模擬環境的帳務數字是示範資料。
/// <b>These endpoints are so far verified only against the local fake server and have not been measured against a real
/// <c>shioaji server</c>.</b> The request fields and the response field names follow the portfolio chapter of the
/// official documentation (<c>https://sinotrade.github.io/llms-full.txt</c>); the documentation has no verbatim
/// responses, though, so the response shape (a bare array, an object wrapping an array, or column arrays) is inferred
/// and all three are accepted, see <c>ShioajiPortfolioParser</c>. On first use against a real server, check the
/// responses in the simulation environment first, and mind the limits the documentation states: trading limits can
/// be queried only 08:30 to 15:00 on trading days, the bank balance supports only SinoPac Bank, sub-accounts and LINE
/// Bank, and the portfolio numbers of the simulation environment are demonstration data.
/// </para>
/// <para>
/// <b>預算。</b>所有方法都計入 <see cref="ShioajiBudgetKind.Portfolio"/>(5 秒 25 次)。本地檢查(帳號、日期區間、
/// 參數)在取預算之前做,被本地拒絕的呼叫不佔額度。預算用盡且等待超過 <see cref="ShioajiRateBudgetOptions.MaxWait"/>
/// 時,請求<b>不會送出</b>。
/// <b>Budget.</b> Every method counts against <see cref="ShioajiBudgetKind.Portfolio"/> (25 per 5 seconds). The local
/// checks (account, date range, arguments) run before the budget is taken, so a call refused locally consumes
/// nothing. When the budget is spent and the wait would exceed <see cref="ShioajiRateBudgetOptions.MaxWait"/> the
/// request is <b>not sent</b>.
/// </para>
/// <para>
/// <b>全部是 POST,永不重試</b>(見 <see cref="ShioajiApiClient"/>)。這些是唯讀查詢,重試本身沒有副作用,但本套件
/// 對 POST 一律只送一次,由呼叫端決定要不要再查。
/// <b>Every call is a POST and is never retried</b> (see <see cref="ShioajiApiClient"/>). They are read-only queries
/// and a retry would have no side effect, but this package sends a POST exactly once and leaves the decision to ask
/// again to the caller.
/// </para>
/// <para>
/// <b>帳號。</b>每個方法的 <c>account</c> 參數為 <see langword="null"/> 時 server 用預設帳號(帳號列表中第一個
/// 證券帳號或期貨帳號)。證券與期貨走不同的方法,因為兩者的回應欄位不同;交割款、交易額度與銀行餘額只有證券帳號、
/// 期貨保證金只有期貨帳號,所以這幾個方法不收帳號類型。
/// <b>Accounts.</b> With a <see langword="null"/> <c>account</c> the server uses its default account (the first
/// securities or futures account in the list). Securities and futures have separate methods because their response
/// fields differ; settlements, trading limits and the bank balance exist only for a securities account and the
/// futures margin only for a futures account, so those methods take no account type.
/// </para>
/// </remarks>
public sealed class ShioajiPortfolioClient
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly ShioajiApiClient _api;
    private readonly ShioajiRateBudget _budget;

    /// <summary>
    /// 建立用戶端。
    /// Creates the client.
    /// </summary>
    /// <param name="api">低階用戶端。The low-level client.</param>
    /// <param name="budget">本地限流預算。The local rate budget.</param>
    /// <exception cref="ArgumentNullException">
    /// 任一參數為 <see langword="null"/> 時擲出。Thrown when an argument is <see langword="null"/>.
    /// </exception>
    public ShioajiPortfolioClient(ShioajiApiClient api, ShioajiRateBudget budget)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(budget);

        _api = api;
        _budget = budget;
    }

    /// <summary>
    /// 證券庫存與未實現損益(<c>POST /api/v1/portfolio/position_unit</c>,<c>account_type</c> 為 <c>S</c>)。
    /// Stock positions with unrealized profit and loss (<c>POST /api/v1/portfolio/position_unit</c>,
    /// <c>account_type</c> <c>S</c>).
    /// </summary>
    /// <param name="unit">整股或零股;<see cref="ShioajiPositionUnit.Unknown"/> 表示不帶 <c>unit</c>,由 server 預設(整股)。Round or odd lots; <see cref="ShioajiPositionUnit.Unknown"/> sends no <c>unit</c> and the server defaults to round lots.</param>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>庫存清單(沒有庫存是成功的空清單),或已對映的失敗。The positions (none is a successful empty list), or a mapped failure.</returns>
    public Task<Result<IReadOnlyList<ShioajiStockPosition>>> ListStockPositionsAsync(
        ShioajiPositionUnit unit = ShioajiPositionUnit.Common,
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioPositionUnit,
            ShioajiAccountType.Stock,
            account,
            unit,
            default,
            ShioajiPortfolioParser.ParseStockPositions,
            cancellationToken);

    /// <summary>
    /// 期貨與選擇權庫存(<c>POST /api/v1/portfolio/position_unit</c>,<c>account_type</c> 為 <c>F</c>)。
    /// Futures and options positions (<c>POST /api/v1/portfolio/position_unit</c>, <c>account_type</c> <c>F</c>).
    /// </summary>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>庫存清單,或已對映的失敗。The positions, or a mapped failure.</returns>
    public Task<Result<IReadOnlyList<ShioajiFuturesPosition>>> ListFuturesPositionsAsync(
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioPositionUnit,
            ShioajiAccountType.Futures,
            account,
            ShioajiPositionUnit.Unknown,
            default,
            ShioajiPortfolioParser.ParseFuturesPositions,
            cancellationToken);

    /// <summary>
    /// 證券庫存明細(<c>POST /api/v1/portfolio/position_detail</c>)。
    /// Stock position details (<c>POST /api/v1/portfolio/position_detail</c>).
    /// </summary>
    /// <param name="positionId">庫存識別碼,取自 <see cref="ShioajiStockPosition.Id"/>(文件的 <c>detail_id</c>)。The position id taken from <see cref="ShioajiStockPosition.Id"/> (the documentation's <c>detail_id</c>).</param>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>明細清單,或已對映的失敗。The details, or a mapped failure.</returns>
    public Task<Result<IReadOnlyList<ShioajiStockPositionDetail>>> ListStockPositionDetailsAsync(
        long positionId,
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioPositionDetail,
            ShioajiAccountType.Stock,
            account,
            ShioajiPositionUnit.Unknown,
            Detail(positionId),
            ShioajiPortfolioParser.ParseStockPositionDetails,
            cancellationToken);

    /// <summary>
    /// 期貨庫存明細(<c>POST /api/v1/portfolio/position_detail</c>)。
    /// Futures position details (<c>POST /api/v1/portfolio/position_detail</c>).
    /// </summary>
    /// <param name="positionId">庫存識別碼,取自 <see cref="ShioajiFuturesPosition.Id"/>。The position id taken from <see cref="ShioajiFuturesPosition.Id"/>.</param>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>明細清單,或已對映的失敗。The details, or a mapped failure.</returns>
    public Task<Result<IReadOnlyList<ShioajiFuturesPositionDetail>>> ListFuturesPositionDetailsAsync(
        long positionId,
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioPositionDetail,
            ShioajiAccountType.Futures,
            account,
            ShioajiPositionUnit.Unknown,
            Detail(positionId),
            ShioajiPortfolioParser.ParseFuturesPositionDetails,
            cancellationToken);

    /// <summary>
    /// 證券已實現損益(<c>POST /api/v1/portfolio/profit_loss</c>)。
    /// Stock realized profit and loss (<c>POST /api/v1/portfolio/profit_loss</c>).
    /// </summary>
    /// <param name="beginDate">起始日(含)。The first day, inclusive.</param>
    /// <param name="endDate">結束日(含)。The last day, inclusive.</param>
    /// <param name="unit">整股或零股;<see cref="ShioajiPositionUnit.Unknown"/> 表示不帶 <c>unit</c>。Round or odd lots; <see cref="ShioajiPositionUnit.Unknown"/> sends no <c>unit</c>.</param>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 損益清單,或已對映的失敗。<paramref name="beginDate"/> 晚於 <paramref name="endDate"/> 回
    /// <see cref="ShioajiErrorCodes.InvalidArgument"/>,不送出請求。
    /// The profit and loss rows, or a mapped failure. A <paramref name="beginDate"/> after <paramref name="endDate"/>
    /// returns <see cref="ShioajiErrorCodes.InvalidArgument"/> and nothing is sent.
    /// </returns>
    public Task<Result<IReadOnlyList<ShioajiStockProfitLoss>>> ListStockProfitLossAsync(
        DateOnly beginDate,
        DateOnly endDate,
        ShioajiPositionUnit unit = ShioajiPositionUnit.Common,
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioProfitLoss,
            ShioajiAccountType.Stock,
            account,
            unit,
            DateRange(beginDate, endDate),
            ShioajiPortfolioParser.ParseStockProfitLoss,
            cancellationToken);

    /// <summary>
    /// 期貨已實現損益(<c>POST /api/v1/portfolio/profit_loss</c>)。
    /// Futures realized profit and loss (<c>POST /api/v1/portfolio/profit_loss</c>).
    /// </summary>
    /// <param name="beginDate">起始日(含)。The first day, inclusive.</param>
    /// <param name="endDate">結束日(含)。The last day, inclusive.</param>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>損益清單,或已對映的失敗。The profit and loss rows, or a mapped failure.</returns>
    public Task<Result<IReadOnlyList<ShioajiFuturesProfitLoss>>> ListFuturesProfitLossAsync(
        DateOnly beginDate,
        DateOnly endDate,
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioProfitLoss,
            ShioajiAccountType.Futures,
            account,
            ShioajiPositionUnit.Unknown,
            DateRange(beginDate, endDate),
            ShioajiPortfolioParser.ParseFuturesProfitLoss,
            cancellationToken);

    /// <summary>
    /// 證券已實現損益明細(<c>POST /api/v1/portfolio/profit_loss_detail</c>),含交易類型(一般或當沖)。
    /// Stock realized profit and loss details (<c>POST /api/v1/portfolio/profit_loss_detail</c>), with the trade type
    /// (ordinary or day trade).
    /// </summary>
    /// <param name="profitLossId">損益識別碼,取自 <see cref="ShioajiStockProfitLoss.Id"/>(文件的 <c>detail_id</c>)。The id taken from <see cref="ShioajiStockProfitLoss.Id"/> (the documentation's <c>detail_id</c>).</param>
    /// <param name="unit">整股或零股;<see cref="ShioajiPositionUnit.Unknown"/> 表示不帶 <c>unit</c>。Round or odd lots; <see cref="ShioajiPositionUnit.Unknown"/> sends no <c>unit</c>.</param>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>明細清單,或已對映的失敗。The details, or a mapped failure.</returns>
    public Task<Result<IReadOnlyList<ShioajiStockProfitLossDetail>>> ListStockProfitLossDetailsAsync(
        long profitLossId,
        ShioajiPositionUnit unit = ShioajiPositionUnit.Common,
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioProfitLossDetail,
            ShioajiAccountType.Stock,
            account,
            unit,
            Detail(profitLossId),
            ShioajiPortfolioParser.ParseStockProfitLossDetails,
            cancellationToken);

    /// <summary>
    /// 期貨已實現損益明細(<c>POST /api/v1/portfolio/profit_loss_detail</c>)。
    /// Futures realized profit and loss details (<c>POST /api/v1/portfolio/profit_loss_detail</c>).
    /// </summary>
    /// <param name="profitLossId">損益識別碼,取自 <see cref="ShioajiFuturesProfitLoss.Id"/>。The id taken from <see cref="ShioajiFuturesProfitLoss.Id"/>.</param>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>明細清單,或已對映的失敗。The details, or a mapped failure.</returns>
    public Task<Result<IReadOnlyList<ShioajiFuturesProfitLossDetail>>> ListFuturesProfitLossDetailsAsync(
        long profitLossId,
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioProfitLossDetail,
            ShioajiAccountType.Futures,
            account,
            ShioajiPositionUnit.Unknown,
            Detail(profitLossId),
            ShioajiPortfolioParser.ParseFuturesProfitLossDetails,
            cancellationToken);

    /// <summary>
    /// 證券已實現損益彙總(<c>POST /api/v1/portfolio/profitloss_sum</c>):依商品的清單加合計。
    /// The stock realized profit and loss summary (<c>POST /api/v1/portfolio/profitloss_sum</c>): a per-contract list
    /// plus a total.
    /// </summary>
    /// <param name="beginDate">起始日(含)。The first day, inclusive.</param>
    /// <param name="endDate">結束日(含)。The last day, inclusive.</param>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>彙總,或已對映的失敗。The summary, or a mapped failure.</returns>
    public Task<Result<ShioajiProfitLossSummary<ShioajiStockProfitLossSummaryItem>>> GetStockProfitLossSummaryAsync(
        DateOnly beginDate,
        DateOnly endDate,
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioProfitLossSummary,
            ShioajiAccountType.Stock,
            account,
            ShioajiPositionUnit.Unknown,
            DateRange(beginDate, endDate),
            ShioajiPortfolioParser.ParseStockProfitLossSummary,
            cancellationToken);

    /// <summary>
    /// 期貨已實現損益彙總(<c>POST /api/v1/portfolio/profitloss_sum</c>)。
    /// The futures realized profit and loss summary (<c>POST /api/v1/portfolio/profitloss_sum</c>).
    /// </summary>
    /// <param name="beginDate">起始日(含)。The first day, inclusive.</param>
    /// <param name="endDate">結束日(含)。The last day, inclusive.</param>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>彙總,或已對映的失敗。The summary, or a mapped failure.</returns>
    public Task<Result<ShioajiProfitLossSummary<ShioajiFuturesProfitLossSummaryItem>>> GetFuturesProfitLossSummaryAsync(
        DateOnly beginDate,
        DateOnly endDate,
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioProfitLossSummary,
            ShioajiAccountType.Futures,
            account,
            ShioajiPositionUnit.Unknown,
            DateRange(beginDate, endDate),
            ShioajiPortfolioParser.ParseFuturesProfitLossSummary,
            cancellationToken);

    /// <summary>
    /// 交割款(<c>POST /api/v1/portfolio/settlements</c>,只有證券帳號)。
    /// Settlements (<c>POST /api/v1/portfolio/settlements</c>, securities account only).
    /// </summary>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>各交割日的金額(T+0、T+1…),或已對映的失敗。The amounts per settlement day (T+0, T+1 and so on), or a mapped failure.</returns>
    public Task<Result<IReadOnlyList<ShioajiSettlement>>> ListSettlementsAsync(
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioSettlements,
            ShioajiAccountType.Stock,
            account,
            ShioajiPositionUnit.Unknown,
            default,
            ShioajiPortfolioParser.ParseSettlements,
            cancellationToken);

    /// <summary>
    /// 交易額度(<c>POST /api/v1/portfolio/trading_limits</c>,只有證券帳號)。文件寫交易日 08:30 至 15:00 可查。
    /// Trading limits (<c>POST /api/v1/portfolio/trading_limits</c>, securities account only). The documentation says
    /// it can be queried 08:30 to 15:00 on trading days.
    /// </summary>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>電子交易、融資與融券三組額度,或已對映的失敗。The electronic-trading, margin-purchase and short-sale limits, or a mapped failure.</returns>
    public Task<Result<ShioajiTradingLimits>> GetTradingLimitsAsync(
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioTradingLimits,
            ShioajiAccountType.Stock,
            account,
            ShioajiPositionUnit.Unknown,
            default,
            ShioajiPortfolioParser.ParseTradingLimits,
            cancellationToken);

    /// <summary>
    /// 期貨保證金與權益(<c>POST /api/v1/portfolio/margin</c>,只有期貨帳號)。
    /// Futures margin and equity (<c>POST /api/v1/portfolio/margin</c>, futures account only).
    /// </summary>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>保證金,或已對映的失敗。The margin, or a mapped failure.</returns>
    public Task<Result<ShioajiMargin>> GetMarginAsync(
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioMargin,
            ShioajiAccountType.Futures,
            account,
            ShioajiPositionUnit.Unknown,
            default,
            ShioajiPortfolioParser.ParseMargin,
            cancellationToken);

    /// <summary>
    /// 交割銀行帳戶餘額(<c>POST /api/v1/portfolio/account_balance</c>,只有證券帳號)。
    /// 文件寫目前只支援永豐銀行、分戶帳與 LINE Bank。
    /// The settlement bank account balance (<c>POST /api/v1/portfolio/account_balance</c>, securities account only).
    /// The documentation says only SinoPac Bank, sub-accounts and LINE Bank are supported for now.
    /// </summary>
    /// <param name="account">帳號;<see langword="null"/> 表示用預設帳號。The account, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 餘額;server 在 200 回應裡報告的錯誤放在 <see cref="ShioajiAccountBalance.ErrorMessage"/>,不是失敗。
    /// The balance; an error the server reports inside a 200 response lands in <see cref="ShioajiAccountBalance.ErrorMessage"/>
    /// and is not a failure.
    /// </returns>
    public Task<Result<ShioajiAccountBalance>> GetAccountBalanceAsync(
        ShioajiPortfolioAccount? account = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.PortfolioAccountBalance,
            ShioajiAccountType.Stock,
            account,
            ShioajiPositionUnit.Unknown,
            default,
            ShioajiPortfolioParser.ParseAccountBalance,
            cancellationToken);

    /// <summary>
    /// 日期區間的額外欄位。起始日晚於結束日在本地就拒絕,不送出請求、不佔預算。
    /// The extra fields for a date range. A start after the end is refused locally, with nothing sent and no budget
    /// taken.
    /// </summary>
    private static Extra DateRange(DateOnly beginDate, DateOnly endDate)
    {
        if (beginDate > endDate)
        {
            return new Extra(
                ShioajiErrors.InvalidArgument(
                    $"起始日 {FormatDate(beginDate)} 晚於結束日 {FormatDate(endDate)}。The start date {FormatDate(beginDate)} is after the end date {FormatDate(endDate)}."),
                null);
        }

        return new Extra(
            null,
            writer =>
            {
                writer.WriteString("begin_date", FormatDate(beginDate));
                writer.WriteString("end_date", FormatDate(endDate));
            });
    }

    /// <summary>
    /// 明細查詢的額外欄位 <c>detail_id</c>。
    /// The extra field <c>detail_id</c> of a detail query.
    /// </summary>
    private static Extra Detail(long id) => new(null, writer => writer.WriteNumber("detail_id", id));

    private static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private async Task<Result<T>> QueryAsync<T>(
        string path,
        ShioajiAccountType accountType,
        ShioajiPortfolioAccount? account,
        ShioajiPositionUnit unit,
        Extra extra,
        Func<string, Result<T>> parse,
        CancellationToken cancellationToken)
    {
        var invalid = extra.Invalid ?? ValidateAccount(account);

        if (invalid is not null)
        {
            return invalid;
        }

        _ = ShioajiWire.AccountType.TryGetWire(accountType, out var accountWire);

        var json = ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("account_type", accountWire);

            extra.Write?.Invoke(writer);

            if (ShioajiPortfolioParser.UnitMap.TryGetWire(unit, out var unitWire))
            {
                writer.WriteString("unit", unitWire);
            }

            if (account is not null)
            {
                WriteOptional(writer, "broker_id", account.BrokerId);
                WriteOptional(writer, "account_id", account.AccountId);
                WriteOptional(writer, "person_id", account.PersonId);
            }

            writer.WriteEndObject();
        });

        var permit = await _budget.AcquireAsync(ShioajiBudgetKind.Portfolio, cancellationToken).ConfigureAwait(false);

        if (permit.IsFailure)
        {
            return permit.Error;
        }

        var response = await _api.PostStringAsync(path, json, cancellationToken).ConfigureAwait(false);

        return response.IsFailure ? response.ToFailure<T>() : parse(response.GetValueOrThrow());
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            writer.WriteString(name, value.Trim());
        }
    }

    private static Error? ValidateAccount(ShioajiPortfolioAccount? account)
    {
        if (account is not null && string.IsNullOrWhiteSpace(account.BrokerId) != string.IsNullOrWhiteSpace(account.AccountId))
        {
            return ShioajiErrors.InvalidArgument(
                "BrokerId 與 AccountId 必須同時提供或同時省略。BrokerId and AccountId must be given together or omitted together.");
        }

        return null;
    }

    /// <summary>
    /// 一個請求在 <c>account_type</c> 之外要多寫的欄位,以及本地檢查已經發現的錯誤。
    /// The fields a request writes beyond <c>account_type</c>, and an error the local checks already found.
    /// </summary>
    /// <param name="Invalid">本地檢查的錯誤;沒有時為 <see langword="null"/>。The local-check error, or <see langword="null"/>.</param>
    /// <param name="Write">寫出額外欄位的回呼;沒有時為 <see langword="null"/>。Writes the extra fields, or <see langword="null"/>.</param>
    private readonly record struct Extra(Error? Invalid, Action<Utf8JsonWriter>? Write);
}
