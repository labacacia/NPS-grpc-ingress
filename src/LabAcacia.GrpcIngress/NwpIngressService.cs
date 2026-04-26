// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using LabAcacia.GrpcIngress.Generated;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabAcacia.GrpcIngress;

/// <summary>
/// Implementation of the generated <see cref="NwpIngress.NwpIngressBase"/> service.
/// Each RPC resolves the configured upstream by <c>ctx.upstream</c>, forwards the
/// request verbatim (bytes in, bytes out), and maps HTTP / transport errors to
/// gRPC status codes.
/// </summary>
public sealed class NwpIngressService : NwpIngress.NwpIngressBase
{
    private readonly IReadOnlyDictionary<string, NwpUpstreamClient> _clients;
    private readonly GrpcIngressOptions _options;
    private readonly ILogger _log;

    public NwpIngressService(
        GrpcIngressOptions options,
        IReadOnlyDictionary<string, NwpUpstreamClient> clients,
        ILogger<NwpIngressService>? logger = null)
    {
        _options = options;
        _clients = clients;
        _log     = (ILogger?)logger ?? NullLogger.Instance;
    }

    // ── GetManifest ──────────────────────────────────────────────────────────

    public override async Task<ManifestResponse> GetManifest(ManifestRequest request, ServerCallContext context)
    {
        var client = ResolveUpstream(request.Ctx);

        using var resp = await client.GetNwmAsync(request.Ctx?.AgentNid, request.Ctx?.Traceparent, context.CancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(context.CancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw MapUpstreamFailure(resp.StatusCode, bytes, "GetManifest");

        var nodeType = TryReadString(bytes, "node_type") ?? string.Empty;
        return new ManifestResponse
        {
            NwmJson  = ByteString.CopyFrom(bytes),
            NodeType = nodeType,
        };
    }

    // ── Invoke ───────────────────────────────────────────────────────────────

    public override async Task<InvokeResponse> Invoke(InvokeRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.ActionId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "action_id is required"));
        if (request.ParamsJson.Length > _options.MaxPayloadBytes)
            throw new RpcException(new Status(StatusCode.ResourceExhausted,
                $"params_json exceeds {_options.MaxPayloadBytes} bytes"));

        var client = ResolveUpstream(request.Ctx);

        // Build ActionFrame body from action_id + raw params JSON.
        byte[] body = BuildActionFrame(request.ActionId, request.ParamsJson);

        using var resp = await client.PostInvokeAsync(
            body,
            request.Ctx?.AgentNid,
            request.Ctx?.IdempotencyKey,
            request.Ctx?.Traceparent,
            context.CancellationToken);

        var respBytes = await resp.Content.ReadAsByteArrayAsync(context.CancellationToken);
        var taskId = (int)resp.StatusCode == 202 ? (TryReadString(respBytes, "task_id") ?? string.Empty) : string.Empty;

        return new InvokeResponse
        {
            HttpStatus = (int)resp.StatusCode,
            BodyJson   = ByteString.CopyFrom(respBytes),
            TaskId     = taskId,
        };
    }

    // ── Query ────────────────────────────────────────────────────────────────

    public override async Task<QueryResponse> Query(QueryRequest request, ServerCallContext context)
    {
        if (request.QueryJson.Length > _options.MaxPayloadBytes)
            throw new RpcException(new Status(StatusCode.ResourceExhausted,
                $"query_json exceeds {_options.MaxPayloadBytes} bytes"));

        var client = ResolveUpstream(request.Ctx);

        using var resp = await client.PostQueryAsync(
            request.QueryJson.ToByteArray(),
            request.Ctx?.AgentNid,
            request.Ctx?.Traceparent,
            context.CancellationToken);

        var respBytes = await resp.Content.ReadAsByteArrayAsync(context.CancellationToken);

        return new QueryResponse
        {
            HttpStatus = (int)resp.StatusCode,
            BodyJson   = ByteString.CopyFrom(respBytes),
        };
    }

    // ── ListActions ──────────────────────────────────────────────────────────

    public override async Task<ActionsResponse> ListActions(ActionsRequest request, ServerCallContext context)
    {
        var client = ResolveUpstream(request.Ctx);

        using var resp = await client.GetActionsAsync(request.Ctx?.AgentNid, request.Ctx?.Traceparent, context.CancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(context.CancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw MapUpstreamFailure(resp.StatusCode, bytes, "ListActions");

        return new ActionsResponse { ActionsJson = ByteString.CopyFrom(bytes) };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private NwpUpstreamClient ResolveUpstream(UpstreamContext? ctx)
    {
        if (ctx is null || string.IsNullOrEmpty(ctx.Upstream))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ctx.upstream is required"));

        if (!_clients.TryGetValue(ctx.Upstream, out var client))
            throw new RpcException(new Status(StatusCode.NotFound, $"Unknown upstream '{ctx.Upstream}'"));

        return client;
    }

    /// <summary>
    /// Wrap the caller-supplied params bytes in the standard ActionFrame JSON shape.
    /// Re-encodes minimally: <c>{"action_id":"...","params":&lt;raw&gt;}</c>.
    /// </summary>
    private static byte[] BuildActionFrame(string actionId, ByteString paramsJson)
    {
        // Default when caller sends no params: `{}`.
        var rawParams = paramsJson.Length > 0 ? paramsJson.ToByteArray() : "{}"u8.ToArray();

        using var ms = new MemoryStream(rawParams.Length + 64);
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("action_id", actionId);
            writer.WritePropertyName("params");

            // Validate that rawParams is valid JSON; else treat as invalid argument.
            try
            {
                using var doc = JsonDocument.Parse(rawParams);
                doc.RootElement.WriteTo(writer);
            }
            catch (JsonException jex)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"params_json is not valid JSON: {jex.Message}"));
            }

            writer.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Extract a top-level string property from a JSON payload. Returns <c>null</c>
    /// if the payload is not JSON, the property is missing, or it is not a string.
    /// </summary>
    private static string? TryReadString(byte[] json, string property)
    {
        if (json.Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RpcException MapUpstreamFailure(
        System.Net.HttpStatusCode status, byte[] body, string op)
    {
        var code = (int)status switch
        {
            400 or 422                  => StatusCode.InvalidArgument,
            401 or 403                  => StatusCode.PermissionDenied,
            404                         => StatusCode.NotFound,
            408                         => StatusCode.DeadlineExceeded,
            409                         => StatusCode.Aborted,
            429                         => StatusCode.ResourceExhausted,
            >= 500 and < 600            => StatusCode.Unavailable,
            _                           => StatusCode.Unknown,
        };

        var snippet = body.Length == 0 ? string.Empty
            : ": " + System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 256));
        return new RpcException(new Status(code, $"{op} upstream returned {(int)status}{snippet}"));
    }
}
