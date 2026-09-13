using System.Net;

namespace IGoLibrary.Ex.Infrastructure.Logging;

internal sealed class NetworkCaptureContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly NetworkBodyCapture _capture;
    private readonly Action<string, bool> _completed;
    private readonly long? _length;
    private int _finished;

    public NetworkCaptureContent(HttpContent inner, NetworkLogSession session, Action<string, bool> completed)
    {
        _inner = inner;
        _capture = session.CreateBody(() => NetworkLogSanitizer.IsText(Headers.ContentType?.ToString()));
        _completed = completed;
        _length = inner.Headers.ContentLength;
        foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    internal void Finish()
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        try { _completed(_capture.Render(Headers.ContentType?.ToString(), _length), _capture.HasFailed); }
        catch { }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length ?? 0;
        return _length.HasValue;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        using var observer = new NetworkCaptureStream(stream, _capture, reading: false);
        try
        {
            await _inner.CopyToAsync(observer, context, cancellationToken).ConfigureAwait(false);
            _capture.Complete();
        }
        catch { _capture.Fail(); throw; }
        finally { Finish(); }
    }

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
    {
        try { return new NetworkCaptureStream(_inner.ReadAsStream(cancellationToken), _capture, reading: true, Finish, leaveOpen: false); }
        catch { _capture.Fail(); Finish(); throw; }
    }

    protected override Task<Stream> CreateContentReadStreamAsync() => CreateContentReadStreamAsync(CancellationToken.None);

    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new NetworkCaptureStream(await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), _capture, reading: true, Finish, leaveOpen: false);
        }
        catch { _capture.Fail(); Finish(); throw; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Finish(); _inner.Dispose(); }
        base.Dispose(disposing);
    }
}
