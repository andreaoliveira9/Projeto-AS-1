using Microsoft.AspNetCore.Http.HttpResults;
using CardType = eShop.Ordering.API.Application.Queries.CardType;
using Order = eShop.Ordering.API.Application.Queries.Order;
using System.Diagnostics;
using System.Diagnostics.Metrics;

public static class OrdersApi
{
    private static readonly ActivitySource ActivitySource = new("eShop.Ordering.API");
    private static readonly Meter Meter = new("eShop.Ordering.API");
    private static readonly Counter<long> OrdersCreatedCounter = Meter.CreateCounter<long>("orders.created", "count", "Total number of orders created");
    // CreateOrder endpoint-specific metrics
    private static readonly Counter<long> CreateOrderAttemptsCounter = Meter.CreateCounter<long>("create.order.attempts", "count", "Total number of create order attempts");
    private static readonly Counter<long> CreateOrderSuccessCounter = Meter.CreateCounter<long>("create.order.success", "count", "Total number of create order successes");
    private static readonly Counter<long> CreateOrderFailureCounter = Meter.CreateCounter<long>("create.order.failure", "count", "Total number of create order failures");
    private static readonly Histogram<double> CreateOrderProcessingTime = Meter.CreateHistogram<double>("create.order.processing.time", "milliseconds", "Time taken to process create order command");

    public static RouteGroupBuilder MapOrdersApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/orders").HasApiVersion(1.0);

        api.MapPut("/cancel", CancelOrderAsync);
        api.MapPut("/ship", ShipOrderAsync);
        api.MapGet("{orderId:int}", GetOrderAsync);
        api.MapGet("/", GetOrdersByUserAsync);
        api.MapGet("/cardtypes", GetCardTypesAsync);
        api.MapPost("/draft", CreateOrderDraftAsync);
        api.MapPost("/", CreateOrderAsync);

