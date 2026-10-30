using System.Globalization;
using System.Text.Json;
using Ozakboy.TradeKit.Shioaji.Contracts;

namespace Ozakboy.TradeKit.Shioaji.History;

/// <summary>
/// 歷史資料與查詢的端點:1 分 K、逐筆、快照、全市場日 OHLCV、排行掃描、借券券源、資券餘額、處置股與注意股。
/// The historical-data and query endpoints: 1-minute bars, ticks, snapshots, the all-market daily OHLCV, the ranking
/// scanner, short-stock sources, margin and short balances, and the disposition and attention lists.
/// </summary>
/// <remarks>
/// <para>
/// <b>盤中禁止用這裡的查詢當即時報價。</b>券商明文規定 <c>ticks</c> / <c>snapshots</c> / <c>kbars</c> 不可在盤中取代即時行情,
/// 違規可能停 IP 與 ID,而且反覆登入無法解除。本套件不替你判斷現在是不是盤中:這個 client 設計給「收盤後回補」使用,
/// 即時行情請訂閱串流(<see cref="MarketData.ShioajiQuoteClient"/>)。
/// <b>Never use these queries as live quotes during the session.</b> The broker forbids <c>ticks</c>,
/// <c>snapshots</c> and <c>kbars</c> as a stand-in for live data, a breach can suspend the IP and the ID, and
/// repeated logins do not lift it. This package does not decide for you whether the market is open: the client is
/// meant for after-the-close backfill, and live data belongs to the stream (<see cref="MarketData.ShioajiQuoteClient"/>).
/// </para>
/// <para>
/// <b>預算。</b>所有方法都先過 <see cref="ShioajiBudgetKind.MarketQuery"/>(10 秒 50 次),預算用盡且等待超過
/// <see cref="ShioajiRateBudgetOptions.MaxWait"/> 時,請求<b>不會送出</b>。本地檢查(區間、檔數、參數)在取預算之前做,
/// 被本地拒絕的呼叫不佔任何額度。
/// <b>Budget.</b> Every method first goes through <see cref="ShioajiBudgetKind.MarketQuery"/> (50 per 10 seconds),
/// and when the budget is spent and the wait would exceed <see cref="ShioajiRateBudgetOptions.MaxWait"/> the request
/// is <b>not sent</b>. The local checks (range, count, arguments) run before the budget is taken, so a call refused
/// locally consumes nothing.
/// </para>
/// <para>
/// <b>空回應不等於沒有資料。</b>每日流量超量時行情查詢不報錯、只回空值;回傳的 <see cref="ShioajiHistoryPage{T}"/>
/// 帶 <see cref="ShioajiHistoryPage{T}.PossiblyTrafficExhausted"/> 讓呼叫端分辨。流量見 <see cref="ShioajiTrafficMeter"/>。
/// <b>An empty answer is not proof of no data.</b> Over the daily traffic allowance market queries do not fail,
/// they answer with empty values; the returned <see cref="ShioajiHistoryPage{T}"/> carries
/// <see cref="ShioajiHistoryPage{T}.PossiblyTrafficExhausted"/> so the caller can tell. For traffic see
/// <see cref="ShioajiTrafficMeter"/>.
/// </para>
/// <para>
/// 所有 POST 都<b>永不重試</b>(見 <see cref="ShioajiApiClient"/>);處置股與注意股是 GET,可以重試。
/// Every POST is <b>never retried</b> (see <see cref="ShioajiApiClient"/>); the disposition and attention lists are
/// GET and may be retried.
/// </para>
/// </remarks>
public sealed class ShioajiHistoryClient
{
    private readonly ShioajiApiClient _api;
    private readonly ShioajiRateBudget _budget;
    private readonly string _dailyQuotesPath;
    private readonly ShioajiTrafficMeter? _trafficMeter;

