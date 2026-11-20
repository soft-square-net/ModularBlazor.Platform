---
title: gRPC in runtime modules (spike)
description: Whether a runtime module can serve gRPC-Web and native gRPC from its private pipeline, what the host must change, how it unloads, and how a Blazor WebAssembly client calls it.
draft: true
sidebar:
  order: 10
---

> **Status: spike, 2026-11-20.** Not a supported feature yet. The sample module, test client and scripts are in
> `scratchpad/grpcspike/` on the branch `spike/grpc-in-modules`.

## Answer

| Question | Result |
| --- | --- |
| gRPC-Web (HTTP/1.1) from a module | **Works.** Unary and server streaming, through the private pipeline (`IModulePipeline`), directly and through YARP with the lab's default cluster settings. It needs one workaround in the module, or a small host fix (below). |
| Native gRPC (HTTP/2) from a module | **Works, but only on an HTTP/2-only cleartext (h2c) endpoint.** The host's endpoints today are HTTP/1.1. `Http1AndHttp2` without TLS falls back to HTTP/1.1. Through YARP it also needs an h2c listener and a cluster pinned to HTTP/2. |
| Unload, upgrade | **Collected** within about 10 s in every variant: after unary and streaming calls, gRPC-Web and HTTP/2, through YARP, with per-method authorization, and after swaps. `HotUnload = false` is **not** needed. |
| Auth and tenancy | **Work unchanged.** Token, `X-Tenant`, token tenant claim and tenant mismatch (403) all apply. Permission checks work through `IAccessScopes` in the service and as per-method endpoint metadata. |
| Blazor WebAssembly client | gRPC-Web over `fetch` works cross-origin. Error statuses are only readable if `Grpc-Status` and `Grpc-Message` are in `Access-Control-Expose-Headers`. The host's CORS policy doesn't expose them; the module can add the header itself (verified). |

**Recommendation:** support **gRPC-Web only**, through `IModulePipeline`, for browser-facing module APIs that gain
from streaming or protobuf contracts. Make the RouteOptions fix in the host, and expose the gRPC headers in the
host's CORS policy. Leave native gRPC out until there is a service-to-service need. It needs a separate h2c port on
every pool and gateway changes, and inside the cluster it buys little over gRPC-Web. Carter/JSON stays the default
for ordinary CRUD.

## The sample

`scratchpad/grpcspike/WebApi.Modules.GrpcDemo` is an `IRuntimeModule` + `IModulePipeline` with prefix `grpcdemo`.
`Grpc.AspNetCore` and `Grpc.AspNetCore.Web` 2.71.0 are private package references. The `.proto` has `SayHello`
(unary), `Countdown` (server streaming) and `WhoAmI` (user, tenant and permission as the module sees them).

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration settings) => services.AddGrpc();

public void ConfigurePipeline(IApplicationBuilder app)
{
    CopyRouteConstraints(app.ApplicationServices);   // only without the host fix, see below
    app.Use(ExposeGrpcHeadersToBrowsers);              // see "Blazor WebAssembly client"
    app.UseRouting();
    app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
    app.UseEndpoints(e => e.MapGrpcService<GreeterService>());
}

public void ConfigureEndpoint(IEndpointConventionBuilder endpoint) => endpoint.TenantOptional();
```

gRPC method paths are `/{package}.{Service}/{Method}`. Under the pipeline's path base,
`/grpcdemo.Greeter/SayHello` is served at `/api/grpcdemo/grpcdemo.Greeter/SayHello`.

Build switches: `-p:GrpcDemoVersion=x.y.z` (upgrade), `-p:GrpcDemoHotUnload=false`, `-p:GrpcDemoNoWorkaround=true`
(rely on the host fix), `-p:GrpcDemoMethodAuth=true` (per-method permission). `starthost.sh` runs a standalone WebApi
on :5398 with the sample as a `Modules:Local` module. `calls.sh <baseUrl> <web|h2>` runs the call matrix with
`dotnet user-jwts` tokens.

## Finding 1: `AddGrpc()` breaks the pipeline's matcher (host fix)

The first gRPC-Web call failed with 500:

```
InvalidOperationException: The constraint reference 'grpcunimplemented' could not be resolved to a type.
   at DfaMatcherBuilder ... at ModulePipelines.InvokeAsync
```

`AddGrpc()` registers a route constraint with `Configure<RouteOptions>`, used by the endpoints it maps for unknown
methods. `ModulePipelines.AddPipelineServices` gives the pipeline its own
`Options.Create(new RouteOptions { ... })`, registered after the module's `ConfigureServices`. That replaces the
options pipeline, so no module `Configure<RouteOptions>` action ever runs. Any library that adds route constraints
this way hits the same error.

- **Host fix (on this branch, internal, no contract change):** `AddPipelineServices` runs the module's own
  `IConfigureOptions<RouteOptions>` instances on the module's RouteOptions before registering them. Verified: the
  sample built with `-p:GrpcDemoNoWorkaround=true` works.
- **Module workaround (works on today's host):** in `ConfigurePipeline`, run every resolvable
  `IConfigureOptions<RouteOptions>` on a scratch `RouteOptions` and copy the constraints it gained into the
  pipeline's RouteOptions (`CopyRouteConstraints` in the sample).

## Finding 2: native gRPC needs an h2c endpoint

The WebApi's Kestrel endpoints are plain `http://`. Over cleartext HTTP/2 can't be negotiated (no ALPN), so:

