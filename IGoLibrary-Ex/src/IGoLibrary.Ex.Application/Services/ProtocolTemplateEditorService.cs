using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class ProtocolTemplateEditorService(
    IProtocolTemplateStore protocolTemplateStore) : IProtocolTemplateEditorService
{
    public Task<TraceIntGraphQlTemplates> LoadTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
    }

    public Task SaveOverridesAsync(
        TraceIntGraphQlTemplateOverrides overrides,
        CancellationToken cancellationToken = default)
    {
        return protocolTemplateStore.SaveOverridesAsync(overrides, cancellationToken);
    }

    public Task ResetOverridesAsync(CancellationToken cancellationToken = default)
    {
        return protocolTemplateStore.ResetOverridesAsync(cancellationToken);
    }
}
