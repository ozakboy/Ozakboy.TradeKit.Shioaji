using Ozakboy.TradeKit.Shioaji.Orders;

namespace Ozakboy.TradeKit.Shioaji.Portfolio;

// 本檔所有模型的欄位都依官方文件(https://sinotrade.github.io/llms-full.txt)的帳務章節。
// 金額、價格、數量、損益一律 decimal;文件寫成整數的欄位也用 decimal,免得實際回小數時被截斷。
// 文件沒有說「一定會有」的欄位一律是可空型別:缺漏時是 null,絕不補 0 —— 0 會被讀成「損益是零」。
// 這些模型尚未對真 server 實測,見 ShioajiPortfolioClient 的說明。
// Every field in this file follows the portfolio chapter of the official documentation
// (https://sinotrade.github.io/llms-full.txt). Money, prices, quantities and profit and loss are always decimal;
// fields the documentation types as integers are decimal too, so a fractional value is never truncated. Fields the
// documentation does not promise are nullable: absent means null and is never filled with zero, because zero reads
// as "no profit and no loss". These models have not been measured against a real server; see the remarks of
// ShioajiPortfolioClient.

/// <summary>
/// 帳務查詢要指定的帳號。省略(傳 <see langword="null"/>)時 server 用預設帳號。
/// The account a portfolio query targets. Omitted (<see langword="null"/>), the server uses its default account.
/// </summary>
public sealed record ShioajiPortfolioAccount
{
    /// <summary>
    /// 券商代號;與 <see cref="AccountId"/> 必須同時提供或同時省略。
    /// The broker id; it must be given together with <see cref="AccountId"/> or omitted together with it.
    /// </summary>
    public string? BrokerId { get; init; }

    /// <summary>
    /// 帳號;與 <see cref="BrokerId"/> 必須同時提供或同時省略。
    /// The account id; it must be given together with <see cref="BrokerId"/> or omitted together with it.
    /// </summary>
    public string? AccountId { get; init; }

    /// <summary>
    /// 身分證字號(文件的 <c>person_id</c>,選填)。這是個資,不會出現在日誌或錯誤訊息,但請求本文會帶它。
    /// The national id (the documentation's <c>person_id</c>, optional). It is personal data: it appears in no log or
    /// error message, but the request body carries it.
    /// </summary>
    public string? PersonId { get; init; }
}

/// <summary>
/// 證券庫存的一列(未實現損益)。
/// One stock position row, with unrealized profit and loss.
/// </summary>
public sealed record ShioajiStockPosition
{
    /// <summary>
    /// 庫存識別碼,拿去查庫存明細(<see cref="ShioajiPortfolioClient.ListStockPositionDetailsAsync"/>)。
    /// The position id, used to look up the position details.
    /// </summary>
    public long? Id { get; init; }

    /// <summary>
    /// 股票代碼。
    /// The stock code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 部位方向(買進為多方、賣出為空方)。
    /// The side of the position.
    /// </summary>
    public ShioajiAction Direction { get; init; }

    /// <summary>
    /// 數量(整股為張、零股為股,依請求的單位)。
    /// The quantity: lots or shares, following the requested unit.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 成本價(<c>price</c>)。
    /// The cost price (<c>price</c>).
    /// </summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// 現價(<c>last_price</c>)。
    /// The last price (<c>last_price</c>).
    /// </summary>
    public decimal? LastPrice { get; init; }

    /// <summary>
    /// 未實現損益(<c>pnl</c>)。
    /// The unrealized profit and loss (<c>pnl</c>).
    /// </summary>
    public decimal? Pnl { get; init; }

    /// <summary>
    /// 昨日庫存數量(<c>yd_quantity</c>)。
    /// The quantity held yesterday (<c>yd_quantity</c>).
    /// </summary>
    public decimal? YdQuantity { get; init; }

    /// <summary>
    /// 委託條件(<c>cond</c>)。
    /// The order condition (<c>cond</c>).
    /// </summary>
    public ShioajiPositionCondition Condition { get; init; }

