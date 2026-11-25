---
title: Live updates
description: The host's SignalR hub /hubs/live, topics authorized from manifest data and server-side scopes, ILiveNotifier for modules and ILiveChannel for the Blazor client.
sidebar:
  order: 15
---

One SignalR hub in the host, `/hubs/live`, pushes small "something changed" messages to the Blazor client. The
client then reads the data again through the module's own endpoints. A module never adds hub code: it publishes
through `ILiveNotifier`, and its manifest says which topics it offers and who may receive them.

```
module (any pool) ─ILiveNotifier─► connections on this replica ─┐
                        └─► RabbitMQ fanout "live" ─► every other replica ─► its connections
browser ─WebSocket /hubs/live?access_token=…─► Subscribe(topic)
```

## Topics

A topic never names the tenant: the hub always uses the connection's tenant.

| Topic | Built with | Who may subscribe |
| --- | --- | --- |
| `user:{id}` | `LiveTopics.User(id)` | that user only |
| `{moduleId}:{kind}:{key}` | `LiveTopics.Of("content", "post", "42")` | holders of the permission the module declares for the kind |
| `unit:{id}` | `LiveTopics.Unit(id)` | holders of the permission of the `unit` kind, declared by the one module that provides units (Organization) |

## Authorization is data, and only server-side

The client sends **only the topic name**. Scopes supplied by a client never count.

1. The module declares its topic kinds in the manifest. The host reads them at install:

   ```csharp
   public ModuleManifest Manifest { get; } = new("content", "Content", "1.0.0", "content")
   {
       LiveTopics = [new LiveTopic("post", "content.view")]
   };
   ```

2. For `content:post:42`, the host checks `content.view` on the scope `post:42`.
3. Optionally, the module adds the resource's other scopes (its press, its category, its parent units) with an
   `ILiveScopeResolver` registered in **its own services**:

   ```csharp
   public sealed class PostScopes(ContentDbContext db) : ILiveScopeResolver
   {
       public async Task<IReadOnlyList<string>> ScopesAsync(string kind, string key, CancellationToken ct) =>
           kind == "post" && Guid.TryParse(key, out var id)
               ? await db.Posts.Where(p => p.Id == id).Select(p => new[] { $"press:{p.PressId}", $"unit:{p.UnitId}" }).FirstOrDefaultAsync(ct) ?? []
               : [];
   }

   services.AddScoped<ILiveScopeResolver, PostScopes>();
   ```

   The host resolves it per subscription from the module's container, in the subscriber's tenant, and keeps only the
   strings it returns.

**It fails closed.** With no resolver, when the module is not loaded on the hub's replica (the core pool), or when the
resolver throws, only the exact scope counts. With no unit provider, or with two, `unit:` topics are refused.

When a user's grants change (for example at shift check-out), every replica checks that user's subscriptions again,
drops the ones no longer allowed and sends `Revoked(topic)` to the client.

## Publishing from a module

```csharp
public sealed class PostPublished(ILiveNotifier live)
{
    public Task NotifyAsync(Post post, CancellationToken ct) =>
        live.PublishAsync(LiveTopics.Of("content", "post", post.Id.ToString()), "post-updated",
            JsonSerializer.SerializeToElement(new { post.Id, post.State }), sequence: post.Version, ct);
}
```

- `ILiveNotifier` is scoped and uses the current tenant.
- The payload is a `JsonElement`, so the host never sees a module type. Keep it small; the client re-reads the data.
- `PublishToUnitAsync("root/region/desk", ...)` sends to the unit and to each of its ancestors.
- **Publish after the change is committed.** Messages are best effort. A client that missed one catches up from the
  module's own feed (`?after={sequence}`).

## The Blazor client: `ILiveChannel`

```csharp
@inject ILiveChannel Live

await using var subscription = await Live.SubscribeAsync(
    $"content:post:{PostId}", OnMessageAsync, fromSequence: lastSeen,
    new LiveSubscribeOptions { OnResync = r => ReloadAfterAsync(r.LastSequence), OnRevoked = _ => CloseAsync() });
```

- `ILiveChannel` lives in `ModularBlazor.Abstractions`; the topic is the same text the module publishes to.
- The channel connects while the user is signed in, reconnects with a backoff and subscribes again.
- It drops duplicate sequences, and raises a resync after a reconnect or on a gap.
- `State` is `Connected`, `Reconnecting` or `Offline`. Layouts.Default shows it in the toolbar.

## Deployment

- The gateway routes `/hubs/live` with WebSockets to the core pool. The client skips negotiation, so no sticky
  sessions are needed.
- Without a broker, messages reach the connections of the publishing replica only.