| Kestrel endpoint | gRPC-Web | Native gRPC |
| --- | --- | --- |
| Default (HTTP/1.1) | Works | `HTTP_1_1_REQUIRED` |
| `Protocols=Http1AndHttp2` | Works | `HTTP_1_1_REQUIRED`. Kestrel logs warning 64: "HTTP/2 is not enabled ... TLS is not enabled" and serves HTTP/1.1 only |
| Second endpoint, `Protocols=Http2` (h2c) | 400 (HTTP/1.1 refused) | **Works**: unary, streaming, auth, tenancy |

The tested host configuration had the existing HTTP/1.1 endpoint plus a second h2c port:

```
Kestrel__Endpoints__http__Url=http://+:8080
Kestrel__Endpoints__h2c__Url=http://+:8081
Kestrel__Endpoints__h2c__Protocols=Http2
```

What the host would have to change for native gRPC:

- The extra endpoint on every pool replica.
- The second port in the AppHost, Compose and Helm manifests.
- Health checks and service discovery for that port.
- **The gateway:**
  - An HTTP/2 listener: h2c inside the cluster, or TLS with ALPN at the edge.
  - A cluster per pool pointing at the h2c port, with `HttpRequest: { Version: "2", VersionPolicy: "RequestVersionExact" }`.
  - Routes that send gRPC traffic to it, by host or by port. The tested YARP matched `Hosts: ["localhost:5082"]`.

  YARP's default (`Version 2`, `RequestVersionOrLower`) falls back to HTTP/1.1 against `http://` destinations, which
  is fine for gRPC-Web and wrong for gRPC.

## Through YARP

The lab gateway on 5080 wasn't serving during the spike, and its replicas only accept identity-service tokens. So the
same image (`mcr.microsoft.com/dotnet/nightly/yarp:2.3-preview`) ran on 5081/5082 in front of the standalone host,
with the lab's shape: a catch-all route to a "core" cluster with default `HttpRequest` settings (see
`scratchpad/grpcspike/yarp.json`).

- **gRPC-Web via 5081:** every case behaves as it does directly. Streaming stays incremental: messages arrived about
  300 ms apart, as sent.
- **Native gRPC via 5082** (h2c listener, h2c cluster): works.
- **Native gRPC to the HTTP/1.1 listener:** `HTTP_1_1_REQUIRED`.

In the lab, `/api/grpcdemo/**` falls through to the core pool's catch-all route. A module placed in another pool needs
its own route, as for any module.

## Unload and upgrade

Measured on the standalone host: unload, then `GET /api/admin/modules/unloading` was polled.

| Scenario | Result |
| --- | --- |
| Unary and streaming calls over gRPC-Web and HTTP/2, direct and via YARP, then deactivate and unload | Listed at +5 s, **collected by +10 s** |
| Upgrade 1.0.0 → 1.0.1 by upload | No gap (next call answered by 1.0.1); old copy collected by +10 s |
| Upgrade **during** a 6 s server stream | The stream finished all 20 messages on the old copy. That copy was collected about 10 s after the stream ended |
| 1.1.0 with per-method authorization (`UseAuthorization` in the pipeline) | Collected by +10 s |
| 1.2.0 (final sample) after gRPC-Web, HTTP/2 and YARP calls | Collected by +10 s |

The unload's own 20 GC passes never caught it, as for every module. That matches the
[unloading](/backend/runtime-module-unloading/) results for Greeting and Pulse.

Why gRPC's caches don't pin the module:

- **Grpc.AspNetCore** (server, web, Grpc.Core.Api, Grpc.Net.Common) is private to the module and loads into its
  collectible context, so its statics die with the module. **Keep it out of the host.** The load context shares every
  assembly the host ships (`TRUSTED_PLATFORM_ASSEMBLIES`). If the host ever referenced Grpc.AspNetCore, the module
  would get the host's copy, and gRPC's per-service caches in the default context would hold module types.
- **Google.Protobuf is not private:** the host already ships 3.35.0, pulled in by `MySql.Data`. The module's copy
  (3.30.x) is ignored, and the generated messages run on the host's Google.Protobuf. That didn't pin in any scenario.
  It does mean a module can't use a Google.Protobuf **newer** than the host's: it gets the host's version, and a
  missing API fails at runtime.

**Caveat, long streams:** a swap or unload doesn't cancel streams already in flight. They keep running on the
swapped-out copy, whose services are already disposed, and they keep it in memory until the client disconnects. A
module with long-lived streams (subscriptions) should link its stream loops to a token it cancels when its services
are disposed.

## Auth and tenancy inside the pipeline

