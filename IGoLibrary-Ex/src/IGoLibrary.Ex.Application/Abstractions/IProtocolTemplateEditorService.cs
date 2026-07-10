using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IProtocolTemplateEditorService
{
    Task<TraceIntProtocolTemplates> LoadDefaultTemplatesAsync(CancellationToken cancellationToken = default);

    Task<TraceIntProtocolTemplates> LoadTemplatesAsync(CancellationToken cancellationToken = default);

    Task SaveOverridesAsync(
        TraceIntProtocolTemplateOverrides overrides,
        CancellationToken cancellationToken = default);

    Task ResetOverridesAsync(CancellationToken cancellationToken = default);
}