    /// <summary>
    /// 融資金額(<c>margin_purchase_amount</c>)。
    /// The margin purchase amount (<c>margin_purchase_amount</c>).
    /// </summary>
    public decimal? MarginPurchaseAmount { get; init; }

    /// <summary>
    /// 擔保品(<c>collateral</c>)。
    /// The collateral (<c>collateral</c>).
    /// </summary>
    public decimal? Collateral { get; init; }

    /// <summary>
    /// 融券保證金(<c>short_sale_margin</c>)。
    /// The short-sale margin (<c>short_sale_margin</c>).
    /// </summary>
    public decimal? ShortSaleMargin { get; init; }

    /// <summary>
    /// 利息(<c>interest</c>)。
    /// The interest (<c>interest</c>).
    /// </summary>
    public decimal? Interest { get; init; }
}

/// <summary>
/// 期貨(含選擇權)庫存的一列。
/// One futures or options position row.
/// </summary>
public sealed record ShioajiFuturesPosition
{
    /// <summary>
    /// 庫存識別碼,拿去查庫存明細。
    /// The position id, used to look up the position details.
    /// </summary>
    public long? Id { get; init; }

    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 部位方向。
    /// The side of the position.
    /// </summary>
    public ShioajiAction Direction { get; init; }

    /// <summary>
    /// 口數。
    /// The number of contracts.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 成本價。
    /// The cost price.
    /// </summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// 現價。
    /// The last price.
    /// </summary>
    public decimal? LastPrice { get; init; }

    /// <summary>
    /// 未實現損益。
    /// The unrealized profit and loss.
    /// </summary>
    public decimal? Pnl { get; init; }
}

/// <summary>
/// 證券庫存明細的一列。
/// One stock position detail row.
/// </summary>
public sealed record ShioajiStockPositionDetail
{
    /// <summary>
    /// 交易日期(台北日曆日)。
    /// The trade date, a Taipei calendar day.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// 股票代碼。
    /// The stock code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 數量。
    /// The quantity.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 成本價。
    /// The cost price.
    /// </summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// 現價。
    /// The last price.
    /// </summary>
    public decimal? LastPrice { get; init; }

    /// <summary>
    /// 委託書號(<c>dseq</c>)。
    /// The order document sequence (<c>dseq</c>).
    /// </summary>
    public string? Dseq { get; init; }

    /// <summary>
    /// 部位方向。
    /// The side of the position.
    /// </summary>
    public ShioajiAction Direction { get; init; }

    /// <summary>
    /// 未實現損益。
    /// The unrealized profit and loss.
    /// </summary>
    public decimal? Pnl { get; init; }

    /// <summary>
    /// 幣別,例如 <c>TWD</c>。文件的列舉是 TWD、USD、HKD、EUR、CAD、BAS,這裡保留原字串。
    /// The currency, such as <c>TWD</c>. The documentation lists TWD, USD, HKD, EUR, CAD and BAS; the raw string is
    /// kept.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// 手續費。
    /// The fee.
    /// </summary>
    public decimal? Fee { get; init; }

    /// <summary>
    /// 委託條件。
    /// The order condition.
    /// </summary>
    public ShioajiPositionCondition Condition { get; init; }

    /// <summary>
    /// 除息金額(<c>ex_dividends</c>)。
    /// The ex-dividend amount (<c>ex_dividends</c>).
    /// </summary>
    public decimal? ExDividends { get; init; }

    /// <summary>
    /// 利息。
    /// The interest.
    /// </summary>
    public decimal? Interest { get; init; }

    /// <summary>
    /// 融資金額(<c>margintrading_amt</c>)。
    /// The margin purchase amount (<c>margintrading_amt</c>).
    /// </summary>
    public decimal? MarginTradingAmount { get; init; }

    /// <summary>
    /// 擔保品。
    /// The collateral.
    /// </summary>
    public decimal? Collateral { get; init; }
}

/// <summary>
/// 期貨庫存明細的一列。
/// One futures position detail row.
/// </summary>
public sealed record ShioajiFuturesPositionDetail
{
    /// <summary>
    /// 交易日期(台北日曆日)。
    /// The trade date, a Taipei calendar day.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 口數。
    /// The number of contracts.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 成本價。
    /// The cost price.
    /// </summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// 現價。
    /// The last price.
    /// </summary>
    public decimal? LastPrice { get; init; }

