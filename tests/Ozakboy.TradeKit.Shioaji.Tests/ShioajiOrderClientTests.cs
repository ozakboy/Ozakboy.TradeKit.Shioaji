using Ozakboy.TradeKit.Shioaji.Auth;
using Ozakboy.TradeKit.Shioaji.Orders;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiOrderClientTests
{
    private const string PlaceOrderPath = "/api/v1/order/place_order";

    private static ShioajiStockOrderRequest Stock() => new()
    {
        Exchange = "TSE",
        Code = "2330",
        Action = ShioajiAction.Buy,
        Price = 600.5m,
        Quantity = 1m,
        PriceType = ShioajiPriceType.Limit,
        OrderType = ShioajiOrderType.Rod,
        OrderLot = ShioajiOrderLot.Common,
        OrderCondition = ShioajiOrderCondition.Cash,
    };

    private static ShioajiFuturesOrderRequest Futures() => new()
    {
        Exchange = "TAIFEX",
        Code = "TXFK6",
        Action = ShioajiAction.Sell,
        Price = 23000m,
        Quantity = 2m,
        PriceType = ShioajiPriceType.Limit,
        OrderType = ShioajiOrderType.Ioc,
        OcType = ShioajiFuturesOcType.DayTrade,
    };

    [TestMethod]
    public async Task PlaceStockOrder_SendsTheDocumentedBody_AndMapsTheTrade()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", PlaceOrderPath, 200, Fixture.Read("trade_response.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Orders.PlaceStockOrderAsync(Stock() with
        {
            DayTradeShort = false,
            CustomField = "sig001",
            Account = new ShioajiOrderAccount { BrokerId = "9A95", AccountId = "1234567" },
        });

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual(
            """{"contract":{"security_type":"STK","exchange":"TSE","code":"2330"},"stock_order":{"action":"Buy","price":600.5,"quantity":1,"price_type":"LMT","order_type":"ROD","order_lot":"Common","order_cond":"Cash","daytrade_short":false,"custom_field":"sig001","account":{"broker_id":"9A95","account_id":"1234567"}}}""",
            server.Requests[0].Body);

        var trade = result.GetValueOrThrow();

        Assert.AreEqual("a1b2c3d4", trade.Order.Id);
        Assert.AreEqual("000001", trade.Order.Seqno);
        Assert.AreEqual("A0001", trade.Order.Ordno);
        Assert.AreEqual(ShioajiAction.Buy, trade.Order.Action);
        Assert.AreEqual(600.5m, trade.Order.Price);
        Assert.AreEqual(ShioajiPriceType.Limit, trade.Order.PriceType);
        Assert.AreEqual(ShioajiOrderLot.Common, trade.Order.OrderLot);
        Assert.AreEqual("9A95", trade.Order.Account!.BrokerId);
        Assert.AreEqual("2330", trade.Contract!.Code);
        Assert.AreEqual(ShioajiOrderStatusKind.PartFilled, trade.Status.Status);
        Assert.AreEqual("0", trade.Status.StatusCode);
        Assert.AreEqual(2m, trade.Status.OrderQuantity);
        Assert.AreEqual(1m, trade.Status.DealQuantity);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1_761_800_400_250), trade.Status.OrderTs);
        Assert.HasCount(1, trade.Status.Deals);
        Assert.AreEqual(600.5m, trade.Status.Deals[0].Price);
        Assert.AreEqual(1m, trade.Status.Deals[0].Quantity);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1_761_800_401_500), trade.Status.Deals[0].Ts);
    }

    [TestMethod]
    public async Task PlaceStockOrder_PriceIsSentAsPlainDecimalText_NeverAsADouble()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", PlaceOrderPath, 200, Fixture.Read("trade_response.json"));
        using var host = ShioajiTestHost.Create(server);

        await host.Orders.PlaceStockOrderAsync(Stock() with { Price = 29.80m, Quantity = 5m });
        await host.Orders.PlaceStockOrderAsync(Stock() with { Price = 0.0000001m });
        await host.Orders.PlaceStockOrderAsync(Stock() with { Price = 123456789012.345m });

        Assert.AreEqual("29.8", TestJson.RawNumber(server.Requests[0].Body, "stock_order", "price"), "trailing zeros are removed, nothing else changes");
        Assert.AreEqual("5", TestJson.RawNumber(server.Requests[0].Body, "stock_order", "quantity"));
        Assert.AreEqual("0.0000001", TestJson.RawNumber(server.Requests[1].Body, "stock_order", "price"), "no exponent notation");
        Assert.AreEqual("123456789012.345", TestJson.RawNumber(server.Requests[2].Body, "stock_order", "price"));
    }

    [TestMethod]
    public async Task PlaceFuturesOrder_SendsTheFuturesOrderBody()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", PlaceOrderPath, 200, Fixture.Read("trade_response.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Orders.PlaceFuturesOrderAsync(Futures());

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual(
            """{"contract":{"security_type":"FUT","exchange":"TAIFEX","code":"TXFK6"},"futures_order":{"action":"Sell","price":23000,"quantity":2,"price_type":"LMT","order_type":"IOC","octype":"DayTrade"}}""",
            server.Requests[0].Body);
    }

    [TestMethod]
    public async Task MarketOrder_MayCarryAZeroPrice_ButALimitOrderMayNot()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", PlaceOrderPath, 200, Fixture.Read("trade_response.json"));
        using var host = ShioajiTestHost.Create(server);

        Assert.IsTrue((await host.Orders.PlaceStockOrderAsync(Stock() with { PriceType = ShioajiPriceType.Market, Price = 0m })).IsSuccess);

        var limit = await host.Orders.PlaceStockOrderAsync(Stock() with { Price = 0m });

        Assert.AreEqual(ShioajiErrorCodes.OrderInvalidRequest, limit.Error!.Code);
        Assert.AreEqual(1, server.Requests.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("abcdefg")]
    [DataRow("ab-cd")]
    [DataRow("中文字")]
    [DataRow("ab cd")]
    public async Task CustomField_MoreThanSixAlphanumerics_IsRefusedLocally(string customField)
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", PlaceOrderPath, 200, Fixture.Read("trade_response.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Orders.PlaceStockOrderAsync(Stock() with { CustomField = customField });

        if (customField.Length == 0)
        {
            Assert.IsTrue(result.IsSuccess, "an empty custom field means none");
            Assert.DoesNotContain("custom_field", server.Requests[0].Body);
        }
        else
        {
            Assert.AreEqual(ShioajiErrorCodes.OrderInvalidCustomField, result.Error!.Code);
            Assert.IsEmpty(server.Requests);
        }
    }

    [TestMethod]
    public async Task CustomField_ExactlySixAlphanumerics_IsAccepted()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", PlaceOrderPath, 200, Fixture.Read("trade_response.json"));
        using var host = ShioajiTestHost.Create(server);

        Assert.IsTrue((await host.Orders.PlaceStockOrderAsync(Stock() with { CustomField = "Ab1Cd2" })).IsSuccess);
        Assert.IsTrue(ShioajiOrderClient.IsValidCustomField(null));
        Assert.IsFalse(ShioajiOrderClient.IsValidCustomField("1234567"));
    }

    [TestMethod]
    public async Task InvalidRequests_AreRefusedLocally_WithoutTouchingTheServerOrTheBudget()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var cases = new ShioajiStockOrderRequest[]
        {
            Stock() with { Exchange = " " },
            Stock() with { Code = string.Empty },
            Stock() with { Action = ShioajiAction.Unknown },
            Stock() with { PriceType = ShioajiPriceType.Unknown },
            Stock() with { OrderType = ShioajiOrderType.Unknown },
            Stock() with { OrderLot = ShioajiOrderLot.Unknown },
            Stock() with { OrderCondition = ShioajiOrderCondition.Unknown },
            Stock() with { Price = -1m },
            Stock() with { Quantity = 0m },
            Stock() with { Quantity = 1.5m },
            Stock() with { Quantity = -2m },
            Stock() with { Account = new ShioajiOrderAccount { BrokerId = " ", AccountId = "1" } },
        };

        foreach (var request in cases)
        {
            var result = await host.Orders.PlaceStockOrderAsync(request);

            Assert.AreEqual(ShioajiErrorCodes.OrderInvalidRequest, result.Error!.Code, request.ToString());
        }

        var badFutures = await host.Orders.PlaceFuturesOrderAsync(Futures() with { OcType = ShioajiFuturesOcType.Unknown });

        Assert.AreEqual(ShioajiErrorCodes.OrderInvalidRequest, badFutures.Error!.Code);
        Assert.IsEmpty(server.Requests);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => host.Orders.PlaceStockOrderAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => host.Orders.PlaceFuturesOrderAsync(null!));
    }

    [TestMethod]
    public async Task OrderPosts_AreNeverRetried_OnA5xx()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", PlaceOrderPath, 503, "{}");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Orders.PlaceStockOrderAsync(Stock());

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(1, server.Count("POST", PlaceOrderPath), "an order request must go out exactly once");
    }

    [TestMethod]
    public async Task EveryOrderEndpoint_IsSentExactlyOnce_WhateverTheFailure()
    {
        using var server = new FakeShioajiHttpServer();
        foreach (var path in new[] { PlaceOrderPath, "/api/v1/order/update_price", "/api/v1/order/update_qty", "/api/v1/order/cancel_order", "/api/v1/order/trades" })
        {
            server.Map("POST", path, 502, "{}");
        }

        using var host = ShioajiTestHost.Create(server);

        await host.Orders.PlaceStockOrderAsync(Stock());
        await host.Orders.PlaceFuturesOrderAsync(Futures());
        await host.Orders.UpdatePriceAsync("t1", 10m);
        await host.Orders.UpdateQuantityAsync("t1", 1m);
        await host.Orders.CancelOrderAsync("t1");
        await host.Orders.ListTradesAsync(ShioajiAccountType.Stock);

        Assert.AreEqual(2, server.Count("POST", PlaceOrderPath));
        Assert.AreEqual(1, server.Count("POST", "/api/v1/order/update_price"));
        Assert.AreEqual(1, server.Count("POST", "/api/v1/order/update_qty"));
        Assert.AreEqual(1, server.Count("POST", "/api/v1/order/cancel_order"));
        Assert.AreEqual(1, server.Count("POST", "/api/v1/order/trades"));
    }

    [TestMethod]
    public async Task ServerErrorsAndTimeouts_AreFlaggedOutcomeUnknown_ButExplicitRefusalsAreNot()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o => o.RequestTimeout = TimeSpan.FromMilliseconds(300));

        server.Map("POST", PlaceOrderPath, 500, "{}");
        var serverError = await host.Orders.PlaceStockOrderAsync(Stock());

        server.Map("POST", PlaceOrderPath, 422, """{"detail":"bad price"}""");
        var refused = await host.Orders.PlaceStockOrderAsync(Stock());

        server.Map("POST", PlaceOrderPath, _ =>
        {
            Thread.Sleep(1200);
            return new FakeResponse(200, "{}");
        });
        var timedOut = await host.Orders.PlaceStockOrderAsync(Stock());

        Assert.AreEqual("true", serverError.Error!.Data![ShioajiErrorDataKeys.OutcomeUnknown]);
        Assert.IsFalse(refused.Error!.Data!.ContainsKey(ShioajiErrorDataKeys.OutcomeUnknown), "a 4xx is an explicit refusal, not an unknown");
        Assert.AreEqual(ShioajiErrorCodes.RequestTimeout, timedOut.Error!.Code);
        Assert.AreEqual("true", timedOut.Error.Data![ShioajiErrorDataKeys.OutcomeUnknown]);
    }

    [TestMethod]
    public async Task ARefusedConnection_IsNotFlaggedUnknown_BecauseTheRequestNeverArrived()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.BaseUri = new Uri($"http://127.0.0.1:{FakeShioajiHttpServer.FindFreePort()}/");
            o.RequestTimeout = TimeSpan.FromSeconds(8);
        });

        var result = await host.Orders.PlaceStockOrderAsync(Stock());

        Assert.AreEqual(ShioajiErrorCodes.ServerUnreachable, result.Error!.Code);
        Assert.IsFalse(result.Error.Data!.ContainsKey(ShioajiErrorDataKeys.OutcomeUnknown));
    }

    [TestMethod]
    public async Task A2xxWithAnUnreadableAnswer_IsOrderResponseUnreadable_NotASilentSuccess()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        foreach (var body in new[] { "not json", "{}", """{"order":{"seqno":"1"}}""", "[]" })
        {
            server.Map("POST", PlaceOrderPath, 200, body);

            var result = await host.Orders.PlaceStockOrderAsync(Stock());

            Assert.AreEqual(ShioajiErrorCodes.OrderResponseUnreadable, result.Error!.Code, body);
            Assert.AreEqual("true", result.Error.Data![ShioajiErrorDataKeys.OutcomeUnknown]);
            Assert.Contains("do not resend", result.Error.Message);
        }
    }

    [TestMethod]
    public async Task PlacedOrder_ThatLacksOptionalFields_StillMaps()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", PlaceOrderPath, 200, """{"order":{"id":"only-id"}}""");
        using var host = ShioajiTestHost.Create(server);

        var trade = (await host.Orders.PlaceStockOrderAsync(Stock())).GetValueOrThrow();

        Assert.AreEqual("only-id", trade.Order.Id);
        Assert.IsNull(trade.Order.Price);
        Assert.AreEqual(ShioajiAction.Unknown, trade.Order.Action);
        Assert.AreEqual(ShioajiOrderStatusKind.Unknown, trade.Status.Status, "an absent status must never read as filled");
        Assert.AreEqual("only-id", trade.Status.Id);
        Assert.IsEmpty(trade.Status.Deals);
        Assert.IsNull(trade.Contract);
    }

    [TestMethod]
    [DataRow("PendingSubmit", ShioajiOrderStatusKind.PendingSubmit)]
    [DataRow("PreSubmitted", ShioajiOrderStatusKind.PreSubmitted)]
    [DataRow("Submitted", ShioajiOrderStatusKind.Submitted)]
    [DataRow("PartFilled", ShioajiOrderStatusKind.PartFilled)]
    [DataRow("Filled", ShioajiOrderStatusKind.Filled)]
    [DataRow("Cancelled", ShioajiOrderStatusKind.Cancelled)]
    [DataRow("Failed", ShioajiOrderStatusKind.Failed)]
    [DataRow("Inactive", ShioajiOrderStatusKind.Inactive)]
    [DataRow("Status.Filled", ShioajiOrderStatusKind.Filled)]
    [DataRow("filled", ShioajiOrderStatusKind.Filled)]
    [DataRow("Weird", ShioajiOrderStatusKind.Unknown)]
    [DataRow("", ShioajiOrderStatusKind.Unknown)]
    public void StatusStrings_MapToTheEightStates_AndUnknownsStayUnknown(string wire, ShioajiOrderStatusKind expected)
    {
        Assert.AreEqual(expected, ShioajiTradeParser.ParseStatusKind(wire));
    }

    [TestMethod]
    public async Task UpdatePrice_UpdateQuantity_Cancel_PostTheTradeId()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/order/update_price", 200, Fixture.Read("trade_response.json"));
        server.Map("POST", "/api/v1/order/update_qty", 200, Fixture.Read("trade_response.json"));
        server.Map("POST", "/api/v1/order/cancel_order", 200, Fixture.Read("trade_response.json"));
        using var host = ShioajiTestHost.Create(server);

        var price = await host.Orders.UpdatePriceAsync("a1b2c3d4", 601m);
        var quantity = await host.Orders.UpdateQuantityAsync("a1b2c3d4", 1m);
        var cancel = await host.Orders.CancelOrderAsync("a1b2c3d4");

        Assert.AreEqual("""{"trade_id":"a1b2c3d4","price":601}""", server.Requests[0].Body);
        Assert.AreEqual("""{"trade_id":"a1b2c3d4","quantity":1}""", server.Requests[1].Body);
        Assert.AreEqual("""{"trade_id":"a1b2c3d4"}""", server.Requests[2].Body);
        Assert.AreEqual("a1b2c3d4", price.GetValueOrThrow()!.Order.Id);
        Assert.IsNotNull(quantity.GetValueOrThrow());
        Assert.IsNotNull(cancel.GetValueOrThrow());
    }

    [TestMethod]
    public async Task ACancelWhoseAnswerIsNotATrade_IsStillASuccess_WithANullValue()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/order/cancel_order", 200, """{"success":true}""");
        using var host = ShioajiTestHost.Create(server);

        var cancel = await host.Orders.CancelOrderAsync("t1");

        Assert.IsTrue(cancel.IsSuccess, "an effective cancellation must not be reported as a failure");
        Assert.IsNull(cancel.GetValueOrThrow());
    }

    [TestMethod]
    public async Task Modifications_LocalValidation()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Orders.CancelOrderAsync(" ")).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Orders.UpdatePriceAsync(string.Empty, 1m)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.OrderInvalidRequest, (await host.Orders.UpdatePriceAsync("t", 0m)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.OrderInvalidRequest, (await host.Orders.UpdateQuantityAsync("t", 0m)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.OrderInvalidRequest, (await host.Orders.UpdateQuantityAsync("t", 1.5m)).Error!.Code);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task Modification_Failures_AreFlaggedLikeOrders()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/order/cancel_order", 500, "{}");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Orders.CancelOrderAsync("t1");

        Assert.AreEqual("shioaji.http.500", result.Error!.Code);
        Assert.AreEqual("true", result.Error.Data![ShioajiErrorDataKeys.OutcomeUnknown]);
    }

    [TestMethod]
    public async Task ListTrades_PostsTheAccountAndRefreshFlag_AndMapsTheTrades()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/order/trades", 200, $"[{Fixture.Read("trade_response.json")}]");
        using var host = ShioajiTestHost.Create(server);

        var trades = (await host.Orders.ListTradesAsync(ShioajiAccountType.Stock, "9A95", "1234567", refresh: true)).GetValueOrThrow();
        var defaults = (await host.Orders.ListTradesAsync(ShioajiAccountType.Futures)).GetValueOrThrow();

        Assert.HasCount(1, trades);
        Assert.AreEqual("a1b2c3d4", trades[0].Order.Id);
        Assert.HasCount(1, defaults);
        Assert.AreEqual("""{"account_type":"S","broker_id":"9A95","account_id":"1234567","refresh":true}""", server.Requests[0].Body);
        Assert.AreEqual("""{"account_type":"F","refresh":false}""", server.Requests[1].Body);
    }

    [TestMethod]
    public async Task ListTrades_AWrappedListIsAccepted_AndGarbageIsMalformed()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/order/trades", 200, $$"""{"trades":[{{Fixture.Read("trade_response.json")}}]}""");
        using var host = ShioajiTestHost.Create(server);

        Assert.HasCount(1, (await host.Orders.ListTradesAsync(ShioajiAccountType.Stock)).GetValueOrThrow());

        server.Map("POST", "/api/v1/order/trades", 200, """{"nope":1}""");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.Orders.ListTradesAsync(ShioajiAccountType.Stock)).Error!.Code);

        server.Map("POST", "/api/v1/order/trades", 200, """[{"contract":{}}]""");
        Assert.AreEqual(ShioajiErrorCodes.MissingField, (await host.Orders.ListTradesAsync(ShioajiAccountType.Stock)).Error!.Code);

        server.Map("POST", "/api/v1/order/trades", 200, "[1]");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.Orders.ListTradesAsync(ShioajiAccountType.Stock)).Error!.Code);

        server.Map("POST", "/api/v1/order/trades", 200, "oops");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.Orders.ListTradesAsync(ShioajiAccountType.Stock)).Error!.Code);
    }

    [TestMethod]
    public async Task ListTrades_LocalValidation()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Orders.ListTradesAsync(ShioajiAccountType.Foreign)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Orders.ListTradesAsync(ShioajiAccountType.Unknown)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Orders.ListTradesAsync(ShioajiAccountType.Stock, brokerId: "9A95")).Error!.Code);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task OrderBudget_RefusesBeforeTheRequestLeaves()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", PlaceOrderPath, 200, Fixture.Read("trade_response.json"));
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.OrderLimit = 2;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });

        Assert.IsTrue((await host.Orders.PlaceStockOrderAsync(Stock())).IsSuccess);
        Assert.IsTrue((await host.Orders.PlaceFuturesOrderAsync(Futures())).IsSuccess);

        var third = await host.Orders.PlaceStockOrderAsync(Stock());
        var cancel = await host.Orders.CancelOrderAsync("t1");

        Assert.AreEqual(ShioajiErrorCodes.RateBudgetExhausted, third.Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.RateBudgetExhausted, cancel.Error!.Code);
        Assert.AreEqual(2, server.Requests.Count, "the refused calls never reach the broker");
    }

    [TestMethod]
    public async Task ListTrades_RefreshUsesTheAccountBudget_PlainListingUsesTheOrderBudget()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/order/trades", 200, "[]");
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.PortfolioLimit = 1;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });

        Assert.IsTrue((await host.Orders.ListTradesAsync(ShioajiAccountType.Stock, refresh: true)).IsSuccess);
        Assert.AreEqual(
            ShioajiErrorCodes.RateBudgetExhausted,
            (await host.Orders.ListTradesAsync(ShioajiAccountType.Stock, refresh: true)).Error!.Code);
        Assert.IsTrue((await host.Orders.ListTradesAsync(ShioajiAccountType.Stock, refresh: false)).IsSuccess);
    }

    [TestMethod]
    public void Constructor_RejectsNulls()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiOrderClient(null!, host.Budget));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiOrderClient(host.Api, null!));
    }
}
