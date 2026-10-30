using System.Text.Json;

namespace Ozakboy.TradeKit.Shioaji.History;

/// <summary>
/// 一次歷史資料 / 查詢的結果:資料列、這次回應吃了多少位元組,以及空回應時「是不是流量用完了」的提示。
/// The result of one historical-data or query call: the rows, how many bytes the response cost, and, for an empty
/// response, a hint on whether the traffic allowance may be spent.
/// </summary>
/// <typeparam name="T">資料列的型別。The type of a row.</typeparam>
/// <remarks>
/// <para>
/// <b>空回應不等於沒有資料。</b>券商每日流量超量時行情查詢<b>不報錯、只回空值</b>。拿到零列時,呼叫端要看
/// <see cref="PossiblyTrafficExhausted"/>:為 <see langword="true"/> 就不要把這段當成「那天沒有行情」存進資料庫,
/// 先用 <see cref="Auth.ShioajiAuthClient.GetUsageAsync"/> 確認剩餘流量。
/// <b>An empty response is not proof of no data.</b> Over the daily traffic allowance the broker's market queries
/// <b>do not fail, they answer with empty values</b>. On zero rows the caller should look at
/// <see cref="PossiblyTrafficExhausted"/>: when it is <see langword="true"/>, do not store the span as "no market
/// that day" and first check the remaining traffic with <see cref="Auth.ShioajiAuthClient.GetUsageAsync"/>.
/// </para>
/// </remarks>
public sealed record ShioajiHistoryPage<T>
{
    /// <summary>
    /// 資料列。空清單是成功,見類別說明。
    /// The rows. An empty list is a success; see the type remarks.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// 這次回應的位元組數(回應的 <c>Content-Length</c>,沒有時是實際讀到的 UTF-8 位元組數)。
    /// The size of this response in bytes: its <c>Content-Length</c>, or the UTF-8 bytes actually read when there is
    /// none.
    /// </summary>
    public required long ResponseBytes { get; init; }

    /// <summary>
    /// 是否沒有任何資料列。
    /// Whether there are no rows at all.
    /// </summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// 空回應時,每日流量可能已經用完。<see cref="IsEmpty"/> 為 <see langword="false"/> 時一定是 <see langword="false"/>。
    /// 為空時:今天還沒用 <c>GetUsageAsync</c> 查過 server 的用量就無從排除,所以是 <see langword="true"/>;查過的話,
    /// 看「server 回報的剩餘量減去之後本地又累計的位元組」是否已經不大於 0。
    /// Whether, on an empty response, the daily traffic allowance may be spent. Always <see langword="false"/>
    /// when <see cref="IsEmpty"/> is <see langword="false"/>. When empty: with no usage fetched from the server
    /// today nothing can be ruled out, so <see langword="true"/>; with one, whether the server's remainder minus
    /// what was accumulated locally since is no longer above zero.
    /// </summary>
    public required bool PossiblyTrafficExhausted { get; init; }
}

/// <summary>
/// 一根 1 分 K(<c>POST /api/v1/data/kbars</c> 的一列)。
/// One 1-minute bar, a row of <c>POST /api/v1/data/kbars</c>.
/// </summary>
public sealed record ShioajiKbar
{
    /// <summary>
    /// 這根 K 棒的時間(UTC)。回應裡不帶時區的字串視為台北牆上時間,固定減 8 小時。
    /// 注意:券商給的是該分鐘的<b>結束</b>時刻(2026-10-31 實測 2330:每個交易日 09:01 至 13:30 共 266 根,
    /// 09:01 含開盤集合競價、13:30 含收盤集合競價);套件原樣保留,要開盤時刻請自行減 1 分鐘。
    /// The bar's time in UTC. A string without an offset in the response is Taipei wall-clock time and 8 hours are
    /// subtracted. Note: the broker gives the <b>end</b> of the minute (measured 2026-10-31 for 2330: 09:01 to 13:30,
    /// 266 bars per trading day; 09:01 holds the opening call auction and 13:30 holds the closing one). The package
    /// keeps it as is. Subtract 1 minute to get the open time.
    /// </summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>
    /// 開盤價。
    /// The open.
    /// </summary>
    public required decimal Open { get; init; }

