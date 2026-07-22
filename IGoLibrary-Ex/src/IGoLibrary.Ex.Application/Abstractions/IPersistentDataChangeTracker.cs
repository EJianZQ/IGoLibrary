namespace IGoLibrary.Ex.Application.Abstractions;

public interface IPersistentDataChangeTracker
{
    event EventHandler? Changed;

    long Version { get; }

    bool IsDirty { get; }

    bool IsAutomaticUploadPaused { get; }

    string? AutomaticUploadPauseReason { get; }

    void MarkChanged(bool pauseAutomaticUpload = false, string? pauseReason = null);

    void MarkSynchronized(long synchronizedVersion);
}
