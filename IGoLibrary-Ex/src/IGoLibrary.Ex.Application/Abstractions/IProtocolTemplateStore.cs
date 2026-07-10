using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IProtocolTemplateStore
{
    Task<TraceIntProtocolTemplates> GetDefaultTemplatesAsync(CancellationToken cancellationToken = default);

    Task<TraceIntProtocolTemplates> GetEffectiveTemplatesAsync(CancellationToken cancellationToken = default);

    Task<TraceIntProtocolTemplates> GetEditableTemplatesAsync(CancellationToken cancellationToken = default);

    Task SaveOverridesAsync(TraceIntProtocolTemplateOverrides overrides, CancellationToken cancellationToken = default);

    Task ResetOverridesAsync(CancellationToken cancellationToken = default);
}