    /// <summary>
    /// 建立用戶端。
    /// Creates the client.
    /// </summary>
    /// <param name="api">低階用戶端。The low-level client.</param>
    /// <param name="budget">本地限流預算。The local rate budget.</param>
    /// <param name="options">
    /// 設定,取 <see cref="ShioajiOptions.DailyQuotesPath"/> 用;<see langword="null"/> 時用 <see cref="ShioajiApiPaths.DailyQuotes"/>。
    /// The settings, used for <see cref="ShioajiOptions.DailyQuotesPath"/>; <see cref="ShioajiApiPaths.DailyQuotes"/>
    /// when <see langword="null"/>.
    /// </param>
    /// <param name="trafficMeter">
    /// 流量計,判斷空回應是不是流量用完用;<see langword="null"/> 時空回應一律標成「可能用完」。
    /// The traffic meter, used to judge whether an empty answer is exhaustion; with <see langword="null"/> an empty
    /// answer is always flagged as possibly exhausted.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="api"/> 或 <paramref name="budget"/> 為 <see langword="null"/> 時擲出。
    /// Thrown when <paramref name="api"/> or <paramref name="budget"/> is <see langword="null"/>.
    /// </exception>
    public ShioajiHistoryClient(
        ShioajiApiClient api,
        ShioajiRateBudget budget,
        ShioajiOptions? options = null,
        ShioajiTrafficMeter? trafficMeter = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(budget);

        _api = api;
        _budget = budget;
        _dailyQuotesPath = options?.DailyQuotesPath ?? ShioajiApiPaths.DailyQuotes;
        _trafficMeter = trafficMeter;
    }