    /// <summary>
    /// 委託書號(<c>dseq</c>)。
    /// The order document sequence (<c>dseq</c>).
    /// </summary>
    public string? Dseq { get; init; }

    /// <summary>
    /// 部位方向。
    /// The side of the position.
    /// </summary>
    public ShioajiAction Direction { get; init; }

    /// <summary>
    /// 未實現損益。
    /// The unrealized profit and loss.
    /// </summary>
    public decimal? Pnl { get; init; }

    /// <summary>
    /// 幣別。
    /// The currency.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// 手續費。
    /// The fee.
    /// </summary>
    public decimal? Fee { get; init; }

    /// <summary>
    /// 建倉口數(<c>entry_quantity</c>)。
    /// The quantity at entry (<c>entry_quantity</c>).
    /// </summary>
    public decimal? EntryQuantity { get; init; }
}

/// <summary>
/// 證券已實現損益的一列。
/// One stock realized profit and loss row.
/// </summary>
public sealed record ShioajiStockProfitLoss
{
    /// <summary>
    /// 識別碼,拿去查損益明細。
    /// The id, used to look up the details.
    /// </summary>
    public long? Id { get; init; }

    /// <summary>
    /// 股票代碼。
    /// The stock code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 數量。
    /// The quantity.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 已實現損益。
    /// The realized profit and loss.
    /// </summary>
    public decimal? Pnl { get; init; }

    /// <summary>
    /// 交易日期(台北日曆日)。
    /// The trade date, a Taipei calendar day.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// 委託書號(<c>dseq</c>)。
    /// The order document sequence (<c>dseq</c>).
    /// </summary>
    public string? Dseq { get; init; }

    /// <summary>
    /// 成交價。
    /// The trade price.
    /// </summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// 損益率(<c>pr_ratio</c>)。單位(百分比或小數)文件沒寫,原值照給。
    /// The profit-and-loss ratio (<c>pr_ratio</c>). The documentation does not say whether it is a percentage or a
    /// fraction; the raw value is handed over.
    /// </summary>
    public decimal? PrRatio { get; init; }

    /// <summary>
    /// 委託條件。
    /// The order condition.
    /// </summary>
    public ShioajiPositionCondition Condition { get; init; }

    /// <summary>
    /// 委託序號(<c>seqno</c>)。
    /// The order sequence number (<c>seqno</c>).
    /// </summary>
    public string? Seqno { get; init; }
}

/// <summary>
/// 期貨已實現損益的一列。
/// One futures realized profit and loss row.
/// </summary>
public sealed record ShioajiFuturesProfitLoss
{
    /// <summary>
    /// 識別碼,拿去查損益明細。
    /// The id, used to look up the details.
    /// </summary>
    public long? Id { get; init; }

    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 口數。
    /// The number of contracts.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 已實現損益。
    /// The realized profit and loss.
    /// </summary>
    public decimal? Pnl { get; init; }

    /// <summary>
    /// 交易日期(台北日曆日)。
    /// The trade date, a Taipei calendar day.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// 建倉價(<c>entry_price</c>)。
    /// The entry price (<c>entry_price</c>).
    /// </summary>
    public decimal? EntryPrice { get; init; }

    /// <summary>
    /// 平倉價(<c>cover_price</c>)。
    /// The cover price (<c>cover_price</c>).
    /// </summary>
    public decimal? CoverPrice { get; init; }

    /// <summary>
    /// 稅。
    /// The tax.
    /// </summary>
    public decimal? Tax { get; init; }

    /// <summary>
    /// 手續費。
    /// The fee.
    /// </summary>
    public decimal? Fee { get; init; }

    /// <summary>
    /// 部位方向。
    /// The side of the position.
    /// </summary>
    public ShioajiAction Direction { get; init; }
}

