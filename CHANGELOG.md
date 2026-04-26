English | [中文版](./CHANGELOG.cn.md)

# Changelog — gRPC Ingress (`LabAcacia.GrpcIngress`)

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Until NPS reaches v1.0 stable, every repository in the suite is synchronized to
the same pre-release version tag.

---

## [1.0.0-alpha.3] — 2026-04-26

### Renamed (BREAKING)

- Package renamed `LabAcacia.GrpcBridge` → `LabAcacia.GrpcIngress` per [NPS-CR-0001](https://github.com/labacacia/NPS-Dev/blob/dev/spec/cr/NPS-CR-0001-anchor-bridge-split.md). The new spec-level **Bridge Node** type (NWP §2A) carries the *NPS → external* direction; this package carries the **inverse** direction (external → NPS) and is therefore renamed `*Ingress`. The on-the-wire surface is identical to alpha.2; only the assembly name + namespace changed. Consumers update `<PackageReference Include="LabAcacia.GrpcBridge"/>` → `LabAcacia.GrpcIngress` and the `using LabAcacia.GrpcBridge;` import.
- The corresponding GitHub repository was renamed `labacacia/NPS-grpc-bridge` → `labacacia/NPS-grpc-ingress`. GitHub redirects the old URL automatically; existing clones can update with `git remote set-url origin https://github.com/labacacia/NPS-grpc-ingress.git`.
- Tests still pass at the same count as alpha.2 (no functional change beyond rename).

### Synced

- Version bumped 1.0.0-alpha.2 → 1.0.0-alpha.3 in lockstep with the rest of the NPS suite.

---

## [0.1.0-alpha.1] — 2026-04-21

### Added

- Initial release of `LabAcacia.GrpcIngress`, an ASP.NET Core library that
  exposes one or more NWP nodes (Memory / Action / Complex / Gateway) as a
  gRPC service.
- Generic passthrough `.proto` (`nwp_bridge.proto`) with bytes-carrying
  payloads — avoids forcing a compile-time schema on top of NWP's runtime
  `AnchorFrame` model.
- Unary RPCs: `GetManifest`, `Invoke`, `Query`, `ListActions`.
- DI extensions: `AddGrpcIngress(...)` + `MapGrpcIngress()`.
- Forwards `agent_nid`, `idempotency_key`, and W3C `traceparent` from
  `UpstreamContext` to upstream HTTP calls.
- Maps NWP/HTTP failure statuses to gRPC status codes
  (`400/422 → INVALID_ARGUMENT`, `401/403 → PERMISSION_DENIED`,
  `404 → NOT_FOUND`, `429 → RESOURCE_EXHAUSTED`, `5xx → UNAVAILABLE`).
- 15 unit tests covering every RPC, header forwarding, error mapping,
  and construction guards.

### Motivation

Responds to a 2026-04-20 review comment arguing that NPS should be
approachable from the existing gRPC / protobuf ecosystem. The bridge is
**generic** — callers keep their dynamic NWP payloads as `bytes` — so
the two protocol philosophies (compile-time vs. runtime schema) coexist
without forcing a conversion on either side.

[0.1.0-alpha.1]: https://github.com/LabAcacia/nps/releases/tag/v0.1.0-alpha.1
