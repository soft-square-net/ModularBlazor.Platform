---
title: Blazor islands in Astro
description: Register Blazor components for JavaScript in a small islands host and mount them into Astro pages lazily, with a static fallback.
sidebar:
  order: 3
---

:::note[Proposed]
Nothing on this page exists in the repository yet. It is a recipe built on public Blazor and Astro APIs. Check the
boot options against the .NET version in `Directory.Build.props` (net10.0) when you implement it.
:::

## How it works

1. A small **islands host** (a Blazor WebAssembly project) registers the components that may appear on public
   pages with `RegisterForJavaScript`. It has no Router and no `#app` element.
2. It is published to `/islands/` on the same origin as the Astro site.
3. An Astro component, `<BlazorIsland>`, renders a custom element with a static fallback. When its `load`
   condition is met, it starts the Blazor runtime (once per page) and calls `Blazor.rootComponents.add`.
4. When the element leaves the page, the component is disposed.

## Why a separate host

The client's `Program.cs` (`ModularBlazor.BlzWebClient`) adds `App` to `#app`, fetches the client manifest and loads
the startup modules before the first render. On an Astro
page there is no `#app`, and there is no reason to download the layouts and the module catalog for a widget. A
separate host stays small and starts fast.

## 1. The islands host

```xml title="src/apps/blazor/ModularBlazor.Islands/ModularBlazor.Islands.csproj"
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <!-- Same rule as the client: islands may call any framework API. -->
    <PublishTrimmed>false</PublishTrimmed>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.Authentication" />
    <PackageReference Include="Microsoft.Extensions.Http" />
    <!-- A generated API client from a module repo, here the Content client (or its package). -->
    <ProjectReference Include="..\..\..\..\..\ModularBlazor.Module.Content\src\WebApi.Modules.Content.Client\WebApi.Modules.Content.Client.csproj" />
  </ItemGroup>
</Project>
```

```csharp title="src/apps/blazor/ModularBlazor.Islands/Program.cs"
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Each island gets a name that Astro pages use. Parameters are passed as JSON from the page.
builder.RootComponents.RegisterForJavaScript<LatestPosts>(identifier: "latest-posts");
builder.RootComponents.RegisterForJavaScript<LeagueTable>(identifier: "league-table");

// The same OIDC settings as the client ("Oidc" in wwwroot/appsettings.json), so the islands see the user the client
// signed in (same origin, same tab). Islands never start a sign-in themselves: they link to /app/login.
builder.Services.AddOidcAuthentication(o => builder.Configuration.Bind("Oidc", o.ProviderOptions));

// The gateway ("WebApi:BaseUrl"), like the client's AddWebApiClient: "WebApi" with the token, "WebApi.Public" without.
var gateway = new Uri(builder.Configuration["WebApi:BaseUrl"] ?? "http://localhost:5080/");
builder.Services.AddHttpClient("WebApi", c => c.BaseAddress = gateway)
    .AddHttpMessageHandler(sp => sp.GetRequiredService<AuthorizationMessageHandler>()
        .ConfigureHandler(authorizedUrls: [gateway.ToString()]));
builder.Services.AddHttpClient("WebApi.Public", c => c.BaseAddress = gateway);

await builder.Build().RunAsync();
```

Island components are ordinary Razor components. Keep them self-contained: take their input as parameters,
render links rather than navigating, and do not depend on a layout.

```razor title="LatestPosts.razor"
@inject IHttpClientFactory Http

<section class="mud-card">
  <h3 class="mud-card__title">@Title</h3>
  @if (_count is null) { <p>Loading…</p> }
  else { <p>@_count posts on your desk · <a href="/app/">Open the app</a></p> }
</section>

@code {
  [Parameter] public string Title { get; set; } = "My desk";
  private int? _count;

  protected override async Task OnInitializedAsync()
  {
    var content = new ContentClient(Http.CreateClient("WebApi")); // signed-in user's token
    try { _count = (await content.ListPostsAsync(PostView.Desk)).Count; }
    catch (AccessTokenNotAvailableException) { _count = 0; }   // not signed in: render the fallback text
  }
}
```

## 2. Start the runtime from an Astro page

The runtime is loaded on demand, with `autostart="false"`, and told where its files are, because the page URL is
not under `/islands/`.

