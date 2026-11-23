using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.Abstractions.Menus;

/// <summary>
/// The badges next to menu entries, for a module's code once it is loaded. Entries with a Badge url get theirs from the
/// host, which asks the url now and then; a module tells it when it knows the count changed (it just approved a post),
/// or sets a badge itself for an entry without a url.
/// </summary>
public interface IMenuBadges
{
    /// <summary>The entry's badge now (null: none).</summary>
    MenuBadge? Of(string entryId);

    /// <summary>
    /// Sets an entry's badge (null clears it). An entry with a Badge url keeps it until the next answer of its url; use
    /// <see cref="RefreshAsync"/> there instead.
    /// </summary>
    void Set(string entryId, MenuBadge? badge);

    /// <summary>Asks the entry's Badge url again now (nothing when it has none).</summary>
    Task RefreshAsync(string entryId);

    /// <summary>Raised when a badge changes.</summary>
    event Action? Changed;
}
