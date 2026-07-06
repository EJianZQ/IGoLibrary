using System.ComponentModel;

namespace IGoLibrary.Ex.Desktop.ViewModels;

internal sealed class ViewModelPropertyBridge(Action<string> notifyPropertyChanged)
{
    private readonly List<(INotifyPropertyChanged Source, PropertyChangedEventHandler Handler)> _subscriptions = [];

    public void Forward(
        INotifyPropertyChanged source,
        string sourcePropertyName,
        params string[] targetPropertyNames)
    {
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (!string.Equals(e.PropertyName, sourcePropertyName, StringComparison.Ordinal))
            {
                return;
            }

            foreach (var targetPropertyName in targetPropertyNames)
            {
                notifyPropertyChanged(targetPropertyName);
            }
        };

        source.PropertyChanged += handler;
        _subscriptions.Add((source, handler));
    }

    public void ForwardSame(INotifyPropertyChanged source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            Forward(source, propertyName, propertyName);
        }
    }

    public void Disconnect()
    {
        foreach (var (source, handler) in _subscriptions)
        {
            source.PropertyChanged -= handler;
        }

        _subscriptions.Clear();
    }
}
