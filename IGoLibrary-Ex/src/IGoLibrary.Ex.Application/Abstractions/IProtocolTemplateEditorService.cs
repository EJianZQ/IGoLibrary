using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IProtocolTemplateEditorService
{
    Task<TraceIntGraphQlTemplateSet> LoadTemplatesAsync(CancellationToken cancellationToken = default);

    Task SaveOverridesAsync(
        TraceIntGraphQlTemplateOverrides overrides,
        CancellationToken cancellationToken = default);

    Task ResetOverridesAsync(CancellationToken cancellationToken = default);
}
