using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using soft.square.net.ModularBlazor.Abstractions;
using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// The host's <see cref="IModuleDialogs"/>: finds the module that owns a dialog id in the user's catalog, loads it like
/// a page would, keeps its styles and scripts on the page while the dialog is open, and hands the open dialogs to
/// <c>ModuleDialogHost</c> (rendered once, in App.razor) to show. Dialogs stack: one may open another.
/// </summary>
public sealed class ModuleDialogService : IModuleDialogs, IDisposable
{
    private readonly ModuleManager _modules;
    private readonly ActiveModuleService _active;
    private readonly NavigationManager _navigation;
    private readonly List<OpenDialog> _open = [];

    public ModuleDialogService(ModuleManager modules, ActiveModuleService active, NavigationManager navigation)
    {
        _modules = modules;
        _active = active;
        _navigation = navigation;
        // The page that opened a dialog is gone: its caller gets "canceled".
        _navigation.LocationChanged += CancelAllOnNavigation;
    }

    /// <summary>The open dialogs, the topmost last.</summary>
    public IReadOnlyList<OpenDialog> Open => _open;

    /// <summary>Raised when a dialog opens or closes.</summary>
    public event Action? Changed;

    public bool IsAvailable(string dialogId)
        => DialogIds.ModuleOf(dialogId) is { } moduleId && _modules.GetDescriptor(moduleId) is { } module
           // A module listed by an older server has no dialog list: any id under it may exist.
           && (module.Dialogs.Length == 0 || module.Dialogs.Any(d => string.Equals(d.Id, dialogId, StringComparison.OrdinalIgnoreCase)));

    public IReadOnlyList<ModuleDialogInfo> FindProviders(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability)) return [];
        return _modules.Catalog
            .SelectMany(m => m.Dialogs)
            .Where(d => d.HasCapability(capability))
            .OrderBy(d => d.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? FindProvider(string capability) => FindProviders(capability).FirstOrDefault()?.Id;

    public async Task<DialogResult> ShowAsync(string dialogId, object? parameters = null, DialogOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogId);
        var moduleId = DialogIds.ModuleOf(dialogId)
            ?? throw new ModuleDialogException($"'{dialogId}' is not a dialog id: use \"{{module id}}/{{name}}\".");

        // The catalog is the user's own (roles and tenant applied by the server): a module that is not in it is not
        // available to this user, so neither are its dialogs.
        if (_modules.GetDescriptor(moduleId) is null)
            throw new ModuleDialogException($"Dialog '{dialogId}' is not available: module '{moduleId}' is not installed or not open to you.");

        try
        {
            await _modules.EnsureLoadedAsync(moduleId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return DialogResult.Cancel();
        }
        catch (Exception ex)
        {
            throw new ModuleDialogException($"Dialog '{dialogId}': module '{moduleId}' could not be loaded ({ex.Message}).", ex);
        }

        var (type, info) = _modules.ResolveDialog(dialogId)
            ?? throw new ModuleDialogException($"Module '{moduleId}' has no dialog '{dialogId}'.");

        var dialog = new OpenDialog(dialogId, moduleId, type, DialogResult.ToJson(parameters), new DialogOptions
        {
            Title = options?.Title ?? info.Title,
            MaxWidth = options?.MaxWidth ?? info.MaxWidth ?? "520px",
            CloseOnOverlayClick = options?.CloseOnOverlayClick ?? true,
            CloseOnEscape = options?.CloseOnEscape ?? true,
        });

        await _active.HoldAsync(moduleId); // the dialog's module styles, even on another module's page
        _open.Add(dialog);
        Changed?.Invoke();

        await using var registration = cancellationToken.Register(dialog.Cancel);
        try
        {
            return await dialog.Result;
        }
        finally
        {
            _open.Remove(dialog);
            Changed?.Invoke();
            await _active.ReleaseAsync(moduleId);
        }
    }

    private void CancelAllOnNavigation(object? sender, LocationChangedEventArgs e)
    {
        foreach (var dialog in _open.ToList()) dialog.Cancel();
    }

    public void Dispose()
    {
        _navigation.LocationChanged -= CancelAllOnNavigation;
        foreach (var dialog in _open.ToList()) dialog.Cancel();
    }

    /// <summary>One open dialog: what the host renders, and the reference the dialog component closes itself with.</summary>
    public sealed class OpenDialog(string id, string moduleId, Type component, JsonElement? parameters, DialogOptions options)
        : IDialogReference
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly TaskCompletionSource<DialogResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id { get; } = id;
        public string ModuleId { get; } = moduleId;
        public Type Component { get; } = component;
        public DialogOptions Options { get; } = options;
        public JsonElement? Parameters { get; } = parameters;

        /// <summary>A unique key for rendering (the same dialog id may be open twice).</summary>
        public Guid Key { get; } = Guid.NewGuid();

        internal Task<DialogResult> Result => _result.Task;

        public T? GetParameters<T>() => Parameters is { } json ? json.Deserialize<T>(Json) : default;

        public void Close(object? data = null) => _result.TrySetResult(DialogResult.Ok(data));

        public void Cancel() => _result.TrySetResult(DialogResult.Cancel());
    }
}
