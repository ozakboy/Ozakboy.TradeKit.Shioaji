using Ozakboy.TradeKit.Shioaji.Streaming;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiSseParserTests
{
    private static ShioajiSseFrame? Feed(ShioajiSseParser parser, string line) => Feed(parser, line, out _);

    private static ShioajiSseFrame? Feed(ShioajiSseParser parser, string line, out ShioajiSseLineKind kind)
    {
        kind = parser.Feed(line, out var frame, out _);

        return frame;
    }

    [TestMethod]
    public void EventAndDataWithoutSpaces_FormOneEventOnTheBlankLine()
    {
        var parser = new ShioajiSseParser();

        Assert.IsNull(Feed(parser, "event:tick_stk"));
        Assert.IsNull(Feed(parser, "data:{\"code\":\"2330\"}"));

        var frame = Feed(parser, string.Empty);

        Assert.AreEqual("tick_stk", frame!.Value.EventName);
        Assert.AreEqual("{\"code\":\"2330\"}", frame.Value.Data);
    }

    [TestMethod]
    public void OneSpaceAfterTheColonIsDropped_ButNotMore()
    {
        var parser = new ShioajiSseParser();

        _ = Feed(parser, "event: tick_stk");
        _ = Feed(parser, "data:  two spaces");

        var frame = Feed(parser, string.Empty);

        Assert.AreEqual("tick_stk", frame!.Value.EventName);
        Assert.AreEqual(" two spaces", frame.Value.Data);
    }

    [TestMethod]
    public void MultipleDataLines_AreJoinedWithNewlines()
    {
        var parser = new ShioajiSseParser();

        _ = Feed(parser, "data:a");
        _ = Feed(parser, "data:b");

        Assert.AreEqual("a\nb", Feed(parser, string.Empty)!.Value.Data);
    }

    [TestMethod]
    public void CommentsAreClassifiedAndProduceNoEvent()
    {
        var parser = new ShioajiSseParser();

        var frame = Feed(parser, ": heartbeat", out var kind);

        Assert.IsNull(frame);
        Assert.AreEqual(ShioajiSseLineKind.Comment, kind);
        Assert.IsNull(Feed(parser, string.Empty));
    }

    [TestMethod]
    public void BlankLineWithNothingAccumulated_ProducesNothing()
    {
        var parser = new ShioajiSseParser();

        Assert.IsNull(Feed(parser, string.Empty, out var kind));
        Assert.AreEqual(ShioajiSseLineKind.Blank, kind);
    }

    [TestMethod]
    public void EventNameWithoutData_StillProducesAnEvent()
    {
        var parser = new ShioajiSseParser();

        _ = Feed(parser, "event:heartbeat");

        var frame = Feed(parser, string.Empty);

        Assert.AreEqual("heartbeat", frame!.Value.EventName);
        Assert.AreEqual(string.Empty, frame.Value.Data);
    }

    [TestMethod]
    public void DataWithoutAnEventName_HasAnEmptyName()
    {
        var parser = new ShioajiSseParser();

        _ = Feed(parser, "data:ping");

        Assert.AreEqual(string.Empty, Feed(parser, string.Empty)!.Value.EventName);
    }

    [TestMethod]
    public void IdAndRetryAreKnownFields_OthersAreReportedAsUnknown()
    {
        var parser = new ShioajiSseParser();

        Assert.AreEqual(ShioajiSseLineKind.Field, parser.Feed("id:42", out _, out _));
        Assert.AreEqual(ShioajiSseLineKind.Field, parser.Feed("retry:3000", out _, out _));

        var kind = parser.Feed("payload:{}", out _, out var fieldName);

        Assert.AreEqual(ShioajiSseLineKind.UnknownField, kind);
        Assert.AreEqual("payload", fieldName);

        var noColon = parser.Feed("justtext", out _, out var wholeLine);

        Assert.AreEqual(ShioajiSseLineKind.UnknownField, noColon);
        Assert.AreEqual("justtext", wholeLine);
    }

    [TestMethod]
    public void StateResetsBetweenEvents()
    {
        var parser = new ShioajiSseParser();

        _ = Feed(parser, "event:a");
        _ = Feed(parser, "data:1");
        _ = Feed(parser, string.Empty);
        _ = Feed(parser, "data:2");

        var frame = Feed(parser, string.Empty);

        Assert.AreEqual(string.Empty, frame!.Value.EventName);
        Assert.AreEqual("2", frame.Value.Data);
    }
}
