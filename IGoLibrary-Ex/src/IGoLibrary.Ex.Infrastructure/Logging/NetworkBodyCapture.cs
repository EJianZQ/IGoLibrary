namespace IGoLibrary.Ex.Infrastructure.Logging;

/// <summary>Bounded capture of bytes already consumed by the transport or caller.</summary>
public sealed class NetworkBodyCapture(Func<bool> isActive, Func<bool>? captureBytes = null)
{
    private readonly object _gate = new();
    private MemoryStream? _buffer;
    private long _observed;
    private bool _complete;
    private bool _failed;
    private bool _stopped;
    private bool _rendered;
    private bool _sanitizationFailed;
    internal int BufferedBytes { get { lock (_gate) return (int)(_buffer?.Length ?? 0); } }
    public bool HasFailed { get { lock (_gate) return _failed || _sanitizationFailed; } }

    public void Append(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            if (_rendered) return;
            if (!isActive()) { Clear(); _stopped = true; return; }
            _observed += bytes.Length;
            if (captureBytes?.Invoke() == false) return;
            if (_observed > NetworkLogSanitizer.BodyLimit) { Clear(); return; }
            (_buffer ??= new MemoryStream()).Write(bytes);
        }
    }

    public void Complete() { lock (_gate) _complete = true; }
    public void Fail() { lock (_gate) _failed = true; }

    public string Render(string? contentType, long? declaredLength = null, IEnumerable<string>? secrets = null)
    {
        lock (_gate)
        {
            var info = $"内容类型={NetworkLogSanitizer.Text(contentType ?? "未知")}；声明字节数={declaredLength?.ToString() ?? "未知"}；已观察字节数={_observed}";
            try
            {
                if (_stopped || !isActive()) return $"{info}；设置关闭后停止采集";
                if (_failed) return $"{info}；读取失败，正文已省略";
                if (_observed == 0 && (_complete || declaredLength == 0)) return $"{info}；无正文";
                if (!NetworkLogSanitizer.IsText(contentType)) return $"{info}；二进制或未知类型，正文已省略";
                if (_observed > NetworkLogSanitizer.BodyLimit) return $"{info}；超过上限，正文已省略";
                if (!_complete && declaredLength != _observed) return $"{info}；{(_observed == 0 ? "未消费" : "仅部分消费")}，正文已省略";
                if (_observed == 0) return $"{info}；无正文";
                return $"{info}；完整；正文={NetworkLogSanitizer.Body(_buffer!.GetBuffer().AsSpan(0, (int)_buffer.Length), contentType!, secrets)}";
            }
            catch { _sanitizationFailed = true; return $"{info}；无法安全脱敏，正文已省略"; }
            finally { _rendered = true; Clear(); }
        }
    }

    private void Clear()
    {
        if (_buffer is not null)
        {
            _buffer.GetBuffer().AsSpan().Clear();
            _buffer.Dispose();
            _buffer = null;
        }
    }
}
