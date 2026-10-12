using Mk.Contracts;
using Mk.Kernel;

namespace Mk.Tests;

public class SettableClock(DateTime localTime) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public override DateTimeOffset GetUtcNow() => new(localTime, TimeSpan.Zero);
}

/// <summary>The kernel is tested alone, with "in-memory" plugins written in the test: no DLL needed.</summary>
public class KernelTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid();
    private readonly PluginRegistry _registry = new();
    private readonly BankingKernel _kernel;

    public KernelTests()
    {
        _kernel = new BankingKernel(_registry, new SettableClock(new DateTime(2026, 10, 12, 14, 0, 0)), _ => { });
        _kernel.Open(Alice, "Alice", 100m);
        _kernel.Open(Bob, "Bob", 0m);
    }

    [Fact]
    public void Without_any_plugin_the_kernel_already_knows_how_to_transfer()
    {
        var result = _kernel.Transfer(Alice, Bob, 30m);

        Assert.True(result.Success);
        Assert.Equal((0m, 70m), (result.Fee, result.Balance));
    }

    [Fact]
    public void A_fee_rule_is_added_to_the_amount_and_appears_on_the_statement()
    {
        _registry.Register(new FixedFees(1m));

        var result = _kernel.Transfer(Alice, Bob, 30m);

        Assert.Equal((1m, 69m), (result.Fee, result.Balance));
        Assert.Contains(_kernel.Get(Alice)!.Lines, l => l.Label == "Fixed fees" && l.Amount == -1m);
    }

    [Fact]
    public void A_crashing_plugin_is_disabled_and_does_not_take_the_kernel_down()
    {
        var faulty = _registry.Register(new FaultyCheck());

        var first = _kernel.Withdraw(Alice, 10m);
        var second = _kernel.Withdraw(Alice, 10m);

        Assert.False(first.Success);  // refused for safety
        Assert.Contains("faulted", first.Error);
        Assert.Equal(PluginState.Faulted, faulty.State);
        Assert.True(second.Success);  // the plugin is set aside, the kernel carries on
    }

    [Fact]
    public void A_plugin_that_requires_a_more_recent_kernel_is_refused()
    {
        var future = _registry.Register(new FuturePlugin());

        Assert.Equal(PluginState.Incompatible, future.State);
        Assert.True(_kernel.Withdraw(Alice, 10m).Success);  // it is never called
    }

    [Fact]
    public void Without_a_format_plugin_the_kernel_cannot_export()
    {
        Assert.Null(_kernel.Export(Alice, "csv", out var formats));
        Assert.Empty(formats);
    }

    private sealed class FixedFees(decimal amount) : IFeeRule
    {
        public string Name => "FixedFees";
        public string Description => "test";
        public string Label => "Fixed fees";
        public decimal ComputeFee(ProposedOperation operation) => amount;
    }

    private sealed class FaultyCheck : IOperationCheck
    {
        public string Name => "Faulty";
        public string Description => "test";
        public string? Check(ProposedOperation operation) => throw new InvalidOperationException("Bug in the plugin");
    }

    private sealed class FuturePlugin : IOperationCheck
    {
        public string Name => "Future";
        public string Description => "test";
        public Version MinimumKernelVersion => new(2, 0);
        public string? Check(ProposedOperation operation) => "must never be called";
    }
}
