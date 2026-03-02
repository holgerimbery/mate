// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Logging;

namespace mate.Modules.AgentConnector.CopilotStudio;

/// <summary>
/// Agent connector for Microsoft Copilot Studio bots via Direct Line v3.
///
/// Implementation notes:
/// - Exponential backoff retry for polling (MaxRetries = 2, BackoffSeconds = 4).
/// - Conversation token is generated once per session; never reused across sessions.
/// - SecretRef resolution is handled by <see cref="AgentConnectionConfig.ResolvedSecrets"/>
///   at the Core layer — this connector never calls or stores raw secrets itself.
/// - Proper IAsyncDisposable cleanup.
/// </summary>
internal sealed class CopilotStudioConnector : IAgentConnector
{
    private const string DirectLineEndpoint = "https://directline.botframework.com/v3/directline";
    private const int MaxRetries = 2;
    private const int BackoffBaseSeconds = 4;

    private readonly CopilotStudioConnectorConfig _config;
    private readonly HttpClient _http;
    private readonly ILogger<CopilotStudioConnector> _logger;
    private string? _conversationToken;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public CopilotStudioConnector(
        CopilotStudioConnectorConfig config,
        HttpClient http,
        ILogger<CopilotStudioConnector> logger)
    {
        _config = config;
        _http   = http;
        _logger = logger;
    }

    public string ConnectorType => "CopilotStudio";

    // ── IAgentConnector ───────────────────────────────────────────────────────

    public async Task<ConversationSession> StartConversationAsync(
        AgentConnectionConfig config,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting CopilotStudio Direct Line conversation for bot '{BotId}'.", _config.BotId);

        _conversationToken = await GenerateConversationTokenAsync(ct);

        var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"{DirectLineEndpoint}/conversations");

        var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Invalid or expired Direct Line secret.",
                System.Net.HttpStatusCode.Forbidden    => "Access denied — check Bot ID and secret.",
                _                                      => $"HTTP {(int)response.StatusCode}: {body}",
            };
            throw new InvalidOperationException($"Failed to start Direct Line conversation: {detail}");
        }

        var conv = JsonSerializer.Deserialize<DirectLineConversation>(body, _jsonOptions);
        var conversationId = conv?.ConversationId ?? conv?.Id;

        if (string.IsNullOrEmpty(conversationId))
            throw new InvalidOperationException("Direct Line response contained no conversation ID.");

        _logger.LogInformation("Direct Line conversation started: {ConversationId}.", conversationId);
        return new ConversationSession
        {
            ConversationId = conversationId,
            ConnectorType  = "CopilotStudio",
        };
    }

    public async Task<AgentResponse> SendMessageAsync(
        ConversationSession session, string text, CancellationToken ct = default)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));

        var activity = new
        {
            type = "message",
            from = new { id = "test-user", name = "Test User" },
            text,
        };

        var json    = JsonSerializer.Serialize(activity, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"{DirectLineEndpoint}/conversations/{session.ConversationId}/activities");
        request.Content = content;

        var sw = Stopwatch.StartNew();
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Failed to send message: HTTP {(int)response.StatusCode}: {err}");
        }

        // Poll for bot reply with exponential backoff
        var reply = await PollForReplyAsync(session.ConversationId, text, ct);
        sw.Stop();

        return new AgentResponse
        {
            Text            = reply.Text ?? string.Empty,
            RawActivityJson = JsonSerializer.Serialize(reply, _jsonOptions),
            LatencyMs       = sw.ElapsedMilliseconds,
        };
    }

    public Task EndConversationAsync(ConversationSession session, CancellationToken ct = default)
    {
        _logger.LogDebug("Ending Direct Line conversation {ConversationId}.", session.ConversationId);
        return Task.CompletedTask;
    }

    // ── Private polling helpers ───────────────────────────────────────────────

    private async Task<DirectLineActivity> PollForReplyAsync(
        string conversationId, string userText, CancellationToken ct)
    {
        string? watermark = null;
        var timeout = DateTimeOffset.UtcNow.AddSeconds(_config.ReplyTimeoutSeconds);

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                int delaySeconds = BackoffBaseSeconds * (1 << (attempt - 1)); // 4, 8 ...
                _logger.LogDebug("Polling retry {Attempt}, waiting {Delay}s.", attempt, delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }

            while (DateTimeOffset.UtcNow < timeout)
            {
                ct.ThrowIfCancellationRequested();

                var url = $"{DirectLineEndpoint}/conversations/{conversationId}/activities";
                if (!string.IsNullOrEmpty(watermark))
                    url += $"?watermark={Uri.EscapeDataString(watermark)}";

                var req      = CreateAuthorizedRequest(HttpMethod.Get, url);
                var resp     = await _http.SendAsync(req, ct);
                var body     = await resp.Content.ReadAsStringAsync(ct);
                var actSet   = JsonSerializer.Deserialize<DirectLineActivitySet>(body, _jsonOptions)
                               ?? new DirectLineActivitySet();

                watermark = actSet.Watermark ?? watermark;

                var botActivities = actSet.Activities
                    .Where(a => a.Type == "message" && a.From?.Id != "test-user")
                    .Where(a => !string.IsNullOrEmpty(a.Text))
                    .ToList();

                if (botActivities.Count > 0)
                {
                    _logger.LogDebug("Received {Count} bot activities.", botActivities.Count);
                    return botActivities.Last(); // Return the most recent response
                }

                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }

        throw new TimeoutException(
            $"No bot reply received within {_config.ReplyTimeoutSeconds}s for conversation {conversationId}.");
    }

    private async Task<string> GenerateConversationTokenAsync(CancellationToken ct)
    {
        // Web Channel Security secret and Direct Line secret both use the same
        // /tokens/generate endpoint — the difference is only which secret is passed.
        var secret = _config.UseWebChannelSecret
            ? _config.WebChannelSecret
            : _config.DirectLineSecret;

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{DirectLineEndpoint}/tokens/generate");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var detail = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => _config.UseWebChannelSecret
                    ? "Invalid or expired Web Channel Security secret. Verify it in Copilot Studio → Settings → Security → Web channel security."
                    : "Invalid or expired Direct Line secret.",
                System.Net.HttpStatusCode.Forbidden => "Access denied — check bot ID and secret.",
                _ => $"HTTP {(int)response.StatusCode}: {body}",
            };
            throw new InvalidOperationException($"Failed to generate conversation token: {detail}");
        }

        var json  = await response.Content.ReadAsStringAsync(ct);
        var token = JsonSerializer.Deserialize<DirectLineTokenResponse>(json, _jsonOptions);

        return token?.Token
            ?? throw new InvalidOperationException("Token endpoint returned no token.");
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _conversationToken ?? _config.DirectLineSecret);
        return req;
    }
}
