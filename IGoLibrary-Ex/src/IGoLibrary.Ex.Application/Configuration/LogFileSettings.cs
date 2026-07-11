namespace IGoLibrary.Ex.Application.Configuration;

public sealed record LogFileSettings(bool Enabled, int RetainedFileCount)
{
    public const int DefaultRetainedFileCount = 30;
    public const int MinRetainedFileCount = 1;
    public const int MaxRetainedFileCount = 365;

    public static LogFileSettings Default { get; } = new(true, DefaultRetainedFileCount);

    public static LogFileSettings Normalize(LogFileSettings? settings)
    {
        var current = settings ?? Default;
        return current with
        {
            RetainedFileCount = Math.Clamp(
                current.RetainedFileCount,
                MinRetainedFileCount,
                MaxRetainedFileCount)
        };
    }
}
