using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LineReminder.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LineReminder;

/// <summary>
/// 接收 LINE webhook 事件，用來查群組 ID：在群組輸入「groupid」，Bot 會回覆該群組的 ID。
/// LINE Developers Console 的 Webhook URL 設為 https://&lt;function-app&gt;.azurewebsites.net/api/line/webhook
/// </summary>
public class LineWebhook(LineMessagingClient line, ILogger<LineWebhook> logger)
{
    private const string Command = "groupid";

    [Function("LineWebhook")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "line/webhook")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(req.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);

        // 驗證請求真的來自 LINE（Anonymous 端點，任何人都打得到）
        if (!IsValidSignature(body, req.Headers["X-Line-Signature"]))
        {
            logger.LogWarning("LINE webhook 簽章驗證失敗");
            return new UnauthorizedResult();
        }

        using var doc = JsonDocument.Parse(body);
        foreach (var ev in doc.RootElement.GetProperty("events").EnumerateArray())
        {
            if (!ev.TryGetProperty("source", out var source))
                continue;

            var sourceType = source.GetProperty("type").GetString();
            var sourceId = sourceType switch
            {
                "group" => source.GetProperty("groupId").GetString(),
                "room" => source.GetProperty("roomId").GetString(),
                _ => source.TryGetProperty("userId", out var userId) ? userId.GetString() : null,
            };
            logger.LogInformation("LINE 事件 {EventType}，來源 {SourceType}：{SourceId}",
                ev.GetProperty("type").GetString(), sourceType, sourceId);

            if (IsCommand(ev) && ev.TryGetProperty("replyToken", out var replyToken) && sourceId is not null)
            {
                var label = sourceType == "group" ? "群組 ID" : $"{sourceType} ID";
                await line.ReplyTextAsync(replyToken.GetString()!, $"{label}：{sourceId}", cancellationToken);
            }
        }

        return new OkResult();
    }

    private static bool IsCommand(JsonElement ev) =>
        ev.GetProperty("type").GetString() == "message"
        && ev.GetProperty("message") is var message
        && message.GetProperty("type").GetString() == "text"
        && string.Equals(message.GetProperty("text").GetString()?.Trim(), Command, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidSignature(string body, string? signature)
    {
        var secret = Environment.GetEnvironmentVariable("LINE_CHANNEL_SECRET")
            ?? throw new InvalidOperationException("缺少設定值 LINE_CHANNEL_SECRET");
        if (string.IsNullOrEmpty(signature))
            return false;

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        Span<byte> actual = stackalloc byte[32];
        return Convert.TryFromBase64String(signature, actual, out var written)
            && written == expected.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual[..written]);
    }
}
