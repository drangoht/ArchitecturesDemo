using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Styles.Contracts;
using Styles.Tooling.Grpc;
using Styles.Tooling.Protobuf;

namespace Styles.Tests;

public class GrpcTests : IAsyncLifetime
{
    private readonly TestSystem _system = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _system.DisposeAsync();

    /// <summary>A gRPC call "by hand": enough to see what really travels.</summary>
    private async Task<HttpResponseMessage> RawCall(string method, object request, string contentType = GrpcFrame.ContentType)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/{BankGrpcService.Name}/{method}")
        {
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(GrpcFrame.Frame(Protobuf.Serialize(request)))
        };
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var response = await _system.Http.SendAsync(httpRequest);
        await response.Content.LoadIntoBufferAsync();   // the trailers are only known once the body is read
        return response;
    }

    private static string Trailer(HttpResponseMessage response, string name) => response.TrailingHeaders.GetValues(name).Single();

    [Fact]
    public async Task A_unary_call_is_a_POST_whose_body_is_a_framed_message_and_the_status_in_the_trailers()
    {
        var response = await RawCall("GetAccount", new AccountRequest { Number = "10002" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/grpc", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(0, body[0]);   // not compressed
        Assert.Equal(body.Length - 5, (body[1] << 24) | (body[2] << 16) | (body[3] << 8) | body[4]);
        var account = Protobuf.Deserialize<Account>(body.AsSpan(5));
        Assert.Equal(("Savings account", 815_000L, AccountType.Savings), (account.Label, account.BalanceCents, account.Type));
        Assert.Equal("0", Trailer(response, "grpc-status"));
    }

    [Fact]
    public async Task A_business_error_still_answers_200_with_the_gRPC_status_in_the_trailers()
    {
        var response = await RawCall("GetAccount", new AccountRequest { Number = "99999" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(((int)GrpcStatus.NotFound).ToString(), Trailer(response, "grpc-status"));
        Assert.Equal("Unknown account \"99999\".", Uri.UnescapeDataString(Trailer(response, "grpc-message")));

        var error = await Assert.ThrowsAsync<GrpcError>(() =>
            _system.Grpc.Grpc.Unary<AccountRequest, Account>("GetAccount", new AccountRequest { Number = "99999" }));
        Assert.Equal(GrpcStatus.NotFound, error.Status);
    }

    [Fact]
    public async Task Without_the_gRPC_content_type_the_request_is_refused()
    {
        var response = await RawCall("GetAccount", new AccountRequest { Number = "10001" }, "application/json");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task The_stream_pushes_each_operation_as_soon_as_it_happens()
    {
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var stream = _system.Grpc.FollowOperations("10001", cancellation: stopping.Token).GetAsyncEnumerator(stopping.Token);
        var first = stream.MoveNextAsync().AsTask();
        await WaitUntil(() => _system.Bank.SubscriberCount == 1);
        Assert.False(first.IsCompleted);   // nothing as long as nothing happens

        await _system.Rest.Transfer("20001", "10001", 12.34m, "Refund");

        Assert.True(await first);
        Assert.Equal(("Refund from 20001", 1234L), (stream.Current.Label, stream.Current.AmountCents));

        await _system.GraphQL.Transfer("10001", "10002", 5m, "Savings");
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(-500L, stream.Current.AmountCents);
    }

    [Fact]
    public async Task The_client_deadline_also_stops_the_work_on_the_server_side()
    {
        using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));   // in case the deadline were not kept
        var stopwatch = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<GrpcError>(async () =>
        {
            await foreach (var _ in _system.Grpc.FollowOperations("10001", TimeSpan.FromMilliseconds(300), safetyNet.Token)) { }
        });

        Assert.Equal(GrpcStatus.DeadlineExceeded, error.Status);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5));
        await WaitUntil(() => _system.Bank.SubscriberCount == 0);   // the subscription was released
    }

    [Fact]
    public async Task A_client_that_gives_up_frees_the_server()
    {
        using var stopping = new CancellationTokenSource();
        var listening = Task.Run(async () =>
        {
            await foreach (var _ in _system.Grpc.FollowOperations("10001", cancellation: stopping.Token)) { }
        });
        await WaitUntil(() => _system.Bank.SubscriberCount == 1);

        stopping.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listening);
        await WaitUntil(() => _system.Bank.SubscriberCount == 0);
    }

    [Fact]
    public async Task Following_an_unknown_account_fails_right_away()
    {
        var error = await Assert.ThrowsAsync<GrpcError>(async () =>
        {
            await foreach (var _ in _system.Grpc.FollowOperations("99999", deadline: TimeSpan.FromSeconds(5))) { }
        });
        Assert.Equal(GrpcStatus.NotFound, error.Status);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException("Condition not reached.");
            await Task.Delay(10);
        }
    }
}
