[English Version](./CHANGELOG.md) | 中文版

# 更新日志 — gRPC Bridge (`LabAcacia.GrpcIngress`)

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [SemVer](https://semver.org/lang/zh-CN/spec/v2.0.0.html)。

NPS 进入 v1.0 稳定版之前，套件内所有仓库统一对齐到同一个 pre-release
版本号。

---

## [1.0.0-alpha.4] —— 2026-04-30

### 同步

- 版本随 NPS 套件升至 1.0.0-alpha.4，本包自身无功能变更。
- `LabAcacia.NPS.NWP` 依赖跟进至 alpha.4，SDK 层带来
  `LabAcacia.NPS.NWP.Anchor` topology 查询类型（NPS-CR-0002）。
  alpha.4 时 gRPC Ingress 不通过 `nwp_ingress.proto` 暴露这些查询。
- 15 tests 仍全绿。

### 摘要

- 把 NWP Memory / Action / Complex Node 暴露为按 `nwp_ingress.proto`
  schema 实现的 gRPC 服务，外部 gRPC 客户端不依赖 NPS SDK 即可调用
  NPS Node。

---

## [1.0.0-alpha.3] —— 2026-04-26

### 重命名（破坏性）

- 包名 `LabAcacia.GrpcBridge` → `LabAcacia.GrpcIngress`，详见 [NPS-CR-0001](https://github.com/labacacia/NPS-Dev/blob/dev/spec/cr/NPS-CR-0001-anchor-bridge-split.md)。新的规范层 **Bridge Node** 类型（NWP §2A）承担 *NPS → 外部* 方向；本包承担**相反**方向（外部 → NPS），故改名 `*Ingress`。线上格式与 alpha.2 完全一致，只是 assembly 名 + 命名空间变了。消费方需更新 `<PackageReference Include="LabAcacia.GrpcBridge"/>` → `LabAcacia.GrpcIngress` 及 `using LabAcacia.GrpcBridge;` 导入。
- 对应 GitHub 仓库 `labacacia/NPS-grpc-bridge` 已重命名为 `labacacia/NPS-grpc-ingress`。GitHub 自动重定向旧 URL；已 clone 的本地仓库用 `git remote set-url origin https://github.com/labacacia/NPS-grpc-ingress.git` 更新即可。
- 测试通过数与 alpha.2 一致（除重命名外无功能变更）。

### 同步

- 版本由 1.0.0-alpha.2 升至 1.0.0-alpha.3，与 NPS 套件其余仓库保持一致。

---

## [0.1.0-alpha.1] — 2026-04-21

### 新增

- `LabAcacia.GrpcIngress` 首次发布：ASP.NET Core 库，把一个或多个 NWP
  节点（Memory / Action / Complex / Gateway）暴露为 gRPC 服务。
- 通用透传 `.proto`（`nwp_bridge.proto`），payload 用 bytes 承载——
  回避了在 NWP 运行时 `AnchorFrame` 模型之上强加编译期 schema 的
  做法。
- Unary RPC：`GetManifest`、`Invoke`、`Query`、`ListActions`。
- DI 扩展：`AddGrpcIngress(...)` + `MapGrpcIngress()`。
- 从 `UpstreamContext` 把 `agent_nid`、`idempotency_key`、W3C
  `traceparent` 透传到上游 HTTP 调用。
- 把 NWP/HTTP 故障状态码映射到 gRPC status：
  `400/422 → INVALID_ARGUMENT`、`401/403 → PERMISSION_DENIED`、
  `404 → NOT_FOUND`、`429 → RESOURCE_EXHAUSTED`、`5xx → UNAVAILABLE`。
- 15 个单元测试覆盖各 RPC、header 透传、错误映射、构造 guard。

### 动机

回应 2026-04-20 的评审意见：NPS 应该能被现有 gRPC / protobuf 生态直接
消费。本桥采用**通用**形态——调用方把动态 NWP payload 继续以 `bytes`
携带——让两种哲学（编译期 vs 运行时 schema）共存，不强迫任一侧转换。

[1.0.0-alpha.4]: https://gitee.com/labacacia/NPS-grpc-ingress/releases/tag/v1.0.0-alpha.4
[1.0.0-alpha.3]: https://gitee.com/labacacia/NPS-grpc-ingress/releases/tag/v1.0.0-alpha.3
[0.1.0-alpha.1]: https://github.com/LabAcacia/nps/releases/tag/v0.1.0-alpha.1
