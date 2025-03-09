using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics.Metrics;
using OpenTelemetry.Exporter.Prometheus;

namespace eShop.ServiceDefaults;

public static partial class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.AddBasicServiceDefaults();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Adds the services except for making outgoing HTTP calls.
    /// </summary>
    /// <remarks>
    /// This allows for things like Polly to be trimmed out of the app if it isn't used.
    /// </remarks>
    public static IHostApplicationBuilder AddBasicServiceDefaults(this IHostApplicationBuilder builder)
    {
        // Default health checks assume the event bus and self health checks
        builder.AddDefaultHealthChecks();

        builder.ConfigureOpenTelemetry();

        builder.Services.AddCheckoutTelemetry();

        return builder;
    }

    public static IServiceCollection AddCheckoutTelemetry(this IServiceCollection services)
    {
        // Register checkout process metrics
        services.ConfigureOpenTelemetryMeterProvider(meter =>
        {
            meter.AddMeter("eShop.Checkout.Metrics");
        });
        
        // Register checkout metrics with a single meter
        services.AddSingleton(sp => 
        {
            var meter = new Meter("eShop.Checkout.Metrics", "1.0.0");
            
            // Register counters for the checkout process
            meter.CreateCounter<long>("checkout_initiated_total", 
                description: "Count of checkout processes initiated");
                
            meter.CreateCounter<long>("orders_created_total", 
                description: "Count of orders successfully created");
                
            meter.CreateCounter<long>("payments_processed_total", 
                description: "Count of payments processed");
                
            meter.CreateCounter<long>("payments_succeeded_total", 
                description: "Count of payments that succeeded");
                
            meter.CreateCounter<long>("payments_failed_total", 
                description: "Count of payments that failed");
                
            meter.CreateHistogram<double>("checkout_duration_seconds", 
                description: "Duration of the checkout process in seconds");
                
            return meter;
        });
        
        return services;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Experimental.Microsoft.Extensions.AI")
                    .AddMeter("eShop.Checkout.Metrics");
            })
            .WithTracing(tracing =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    // We want to view all traces in development
                    tracing.SetSampler(new AlwaysOnSampler());
                }

                tracing.AddAspNetCoreInstrumentation()
                    .AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Experimental.Microsoft.Extensions.AI")
                    .AddSource(OpenTelemetryCheckoutExtensions.CheckoutActivitySource.Name)
                    .AddSource(OpenTelemetryCheckoutExtensions.OrderingActivitySource.Name)
                    .AddSource(OpenTelemetryCheckoutExtensions.BasketActivitySource.Name)
                    .AddSource(OpenTelemetryCheckoutExtensions.PaymentActivitySource.Name)
                    .AddSource(OpenTelemetryCheckoutExtensions.CatalogActivitySource.Name);
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var collectorHost = builder.Configuration["OTEL_COLLECTOR_HOST"] ?? "localhost";
        var collectorPortGrpc = builder.Configuration["OTEL_COLLECTOR_PORT_GRPC"] ?? "4317";
        var collectorUri = new Uri($"http://{collectorHost}:{collectorPortGrpc}");

        builder.Services.Configure<OpenTelemetryLoggerOptions>(logging => 
            logging.AddOtlpExporter(options =>
            {
                options.Endpoint = collectorUri;
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            }));

        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
        {
            metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = collectorUri;
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            });
            metrics.AddPrometheusExporter();
        });

        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => 
            tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = collectorUri;
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            }));
        
        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
        }

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Uncomment the following line to enable the Prometheus endpoint (requires the OpenTelemetry.Exporter.Prometheus.AspNetCore package)
        app.MapPrometheusScrapingEndpoint();

        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks("/health");

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}
