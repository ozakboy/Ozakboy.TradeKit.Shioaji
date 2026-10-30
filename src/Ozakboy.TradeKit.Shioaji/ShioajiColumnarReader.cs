using System.Text.Json;
using Ozakboy.TradeKit.Shioaji.History;

namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 一份「欄位陣列」回應讀進來之後的樣子:每個欄位是一條等長的陣列,第 i 列就是各陣列的第 i 格。
/// What a column-array response looks like once read: every column is an array of the same length, and row i is the
/// i-th cell of each.
/// </summary>
/// <remarks>
/// 這個型別只在 <see cref="ShioajiColumnarReader.Read{T}"/> 的回呼裡有效 —— 格子是指向底層 JSON 文件的 <see cref="JsonElement"/>,
/// 回呼一結束文件就釋放。回呼要把需要的值複製成自己的型別(<see cref="decimal"/>、<see cref="DateTimeOffset"/> 等)再回傳。
/// This type is valid only inside the callback of <see cref="ShioajiColumnarReader.Read{T}"/>: the cells are
/// <see cref="JsonElement"/> values pointing into the underlying JSON document, which is released when the callback
/// ends. The callback must copy what it needs into its own types (<see cref="decimal"/>,
/// <see cref="DateTimeOffset"/> and so on) before returning.
/// </remarks>
internal sealed class ShioajiColumnarTable
{
    private readonly Dictionary<string, JsonElement[]> _columns;
    private readonly string _context;

    public ShioajiColumnarTable(Dictionary<string, JsonElement[]> columns, int rowCount, string context)
    {
        _columns = columns;
        RowCount = rowCount;
        _context = context;
    }

    /// <summary>
    /// 列數。空回應是 0。
    /// The number of rows; 0 for an empty response.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    /// 所有欄位名稱。
    /// The names of all columns.
    /// </summary>
    public IEnumerable<string> ColumnNames => _columns.Keys;

    /// <summary>
    /// 有沒有這個欄位(名稱不分大小寫)。
    /// Whether the column exists; the name is case-insensitive.
    /// </summary>
    public bool Has(string column) => _columns.ContainsKey(column);

    /// <summary>
    /// 讀一格時間。欄位不存在、格子是 null 或讀不懂時回傳 <see langword="false"/>。
    /// Reads a time cell; <see langword="false"/> when the column is absent, the cell is null or it cannot be read.
    /// </summary>
    public bool TryTime(string column, int row, out DateTimeOffset utc)
    {
        if (TryCell(column, row, out var cell))
        {
            return ShioajiTimestamps.TryReadColumnTime(cell, out utc);
        }

        utc = default;
        return false;
    }

    /// <summary>
    /// 讀一格數字,由原始文字讀成 <see cref="decimal"/>(不經過 <see cref="double"/>),字串型數字也收。
    /// 欄位不存在、格子是 null 或讀不懂時回傳 <see langword="false"/>。
    /// Reads a number cell from its raw text into <see cref="decimal"/>, never through <see cref="double"/>; a
    /// string-typed number is accepted. <see langword="false"/> when the column is absent, the cell is null or it
    /// cannot be read.
    /// </summary>
    public bool TryDecimal(string column, int row, out decimal value)
    {
        if (TryCell(column, row, out var cell))
        {
            return ShioajiFieldReader.TryReadDecimal(cell, out value);
        }

        value = 0m;
        return false;
    }

    /// <summary>
    /// 讀一格選填的數字;欄位不存在、格子是 null 或讀不懂都得到 <see langword="null"/>。
    /// Reads an optional number cell; absent, null and unreadable all give <see langword="null"/>.
    /// </summary>
    public decimal? OptionalDecimal(string column, int row) =>
        TryDecimal(column, row, out var value) ? value : null;