/// <summary>
/// 證券已實現損益明細的一列。
/// One stock realized profit and loss detail row.
/// </summary>
public sealed record ShioajiStockProfitLossDetail
{
    /// <summary>
    /// 交易日期(台北日曆日)。
    /// The trade date, a Taipei calendar day.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// 股票代碼。
    /// The stock code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 數量。
    /// The quantity.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 委託書號(<c>dseq</c>)。
    /// The order document sequence (<c>dseq</c>).
    /// </summary>
    public string? Dseq { get; init; }

    /// <summary>
    /// 手續費。
    /// The fee.
    /// </summary>
    public decimal? Fee { get; init; }

    /// <summary>
    /// 證交稅。
    /// The securities transaction tax.
    /// </summary>
    public decimal? Tax { get; init; }

    /// <summary>
    /// 幣別。
    /// The currency.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// 價格。
    /// The price.
    /// </summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// 成本(<c>cost</c>)。
    /// The cost (<c>cost</c>).
    /// </summary>
    public decimal? Cost { get; init; }

    /// <summary>
    /// 償還融資金額(<c>rep_margintrading_amt</c>)。
    /// The margin purchase amount repaid (<c>rep_margintrading_amt</c>).
    /// </summary>
    public decimal? RepMarginTradingAmount { get; init; }

    /// <summary>
    /// 償還擔保品(<c>rep_collateral</c>)。
    /// The collateral returned (<c>rep_collateral</c>).
    /// </summary>
    public decimal? RepCollateral { get; init; }

    /// <summary>
    /// 償還保證金(<c>rep_margin</c>)。
    /// The margin returned (<c>rep_margin</c>).
    /// </summary>
    public decimal? RepMargin { get; init; }

    /// <summary>
    /// 融券費(<c>shortselling_fee</c>)。
    /// The short-selling fee (<c>shortselling_fee</c>).
    /// </summary>
    public decimal? ShortSellingFee { get; init; }

    /// <summary>
    /// 除息金額(<c>ex_dividend_amt</c>)。
    /// The ex-dividend amount (<c>ex_dividend_amt</c>).
    /// </summary>
    public decimal? ExDividendAmount { get; init; }

    /// <summary>
    /// 利息。
    /// The interest.
    /// </summary>
    public decimal? Interest { get; init; }

    /// <summary>
    /// 交易類型:一般或當沖(<c>trade_type</c>)。
    /// The trade type, ordinary or day trade (<c>trade_type</c>).
    /// </summary>
    public ShioajiProfitLossTradeType TradeType { get; init; }

    /// <summary>
    /// 委託條件。
    /// The order condition.
    /// </summary>
    public ShioajiPositionCondition Condition { get; init; }
}

/// <summary>
/// 期貨已實現損益明細的一列。
/// One futures realized profit and loss detail row.
/// </summary>
public sealed record ShioajiFuturesProfitLossDetail
{
    /// <summary>
    /// 交易日期(台北日曆日)。
    /// The trade date, a Taipei calendar day.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 口數。
    /// The number of contracts.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 委託書號(<c>dseq</c>)。
    /// The order document sequence (<c>dseq</c>).
    /// </summary>
    public string? Dseq { get; init; }

    /// <summary>
    /// 手續費。
    /// The fee.
    /// </summary>
    public decimal? Fee { get; init; }

    /// <summary>
    /// 稅。
    /// The tax.
    /// </summary>
    public decimal? Tax { get; init; }

    /// <summary>
    /// 幣別。
    /// The currency.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// 部位方向。
    /// The side of the position.
    /// </summary>
    public ShioajiAction Direction { get; init; }

    /// <summary>
    /// 建倉日期(<c>entry_date</c>)。
    /// The entry date (<c>entry_date</c>).
    /// </summary>
    public DateOnly? EntryDate { get; init; }

    /// <summary>
    /// 建倉口數(<c>entry_quantity</c>)。
    /// The quantity at entry (<c>entry_quantity</c>).
    /// </summary>
    public decimal? EntryQuantity { get; init; }

    /// <summary>
    /// 建倉委託序號(<c>entry_seqno</c>)。
    /// The entry order sequence number (<c>entry_seqno</c>).
    /// </summary>
    public string? EntrySeqno { get; init; }

