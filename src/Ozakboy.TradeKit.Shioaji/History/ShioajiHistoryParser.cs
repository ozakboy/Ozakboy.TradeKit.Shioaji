using System.Text.Json;

namespace Ozakboy.TradeKit.Shioaji.History;

/// <summary>
/// 歷史資料與查詢回應的對映。
/// Mapping of historical-data and query responses.
/// </summary>
internal static class ShioajiHistoryParser
{
    private static readonly string[] ListWrapperNames = ["items", "data", "results", "list", "snapshots", "rows"];

    private static readonly string[] KbarColumns = ["datetime", "Open", "High", "Low", "Close", "Volume", "Amount"];

    private static readonly string[] TickColumns = ["datetime", "close", "volume"];

    public static Result<IReadOnlyList<ShioajiKbar>> ParseKbars(string body) =>
        ShioajiColumnarReader.Read<IReadOnlyList<ShioajiKbar>>(
            body,
            ShioajiApiPaths.Kbars,
            KbarColumns,
            table =>
            {
                var bars = new List<ShioajiKbar>(table.RowCount);

                for (var row = 0; row < table.RowCount; row++)
                {
                    if (!table.TryTime("datetime", row, out var time))
                    {
                        return table.Unreadable("datetime", row);
                    }

                    if (!table.TryDecimal("Open", row, out var open))
                    {
                        return table.Unreadable("Open", row);
                    }

                    if (!table.TryDecimal("High", row, out var high))
                    {
                        return table.Unreadable("High", row);
                    }

                    if (!table.TryDecimal("Low", row, out var low))
                    {
                        return table.Unreadable("Low", row);
                    }

                    if (!table.TryDecimal("Close", row, out var close))
                    {
                        return table.Unreadable("Close", row);
                    }

                    if (!table.TryDecimal("Volume", row, out var volume))
                    {
                        return table.Unreadable("Volume", row);
                    }

                    if (!table.TryDecimal("Amount", row, out var amount))
                    {
                        return table.Unreadable("Amount", row);
                    }

                    bars.Add(new ShioajiKbar
                    {
                        Time = time,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = volume,
                        Amount = amount,
                    });
                }

                return bars;
            });

    public static Result<IReadOnlyList<ShioajiTick>> ParseTicks(string body) =>
        ShioajiColumnarReader.Read<IReadOnlyList<ShioajiTick>>(
            body,
            ShioajiApiPaths.Ticks,
            TickColumns,
            table =>
            {
                var ticks = new List<ShioajiTick>(table.RowCount);

                for (var row = 0; row < table.RowCount; row++)
                {
                    if (!table.TryTime("datetime", row, out var time))
                    {
                        return table.Unreadable("datetime", row);
                    }

                    if (!table.TryDecimal("close", row, out var close))
                    {
                        return table.Unreadable("close", row);
                    }

                    if (!table.TryDecimal("volume", row, out var volume))
                    {
                        return table.Unreadable("volume", row);
                    }

                    ticks.Add(new ShioajiTick
                    {
                        Time = time,
                        Close = close,
                        Volume = volume,
                        BidPrice = table.OptionalDecimal("bid_price", row),
                        BidVolume = table.OptionalDecimal("bid_volume", row),
                        AskPrice = table.OptionalDecimal("ask_price", row),
                        AskVolume = table.OptionalDecimal("ask_volume", row),
                        TickType = table.OptionalInt32("tick_type", row),
                    });
                }

                return ticks;
            });

    public static Result<IReadOnlyList<ShioajiSnapshot>> ParseSnapshots(string body) =>
        ParseObjects(body, ShioajiApiPaths.Snapshots, allowColumnar: false, MapSnapshot);

    public static Result<IReadOnlyList<ShioajiDailyQuote>> ParseDailyQuotes(string body, string path) =>
        ParseObjects(body, path, allowColumnar: false, MapDailyQuote);

    public static Result<IReadOnlyList<ShioajiScannerItem>> ParseScanner(string body) =>
        ParseObjects(body, ShioajiApiPaths.Scanner, allowColumnar: false, MapScannerItem);

    public static Result<IReadOnlyList<ShioajiShortStockSource>> ParseShortStockSources(string body) =>
        ParseObjects(body, ShioajiApiPaths.ShortStockSources, allowColumnar: false, MapShortStockSource);

