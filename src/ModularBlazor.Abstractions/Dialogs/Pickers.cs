namespace soft.square.net.ModularBlazor.Abstractions;

/// <summary>
/// The standard picker capabilities (<see cref="ClientDialogAttribute.Provides"/>). Every picker dialog takes
/// <see cref="PickerParameters"/> and closes with a <see cref="PickerResult"/>, so a caller can use whichever module
/// provides one. Other capabilities follow the same "{kind}:{name}" form; the module that defines one documents its shapes.
/// </summary>
public static class PickerCapabilities
{
    /// <summary>Files and images (the Storage module). <see cref="PickerParameters.Accept"/> and <see cref="PickerParameters.PublicOnly"/> apply.</summary>
    public const string Asset = "picker:asset";

    /// <summary>Keywords (tags).</summary>
    public const string Keyword = "picker:keyword";

    /// <summary>Terms of a taxonomy (sections, categories).</summary>
    public const string Term = "picker:term";

    /// <summary>Concepts (people, places, organisations) of a knowledge graph.</summary>
    public const string Concept = "picker:concept";
}

/// <summary>
/// What a picker dialog is opened with (web JSON: <c>{ multiple, selected, accept?, publicOnly? }</c>).
/// A picker ignores what does not apply to it.
/// </summary>
/// <param name="Multiple">True: the user may pick several items.</param>
/// <param name="Selected">The ids picked already, shown as selected.</param>
/// <param name="Accept">Assets: what may be picked, like an HTML accept attribute ("image/*", ".pdf,.docx").</param>
/// <param name="PublicOnly">Assets: only files with a public URL (for content that is published).</param>
public sealed record PickerParameters(
    bool Multiple = false,
    IReadOnlyList<string>? Selected = null,
    string? Accept = null,
    bool? PublicOnly = null);

/// <summary>
/// What a picker dialog closes with (web JSON: <c>{ ids, items: [{ id, text, thumbnailUrl?, url? }] }</c>): the ids in
/// the order picked, and enough about each item to show it without asking its module.
/// </summary>
public sealed record PickerResult(IReadOnlyList<string> Ids, IReadOnlyList<PickedItem> Items);

/// <summary>One picked item.</summary>
/// <param name="Id">Its id in the providing module, as text.</param>
/// <param name="Text">What to show for it (a file name, a term's name).</param>
/// <param name="ThumbnailUrl">A small image, when it has one.</param>
/// <param name="Url">Where it is served (an asset's public URL).</param>
public sealed record PickedItem(string Id, string Text, string? ThumbnailUrl = null, string? Url = null);

public static class PickerDialogExtensions
{
    /// <summary>
    /// Opens the first dialog that provides <paramref name="capability"/> and returns what was picked, or null when the
    /// user canceled.
    /// </summary>
    /// <exception cref="ModuleDialogException">No module in the user's catalog provides the capability.</exception>
    public static async Task<PickerResult?> PickAsync(this IModuleDialogs dialogs, string capability, PickerParameters? parameters = null,
        DialogOptions? options = null, CancellationToken cancellationToken = default)
    {
        var id = dialogs.FindProvider(capability)
            ?? throw new ModuleDialogException($"No module you can use provides '{capability}'.");
        var result = await dialogs.ShowAsync(id, parameters ?? new PickerParameters(), options, cancellationToken);
        return result.Canceled ? null : result.As<PickerResult>() ?? new PickerResult([], []);
    }
}
