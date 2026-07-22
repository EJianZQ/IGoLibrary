namespace IGoLibrary.Ex.Desktop;

internal sealed record RestartArguments(
    int? ParentProcessId,
    string[] ApplicationArguments,
    string? UpdateTransactionId = null,
    string? RestoreTransactionId = null)
{
    public const string ParentProcessIdOption = "--storage-restart-parent-pid";
    public const string UpdateTransactionOption = "--update-transaction";
    public const string RestoreTransactionOption = "--data-restore-transaction";

    public static RestartArguments Parse(IReadOnlyList<string> arguments)
    {
        int? parentProcessId = null;
        string? updateTransactionId = null;
        string? restoreTransactionId = null;
        var forwarded = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], UpdateTransactionOption, StringComparison.Ordinal))
            {
                if (index + 1 >= arguments.Count ||
                    !Guid.TryParseExact(arguments[++index], "N", out var transactionId))
                {
                    throw new ArgumentException("更新事务参数无效", nameof(arguments));
                }

                updateTransactionId = transactionId.ToString("N");
                continue;
            }

            if (string.Equals(arguments[index], RestoreTransactionOption, StringComparison.Ordinal))
            {
                if (index + 1 >= arguments.Count ||
                    !Guid.TryParseExact(arguments[++index], "N", out var transactionId))
                {
                    throw new ArgumentException("数据恢复事务参数无效", nameof(arguments));
                }

                restoreTransactionId = transactionId.ToString("N");
                continue;
            }

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
                throw new ArgumentException("重启父进程参数无效", nameof(arguments));
            }

            parentProcessId = parsed;
        }

        return new RestartArguments(
            parentProcessId,
            forwarded.ToArray(),
            updateTransactionId,
            restoreTransactionId);
    }
}