    public static Result<IReadOnlyList<ShioajiCreditEnquire>> ParseCreditEnquire(string body) =>
        ParseObjects(body, ShioajiApiPaths.CreditEnquire, allowColumnar: false, MapCreditEnquire);

    public static Result<IReadOnlyList<ShioajiRegulatoryItem>> ParseRegulatory(string body, ShioajiRegulatoryKind kind)
    {
        var path = kind == ShioajiRegulatoryKind.Punish ? ShioajiApiPaths.RegulatoryPunish : ShioajiApiPaths.RegulatoryNotice;

        return ParseObjects(
            body,
            path,
            allowColumnar: true,
            (element, context) => MapRegulatoryItem(element, context, kind));
    }

    /// <summary>
    /// 解析「物件陣列」形狀的回應。回應的外形沒有官方範例,所以裸陣列與「包著陣列的物件」兩種都收;
    /// <paramref name="allowColumnar"/> 為真時,連 Python SDK 那種欄位陣列物件也收。
    /// 空本文、<c>null</c>、<c>[]</c>、<c>{}</c> 都是成功的空清單(流量超量時行情查詢回空值,見
    /// <see cref="ShioajiColumnarReader"/>)。
    /// Parses a response shaped as an array of objects. There is no official sample of the shape, so both a bare
    /// array and an object wrapping one are accepted, and when <paramref name="allowColumnar"/> is set so is the
    /// column-array object the Python SDK uses. An empty body, <c>null</c>, <c>[]</c> and <c>{}</c> are successful
    /// empty lists (over the traffic allowance market queries answer with empty values; see
    /// <see cref="ShioajiColumnarReader"/>).
    /// </summary>
    private static Result<IReadOnlyList<T>> ParseObjects<T>(
        string body,
        string context,
        bool allowColumnar,
        Func<JsonElement, string, Result<T>> mapRow)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new List<T>();
        }

        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<IReadOnlyList<T>>();
        }

        using var document = parsed.GetValueOrThrow();
        var root = document.RootElement;
        var array = root;

        switch (root.ValueKind)
        {
            case JsonValueKind.Null:
                return new List<T>();
            case JsonValueKind.Array:
                break;
            case JsonValueKind.Object:
                if (!root.EnumerateObject().MoveNext())
                {
                    return new List<T>();
                }

                var wrapped = false;

                foreach (var name in ListWrapperNames)
                {
                    if (root.TryGetProperty(name, out var inner) && inner.ValueKind == JsonValueKind.Array)
                    {
                        array = inner;
                        wrapped = true;
                        break;
                    }
                }

                if (!wrapped)
                {
                    return allowColumnar
                        ? ParseColumnarRows(body, context, mapRow)
                        : ShioajiErrors.MalformedResponse($"{context} 的回應是物件,但找不到資料陣列。The response for {context} is an object with no data array in it.");
                }

                break;
            default:
                return ShioajiErrors.MalformedResponse($"{context} 的回應不是陣列。The response for {context} is not an array.");
        }

        var rows = new List<T>(array.GetArrayLength());

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return ShioajiErrors.MalformedResponse($"{context} 的陣列裡有不是 JSON 物件的元素。An element of the array in {context} is not a JSON object.");
            }

            var mapped = mapRow(item, context);

            if (mapped.IsFailure)
            {
                return mapped.ToFailure<IReadOnlyList<T>>();
            }

            rows.Add(mapped.GetValueOrThrow());
        }

        return rows;
    }

    private static Result<IReadOnlyList<T>> ParseColumnarRows<T>(
        string body,
        string context,
        Func<JsonElement, string, Result<T>> mapRow) =>
        ShioajiColumnarReader.Read<IReadOnlyList<T>>(
            body,
            context,
            ["code"],
            table =>
            {
                var rows = new List<T>(table.RowCount);

                for (var row = 0; row < table.RowCount; row++)
                {
                    var mapped = mapRow(table.RowAsObject(row), context);

                    if (mapped.IsFailure)
                    {
                        return mapped.ToFailure<IReadOnlyList<T>>();
                    }

                    rows.Add(mapped.GetValueOrThrow());
                }

                return rows;
            });

    private static Result<ShioajiSnapshot> MapSnapshot(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");
        var close = fields.RequiredDecimal("close");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiSnapshot
        {
            Code = code,
            Exchange = fields.OptionalString("exchange"),
            Time = ReadTime(fields, "ts") ?? ReadTime(fields, "datetime"),
            Open = fields.OptionalDecimal("open"),
            High = fields.OptionalDecimal("high"),
            Low = fields.OptionalDecimal("low"),
            Close = close,
            TickType = ToInt32(fields.OptionalInt64("tick_type")),
            ChangePrice = fields.OptionalDecimal("change_price"),
            ChangeRate = fields.OptionalDecimal("change_rate"),
            ChangeType = ToInt32(fields.OptionalInt64("change_type")),
            AveragePrice = fields.OptionalDecimal("average_price"),
            Volume = fields.OptionalDecimal("volume"),
            TotalVolume = fields.OptionalDecimal("total_volume"),
            Amount = fields.OptionalDecimal("amount"),
            TotalAmount = fields.OptionalDecimal("total_amount"),
            YesterdayVolume = fields.OptionalDecimal("yesterday_volume"),
            VolumeRatio = fields.OptionalDecimal("volume_ratio"),
            BuyPrice = fields.OptionalDecimal("buy_price"),
            BuyVolume = fields.OptionalDecimal("buy_volume"),
            SellPrice = fields.OptionalDecimal("sell_price"),
            SellVolume = fields.OptionalDecimal("sell_volume"),
            Raw = element.Clone(),
        };
    }

    private static Result<ShioajiDailyQuote> MapDailyQuote(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");
        var open = fields.RequiredDecimal("open");
        var high = fields.RequiredDecimal("high");
        var low = fields.RequiredDecimal("low");
        var close = fields.RequiredDecimal("close");
        var volume = fields.RequiredDecimal("volume");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiDailyQuote
        {
            Code = code,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
            Raw = element.Clone(),
        };
    }

    private static Result<ShioajiScannerItem> MapScannerItem(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiScannerItem
        {
            Code = code,
            Name = fields.OptionalString("name"),
            Time = ReadTime(fields, "ts") ?? ReadTime(fields, "datetime"),
            Open = fields.OptionalDecimal("open"),
            High = fields.OptionalDecimal("high"),
            Low = fields.OptionalDecimal("low"),
            Close = fields.OptionalDecimal("close"),
            ChangePrice = fields.OptionalDecimal("change_price"),
            ChangeRate = fields.OptionalDecimal("change_rate"),
            AveragePrice = fields.OptionalDecimal("average_price"),
            Volume = fields.OptionalDecimal("volume"),
            TotalVolume = fields.OptionalDecimal("total_volume"),
            TotalAmount = fields.OptionalDecimal("total_amount"),
            VolumeRatio = fields.OptionalDecimal("volume_ratio"),
            Raw = element.Clone(),
        };
    }

    private static Result<ShioajiShortStockSource> MapShortStockSource(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiShortStockSource
        {
            Code = code,
            Source = fields.OptionalDecimal("short_stock_source"),
            UpdateTime = ReadTime(fields, "update_time") ?? ReadTime(fields, "ts"),
            Raw = element.Clone(),
        };
    }

    private static Result<ShioajiCreditEnquire> MapCreditEnquire(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.OptionalString("stock_id") ?? fields.OptionalString("code");

        if (string.IsNullOrWhiteSpace(code))
        {
            fields.Fail("stock_id");
            return fields.Error!;
        }

        return new ShioajiCreditEnquire
        {
            Code = code,
            UpdateTime = ReadTime(fields, "update_time") ?? ReadTime(fields, "ts"),
            MarginSystem = fields.OptionalString("system"),
            MarginUnit = fields.OptionalDecimal("margin_unit"),
            ShortUnit = fields.OptionalDecimal("short_unit"),
            Raw = element.Clone(),
        };
    }

    private static Result<ShioajiRegulatoryItem> MapRegulatoryItem(JsonElement element, string context, ShioajiRegulatoryKind kind)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiRegulatoryItem
        {
            Kind = kind,
            Code = code,
            Name = fields.OptionalString("name"),
            Raw = element.Clone(),
        };
    }

    private static DateTimeOffset? ReadTime(ShioajiFieldReader fields, string name) =>
        fields.TryGet(name, out var value) && ShioajiTimestamps.TryReadColumnTime(value, out var time) ? time : null;

    private static int? ToInt32(long? value) =>
        value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
}
