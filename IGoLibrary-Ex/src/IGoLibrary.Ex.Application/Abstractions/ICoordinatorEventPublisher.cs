namespace IGoLibrary.Ex.Application.Abstractions;

public interface ICoordinatorEventPublisher
{
    Task PublishAsync(CoordinatorEvent @event, CancellationToken cancellationToken = default);
}
