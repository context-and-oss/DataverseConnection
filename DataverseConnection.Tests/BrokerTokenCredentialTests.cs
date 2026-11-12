using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Xunit;

namespace DataverseConnection.Tests;

public class BrokerTokenCredentialTests
{
    private const string EnvironmentUrl = "https://org.crm4.dynamics.com";
    private const string Scope = EnvironmentUrl + "/.default";
    private static TokenRequestContext Context => new([Scope]);
    private static string Response(DateTimeOffset? expiry = null) => JsonSerializer.Serialize(new
    {
        accessToken = "test-secret-token",
        expiresOn = (expiry ?? DateTimeOffset.UtcNow.AddHours(1)).ToString("O")
    }) + "\n";

    [Fact]
    public async Task SyncAndAsync_SendExactScope_AndUseOneConnectionPerRequest()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        await using var broker = new TestBroker(async (stream, ct) =>
        {
            var bytes = Encoding.UTF8.GetBytes(Response(expiry));
            // Deliberately split the response to exercise framing across multiple reads.
            foreach (var b in bytes)
                await stream.WriteAsync(new byte[] { b }, ct);
        });
        var credential = new BrokerTokenCredential("https://ORG.crm4.dynamics.com/some/path", broker.Path);

        var first = await credential.GetTokenAsync(Context, default);
        var second = credential.GetToken(Context, default);

