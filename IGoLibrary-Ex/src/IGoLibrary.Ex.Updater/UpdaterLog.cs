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
        _path = Path.Combine(
            directory,
            $"{DateTime.UtcNow:yyyyMMdd}-{NormalizeFileSegment(transactionId)}-{NormalizeFileSegment(role)}.log");
    }

    public static UpdaterLog? TryCreateEmergency(string role)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Core.UpdateProtocol.ProductName,
                "updates",
                "logs");
            return new UpdaterLog(directory, "emergency", role);
        }
        catch
        {
            return null;
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null)
    {
        var detail = exception is null
            ? message
            : $"{message} | {exception}";
        Write("ERROR", detail);
    }

    private void Write(string level, string message)
    {
        var safeMessage = UpdaterLogSanitizer.Sanitize(message)
            .ReplaceLineEndings("\\n");
        try
        {
            lock (_syncRoot)
            {
                File.AppendAllText(
                    _path,
                    $"{DateTimeOffset.UtcNow:O} [{level}] {safeMessage}{Environment.NewLine}",
                    Utf8NoBom);
            }
        }
        catch
        {
        }
    }

    private static string NormalizeFileSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = new string(value
            .Where(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Take(64)
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }
}
