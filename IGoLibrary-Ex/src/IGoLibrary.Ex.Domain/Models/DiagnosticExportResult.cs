namespace IGoLibrary.Ex.Domain.Models;

public sealed record DiagnosticExportResult(
    string FilePath,
    DateTimeOffset ExportedAt);
