using System.Globalization;
using Mk.Contracts;

namespace Mk.Plugins.NightLimit;

/// <summary>"Check" extension point: withdrawals limited at night (time range and limit set in plugin.json).</summary>
public sealed class NightLimit : IOperationCheck
{
    private decimal _limit = 200m;
    private TimeOnly _start = new(22, 0), _end = new(6, 0);

    public string Name => "NightLimit";
    public string Description => "Limits the amount of withdrawals during the night.";

    public void Initialize(PluginContext context)
    {
        if (context.Setting("limit") is { } limit)
            _limit = decimal.Parse(limit, CultureInfo.InvariantCulture);
        if (context.Setting("start") is { } start)
            _start = TimeOnly.Parse(start, CultureInfo.InvariantCulture);
        if (context.Setting("end") is { } end)
            _end = TimeOnly.Parse(end, CultureInfo.InvariantCulture);
        context.Log($"withdrawals limited to {_limit} € from {_start:HH\\:mm} to {_end:HH\\:mm}");
    }

    public string? Check(ProposedOperation operation)
    {
        if (operation.Type != OperationType.Withdrawal || operation.Amount <= _limit)
            return null;

        var time = TimeOnly.FromDateTime(operation.Date);
        var atNight = _start <= _end
            ? time >= _start && time < _end
            : time >= _start || time < _end;  // range spanning midnight
        return atNight ? $"Withdrawal limited to {_limit} € between {_start:HH\\:mm} and {_end:HH\\:mm}." : null;
    }
}
