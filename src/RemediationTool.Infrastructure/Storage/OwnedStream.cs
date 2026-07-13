namespace RemediationTool.Infrastructure.Storage;

/// <summary>
/// Keeps a response/resource alive for as long as its stream is consumed and
/// disposes both together. This allows S3 response streams to be returned safely.
/// </summary>
internal sealed class OwnedStream : Stream
{
    private readonly Stream _inner;
    private readonly IDisposable _owner;
    private bool _disposed;

    public OwnedStream(Stream inner, IDisposable owner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken)
        => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
        => _inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer)
        => _inner.Read(buffer);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
        => _inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
        => _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin)
        => _inner.Seek(offset, origin);

    public override void SetLength(long value)
        => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
        => _inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer)
        => _inner.Write(buffer);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
        => _inner.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                _inner.Dispose();
            }
            finally
            {
                _owner.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            try
            {
                await _inner.DisposeAsync();
            }
            finally
            {
                _owner.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }
}