| Call | Result |
| --- | --- |
| No token | 401 from the host's catch-all; the client maps it to `Unauthenticated` |
| `editor1` (token tenant acme), `X-Tenant: acme` | OK; `WhoAmI`: user `editor1`, tenant `acme`, resolved by `header`, `grpcdemo.read` granted |
| `editor1`, no header | OK; tenant `acme` resolved by `claim` |
| `editor1`, `X-Tenant: globex` | 403 from the host (tenant mismatch); client sees `PermissionDenied` |
| `admin` (platform-admin, no grant) and a method that checks `IAccessScopes` | `RpcException(PermissionDenied)` from the service |
| Per-method permission: `PermissionRequirement` metadata on `Countdown`, `app.UseAuthorization()` in the pipeline | Admin: `SayHello` OK, `Countdown` 403. Editor: OK. Module still unloads |

- Coarse rules stay on the catch-all (`ConfigureEndpoint`: `RequirePermission`, `TenantOptional`). They apply to
  every gRPC method of the module.
- Per-method rules work two ways:
  - in code, through `IAccessScopes`: `throw new RpcException(new Status(StatusCode.PermissionDenied, ...))`;
  - or as endpoint metadata on the gRPC method, with `UseAuthorization()` in the pipeline. Use
    `MapGrpcService<T>().Add(b => ...)` filtered on `GrpcMethodMetadata`, and an inline policy. A named policy is not
    allowed.

  The host's `ModuleAuthorization.Validate` doesn't see pipeline endpoints. A permission used only there isn't
  checked against `Manifest.Permissions` at activation; declare it anyway.
- `ITenantContext` and `IAccessScopes` inject into the gRPC service as usual, since `RequestServices` is the module
  scope.
- The host's 401 and 403 are problem+json, not gRPC responses. `Grpc.Net.Client` maps the HTTP status to a gRPC
  status, so clients see `Unauthenticated` / `PermissionDenied` with a generic message.
- Under gRPC-Web, `HttpContext.Request.Protocol` reads `HTTP/2`: the gRPC-Web middleware rewrites it.

## Blazor WebAssembly client

The browser can only send gRPC-Web over `fetch`. Tested from a page on another origin (`http://localhost:5397`,
listed in `Cors:AllowedOrigins`) with hand-made gRPC-Web frames, which is what `Grpc.Net.Client.Web` sends in WASM:

- The preflight passed: `Authorization`, `Content-Type`, `X-Grpc-Web` and `X-Tenant` are allowed by `AllowAnyHeader`.
- Unary and streaming responses, including their trailer frames, were read correctly.
- **Errors weren't readable.** A service error is a trailers-only response: HTTP 200, empty body, `grpc-status` in the
  headers. Without `Access-Control-Expose-Headers` the page read `grpc-status = null`, so the client would report a
  broken response instead of `PermissionDenied`. After the module added
  `Access-Control-Expose-Headers: Grpc-Status,Grpc-Message,Grpc-Status-Details-Bin`, the page read `grpc-status = 7`
  and the message.
  - Host change (preferred): `.WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Status-Details-Bin")` on the
    default CORS policy.
  - Until then: the module's middleware in the sample.

Client setup (not run in the real Blazor client in this spike; the .NET console client uses the same handlers):

```csharp
// PathBaseHandler: GrpcChannel drops the address path, so put /api/{prefix} back in front of every call.
sealed class PathBaseHandler(string pathBase) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.RequestUri = new UriBuilder(request.RequestUri!) { Path = pathBase + request.RequestUri!.AbsolutePath }.Uri;
        return base.SendAsync(request, ct);
    }
}

var handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb,
    new PathBaseHandler("/api/grpcdemo") { InnerHandler = new HttpClientHandler() });
var channel = GrpcChannel.ForAddress(gatewayBaseUrl, new GrpcChannelOptions { HttpHandler = handler });
var client = new Greeter.GreeterClient(channel);
// Bearer token and X-Tenant: per-call Metadata, or a delegating handler like the platform's API clients use.
```

- In the platform client, the inner handler should be the existing authorization handler chain, so tokens and the
  tenant header come from the same place as for the NSwag clients.
- The generated client (`Grpc.Tools`, `GrpcServices="Client"`) belongs in the module's API client package, shipped
  like the NSwag clients. Grpc.Net.Client and Grpc.Net.Client.Web then load in the browser too, which adds to the
  download size.
- Use `GrpcWebMode.GrpcWeb` (binary).
- Server streaming in WASM needs browser response streaming. Recent `GrpcWebHandler` versions turn it on; verify it in
  the real client before relying on it.

## Not covered

- The real Blazor client with `Grpc.Net.Client.Web` (a hand-made `fetch` probe stood in for it).
- The lab's own gateway and multi-replica pools. The lab wasn't serving on 5080 and needs identity-service tokens; a
  local YARP of the same image was used.
- Client streaming and bidirectional streaming. They aren't possible over gRPC-Web, and native gRPC was only tested
  with unary and server streaming.
- gRPC reflection, health checks, compression, deadlines across a swap.
- TLS with ALPN instead of h2c.