    /// <summary>
    /// 查 1 分 K(<c>POST /api/v1/data/kbars</c>)。只有 1 分 K,要別的週期請自己合成。
    /// Queries 1-minute bars (<c>POST /api/v1/data/kbars</c>). Only 1-minute bars exist; build other periods
    /// yourself.
    /// </summary>
    /// <param name="contract">商品。The contract.</param>
    /// <param name="start">起始日(含)。The first day, inclusive.</param>
    /// <param name="end">結束日(含)。The last day, inclusive.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// K 棒(時間由舊到新是否排序取決於 server,本套件不重排),或已對映的失敗。
    /// The bars (whether they come oldest first is up to the server; this package does not reorder), or a mapped
    /// failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>單次區間最多 30 天</b>(<see cref="ShioajiConstants.MaxKbarRangeDays"/>,含頭尾兩天),超過回
    /// <see cref="ShioajiErrorCodes.HistoryRangeTooLong"/>,<paramref name="start"/> 晚於 <paramref name="end"/> 回
    /// <see cref="ShioajiErrorCodes.InvalidArgument"/>,兩者都不送出請求。官方只說「30 天」沒說含不含頭尾,這裡取較保守的
    /// 「含頭尾共 30 個日曆日」。歷史自 2020-03-02 起(股票 / 指數)、2020-03-22 起(期貨),更早的區間由 server 回空。
    /// <b>One query spans at most 30 days</b> (<see cref="ShioajiConstants.MaxKbarRangeDays"/>, both ends counted);
    /// more returns <see cref="ShioajiErrorCodes.HistoryRangeTooLong"/> and a <paramref name="start"/> after
    /// <paramref name="end"/> returns <see cref="ShioajiErrorCodes.InvalidArgument"/>, neither sending a request. The
    /// broker says "30 days" without saying whether the ends count; the more conservative 30 calendar days with both
    /// ends counted is used. History begins on 2020-03-02 (stocks and indices) and 2020-03-22 (futures); an earlier
    /// span is answered empty by the server.
    /// </para>
    /// </remarks>
    public async Task<Result<ShioajiHistoryPage<ShioajiKbar>>> GetKbarsAsync(
        ShioajiContractRef contract,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var invalid = ValidateContract(contract, out var securityType);

        if (invalid is not null)
        {
            return invalid;
        }

        if (start > end)
        {
            return ShioajiErrors.InvalidArgument(
                $"起始日 {FormatDate(start)} 晚於結束日 {FormatDate(end)}。The start date {FormatDate(start)} is after the end date {FormatDate(end)}.");
        }

        var days = end.DayNumber - start.DayNumber + 1;

        if (days > ShioajiConstants.MaxKbarRangeDays)
        {
            return ShioajiErrors.HistoryRangeTooLong(start, end, days, ShioajiConstants.MaxKbarRangeDays);
        }

        var json = ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            WriteContract(writer, "contract", contract, securityType);
            writer.WriteString("start", FormatDate(start));
            writer.WriteString("end", FormatDate(end));
            writer.WriteEndObject();
        });

        return await QueryAsync(ShioajiApiPaths.Kbars, json, ShioajiHistoryParser.ParseKbars, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 查逐筆成交(<c>POST /api/v1/data/ticks</c>)。
    /// Queries ticks (<c>POST /api/v1/data/ticks</c>).
    /// </summary>
    /// <param name="contract">商品。The contract.</param>
    /// <param name="date">交易日。The trading day.</param>
    /// <param name="query">查詢條件,見 <see cref="ShioajiTicksQuery"/>。The conditions, see <see cref="ShioajiTicksQuery"/>.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>逐筆成交,或已對映的失敗。The ticks, or a mapped failure.</returns>
    public async Task<Result<ShioajiHistoryPage<ShioajiTick>>> GetTicksAsync(
        ShioajiContractRef contract,
        DateOnly date,
        ShioajiTicksQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(query);

        var invalid = ValidateContract(contract, out var securityType);

        if (invalid is not null)
        {
            return invalid;
        }

        var queryInvalid = ValidateTicksQuery(query);

        if (queryInvalid is not null)
        {
            return queryInvalid;
        }

        var json = ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            WriteContract(writer, "contract", contract, securityType);
            writer.WriteString("date", FormatDate(date));
            writer.WriteString("query_type", query.Type.ToString());

            switch (query.Type)
            {
                case ShioajiTicksQueryType.RangeTime:
                    writer.WriteString("time_start", FormatTime(query.TimeStart!.Value));
                    writer.WriteString("time_end", FormatTime(query.TimeEnd!.Value));
                    break;
                case ShioajiTicksQueryType.LastCount:
                    writer.WriteNumber("last_cnt", query.LastCount!.Value);
                    break;
            }

            writer.WriteEndObject();
        });

        return await QueryAsync(ShioajiApiPaths.Ticks, json, ShioajiHistoryParser.ParseTicks, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 查快照(<c>POST /api/v1/data/snapshots</c>)。
    /// Queries snapshots (<c>POST /api/v1/data/snapshots</c>).
    /// </summary>
    /// <param name="contracts">商品清單,至少 1 檔、最多 500 檔(<see cref="ShioajiConstants.MaxSnapshotContracts"/>)。At least 1 and at most 500 contracts.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 快照,或已對映的失敗。超過 500 檔回 <see cref="ShioajiErrorCodes.HistoryTooManyContracts"/>,不送出請求。
    /// The snapshots, or a mapped failure. More than 500 contracts returns
    /// <see cref="ShioajiErrorCodes.HistoryTooManyContracts"/> and nothing is sent.
    /// </returns>
    public async Task<Result<ShioajiHistoryPage<ShioajiSnapshot>>> GetSnapshotsAsync(
        IReadOnlyList<ShioajiContractRef> contracts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        if (contracts.Count == 0)
        {
            return ShioajiErrors.InvalidArgument("快照查詢至少要一檔商品。A snapshot query needs at least one contract.");
        }

        if (contracts.Count > ShioajiConstants.MaxSnapshotContracts)
        {
            return ShioajiErrors.HistoryTooManyContracts(contracts.Count, ShioajiConstants.MaxSnapshotContracts);
        }

        var json = BuildContractListBody(contracts, requireStock: false, out var invalid);

        if (invalid is not null)
        {
            return invalid;
        }

        return await QueryAsync(ShioajiApiPaths.Snapshots, json, ShioajiHistoryParser.ParseSnapshots, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 查全市場日 OHLCV。<b>端點路徑與回應形狀都未驗證,待實機 OpenAPI 確認。</b>
    /// Queries the all-market daily OHLCV. <b>Both the endpoint path and the response shape are unverified and await
    /// confirmation against a real server's OpenAPI.</b>
    /// </summary>
    /// <param name="date">交易日。The trading day.</param>
    /// <param name="excludeWarrant">是否排除權證,預設 <see langword="true"/>。Whether to exclude warrants; <see langword="true"/> by default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>日 OHLCV,或已對映的失敗。The daily OHLCV, or a mapped failure.</returns>
    /// <remarks>
    /// <para>
    /// 文件只提到命令列的 <c>shioaji data daily-quotes --date YYYY-MM-DD --exclude-warrant</c>,沒有寫對應的 HTTP 路徑。
    /// 這裡用 <c>POST</c> <see cref="ShioajiOptions.DailyQuotesPath"/>(預設 <see cref="ShioajiApiPaths.DailyQuotes"/>),
    /// body <c>{date, exclude_warrant}</c>;回應假設是物件陣列,每筆至少 <c>code</c>、<c>open</c>、<c>high</c>、<c>low</c>、
    /// <c>close</c>、<c>volume</c>,多餘的欄位進 <see cref="ShioajiDailyQuote.Raw"/>。路徑不對時回 <c>shioaji.http.404</c>,
    /// 到 server 的 <c>/docs</c> 找真正的路徑後設定 <see cref="ShioajiOptions.DailyQuotesPath"/>。
    /// The documentation mentions only the command line's <c>shioaji data daily-quotes --date YYYY-MM-DD
    /// --exclude-warrant</c> and gives no HTTP path. This sends a <c>POST</c> to
    /// <see cref="ShioajiOptions.DailyQuotesPath"/> (<see cref="ShioajiApiPaths.DailyQuotes"/> by default) with the
    /// body <c>{date, exclude_warrant}</c>; the response is assumed to be an array of objects, each with at least
    /// <c>code</c>, <c>open</c>, <c>high</c>, <c>low</c>, <c>close</c> and <c>volume</c>, and any further fields go to
    /// <see cref="ShioajiDailyQuote.Raw"/>. A wrong path answers <c>shioaji.http.404</c>; find the real one on the
    /// server's <c>/docs</c> and set <see cref="ShioajiOptions.DailyQuotesPath"/>.
    /// </para>
    /// </remarks>
    public async Task<Result<ShioajiHistoryPage<ShioajiDailyQuote>>> GetDailyQuotesAsync(
        DateOnly date,
        bool excludeWarrant = true,
        CancellationToken cancellationToken = default)
    {
        var json = ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("date", FormatDate(date));
            writer.WriteBoolean("exclude_warrant", excludeWarrant);
            writer.WriteEndObject();
        });

        var path = _dailyQuotesPath;

        return await QueryAsync(path, json, body => ShioajiHistoryParser.ParseDailyQuotes(body, path), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 查排行掃描(<c>POST /api/v1/data/scanner</c>)。
    /// Queries a ranking scan (<c>POST /api/v1/data/scanner</c>).
    /// </summary>
    /// <param name="scannerType">排行種類。The kind of ranking.</param>
    /// <param name="date">日期;<see langword="null"/> 表示不帶、由 server 用預設。The date, or <see langword="null"/> to send none and let the server default.</param>
    /// <param name="ascending">是否由小到大。Whether ascending.</param>
    /// <param name="count">要幾筆,0 到 200,預設 100。How many rows, 0 to 200; 100 by default.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>排行,或已對映的失敗。The ranking, or a mapped failure.</returns>
    public async Task<Result<ShioajiHistoryPage<ShioajiScannerItem>>> GetScannerAsync(
        ShioajiScannerType scannerType,
        DateOnly? date = null,
        bool ascending = false,
        int count = 100,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(scannerType) || scannerType == ShioajiScannerType.Unknown)
        {
            return ShioajiErrors.InvalidArgument("必須指定排行種類。A scanner type must be given.");
        }

        if (count is < 0 or > ShioajiConstants.MaxScannerCount)
        {
            return ShioajiErrors.InvalidArgument(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"count 必須介於 0 到 {ShioajiConstants.MaxScannerCount}。count must be between 0 and {ShioajiConstants.MaxScannerCount}."));
        }

        var json = ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("scanner_type", scannerType.ToString());

            if (date is { } day)
            {
                writer.WriteString("date", FormatDate(day));
            }

            writer.WriteBoolean("ascending", ascending);
            writer.WriteNumber("count", count);
            writer.WriteEndObject();
        });

        return await QueryAsync(ShioajiApiPaths.Scanner, json, ShioajiHistoryParser.ParseScanner, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 查借券券源(<c>POST /api/v1/data/short_stock_sources</c>),只支援股票。
    /// Queries short-stock sources (<c>POST /api/v1/data/short_stock_sources</c>); stocks only.
    /// </summary>
    /// <param name="contracts">股票清單,至少 1 檔,類別必須全是 <see cref="ShioajiSecurityType.Stock"/>。At least one contract, all of them <see cref="ShioajiSecurityType.Stock"/>.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>券源,或已對映的失敗。The sources, or a mapped failure.</returns>
    /// <remarks>
    /// 請求 body 的形狀(<c>{contracts: [...]}</c>)文件沒寫,是比照快照推定的。
    /// The shape of the request body (<c>{contracts: [...]}</c>) is not documented and is inferred from the snapshot
    /// endpoint.
    /// </remarks>
    public async Task<Result<ShioajiHistoryPage<ShioajiShortStockSource>>> GetShortStockSourcesAsync(
        IReadOnlyList<ShioajiContractRef> contracts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        if (contracts.Count == 0)
        {
            return ShioajiErrors.InvalidArgument("借券券源查詢至少要一檔股票。A short-stock-source query needs at least one stock.");
        }

        var json = BuildContractListBody(contracts, requireStock: true, out var invalid);

        if (invalid is not null)
        {
            return invalid;
        }

        return await QueryAsync(ShioajiApiPaths.ShortStockSources, json, ShioajiHistoryParser.ParseShortStockSources, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 查資券餘額(<c>POST /api/v1/data/credit_enquire</c>),只支援股票。
    /// Queries margin and short balances (<c>POST /api/v1/data/credit_enquire</c>); stocks only.
    /// </summary>
    /// <param name="contracts">股票清單,至少 1 檔,類別必須全是 <see cref="ShioajiSecurityType.Stock"/>。At least one contract, all of them <see cref="ShioajiSecurityType.Stock"/>.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>資券餘額,或已對映的失敗。The balances, or a mapped failure.</returns>
    /// <remarks>
    /// 請求 body 的形狀(<c>{contracts: [...]}</c>)文件沒寫,是比照快照推定的。
    /// The shape of the request body (<c>{contracts: [...]}</c>) is not documented and is inferred from the snapshot
    /// endpoint.
    /// </remarks>
    public async Task<Result<ShioajiHistoryPage<ShioajiCreditEnquire>>> GetCreditEnquireAsync(
        IReadOnlyList<ShioajiContractRef> contracts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        if (contracts.Count == 0)
        {
            return ShioajiErrors.InvalidArgument("資券餘額查詢至少要一檔股票。A credit-enquiry needs at least one stock.");
        }

        var json = BuildContractListBody(contracts, requireStock: true, out var invalid);

        if (invalid is not null)
        {
            return invalid;
        }

        return await QueryAsync(ShioajiApiPaths.CreditEnquire, json, ShioajiHistoryParser.ParseCreditEnquire, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 查處置股清單(<c>GET /api/v1/data/regulatory_punish</c>)。
    /// Queries the disposition list (<c>GET /api/v1/data/regulatory_punish</c>).
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>處置股,或已對映的失敗。The disposition entries, or a mapped failure.</returns>
    public Task<Result<ShioajiHistoryPage<ShioajiRegulatoryItem>>> GetRegulatoryPunishAsync(CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.RegulatoryPunish,
            jsonBody: null,
            body => ShioajiHistoryParser.ParseRegulatory(body, ShioajiRegulatoryKind.Punish),
            cancellationToken);

    /// <summary>
    /// 查注意股清單(<c>GET /api/v1/data/regulatory_notice</c>)。
    /// Queries the attention list (<c>GET /api/v1/data/regulatory_notice</c>).
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>注意股,或已對映的失敗。The attention entries, or a mapped failure.</returns>
    public Task<Result<ShioajiHistoryPage<ShioajiRegulatoryItem>>> GetRegulatoryNoticeAsync(CancellationToken cancellationToken = default) =>
        QueryAsync(
            ShioajiApiPaths.RegulatoryNotice,
            jsonBody: null,
            body => ShioajiHistoryParser.ParseRegulatory(body, ShioajiRegulatoryKind.Notice),
            cancellationToken);

    /// <summary>
    /// 每個方法共用的流程:先取行情查詢預算,再送出,再對映,最後算出「空回應是不是流量用完」。
    /// <paramref name="jsonBody"/> 為 <see langword="null"/> 時送 GET,否則送 POST。
    /// The flow every method shares: take the market-query budget, send, map, and finally work out whether an empty
    /// answer may be exhaustion. A GET is sent when <paramref name="jsonBody"/> is <see langword="null"/>, a POST
    /// otherwise.
    /// </summary>
    private async Task<Result<ShioajiHistoryPage<T>>> QueryAsync<T>(
        string path,
        string? jsonBody,
        Func<string, Result<IReadOnlyList<T>>> parse,
        CancellationToken cancellationToken)
    {
        var permit = await _budget.AcquireAsync(ShioajiBudgetKind.MarketQuery, cancellationToken).ConfigureAwait(false);

        if (permit.IsFailure)
        {
            return permit.Error;
        }

        var response = jsonBody is null
            ? await _api.GetBodyAsync(path, cancellationToken).ConfigureAwait(false)
            : await _api.PostBodyAsync(path, jsonBody, cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.ToFailure<ShioajiHistoryPage<T>>();
        }

        var body = response.GetValueOrThrow();
        var items = parse(body.Text);

        if (items.IsFailure)
        {
            return items.ToFailure<ShioajiHistoryPage<T>>();
        }

        var rows = items.GetValueOrThrow();

        return new ShioajiHistoryPage<T>
        {
            Items = rows,
            ResponseBytes = body.Bytes,
            PossiblyTrafficExhausted = rows.Count == 0 && (_trafficMeter?.IsPossiblyExhausted() ?? true),
        };
    }

    private static Error? ValidateContract(ShioajiContractRef contract, out string securityType)
    {
        if (!ShioajiWire.SecurityType.TryGetWire(contract.SecurityType, out securityType))
        {
            return ShioajiErrors.InvalidArgument("必須指定商品類別。A security type must be given.");
        }

        if (string.IsNullOrWhiteSpace(contract.Exchange))
        {
            return ShioajiErrors.BlankArgument(nameof(contract.Exchange));
        }

        if (string.IsNullOrWhiteSpace(contract.Code))
        {
            return ShioajiErrors.BlankArgument(nameof(contract.Code));
        }

        return null;
    }

    private static Error? ValidateTicksQuery(ShioajiTicksQuery query)
    {
        switch (query.Type)
        {
            case ShioajiTicksQueryType.AllDay:
                return null;
            case ShioajiTicksQueryType.RangeTime:
                if (query.TimeStart is not { } start || query.TimeEnd is not { } end)
                {
                    return ShioajiErrors.InvalidArgument("RangeTime 必須同時帶 TimeStart 與 TimeEnd。RangeTime needs both TimeStart and TimeEnd.");
                }

                return start >= end
                    ? ShioajiErrors.InvalidArgument("TimeStart 必須早於 TimeEnd。TimeStart must be earlier than TimeEnd.")
                    : null;
            case ShioajiTicksQueryType.LastCount:
                return query.LastCount is >= 1
                    ? null
                    : ShioajiErrors.InvalidArgument("LastCount 必須至少為 1。LastCount must be at least 1.");
            default:
                return ShioajiErrors.InvalidArgument("必須指定逐筆查詢方式。A tick query type must be given.");
        }
    }

    /// <summary>
    /// 組出 <c>{"contracts":[…]</c> 本文;任一檔不合法時 <paramref name="invalid"/> 帶第一個錯誤,回傳值無意義。
    /// Builds the <c>{"contracts":[…]}</c> body; when any contract is invalid <paramref name="invalid"/> carries the
    /// first error and the return value is meaningless.
    /// </summary>
    private static string BuildContractListBody(
        IReadOnlyList<ShioajiContractRef> contracts,
        bool requireStock,
        out Error? invalid)
    {
        var wires = new string[contracts.Count];

        for (var index = 0; index < contracts.Count; index++)
        {
            var contract = contracts[index];

            if (contract is null)
            {
                invalid = ShioajiErrors.InvalidArgument(
                    string.Create(CultureInfo.InvariantCulture, $"商品清單的第 {index} 項是 null。Item {index} of the contract list is null."));
                return string.Empty;
            }

            invalid = ValidateContract(contract, out wires[index]);

            if (invalid is not null)
            {
                return string.Empty;
            }

            if (requireStock && contract.SecurityType != ShioajiSecurityType.Stock)
            {
                invalid = ShioajiErrors.InvalidArgument(
                    string.Create(CultureInfo.InvariantCulture, $"這個查詢只支援股票,第 {index} 項({contract.Code})不是股票。This query supports stocks only, and item {index} ({contract.Code}) is not a stock."));
                return string.Empty;
            }
        }

        invalid = null;

        return ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartArray("contracts");

            for (var index = 0; index < contracts.Count; index++)
            {
                WriteContract(writer, propertyName: null, contracts[index], wires[index]);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private static void WriteContract(Utf8JsonWriter writer, string? propertyName, ShioajiContractRef contract, string securityType)
    {
        if (propertyName is null)
        {
            writer.WriteStartObject();
        }
        else
        {
            writer.WriteStartObject(propertyName);
        }

        writer.WriteString("security_type", securityType);
        writer.WriteString("exchange", contract.Exchange.Trim());
        writer.WriteString("code", contract.Code.Trim());
        writer.WriteEndObject();
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatTime(TimeOnly time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
