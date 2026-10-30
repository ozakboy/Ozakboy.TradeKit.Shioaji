# Ozakboy.TradeKit.Shioaji

A .NET client for the official local HTTP server of SinoPac's Shioaji, with no third-party packages.

[繁體中文](README_zh-TW.md)

Shioaji is SinoPac Securities' trading and market-data API. The vendor ships it as a local process: `shioaji server
start` logs in to the broker, loads the certificate, keeps the token and connections alive, and exposes HTTP and SSE
on `127.0.0.1:8080`. This package handles what is **outside** that process: starting it and watching it, speaking
HTTP to it, reading its SSE stream, turning its JSON into typed C# objects, and refusing excess calls with a local
budget before they ever reach the broker.

It is built on the [`Ozakboy.Http`](https://github.com/ozakboy/Ozakboy.Http) pipeline and the
[`Ozakboy.Security`](https://github.com/ozakboy/Ozakboy.Security) masker. The policy of this package family is to use
only the .NET BCL, first-party Microsoft packages and `Ozakboy.*`, judged by the **transitive** graph rather than by
package name; every dependency here complies, see "Dependency policy" at the end.

> **0.1.x is M0: let a desktop application start the server, see market data, place one simulated order and receive
> its report.** It has **never been run against a real `shioaji server` with valid credentials**: 0.1.1 was only
> adjusted after one run of the real `shioaji server start` with fake credentials (which measured start-up and
> login-failure behaviour; see "Facts measured on 2026-10-30"), and every other test runs against a local fake
> server. Which behaviours follow the documentation and which were never measured is listed in "Limits and
> unmeasured behaviour". **Read that section before going live.**

## What this release does

| Scope | Status |
| --- | --- |
| Start and supervise the official server process (`ShioajiServerSupervisor`), credentials only through child-process environment variables | Done |
| Attach to a server started elsewhere, refusing when its mode disagrees | Done |
| Backoff restarts after an unexpected exit, Faulted after a limit | Done |
| `/api/v1/info`, accounts, usage, stream status, `subscribe_trade` / `unsubscribe_trade` | Done |
| Contracts: one contract, details (with day-trading eligibility), a paged list, the futures list | Done |
| Market-data subscribe / unsubscribe, with the three silently failing subscriptions refused locally | Done |
| Aggregate SSE stream: ticks, order books and combined quotes for stocks and futures/options, index quotes, order reports | Done; fields follow the documentation and are unverified against a real server |
| Drop and idle detection, backoff reconnection, a post-reconnect replay callback, lifecycle events, a bounded queue | Done |
| Placing orders (stocks and futures), repricing, resizing, cancelling, listing; order requests never retried | Done |
| Local rate budget: market queries, account queries, orders, the subscription ceiling | Done |
| Mapping to the neutral `TradeKit.Abstractions` interfaces (`IExchangeClient` and so on) | Not yet; M1 |
| Account data (`portfolio`), historical data (bars / ticks / snapshots), combo orders, earmarking, regulatory lists | Not yet; see "Future" |

Every price and quantity is a `decimal` and every time is a UTC `DateTimeOffset`. Expected failures are always
`Result` / `Result<T>`, with codes starting with `shioaji.` that live in `ShioajiErrorCodes`, each documented in
Chinese and English.

## Installation

```
dotnet add package Ozakboy.TradeKit.Shioaji
```

Target framework `net10.0`. You also need the official `shioaji` command-line tool (`uv tool install shioaji`; on
Windows there is an official `install.ps1`). This package does not install it for you.

## Quick start

```csharp
var services = new ServiceCollection();
services.AddLogging();

services.AddShioaji(options =>
{
    options.BaseUri = new Uri("http://127.0.0.1:8080");
});

