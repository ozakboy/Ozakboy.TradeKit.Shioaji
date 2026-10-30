using System.Globalization;
using Ozakboy.TradeKit.Shioaji.History;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiColumnarReaderTests
{
    private static readonly string[] Required = ["datetime", "close"];

    /// <summary>
    /// 把表格讀成 (時間, 收盤價) 兩欄,方便各種格式的斷言。
    /// Reads the table as (time, close) pairs, which makes the assertions on the various formats short.
    /// </summary>
    private static Result<IReadOnlyList<(DateTimeOffset Time, decimal Close)>> ReadPairs(string body) =>
        ShioajiColumnarReader.Read<IReadOnlyList<(DateTimeOffset, decimal)>>(
            body,
            "/test",
            Required,
            table =>
            {
                var rows = new List<(DateTimeOffset, decimal)>();

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

                    rows.Add((time, close));
                }

                return rows;
            });

    private static DateTimeOffset ReadFirstTime(string datetimeJson)
    {
        var result = ReadPairs($$"""{"datetime":[{{datetimeJson}}],"close":[1]}""");

        Assert.IsTrue(result.IsSuccess, $"{datetimeJson}: {result.Error}");

        return result.GetValueOrThrow().Single().Time;
    }

    // ───────────────────────────── datetime 的三種格式 ─────────────────────────────

    [TestMethod]
    public void Datetime_IsoWithoutAnOffset_IsTaipeiWallClock_MinusEightHours()
    {
        Assert.AreEqual(new DateTimeOffset(2026, 10, 29, 1, 0, 0, TimeSpan.Zero), ReadFirstTime("\"2026-10-29T09:00:00\""));
        Assert.AreEqual(new DateTimeOffset(2026, 10, 29, 1, 0, 0, TimeSpan.Zero), ReadFirstTime("\"2026-10-29 09:00:00\""));
        Assert.AreEqual(new DateTimeOffset(2026, 10, 28, 16, 0, 0, TimeSpan.Zero), ReadFirstTime("\"2026-10-29T00:00:00\""), "crossing midnight goes back a day");
        Assert.AreEqual(new DateTimeOffset(2026, 10, 29, 1, 0, 0, TimeSpan.Zero).AddTicks(1_234_567), ReadFirstTime("\"2026-10-29T09:00:00.123456789\""), "digits beyond a tick are dropped");
    }

    [TestMethod]
    public void Datetime_IsoWithItsOwnOffset_KeepsThatOffset()
    {
        var expected = new DateTimeOffset(2026, 10, 29, 1, 0, 0, TimeSpan.Zero);

        Assert.AreEqual(expected, ReadFirstTime("\"2026-10-29T09:00:00+08:00\""));
        Assert.AreEqual(expected, ReadFirstTime("\"2026-10-29T01:00:00Z\""));
        Assert.AreEqual(expected, ReadFirstTime("\"2026-10-28T20:00:00-05:00\""));
    }

    [TestMethod]
    public void Datetime_IntegerEpoch_IsSecondsMillisecondsMicrosecondsOrNanosecondsBySize()
    {
        var instant = new DateTimeOffset(2026, 10, 29, 1, 0, 0, TimeSpan.Zero);
        var seconds = instant.ToUnixTimeSeconds();

        Assert.AreEqual(instant, ReadFirstTime(seconds.ToString(CultureInfo.InvariantCulture)), "seconds");
        Assert.AreEqual(instant, ReadFirstTime((seconds * 1000).ToString(CultureInfo.InvariantCulture)), "milliseconds");
        Assert.AreEqual(instant, ReadFirstTime((seconds * 1_000_000).ToString(CultureInfo.InvariantCulture)), "microseconds");
        Assert.AreEqual(instant, ReadFirstTime((seconds * 1_000_000_000).ToString(CultureInfo.InvariantCulture)), "nanoseconds");
        Assert.AreEqual(instant.AddTicks(5_000_000), ReadFirstTime((seconds + 0.5m).ToString(CultureInfo.InvariantCulture)), "fractional seconds");
        Assert.AreEqual(instant.AddMilliseconds(123), ReadFirstTime((seconds * 1000 + 123).ToString(CultureInfo.InvariantCulture)));
        Assert.AreEqual(instant.AddTicks(7), ReadFirstTime((seconds * 1_000_000_000 + 700).ToString(CultureInfo.InvariantCulture)), "a nanosecond epoch keeps 100 ns resolution");
        Assert.AreEqual(instant, ReadFirstTime($"\"{seconds}\""), "a string-typed epoch is accepted too");
    }

    [TestMethod]
    [DataRow("\"not a time\"")]
    [DataRow("\"\"")]
    [DataRow("true")]
    [DataRow("{}")]
    [DataRow("0")]
    [DataRow("-5")]
    [DataRow("null")]
    public void Datetime_UnreadableCells_FailWithTheColumnAndRow(string cell)
    {
        var result = ReadPairs($$"""{"datetime":["2026-10-29T09:00:00",{{cell}}],"close":[1,2]}""");

        Assert.AreEqual(ShioajiErrorCodes.MissingField, result.Error!.Code, cell);
        Assert.IsTrue(result.Error.TryGetData(ShioajiErrorDataKeys.Field, out var field));
        Assert.AreEqual("datetime", field);
        Assert.IsTrue(result.Error.TryGetData(ShioajiErrorDataKeys.Row, out var row));
        Assert.AreEqual("1", row);
    }

    // ───────────────────────────── 數字 ─────────────────────────────

    [TestMethod]
    public void Numbers_AreReadFromTheRawTextIntoDecimal_NeverThroughDouble()
    {
        var result = ReadPairs("""{"datetime":["2026-10-29T09:00:00","2026-10-29T09:00:01","2026-10-29T09:00:02","2026-10-29T09:00:03"],"close":[29.8,"29.8",0.1234567890123456789012345,12345678901234567890]}""");

        var rows = result.GetValueOrThrow();

        Assert.AreEqual(29.8m, rows[0].Close, "29.8 through double would be 29.800000000000001");
        Assert.AreEqual(29.8m, rows[1].Close);
        Assert.AreEqual(0.1234567890123456789012345m, rows[2].Close, "25 decimal places survive");
        Assert.AreEqual(12345678901234567890m, rows[3].Close);
    }

    [TestMethod]
    public void Numbers_ANullOrJunkRequiredCell_IsAFailure_NeverZero()
    {
        foreach (var cell in new[] { "null", "\"abc\"", "true", "[]" })
        {
            var result = ReadPairs($$"""{"datetime":["2026-10-29T09:00:00"],"close":[{{cell}}]}""");

            Assert.AreEqual(ShioajiErrorCodes.MissingField, result.Error!.Code, cell);
        }
    }

    // ───────────────────────────── 形狀 ─────────────────────────────

    [TestMethod]
    public void ColumnNames_AreCaseInsensitive()
    {
        var result = ReadPairs("""{"DateTime":["2026-10-29T09:00:00"],"CLOSE":[5]}""");

        Assert.AreEqual(5m, result.GetValueOrThrow().Single().Close);
    }

    [TestMethod]
    public void ColumnsOfDifferentLengths_FailTheWholeResponse_EvenWhenAnExtraColumnIsTheOddOne()
    {
        var shorter = ReadPairs("""{"datetime":["2026-10-29T09:00:00","2026-10-29T09:00:01"],"close":[1]}""");
        var longer = ReadPairs("""{"datetime":["2026-10-29T09:00:00"],"close":[1,2]}""");
        var extra = ReadPairs("""{"datetime":["2026-10-29T09:00:00"],"close":[1],"unrelated":[1,2,3]}""");

        foreach (var result in new[] { shorter, longer, extra })
        {
            Assert.AreEqual(ShioajiErrorCodes.HistoryMalformed, result.Error!.Code);
            Assert.AreEqual(ErrorCategory.Unexpected, result.Error.Category);
        }

        Assert.Contains("close=1", shorter.Error!.Message);
        Assert.Contains("datetime=2", shorter.Error.Message);
        Assert.Contains("unrelated=3", extra.Error!.Message);
    }

    [TestMethod]
    public void AnEmptyColumnNextToAFullOne_IsAlsoALengthMismatch()
    {
        var result = ReadPairs("""{"datetime":["2026-10-29T09:00:00"],"close":[]}""");

        Assert.AreEqual(ShioajiErrorCodes.HistoryMalformed, result.Error!.Code);
    }

    [TestMethod]
    public void ARequiredColumnThatIsNotAnArray_IsMalformed()
    {
        var result = ReadPairs("""{"datetime":["2026-10-29T09:00:00"],"close":5}""");

        Assert.AreEqual(ShioajiErrorCodes.HistoryMalformed, result.Error!.Code);
        Assert.Contains("close", result.Error.Message);
    }

    [TestMethod]
    public void ARequiredColumnThatIsAbsentWhileRowsExist_IsAMissingField()
    {
        var result = ReadPairs("""{"datetime":["2026-10-29T09:00:00"]}""");

        Assert.AreEqual(ShioajiErrorCodes.MissingField, result.Error!.Code);
        Assert.IsTrue(result.Error.TryGetData(ShioajiErrorDataKeys.Field, out var field));
        Assert.AreEqual("close", field);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("""{"datetime":null,"close":null}""")]
    [DataRow("""{"datetime":[],"close":[]}""")]
    [DataRow("""{"datetime":[]}""")]
    public void Empty_ResponsesAreASuccessfulZeroRowTable(string body)
    {
        var result = ReadPairs(body);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.IsEmpty(result.GetValueOrThrow());
    }

    [TestMethod]
    [DataRow("42")]
    [DataRow("\"text\"")]
    [DataRow("true")]
    [DataRow("[1,2]")]
    [DataRow("""{"detail":"quota"}""")]
    [DataRow("""{"detail":"quota","code":null}""")]
    public void NotAColumnArrayObject_IsMalformed(string body)
    {
        var result = ReadPairs(body);

        Assert.AreEqual(ShioajiErrorCodes.HistoryMalformed, result.Error!.Code, body);
    }

    [TestMethod]
    public void NotJson_IsAMalformedResponse()
    {
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, ReadPairs("<html>").Error!.Code);
    }

    [TestMethod]
    public void NonColumnScalarsBesideTheColumns_AreIgnored()
    {
        var result = ReadPairs("""{"count":2,"datetime":["2026-10-29T09:00:00","2026-10-29T09:00:01"],"close":[1,2],"status":"ok"}""");

        Assert.HasCount(2, result.GetValueOrThrow());
    }

    // ───────────────────────────── 表格的輔助讀取 ─────────────────────────────

    [TestMethod]
    public void TheTableHelpers_ReadOptionalCellsLeniently()
    {
        var result = ShioajiColumnarReader.Read<string>(
            """{"datetime":["2026-10-29T09:00:00","2026-10-29T09:00:01"],"close":[1,2],"opt_num":[7,null],"opt_int":["5",12.5],"opt_text":["a",3]}""",
            "/test",
            Required,
            table =>
            {
                Assert.AreEqual(2, table.RowCount);
                Assert.IsTrue(table.Has("OPT_NUM"));
                Assert.IsFalse(table.Has("missing"));
                Assert.HasCount(5, table.ColumnNames.ToList());

                Assert.AreEqual(7m, table.OptionalDecimal("opt_num", 0));
                Assert.IsNull(table.OptionalDecimal("opt_num", 1), "a null cell");
                Assert.IsNull(table.OptionalDecimal("missing", 0), "an absent column");
                Assert.IsNull(table.OptionalDecimal("opt_num", 5), "a row out of range");
                Assert.IsNull(table.OptionalDecimal("opt_num", -1), "a negative row");

                Assert.AreEqual(5, table.OptionalInt32("opt_int", 0), "a string-typed integer");
                Assert.IsNull(table.OptionalInt32("opt_int", 1), "12.5 is not an integer");
                Assert.IsNull(table.OptionalInt32("missing", 0));

                Assert.AreEqual("a", table.OptionalString("opt_text", 0));
                Assert.AreEqual("3", table.OptionalString("opt_text", 1), "a number reads as its raw text");
                Assert.IsNull(table.OptionalString("missing", 0));

                var row = table.RowAsObject(1);
                Assert.AreEqual(2, row.GetProperty("close").GetInt32());
                Assert.AreEqual(System.Text.Json.JsonValueKind.Null, row.GetProperty("opt_num").ValueKind);

                return "done";
            });

        Assert.AreEqual("done", result.GetValueOrThrow());
    }

}
