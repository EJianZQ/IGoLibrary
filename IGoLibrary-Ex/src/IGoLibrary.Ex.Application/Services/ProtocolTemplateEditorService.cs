using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class ProtocolTemplateEditorService(
    IProtocolTemplateStore protocolTemplateStore) : IProtocolTemplateEditorService
{
    public Task<TraceIntProtocolTemplates> LoadDefaultTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return protocolTemplateStore.GetDefaultTemplatesAsync(cancellationToken);
    }

    public Task<TraceIntProtocolTemplates> LoadTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return protocolTemplateStore.GetEditableTemplatesAsync(cancellationToken);
    }

    public Task SaveOverridesAsync(
        TraceIntProtocolTemplateOverrides overrides,
        CancellationToken cancellationToken = default)
    {
        return protocolTemplateStore.SaveOverridesAsync(overrides, cancellationToken);
    }

    public Task ResetOverridesAsync(CancellationToken cancellationToken = default)
    {
        return protocolTemplateStore.ResetOverridesAsync(cancellationToken);
    }
}
