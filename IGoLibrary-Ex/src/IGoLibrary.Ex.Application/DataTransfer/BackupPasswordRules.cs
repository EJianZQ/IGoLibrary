namespace IGoLibrary.Ex.Application.Backup;

public static class BackupPasswordRules
{
    public const int MinimumLength = 12;

    public const int MaximumLength = 256;

    public static void Validate(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length is < MinimumLength or > MaximumLength)
        {
            throw new ArgumentException(
                $"备份密码长度必须为 {MinimumLength}～{MaximumLength} 个字符",
                nameof(password));
        }
    }
}