    /// <summary>
    /// 最高價。
    /// The high.
    /// </summary>
    public required decimal High { get; init; }

    /// <summary>
    /// 最低價。
    /// The low.
    /// </summary>
    public required decimal Low { get; init; }

    /// <summary>
    /// 收盤價。
    /// The close.
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// 成交量。
    /// The volume.
    /// </summary>
    public required decimal Volume { get; init; }

    /// <summary>
    /// 成交金額。
    /// The turnover.
    /// </summary>
    public required decimal Amount { get; init; }
}

/// <summary>
/// 一筆逐筆成交(<c>POST /api/v1/data/ticks</c> 的一列)。
/// One tick, a row of <c>POST /api/v1/data/ticks</c>.
/// </summary>
public sealed record ShioajiTick
{
    /// <summary>
    /// 成交時間(UTC),轉換規則同 <see cref="ShioajiKbar.Time"/>。
    /// The trade time in UTC, converted as <see cref="ShioajiKbar.Time"/> is.
    /// </summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>
    /// 成交價。
    /// The trade price.
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// 成交量。
    /// The trade volume.
    /// </summary>
    public required decimal Volume { get; init; }

    /// <summary>
    /// 成交當下的買一價;回應沒給時為 <see langword="null"/>。
    /// The best bid at the time of the trade; <see langword="null"/> when the response gives none.
    /// </summary>
    public required decimal? BidPrice { get; init; }

    /// <summary>
    /// 成交當下的買一量;回應沒給時為 <see langword="null"/>。
    /// The best bid size at the time of the trade; <see langword="null"/> when the response gives none.
    /// </summary>
    public required decimal? BidVolume { get; init; }

    /// <summary>
    /// 成交當下的賣一價;回應沒給時為 <see langword="null"/>。
    /// The best ask at the time of the trade; <see langword="null"/> when the response gives none.
    /// </summary>
    public required decimal? AskPrice { get; init; }

    /// <summary>
    /// 成交當下的賣一量;回應沒給時為 <see langword="null"/>。
    /// The best ask size at the time of the trade; <see langword="null"/> when the response gives none.
    /// </summary>
    public required decimal? AskVolume { get; init; }

    /// <summary>
    /// 內外盤:1 外盤、2 內盤、0 無法判定(同串流的 <c>tick_type</c>);回應沒給時為 <see langword="null"/>。
    /// Which side hit: 1 outer, 2 inner, 0 undetermined (as the stream's <c>tick_type</c>); <see langword="null"/>
    /// when the response gives none.
    /// </summary>
    public required int? TickType { get; init; }
}

/// <summary>
/// 一檔商品的快照(<c>POST /api/v1/data/snapshots</c> 的一筆)。
/// One contract's snapshot, an entry of <c>POST /api/v1/data/snapshots</c>.
/// </summary>
/// <remarks>
/// 只有 <see cref="Code"/> 與 <see cref="Close"/> 是必要欄位。欄位名取自 Python SDK 的 <c>Snapshot</c>,
/// <b>未在 HTTP 上驗證</b>;沒有對映成屬性的欄位在 <see cref="Raw"/>。
/// Only <see cref="Code"/> and <see cref="Close"/> are required. The field names come from the Python SDK's
/// <c>Snapshot</c> and are <b>unverified over HTTP</b>; fields not mapped to a property are kept in
/// <see cref="Raw"/>.
/// </remarks>
public sealed record ShioajiSnapshot
{
    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 交易所代號;回應沒給時為 <see langword="null"/>。
    /// The exchange code; <see langword="null"/> when the response gives none.
    /// </summary>
    public required string? Exchange { get; init; }

    /// <summary>
    /// 快照時間(UTC);回應沒給或讀不懂時為 <see langword="null"/>。
    /// The snapshot time in UTC; <see langword="null"/> when absent or unreadable.
    /// </summary>
    public required DateTimeOffset? Time { get; init; }

    /// <summary>
    /// 開盤價。
    /// The open.
    /// </summary>
    public required decimal? Open { get; init; }

