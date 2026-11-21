using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.Abstractions.Menus;

/// <summary>An entry of a built menu with what is in it (a group's links and subgroups, in order).</summary>
public sealed record MenuNode(MenuItem Item, IReadOnlyList<MenuNode> Children)
{
    public bool HasChildren => Children.Count > 0;
}

/// <summary>
/// The menus of the modules the user may see, built by the host from their metadata (MenuLink and MenuGroup attributes),
/// so a module's links come before its code is downloaded and go when it is unloaded or deactivated. Layouts show a
/// zone with <see cref="MenuZone"/> or read <see cref="Zone"/> themselves.
/// </summary>
public interface IMenuService
{
    /// <summary>The top level of a zone (<see cref="MenuZones"/>), in order. Unknown zone: empty.</summary>
    IReadOnlyList<MenuNode> Zone(string zone);

    /// <summary>Raised when the menus change: sign-in, sign-out, a module installed, activated, deactivated or unloaded.</summary>
    event Action? Changed;
}