    /// <summary>
    /// 讀一格選填的整數(<c>12.0</c> 這種整數值也收);欄位不存在、格子是 null 或讀不懂都得到 <see langword="null"/>。
    /// Reads an optional integer cell (an integral value written as <c>12.0</c> is accepted); absent, null and
    /// unreadable all give <see langword="null"/>.
    /// </summary>
    public int? OptionalInt32(string column, int row) =>
        TryCell(column, row, out var cell)
        && ShioajiFieldReader.TryReadInt64(cell, out var value)
        && value is >= int.MinValue and <= int.MaxValue
            ? (int)value
            : null;

    /// <summary>
    /// 讀一格選填的字串。
    /// Reads an optional string cell.
    /// </summary>
    public string? OptionalString(string column, int row) =>
        TryCell(column, row, out var cell) && ShioajiFieldReader.TryReadString(cell, out var text) ? text : null;

    /// <summary>
    /// 組出「某一格讀不懂」的錯誤,帶欄位名與列號。
    /// Builds the error for a cell that cannot be read, carrying the column name and the row.
    /// </summary>
    public Error Unreadable(string column, int row) =>
        ShioajiErrors.MissingField(column, _context)
            .WithData(ShioajiErrorDataKeys.Row, row.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// 把第 <paramref name="row"/> 列組成一個 JSON 物件(給需要保留原文的對映用)。
    /// Assembles row <paramref name="row"/> into a JSON object, for mappings that keep the original text.
    /// </summary>
    public JsonElement RowAsObject(int row)
    {
        var json = ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();

            foreach (var (name, cells) in _columns)
            {
                writer.WritePropertyName(name);
                cells[row].WriteTo(writer);
            }

            writer.WriteEndObject();
        });

        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private bool TryCell(string column, int row, out JsonElement cell)
    {
        if (_columns.TryGetValue(column, out var cells)
            && (uint)row < (uint)cells.Length
            && cells[row].ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            cell = cells[row];
            return true;
        }

        cell = default;
        return false;
    }
}

/// <summary>
/// 歷史資料「欄位陣列」回應的讀取器,例如 <c>{"datetime":[…],"Open":[…],"Close":[…]}</c>。
/// The reader of the column-array responses of historical data, such as
/// <c>{"datetime":[…],"Open":[…],"Close":[…]}</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>各欄位長度不一致就整筆失敗。</b>少了半邊的欄位陣列湊不出一列可信的資料(第 i 筆的收盤價對到別筆的時間,
/// 比沒有資料更糟),所以任何一個欄位的長度和別的不同,整個回應回 <see cref="ShioajiErrorCodes.HistoryMalformed"/>,
/// 不會送出已經湊得起來的前幾列。
/// <b>Columns of unequal length fail the whole response.</b> Half-filled column arrays cannot yield a row anyone
/// should trust (a close matched to another row's time is worse than no data), so when any column's length differs
/// from another's the whole response fails with <see cref="ShioajiErrorCodes.HistoryMalformed"/> and not even the
/// rows that could be assembled are handed out.
/// </para>
/// <para>
/// <b>空回應是成功的空清單。</b>文件說每日流量超量時行情查詢回「空值」,但沒寫那長什麼樣,所以空本文、<c>null</c>、
/// <c>[]</c>、<c>{}</c>、欄位全是 <c>null</c>、欄位全是空陣列都當成零列。<b>零列不等於真的沒有資料</b>:
/// 呼叫端要看 <see cref="ShioajiHistoryPage{T}.PossiblyTrafficExhausted"/>。
/// <b>An empty response is a successful empty list.</b> The documentation says that over the daily traffic
/// allowance a market query answers with "empty values" without saying what that looks like, so an empty body,
/// <c>null</c>, <c>[]</c>, <c>{}</c>, all-null columns and all-empty columns all read as zero rows. <b>Zero rows is
/// not proof that there is no data</b>: callers should look at <see cref="ShioajiHistoryPage{T}.PossiblyTrafficExhausted"/>.
/// </para>
/// </remarks>
internal static class ShioajiColumnarReader
{
    /// <summary>
    /// 讀一份欄位陣列回應並交給 <paramref name="map"/> 對映。
    /// Reads a column-array response and hands it to <paramref name="map"/> for mapping.
    /// </summary>
    /// <typeparam name="T">對映的結果型別。The type the mapping produces.</typeparam>
    /// <param name="body">回應本文。The response body.</param>
    /// <param name="context">錯誤訊息裡的端點名稱。The endpoint name used in error messages.</param>
    /// <param name="requiredColumns">必要的欄位名(不分大小寫);只在至少有一列時才檢查。The required column names, case-insensitive; checked only when there is at least one row.</param>
    /// <param name="map">對映回呼;表格只在回呼裡有效。The mapping callback; the table is valid only inside it.</param>
    /// <returns>對映的結果,或讀取階段的失敗。The mapping's result, or a failure from the reading stage.</returns>
    public static Result<T> Read<T>(
        string body,
        string context,
        IReadOnlyList<string> requiredColumns,
        Func<ShioajiColumnarTable, Result<T>> map)
    {
        var empty = new ShioajiColumnarTable(new Dictionary<string, JsonElement[]>(StringComparer.OrdinalIgnoreCase), 0, context);

        if (string.IsNullOrWhiteSpace(body))
        {
            return map(empty);
        }

        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<T>();
        }