    /// <summary>
    /// 建倉價。
    /// The entry price.
    /// </summary>
    public decimal? EntryPrice { get; init; }

    /// <summary>
    /// 平倉價。
    /// The cover price.
    /// </summary>
    public decimal? CoverPrice { get; init; }

    /// <summary>
    /// 已實現損益。
    /// The realized profit and loss.
    /// </summary>
    public decimal? Pnl { get; init; }
}

/// <summary>
/// 證券已實現損益彙總的一列(依商品)。
/// One per-contract row of the stock realized profit and loss summary.
/// </summary>
public sealed record ShioajiStockProfitLossSummaryItem
{
    /// <summary>
    /// 股票代碼。
    /// The stock code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 數量。
    /// The quantity.
    /// </summary>
    public decimal? Quantity { get; init; }

    /// <summary>
    /// 買進均價(<c>entry_price</c>)。
    /// The average entry price (<c>entry_price</c>).
    /// </summary>
    public decimal? EntryPrice { get; init; }

    /// <summary>
    /// 賣出均價(<c>cover_price</c>)。
    /// The average cover price (<c>cover_price</c>).
    /// </summary>
    public decimal? CoverPrice { get; init; }

    /// <summary>
    /// 已實現損益。
    /// The realized profit and loss.
    /// </summary>
    public decimal? Pnl { get; init; }

    /// <summary>
    /// 幣別。
    /// The currency.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// 建倉成本(<c>entry_cost</c>)。
    /// The entry cost (<c>entry_cost</c>).
    /// </summary>
    public decimal? EntryCost { get; init; }

    /// <summary>
    /// 平倉成本(<c>cover_cost</c>)。
    /// The cover cost (<c>cover_cost</c>).
    /// </summary>
    public decimal? CoverCost { get; init; }

    /// <summary>
    /// 買進成本(<c>buy_cost</c>)。
    /// The buy cost (<c>buy_cost</c>).
    /// </summary>
    public decimal? BuyCost { get; init; }

    /// <summary>
    /// 賣出成本(<c>sell_cost</c>)。
    /// The sell cost (<c>sell_cost</c>).
    /// </summary>
    public decimal? SellCost { get; init; }

    /// <summary>
    /// 損益率(<c>pr_ratio</c>)。
    /// The profit-and-loss ratio (<c>pr_ratio</c>).
    /// </summary>
    public decimal? PrRatio { get; init; }

    /// <summary>
    /// 委託條件。
    /// The order condition.
    /// </summary>
    public ShioajiPositionCondition Condition { get; init; }
}

/// <summary>
/// 期貨已實現損益彙總的一列(依商品)。
/// One per-contract row of the futures realized profit and loss summary.
/// </summary>
public sealed record ShioajiFuturesProfitLossSummaryItem
{
    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 口數。
    /// The number of contracts.
    /// </summary>
    public decimal? Quantity { get; init; }

    /// <summary>
    /// 建倉均價。
    /// The average entry price.
    /// </summary>
    public decimal? EntryPrice { get; init; }

    /// <summary>
    /// 平倉均價。
    /// The average cover price.
    /// </summary>
    public decimal? CoverPrice { get; init; }

    /// <summary>
    /// 已實現損益。
    /// The realized profit and loss.
    /// </summary>
    public decimal? Pnl { get; init; }

    /// <summary>
    /// 幣別。
    /// The currency.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// 部位方向。
    /// The side of the position.
    /// </summary>
    public ShioajiAction Direction { get; init; }

    /// <summary>
    /// 稅。
    /// The tax.
    /// </summary>
    public decimal? Tax { get; init; }

    /// <summary>
    /// 手續費。
    /// The fee.
    /// </summary>
    public decimal? Fee { get; init; }
}

/// <summary>
/// 已實現損益彙總的合計(文件的 <c>ProfitLossTotal</c>)。
/// The total of the realized profit and loss summary (the documentation's <c>ProfitLossTotal</c>).
/// </summary>
public sealed record ShioajiProfitLossTotal
{
    /// <summary>
    /// 建倉金額(<c>entry_amount</c>)。
    /// The entry amount (<c>entry_amount</c>).
    /// </summary>
    public decimal? EntryAmount { get; init; }

