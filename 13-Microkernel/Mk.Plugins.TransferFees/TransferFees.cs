using System.Globalization;
using Mk.Contracts;

namespace Mk.Plugins.TransferFees;

/// <summary>"Fees" extension point: a percentage of the transfer, bounded by a minimum and a maximum.</summary>
public sealed class TransferFees : IFeeRule
{
    private decimal _rate = 0.001m, _minimum = 0.20m, _maximum = 5m;

    public string Name => "TransferFees";
    public string Description => "Proportional transfer fees, with a minimum and a maximum.";
    public string Label => "Transfer fees";

    public void Initialize(PluginContext context)
    {
        _rate = Read(context, "rate", _rate);
        _minimum = Read(context, "minimum", _minimum);
        _maximum = Read(context, "maximum", _maximum);
        context.Log($"rate {_rate:P2}, between {_minimum} € and {_maximum} €");
    }

    public decimal ComputeFee(ProposedOperation operation) =>
        operation.Type != OperationType.Transfer
            ? 0
            : Math.Clamp(Math.Round(operation.Amount * _rate, 2), _minimum, _maximum);

    private static decimal Read(PluginContext context, string key, decimal defaultValue) =>
        context.Setting(key) is { } value ? decimal.Parse(value, CultureInfo.InvariantCulture) : defaultValue;
}
