using System.Diagnostics;

namespace Styles.Client;

/// <summary>What a scenario cost on the network: number of HTTP requests, bytes sent and received (bodies only).</summary>
public sealed class Measure
{
    private int _requests;
    private long _bytesSent, _bytesReceived;

    public int Requests => _requests;
    public long BytesSent => Interlocked.Read(ref _bytesSent);
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);
    public TimeSpan Duration { get; internal set; }

    internal void Request(long sent)
    {
        Interlocked.Increment(ref _requests);
        Interlocked.Add(ref _bytesSent, sent);
    }

    internal void Received(long bytes) => Interlocked.Add(ref _bytesReceived, bytes);

    public override string ToString() =>
        $"{Requests} request(s), {BytesSent} B sent, {BytesReceived} B received, {Duration.TotalMilliseconds:0} ms";
}

/// <summary>
/// Counts the requests and the bytes of the bodies (the headers are not counted: HTTP/2 compresses them, HTTP/1.1 does not),
/// and can add a latency per request to simulate a mobile network.
/// </summary>
public sealed class HttpCounter : DelegatingHandler
{
    public Measure Measure { get; private set; } = new();

    public TimeSpan Latency { get; set; }

    public HttpCounter() : base(new SocketsHttpHandler()) { }

    public HttpCounter(HttpMessageHandler inner) : base(inner) { }

    /// <summary>Starts a new measure and returns it.</summary>
    public Measure New() => Measure = new Measure();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        var measure = Measure;
        long sent = 0;
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellation);
            sent = request.Content.Headers.ContentLength ?? 0;
        }
        measure.Request(sent);
        if (Latency > TimeSpan.Zero)
            await Task.Delay(Latency, cancellation);

        var response = await base.SendAsync(request, cancellation);
        var original = response.Content;
        var stream = await original.ReadAsStreamAsync(cancellation);
        var counted = new StreamContent(new CountingStream(stream, measure.Received));
        foreach (var header in original.Headers)
            counted.Headers.TryAddWithoutValidation(header.Key, header.Value);
        response.Content = counted;   // the trailers stay on the response
        return response;
    }

    private sealed class CountingStream(Stream inner, Action<long> countBytes) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            countBytes(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default)
        {
            var read = await inner.ReadAsync(buffer, cancellation);
            countBytes(read);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellation) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellation).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

public static class Timing
{
    /// <summary>Runs a scenario with a fresh measure, and returns it with its result.</summary>
    public static async Task<(T Result, Measure Measure)> Run<T>(HttpCounter counter, Func<Task<T>> scenario)
    {
        var measure = counter.New();
        var stopwatch = Stopwatch.StartNew();
        var result = await scenario();
        measure.Duration = stopwatch.Elapsed;
        return (result, measure);
    }
}
