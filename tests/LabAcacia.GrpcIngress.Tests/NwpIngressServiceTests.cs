// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using LabAcacia.GrpcIngress;
using LabAcacia.GrpcIngress.Generated;
using Xunit;

namespace LabAcacia.GrpcIngress.Tests;

/// <summary>
/// Unit tests for <see cref="NwpIngressService"/>. The upstream NWP node is replaced
/// by a <see cref="StubHandler"/> so tests run without Kestrel or real HTTP I/O.
/// The gRPC <c>ServerCallContext</c> is fabricated with
/// <see cref="TestServerCallContext.Create"/>.
/// </summary>
public sealed class NwpIngressServiceTests
{
    // ── GetManifest ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetManifest_ForwardsNwmBodyAndExtractsNodeType()
    {
        var (svc, handler) = BuildService(StubHandler.ForActionNode());

        var resp = await svc.GetManifest(
            new ManifestRequest { Ctx = Ctx("orders") },
            TestServerCallContext.Create());

        Assert.Equal("action", resp.NodeType);
        Assert.Contains("urn:nps:node:test:orders", resp.NwmJson.ToStringUtf8());
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/.nwm"));
    }

    [Fact]
    public async Task GetManifest_UnknownUpstream_ThrowsNotFound()
    {
        var (svc, _) = BuildService(StubHandler.ForActionNode());

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.GetManifest(new ManifestRequest { Ctx = Ctx("missing") }, TestServerCallContext.Create()));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetManifest_MissingCtx_ThrowsInvalidArgument()
    {
        var (svc, _) = BuildService(StubHandler.ForActionNode());

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.GetManifest(new ManifestRequest { Ctx = new UpstreamContext() }, TestServerCallContext.Create()));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task GetManifest_Upstream500_MappedToUnavailable()
    {
        var handler = StubHandler.ForActionNode();
        handler.NwmStatus = HttpStatusCode.InternalServerError;
        handler.NwmBody   = "boom";
        var (svc, _) = BuildService(handler);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.GetManifest(new ManifestRequest { Ctx = Ctx("orders") }, TestServerCallContext.Create()));
        Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
    }

    // ── Invoke ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invoke_PostsActionFrame_AndReturnsBodyVerbatim()
    {
        var (svc, handler) = BuildService(StubHandler.ForActionNode());

        var req = new InvokeRequest
        {
            Ctx        = Ctx("orders", agentNid: "nid:ed25519:test"),
            ActionId   = "orders.create",
            ParamsJson = ByteString.CopyFromUtf8("""{"sku":"ABC-123"}"""),
        };

        var resp = await svc.Invoke(req, TestServerCallContext.Create());

        Assert.Equal(200, resp.HttpStatus);
        Assert.Contains("\"ok\":true", resp.BodyJson.ToStringUtf8());

        var (path, body) = handler.RequestBodies.Single(b => b.Path.EndsWith("/invoke"));
        Assert.EndsWith("/invoke", path);
        Assert.Contains("\"action_id\":\"orders.create\"", body);
        Assert.Contains("\"sku\":\"ABC-123\"", body);

        // Agent NID header forwarded.
        var invoke = handler.Requests.First(r => r.RequestUri!.AbsolutePath.EndsWith("/invoke"));
        Assert.Equal("nid:ed25519:test", invoke.Headers.GetValues("X-NWP-Agent").Single());
    }

    [Fact]
    public async Task Invoke_WithIdempotencyKey_ForwardsHeader()
    {
        var (svc, handler) = BuildService(StubHandler.ForActionNode());

        var ctx = Ctx("orders");
        ctx.IdempotencyKey = "idem-42";

        await svc.Invoke(
            new InvokeRequest { Ctx = ctx, ActionId = "orders.create", ParamsJson = ByteString.CopyFromUtf8("{}") },
            TestServerCallContext.Create());

        var invoke = handler.Requests.First(r => r.RequestUri!.AbsolutePath.EndsWith("/invoke"));
        Assert.Equal("idem-42", invoke.Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public async Task Invoke_Async202_ExtractsTaskId()
    {
        var handler = StubHandler.ForActionNode();
        handler.InvokeStatus = HttpStatusCode.Accepted;
        handler.InvokeBody   = """{"task_id":"t-xyz","status":"pending"}""";
        var (svc, _) = BuildService(handler);

        var resp = await svc.Invoke(
            new InvokeRequest { Ctx = Ctx("orders"), ActionId = "orders.create", ParamsJson = ByteString.CopyFromUtf8("{}") },
            TestServerCallContext.Create());

        Assert.Equal(202, resp.HttpStatus);
        Assert.Equal("t-xyz", resp.TaskId);
    }

    [Fact]
    public async Task Invoke_MissingActionId_ThrowsInvalidArgument()
    {
        var (svc, _) = BuildService(StubHandler.ForActionNode());

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.Invoke(new InvokeRequest { Ctx = Ctx("orders"), ActionId = "" }, TestServerCallContext.Create()));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Invoke_InvalidParamsJson_ThrowsInvalidArgument()
    {
        var (svc, _) = BuildService(StubHandler.ForActionNode());

        var ex = await Assert.ThrowsAsync<RpcException>(() => svc.Invoke(
            new InvokeRequest
            {
                Ctx        = Ctx("orders"),
                ActionId   = "orders.create",
                ParamsJson = ByteString.CopyFromUtf8("{not json"),
            },
            TestServerCallContext.Create()));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Invoke_PayloadTooLarge_ThrowsResourceExhausted()
    {
        var (svc, _) = BuildService(StubHandler.ForActionNode(), maxPayload: 64);

        // 128-byte payload exceeds the 64-byte cap.
        var big = new string('x', 128);

        var ex = await Assert.ThrowsAsync<RpcException>(() => svc.Invoke(
            new InvokeRequest
            {
                Ctx        = Ctx("orders"),
                ActionId   = "orders.create",
                ParamsJson = ByteString.CopyFromUtf8($"\"{big}\""),
            },
            TestServerCallContext.Create()));
        Assert.Equal(StatusCode.ResourceExhausted, ex.StatusCode);
    }

    [Fact]
    public async Task Invoke_UpstreamReturns404_ReportsInResponseNot_RpcException()
    {
        // Invoke forwards the upstream status as a data field; only transport-level
        // failures (which we don't have in a stub) would become RpcException. This
        // keeps callers in control of how to treat 4xx business errors.
        var handler = StubHandler.ForActionNode();
        handler.InvokeStatus = HttpStatusCode.NotFound;
        handler.InvokeBody   = """{"error":"NWP-ACTION-NOT-FOUND"}""";
        var (svc, _) = BuildService(handler);

        var resp = await svc.Invoke(
            new InvokeRequest { Ctx = Ctx("orders"), ActionId = "orders.missing", ParamsJson = ByteString.CopyFromUtf8("{}") },
            TestServerCallContext.Create());

        Assert.Equal(404, resp.HttpStatus);
        Assert.Contains("NWP-ACTION-NOT-FOUND", resp.BodyJson.ToStringUtf8());
    }

    // ── Query ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Query_PostsQueryBodyVerbatim_AndReturnsUpstreamBody()
    {
        var (svc, handler) = BuildService(StubHandler.ForMemoryNode());

        var body = ByteString.CopyFromUtf8("""{"limit":50}""");
        var resp = await svc.Query(
            new QueryRequest { Ctx = Ctx("products"), QueryJson = body },
            TestServerCallContext.Create());

        Assert.Equal(200, resp.HttpStatus);
        Assert.Contains("\"count\"", resp.BodyJson.ToStringUtf8());

        var (path, captured) = handler.RequestBodies.Single(b => b.Path.EndsWith("/query"));
        Assert.Equal("""{"limit":50}""", captured);
    }

    // ── ListActions ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListActions_ReturnsActionsBody()
    {
        var (svc, _) = BuildService(StubHandler.ForActionNode());

        var resp = await svc.ListActions(
            new ActionsRequest { Ctx = Ctx("orders") },
            TestServerCallContext.Create());

        Assert.Contains("orders.create", resp.ActionsJson.ToStringUtf8());
        Assert.Contains("orders.cancel", resp.ActionsJson.ToStringUtf8());
    }

    // ── Construction guards ──────────────────────────────────────────────────

    [Fact]
    public void Options_DuplicateUpstreamNames_Throws()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddGrpcIngress(o => o.Upstreams = new[]
            {
                new NwpUpstream { Name = "a", BaseUrl = new Uri("https://a.test") },
                new NwpUpstream { Name = "a", BaseUrl = new Uri("https://b.test") },
            }));
    }

    [Fact]
    public void Options_EmptyUpstreams_Throws()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddGrpcIngress(_ => { /* leave Upstreams empty */ }));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static UpstreamContext Ctx(string upstream, string? agentNid = null)
        => new() { Upstream = upstream, AgentNid = agentNid ?? string.Empty };

    private static (NwpIngressService svc, StubHandler handler) BuildService(
        StubHandler handler, int? maxPayload = null)
    {
        var upstream = handler.NodeType == "memory"
            ? new NwpUpstream { Name = "products", BaseUrl = new Uri("https://memory.test/products") }
            : new NwpUpstream { Name = "orders",   BaseUrl = new Uri("https://action.test/orders") };

        var opts = new GrpcIngressOptions { Upstreams = new[] { upstream } };
        if (maxPayload is int mp) opts.MaxPayloadBytes = mp;

        var client = new NwpUpstreamClient(new HttpClient(handler), upstream);
        var clients = new Dictionary<string, NwpUpstreamClient> { [upstream.Name] = client };
        return (new NwpIngressService(opts, clients), handler);
    }
}

