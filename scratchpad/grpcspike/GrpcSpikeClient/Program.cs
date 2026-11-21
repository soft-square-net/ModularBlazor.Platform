// Usage: GrpcSpikeClient <baseUrl> <web|h2> [token] [tenant] [permission]
//   baseUrl  e.g. http://localhost:5398/api/grpcdemo (the module's path base)
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using GrpcDemo.Protos;

var baseUrl = new Uri(args[0].TrimEnd('/') + "/");
var mode = args.Length > 1 ? args[1] : "web";
var token = args.Length > 2 && args[2] != "-" ? args[2] : null;
var tenant = args.Length > 3 && args[3] != "-" ? args[3] : null;
var permission = args.Length > 4 ? args[4] : "";

// GrpcChannel ignores the path of its address: gRPC calls go to /{package}.{Service}/{Method}. The module serves them
// under its path base (/api/{prefix}), so a handler puts the base path back in front.
HttpMessageHandler handler = new PathBaseHandler(baseUrl.AbsolutePath) { InnerHandler = new SocketsHttpHandler() };
if (mode == "web") handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, handler) { HttpVersion = new Version(1, 1) };

using var channel = GrpcChannel.ForAddress(new Uri(baseUrl.GetLeftPart(UriPartial.Authority)), new GrpcChannelOptions
{
    HttpHandler = handler,
    // Native gRPC over plain http needs prior-knowledge HTTP/2 (h2c); gRPC-Web is HTTP/1.1.
    HttpVersion = mode == "web" ? new Version(1, 1) : new Version(2, 0),
    HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact
});
var client = new Greeter.GreeterClient(channel);
var headers = new Metadata();
if (token is not null) headers.Add("Authorization", $"Bearer {token}");
if (tenant is not null) headers.Add("X-Tenant", tenant);

try
{
    var hello = await client.SayHelloAsync(new HelloRequest { Name = "spike" }, headers);
    Console.WriteLine($"SayHello: {hello.Message} (module {hello.ModuleVersion})");

    var sw = Stopwatch.StartNew();
    using var stream = client.Countdown(new CountdownRequest { Count = int.TryParse(Environment.GetEnvironmentVariable("GRPC_COUNT"), out var n) ? n : 3, IntervalMs = 300 }, headers);
    await foreach (var r in stream.ResponseStream.ReadAllAsync())
        Console.WriteLine($"Countdown: {r.Value} at {sw.ElapsedMilliseconds} ms");

    var who = await client.WhoAmIAsync(new WhoAmIRequest { Permission = permission }, headers);
    Console.WriteLine($"WhoAmI: {who}");
}
catch (RpcException e)
{
    Console.WriteLine($"RpcException: {e.StatusCode} {e.Status.Detail}");
    foreach (var t in e.Trailers) Console.WriteLine($"  trailer {t.Key}={t.Value}");
    Environment.ExitCode = 1;
}

sealed class PathBaseHandler(string pathBase) : DelegatingHandler
{
    private readonly string _pathBase = pathBase.TrimEnd('/');

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        request.RequestUri = new UriBuilder(uri) { Path = _pathBase + uri.AbsolutePath }.Uri;
        return base.SendAsync(request, ct);
    }
}
