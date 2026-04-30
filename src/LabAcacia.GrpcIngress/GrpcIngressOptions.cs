// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

namespace LabAcacia.GrpcIngress;

/// <summary>
/// Declares one NWP node that the gRPC ingress should expose to gRPC clients.
/// Identical in shape to <c>LabAcacia.McpIngress.NwpUpstream</c> — the two
/// ingresses share the upstream configuration surface so operators can lift one
/// config block and reuse it across protocols.
/// </summary>
public sealed record NwpUpstream
{
    /// <summary>Logical name, used by gRPC callers as <c>UpstreamContext.upstream</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Base URL of the NWP node (scheme + host + path prefix, no trailing slash).</summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>Optional default Agent NID forwarded as <c>X-NWP-Agent</c>
    /// when the gRPC request does not supply one.</summary>
    public string? AgentNid { get; init; }

    /// <summary>Optional static Authorization header for upstream calls.</summary>
    public string? AuthHeader { get; init; }
}

/// <summary>Configuration for the gRPC ingress server.</summary>
public sealed class GrpcIngressOptions
{
    /// <summary>One or more NWP nodes to expose.</summary>
    public required IReadOnlyList<NwpUpstream> Upstreams { get; set; }

    /// <summary>
    /// Max bytes the ingress will accept in a single Invoke / Query payload.
    /// Defaults to 4 MiB which matches gRPC's default max-receive-size.
    /// </summary>
    public int MaxPayloadBytes { get; set; } = 4 * 1024 * 1024;
}
