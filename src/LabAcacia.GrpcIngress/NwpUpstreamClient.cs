// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Text;

namespace LabAcacia.GrpcIngress;

/// <summary>
/// Thin typed client for a single upstream NWP node. Forwards bytes verbatim
/// — the ingress does not re-encode JSON payloads between the gRPC wire and
/// the upstream HTTP call.
/// </summary>
public sealed class NwpUpstreamClient
{
    private readonly HttpClient _http;
    private readonly NwpUpstream _up;

    public NwpUpstreamClient(HttpClient http, NwpUpstream upstream)
    {
        _http = http;
        _up   = upstream;
    }

    public NwpUpstream Upstream => _up;

    public Task<HttpResponseMessage> GetNwmAsync(string? agentNid, string? traceparent, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, "/.nwm", body: null, idempotencyKey: null, agentNid, traceparent, ct);

    public Task<HttpResponseMessage> GetActionsAsync(string? agentNid, string? traceparent, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, "/actions", body: null, idempotencyKey: null, agentNid, traceparent, ct);

    public Task<HttpResponseMessage> PostQueryAsync(
        byte[] body, string? agentNid, string? traceparent, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/query", body, idempotencyKey: null, agentNid, traceparent, ct);

    public Task<HttpResponseMessage> PostInvokeAsync(
        byte[] body, string? agentNid, string? idempotencyKey, string? traceparent, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/invoke", body, idempotencyKey, agentNid, traceparent, ct);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string subPath,
        byte[]? body,
        string? idempotencyKey,
        string? agentNid,
        string? traceparent,
        CancellationToken ct)
    {
        var url = new Uri(_up.BaseUrl.ToString().TrimEnd('/') + subPath);
        using var req = new HttpRequestMessage(method, url);

        if (body is not null)
        {
            req.Content = new ByteArrayContent(body);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/nwp-frame")
            {
                CharSet = "utf-8",
            };
        }

        // Caller-supplied NID wins; fall back to upstream default if any.
        var nid = !string.IsNullOrEmpty(agentNid) ? agentNid : _up.AgentNid;
        if (!string.IsNullOrEmpty(nid))
            req.Headers.Add("X-NWP-Agent", nid);

        if (!string.IsNullOrEmpty(idempotencyKey))
            req.Headers.Add("Idempotency-Key", idempotencyKey);

        if (!string.IsNullOrEmpty(traceparent))
            req.Headers.Add("traceparent", traceparent);

        if (!string.IsNullOrEmpty(_up.AuthHeader))
            req.Headers.TryAddWithoutValidation("Authorization", _up.AuthHeader);

        return await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
    }
}
