namespace DebugProbe.AspNetCore.Internal.Streams;

internal sealed class BoundedResponseCaptureStream : Stream
{
    private readonly Stream _inner;
    private readonly MemoryStream _capture = new();
    private readonly int _captureLimit;

    public BoundedResponseCaptureStream(Stream inner, int captureLimit)
    {
        _inner = inner;
        _captureLimit = captureLimit;
    }

    public long TotalBytesWritten { get; private set; }

    public byte[] CapturedBytes => _capture.ToArray();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        Capture(buffer.AsSpan(offset, count));
        _inner.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        Capture(buffer.AsSpan(offset, count));
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Capture(buffer);
        _inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        Capture(buffer.Span);
        await _inner.WriteAsync(buffer, cancellationToken);
    }

    private void Capture(ReadOnlySpan<byte> buffer)
    {
        TotalBytesWritten += buffer.Length;

        var remaining = _captureLimit - (int)_capture.Length;

        if (remaining <= 0)
        {
            return;
        }

        _capture.Write(buffer[..Math.Min(buffer.Length, remaining)]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _capture.Dispose();
        }

        base.Dispose(disposing);
    }
}
