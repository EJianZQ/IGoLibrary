using System.Diagnostics;

namespace IGoLibrary.Ex.Infrastructure.Logging;

public sealed class TraceListenerRegistrar(AppTraceListener listener) : IDisposable
{
    private readonly object _gate = new();
    private bool _attached;

    public void Attach()
    {
        lock (_gate)
        {
            if (_attached)
            {
                return;
            }

            Trace.AutoFlush = true;
            Trace.Listeners.Add(listener);
            _attached = true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_attached)
            {
                return;
            }

            Trace.Listeners.Remove(listener);
            _attached = false;
        }
    }
}
