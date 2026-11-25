using soft.square.net.ModularBlazor.Abstractions;
using soft.square.net.Share.All;

namespace soft.square.net.ClientModules.Tests;

/// <summary>Pickers found by capability (IModuleDialogs.FindProvider, PickAsync) and the standard picker JSON.</summary>
public sealed class PickerTests
{
    /// <summary>Implements only what IModuleDialogs had before capabilities: the new members must have defaults.</summary>
    private class OldDialogs : IModuleDialogs
    {
        public List<(string Id, object? Parameters)> Shown { get; } = [];
        public DialogResult Next { get; set; } = DialogResult.Cancel();

        public Task<DialogResult> ShowAsync(string dialogId, object? parameters = null, DialogOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Shown.Add((dialogId, parameters));
            return Task.FromResult(Next);
        }

        public bool IsAvailable(string dialogId) => true;
    }

    private sealed class CatalogDialogs(params ModuleDialogInfo[] dialogs) : OldDialogs, IModuleDialogs
    {
        public IReadOnlyList<ModuleDialogInfo> FindProviders(string capability) => dialogs.Where(d => d.HasCapability(capability)).ToList();
    }

    [Fact]
    public void An_implementation_without_capabilities_finds_none()
    {
        IModuleDialogs dialogs = new OldDialogs();
        dialogs.FindProviders(PickerCapabilities.Asset).ShouldBeEmpty();
        dialogs.FindProvider(PickerCapabilities.Asset).ShouldBeNull();
    }

    [Fact]
    public void FindProvider_is_the_first_provider()
    {
        IModuleDialogs dialogs = new CatalogDialogs(
            new ModuleDialogInfo("Module.Storage/pick-asset", null, [PickerCapabilities.Asset]),
            new ModuleDialogInfo("Module.Taxonomy/pick-term", null, [PickerCapabilities.Term]));

        dialogs.FindProvider(PickerCapabilities.Term).ShouldBe("Module.Taxonomy/pick-term");
        dialogs.FindProvider(PickerCapabilities.Keyword).ShouldBeNull();
    }

    [Fact]
    public async Task PickAsync_sends_the_standard_parameters_and_reads_the_standard_result()
    {
        var dialogs = new CatalogDialogs(new ModuleDialogInfo("Module.Storage/pick-asset", null, [PickerCapabilities.Asset]))
        {
            Next = DialogResult.Ok(new { ids = new[] { "7" }, items = new[] { new { id = "7", text = "logo.png", thumbnailUrl = "t/7.png" } } })
        };

        var picked = await ((IModuleDialogs)dialogs).PickAsync(PickerCapabilities.Asset,
            new PickerParameters(Multiple: true, Selected: ["3"], Accept: "image/*", PublicOnly: true));

        picked.ShouldNotBeNull();
        picked.Ids.ShouldBe(["7"]);
        picked.Items.ShouldHaveSingleItem().ShouldBe(new PickedItem("7", "logo.png", "t/7.png"));

        var (id, parameters) = dialogs.Shown.ShouldHaveSingleItem();
        id.ShouldBe("Module.Storage/pick-asset");
        DialogResult.ToJson(parameters)!.Value.GetRawText()
            .ShouldBe("""{"multiple":true,"selected":["3"],"accept":"image/*","publicOnly":true}""");
    }

    [Fact]
    public async Task PickAsync_returns_null_when_canceled_and_throws_without_a_provider()
    {
        IModuleDialogs dialogs = new CatalogDialogs(new ModuleDialogInfo("Module.Storage/pick-asset", null, [PickerCapabilities.Asset]));
        (await dialogs.PickAsync(PickerCapabilities.Asset)).ShouldBeNull();
        await Should.ThrowAsync<ModuleDialogException>(() => dialogs.PickAsync(PickerCapabilities.Concept));
    }
}