        return api;
    }

    public static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> CancelOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CancelOrderCommand command,
        [AsParameters] OrderServices services)
    {
        if (requestId == Guid.Empty)
        {
            return TypedResults.BadRequest("Empty GUID is not valid for request ID");
        }

        var requestCancelOrder = new IdentifiedCommand<CancelOrderCommand, bool>(command, requestId);

        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            requestCancelOrder.GetGenericTypeName(),
            nameof(requestCancelOrder.Command.OrderNumber),
            requestCancelOrder.Command.OrderNumber,
            requestCancelOrder);

        var commandResult = await services.Mediator.Send(requestCancelOrder);

        if (!commandResult)
        {
            return TypedResults.Problem(detail: "Cancel order failed to process.", statusCode: 500);
        }

        return TypedResults.Ok();
    }

    public static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> ShipOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        ShipOrderCommand command,
        [AsParameters] OrderServices services)
    {
        if (requestId == Guid.Empty)
        {
            return TypedResults.BadRequest("Empty GUID is not valid for request ID");
        }

        var requestShipOrder = new IdentifiedCommand<ShipOrderCommand, bool>(command, requestId);

        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            requestShipOrder.GetGenericTypeName(),
            nameof(requestShipOrder.Command.OrderNumber),
            requestShipOrder.Command.OrderNumber,
            requestShipOrder);

        var commandResult = await services.Mediator.Send(requestShipOrder);

        if (!commandResult)
        {
            return TypedResults.Problem(detail: "Ship order failed to process.", statusCode: 500);
        }

        return TypedResults.Ok();
    }

    public static async Task<Results<Ok<Order>, NotFound>> GetOrderAsync(int orderId, [AsParameters] OrderServices services)
    {
        try
        {
            var order = await services.Queries.GetOrderAsync(orderId);
            return TypedResults.Ok(order);
        }
        catch
        {
            return TypedResults.NotFound();
        }
    }

    public static async Task<Ok<IEnumerable<OrderSummary>>> GetOrdersByUserAsync([AsParameters] OrderServices services)
    {
        var userId = services.IdentityService.GetUserIdentity();
        var orders = await services.Queries.GetOrdersFromUserAsync(userId);
        return TypedResults.Ok(orders);
    }

    public static async Task<Ok<IEnumerable<CardType>>> GetCardTypesAsync(IOrderQueries orderQueries)
    {
        var cardTypes = await orderQueries.GetCardTypesAsync();
        return TypedResults.Ok(cardTypes);
    }

    public static async Task<OrderDraftDTO> CreateOrderDraftAsync(CreateOrderDraftCommand command, [AsParameters] OrderServices services)
    {
        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.BuyerId),
            command.BuyerId,
            command);

        return await services.Mediator.Send(command);
    }

    public static async Task<Results<Ok, BadRequest<string>>> CreateOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CreateOrderRequest request,
        [AsParameters] OrderServices services)
    {
        // Start an activity for tracing with important tags
        using var activity = ActivitySource.StartActivity("CreateOrderAsync", ActivityKind.Server);
        activity?.SetTag("endpoint", "CreateOrder");
        activity?.SetTag("request.id", requestId);
        activity?.SetTag("user.id", request.UserId);

        // Start timing the processing of this endpoint
        var stopwatch = Stopwatch.StartNew();
        CreateOrderAttemptsCounter.Add(1);
        services.Logger.LogInformation("CreateOrderAsync: Attempting to create order.");

        if (requestId == Guid.Empty)
        {
            stopwatch.Stop();
            CreateOrderFailureCounter.Add(1);
            services.Logger.LogWarning("CreateOrderAsync: RequestId is empty. Aborting.");
            return TypedResults.BadRequest("RequestId is missing.");
        }

        using (services.Logger.BeginScope(new List<KeyValuePair<string, object>>
               { new("IdentifiedCommandId", requestId) }))
        {
            // Mask credit card number to ensure sensitive data is not logged
            var maskedCCNumber = request.CardNumber.Substring(request.CardNumber.Length - 4)
                                   .PadLeft(request.CardNumber.Length, 'X');
            var createOrderCommand = new CreateOrderCommand(
                request.Items,
                request.UserId,
                request.UserName,
                request.City,
                request.Street,
                request.State,
                request.Country,
                request.ZipCode,
                maskedCCNumber,
                request.CardHolderName,
                request.CardExpiration,
                request.CardSecurityNumber,
                request.CardTypeId);

            var requestCreateOrder = new IdentifiedCommand<CreateOrderCommand, bool>(createOrderCommand, requestId);

            // Log non-sensitive command details
            services.Logger.LogInformation("CreateOrderAsync: Sending command {CommandName} with Id {CommandId}",
                requestCreateOrder.GetGenericTypeName(), requestCreateOrder.Id);

            var result = await services.Mediator.Send(requestCreateOrder);
            stopwatch.Stop();
            CreateOrderProcessingTime.Record(stopwatch.ElapsedMilliseconds);

            if (result)
            {
                services.Logger.LogInformation("CreateOrderAsync: Order created successfully. RequestId: {RequestId}", requestId);
                OrdersCreatedCounter.Add(1, new KeyValuePair<string, object>("UserId", request.UserId));
                CreateOrderSuccessCounter.Add(1);
            }
            else
            {
                services.Logger.LogWarning("CreateOrderAsync: Order creation failed. RequestId: {RequestId}", requestId);
                CreateOrderFailureCounter.Add(1);
            }

            // Enrich the activity with processing time
            activity?.SetTag("processing.time.ms", stopwatch.ElapsedMilliseconds);
            return TypedResults.Ok();
        }
    }
}

public record CreateOrderRequest(
    string UserId,
    string UserName,
    string City,
    string Street,
    string State,
    string Country,
    string ZipCode,
    string CardNumber,
    string CardHolderName,
    DateTime CardExpiration,
    string CardSecurityNumber,
    int CardTypeId,
    string Buyer,
    List<BasketItem> Items);
