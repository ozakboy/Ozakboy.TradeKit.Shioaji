namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 本地限流預算:在請求到達券商之前先擋下。
/// The local rate budget: refuses before a request reaches the broker.
/// </summary>
/// <remarks>
/// <para>
/// <b>為什麼要在本地做。</b>券商對超量的處置是停 IP 與 ID,而且反覆登入無法解除;官方 server 本身並不替呼叫端限流。
/// 所以預算的方向是保守的:寧可讓請求在本地多等一個視窗,也不要讓它到達處置線。
/// <b>Why it lives locally.</b> The broker's penalty for overuse is to suspend the IP and the ID, and logging in
/// again does not lift it; the official server does not rate-limit on the caller's behalf. The budget therefore
/// errs conservative: better a request waits a window locally than reaches the penalty line.
/// </para>
/// <para>
/// <b>滑動視窗。</b>每個時間類別各保留視窗內的請求時刻;視窗內的次數已滿時,等到最舊的那一筆滑出視窗。
/// 需要等的時間超過 <see cref="ShioajiRateBudgetOptions.MaxWait"/> 時<b>直接失敗</b>,不會先睡一陣子才失敗 ——
/// 呼叫端(尤其是下單)需要立刻知道「這一筆沒送出」。
/// <b>Sliding windows.</b> Each timed category keeps the instants of the requests inside its window; when the
/// window is full the call waits for the oldest to slide out. A wait that would exceed
/// <see cref="ShioajiRateBudgetOptions.MaxWait"/> <b>fails at once</b> instead of sleeping first, because a
/// caller, an order above all, needs to know immediately that this one was not sent.
/// </para>
/// <para>
/// 時間來源是注入的 <see cref="TimeProvider"/>,測試可以用假時鐘走完一整個視窗而不必真的等。
/// The time source is an injected <see cref="TimeProvider"/>, so a test can walk through a whole window on a
/// fake clock without waiting.
/// </para>
/// </remarks>
public sealed class ShioajiRateBudget
{
    private const string BudgetPath = "rate-budget";

    private readonly ShioajiRateBudgetOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Window[] _windows;
    private readonly Lock _subscriptionLock = new();
    private int _subscriptionCount;

    /// <summary>
    /// 建立預算。
    /// Creates the budget.
    /// </summary>
    /// <param name="options">設定;<see langword="null"/> 時用券商公告的預設值。The settings, or the broker's published defaults when <see langword="null"/>.</param>
    /// <param name="timeProvider">時間來源;<see langword="null"/> 時用系統時間。The time source, or system time when <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> 不合法時擲出。Thrown when <paramref name="options"/> is invalid.
    /// </exception>
    public ShioajiRateBudget(ShioajiRateBudgetOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new ShioajiRateBudgetOptions();

        var validation = _options.Validate();

        if (validation.IsFailure)
        {
            throw new ArgumentException(validation.Error.Message, nameof(options));
        }

        _timeProvider = timeProvider ?? TimeProvider.System;

        _windows =
        [
            new Window(_options.MarketQueryLimit, _options.MarketQueryWindow),
            new Window(_options.PortfolioLimit, _options.PortfolioWindow),
            new Window(_options.OrderLimit, _options.OrderWindow),
        ];
    }

    /// <summary>
    /// 目前本地計入的訂閱數。
    /// The subscriptions currently counted locally.
    /// </summary>
    public int SubscriptionCount
    {
        get
        {
            lock (_subscriptionLock)
            {
                return _subscriptionCount;
            }
        }
    }