    /// <summary>
    /// 平倉金額(<c>cover_amount</c>)。
    /// The cover amount (<c>cover_amount</c>).
    /// </summary>
    public decimal? CoverAmount { get; init; }

    /// <summary>
    /// 總數量。
    /// The total quantity.
    /// </summary>
    public decimal? Quantity { get; init; }

    /// <summary>
    /// 買進成本(<c>buy_cost</c>)。
    /// The buy cost (<c>buy_cost</c>).
    /// </summary>
    public decimal? BuyCost { get; init; }

    /// <summary>
    /// 賣出成本(<c>sell_cost</c>)。
    /// The sell cost (<c>sell_cost</c>).
    /// </summary>
    public decimal? SellCost { get; init; }

    /// <summary>
    /// 總損益。
    /// The total profit and loss.
    /// </summary>
    public decimal? Pnl { get; init; }

    /// <summary>
    /// 損益率(<c>pr_ratio</c>)。
    /// The profit-and-loss ratio (<c>pr_ratio</c>).
    /// </summary>
    public decimal? PrRatio { get; init; }
}

/// <summary>
/// 已實現損益彙總:依商品的清單加合計。
/// The realized profit and loss summary: a per-contract list plus a total.
/// </summary>
/// <typeparam name="TItem">清單的列型別(證券或期貨)。The row type of the list, stock or futures.</typeparam>
public sealed record ShioajiProfitLossSummary<TItem>
{
    /// <summary>
    /// 依商品的彙總列。
    /// The per-contract rows.
    /// </summary>
    public required IReadOnlyList<TItem> Items { get; init; }

    /// <summary>
    /// 合計;回應沒有 <c>total</c> 時為 <see langword="null"/>。
    /// The total, or <see langword="null"/> when the response has no <c>total</c>.
    /// </summary>
    public ShioajiProfitLossTotal? Total { get; init; }
}

/// <summary>
/// 一筆交割款。
/// One settlement amount.
/// </summary>
public sealed record ShioajiSettlement
{
    /// <summary>
    /// 交割日(台北日曆日)。
    /// The settlement date, a Taipei calendar day.
    /// </summary>
    public required DateOnly Date { get; init; }

    /// <summary>
    /// 金額,正負號的意義(應收或應付)文件沒寫,原值照給。
    /// The amount. The documentation does not say what the sign means (receivable or payable); the raw value is
    /// handed over.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// T+n 的 n(文件的 <c>T</c>;<c>0</c> 是成交當日)。
    /// The n of T+n (the documentation's <c>T</c>; <c>0</c> is the trade day itself).
    /// </summary>
    public required int TPlus { get; init; }
}

/// <summary>
/// 證券交易額度。三組額度都是「上限、已用、可用」。
/// Stock trading limits: three sets of limit, used and available.
/// </summary>
public sealed record ShioajiTradingLimits
{
    /// <summary>
    /// 電子交易額度上限(<c>trading_limit</c>)。
    /// The electronic trading limit (<c>trading_limit</c>).
    /// </summary>
    public decimal? TradingLimit { get; init; }

    /// <summary>
    /// 電子交易已用額度(<c>trading_used</c>)。
    /// The electronic trading amount used (<c>trading_used</c>).
    /// </summary>
    public decimal? TradingUsed { get; init; }

    /// <summary>
    /// 電子交易可用額度(<c>trading_available</c>)。
    /// The electronic trading amount available (<c>trading_available</c>).
    /// </summary>
    public required decimal TradingAvailable { get; init; }

    /// <summary>
    /// 融資額度上限(<c>margin_limit</c>)。
    /// The margin purchase limit (<c>margin_limit</c>).
    /// </summary>
    public decimal? MarginLimit { get; init; }

    /// <summary>
    /// 融資已用額度(<c>margin_used</c>)。
    /// The margin purchase amount used (<c>margin_used</c>).
    /// </summary>
    public decimal? MarginUsed { get; init; }

