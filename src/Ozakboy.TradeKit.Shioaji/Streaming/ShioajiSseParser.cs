using System.Text;

namespace Ozakboy.TradeKit.Shioaji.Streaming;

/// <summary>
/// 一行 SSE 的分類結果。
/// How one SSE line was classified.
/// </summary>
internal enum ShioajiSseLineKind
{
    /// <summary>
    /// 空行:若累積了事件就結束它。
    /// A blank line: ends the event when one has accumulated.
    /// </summary>
    Blank = 0,

    /// <summary>
    /// <c>event:</c>、<c>data:</c>、<c>id:</c>、<c>retry:</c> 這些標準欄位。
    /// A standard field: <c>event:</c>, <c>data:</c>, <c>id:</c> or <c>retry:</c>.
    /// </summary>
    Field = 1,

    /// <summary>
    /// 以 <c>:</c> 開頭的註解行。
    /// A comment line starting with <c>:</c>.
    /// </summary>
    Comment = 2,

    /// <summary>
    /// 欄位名不是標準欄位的行。
    /// A line whose field name is not a standard one.
    /// </summary>
    UnknownField = 3,
}

/// <summary>
/// 一個完整的 SSE 事件。
/// One complete SSE event.
/// </summary>
internal readonly record struct ShioajiSseFrame(string EventName, string Data);

/// <summary>
/// 逐行解析 SSE。
/// A line-by-line SSE parser.
/// </summary>
/// <remarks>
/// 規則依 WHATWG 的 SSE 規格,只取本套件用得到的部分:<c>event:</c> 設事件名;<c>data:</c> 累加,多行以換行相接;
/// 空行結束一個事件;以 <c>:</c> 開頭的是註解;冒號後面的一個空白會被去掉(Shioaji 寫的是沒有空白的
/// <c>event:tick_stk</c>,兩種寫法都要能讀)。事件名和資料都沒有的空行不產生事件。
/// The rules follow the WHATWG SSE specification, taking only the parts this package uses: <c>event:</c> sets the
/// event name; <c>data:</c> accumulates, joined by newlines across lines; a blank line ends an event; a line
/// starting with <c>:</c> is a comment; one space after the colon is dropped (Shioaji writes <c>event:tick_stk</c>
/// with none, and both forms must read). A blank line with neither an event name nor data produces no event.
/// </remarks>
internal sealed class ShioajiSseParser
{
    private readonly StringBuilder _data = new();
    private string? _eventName;
    private bool _hasData;

    /// <summary>
    /// 餵入一行。若這一行結束了一個事件,<paramref name="frame"/> 為該事件。
    /// Feeds one line. When the line ends an event, <paramref name="frame"/> is that event.
    /// </summary>
    /// <param name="line">不含換行的一行。One line without its line break.</param>
    /// <param name="frame">結束的事件。The finished event.</param>
    /// <param name="fieldName">
    /// <see cref="ShioajiSseLineKind.UnknownField"/> 時為欄位名(沒有冒號的行就是整行),用於「第一次看到的未知格式」日誌。
    /// For <see cref="ShioajiSseLineKind.UnknownField"/>, the field name (the whole line when it has no colon), for
    /// the "first unknown format seen" log.
    /// </param>
    /// <returns>這一行的分類。The classification of the line.</returns>
    public ShioajiSseLineKind Feed(string line, out ShioajiSseFrame? frame, out string? fieldName)
    {
        frame = null;
        fieldName = null;

        if (line.Length == 0)
        {
            if (_eventName is not null || _hasData)
            {
                frame = new ShioajiSseFrame(_eventName ?? string.Empty, _data.ToString());
            }

            Reset();
            return ShioajiSseLineKind.Blank;
        }

        if (line[0] == ':')
        {
            return ShioajiSseLineKind.Comment;
        }

        var colon = line.IndexOf(':', StringComparison.Ordinal);
        var name = colon < 0 ? line : line[..colon];
        var value = colon < 0 ? string.Empty : line[(colon + 1)..];

        if (value.Length > 0 && value[0] == ' ')
        {
            value = value[1..];
        }

        switch (name)
        {
            case "event":
                _eventName = value;
                return ShioajiSseLineKind.Field;
            case "data":
                if (_hasData)
                {
                    _data.Append('\n');
                }

                _data.Append(value);
                _hasData = true;
                return ShioajiSseLineKind.Field;
            case "id":
            case "retry":
                return ShioajiSseLineKind.Field;
            default:
                fieldName = name;
                return ShioajiSseLineKind.UnknownField;
        }
    }

    private void Reset()
    {
        _eventName = null;
        _hasData = false;
        _data.Clear();
    }
}