    /// <summary>
    /// 最高價。
    /// The high.
    /// </summary>
    public required decimal? High { get; init; }

    /// <summary>
    /// 最低價。
    /// The low.
    /// </summary>
    public required decimal? Low { get; init; }

    /// <summary>
    /// 最新成交價。
    /// The last trade price.
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// 內外盤(同 <see cref="ShioajiTick.TickType"/>)。
    /// Which side hit, as <see cref="ShioajiTick.TickType"/>.
    /// </summary>
    public required int? TickType { get; init; }

    /// <summary>
    /// 漲跌價。
    /// The price change.
    /// </summary>
    public required decimal? ChangePrice { get; init; }

    /// <summary>
    /// 漲跌幅(百分比)。
    /// The percentage change.
    /// </summary>
    public required decimal? ChangeRate { get; init; }

    /// <summary>
    /// 漲跌類型:1 漲停、2 漲、3 平、4 跌、5 跌停(同串流的 <c>chg_type</c>)。
    /// The change type: 1 limit-up, 2 up, 3 flat, 4 down, 5 limit-down (as the stream's <c>chg_type</c>).
    /// </summary>
    public required int? ChangeType { get; init; }

    /// <summary>
    /// 均價。
    /// The average price.
    /// </summary>
    public required decimal? AveragePrice { get; init; }

    /// <summary>
    /// 最新一筆的成交量。
    /// The volume of the last trade.
    /// </summary>
    public required decimal? Volume { get; init; }

    /// <summary>
    /// 總成交量。
    /// The total volume.
    /// </summary>
    public required decimal? TotalVolume { get; init; }

    /// <summary>
    /// 最新一筆的成交金額。
    /// The turnover of the last trade.
    /// </summary>
    public required decimal? Amount { get; init; }

    /// <summary>
    /// 總成交金額。
    /// The total turnover.
    /// </summary>
    public required decimal? TotalAmount { get; init; }

    /// <summary>
    /// 昨日成交量。
    /// Yesterday's volume.
    /// </summary>
    public required decimal? YesterdayVolume { get; init; }

    /// <summary>
    /// 量比。
    /// The volume ratio.
    /// </summary>
    public required decimal? VolumeRatio { get; init; }

    /// <summary>
    /// 買一價。
    /// The best bid.
    /// </summary>
    public required decimal? BuyPrice { get; init; }

    /// <summary>
    /// 買一量。
    /// The best bid size.
    /// </summary>
    public required decimal? BuyVolume { get; init; }

    /// <summary>
    /// 賣一價。
    /// The best ask.
    /// </summary>
    public required decimal? SellPrice { get; init; }

    /// <summary>
    /// 賣一量。
    /// The best ask size.
    /// </summary>
    public required decimal? SellVolume { get; init; }

    /// <summary>
    /// 這一筆的完整原始 JSON 物件。
    /// The complete raw JSON object of this entry.
    /// </summary>
    public required JsonElement Raw { get; init; }
}

/// <summary>
/// 全市場日 OHLCV 的一筆。
/// One entry of the all-market daily OHLCV.
/// </summary>
/// <remarks>
/// <b>端點路徑與回應形狀都未驗證,待實機 OpenAPI 確認。</b>這裡假設回應是物件陣列,每筆至少有
/// <c>code</c>、<c>open</c>、<c>high</c>、<c>low</c>、<c>close</c>、<c>volume</c>;多餘的欄位在 <see cref="Raw"/>。
/// <b>Both the endpoint path and the response shape are unverified and await confirmation against a real server's
/// OpenAPI.</b> The response is assumed to be an array of objects, each with at least <c>code</c>, <c>open</c>,
/// <c>high</c>, <c>low</c>, <c>close</c> and <c>volume</c>; any further fields are kept in <see cref="Raw"/>.
/// </remarks>
public sealed record ShioajiDailyQuote
{
    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 開盤價。
    /// The open.
    /// </summary>
    public required decimal Open { get; init; }

    /// <summary>
    /// 最高價。
    /// The high.
    /// </summary>
    public required decimal High { get; init; }

    /// <summary>
    /// 最低價。
    /// The low.
    /// </summary>
    public required decimal Low { get; init; }

