using System.Globalization;
using System.Text.Json;

namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 從一個 JSON 物件讀欄位。必填欄位缺漏或無法解析時記下第一個錯誤並回傳預設值,
/// 讓對映程式碼可以一路寫完、最後檢查一次 <see cref="Error"/>。
/// Reads fields from a JSON object. A missing or unreadable required field records the first error and returns
/// a default, so mapping code can run to the end and check <see cref="Error"/> once.
/// </summary>
/// <remarks>
/// <para>
/// <b>數字一律由原始文字讀成 <see cref="decimal"/>。</b>JSON 數字用 <see cref="JsonElement.TryGetDecimal(out decimal)"/>
/// 解析原始位元組,字串型數字用 <see cref="decimal.TryParse(string, NumberStyles, IFormatProvider, out decimal)"/>
/// 配 <see cref="CultureInfo.InvariantCulture"/>;中間沒有任何一步經過 <see cref="double"/>,所以 <c>29.8</c> 就是
/// <c>29.8m</c>,不是 <c>29.800000000000001m</c>。
/// <b>Numbers are always read from their raw text into <see cref="decimal"/>.</b> A JSON number is parsed from
/// the raw bytes with <see cref="JsonElement.TryGetDecimal(out decimal)"/> and a string-typed number with
/// <see cref="decimal.TryParse(string, NumberStyles, IFormatProvider, out decimal)"/> under the invariant
/// culture. No step goes through <see cref="double"/>, so <c>29.8</c> is <c>29.8m</c> rather than
/// <c>29.800000000000001m</c>.
/// </para>
/// <para>
/// 選填欄位寬鬆:缺漏或讀不懂都得到 <see langword="null"/>。必填欄位嚴格:缺漏就是失敗,絕不補 0 ——
/// 0 會被讀成「成交價是零元」。
/// Optional fields are lenient: absent or unreadable gives <see langword="null"/>. Required fields are strict: a
/// gap is a failure and is never filled with zero, because zero reads as a price of nothing.
/// </para>
/// </remarks>
internal sealed class ShioajiFieldReader
{
    private readonly JsonElement _element;
    private readonly string _context;

    public ShioajiFieldReader(JsonElement element, string context)
    {
        _element = element;
        _context = context;
    }

    /// <summary>
    /// 被讀取的物件。
    /// The object being read.
    /// </summary>
    public JsonElement Element => _element;

    /// <summary>
    /// 第一個記下的錯誤,沒有時為 <see langword="null"/>。
    /// The first recorded error, or <see langword="null"/>.
    /// </summary>
    public Error? Error { get; private set; }

    /// <summary>
    /// 是否沒有任何錯誤。
    /// Whether no error was recorded.
    /// </summary>
    public bool IsValid => Error is null;

    /// <summary>
    /// 取得欄位的 JSON 值;欄位不存在或為 null 時回傳 <see langword="false"/>。
    /// Gets a field's JSON value; <see langword="false"/> when absent or null.
    /// </summary>
    public bool TryGet(string name, out JsonElement value)
    {
        if (_element.ValueKind == JsonValueKind.Object
            && _element.TryGetProperty(name, out value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return true;
        }

        value = default;
        return false;
    }

    public string RequiredString(string name)
    {
        var value = OptionalString(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            Fail(name);
            return string.Empty;
        }

        return value;
    }

    public decimal RequiredDecimal(string name)
    {
        var value = OptionalDecimal(name);

        if (value is null)
        {
            Fail(name);
            return 0m;
        }

        return value.Value;
    }

    public long RequiredInt64(string name)
    {
        var value = OptionalInt64(name);

        if (value is null)
        {
            Fail(name);
            return 0L;
        }

        return value.Value;
    }

    public string? OptionalString(string name) =>
        TryGet(name, out var value) && TryReadString(value, out var text) ? text : null;

    public decimal? OptionalDecimal(string name) =>
        TryGet(name, out var value) && TryReadDecimal(value, out var number) ? number : null;

    public long? OptionalInt64(string name) =>
        TryGet(name, out var value) && TryReadInt64(value, out var number) ? number : null;

    public bool? OptionalBool(string name) =>
        TryGet(name, out var value) && TryReadBool(value, out var flag) ? flag : null;

    /// <summary>
    /// 記下一個缺漏欄位的錯誤(只留第一個)。
    /// Records a missing-field error, keeping only the first.
    /// </summary>
    public void Fail(string name) => Error ??= ShioajiErrors.MissingField(name, _context);

    /// <summary>
    /// 記下任意錯誤(只留第一個)。
    /// Records an arbitrary error, keeping only the first.
    /// </summary>
    public void Fail(Error error) => Error ??= error;

    public static bool TryReadString(JsonElement value, out string text)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                text = value.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.Number:
                text = value.GetRawText();
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    public static bool TryReadDecimal(JsonElement value, out decimal number)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetDecimal(out number);
            case JsonValueKind.String:
                return decimal.TryParse(
                    value.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out number);
            default:
                number = 0m;
                return false;
        }
    }

    public static bool TryReadInt64(JsonElement value, out long number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number))
        {
            return true;
        }

        // 12.0 這種整數值的小數寫法,以及字串型數字,都經過 decimal 讀,仍然不碰 double。
        // An integral value written as 12.0, and a string-typed number, are read through decimal, still never
        // touching double.
        if (TryReadDecimal(value, out var decimalNumber)
            && decimalNumber == decimal.Truncate(decimalNumber)
            && decimalNumber >= long.MinValue
            && decimalNumber <= long.MaxValue)
        {
            number = (long)decimalNumber;
            return true;
        }

        number = 0L;
        return false;
    }

    public static bool TryReadBool(JsonElement value, out bool flag)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                flag = true;
                return true;
            case JsonValueKind.False:
                flag = false;
                return true;
            case JsonValueKind.String when bool.TryParse(value.GetString(), out flag):
                return true;
            case JsonValueKind.Number when value.TryGetInt64(out var number) && number is 0 or 1:
                flag = number == 1;
                return true;
            default:
                flag = false;
                return false;
        }
    }
}
