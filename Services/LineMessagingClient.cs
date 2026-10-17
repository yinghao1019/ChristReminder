using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LineReminder.Services;

/// <summary>呼叫 LINE Messaging API 推送訊息。</summary>
public class LineMessagingClient(HttpClient http)
{
    private const string PushUrl = "https://api.line.me/v2/bot/message/push";

    public async Task PushTextAsync(string to, string text, CancellationToken cancellationToken = default)
    {
        var token = Environment.GetEnvironmentVariable("LINE_CHANNEL_ACCESS_TOKEN")
            ?? throw new InvalidOperationException("缺少設定值 LINE_CHANNEL_ACCESS_TOKEN");

        using var request = new HttpRequestMessage(HttpMethod.Post, PushUrl)
        {
            Content = JsonContent.Create(new
            {
                to,
                messages = new[] { new { type = "text", text } },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // 同一個 Retry Key 重送時，LINE 不會重複發送
        request.Headers.Add("X-Line-Retry-Key", Guid.NewGuid().ToString());

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"LINE 推送失敗 ({(int)response.StatusCode})：{body}");
        }
    }
}
