using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Tests.Modularity;

/// <summary>Which modules a pool activates: Modules:Enabled (empty: every module) minus Modules:Excluded.</summary>
public sealed class PoolModuleSelectionTests
{
    [Fact]
    public void Empty_lists_enable_every_module()
    {
        var options = new ModuleRuntimeOptions();
        options.IsEnabled("dynamicfields").ShouldBeTrue();
    }

    [Fact]
    public void Enabled_list_limits_the_pool_to_its_modules()
    {
        var options = new ModuleRuntimeOptions { Enabled = ["sales", "greeting"] };
        options.IsEnabled("Sales").ShouldBeTrue();
        options.IsEnabled("dynamicfields").ShouldBeFalse();
    }

    [Fact]
    public void Excluded_modules_are_skipped_when_every_module_is_enabled()
    {
        // The lab's core pool: every module except the ones extracted to other pools.
        var options = new ModuleRuntimeOptions { Excluded = ["sales", "elsa"] };
        options.IsEnabled("dynamicfields").ShouldBeTrue();
        options.IsEnabled("identity").ShouldBeTrue();
        options.IsEnabled("ELSA").ShouldBeFalse();
    }

    [Fact]
    public void Excluded_wins_over_enabled()
    {
        var options = new ModuleRuntimeOptions { Enabled = ["sales"], Excluded = ["sales"] };
        options.IsEnabled("sales").ShouldBeFalse();
    }
}
