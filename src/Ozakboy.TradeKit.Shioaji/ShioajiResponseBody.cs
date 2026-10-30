namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 一個 2xx 回應的本文與它的位元組數。
/// The body of a 2xx response and its byte count.
/// </summary>
/// <param name="Text">回應本文。The response body.</param>
/// <param name="Bytes">
/// 位元組數:回應的 <c>Content-Length</c>,沒有時是實際讀到的 UTF-8 位元組數。
/// The byte count: the response's <c>Content-Length</c>, or the UTF-8 bytes actually read when there is none.
/// </param>
internal readonly record struct ShioajiResponseBody(string Text, long Bytes);
