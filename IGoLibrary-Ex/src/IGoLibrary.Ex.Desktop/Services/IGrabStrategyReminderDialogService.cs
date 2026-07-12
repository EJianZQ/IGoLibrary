namespace IGoLibrary.Ex.Desktop.Services;

public enum GrabStrategyReminderDecision
{
    Cancel,
    SwitchToOptimal,
    KeepCurrent
}

public sealed record GrabStrategyReminderResult(
    GrabStrategyReminderDecision Decision,
    bool DisableReminder)
{
    public static GrabStrategyReminderResult Cancelled { get; } =
        new(GrabStrategyReminderDecision.Cancel, DisableReminder: false);
}

public interface IGrabStrategyReminderDialogService
{
    Task<GrabStrategyReminderResult> ShowAsync(CancellationToken cancellationToken = default);
}
