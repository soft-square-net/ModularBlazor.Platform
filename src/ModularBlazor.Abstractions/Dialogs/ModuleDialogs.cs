using System.Text.Json;
using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.Abstractions;

/// <summary>
/// Opens a dialog that a client module provides, from any module, without a reference to it, and waits for its result.
/// <para>
/// A dialog is addressed by its id, <c>"{module id}/{name}"</c> (e.g. <c>"Module.Shifts/pick-shift"</c>): the host
/// loads the owning module on first use (as it does for a page of it), shows the dialog in MudTail's dialog shell and
/// completes the task when the dialog closes. Parameters and the result travel as JSON, so the caller and the dialog
/// share no types: the id and the two JSON shapes are the dialog's contract, versioned like an HTTP route.
/// </para>
/// <code>
/// var result = await Dialogs.ShowAsync("Module.Shifts/pick-shift", new { sectionId = 3 });
/// if (!result.Canceled) { var picked = result.As&lt;PickedShift&gt;(); ... }
/// </code>
/// </summary>
public interface IModuleDialogs
{
    /// <summary>
    /// Shows the dialog and completes when it closes: with its data when it called <see cref="IDialogReference.Close"/>,
    /// canceled when it was dismissed (its ✕, Escape, the overlay, <see cref="IDialogReference.Cancel"/>, navigation away
    /// from the page, or <paramref name="cancellationToken"/>).
    /// </summary>
    /// <exception cref="ModuleDialogException">
    /// No module in the user's catalog provides <paramref name="dialogId"/>, or its module could not be loaded.
    /// </exception>
    Task<DialogResult> ShowAsync(string dialogId, object? parameters = null, DialogOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Whether a module in the signed-in user's catalog may provide this dialog (its module is not loaded for this).</summary>
    bool IsAvailable(string dialogId);

    /// <summary>
    /// The dialogs in the signed-in user's catalog that provide a capability (<see cref="ClientDialogAttribute.Provides"/>,
    /// e.g. <see cref="PickerCapabilities.Asset"/>), in module id order. Read from the catalog: no module is loaded.
    /// </summary>
    IReadOnlyList<ModuleDialogInfo> FindProviders(string capability) => [];

    /// <summary>
    /// The id of a dialog that provides <paramref name="capability"/> (the first of <see cref="FindProviders"/>), or null
    /// when no module in the user's catalog provides it. Pass the id to <see cref="ShowAsync"/>:
    /// <c>if (Dialogs.FindProvider(PickerCapabilities.Asset) is { } id) await Dialogs.ShowAsync(id, ...)</c>.
    /// </summary>
    string? FindProvider(string capability) => FindProviders(capability).FirstOrDefault()?.Id;
}

/// <summary>How the host shows a dialog. Unset values come from the dialog's <see cref="ClientDialogAttribute"/>.</summary>
public sealed record DialogOptions
{
    /// <summary>The title bar's text.</summary>
    public string? Title { get; init; }

    /// <summary>A CSS length, e.g. "520px" (the default) or "90vw".</summary>
    public string? MaxWidth { get; init; }

    /// <summary>Whether clicking outside the dialog cancels it. Default true.</summary>
    public bool? CloseOnOverlayClick { get; init; }

    /// <summary>Whether Escape cancels it. Default true.</summary>
    public bool? CloseOnEscape { get; init; }
}

/// <summary>How a dialog ended, and the data it returned.</summary>
public sealed class DialogResult
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DialogResult(bool canceled, JsonElement? data)
    {
        Canceled = canceled;
        Data = data;
    }

    /// <summary>True when the dialog was dismissed rather than closed with a result.</summary>
    public bool Canceled { get; }

    /// <summary>What the dialog passed to <see cref="IDialogReference.Close"/>, as JSON (null when it passed nothing).</summary>
    public JsonElement? Data { get; }

    /// <summary>The data in the caller's own type (web JSON naming: camelCase, case-insensitive).</summary>
    public T? As<T>() => Data is { } data ? data.Deserialize<T>(Json) : default;

    public static DialogResult Ok(object? data = null) => new(false, ToJson(data));

    public static DialogResult Cancel() => new(true, null);

    /// <summary>A value as web JSON (camelCase), the way dialog parameters and results travel.</summary>
    public static JsonElement? ToJson(object? value) => value switch
    {
        null => null,
        JsonElement element => element.Clone(),
        _ => JsonSerializer.SerializeToElement(value, value.GetType(), Json),
    };
}

/// <summary>
/// Marks a component as a dialog other modules can open by id. The id must start with the module's own id:
/// <c>@attribute [ClientDialog("Module.Shifts/pick-shift", Title = "Pick a shift")]</c>.
/// <para>
/// The host renders the dialog's frame (overlay, title bar with a close button); the component renders its body and
/// its buttons, usually a <c>div.mud-dialog-content</c> and a <c>div.mud-dialog-actions</c>, and closes itself through
/// the cascading <see cref="IDialogReference"/>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ClientDialogAttribute(string id) : Attribute
{
    public string Id { get; } = id;

    /// <summary>Default title bar text (the caller's <see cref="DialogOptions.Title"/> wins).</summary>
    public string? Title { get; set; }

    /// <summary>Default maximum width, a CSS length.</summary>
    public string? MaxWidth { get; set; }

    /// <summary>
    /// What the dialog provides, so callers find it by capability rather than by id
    /// (<see cref="IModuleDialogs.FindProvider"/>): "{kind}:{name}" in lower case, e.g. <c>Provides = new[] { "picker:asset" }</c>.
    /// A dialog that provides a <see cref="PickerCapabilities">picker</see> takes <see cref="PickerParameters"/> and
    /// closes with a <see cref="PickerResult"/>. Read from the module's metadata when it is uploaded.
    /// </summary>
    public string[] Provides { get; set; } = [];
}

/// <summary>
/// Given to an open dialog component as a cascading parameter:
/// <c>[CascadingParameter] public IDialogReference Dialog { get; set; } = default!;</c>
/// </summary>
public interface IDialogReference
{
    /// <summary>The dialog's id.</summary>
    string Id { get; }

    /// <summary>What the caller passed as parameters, as JSON (null when it passed none).</summary>
    JsonElement? Parameters { get; }

    /// <summary>The parameters in the dialog's own type (web JSON naming), or default when there are none.</summary>
    T? GetParameters<T>();

    /// <summary>Closes the dialog with a result; the caller's <see cref="IModuleDialogs.ShowAsync"/> gets it as JSON.</summary>
    void Close(object? data = null);

    /// <summary>Closes the dialog without a result.</summary>
    void Cancel();
}

/// <summary>A dialog could not be shown: unknown id, a module the user may not use, or a module that failed to load.</summary>
public sealed class ModuleDialogException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Dialog ids: <c>"{module id}/{name}"</c>.</summary>
public static class DialogIds
{
    /// <summary>The module id part of a dialog id, or null when the id has no "/".</summary>
    public static string? ModuleOf(string dialogId)
    {
        var slash = dialogId.IndexOf('/');
        return slash > 0 && slash < dialogId.Length - 1 ? dialogId[..slash] : null;
    }
}
