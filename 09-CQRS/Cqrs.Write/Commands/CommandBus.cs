using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cqrs.Write.Commands;

/// <summary>An intention to change the state (in the imperative). It returns no data.</summary>
public interface ICommand;

public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task Handle(TCommand command);
}

/// <summary>Shape validation of a command, run BEFORE the handler (the business rules stay in the aggregate).</summary>
public interface IValidator<in TCommand> where TCommand : ICommand
{
    IEnumerable<string> Validate(TCommand command);
}

public class InvalidCommandException(IReadOnlyList<string> errors) : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Single entry point for the commands: a shared "pipeline" (validation, logging)
/// before the handler. It is the role MediatR often plays, written here in a few lines.
/// </summary>
public class CommandBus(IServiceProvider services, ILogger<CommandBus> logger)
{
    public async Task Send<TCommand>(TCommand command) where TCommand : ICommand
    {
        var errors = services.GetServices<IValidator<TCommand>>()
            .SelectMany(validator => validator.Validate(command))
            .ToList();
        if (errors.Count > 0)
            throw new InvalidCommandException(errors);

        logger.LogInformation("Command {Command}: {Content}", typeof(TCommand).Name, command);
        await services.GetRequiredService<ICommandHandler<TCommand>>().Handle(command);
    }
}
