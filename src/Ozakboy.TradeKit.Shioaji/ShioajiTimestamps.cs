using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 把 Shioaji 的時間表示轉成 UTC <see cref="DateTimeOffset"/>。
/// Converts Shioaji's time representations into UTC <see cref="DateTimeOffset"/> values.
/// </summary>
/// <remarks>
/// <para>
/// <b>轉換規則。</b>
/// <b>The conversion rules.</b>
/// </para>
/// <list type="number">
/// <item><description>
/// <c>date</c> + <c>time</c>(或單獨的 <c>datetime</c>)不帶時區時,一律視為台北牆上時間,固定 UTC+08:00,
/// 減 8 小時得 UTC。台灣沒有日光節約時間,所以不查時區資料庫,也不受執行機器的時區影響。
/// A <c>date</c> + <c>time</c>, or a lone <c>datetime</c>, without an offset is Taipei wall-clock time at a
/// fixed UTC+08:00, and subtracting 8 hours gives UTC. Taiwan has no daylight saving, so no time-zone
/// database is consulted and the host machine's time zone has no effect.
/// </description></item>
/// <item><description>
/// <c>datetime</c> 若自帶時區(結尾是 <c>Z</c> 或 <c>±hh:mm</c>),以它自帶的為準。
/// A <c>datetime</c> that carries its own offset (ending in <c>Z</c> or <c>±hh:mm</c>) uses that offset.
/// </description></item>
/// <item><description>
/// 數字型的時間戳(委託的 <c>order_ts</c>、成交的 <c>ts</c>)視為 Unix 秒(含小數);大於 1e11 的視為毫秒。
/// A numeric timestamp (an order's <c>order_ts</c>, a deal's <c>ts</c>) is Unix seconds, fractions included;
/// anything above 1e11 is read as milliseconds.
/// </description></item>
/// <item><description>
/// 小數秒最多保留到 100 奈秒(.NET 的刻度),更細的位數直接捨去。
/// Fractional seconds keep at most 100-nanosecond resolution (the .NET tick); finer digits are dropped.
/// </description></item>
/// </list>
/// </remarks>
internal static partial class ShioajiTimestamps
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd"];

    private static readonly string[] TimeFormats =
    [
        "HH:mm:ss.FFFFFFF",
        "HH:mm:ss",
        "HHmmss",
        "HHmmss.FFFFFFF",
    ];

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy/MM/dd HH:mm:ss.FFFFFFF",
        "yyyy/MM/dd HH:mm:ss",
    ];

    [GeneratedRegex(@"(\.\d{7})\d+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex ExcessFraction();

    [GeneratedRegex(@"(?:[zZ]|[+-]\d{2}:?\d{2})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex OffsetSuffix();

    /// <summary>
    /// 把台北牆上時間轉成 UTC。
    /// Converts a Taipei wall-clock time to UTC.
    /// </summary>
    public static DateTimeOffset TaipeiToUtc(DateTime wallClock) =>
        new DateTimeOffset(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), ShioajiConstants.TaipeiUtcOffset)
            .ToUniversalTime();

    /// <summary>
    /// 由 <c>date</c> 與 <c>time</c> 兩個欄位組出 UTC 時間;任一缺漏或格式不符時回傳 <see langword="false"/>。
    /// Builds a UTC time from the <c>date</c> and <c>time</c> fields; <see langword="false"/> when either is
    /// absent or in an unrecognised format.
    /// </summary>
    public static bool TryFromDateAndTime(string? date, string? time, out DateTimeOffset utc)
    {
        utc = default;

        if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time))
        {
            return false;
        }

        if (!DateOnly.TryParseExact(date.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || !TimeOnly.TryParseExact(ExcessFraction().Replace(time.Trim(), "$1"), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
        {
            return false;
        }

        utc = TaipeiToUtc(day.ToDateTime(clock));
        return true;
    }

    /// <summary>
    /// 讀一個交易日的日曆日期:接受 <c>yyyy-MM-dd</c>、<c>yyyy/MM/dd</c>、<c>yyyyMMdd</c>,後面跟著時間
    /// (以空白或 <c>T</c> 隔開)時只取日期那一段、原樣照寫,不做時區換算。交易日是台北的日曆日,換成 UTC 會讓午夜前後的日期錯一天。
    /// Reads the calendar date of a trading day: <c>yyyy-MM-dd</c>, <c>yyyy/MM/dd</c> and <c>yyyyMMdd</c> are accepted,
    /// and when a time follows (separated by a space or <c>T</c>) only the date part is taken, as written, with no
    /// time-zone conversion. A trading day is a Taipei calendar day, and converting to UTC would shift dates around
    /// midnight by a day.
    /// </summary>
    public static bool TryReadDate(string? text, out DateOnly date)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var cut = trimmed.IndexOfAny([' ', 'T']);
        var datePart = cut > 0 ? trimmed[..cut] : trimmed;

        return DateOnly.TryParseExact(datePart, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>
    /// 解析 <c>datetime</c> 字串。
    /// Parses a <c>datetime</c> string.
    /// </summary>
    public static bool TryFromDateTimeText(string? text, out DateTimeOffset utc)
    {
        utc = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = ExcessFraction().Replace(text.Trim(), "$1");

        if (OffsetSuffix().IsMatch(trimmed))
        {
            if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
            {
                utc = withOffset.ToUniversalTime();
                return true;
            }

            return false;
        }

        if (DateTime.TryParseExact(trimmed, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            utc = TaipeiToUtc(local);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 由 Unix 秒(含小數)組出 UTC 時間;大於 1e11 的視為毫秒。
    /// Builds a UTC time from Unix seconds with fractions; anything above 1e11 is taken as milliseconds.
    /// </summary>
    public static bool TryFromUnix(decimal value, out DateTimeOffset utc)
    {
        utc = default;

        var seconds = Math.Abs(value) >= 100_000_000_000m ? value / 1000m : value;
        var ticks = decimal.Truncate(seconds * TimeSpan.TicksPerSecond);

        // DateTimeOffset 的範圍是西元 1 到 9999 年;範圍外的數字幾乎一定是欄位意義讀錯,不要硬轉。
        // DateTimeOffset spans the years 1 to 9999; a number outside that is almost certainly a misread field and
        // is not forced.
        var epochTicks = DateTimeOffset.UnixEpoch.UtcTicks;
        var min = DateTimeOffset.MinValue.UtcTicks - epochTicks;
        var max = DateTimeOffset.MaxValue.UtcTicks - epochTicks;

        if (ticks < min || ticks > max)
        {
            return false;
        }

        utc = new DateTimeOffset(epochTicks + (long)ticks, TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// 由整數(或帶小數)的 epoch 時間戳組出 UTC 時間,依數值大小判斷單位:絕對值小於 1e11 為秒、小於 1e14 為毫秒、
    /// 小於 1e17 為微秒、其餘為奈秒。零與負數視為沒有時間,回傳 <see langword="false"/>。
    /// Builds a UTC time from an integer (or fractional) epoch timestamp, judging the unit by magnitude: below 1e11
    /// is seconds, below 1e14 milliseconds, below 1e17 microseconds, anything above nanoseconds. Zero and negative
    /// values mean no time and return <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// 2026 年的秒是 10 位數、毫秒 13 位數、微秒 16 位數、奈秒 19 位數,四個區間彼此隔了好幾個數量級,
    /// 所以門檻不會把一個現代時間判成錯的單位。
    /// In 2026 seconds have 10 digits, milliseconds 13, microseconds 16 and nanoseconds 19, so the four ranges are
    /// orders of magnitude apart and the thresholds cannot put a modern time in the wrong unit.
    /// </remarks>
    public static bool TryFromEpoch(decimal value, out DateTimeOffset utc)
    {
        utc = default;

        if (value <= 0m)
        {
            return false;
        }

        var ticks = value switch
        {
            < 100_000_000_000m => value * TimeSpan.TicksPerSecond,
            < 100_000_000_000_000m => value * TimeSpan.TicksPerMillisecond,
            < 100_000_000_000_000_000m => value * TimeSpan.TicksPerMillisecond / 1000m,
            _ => value / 100m,
        };

        ticks = decimal.Truncate(ticks);

        var epochTicks = DateTimeOffset.UnixEpoch.UtcTicks;
        var max = DateTimeOffset.MaxValue.UtcTicks - epochTicks;

        if (ticks > max)
        {
            return false;
        }

        utc = new DateTimeOffset(epochTicks + (long)ticks, TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// 讀一個欄位陣列裡的時間格:字串(ISO 日期時間,不帶時區視為台北牆上時間)或數字(epoch,單位依大小判斷,見
    /// <see cref="TryFromEpoch"/>),字串型的數字也收。
    /// Reads a time cell of a column array: a string (an ISO date-time, Taipei wall-clock time when it carries no
    /// offset) or a number (an epoch whose unit is judged by magnitude, see <see cref="TryFromEpoch"/>); a
    /// string-typed number is accepted too.
    /// </summary>
    public static bool TryReadColumnTime(JsonElement value, out DateTimeOffset utc)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDecimal(out var number):
                return TryFromEpoch(number, out utc);
            case JsonValueKind.String:
                var text = value.GetString();

                if (TryFromDateTimeText(text, out utc))
                {
                    return true;
                }

                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    && TryFromEpoch(parsed, out utc);
            default:
                utc = default;
                return false;
        }
    }

    /// <summary>
    /// 讀一個可能是數字(Unix 秒)或字串(日期時間)的時間欄位。
    /// Reads a time field that may be a number (Unix seconds) or a string (a date-time).
    /// </summary>
    public static bool TryRead(JsonElement value, out DateTimeOffset utc)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDecimal(out var number):
                return TryFromUnix(number, out utc);
            case JsonValueKind.String:
                var text = value.GetString();

                if (TryFromDateTimeText(text, out utc))
                {
                    return true;
                }

                // 字串型的數字(例如 "1761800000.123")。
                // A string-typed number such as "1761800000.123".
                if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return TryFromUnix(parsed, out utc);
                }

                return false;
            default:
                utc = default;
                return false;
        }
    }
}
