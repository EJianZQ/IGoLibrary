using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class SafeEventPublisher
{
    public static void Publish<TEventArgs>(
        object sender,
        EventHandler<TEventArgs>? handlers,
        TEventArgs args,
        ILogger logger,
        string failureMessage)
        where TEventArgs : EventArgs
    {
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(sender, args);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, failureMessage);
            }
        }
    }
}
