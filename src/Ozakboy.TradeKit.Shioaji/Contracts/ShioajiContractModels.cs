using System.Text.Json;

namespace Ozakboy.TradeKit.Shioaji.Contracts;

/// <summary>
/// 一個商品(<c>GET /api/v1/data/contracts/{code}</c>)。
/// One contract, from <c>GET /api/v1/data/contracts/{code}</c>.
/// </summary>
public sealed record ShioajiContract
{
    /// <summary>
    /// 商品類別。
    /// The security type.
    /// </summary>
    public required ShioajiSecurityType SecurityType { get; init; }

    /// <summary>
    /// 地區(例如 <c>TW</c>);server 沒給時為 <see langword="null"/>。
    /// The region, such as <c>TW</c>; <see langword="null"/> when the server gave none.
    /// </summary>
    public required string? Region { get; init; }

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
    /// 連續月合約(<c>TXFR1</c>)對應的實際合約代碼(例如 <c>TXFF6</c>);其他商品為 <see langword="null"/>。
    /// 訂閱連續月合約時必須帶它。
    /// For a continuous-month contract (<c>TXFR1</c>), the actual contract code it currently maps to (such as
    /// <c>TXFF6</c>); <see langword="null"/> for anything else. A subscription to a continuous-month contract
    /// must carry it.
    /// </summary>
    public required string? TargetCode { get; init; }
}

/// <summary>
/// 商品的詳細資訊(<c>GET /api/v1/data/contracts/{code}/info</c>)。
/// A contract's details, from <c>GET /api/v1/data/contracts/{code}/info</c>.
/// </summary>
/// <remarks>
/// 回應中沒有對映成屬性的欄位保留在 <see cref="Raw"/>,不會遺失。
/// Fields of the response not mapped to a property are kept in <see cref="Raw"/> and are not lost.
/// </remarks>
public sealed record ShioajiContractInfo
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
    public required string Name { get; init; }

    /// <summary>
    /// 交易所代號。
    /// The exchange code.
    /// </summary>
    public required string Exchange { get; init; }

    /// <summary>
    /// 商品類別。
    /// The security type.
    /// </summary>
    public required ShioajiSecurityType SecurityType { get; init; }

    /// <summary>
    /// 股票的現股當沖資格;非股票或讀不懂時為 <see cref="ShioajiDayTradeEligibility.Unknown"/>,
    /// 呼叫端<b>不可</b>把它當成可以當沖。
    /// A stock's day-trading eligibility; <see cref="ShioajiDayTradeEligibility.Unknown"/> when not a stock or
    /// unreadable, which a caller must <b>never</b> read as "may day-trade".
    /// </summary>
    public required ShioajiDayTradeEligibility DayTrade { get; init; }

    /// <summary>
    /// 商品分類(產業別)代碼;server 沒給時為 <see langword="null"/>。
    /// The category (industry) code; <see langword="null"/> when the server gave none.
    /// </summary>
    public required string? Category { get; init; }

    /// <summary>
    /// 商品檔的更新日期;沒給或讀不懂時為 <see langword="null"/>。
    /// The date the contract file was updated; <see langword="null"/> when absent or unreadable.
    /// </summary>
    public required DateOnly? UpdateDate { get; init; }

    /// <summary>
    /// 參考價(昨收);沒給時為 <see langword="null"/>。
    /// The reference price (previous close); <see langword="null"/> when absent.
    /// </summary>
    public required decimal? Reference { get; init; }

    /// <summary>
    /// 漲停價;沒給時為 <see langword="null"/>。
    /// The limit-up price; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? LimitUp { get; init; }

    /// <summary>
    /// 跌停價;沒給時為 <see langword="null"/>。
    /// The limit-down price; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? LimitDown { get; init; }

    /// <summary>
    /// 交易單位(每張股數、每點金額等);沒給時為 <see langword="null"/>。
    /// The trading unit (shares per lot, value per point and so on); <see langword="null"/> when absent.
    /// </summary>
    public required decimal? Unit { get; init; }

    /// <summary>
    /// 連續月合約對應的實際合約代碼;其他商品為 <see langword="null"/>。
    /// For a continuous-month contract, the actual contract it maps to; <see langword="null"/> otherwise.
    /// </summary>
    public required string? TargetCode { get; init; }

    /// <summary>
    /// 回應的完整原始 JSON 物件。
    /// The complete raw JSON object of the response.
    /// </summary>
    public required JsonElement Raw { get; init; }
}

/// <summary>
/// 商品列表的一頁(<c>GET /api/v1/data/contracts</c>)。
/// One page of the contract list, from <c>GET /api/v1/data/contracts</c>.
/// </summary>
public sealed record ShioajiContractPage
{
    /// <summary>
    /// 這一頁的商品。不帶分頁參數時,是全部商品。
    /// The contracts on this page; all of them when no paging was requested.
    /// </summary>
    public required IReadOnlyList<ShioajiContract> Items { get; init; }

    /// <summary>
    /// 頁碼;server 沒回報時取請求時帶的值,兩者都沒有則為 <see langword="null"/>。
    /// The page number; the requested value when the server reports none, <see langword="null"/> when neither exists.
    /// </summary>
    public required int? Page { get; init; }

    /// <summary>
    /// 每頁筆數;取得方式同 <see cref="Page"/>。
    /// The page size; obtained the same way as <see cref="Page"/>.
    /// </summary>
    public required int? PageSize { get; init; }

    /// <summary>
    /// 總筆數;server 沒回報時為 <see langword="null"/>。
    /// The total count; <see langword="null"/> when the server reports none.
    /// </summary>
    public required long? Total { get; init; }
}
