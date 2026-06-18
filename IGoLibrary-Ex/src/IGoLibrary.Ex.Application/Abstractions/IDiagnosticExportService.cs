using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IDiagnosticExportService
{
    Task<DiagnosticExportResult> ExportAsync(CancellationToken cancellationToken = default);
}