    /// <summary>
    /// 融資可用額度(<c>margin_available</c>)。
    /// The margin purchase amount available (<c>margin_available</c>).
    /// </summary>
    public decimal? MarginAvailable { get; init; }

    /// <summary>
    /// 融券額度上限(<c>short_limit</c>)。
    /// The short-sale limit (<c>short_limit</c>).
    /// </summary>
    public decimal? ShortLimit { get; init; }

    /// <summary>
    /// 融券已用額度(<c>short_used</c>)。
    /// The short-sale amount used (<c>short_used</c>).
    /// </summary>
    public decimal? ShortUsed { get; init; }

    /// <summary>
    /// 融券可用額度(<c>short_available</c>)。
    /// The short-sale amount available (<c>short_available</c>).
    /// </summary>
    public decimal? ShortAvailable { get; init; }
}

/// <summary>
/// 期貨帳戶的保證金與權益。文件把全部欄位標成浮點數。
/// The margin and equity of a futures account. The documentation types every field as a float.
/// </summary>
public sealed record ShioajiMargin
{
    /// <summary>
    /// 昨日餘額(<c>yesterday_balance</c>)。
    /// Yesterday's balance (<c>yesterday_balance</c>).
    /// </summary>
    public decimal? YesterdayBalance { get; init; }

    /// <summary>
    /// 今日餘額(<c>today_balance</c>)。
    /// Today's balance (<c>today_balance</c>).
    /// </summary>
    public decimal? TodayBalance { get; init; }

    /// <summary>
    /// 存提(<c>deposit_withdrawal</c>)。
    /// Deposits and withdrawals (<c>deposit_withdrawal</c>).
    /// </summary>
    public decimal? DepositWithdrawal { get; init; }

    /// <summary>
    /// 手續費(<c>fee</c>)。
    /// The fee (<c>fee</c>).
    /// </summary>
    public decimal? Fee { get; init; }

    /// <summary>
    /// 稅(<c>tax</c>)。
    /// The tax (<c>tax</c>).
    /// </summary>
    public decimal? Tax { get; init; }

    /// <summary>
    /// 原始保證金(<c>initial_margin</c>)。
    /// The initial margin (<c>initial_margin</c>).
    /// </summary>
    public decimal? InitialMargin { get; init; }

    /// <summary>
    /// 維持保證金(<c>maintenance_margin</c>)。
    /// The maintenance margin (<c>maintenance_margin</c>).
    /// </summary>
    public decimal? MaintenanceMargin { get; init; }

    /// <summary>
    /// 追繳金額(<c>margin_call</c>)。
    /// The margin call amount (<c>margin_call</c>).
    /// </summary>
    public decimal? MarginCall { get; init; }

    /// <summary>
    /// 風險指標(<c>risk_indicator</c>)。單位文件沒寫,原值照給。
    /// The risk indicator (<c>risk_indicator</c>). The documentation does not give its unit; the raw value is handed
    /// over.
    /// </summary>
    public decimal? RiskIndicator { get; init; }

    /// <summary>
    /// 權利金收支(<c>royalty_revenue_expenditure</c>)。
    /// Premium receipts and payments (<c>royalty_revenue_expenditure</c>).
    /// </summary>
    public decimal? RoyaltyRevenueExpenditure { get; init; }

    /// <summary>
    /// 權益數(<c>equity</c>)。
    /// The equity (<c>equity</c>).
    /// </summary>
    public required decimal Equity { get; init; }

    /// <summary>
    /// 權益總值(<c>equity_amount</c>)。
    /// The total equity amount (<c>equity_amount</c>).
    /// </summary>
    public decimal? EquityAmount { get; init; }

    /// <summary>
    /// 選擇權買方未平倉市值(<c>option_openbuy_market_value</c>)。
    /// The market value of open long options (<c>option_openbuy_market_value</c>).
    /// </summary>
    public decimal? OptionOpenBuyMarketValue { get; init; }

    /// <summary>
    /// 選擇權賣方未平倉市值(<c>option_opensell_market_value</c>)。
    /// The market value of open short options (<c>option_opensell_market_value</c>).
    /// </summary>
    public decimal? OptionOpenSellMarketValue { get; init; }

