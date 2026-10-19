using DairyControl.Api.Health;
using DairyControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DairyControl.Api.Tests
{
    public class HealthEndpointTests
    {
        private static async Task<TestServer> StartAsync(Action<IServiceCollection> configureServices)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication();
            builder.Services.AddAuthorization();
            configureServices(builder.Services);

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapDairyControlHealth();
            await app.StartAsync();

            return app.GetTestServer();
        }

        [Fact]
        public async Task Health_DatabaseNotReachable_Returns503()
        {
            // Arrange: AppDbContext without a configured provider cannot connect, like an unreachable database
            using var server = await StartAsync(services =>
            {
                services.AddDbContext<AppDbContext>(options => { });
                services.AddDairyControlHealthChecks();
            });

            // Act
            var response = await server.CreateClient().GetAsync(HealthEndpoints.Path, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        [Fact]
        public async Task Health_AllChecksHealthy_Returns200WithoutToken()
        {
            // Arrange: the database check is replaced by a healthy one; the endpoint itself is the real one
            using var server = await StartAsync(services =>
            {
                services.AddDbContext<AppDbContext>(options => { });
                services.AddDairyControlHealthChecks();
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    options.Registrations.Clear();
                    options.Registrations.Add(new HealthCheckRegistration(
                        "database", _ => new HealthyCheck(), failureStatus: null, tags: null));
                });
            });

            // Act
            var response = await server.CreateClient().GetAsync(HealthEndpoints.Path, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task HealthLive_DatabaseNotReachable_Returns200WithoutToken()
        {
            // Arrange: the database cannot be reached, as in an outage; liveness must not depend on it
            using var server = await StartAsync(services =>
            {
                services.AddDbContext<AppDbContext>(options => { });
                services.AddDairyControlHealthChecks();
            });

            // Act
            var response = await server.CreateClient().GetAsync(HealthEndpoints.PathLive, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public void AddDairyControlHealthChecks_Registered_DatabaseCheckHasTimeout()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options => { });

            // Act
            services.AddDairyControlHealthChecks();
            var options = services.BuildServiceProvider()
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>().Value;

            // Assert
            var registration = Assert.Single(options.Registrations);
            Assert.Equal("database", registration.Name);
            Assert.Equal(HealthEndpoints.CheckTimeout, registration.Timeout);
        }

        private sealed class HealthyCheck : IHealthCheck
        {
            public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
                => Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}
