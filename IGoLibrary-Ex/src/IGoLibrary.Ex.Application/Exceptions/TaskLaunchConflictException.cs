namespace IGoLibrary.Ex.Application.Exceptions;

public sealed class TaskLaunchConflictException(string message) : InvalidOperationException(message);
