English | [中文版](./README.cn.md)

# LabAcacia.GrpcIngress

[![NuGet](https://img.shields.io/nuget/v/LabAcacia.GrpcIngress.svg)](https://www.nuget.org/packages/LabAcacia.GrpcIngress)

An **ASP.NET Core library** that exposes one or more **NPS NWP nodes** as a
single **gRPC service**. gRPC / protobuf clients — from any language with a
protoc plugin — can read from NWP Memory Nodes, invoke NWP Action / Complex /
Gateway Nodes, and list available actions without knowing anything about
NPS's native wire format.

- **Protocol**: gRPC over HTTP/2, service package `labacacia.grpc_ingress.v1`.
- **Target**: .NET 10, ASP.NET Core.
- **NWP spec**: `spec/NPS-2-NWP.md` v0.5.

---

## Why a generic bytes-carrying service?

NWP schemas are declared at **runtime** via `AnchorFrame` + `/.schema`.
A conventional typed `.proto` would force every NWP action into a schema
that doesn't exist at code-gen time — the worst of both worlds.

This bridge takes the opposite approach: it defines **four small generic
RPCs** (`GetManifest`, `Invoke`, `Query`, `ListActions`) whose payloads are
JSON-encoded NWP frame bodies carried as `bytes`. Callers who want
compile-time typing can generate their own `.proto` files from a specific
node's `AnchorFrame` and layer them on top of this service.

| gRPC RPC      | NWP call                       | Notes                                                                 |
| ------------- | ------------------------------ | --------------------------------------------------------------------- |
| `GetManifest` | `GET /.nwm`                    | Returns raw JSON + shortcut `node_type`.                              |
| `Invoke`      | `POST /invoke`                 | Wraps `action_id` + caller's `params_json` as an ActionFrame.         |
| `Query`       | `POST /query`                  | Forwards the query JSON verbatim.                                     |
| `ListActions` | `GET /actions`                 | Returns the raw `/actions` body.                                      |

---

## Install

```bash
dotnet add package LabAcacia.GrpcIngress
```

---

## Quick start

```csharp
using LabAcacia.GrpcIngress;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpcIngress(o =>
{
    o.Upstreams = new[]
    {
        new NwpUpstream
        {
            Name    = "orders",
            BaseUrl = new Uri("https://api.example.com/orders"),
        },
        new NwpUpstream
        {
            Name    = "products",
            BaseUrl = new Uri("https://api.example.com/products"),
        },
    };
});

var app = builder.Build();
app.MapGrpcIngress();
app.Run();
```

On the client side (C# example; other languages compile the same `.proto`):

```csharp
using Grpc.Net.Client;
using LabAcacia.GrpcIngress.Generated;

using var channel = GrpcChannel.ForAddress("https://localhost:5001");
var client = new NwpIngress.NwpIngressClient(channel);

var resp = await client.InvokeAsync(new InvokeRequest
{
    Ctx        = new UpstreamContext { Upstream = "orders", AgentNid = "nid:ed25519:..." },
    ActionId   = "orders.create",
    ParamsJson = Google.Protobuf.ByteString.CopyFromUtf8("""{"sku":"ABC-123","qty":1}"""),
});

Console.WriteLine($"http={resp.HttpStatus}, body={resp.BodyJson.ToStringUtf8()}");
```

---

## Error mapping

Transport-level failures become `RpcException` with the following mapping:

| Upstream HTTP | gRPC status           |
| ------------- | --------------------- |
| 400 / 422     | `INVALID_ARGUMENT`    |
| 401 / 403     | `PERMISSION_DENIED`   |
| 404           | `NOT_FOUND`           |
| 408           | `DEADLINE_EXCEEDED`   |
| 409           | `ABORTED`             |
| 429           | `RESOURCE_EXHAUSTED`  |
| 5xx           | `UNAVAILABLE`         |

`Invoke` and `Query` intentionally **do not throw** on upstream 4xx — the
`http_status` field is returned as data so callers can distinguish
business-level rejections (bad input, task not found, rate-limited) from
transport-level failures (upstream down) without reading exceptions.

`GetManifest` and `ListActions` **do throw** on non-2xx: those responses
are part of the discovery path and there is no sensible neutral
representation.

---

## Not in scope (yet)

- **Server-streaming / bidi**: the first alpha is unary-only. `AlignStream`
  async task output is reachable today by polling `system.task.status` via
  `Invoke`. Server-streaming `InvokeStream` is planned for `0.2.0-alpha`.
- **Reflection / gRPC descriptors**: the service is intentionally small
  enough that generated `.proto` artifacts suffice; `grpcurl` users can
  point at the `.proto` directly.
- **Auth**: the ingress forwards `Authorization` / `X-NWP-Agent` headers
  from its configuration; host-level auth (TLS, gRPC interceptors, API
  gateway) is expected to be layered by the deployer.

---

## Further reading

- [gRPC Ingress deep dive](../../docs/compat/grpc-ingress.en.md) — bytes-passthrough rationale, dual error-mapping policy, multi-language clients, layering strong-typed proto, deployment notes
- [Compat ingresses overview](../../docs/compat/index.en.md) — when to pick MCP / A2A / gRPC

---

## License

Apache-2.0. See [`LICENSE`](../../LICENSE) and [`NOTICE`](../../NOTICE).
