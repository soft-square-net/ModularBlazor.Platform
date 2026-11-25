using soft.square.net.ModularBlazor.Abstractions.Shell;
using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// The dock panels of the catalog's modules (IStudioPanels). Like <see cref="ShellContributionService"/>, the catalog is
/// the user's manifest, already filtered by the server, so this only picks a studio's panels and orders them. It keeps
/// no module types: a panel's component is resolved when it is mounted (Components/ModulePanelHost).
/// </summary>
public sealed class StudioPanelService : IStudioPanels, IDisposable
{
  private readonly ModuleManager _modules;

  public StudioPanelService(ModuleManager modules)
  {
    _modules = modules;
    _modules.Changed += OnChanged;
  }

  public event Action? Changed;

  public IReadOnlyList<ShellContribution> For(string studio, IEnumerable<string>? contextKeys = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(studio);
    var keys = contextKeys?.ToList();
    return Panels()
      .Where(c => c.IsForStudio(studio, keys))
      .OrderBy(c => c.Order)
      .ThenBy(c => c.ModuleId, StringComparer.OrdinalIgnoreCase)
      .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  public ShellContribution? Find(string id)
    => string.IsNullOrWhiteSpace(id) ? null : Panels().FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

  private IEnumerable<ShellContribution> Panels()
    => _modules.Catalog.SelectMany(m => m.Contributions).Where(c => c.Kind == ShellContributionKinds.DockPanel);

  private void OnChanged() => Changed?.Invoke();

  public void Dispose() => _modules.Changed -= OnChanged;
}