```ts title="src/lib/blazor.ts"
type BlazorApi = {
  start(options?: unknown): Promise<void>;
  rootComponents: {
    add(el: Element, name: string, params: Record<string, unknown>): Promise<{
      setParameters(p: Record<string, unknown>): Promise<void>;
      dispose(): Promise<void>;
    }>;
  };
};

const BASE = '/islands/';
let starting: Promise<BlazorApi> | undefined;

/** Loads and starts the islands host once per page; every island awaits the same promise. */
export function startBlazor(): Promise<BlazorApi> {
  return (starting ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = `${BASE}_framework/blazor.webassembly.js`;
    script.setAttribute('autostart', 'false');
    script.onerror = reject;
    script.onload = () => {
      const Blazor = (window as unknown as { Blazor: BlazorApi }).Blazor;
      Blazor.start({
        // Boot files come from /islands/_framework/, appsettings.json from /islands/.
        loadBootResource: (type: string, name: string) =>
          type === 'configuration' ? `${BASE}${name}` : `${BASE}_framework/${name}`,
      }).then(() => resolve(Blazor), reject);
    };
    document.head.append(script);
  }));
}
```

## 3. The `<BlazorIsland>` component

```astro title="src/components/BlazorIsland.astro"
---
interface Props {
  /** Identifier given to RegisterForJavaScript. */
  component: string;
  /** Component parameters (JSON-serialisable). */
  params?: Record<string, unknown>;
  /** When to start: like client:load / client:idle / client:visible / client:media. */
  load?: 'eager' | 'idle' | 'visible' | 'media';
  media?: string;
}
const { component, params = {}, load = 'visible', media } = Astro.props;
---
<blazor-island data-component={component} data-params={JSON.stringify(params)} data-load={load} data-media={media}>
  <slot />
</blazor-island>

<script>
  import { startBlazor } from '../lib/blazor';

  class BlazorIsland extends HTMLElement {
    #instance?: { dispose(): Promise<void> };
    #cancel = () => {};
    #disconnected = false;

    connectedCallback() {
      const mount = () => this.#mount();
      switch (this.dataset.load) {
        case 'eager':
          mount();
          break;
        case 'idle': {
          const id = requestIdleCallback(mount);
          this.#cancel = () => cancelIdleCallback(id);
          break;
        }
        case 'media': {
          const mq = matchMedia(this.dataset.media ?? 'all');
          if (mq.matches) { mount(); break; }
          const onChange = () => { if (mq.matches) { mq.removeEventListener('change', onChange); mount(); } };
          mq.addEventListener('change', onChange);
          this.#cancel = () => mq.removeEventListener('change', onChange);
          break;
        }
        default: {
          const io = new IntersectionObserver(([entry]) => {
            if (entry.isIntersecting) { io.disconnect(); mount(); }
          }, { rootMargin: '200px' });
          io.observe(this);
          this.#cancel = () => io.disconnect();
        }
      }
    }

    async #mount() {
      const Blazor = await startBlazor();
      if (this.#disconnected) return;
      this.replaceChildren(); // drop the static fallback; Blazor renders into the element
      this.#instance = await Blazor.rootComponents.add(
        this, this.dataset.component!, JSON.parse(this.dataset.params ?? '{}'));
    }

    disconnectedCallback() {
      this.#disconnected = true;
      this.#cancel();
      this.#instance?.dispose();
    }
  }

  customElements.define('blazor-island', BlazorIsland);
</script>
```

## 4. Use it

```astro title="src/pages/sport/league.astro"
---
import Site from '../../layouts/Site.astro';
import BlazorIsland from '../../components/BlazorIsland.astro';
---
<Site title="League">
  <article>
    <h1>Premier division</h1>
    <p>Static article text, rendered at build time…</p>

    <BlazorIsland component="league-table" params={{ season: 2026 }} load="visible">
      <!-- Fallback: shown until the island starts, and to crawlers. -->
      <a href="/app/sport/league">See the full table</a>
    </BlazorIsland>
  </article>
</Site>
```

## Runtime modules

The islands host only knows components it was compiled with. Components that live in a **runtime module**
(the Dashboard and Reports samples, or any module installed by an admin) cannot be registered directly, because
`RegisterForJavaScript` needs the type before the host is built. Two ways forward:

- **Reference the module project** from the islands host. Simple, but the island ships with the host and no
  longer follows the module's own release.
- **A generic `ModuleIsland` component.** Register one component (`"module-island"`) that takes a module id and
  a component type name, downloads the module and renders the type with `DynamicComponent`. That needs the module
  loader, which is in `ModularBlazor.BlzWebClient/Services` today (`ModuleManager`, the package readers, the asset
  store); it would first have to move into a shared library.

Start with the first option and move to the second only when several modules need islands.

## Checklist

- [ ] The islands host is published to `/islands/` on the **same origin** as the Astro site.
- [ ] Its `appsettings.json` has the same `Oidc` and `WebApi:BaseUrl` as the client, and `Cors:AllowedOrigins` of the
      pools lists the site origin.
- [ ] Every island renders a useful fallback in its slot.
- [ ] Islands render links instead of navigating, and pass data as parameters.
- [ ] MudTail styles the island: it uses the same CSS classes as the rest of the page.
