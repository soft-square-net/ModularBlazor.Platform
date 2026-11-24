using System.Text.Json;
using Microsoft.JSInterop;

namespace soft.square.net.Layouts.Default.Shell;

/// <summary>
/// What the user set in the shell, kept in localStorage ("mb.shell") so it survives a reload: the nav drawer's size,
/// the open menu groups, the context panel and the density. Colours and the light/dark mode are MudTail's theme.
/// </summary>
public sealed class ShellState
{
  public const string StorageKey = "mb.shell";

  /// <summary>"auto" (mini from 1024 to 1279px, expanded from 1280px), "mini" or "full".</summary>
  public string Nav { get; set; } = "auto";

  /// <summary>The ids of the menu groups the user opened.</summary>
  public HashSet<string> OpenGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>The context panel from 1280px (below that it is a sheet the user opens).</summary>
  public bool ContextOpen { get; set; } = true;

  /// <summary>"comfortable" or "compact".</summary>
  public string Density { get; set; } = "comfortable";

  /// <summary>The dock pinned open (it stays open on a click elsewhere), or null.</summary>
  public string? PinnedDock { get; set; }

  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static async Task<ShellState> LoadAsync(IJSRuntime js)
  {
    try
    {
      var text = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
      var state = string.IsNullOrEmpty(text) ? null : JsonSerializer.Deserialize<ShellState>(text, Json);
      if (state is null) return new ShellState();
      state.OpenGroups = new HashSet<string>(state.OpenGroups ?? [], StringComparer.OrdinalIgnoreCase);
      if (state.Nav is not ("auto" or "mini" or "full")) state.Nav = "auto";
      if (state.Density is not ("comfortable" or "compact")) state.Density = "comfortable";
      return state;
    }
    catch (Exception ex) when (ex is JSException or JsonException)
    {
      return new ShellState(); // storage blocked or an old shape
    }
  }

  public async Task SaveAsync(IJSRuntime js)
  {
    try
    {
      await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(this, Json));
    }
    catch (JSException)
    {
      // storage blocked: the settings last until the page is reloaded
    }
  }
}
