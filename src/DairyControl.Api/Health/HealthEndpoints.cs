using DairyControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DairyControl.Api.Health
{
    public static class HealthEndpoints
    {
        public const string Path = "/health";

        /// <summary>Liveness: the process answers. No database check, so a database outage never makes the platform restart the API.</summary>
        public const string PathLive = "/health/live";

        // The context retries on failure (up to ~30 s per attempt), so without a timeout the probe would hang
        // instead of answering 503 when the database is down.
        public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

        public static IServiceCollection AddDairyControlHealthChecks(this IServiceCollection services)
        {
            services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                foreach (var registration in options.Registrations)
                    registration.Timeout = CheckTimeout;
            });
            return services;
        }

        // 200 when the API and the database are reachable, 503 otherwise. Anonymous so probes need no token.
        // /health/live is the liveness probe: 200 while the process runs, whatever the database does.
        public static IEndpointConventionBuilder MapDairyControlHealth(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapHealthChecks(PathLive, new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
            return endpoints.MapHealthChecks(Path).AllowAnonymous();
        }
    }
}
