using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RaceDay.Api.Data;
using RaceDay.Api.Services;

namespace RaceDay.Tests.Infrastructure;

public class FakeWeatherService : IWeatherService
{
    public WeatherResult? Result { get; set; } =
        new(DateTime.UtcNow.Date, 24.5, "Clear sky", 12.0);

    public Task<WeatherResult?> GetForecastAsync(string location, DateTime date, CancellationToken ct = default)
        => Task.FromResult(Result);
}

public class RaceDayFactory : WebApplicationFactory<Program>
{
    public const string InviteCode = "test-invite-code";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeWeatherService Weather { get; } = new();

    static RaceDayFactory()
    {
        // Program.cs reads these while building, before the factory's own config hooks run,
        // so environment variables are the most reliable way to supply them.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("Jwt__Key", "test-key-test-key-test-key-test-key-12345");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "RaceDay.Api");
        Environment.SetEnvironmentVariable("Jwt__Audience", "RaceDay.Clients");
        Environment.SetEnvironmentVariable("Registration__OrganiserInviteCode", InviteCode);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            _connection.Open();   // in-memory SQLite lives only while this connection is open
            services.AddDbContext<RaceDayDbContext>(o => o.UseSqlite(_connection));

            services.RemoveAll<IWeatherService>();
            services.AddSingleton<IWeatherService>(Weather);

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<RaceDayDbContext>().Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

public record TestUser(HttpClient Client, int UserId, string Email, string Password);

public static class Api
{
    public const string DefaultPassword = "Passw0rd!x";

    /// <summary>Registers a fresh user through the API and returns a client already carrying their JWT.</summary>
    public static async Task<TestUser> CreateUserAsync(RaceDayFactory f, string role = "Participant")
    {
        var email = $"{Guid.NewGuid():N}@test.com";
        var client = f.CreateClient();

        var res = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Test",
            lastName = "User",
            email,
            password = DefaultPassword,
            phoneNumber = "0711234567",
            role,
            inviteCode = role == "Organiser" ? RaceDayFactory.InviteCode : null
        });
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());

        return new TestUser(client, json.GetProperty("userId").GetInt32(), email, DefaultPassword);
    }

    public static async Task<int> CreateEventAsync(HttpClient organiser, string location = "Pretoria, Gauteng")
    {
        var res = await organiser.PostAsJsonAsync("/api/events", new
        {
            eventName = $"Test Event {Guid.NewGuid():N}",
            eventDate = DateTime.UtcNow.Date.AddDays(30),
            location,
            description = "A test event.",
            routeInfo = "Test route",
            eventType = "Run",
            distanceKm = 10
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eventId").GetInt32();
    }

    public static async Task<int> CreateCategoryAsync(HttpClient organiser, int eventId)
    {
        var res = await organiser.PostAsJsonAsync($"/api/events/{eventId}/categories", new
        {
            categoryName = "10km Road Race",
            distanceKm = 10,
            entryFee = 90,
            maxParticipants = 300
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("categoryId").GetInt32();
    }
}
