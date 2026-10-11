using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
#if (ai)
            // answers of the assistant take longer than the standard timeouts; the provider clients retry on their own
            if (http.Name != "Coworkee.Ai")
            {
                http.AddStandardResilienceHandler();
            }
#else
            http.AddStandardResilienceHandler();
#endif
            http.AddServiceDiscovery();
        });
#if (redis)
        if (builder.Configuration["ConnectionStrings:redis"] is { Length: > 0 })
        {
            // health check and traces for the Redis connection the Coworkee services share
            builder.AddRedisClient("redis");
        }
#endif
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        return app;
    }

    private static void ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation()
                .AddMeter("Microsoft.EntityFrameworkCore", "Npgsql"))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments(HealthEndpointPath) && !ctx.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation());

        // the Aspire dashboard, and the Grafana stack of the app host when it runs one
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.WithLogging(l => l.AddOtlpExporter()).WithMetrics(m => m.AddOtlpExporter()).WithTracing(t => t.AddOtlpExporter());
        }

        if (builder.Configuration["GRAFANA_OTLP_ENDPOINT"] is { Length: > 0 } grafana)
        {
            void Grafana(OtlpExporterOptions o) => (o.Endpoint, o.Protocol, o.Headers) = (new Uri(grafana), OtlpExportProtocol.Grpc, string.Empty);
            telemetry.WithLogging(l => l.AddOtlpExporter("grafana", Grafana)).WithMetrics(m => m.AddOtlpExporter("grafana", Grafana)).WithTracing(t => t.AddOtlpExporter("grafana", Grafana));
        }
    }
}