    /// <summary>
    /// 取得一次額度。時間類別在視窗已滿時等待;<see cref="ShioajiBudgetKind.Subscription"/> 不等待,滿了就失敗。
    /// Acquires one unit. Timed categories wait when the window is full; <see cref="ShioajiBudgetKind.Subscription"/>
    /// never waits and fails when full.
    /// </summary>
    /// <param name="kind">預算類別。The budget category.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 成功代表可以送出請求(<see cref="ShioajiBudgetKind.Subscription"/> 已計入一個);失敗代表請求<b>不可</b>送出,
    /// 代碼為 <see cref="ShioajiErrorCodes.RateBudgetExhausted"/> 或 <see cref="ShioajiErrorCodes.SubscriptionLimitExceeded"/>。
    /// Success means the request may go out (and for <see cref="ShioajiBudgetKind.Subscription"/> one is now
    /// counted); failure means it must <b>not</b> be sent, with code
    /// <see cref="ShioajiErrorCodes.RateBudgetExhausted"/> or <see cref="ShioajiErrorCodes.SubscriptionLimitExceeded"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> 不是已定義的值時擲出。Thrown when <paramref name="kind"/> is not a defined value.
    /// </exception>
    public async Task<Result> AcquireAsync(ShioajiBudgetKind kind, CancellationToken cancellationToken = default)
    {
        if (kind == ShioajiBudgetKind.Subscription)
        {
            return AcquireSubscription();
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義的預算類別。An undefined budget category.");
        }

        var window = _windows[(int)kind];
        var startedAt = _timeProvider.GetTimestamp();
        var maxWait = _options.MaxWait;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ShioajiErrors.RequestCancelled(BudgetPath);
            }

            var wait = window.TryTake(_timeProvider.GetTimestamp(), _timeProvider);

            if (wait == TimeSpan.Zero)
            {
                return Result.Success();
            }

            if (_timeProvider.GetElapsedTime(startedAt) + wait > maxWait)
            {
                return ShioajiErrors.RateBudgetExhausted(kind.ToString(), wait, maxWait);
            }

            try
            {
                await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ShioajiErrors.RequestCancelled(BudgetPath);
            }
        }
    }

    /// <summary>
    /// 釋放一個訂閱額度(取消訂閱成功,或訂閱請求失敗需要把先前計入的還回去時呼叫)。計數不會低於 0。
    /// Releases one subscription unit, for a successful unsubscription or for a failed subscription whose
    /// earlier count must be returned. The count never goes below zero.
    /// </summary>
    public void ReleaseSubscription()
    {
        lock (_subscriptionLock)
        {
            if (_subscriptionCount > 0)
            {
                _subscriptionCount--;
            }
        }
    }

    private Result AcquireSubscription()
    {
        lock (_subscriptionLock)
        {
            if (_subscriptionCount >= _options.SubscriptionLimit)
            {
                return ShioajiErrors.SubscriptionLimitExceeded(_options.SubscriptionLimit);
            }

            _subscriptionCount++;
            return Result.Success();
        }
    }

    /// <summary>
    /// 一個滑動視窗。
    /// One sliding window.
    /// </summary>
    private sealed class Window
    {
        private readonly int _limit;
        private readonly TimeSpan _length;
        private readonly Queue<long> _stamps = new();
        private readonly Lock _gate = new();

        public Window(int limit, TimeSpan length)
        {
            _limit = limit;
            _length = length;
        }

        /// <summary>
        /// 嘗試取一格。成功回傳 <see cref="TimeSpan.Zero"/>;已滿回傳還要等多久(一定為正值)。
        /// Tries to take one slot. Returns <see cref="TimeSpan.Zero"/> on success, otherwise how long until one
        /// frees up, always positive.
        /// </summary>
        public TimeSpan TryTake(long now, TimeProvider timeProvider)
        {
            lock (_gate)
            {
                while (_stamps.Count > 0 && timeProvider.GetElapsedTime(_stamps.Peek(), now) >= _length)
                {
                    _stamps.Dequeue();
                }

                if (_stamps.Count < _limit)
                {
                    _stamps.Enqueue(now);
                    return TimeSpan.Zero;
                }

                var remaining = _length - timeProvider.GetElapsedTime(_stamps.Peek(), now);

                // 至少 1 毫秒:避免剛好在邊界上算出 0 而被當成「已取得」。
                // At least a millisecond, so landing exactly on the boundary cannot compute zero and read as
                // "acquired".
                return remaining < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : remaining;
            }
        }
    }
}
