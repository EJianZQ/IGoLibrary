using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record HealthCheckItem(
    string Key,
    string Title,
    string Message,
    HealthSeverity Severity);