    /// <summary>
    /// 收盤價。
    /// The close.
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// 成交量。
    /// The volume.
    /// </summary>
    public required decimal Volume { get; init; }

    /// <summary>
    /// 這一筆的完整原始 JSON 物件(含沒有對映成屬性的欄位)。
    /// The complete raw JSON object of this entry, including the fields not mapped to a property.
    /// </summary>
    public required JsonElement Raw { get; init; }
}

/// <summary>
/// 排行掃描的一筆(<c>POST /api/v1/data/scanner</c>)。
/// One entry of a ranking scan (<c>POST /api/v1/data/scanner</c>).
/// </summary>
/// <remarks>
/// 只有 <see cref="Code"/> 是必要欄位。欄位名取自 Python SDK 的 <c>ScannerItem</c>,<b>未在 HTTP 上驗證</b>;
/// 其餘欄位(委託筆數等)在 <see cref="Raw"/>。
/// Only <see cref="Code"/> is required. The field names come from the Python SDK's <c>ScannerItem</c> and are
/// <b>unverified over HTTP</b>; the rest (order counts and so on) are in <see cref="Raw"/>.
/// </remarks>
public sealed record ShioajiScannerItem
{
    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 商品名稱。
    /// The contract name.
    /// </summary>
    public required string? Name { get; init; }

    /// <summary>
    /// 資料時間(UTC);沒給或讀不懂時為 <see langword="null"/>。
    /// The data time in UTC; <see langword="null"/> when absent or unreadable.
    /// </summary>
    public required DateTimeOffset? Time { get; init; }

    /// <summary>
    /// 開盤價。
    /// The open.
    /// </summary>
    public required decimal? Open { get; init; }

    /// <summary>
    /// 最高價。
    /// The high.
    /// </summary>
    public required decimal? High { get; init; }

    /// <summary>
    /// 最低價。
    /// The low.
    /// </summary>
    public required decimal? Low { get; init; }

    /// <summary>
    /// 最新成交價。
    /// The last trade price.
    /// </summary>
    public required decimal? Close { get; init; }

    /// <summary>
    /// 漲跌價。
    /// The price change.
    /// </summary>
    public required decimal? ChangePrice { get; init; }

    /// <summary>
    /// 漲跌幅(百分比)。
    /// The percentage change.
    /// </summary>
    public required decimal? ChangeRate { get; init; }

    /// <summary>
    /// 均價。
    /// The average price.
    /// </summary>
    public required decimal? AveragePrice { get; init; }

    /// <summary>
    /// 最新一筆的成交量。
    /// The volume of the last trade.
    /// </summary>
    public required decimal? Volume { get; init; }

    /// <summary>
    /// 總成交量。
    /// The total volume.
    /// </summary>
    public required decimal? TotalVolume { get; init; }

    /// <summary>
    /// 總成交金額。
    /// The total turnover.
    /// </summary>
    public required decimal? TotalAmount { get; init; }

    /// <summary>
    /// 量比。
    /// The volume ratio.
    /// </summary>
    public required decimal? VolumeRatio { get; init; }

    /// <summary>
    /// 這一筆的完整原始 JSON 物件。
    /// The complete raw JSON object of this entry.
    /// </summary>
    public required JsonElement Raw { get; init; }
}

/// <summary>
/// 一檔股票的借券券源(<c>POST /api/v1/data/short_stock_sources</c>)。
/// One stock's short-selling source (<c>POST /api/v1/data/short_stock_sources</c>).
/// </summary>
/// <remarks>
/// 只有 <see cref="Code"/> 是必要欄位。欄位名取自 Python SDK 的 <c>ShortStockSource</c>,<b>未在 HTTP 上驗證</b>。
/// Only <see cref="Code"/> is required. The field names come from the Python SDK's <c>ShortStockSource</c> and are
/// <b>unverified over HTTP</b>.
/// </remarks>
public sealed record ShioajiShortStockSource
{
    /// <summary>
    /// 股票代碼。
    /// The stock code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 可借券的券源數量(<c>short_stock_source</c>);沒給時為 <see langword="null"/>。
    /// The available lendable quantity (<c>short_stock_source</c>); <see langword="null"/> when absent.
    /// </summary>
    public required decimal? Source { get; init; }

