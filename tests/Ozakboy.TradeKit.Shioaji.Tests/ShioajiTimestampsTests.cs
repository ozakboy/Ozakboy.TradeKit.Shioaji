using System.Text.Json;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiTimestampsTests
{
    [TestMethod]
    public void DateAndTime_AreTaipeiWallClock_ConvertedAtAFixedPlus8()
    {
        Assert.IsTrue(ShioajiTimestamps.TryFromDateAndTime("2026-10-30", "09:00:01.234567", out var utc));

        Assert.AreEqual(TimeSpan.Zero, utc.Offset);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 1, 0, 1, TimeSpan.Zero).AddTicks(2345670), utc);
    }

    [TestMethod]
    public void MidnightTaipei_RollsBackToThePreviousUtcDay()
    {
        Assert.IsTrue(ShioajiTimestamps.TryFromDateAndTime("2026-01-01", "00:30:00", out var utc));

        Assert.AreEqual(new DateTimeOffset(2025, 12, 31, 16, 30, 0, TimeSpan.Zero), utc);
    }

    [TestMethod]
    public void DateAndTime_AcceptTheCommonSpellings()
    {
        Assert.IsTrue(ShioajiTimestamps.TryFromDateAndTime("2026/10/30", "09:00:00", out var slashed));
        Assert.IsTrue(ShioajiTimestamps.TryFromDateAndTime("20261030", "090000", out var compact));
        Assert.AreEqual(slashed, compact);
    }

    [TestMethod]
    public void NineDigitFractions_AreTruncatedToTicks()
    {
        Assert.IsTrue(ShioajiTimestamps.TryFromDateAndTime("2026-10-30", "09:00:01.123456789", out var utc));

        Assert.AreEqual(1_234_567, utc.Ticks % TimeSpan.TicksPerSecond);
    }

    [TestMethod]
    public void MissingOrUnreadableParts_AreRejected()
    {
        Assert.IsFalse(ShioajiTimestamps.TryFromDateAndTime(null, "09:00:00", out _));
        Assert.IsFalse(ShioajiTimestamps.TryFromDateAndTime("2026-10-30", " ", out _));
        Assert.IsFalse(ShioajiTimestamps.TryFromDateAndTime("not a date", "09:00:00", out _));
        Assert.IsFalse(ShioajiTimestamps.TryFromDateAndTime("2026-10-30", "25:61:61", out _));
    }

    [TestMethod]
    public void DateTimeText_WithoutAnOffsetIsTaipei_WithOneItIsHonoured()
    {
        Assert.IsTrue(ShioajiTimestamps.TryFromDateTimeText("2026-10-30 09:00:01.5", out var taipei));
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 1, 0, 1, 500, TimeSpan.Zero), taipei);

        Assert.IsTrue(ShioajiTimestamps.TryFromDateTimeText("2026-10-30T09:00:01Z", out var zulu));
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 9, 0, 1, TimeSpan.Zero), zulu);

        Assert.IsTrue(ShioajiTimestamps.TryFromDateTimeText("2026-10-30T09:00:01+08:00", out var explicitOffset));
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 1, 0, 1, TimeSpan.Zero), explicitOffset);

        Assert.IsFalse(ShioajiTimestamps.TryFromDateTimeText("garbage", out _));
        Assert.IsFalse(ShioajiTimestamps.TryFromDateTimeText("", out _));
    }

    [TestMethod]
    public void UnixSeconds_KeepFractions_AndMillisecondsAreRecognised()
    {
        Assert.IsTrue(ShioajiTimestamps.TryFromUnix(1_761_800_000.25m, out var seconds));
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1_761_800_000).AddMilliseconds(250), seconds);

        Assert.IsTrue(ShioajiTimestamps.TryFromUnix(1_761_800_000_250m, out var milliseconds));
        Assert.AreEqual(seconds, milliseconds);

        Assert.IsFalse(ShioajiTimestamps.TryFromUnix(99_999_999_999_999_999m, out _));
    }

    [TestMethod]
    public void TryRead_HandlesNumbersStringsAndNumericStrings()
    {
        using var document = JsonDocument.Parse("""{"n":1761800000.5,"s":"2026-10-30 09:00:00","x":"1761800000.5","bad":true}""");
        var root = document.RootElement;

        Assert.IsTrue(ShioajiTimestamps.TryRead(root.GetProperty("n"), out var number));
        Assert.IsTrue(ShioajiTimestamps.TryRead(root.GetProperty("s"), out var text));
        Assert.IsTrue(ShioajiTimestamps.TryRead(root.GetProperty("x"), out var numericText));
        Assert.IsFalse(ShioajiTimestamps.TryRead(root.GetProperty("bad"), out _));

        Assert.AreEqual(number, numericText);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 1, 0, 0, TimeSpan.Zero), text);
    }

    [TestMethod]
    public void TaipeiToUtc_IgnoresTheKindOfTheInput()
    {
        var local = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Local);
        var utcKind = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

        Assert.AreEqual(ShioajiTimestamps.TaipeiToUtc(local), ShioajiTimestamps.TaipeiToUtc(utcKind));
    }
}