// What to replay after a reconnection: the order-report subscriptions (securities and futures once each) and the
// market-data subscriptions. Shioaji needs subscriptions made again after a drop, and the stream reader calls this
// callback after every successful reconnection.
services.AddSingleton<Func<CancellationToken, Task<Result>>>(provider => async cancellationToken =>
{
    var auth = provider.GetRequiredService<ShioajiAuthClient>();
    var quotes = provider.GetRequiredService<ShioajiQuoteClient>();

    var accounts = await auth.GetAccountsAsync(cancellationToken);

    if (accounts.IsFailure)
    {
        return accounts.ToResult();
    }

    foreach (var account in accounts.GetValueOrThrow().Where(a => a.IsSigned && a.AccountType is ShioajiAccountType.Stock or ShioajiAccountType.Futures))
    {
        var subscribed = await auth.SubscribeTradeAsync(account, cancellationToken);

        if (subscribed.IsFailure)
        {
            return subscribed;
        }
    }

    return await quotes.ReplaySubscriptionsAsync(cancellationToken);
});

// You read the credentials from the environment or a secure store; the function is called only at the instant the
// child process starts.
services.AddShioajiServerSupervisor(
    options =>
    {
        options.Production = false; // simulation is the default
        options.BindAddress = "127.0.0.1:8080";
    },
    provider => () => new ShioajiCredentials
    {
        ApiKey = Environment.GetEnvironmentVariable("MY_SHIOAJI_API_KEY")!,
        SecretKey = Environment.GetEnvironmentVariable("MY_SHIOAJI_SECRET_KEY")!,
    });

await using var provider = services.BuildServiceProvider();
```

```csharp
// 1. Start (or attach to) the official server.
var supervisor = provider.GetRequiredService<ShioajiServerSupervisor>();
var started = await supervisor.StartAsync(cancellationToken);

if (started.IsFailure)
{
    Console.WriteLine($"{started.Error.Code}: {started.Error.Message}");
    return;
}

// 2. Accounts, and the order-report subscriptions (securities and futures once each, or order_event carries only
//    heartbeats).
var auth = provider.GetRequiredService<ShioajiAuthClient>();
var accounts = (await auth.GetAccountsAsync(cancellationToken)).GetValueOrThrow();

foreach (var account in accounts.Where(a => a.IsSigned && a.AccountType is ShioajiAccountType.Stock or ShioajiAccountType.Futures))
{
    await auth.SubscribeTradeAsync(account, cancellationToken);
}

// 3. Subscribe to market data. For a continuous-month contract (TXFR1), look the TargetCode up with
//    GetContractAsync first and pass it in.
var quotes = provider.GetRequiredService<ShioajiQuoteClient>();

await quotes.SubscribeAsync(
    new ShioajiSubscription
    {
        SecurityType = ShioajiSecurityType.Stock,
        Exchange = "TSE",
        Code = "2330",
        QuoteType = ShioajiQuoteType.Tick,
    },
    cancellationToken);

// 4. Read the stream. One consumer at a time.
var reader = provider.GetRequiredService<ShioajiStreamReader>();

await foreach (var item in reader.ReadAsync(cancellationToken))
{
    if (item.IsFailure)
    {
        continue; // An event that could not be read: note it; the stream does not end because of it.
    }

    switch (item.GetValueOrThrow())
    {
        case ShioajiTickStk tick:
            Console.WriteLine($"{tick.Code} {tick.Close} @ {tick.Time:O}");
            break;
        case ShioajiOrderEvent order:
            Console.WriteLine($"report {order.EventId} {order.EventType}"); // de-duplicate on EventId
            break;
        case ShioajiStreamLifecycleEvent { Kind: ShioajiStreamLifecycleKind.Disconnected }:
            break; // Market data between this and the next Connected was not received: a data gap.
    }
}

// 5. Place an order (simulation).
var orders = provider.GetRequiredService<ShioajiOrderClient>();
var placed = await orders.PlaceStockOrderAsync(
    new ShioajiStockOrderRequest
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
        CustomField = "sig001",
    },
    cancellationToken);

