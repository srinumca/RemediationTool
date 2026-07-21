using System.Text;
using RemediationTool.Infrastructure.Storage;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class StreamInfrastructureTests
{
    [Fact]
    public async Task OwnedStream_ForwardsReadWriteSeekAndFlushOperations()
    {
        await using var inner = new MemoryStream();
        var owner = new TrackingDisposable();
        await using var stream = new OwnedStream(inner, owner);
        var payload = Encoding.UTF8.GetBytes("abcdef");

        await stream.WriteAsync(payload);
        await stream.FlushAsync(CancellationToken.None);
        Assert.Equal(payload.Length, stream.Length);
        Assert.True(stream.CanRead);
        Assert.True(stream.CanWrite);
        Assert.True(stream.CanSeek);

        stream.Seek(0, SeekOrigin.Begin);
        var buffer = new byte[payload.Length];
        var read = await stream.ReadAsync(buffer);

        Assert.Equal(payload.Length, read);
        Assert.Equal(payload, buffer);
        stream.SetLength(3);
        Assert.Equal(3, stream.Length);
    }

    [Fact]
    public void OwnedStream_Dispose_DisposesInnerAndOwnerExactlyOnce()
    {
        var inner = new TrackingMemoryStream();
        var owner = new TrackingDisposable();
        var stream = new OwnedStream(inner, owner);

        stream.Dispose();
        stream.Dispose();

        Assert.Equal(1, inner.DisposeCount);
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task OwnedStream_DisposeAsync_DisposesInnerAndOwnerExactlyOnce()
    {
        var inner = new TrackingMemoryStream();
        var owner = new TrackingDisposable();
        var stream = new OwnedStream(inner, owner);

        await stream.DisposeAsync();
        await stream.DisposeAsync();

        Assert.Equal(1, inner.DisposeAsyncCount);
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public void OwnedStream_DisposeInnerFailure_StillDisposesOwner()
    {
        var inner = new ThrowingDisposeStream();
        var owner = new TrackingDisposable();
        var stream = new OwnedStream(inner, owner);

        var exception = Assert.Throws<IOException>(() => stream.Dispose());

        Assert.Equal("dispose failed", exception.Message);
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public void OwnedStream_Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OwnedStream(null!, new TrackingDisposable()));
        Assert.Throws<ArgumentNullException>(() =>
            new OwnedStream(new MemoryStream(), null!));
    }

    [Fact]
    public async Task TemporarySeekableStream_IsUniqueSeekableAndDeletedOnClose()
    {
        string firstPath;
        string secondPath;

        await using (var first = TemporarySeekableStream.Create())
        await using (var second = TemporarySeekableStream.Create())
        {
            firstPath = first.Name;
            secondPath = second.Name;
            Assert.NotEqual(firstPath, secondPath);
            Assert.True(first.CanRead);
            Assert.True(first.CanWrite);
            Assert.True(first.CanSeek);

            var payload = Encoding.UTF8.GetBytes("temporary-data");
            await first.WriteAsync(payload);
            first.Position = 0;
            var buffer = new byte[payload.Length];
            Assert.Equal(payload.Length, await first.ReadAsync(buffer));
            Assert.Equal(payload, buffer);
        }

        Assert.False(File.Exists(firstPath));
        Assert.False(File.Exists(secondPath));
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public int DisposeCount { get; private set; }

        public int DisposeAsyncCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            DisposeAsyncCount++;
            await base.DisposeAsync();
        }
    }

    private sealed class ThrowingDisposeStream : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                throw new IOException("dispose failed");

            base.Dispose(disposing);
        }
    }
}
