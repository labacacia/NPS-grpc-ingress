[English Version](./README.md) | 中文版

# LabAcacia.GrpcIngress

[![NuGet](https://img.shields.io/nuget/v/LabAcacia.GrpcIngress.svg)](https://www.nuget.org/packages/LabAcacia.GrpcIngress)

一个 **ASP.NET Core 库**，把一个或多个 **NPS NWP 节点** 暴露成一个 **gRPC
服务**。任何有 protoc 插件的语言写的 gRPC / protobuf 客户端都能读 NWP
Memory Node、调用 NWP Action / Complex / Gateway Node、列出可用 action，
而无需了解 NPS 原生 wire 格式。

- **协议**：gRPC over HTTP/2，服务包 `labacacia.grpc_ingress.v1`。
- **目标**：.NET 10，ASP.NET Core。
- **NWP 规范**：`spec/NPS-2-NWP.md` v0.5。

---

## 为什么是通用的 bytes 透传？

NWP 的 schema 在 **运行时** 通过 `AnchorFrame` + `/.schema` 声明。
传统的强类型 `.proto` 会把每个 NWP action 塞进一个在代码生成阶段根本
不存在的 schema 里——两头都不讨好。

本 bridge 走相反的路：只定义 **4 个小巧的通用 RPC**（`GetManifest`、
`Invoke`、`Query`、`ListActions`），payload 是 JSON 编码的 NWP 帧体，
以 `bytes` 透传。想要编译期强类型的调用方，可以从具体节点的
`AnchorFrame` 派生出自己的 `.proto`，叠在本服务之上。

| gRPC RPC      | NWP 调用                      | 说明                                                                  |
| ------------- | ----------------------------- | --------------------------------------------------------------------- |
| `GetManifest` | `GET /.nwm`                   | 返回原始 JSON + 便利字段 `node_type`。                                 |
| `Invoke`      | `POST /invoke`                | 把 `action_id` + 调用方的 `params_json` 包成 ActionFrame。            |
| `Query`       | `POST /query`                 | 原样透传 query JSON。                                                  |
| `ListActions` | `GET /actions`                | 返回 `/actions` 原始 body。                                            |

---

## 安装

```bash
dotnet add package LabAcacia.GrpcIngress
```

---

## 快速开始

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

客户端（C# 示例；其他语言编译同一个 `.proto` 即可）：

```csharp
using Grpc.Net.Client;
using LabAcacia.GrpcIngress.Generated;

using var channel = GrpcChannel.ForAddress("https://localhost:5001");
var client = new NwpBridge.NwpBridgeClient(channel);

var resp = await client.InvokeAsync(new InvokeRequest
{
    Ctx        = new UpstreamContext { Upstream = "orders", AgentNid = "nid:ed25519:..." },
    ActionId   = "orders.create",
    ParamsJson = Google.Protobuf.ByteString.CopyFromUtf8("""{"sku":"ABC-123","qty":1}"""),
});

Console.WriteLine($"http={resp.HttpStatus}, body={resp.BodyJson.ToStringUtf8()}");
```

---

## 错误映射

传输层故障会以 `RpcException` 抛出，映射如下：

| 上游 HTTP     | gRPC 状态             |
| ------------- | --------------------- |
| 400 / 422     | `INVALID_ARGUMENT`    |
| 401 / 403     | `PERMISSION_DENIED`   |
| 404           | `NOT_FOUND`           |
| 408           | `DEADLINE_EXCEEDED`   |
| 409           | `ABORTED`             |
| 429           | `RESOURCE_EXHAUSTED`  |
| 5xx           | `UNAVAILABLE`         |

`Invoke` 和 `Query` **故意不在上游 4xx 时抛异常**——把 `http_status`
作为数据返回，让调用方自己区分业务拒绝（入参错、任务不存在、限速）
和传输层故障（上游挂了），不必靠异常。

`GetManifest` 和 `ListActions` **在非 2xx 时抛异常**：这两个调用属于
发现路径，没有合理的中性表示。

---

## 本次不做

- **Server-streaming / bidi**：首个 alpha 只做 unary。`AlignStream` 异步
  任务输出今天可以通过 `Invoke` 轮询 `system.task.status` 访问。
  Server-streaming 的 `InvokeStream` 计划在 `0.2.0-alpha` 中加入。
- **Reflection / gRPC descriptor**：服务本身小到 `.proto` 生成物足以
  使用；`grpcurl` 用户直接指向 `.proto` 即可。
- **鉴权**：桥从配置转发 `Authorization` / `X-NWP-Agent` header；
  host 级别鉴权（TLS、gRPC interceptor、API gateway）由部署方自行叠加。

---

## 许可证

Apache-2.0。参见 [`LICENSE`](./LICENSE) 和 [`NOTICE`](./NOTICE)。
