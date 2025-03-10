using eShop.Basket.API.Repositories;
using eShop.Basket.API.IntegrationEvents.EventHandling.Events;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace eShop.Basket.API.IntegrationEvents.EventHandling;

public class OrderStartedIntegrationEventHandler(
    IBasketRepository repository,
    ILogger<OrderStartedIntegrationEventHandler> logger) : IIntegrationEventHandler<OrderStartedIntegrationEvent>
{
    private static readonly ActivitySource ActivitySource = new("eShop.Basket.API");
    private static readonly Meter Meter = new("eShop.Basket.API");
    private static readonly Counter<long> BasketDeleteAttemptsCounter = Meter.CreateCounter<long>("basket.delete.attempts", "count", "Total number of basket delete attempts");
    private static readonly Counter<long> BasketDeleteErrorsCounter = Meter.CreateCounter<long>("basket.delete.errors", "count", "Total number of basket delete errors");
    private static readonly Counter<long> BasketDeleteSuccessCounter = Meter.CreateCounter<long>("basket.delete.success", "count", "Total number of basket delete successes");
    public async Task Handle(OrderStartedIntegrationEvent @event)
    {
        using var activity = ActivitySource.StartActivity("DeleteBasket", ActivityKind.Internal);
        activity?.SetTag("event.id", @event.Id);
        activity?.SetTag("event.userId", @event.UserId);

        BasketDeleteAttemptsCounter.Add(1);

        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);
        
        bool deleted = await repository.DeleteBasketAsync(@event.UserId);
        
        if (deleted)
        {
            activity?.SetTag("basket.delete.status", "success");
            logger.LogInformation("Successfully deleted basket for id: {Id}", @event.UserId);
            BasketDeleteSuccessCounter.Add(1);
        }
        else
        {
            activity?.SetTag("basket.delete.status", "error");
            logger.LogError("Error deleting basket for id: {Id}", @event.UserId);
            BasketDeleteErrorsCounter.Add(1);
        }
    }
}
