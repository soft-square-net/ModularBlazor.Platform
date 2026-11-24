using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using soft.square.net.ModularBlazor.Abstractions.MudTail;
using soft.square.net.ModularBlazor.BlzWebClient.Auth;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// Development only: the inner loop of a module repo's playground. The WebApi's client-modules module reinstalls a local
/// client module (ClientModules:Local) as soon as it is rebuilt and says so on <c>GET api/client-modules/dev/events</c>
/// (Server-Sent Events); this reloads the module, and the modules that depend on it, in place and renders the current
/// page again: save, build, and the page shows the change with the user still signed in.
/// <para>
/// The WebApi answers 404 when it has no local client modules (or is not in Development): then this stops for the page's
/// life. A dropped stream is opened again; on every (re)connect the manifest is checked, so a change made while
/// disconnected is not missed.
/// </para>
/// </summary>
public sealed class DevModuleReloader(
    ModuleManager modules,
    IHttpClientFactory httpClients,
    NavigationManager navigation,
    MudTailRuntime mudTail,
    Microsoft.Extensions.Localization.IStringLocalizer<App> texts)
{
    private const string EventsUrl = "api/client-modules/dev/events";
    private int _started;

    /// <summary>Starts listening in the background (once).</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var delay = TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                var client = httpClients.CreateClient(AuthServiceExtensions.WebApiPublicClient);
                using var request = new HttpRequestMessage(HttpMethod.Get, EventsUrl);
                request.SetBrowserResponseStreamingEnabled(true);
                request.Headers.Accept.ParseAdd("text/event-stream");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return; // no local client modules here
                response.EnsureSuccessStatusCode();

                modules.AutoReloadUpdates = true;
                delay = TimeSpan.FromSeconds(1);
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
                string? type = null;
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.StartsWith("event:", StringComparison.Ordinal)) type = line[6..].Trim();
                    else if (line.Length == 0 && type is not null)
                    {
                        await OnEventAsync(type);
                        type = null;
                    }
                }
                await Task.Delay(250); // the WebApi ends the stream every few minutes: open it again
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                Console.WriteLine($"Dev reload: {ex.Message}; reconnecting in {delay.TotalSeconds:0} s");
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 15));
                continue;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Dev reload stopped: {ex}");
                modules.AutoReloadUpdates = false;
                return;
            }
        }
    }

    /// <summary>"hello" (connected) or "changed": check the manifest, then reload what has a newer build.</summary>
    private async Task OnEventAsync(string type)
    {
        if (type is not ("hello" or "changed")) return;
        await modules.RefreshAsync(force: type == "changed");
        if (modules.UpdatesAvailable.Count == 0) return;

        var reloaded = await modules.ReloadWithDependentsAsync(modules.UpdatesAvailable.ToList());
        if (reloaded.Count == 0) return;

        // The Router builds the current page from the new assemblies: an in-app navigation to the same URL. (Not
        // NavigationManager.Refresh(): without enhanced navigation, as in a standalone WebAssembly app, it loads the page.)
        navigation.NavigateTo(navigation.Uri, forceLoad: false, replace: true);
        Console.WriteLine($"Dev reload: {string.Join(", ", reloaded)}");
        _ = mudTail.ToastAsync(texts["Reloaded {0}.", string.Join(", ", reloaded)], "success");
    }
}
