using System.Text.Json.Serialization;

namespace mate.Modules.AgentConnector.CopilotStudio;

// ── Direct Line activity models (aligned with Bot Framework Direct Line v3) ──

internal sealed class DirectLineActivity
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("from")]
    public DirectLineChannelAccount? From { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("replyToId")]
    public string? ReplyToId { get; set; }

    [JsonPropertyName("attachments")]
    public List<DirectLineAttachment> Attachments { get; set; } = [];
}

internal sealed class DirectLineChannelAccount
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class DirectLineAttachment
{
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("content")]
    public object? Content { get; set; }
}

internal sealed class DirectLineConversation
{
    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("streamUrl")]
    public string? StreamUrl { get; set; }
}

internal sealed class DirectLineTokenResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }
}

internal sealed class DirectLineActivitySet
{
    [JsonPropertyName("activities")]
    public List<DirectLineActivity> Activities { get; set; } = [];

    [JsonPropertyName("watermark")]
    public string? Watermark { get; set; }
}
