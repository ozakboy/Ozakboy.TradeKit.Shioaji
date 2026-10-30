using System.Globalization;
using System.Text.Json;

namespace Ozakboy.TradeKit.Shioaji.Contracts;

/// <summary>
/// 商品回應的對映。
/// Mapping of contract responses.
/// </summary>
internal static class ShioajiContractParser
{
    private static readonly string[] UpdateDateFormats = ["yyyy/MM/dd", "yyyy-MM-dd", "yyyyMMdd"];

    private static readonly string[] ListWrapperNames = ["items", "data", "contracts", "results", "list"];

    public static Result<ShioajiContract> ParseContract(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return ShioajiErrors.MalformedResponse($"{context} 的商品不是 JSON 物件。A contract in {context} is not a JSON object.");
        }

        var fields = new ShioajiFieldReader(element, context);
        var exchange = fields.RequiredString("exchange");
        var code = fields.RequiredString("code");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiContract
        {
            SecurityType = ShioajiWire.SecurityType.Parse(fields.OptionalString("security_type")),
            Region = fields.OptionalString("region"),
            Exchange = exchange,
            Code = code,
            TargetCode = NullIfBlank(fields.OptionalString("target_code")),
        };
    }

    public static Result<ShioajiContract> ParseContract(string body, string context)
    {
        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<ShioajiContract>();
        }

        using var document = parsed.GetValueOrThrow();

        return ParseContract(document.RootElement, context);
    }

    public static Result<ShioajiContractInfo> ParseInfo(string body, string context)
    {
        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<ShioajiContractInfo>();
        }

        using var document = parsed.GetValueOrThrow();

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return ShioajiErrors.MalformedResponse($"{context} 的回應不是 JSON 物件。The response for {context} is not a JSON object.");
        }

        var fields = new ShioajiFieldReader(document.RootElement, context);
        var code = fields.RequiredString("code");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiContractInfo
        {
            Code = code,
            Name = fields.OptionalString("name") ?? string.Empty,
            Exchange = fields.OptionalString("exchange") ?? string.Empty,
            SecurityType = ShioajiWire.SecurityType.Parse(fields.OptionalString("security_type")),
            DayTrade = ShioajiWire.DayTrade.Parse(fields.OptionalString("day_trade")),
            Category = NullIfBlank(fields.OptionalString("category")),
            UpdateDate = ReadUpdateDate(fields.OptionalString("update_date")),
            Reference = fields.OptionalDecimal("reference"),
            LimitUp = fields.OptionalDecimal("limit_up"),
            LimitDown = fields.OptionalDecimal("limit_down"),
            Unit = fields.OptionalDecimal("unit"),
            TargetCode = NullIfBlank(fields.OptionalString("target_code")),
            Raw = document.RootElement.Clone(),
        };
    }

    /// <summary>
    /// 解析商品列表的回應。回應的外形沒有官方範例,所以陣列與「包著陣列的物件」兩種都收。
    /// Parses a contract-list response. There is no official sample of its shape, so both a bare array and an
    /// object wrapping one are accepted.
    /// </summary>
    public static Result<ShioajiContractPage> ParsePage(string body, string context, int? requestedPage, int? requestedPageSize)
    {
        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<ShioajiContractPage>();
        }

        using var document = parsed.GetValueOrThrow();

        var items = ParseList(document.RootElement, context, out var wrapper);

        if (items.IsFailure)
        {
            return items.ToFailure<ShioajiContractPage>();
        }

        int? page = requestedPage;
        int? pageSize = requestedPageSize;
        long? total = null;

        if (wrapper is { } wrapperElement)
        {
            var fields = new ShioajiFieldReader(wrapperElement, context);
            page = ToInt32(fields.OptionalInt64("page")) ?? page;
            pageSize = ToInt32(fields.OptionalInt64("page_size")) ?? pageSize;
            total = fields.OptionalInt64("total") ?? fields.OptionalInt64("total_count") ?? fields.OptionalInt64("count");
        }

        return new ShioajiContractPage
        {
            Items = items.GetValueOrThrow(),
            Page = page,
            PageSize = pageSize,
            Total = total,
        };
    }

    public static Result<IReadOnlyList<ShioajiContract>> ParseContractList(string body, string context)
    {
        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<IReadOnlyList<ShioajiContract>>();
        }

        using var document = parsed.GetValueOrThrow();

        return ParseList(document.RootElement, context, out _);
    }

    private static Result<IReadOnlyList<ShioajiContract>> ParseList(JsonElement root, string context, out JsonElement? wrapper)
    {
        wrapper = null;
        var array = root;

        if (root.ValueKind == JsonValueKind.Object)
        {
            var found = false;

            foreach (var name in ListWrapperNames)
            {
                if (root.TryGetProperty(name, out var inner) && inner.ValueKind == JsonValueKind.Array)
                {
                    array = inner;
                    wrapper = root;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return ShioajiErrors.MalformedResponse(
                    $"{context} 的回應是物件,但找不到商品陣列。The response for {context} is an object with no contract array in it.");
            }
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return ShioajiErrors.MalformedResponse($"{context} 的回應不是陣列。The response for {context} is not an array.");
        }

        var contracts = new List<ShioajiContract>(array.GetArrayLength());

        foreach (var item in array.EnumerateArray())
        {
            var contract = ParseContract(item, context);

            if (contract.IsFailure)
            {
                return contract.ToFailure<IReadOnlyList<ShioajiContract>>();
            }

            contracts.Add(contract.GetValueOrThrow());
        }

        return contracts;
    }

    private static DateOnly? ReadUpdateDate(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && DateOnly.TryParseExact(text.Trim(), UpdateDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static int? ToInt32(long? value) =>
        value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
