namespace IGoLibrary.Ex.Application.Exceptions;

public sealed class BackupSyncConflictException(string message) : InvalidOperationException(message);