if (placed.IsFailure)
{
    // A timeout or 5xx carries outcomeUnknown=true: the order may stand. Look it up first; do not resend.
    Console.WriteLine($"{placed.Error.Code}: {placed.Error.Message}");
}
```

A test copies this example line by line (`ReadmeQuickStartTests`) and runs it against the fake server; if the example
breaks, the test goes red.

## Design decisions worth knowing

### Why the official server, rather than a home-grown sidecar or a community package

Login, certificate activation, token caching and Solace reconnection all live inside the official server, and the
documentation names C# as a client of exactly this route. A home-grown Python sidecar would be one more thing to
maintain that moves with the broker's releases, and a community package breaks the family's dependency policy. So this
package covers only the **outside** of the server, and treats "what the server does" as something to be measured,
faked (the test double) and defended against.

### Credentials travel only through the child process's environment

The `ShioajiServerSupervisor` constructor takes a `Func<ShioajiCredentials>` that is **called only at the instant the
child process starts**; its values go into `ProcessStartInfo.Environment` (`SJ_API_KEY`, `SJ_SEC_KEY`, `SJ_CA_PATH`,
`SJ_CA_PASSWD`).

- **No file is written.** There is no `.env`, and no value goes on the command line (it would show in a process
  list). A test scans the whole `HomePath` after a start and confirms no file contains a credential literal.
- **Nothing reaches a log.** All four values are registered with the `SecretMasker` as soon as they are obtained, and
  the child's stdout and stderr are masked line by line before they are logged (stdout at Information, stderr at
  Warning). Masking is literal replacement, so **a value shorter than 8 characters cannot be registered** (it would
  mask swathes of ordinary text); use credentials and passwords that are long enough.
- `ShioajiCredentials.ToString()` prints only whether each field is set, and `AdditionalEnvironment` refuses the four
  keys, so credentials can only travel through the provider.
- To require HTTP authentication (`ShioajiServerOptions.RequireAuth`, which maps to `--auth`), set
  `ShioajiOptions.UseBearerAuth` so every request carries `Authorization: Bearer <key>:<secret>`. The key, the secret
  and the literal `key:secret` are registered on the client's masker, so any of them is replaced in error bodies,
  messages and logs (a test makes the fake server echo the header back to prove the masking works). Sending the
  bearer credential over plain `http` to a non-loopback address is refused when the options are validated.

### The pipeline: no signing, no rate limiting, GET retried, POST never

- **No signing.** The server needs none; authentication is the Bearer header.
- **No rate limiting in the pipeline.** Rate limiting inside the pipeline queues, whereas here the need is to refuse
  and say plainly that this one was not sent, so it is `ShioajiRateBudget`'s job; see the next section.
- **Retry for GET only.** Every POST, meaning orders, repricing, resizing, cancelling, market-data subscriptions and
  `subscribe_trade`, is marked non-idempotent and additionally pinned to `RetryPolicy.NoRetry`. Two guards: if
  someone swaps the pipeline's default policy for one that retries everything, an order still goes out once. Tests
  prove "sent exactly once, whatever the failure" for every order endpoint, and those tests go red when the guard is
  removed.
- **An unknown outcome is flagged.** An order-type request that times out or meets a 5xx carries
  `outcomeUnknown = true` in the error's `Data`: the broker may have accepted it, and the caller must look it up
  (`ListTradesAsync`) rather than resend. A refused connection (the request certainly never arrived) and a 4xx (an
  explicit refusal) do not carry it. A 2xx whose body cannot be read is the dedicated
  `shioaji.order.response_unreadable`, flagged the same way.

### The local rate budget and the red lines

The broker's penalty for overuse is to **suspend the IP and the ID, and logging in again does not lift it**, so the
budget errs conservative: better to wait locally than to touch the penalty line. `ShioajiRateBudget` is a sliding
window over a `TimeProvider`; a wait that would exceed `MaxWait` (10 seconds by default) **fails at once**
(`shioaji.rate_budget.exhausted`, transient) instead of sleeping first, because a caller placing an order needs to know
immediately that this one was not sent.

| Category | Ceiling | Calls counted today |
| --- | --- | --- |
| `MarketQuery` | 50 per 10 s | none in this release (reserved for historical data) |
| `Portfolio` | 25 per 5 s | `GetUsageAsync`; `ListTradesAsync(refresh: true)` |
| `Order` | 250 per 10 s | place, reprice, resize, cancel; `ListTradesAsync(refresh: false)` |
| `Subscription` | 200 at once | `SubscribeAsync` +1, `UnsubscribeAsync` −1; a repeat is not counted twice, a failed request returns the unit it took |

Contract lookups read the server's local contract file and are not the broker's market queries, so they do not count
against `MarketQuery`. The defaults are the broker's published ceilings; raising them hands the penalty-line risk to
you. Three more published red lines are not in the budget and are yours to respect: **never use `ticks` /
`snapshots` / `kbars` as live quotes during trading hours** (this release has none of those endpoints, so it cannot
trip it), at most 5 connections per identity (the aggregate stream counts as one SSE connection), and the daily
traffic ceiling (`GetUsageAsync` tells you what is left).

### Three silently failing subscriptions are refused locally

- A continuous-month contract (code ending in `R1` or `R2`) **without a `TargetCode`**: the server answers 200 and
  then sends only heartbeats. Refused with `shioaji.subscription.target_code_required`.
- **An index subscribed for `Tick` or `BidAsk`**: an index supports `Quote` only. Refused with
  `shioaji.subscription.index_requires_quote`.
- **More than 200 subscriptions**: refused with `shioaji.subscription.limit_exceeded`.

All three are refused before the request leaves and consume no budget.

### Stream and reconnection semantics

- It reads the **aggregate stream** `GET /api/v1/stream/data`: one connection carries every named event with shared
  heartbeats. The stream uses a separate `HttpClient` that bypasses the pipeline (the pipeline reads a response to its
  end and an SSE response has none).
- **Two ways to notice a dead connection:** the connection ends or a read fails, and `StreamIdleTimeout` (45 seconds
  by default) passes with no line at all; heartbeats and SSE comment lines both count as "received".
- **Reconnection:** exponential backoff (1 s initial, 30 s ceiling, ±20% jitter). After a connection has delivered
  data the backoff resets; one that dropped before any data does not, so a server that closes right after accepting
  cannot be hammered every second.
- **Shioaji needs subscriptions made again after a drop.** After a successful reconnection the callback you injected
  (a `Func<CancellationToken, Task<Result>>`, registered in the container or passed to the constructor) runs; use it
  to replay market-data subscriptions (`ShioajiQuoteClient.ReplaySubscriptionsAsync`) and `subscribe_trade`. A failed
  result or an expected exception from it is only logged, and the stream carries on.
- **Lifecycle events** (`ShioajiStreamLifecycleEvent`: `Connected` / `Disconnected` / `Reconnecting`, with the attempt
  number and the reason) come through the same `IAsyncEnumerable` as the market events. **Everything between a
  `Disconnected` and the next `Connected` was not received**; a strategy should treat it as a data gap.
- **A bounded queue** (10,000 by default): when full the **oldest is dropped** and counted in `DroppedEvents`, and
  reading never blocks. A lifecycle event in the queue can be dropped too, so judge gaps together with `DroppedEvents`.
- **Unknown formats are logged once.** A comment line, an unrecognised field name and a line with no colon each get
  one log line the first time their kind appears, and so does the text of the first heartbeat, so the first run
  against a real server shows in the log what a heartbeat actually looks like.

### Event classification

| Event | Type |
| --- | --- |
| `tick_stk` / `bidask_stk` / `quote_stk` | `ShioajiTickStk` / `ShioajiBidAskStk` / `ShioajiQuoteStk` |
| `tick_fop` / `bidask_fop` / `quote_fop` | `ShioajiTickFop` / `ShioajiBidAskFop` / `ShioajiQuoteFop` |
| `quote_idx` | `ShioajiQuoteIdx` |
| `order_event` | `ShioajiOrderEvent` (with `EventId` for de-duplication, and `RawJson`) |
| `heartbeat` / `ping` / a blank event name / an unrecognised name whose `data` is not a JSON object | `ShioajiHeartbeat` (original text kept) |
| An unrecognised name whose `data` is a JSON object (such as `kbar`) | `ShioajiUnknownEvent` (passed through, not dropped) |
| A known market or order event name that cannot be read | a **failure** on the stream (`shioaji.stream.malformed_event`), never swallowed as a heartbeat |
| Connection lifecycle | `ShioajiStreamLifecycleEvent` |

A book is an `IReadOnlyList<decimal>` **exactly 5 long**; an event with any other length fails rather than handing a
strategy a broken book. A missing required field (code, time, price, book arrays, `event_id`) is always a failure and
**never zero**, because zero reads as a price of nothing.

### Numbers and time

- **JSON numbers are always read from their raw text into `decimal`**, with no step through `double`; a string-typed
  number (`"29.8"`) reads too. On the way out `Precision.ToPlainString` writes the raw number with no exponent and no
  trailing zeros. Tests prove it with `29.8` and a 25-digit fraction.
- **Time:** Shioaji's `date`, `time` and `datetime` are **Taipei wall-clock time**, converted to UTC at a fixed
  UTC+08:00 (Taiwan has no daylight saving, so no time-zone database is consulted and the host's time zone has no
  effect). A `datetime` carrying its own `Z` or `±hh:mm` uses that. A numeric timestamp (`order_ts`, a deal's `ts`) is
  Unix seconds with fractions, and anything above 1e11 is read as milliseconds. `ShioajiStreamEvent.ReceivedAt` is
  when this package received the event; `ShioajiMarketEvent.Time` is the exchange's match time.

### What the supervisor does

- **Attach:** if `/api/v1/info` already answers at start and its `simulation` agrees with `Production`, the server
  counts as started externally and the state becomes `Attached`; **its process is not managed**, nothing is touched on
  stop, and the credentials are not even fetched.
- **A mode mismatch is refused:** it answers but `simulation` disagrees with `Production`, and the result is
  `shioaji.server.mode_mismatch`. This package **never kills a server someone else started** and never trades on it.
  A process it started itself that reports a different mode ("wanted simulation, reached production") is refused and
  killed.
- **Start:** `shioaji server start --no-open` (without `--no-open` the CLI opens a browser, so it is always passed),
  plus `--production`, `--auth` and `--receive-window` as configured. Mode, address and authentication are always set
  explicitly through environment variables rather than inherited from whatever those names hold on the machine. A
  timeout kills the process and returns `shioaji.server.start_timeout` with the last 20 output lines (masked) in
  `Data`; an exit before readiness is `shioaji.server.exited_early` with the exit code and the last 20 lines.
- **Restart:** an unexpected exit after readiness is restarted with exponential backoff (`RestartBackoff`, capped at
  60 s); more than `MaxRestarts` (5 by default) consecutive restarts put it in `Faulted` and it gives up, which needs
  a human. A process that ran for `StableAfter` before exiting starts the consecutive count over.
- **Stop:** `shioaji server stop` first (waiting `StopTimeout`, 10 s by default), then the whole process tree is killed.
- **Colour codes are stripped from the output.** The real `shioaji` writes ANSI colour codes. Each stdout / stderr line
  has its ANSI escape sequences stripped first and is masked after, and only then logged; the "last 20 lines" in
  error data use the stripped text too.
- **A failed login is a dedicated error.** The process exits before readiness and its output contains
  `Authentication failed` (case-insensitive), or `login validation error`, `invalid secret_key` or
  `invalid api_key` → `shioaji.server.auth_failed`, with that line (stripped and masked) in `Error.Data["detail"]`.
  Retrying the same credentials earns the same answer, so it goes **straight to `Faulted` and never enters the restart
  loop**; an authentication failure during a restart goes to `Faulted` too.
- **`secret_key` must be base58.** `ShioajiCredentials.Validate()` (which the supervisor calls before starting any
  process) checks that `SecretKey` has only base58 characters; one with `0`, `O`, `I`, `l`, `_`, `-` and the like gives
  `shioaji.server.invalid_credentials` and **no process is started**. The message says only that the format does not
  match (base58) and echoes no character. The format of `ApiKey` is unknown and it is not checked.
- **Log file location.** The `server log: <path>` line printed on stdout at start is parsed into
  `ShioajiServerStatus.LogFilePath`, so a host can show the path to the user; `null` when it cannot be parsed.
- The `ShioajiServerStatus` snapshot: `State`, `ProcessId`, `StartedAt`, `Restarts`, `LastExitCode`, `LastError`,
  `LogFilePath`.

## Facts measured on 2026-10-30 against the real server (no valid credentials)

One run of the real `shioaji.exe server start --no-open` with fake credentials (`SJ_HTTP_ADDR=127.0.0.1:18080`,
`SJ_HOME_PATH` pointing at a temporary directory). **No login ever succeeded**, so only start-up and login failure
are measured; the rest is still inferred from the documentation.

- **The server is a foreground process and does not detach.** On a failed login the process simply exits, and the last
  stdout line is
  `Error: Authentication failed: login validation error: invalid secret_key (base58 decode failed): provided string contained invalid character '_' at byte 5`.
  So the "exits 0 while the server still answers → Attached" path does not happen in practice; it stays in the code
  only as a defence.
- **The output carries ANSI colour codes**, for example `ESC[2m2026-10-30T08:33:02.060484ZESC[0m ESC[32m INFO ESC[0m …`.
- **The first stdout line at start is `server log: <SJ_HOME_PATH>\logs\server-<port>\server.<UTC date>.log`.**
- **`secret_key` must be base58**; a value with a character such as `_` makes the server fail the login and exit.
- **The output of `shioaji server status` is in toon format**; with no server running it is four lines:
  ```
  running: false
  pid: null
  port: 18080
  healthy: false
  ```
  This package does **not** parse it (health is judged with `GET /api/v1/info`); the format is recorded here only.

## Limits and unmeasured behaviour

**Nothing here has yet been run against a real `shioaji server` with valid credentials** (only the start-up and
login-failure run above). Each item below is either unwritten in the documentation or written but never measured by
me. The package handles them defensively, which is not the same as correctly; on the first successful login against a
real server (the M0 measurement), check each against the log:

- **The heartbeat format and cadence.** Undocumented. The package treats event names `heartbeat` / `ping`, a blank
  name, and unknown non-JSON-object events as heartbeats and keeps their text; SSE comment lines also count as
  "alive". The text of the first heartbeat and of the first comment line goes into the log.
- **The shape of a non-2xx error body.** Undocumented. The first 2 KB, masked, goes into `Error.Data["body"]`, and a
  sentence is assembled only from string fields named `detail` / `message` / `error` / `msg` in a JSON object.
- **The JSON shape of `order_event`.** The documentation has a field list and no sample. Whether the deal part sits
  at the top level or under `deal` is unclear and both are accepted; `EventType` is inferred from the content (an
  explicit `event_type` field wins). **The whole original text is always kept in `RawJson`**, so a wrong inference can
  be re-read.
- **The responses of `place_order`, `update_price`, `update_qty` and `cancel_order`.** `place_order` is read as a
  `Trade` per the documentation, and only `order.id` is required; a 2xx that cannot be read is
  `shioaji.order.response_unreadable` (the order may stand). The format of the reprice, resize and cancel answers is
  not described, so any 2xx is success: a readable `Trade` is passed back and an unreadable one gives `null`, because
  an effective cancellation must not be reported as a failure just because the answer looks different.
- **The response of `subscribe_trade`.** Undocumented; any 2xx is success and the body is not read.
- **The `timestamp` format of `/api/v1/stream/status`.** Assumed to be Unix seconds or a date-time string; `null`
  when unreadable.
- **Field names.** `closing_oddlot_*` and `fixed_trade_*` on `tick_stk`, `avail_borrowing` on `quote_stk`, the finer
  fields of the `*_fop` events and the fields of `quote_idx` are written in the documentation as wildcards or not at
  all; the names come from the Python SDK and are **unverified over HTTP**. A mismatch leaves that field `null` and
  does not fail the event (only code, time, price and the books are required).
- **Whether `shioaji server start` stays in the foreground or detaches.** Measured on 2026-10-30: **foreground, no
  detaching** (see above). The defensive path, a process that exits 0 while the server still answers counting as
  Attached and a stop then calling `shioaji server stop`, remains in the code and was exercised only with a scripted
  probe.
- **The behaviour of `server stop`, the `--port` parameter, whether an SSE connection counts toward the 5 login
  connections, and the real HTTP / SSE latency.** None measured.
- **`SJ_AGENT_HARNESS`** has unknown semantics and is only passed through, via
  `ShioajiServerOptions.AdditionalEnvironment`.
- **Platforms other than Windows.** The tests have run only on Windows. The supervisor's process handling and the
  `HttpListener` fake should be portable and the CI file targets ubuntu, but it **has not been run**.

## Future

Deliberately not in this release, planned for later ones:

- Implementing the neutral `TradeKit.Abstractions` interfaces: `IExchangeClient`, `IMarketDataFeed`,
  `IUserDataFeed` (the mapping is M1).
- Account data (`portfolio`): positions, profit and loss, settlements, margin, trading limits.
- Historical data: bars, ticks, snapshots. Those three are what use the `MarketQuery` budget, and where the "no
  live quotes from historical endpoints during trading hours" rule starts to need enforcing in code.
- Combo orders, earmarking, `watchlist`, regulatory (disposition / notice) lists.
- The value-added streams (`calculated_index`, `index_components`, `kbar`, `scanner`): for now they pass through as
  `ShioajiUnknownEvent`.

## Testing

Unit and contract tests never touch the Internet and need no credentials:

- **`FakeShioajiHttpServer`** (`tests/.../Support`) is a stand-in for the server on `127.0.0.1:<random port>`, built
  on the BCL's `HttpListener`. It registers routes, serves an SSE endpoint that pushes events, simulates drops and
  records the requests it receives. Its behaviour follows the documentation (the two-line `event:` + `data:` format,
  only heartbeats when unsubscribed, only heartbeats for a continuous month without `target_code`), each behaviour
  names the documentation section it rests on, and it is a `public` class that can be copied or referenced
  elsewhere.
- **`FakeShioajiServerExe`** (`tests/FakeShioajiServerExe`) is a minimal console project that imitates the `shioaji`
  command line: it serves `/api/v1/info`, exits when told `server stop`, crashes at once, never becomes ready,
  crashes once and then recovers, or crashes every time. The supervisor's start, timeout, restart, Faulted, forced
  kill, masking and "credentials never touch disk" are all tested against it.
- **The fixtures are not recordings.** `tests/.../Fixtures/PROVENANCE.md` says, file by file, what each was built from:
  with no credentials, all of them are hand-built from the documented field lists. The tests therefore prove that the
  package reads the shape the documentation describes, **not** that a real server looks like this.

```
dotnet test -c Release
dotnet test -c Release -- --coverage --coverage-output-format cobertura
```

## Security

- The package ships no default credentials and writes none to a file, a log or a `ToString`.
- Broker-login credentials travel only through the child process's environment; see "Credentials travel only through
  the child process's environment" above.
- Bearer credentials are registered on the client's masker, so no `Error` message, no `Data` entry and no exception
  text carries them, even when the server echoes the header into an error body.
- The reason text of a stream reconnection, the child process's output, and the content of an unknown format in a log
  all pass through the masker before they are written.
- Personal data (`ShioajiAccount.PersonId`, `Username`) lives in memory only and this package's logs never write it;
  please keep it out of yours too.

## Dependency policy

Only the .NET BCL, first-party Microsoft packages and `Ozakboy.*` packages, judged by the **transitive** graph.

| Package | Version | Used for |
| --- | --- | --- |
| `Ozakboy.Http` | 0.3.4 | the request pipeline (retry, timeouts, sanitised logging, masking) |
| `Ozakboy.Core.Abstractions` | 0.3.0 | `Result` / `Error` / `Precision` |
| `Ozakboy.Security` | 0.1.1 | `SecretMasker` |
| `Ozakboy.TradeKit.Abstractions` | 0.8.0 | referenced only; the neutral-interface mapping is M1 |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.14 | DI registration |
| `Microsoft.Extensions.Http` | 10.0.14 | the named `HttpClient`s |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.14 | logging |

The test project additionally uses MSTest and `Microsoft.Extensions.TimeProvider.Testing`, on Microsoft.Testing.Platform
rather than the legacy VSTest (whose path transitively drags in the third-party Newtonsoft.Json).

## License

MIT, see [LICENSE](LICENSE).