    /// <summary>
    /// 選擇權未平倉損益(<c>option_open_position</c>)。
    /// The open profit and loss of options (<c>option_open_position</c>).
    /// </summary>
    public decimal? OptionOpenPosition { get; init; }

    /// <summary>
    /// 選擇權已平倉損益(<c>option_settle_profitloss</c>)。
    /// The settled profit and loss of options (<c>option_settle_profitloss</c>).
    /// </summary>
    public decimal? OptionSettleProfitLoss { get; init; }

    /// <summary>
    /// 期貨未平倉損益(<c>future_open_position</c>)。
    /// The open profit and loss of futures (<c>future_open_position</c>).
    /// </summary>
    public decimal? FutureOpenPosition { get; init; }

    /// <summary>
    /// 當日期貨未平倉損益(<c>today_future_open_position</c>)。
    /// Today's open profit and loss of futures (<c>today_future_open_position</c>).
    /// </summary>
    public decimal? TodayFutureOpenPosition { get; init; }

    /// <summary>
    /// 期貨已平倉損益(<c>future_settle_profitloss</c>)。
    /// The settled profit and loss of futures (<c>future_settle_profitloss</c>).
    /// </summary>
    public decimal? FutureSettleProfitLoss { get; init; }

    /// <summary>
    /// 可動用(出金)保證金(<c>available_margin</c>)。
    /// The available margin (<c>available_margin</c>).
    /// </summary>
    public decimal? AvailableMargin { get; init; }

    /// <summary>
    /// 溢繳保證金(<c>plus_margin</c>)。
    /// The excess margin (<c>plus_margin</c>).
    /// </summary>
    public decimal? PlusMargin { get; init; }

    /// <summary>
    /// 溢繳保證金指標(<c>plus_margin_indicator</c>)。
    /// The excess margin indicator (<c>plus_margin_indicator</c>).
    /// </summary>
    public decimal? PlusMarginIndicator { get; init; }

    /// <summary>
    /// 有價證券抵繳金額(<c>security_collateral_amount</c>)。
    /// The securities collateral amount (<c>security_collateral_amount</c>).
    /// </summary>
    public decimal? SecurityCollateralAmount { get; init; }

    /// <summary>
    /// 委託權利金(<c>order_margin_premium</c>)。
    /// The order premium margin (<c>order_margin_premium</c>).
    /// </summary>
    public decimal? OrderMarginPremium { get; init; }

    /// <summary>
    /// 抵繳金額(<c>collateral_amount</c>)。
    /// The collateral amount (<c>collateral_amount</c>).
    /// </summary>
    public decimal? CollateralAmount { get; init; }
}

/// <summary>
/// 交割銀行帳戶的餘額。
/// The balance of the settlement bank account.
/// </summary>
public sealed record ShioajiAccountBalance
{
    /// <summary>
    /// 餘額(<c>acc_balance</c>)。查詢失敗(<see cref="ErrorMessage"/> 有內容)時可能缺漏。
    /// The balance (<c>acc_balance</c>). May be absent when the query failed (<see cref="ErrorMessage"/> has text).
    /// </summary>
    public decimal? Balance { get; init; }

    /// <summary>
    /// 查詢時間(<c>date</c>)。文件範例沒有時區,視為台北牆上時間,轉成 UTC。
    /// The query time (<c>date</c>). The documentation's example carries no offset, so it is read as Taipei
    /// wall-clock time and converted to UTC.
    /// </summary>
    public DateTimeOffset? QueriedAt { get; init; }

    /// <summary>
    /// server 在 200 回應裡帶的錯誤訊息(<c>errmsg</c>);空字串讀成 <see langword="null"/>。
    /// 文件寫只支援永豐銀行、分戶帳與 LINE Bank,其他銀行預期會在這裡看到訊息而不是 HTTP 錯誤。
    /// The error message the server carries inside a 200 response (<c>errmsg</c>); an empty string reads as
    /// <see langword="null"/>. The documentation says only SinoPac Bank, sub-accounts and LINE Bank are supported, so
    /// another bank is expected to show a message here rather than an HTTP error.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
