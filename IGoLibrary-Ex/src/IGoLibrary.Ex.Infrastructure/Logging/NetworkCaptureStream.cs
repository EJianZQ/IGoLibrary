namespace IGoLibrary.Ex.Infrastructure.Logging;

/// <summary>Observes successful I/O without adding reads, writes or buffering to the underlying stream.</summary>
public sealed class NetworkCaptureStream(
    Stream inner, NetworkBodyCapture capture, bool reading, Action? finished = null, bool leaveOpen = true) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanTimeout => inner.CanTimeout;
    public override int ReadTimeout { get => inner.ReadTimeout; set => inner.ReadTimeout = value; }
    public override int WriteTimeout { get => inner.WriteTimeout; set => inner.WriteTimeout = value; }
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);

    private void Observe(ReadOnlySpan<byte> bytes, bool end)
    {
        try
        {
            capture.Append(bytes);
            if (end) { capture.Complete(); finished?.Invoke(); }
        }
        catch { /* Logging must not affect I/O. */ }
    }

    private void Failed()
    {
        try { capture.Fail(); finished?.Invoke(); }
        catch { }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        try
        {
            var count = inner.Read(buffer);
            if (reading) Observe(buffer[..count], count == 0 && buffer.Length > 0);
            return count;
        }
        catch { Failed(); throw; }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (reading) Observe(buffer.Span[..count], count == 0 && buffer.Length > 0);
            return count;
        }
        catch { Failed(); throw; }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        try { inner.Write(buffer); if (!reading) Observe(buffer, false); }
        catch { Failed(); throw; }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (!reading) Observe(buffer.Span, false);
        }
        catch { Failed(); throw; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { finished?.Invoke(); } catch { }
            if (!leaveOpen) inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        try { finished?.Invoke(); } catch { }
        if (!leaveOpen) await inner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
