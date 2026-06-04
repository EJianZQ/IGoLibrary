using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IProtocolTemplateStore
{
    Task<TraceIntGraphQlTemplates> GetEffectiveTemplatesAsync(CancellationToken cancellationToken = default);

    Task SaveOverridesAsync(TraceIntGraphQlTemplateOverrides overrides, CancellationToken cancellationToken = default);

    Task ResetOverridesAsync(CancellationToken cancellationToken = default);
}
