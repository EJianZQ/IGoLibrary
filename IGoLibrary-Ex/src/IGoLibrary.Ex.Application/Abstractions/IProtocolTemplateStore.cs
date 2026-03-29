using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IProtocolTemplateStore
{
    Task<ProtocolTemplateSet> GetEffectiveTemplatesAsync(CancellationToken cancellationToken = default);

    Task SaveOverridesAsync(ProtocolTemplateOverrides overrides, CancellationToken cancellationToken = default);

    Task ResetOverridesAsync(CancellationToken cancellationToken = default);
}
