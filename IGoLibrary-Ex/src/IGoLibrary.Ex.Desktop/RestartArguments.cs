namespace IGoLibrary.Ex.Desktop;

internal sealed record RestartArguments(int? ParentProcessId, string[] ApplicationArguments)
{
    public const string ParentProcessIdOption = "--storage-restart-parent-pid";

    public static RestartArguments Parse(IReadOnlyList<string> arguments)
    {
        int? parentProcessId = null;
        var forwarded = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], ParentProcessIdOption, StringComparison.Ordinal))
            {
                forwarded.Add(arguments[index]);
                continue;
            }

            if (index + 1 >= arguments.Count ||
                !int.TryParse(
                    arguments[++index],
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed) ||
                parsed <= 0)
            {
                throw new ArgumentException("重启父进程参数无效。", nameof(arguments));
            }

            parentProcessId = parsed;
        }

        return new RestartArguments(parentProcessId, forwarded.ToArray());
    }
}
