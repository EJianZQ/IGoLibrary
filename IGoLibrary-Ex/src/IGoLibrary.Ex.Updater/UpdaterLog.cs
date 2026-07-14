using System.Text;

namespace IGoLibrary.Ex.Updater;

internal sealed class UpdaterLog
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly object _syncRoot = new();
    private readonly string _path;

    public UpdaterLog(string directory, string transactionId, string role)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd}-{transactionId}-{role}.log");
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null)
    {
        var detail = exception is null
            ? message
            : $"{message} | {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", detail);
    }

    private void Write(string level, string message)
    {
        var safeMessage = message.Replace('\r', ' ').Replace('\n', ' ');
        lock (_syncRoot)
        {
            File.AppendAllText(
                _path,
                $"{DateTimeOffset.UtcNow:O} [{level}] {safeMessage}{Environment.NewLine}",
                Utf8NoBom);
        }
    }
}
