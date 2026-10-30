using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// JSON 的共用設定與小工具。
/// Shared JSON settings and small helpers.
/// </summary>
internal static class ShioajiJson
{
    /// <summary>
    /// 低階 <c>GetAsync&lt;T&gt;</c> / <c>PostAsync&lt;T&gt;</c> 用的序列化設定:snake_case 屬性名、略過 null、
    /// 數字可以從字串讀。
    /// Serializer settings for the low-level <c>GetAsync&lt;T&gt;</c> and <c>PostAsync&lt;T&gt;</c>: snake_case
    /// property names, nulls skipped, and numbers readable from strings.
    /// </summary>
    /// <remarks>
    /// <c>AllowReadingFromString</c> 讓 <c>"29.8"</c> 這種字串型數字也能讀成 <see cref="decimal"/>,
    /// 而且走的是 <see cref="decimal"/> 自己的剖析(原始文字),不經過 <see cref="double"/>。
    /// <c>AllowReadingFromString</c> lets a string-typed number such as <c>"29.8"</c> read into a
    /// <see cref="decimal"/>, and it goes through <see cref="decimal"/>'s own parsing of the raw text, never
    /// through <see cref="double"/>.
    /// </remarks>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            PropertyNameCaseInsensitive = true,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }

    /// <summary>
    /// 把 UTF-8 JSON 寫成字串;<paramref name="write"/> 負責寫出整個值。
    /// Writes UTF-8 JSON to a string; <paramref name="write"/> writes the whole value.
    /// </summary>
    public static string Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>
    /// 解析一段 JSON 文字,失敗時回傳讀不懂的回應錯誤。呼叫端負責釋放文件。
    /// Parses JSON text, returning a malformed-response failure when it cannot. The caller disposes the
    /// document.
    /// </summary>
    public static Result<JsonDocument> Parse(string json, string context)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return ShioajiErrors.MalformedResponse(
                $"{context} 的回應不是合法的 JSON({exception.Message})。The response for {context} is not valid JSON ({exception.Message}).");
        }
    }
}