// ── Stub upstream ────────────────────────────────────────────────────────────

/// <summary>
/// In-memory HTTP handler that mimics an NWP Memory or Action Node. Mirrors
/// <c>LabAcacia.McpIngress.Tests.StubHandler</c> so the two ingresses. test fixtures
/// stay recognisably similar.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();

    public List<(string Path, string Body)> RequestBodies { get; } = new();

    public string NodeType { get; init; } = "action";

    public string NwmBody     { get; set; } = string.Empty;
    public HttpStatusCode NwmStatus { get; set; } = HttpStatusCode.OK;
    public string ActionsBody { get; set; } = string.Empty;
    public string QueryBody   { get; set; } = string.Empty;

    public HttpStatusCode InvokeStatus { get; set; } = HttpStatusCode.OK;
    public string         InvokeBody   { get; set; } = """{"anchor_ref":null,"count":1,"data":[{"ok":true}],"token_est":0}""";

    public static StubHandler ForMemoryNode() => new()
    {
        NodeType  = "memory",
        NwmBody   = """{"nwp":"0.4","node_id":"urn:nps:node:test:products","node_type":"memory","display_name":"Products"}""",
        QueryBody = """{"anchor_ref":"sha256:x","count":2,"data":[{"id":1},{"id":2}],"token_est":4}""",
    };

    public static StubHandler ForActionNode() => new()
    {
        NodeType    = "action",
        NwmBody     = """{"nwp":"0.4","node_id":"urn:nps:node:test:orders","node_type":"action","display_name":"Orders"}""",
        ActionsBody = """
        {
          "actions": {
            "orders.create": { "description": "Create an order", "async": true },
            "orders.cancel": { "description": "Cancel an order", "async": false }
          }
        }
        """,
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(ct);
            RequestBodies.Add((path, body));
        }
        Requests.Add(request);

        return path switch
        {
            var p when p.EndsWith("/.nwm")    => Text(NwmBody, NwmStatus),
            var p when p.EndsWith("/actions") => Text(ActionsBody),
            var p when p.EndsWith("/query")   => Text(QueryBody),
            var p when p.EndsWith("/invoke")  => new HttpResponseMessage(InvokeStatus)
            {
                Content = new StringContent(InvokeBody, Encoding.UTF8, "application/nwp-capsule"),
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

// ── Minimal ServerCallContext for unit tests ─────────────────────────────────

/// <summary>
/// Grpc.Core ships <c>TestServerCallContext</c> via <c>Grpc.Core.Testing</c>, but
/// pulling that in just for unit tests is heavy. This re-implementation matches
/// the handful of properties our service actually reads.
/// </summary>
internal sealed class TestServerCallContext : ServerCallContext
{
    public static TestServerCallContext Create() => new();

    protected override string MethodCore => "/test/Method";
    protected override string HostCore => "localhost";
    protected override string PeerCore => "127.0.0.1";
    protected override DateTime DeadlineCore => DateTime.UtcNow.AddSeconds(30);
    protected override Metadata RequestHeadersCore { get; } = new();
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata ResponseTrailersCore { get; } = new();
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new("none", new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotSupportedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