        using var document = parsed.GetValueOrThrow();
        var root = document.RootElement;

        switch (root.ValueKind)
        {
            case JsonValueKind.Null:
                return map(empty);
            case JsonValueKind.Array when root.GetArrayLength() == 0:
                return map(empty);
            case JsonValueKind.Object:
                break;
            default:
                return ShioajiErrors.HistoryMalformed(
                    $"{context} 的回應不是欄位陣列(要的是 JSON 物件,每個欄位一條陣列)。The response for {context} is not a column-array object.");
        }

        var columns = new Dictionary<string, JsonElement[]>(StringComparer.OrdinalIgnoreCase);
        var scalars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Array:
                    columns[property.Name] = [.. property.Value.EnumerateArray()];
                    break;
                case JsonValueKind.Null:
                    break;
                default:
                    scalars.Add(property.Name);
                    break;
            }
        }

        // 必要欄位出現了,卻不是陣列:那不是欄位陣列回應,不能當成「缺欄位」輕描淡寫帶過。
        // A required column that is present but not an array: this is not a column-array response, and it must not
        // be played down as a mere missing field.
        foreach (var required in requiredColumns)
        {
            if (scalars.Contains(required))
            {
                return ShioajiErrors.HistoryMalformed($"{context} 的欄位「{required}」不是陣列。The column \"{required}\" of {context} is not an array.");
            }
        }

        if (columns.Count == 0)
        {
            // {} 或欄位全是 null:空值。有其他內容卻沒有任何陣列,是看不懂的形狀。
            // {} or all-null columns: empty values. Other content with no array at all is a shape nobody expected.
            return scalars.Count > 0
                ? ShioajiErrors.HistoryMalformed($"{context} 的回應裡找不到任何欄位陣列。The response for {context} contains no column array.")
                : map(empty);
        }

        var rowCount = columns.Values.First().Length;

        if (columns.Values.Any(cells => cells.Length != rowCount))
        {
            return ShioajiErrors.HistoryMalformed(
                $"{context} 的欄位陣列長度不一致({DescribeLengths(columns)}),整筆回應不採用。The column arrays of {context} differ in length ({DescribeLengths(columns)}); the whole response is discarded.");
        }

        if (rowCount > 0)
        {
            foreach (var required in requiredColumns)
            {
                if (!columns.ContainsKey(required))
                {
                    return ShioajiErrors.MissingField(required, context);
                }
            }
        }

        return map(new ShioajiColumnarTable(columns, rowCount, context));
    }

    private static string DescribeLengths(Dictionary<string, JsonElement[]> columns) =>
        string.Join(
            ", ",
            columns.Select(pair => string.Concat(pair.Key, "=", pair.Value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))));
}
