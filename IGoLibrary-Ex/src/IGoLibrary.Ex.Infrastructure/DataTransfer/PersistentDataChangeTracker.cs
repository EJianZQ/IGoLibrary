using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

public sealed class PersistentDataChangeTracker : IPersistentDataChangeTracker
{
    private readonly string _statePath;
    private readonly ILogger<PersistentDataChangeTracker> _logger;
    private readonly object _gate = new();
    private long _version;
    private bool _isDirty;
    private bool _isAutomaticUploadPaused;
    private string? _automaticUploadPauseReason;

    public PersistentDataChangeTracker(
        StorageLocations locations,
        ILogger<PersistentDataChangeTracker> logger)
    {
        _logger = logger;
        var directory = Path.Combine(locations.DataDirectory, ".backup-sync");
        _statePath = Path.Combine(directory, "change-state.json");
        TryLoad();
    }

    public event EventHandler? Changed;

    public long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public bool IsDirty
    {
        get
        {
            lock (_gate)
            {
                return _isDirty;
            }
        }
    }

    public bool IsAutomaticUploadPaused
    {
        get
        {
            lock (_gate)
            {
                return _isAutomaticUploadPaused;
            }
        }
    }

    public string? AutomaticUploadPauseReason
    {
        get
        {
            lock (_gate)
            {
                return _automaticUploadPauseReason;
            }
        }
    }

    public void MarkChanged(bool pauseAutomaticUpload = false, string? pauseReason = null)
    {
        lock (_gate)
        {
            _version++;
            _isDirty = true;
            if (pauseAutomaticUpload)
            {
                _isAutomaticUploadPaused = true;
                _automaticUploadPauseReason = string.IsNullOrWhiteSpace(pauseReason)
                    ? "自动上传已暂停，需要手动确认"
                    : pauseReason;
            }
            TrySaveLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSynchronized(long synchronizedVersion)
    {
        lock (_gate)
        {
            if (_version != synchronizedVersion)
            {
                return;
            }

            _isDirty = false;
            _isAutomaticUploadPaused = false;
            _automaticUploadPauseReason = null;
            TrySaveLocked();
        }
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return;
            }

            var state = JsonSerializer.Deserialize<ChangeState>(
                File.ReadAllText(_statePath),
                Persistence.AppJson.Default);
            if (state is not null && state.Version >= 0)
            {
                _version = state.Version;
                _isDirty = state.IsDirty;
                _isAutomaticUploadPaused = state.IsAutomaticUploadPaused;
                _automaticUploadPauseReason = state.AutomaticUploadPauseReason;
            }
        }
        catch (Exception ex)
        {
            _isDirty = true;
            _logger.LogWarning(ex, "Persistent backup change state is invalid; data will be considered dirty.");
        }
    }

    private void TrySaveLocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(_statePath)!;
            Directory.CreateDirectory(directory);
            var temporary = _statePath + ".tmp";
            var json = JsonSerializer.Serialize(
                new ChangeState(
                    _version,
                    _isDirty,
                    _isAutomaticUploadPaused,
                    _automaticUploadPauseReason),
                Persistence.AppJson.Default);
            File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false));
            File.Move(temporary, _statePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist backup change state.");
        }
    }

    private sealed record ChangeState(
        long Version,
        bool IsDirty,
        bool IsAutomaticUploadPaused = false,
        string? AutomaticUploadPauseReason = null);
}