    /// <summary>
    /// 券源的更新時間(UTC);沒給或讀不懂時為 <see langword="null"/>。
    /// When the source was updated, in UTC; <see langword="null"/> when absent or unreadable.
    /// </summary>
    public required DateTimeOffset? UpdateTime { get; init; }

    /// <summary>
    /// 這一筆的完整原始 JSON 物件。
    /// The complete raw JSON object of this entry.
    /// </summary>
    public required JsonElement Raw { get; init; }
}

/// <summary>
/// 一檔股票的資券餘額(<c>POST /api/v1/data/credit_enquire</c>)。
/// One stock's margin and short balance (<c>POST /api/v1/data/credit_enquire</c>).
/// </summary>
/// <remarks>
/// 只有 <see cref="Code"/> 是必要欄位(讀 <c>stock_id</c>,沒有時讀 <c>code</c>)。欄位名取自 Python SDK 的
/// <c>CreditEnquire</c>,<b>未在 HTTP 上驗證</b>。
/// Only <see cref="Code"/> is required (read from <c>stock_id</c>, or <c>code</c> when that is absent). The field
/// names come from the Python SDK's <c>CreditEnquire</c> and are <b>unverified over HTTP</b>.
/// </remarks>
public sealed record ShioajiCreditEnquire
{
    /// <summary>
    /// 股票代碼。
    /// The stock code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 資料更新時間(UTC);沒給或讀不懂時為 <see langword="null"/>。
    /// When the data was updated, in UTC; <see langword="null"/> when absent or unreadable.
    /// </summary>
    public required DateTimeOffset? UpdateTime { get; init; }

    /// <summary>
    /// 融資融券系統別(<c>system</c>);沒給時為 <see langword="null"/>。
    /// The margin system (<c>system</c>); <see langword="null"/> when absent.
    /// </summary>
    public required string? MarginSystem { get; init; }

    /// <summary>
    /// 融資餘額單位(<c>margin_unit</c>);沒給時為 <see langword="null"/>。
    /// The margin balance in units (<c>margin_unit</c>); <see langword="null"/> when absent.
    /// </summary>
    public required decimal? MarginUnit { get; init; }

    /// <summary>
    /// 融券餘額單位(<c>short_unit</c>);沒給時為 <see langword="null"/>。
    /// The short balance in units (<c>short_unit</c>); <see langword="null"/> when absent.
    /// </summary>
    public required decimal? ShortUnit { get; init; }

    /// <summary>
    /// 這一筆的完整原始 JSON 物件。
    /// The complete raw JSON object of this entry.
    /// </summary>
    public required JsonElement Raw { get; init; }
}

/// <summary>
/// 處置股或注意股清單的一筆。
/// One entry of the disposition or attention list.
/// </summary>
/// <remarks>
/// <b>回應形狀未驗證。</b>文件沒寫這兩個端點回什麼,這裡同時接受「物件陣列」與「欄位陣列」(Python SDK 的做法),
/// 只要求有 <c>code</c>;其餘內容(公告日、處置期間等)都在 <see cref="Raw"/>。
/// <b>The response shape is unverified.</b> The documentation does not say what these two endpoints answer; both an
/// array of objects and a column-array object (the Python SDK's way) are accepted, with only <c>code</c> required;
/// everything else (announcement date, disposition period and so on) is in <see cref="Raw"/>.
/// </remarks>
public sealed record ShioajiRegulatoryItem
{
    /// <summary>
    /// 是處置股還是注意股。
    /// Whether this is a disposition or an attention entry.
    /// </summary>
    public required ShioajiRegulatoryKind Kind { get; init; }

    /// <summary>
    /// 股票代碼。
    /// The stock code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 股票名稱;沒給時為 <see langword="null"/>。
    /// The stock name; <see langword="null"/> when absent.
    /// </summary>
    public required string? Name { get; init; }

    /// <summary>
    /// 這一筆的完整原始 JSON 物件。
    /// The complete raw JSON object of this entry.
    /// </summary>
    public required JsonElement Raw { get; init; }
}