        Assert.Equal("test-secret-token", first.Token);
        Assert.Equal(expiry, first.ExpiresOn);
        Assert.Equal(first.Token, second.Token);
        Assert.Equal(2, broker.Requests.Count);
        Assert.All(broker.Requests, request => Assert.Equal("{\"scope\":\"" + Scope + "\"}", request));
    }

    [Theory]
    [InlineData()]
    [InlineData("https://another.crm4.dynamics.com/.default")]
    [InlineData("http://org.crm4.dynamics.com/.default")]
    [InlineData("https://org.crm4.dynamics.com")]
    [InlineData("https://org.crm4.dynamics.com/.default", "https://another.crm4.dynamics.com/.default")]
    public async Task InvalidScopes_AreRejectedBeforeConnecting(params string[] scopes)
    {
        await using var broker = new TestBroker((stream, ct) => Write(stream, Response(), ct));
        var credential = new BrokerTokenCredential(EnvironmentUrl, broker.Path);

        await Assert.ThrowsAsync<AuthenticationFailedException>(async () =>
            await credential.GetTokenAsync(new TokenRequestContext(scopes), default));
        Assert.Empty(broker.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json\n")]
    [InlineData("[]\n")]
    [InlineData("{}\n")]
    [InlineData("{\"accessToken\":\"secret\"}\n")]
    [InlineData("{\"accessToken\":\"secret\",\"expiresOn\":null}\n")]
    [InlineData("{\"accessToken\":123,\"expiresOn\":\"2099-01-01T00:00:00Z\"}\n")]
    [InlineData("{\"accessToken\":\"\",\"expiresOn\":\"2099-01-01T00:00:00Z\"}\n")]
    [InlineData("{\"accessToken\":\" secret \",\"expiresOn\":\"2099-01-01T00:00:00Z\"}\n")]
    [InlineData("{\"accessToken\":\"secret\",\"expiresOn\":\"not-a-date\"}\n")]
    [InlineData("{\"accessToken\":\"secret\",\"expiresOn\":\"2099-01-01T00:00:00\"}\n")]
    [InlineData("{\"accessToken\":\"secret\",\"expiresOn\":\"01/01/2099\"}\n")]
    [InlineData("{\"accessToken\":\"secret\",\"accessToken\":\"another\",\"expiresOn\":\"2099-01-01T00:00:00Z\"}\n")]
    [InlineData("{\"accessToken\":\"secret\",\"expiresOn\":\"2099-01-01T00:00:00Z\",\"expiresOn\":\"2099-01-01T00:00:00Z\"}\n")]
    [InlineData("{\"accessToken\":\"secret\",\"expiresOn\":\"2099-01-01T00:00:00Z\",\"error\":\"secret\"}\n")]
    [InlineData("{\"accessToken\":\"secret\",\"expiresOn\":\"2099-01-01T00:00:00Z\"}")]
    [InlineData("{\"accessToken\":\"secret\",\"expiresOn\":\"2000-01-01T00:00:00Z\"}\n")]
    [InlineData("{\"accessToken\":\"secret\\u0000\",\"expiresOn\":\"2099-01-01T00:00:00Z\"}\n")]
    [InlineData("{\"accessToken\":\"secret\\uD800\",\"expiresOn\":\"2099-01-01T00:00:00Z\"}\n")]
    [InlineData("{\"accessToken\":\"secret\",\"expiresOn\":\"\\uDC00\"}\n")]
    public async Task MalformedOrExpiredResponses_FailWithoutLeakingResponse(string response)
    {
        await using var broker = new TestBroker((stream, ct) => Write(stream, response, ct));
        var credential = new BrokerTokenCredential(EnvironmentUrl, broker.Path);

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(async () =>
            await credential.GetTokenAsync(Context, default));
        Assert.DoesNotContain("secret", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task InvalidUtf8_IsRejected()
    {
        await using var broker = new TestBroker(async (stream, ct) =>
        {
            var bytes = Encoding.UTF8.GetBytes(Response());
            bytes[20] = 0xff;
            await stream.WriteAsync(bytes, ct);
        });
        var credential = new BrokerTokenCredential(EnvironmentUrl, broker.Path);

        await Assert.ThrowsAsync<AuthenticationFailedException>(async () =>
            await credential.GetTokenAsync(Context, default));
    }

    [Theory]
    [InlineData("2099-01-01T00:00:00Z")]
    [InlineData("2099-01-01T01:00:00+01:00")]
    [InlineData("2098-12-31T19:00:00-05:00")]
    public async Task Iso8601UtcAndOffsets_AreAccepted(string expiry)
    {
        var response = JsonSerializer.Serialize(new { accessToken = "token", expiresOn = expiry }) + "\n";
        await using var broker = new TestBroker((stream, ct) => Write(stream, response, ct));
        var credential = new BrokerTokenCredential(EnvironmentUrl, broker.Path);

        var token = await credential.GetTokenAsync(Context, default);
        Assert.Equal(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), token.ExpiresOn);
    }

    [Fact]
    public async Task CompleteFrame_DoesNotRequireBrokerToCloseConnection()
    {
        await using var broker = new TestBroker(async (stream, ct) =>
        {
            await Write(stream, Response(), ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });
        var credential = new BrokerTokenCredential(EnvironmentUrl, broker.Path);
        Assert.Equal("test-secret-token", (await credential.GetTokenAsync(Context, default)).Token);
    }

    [Fact]
    public async Task MissingSocket_FailsClosed_InSyncAndAsyncPaths()
    {
        var credential = new BrokerTokenCredential(EnvironmentUrl, "/tmp/missing-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<AuthenticationFailedException>(() => credential.GetToken(Context, default));
        await Assert.ThrowsAsync<AuthenticationFailedException>(async () =>
            await credential.GetTokenAsync(Context, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SilentOrTricklingBroker_HitsTotalTimeout(bool trickle)
    {
        await using var broker = new TestBroker(async (stream, ct) =>
        {
            while (true)
            {
                if (trickle)
                    await Write(stream, " ", ct);
                await Task.Delay(30, ct);
            }
        });
        var credential = new BrokerTokenCredential(EnvironmentUrl, broker.Path,
            new BrokerTokenCredentialOptions { Timeout = TimeSpan.FromMilliseconds(150) });

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(async () =>
            await credential.GetTokenAsync(Context, default));
        Assert.Contains("timed out", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_IsPreserved_ForSyncAndAsync(bool synchronous)
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var broker = new TestBroker(async (stream, ct) =>
        {
            received.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });
        var credential = new BrokerTokenCredential(EnvironmentUrl, broker.Path);
        using var cancellation = new CancellationTokenSource();
        var acquisition = synchronous
            ? Task.Run(() => credential.GetToken(Context, cancellation.Token))
            : credential.GetTokenAsync(Context, cancellation.Token).AsTask();
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquisition);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task PreCancelledRequest_DoesNotConnect()
    {
        await using var broker = new TestBroker((stream, ct) => Write(stream, Response(), ct));
        var credential = new BrokerTokenCredential(EnvironmentUrl, broker.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await credential.GetTokenAsync(Context, cancellation.Token));
        Assert.Empty(broker.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedResponse_IsRejected_WithOrWithoutNewline(bool terminated)
    {
        await using var broker = new TestBroker((stream, ct) =>
            Write(stream, new string('x', 101) + (terminated ? "\n" : ""), ct));
        var credential = new BrokerTokenCredential(EnvironmentUrl, broker.Path,
            new BrokerTokenCredentialOptions { MaxResponseBytes = 100 });

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(async () =>
            await credential.GetTokenAsync(Context, default));
        Assert.Contains("size limit", exception.Message);
    }

    [Fact]
    public async Task ResponseExactlyAtLimit_IsAccepted_AndOptionsAreSnapshotted()
    {
        var response = Response();
        await using var broker = new TestBroker((stream, ct) => Write(stream, response, ct));
        var options = new BrokerTokenCredentialOptions { MaxResponseBytes = Encoding.UTF8.GetByteCount(response) };
        var credential = new BrokerTokenCredential(EnvironmentUrl, broker.Path, options);
        options.MaxResponseBytes = 1;
        options.Timeout = TimeSpan.Zero;

        Assert.Equal("test-secret-token", (await credential.GetTokenAsync(Context, default)).Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://org.crm4.dynamics.com")]
    [InlineData("not-a-url")]
    [InlineData("https://user:password@org.crm4.dynamics.com")]
    [InlineData("https://org.crm4.dynamics.com?query=value")]
    [InlineData("https://org.crm4.dynamics.com#fragment")]
    public void InvalidEnvironment_IsRejected(string url) =>
        Assert.Throws<ArgumentException>(() => new BrokerTokenCredential(url, "/tmp/broker.sock"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative.sock")]
    [InlineData("/tmp/invalid\0.sock")]
    public void InvalidSocketPath_IsRejected(string socketPath) =>
        Assert.Throws<ArgumentException>(() => new BrokerTokenCredential(EnvironmentUrl, socketPath));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(300001)]
    public void UnboundedOrInvalidTimeout_IsRejected(int milliseconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrokerTokenCredential(EnvironmentUrl, "/tmp/broker.sock",
            new BrokerTokenCredentialOptions { Timeout = TimeSpan.FromMilliseconds(milliseconds) }));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1048577)]
    public void UnboundedOrInvalidResponseLimit_IsRejected(int bytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrokerTokenCredential(EnvironmentUrl, "/tmp/broker.sock",
            new BrokerTokenCredentialOptions { MaxResponseBytes = bytes }));

    private static async Task Write(NetworkStream stream, string response, CancellationToken ct) =>
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);

    /// <summary>A real socket peer, bounded to keep broken transport tests from hanging.</summary>
    private sealed class TestBroker : IAsyncDisposable
    {
        private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(10));
        private readonly Task _server;
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dv-" + Guid.NewGuid().ToString("N") + ".sock");
        public List<string> Requests { get; } = [];

        public TestBroker(Func<NetworkStream, CancellationToken, Task> respond)
        {
            _listener.Bind(new UnixDomainSocketEndPoint(Path));
            _listener.Listen(5);
            _server = Run(respond);
        }

        private async Task Run(Func<NetworkStream, CancellationToken, Task> respond)
        {
            try
            {
                while (true)
                {
                    using var peer = await _listener.AcceptAsync(_stop.Token);
                    using var stream = new NetworkStream(peer, ownsSocket: false);
                    using var reader = new StreamReader(stream, new UTF8Encoding(false, true), leaveOpen: true);
                    var request = await reader.ReadLineAsync(_stop.Token);
                    Assert.NotNull(request);
                    Requests.Add(request);
                    try
                    {
                        await respond(stream, _stop.Token);
                    }
                    catch (IOException) { /* The client can disconnect on cancellation or size limits. */ }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try { await _server; }
            finally
            {
                _listener.Dispose();
                _stop.Dispose();
                File.Delete(Path);
            }
        }
    }
}
