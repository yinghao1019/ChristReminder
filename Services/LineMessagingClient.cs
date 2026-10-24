using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LineReminder.Services;

/// <summary>呼叫 LINE Messaging API 推送訊息。</summary>
public class LineMessagingClient(HttpClient http)
{
    private const string PushUrl = "https://api.line.me/v2/bot/message/push";
    private const string ReplyUrl = "https://api.line.me/v2/bot/message/reply";

    public Task PushTextAsync(string to, string text, CancellationToken cancellationToken = default) =>
        // 同一個 Retry Key 重送時，LINE 不會重複發送
        SendAsync(PushUrl, new { to, messages = TextMessages(text) }, Guid.NewGuid().ToString(), cancellationToken);

    /// <summary>用 webhook 事件的 replyToken 回覆訊息（不計入每月推播額度）。</summary>
    public Task ReplyTextAsync(string replyToken, string text, CancellationToken cancellationToken = default) =>
        SendAsync(ReplyUrl, new { replyToken, messages = TextMessages(text) }, retryKey: null, cancellationToken);

    private static object[] TextMessages(string text) => [new { type = "text", text }];

    private async Task SendAsync(string url, object payload, string? retryKey, CancellationToken cancellationToken)
    {
        var token = Environment.GetEnvironmentVariable("LINE_CHANNEL_ACCESS_TOKEN")
            ?? throw new InvalidOperationException("缺少設定值 LINE_CHANNEL_ACCESS_TOKEN");

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (retryKey is not null)
            request.Headers.Add("X-Line-Retry-Key", retryKey);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"LINE API 呼叫失敗 ({(int)response.StatusCode})：{body}");
        }
    }
}
