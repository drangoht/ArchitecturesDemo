using Med.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Tests;

/// <summary>The hand-written mediator, alone, with toy messages: dispatch, pipeline order, notifications.</summary>
public class MediatorTests
{
    private readonly Trace _trace = new();

    private IMediator Build(Action<IServiceCollection, MediatorBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_trace);
        var builder = services.AddMediator();   // no assembly scanned: each test registers what it needs
        configure(services, builder);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Send_reaches_the_handler_of_the_request_type()
    {
        var mediator = Build((s, _) => s.AddScoped<IRequestHandler<Ping, string>, PingHandler>());

        Assert.Equal("pong: hello", await mediator.Send(new Ping("hello")));
    }

    [Fact]
    public async Task A_request_without_a_handler_fails_at_run_time_with_its_name()
    {
        var mediator = Build((_, _) => { });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new Ping("hello")));

        Assert.Contains("Ping", error.Message);
    }

    [Fact]
    public async Task The_behaviors_wrap_the_handler_in_their_registration_order()
    {
        var mediator = Build((s, b) =>
        {
            s.AddScoped<IRequestHandler<Ping, string>, PingHandler>();
            b.AddBehavior(typeof(OuterBehavior<,>)).AddBehavior(typeof(InnerBehavior<,>));
        });

        await mediator.Send(new Ping("hello"));

        Assert.Equal(["outer: before", "inner: before", "handler", "inner: after", "outer: after"], _trace.Steps);
    }

    [Fact]
    public async Task A_behavior_can_answer_without_calling_the_handler()
    {
        var mediator = Build((s, b) =>
        {
            s.AddScoped<IRequestHandler<Ping, string>, PingHandler>();
            b.AddBehavior(typeof(CacheBehavior<,>));
        });

        Assert.Equal("from the cache", await mediator.Send(new Ping("cached")));
        Assert.Empty(_trace.Steps);
    }

    [Fact]
    public async Task Publish_calls_every_handler_in_their_registration_order()
    {
        var mediator = Build((s, _) =>
        {
            s.AddScoped<INotificationHandler<Rang>, FirstListener>();
            s.AddScoped<INotificationHandler<Rang>, SecondListener>();
        });

        await mediator.Publish(new Rang());

        Assert.Equal(["first", "second"], _trace.Steps);
    }

    [Fact]
    public async Task Publish_without_any_handler_does_nothing()
    {
        var mediator = Build((_, _) => { });

        await mediator.Publish(new Rang());

        Assert.Empty(_trace.Steps);
    }

    [Fact]
    public async Task A_failing_notification_handler_stops_the_next_ones_and_reaches_the_caller()
    {
        var mediator = Build((s, _) =>
        {
            s.AddScoped<INotificationHandler<Rang>, FailingListener>();
            s.AddScoped<INotificationHandler<Rang>, SecondListener>();
        });

        await Assert.ThrowsAsync<TimeoutException>(() => mediator.Publish(new Rang()));

        Assert.Empty(_trace.Steps);
    }

    // ---------- Toy messages ----------

    public class Trace
    {
        public List<string> Steps { get; } = [];
    }

    public record Ping(string Text) : IRequest<string>;

    public class PingHandler(Trace trace) : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken ct)
        {
            trace.Steps.Add("handler");
            return Task.FromResult($"pong: {request.Text}");
        }
    }

    public class OuterBehavior<TRequest, TResponse>(Trace trace) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            trace.Steps.Add("outer: before");
            var response = await next();
            trace.Steps.Add("outer: after");
            return response;
        }
    }

    public class InnerBehavior<TRequest, TResponse>(Trace trace) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            trace.Steps.Add("inner: before");
            var response = await next();
            trace.Steps.Add("inner: after");
            return response;
        }
    }

    public class CacheBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct) =>
            request is Ping { Text: "cached" } ? Task.FromResult((TResponse)(object)"from the cache") : next();
    }

    public record Rang : INotification;

    public class FirstListener(Trace trace) : INotificationHandler<Rang>
    {
        public Task Handle(Rang notification, CancellationToken ct)
        {
            trace.Steps.Add("first");
            return Task.CompletedTask;
        }
    }

    public class SecondListener(Trace trace) : INotificationHandler<Rang>
    {
        public Task Handle(Rang notification, CancellationToken ct)
        {
            trace.Steps.Add("second");
            return Task.CompletedTask;
        }
    }

    public class FailingListener : INotificationHandler<Rang>
    {
        public Task Handle(Rang notification, CancellationToken ct) => throw new TimeoutException("The mail server is down.");
    }
}
