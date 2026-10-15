using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RaceDay.Tests.Infrastructure;

namespace RaceDay.Tests;

public class EventsTests : IClassFixture<RaceDayFactory>
{
    private readonly RaceDayFactory _factory;
    public EventsTests(RaceDayFactory factory) => _factory = factory;

    private static object EventBody(string name = "Updated Event") => new
    {
        eventName = name,
        eventDate = DateTime.UtcNow.Date.AddDays(45),
        location = "Cape Town, Western Cape",
        description = "Updated description.",
        routeInfo = "Updated route",
        eventType = "Cycle",
        distanceKm = 60
    };

    // ---------- Public access ----------

    [Fact]
    public async Task GetEvents_Anonymous_Returns200()
    {
        var res = await _factory.CreateClient().GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task GetEvents_LocationFilter_ReturnsOnlyMatches()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var inDurban = await Api.CreateEventAsync(org.Client, "Durban, KwaZulu-Natal");
        var inPretoria = await Api.CreateEventAsync(org.Client, "Pretoria, Gauteng");

        var json = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/events?location=Durban");
        var ids = json.EnumerateArray().Select(e => e.GetProperty("eventId").GetInt32()).ToList();

        Assert.Contains(inDurban, ids);
        Assert.DoesNotContain(inPretoria, ids);
    }

    [Fact]
    public async Task GetEvent_Unknown_Returns404()
    {
        var res = await _factory.CreateClient().GetAsync("/api/events/999999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ---------- Create: success and role failures ----------

    [Fact]
    public async Task CreateEvent_AsOrganiser_Returns201()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");

        var res = await org.Client.PostAsJsonAsync("/api/events", EventBody("New Event"));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(org.UserId, json.GetProperty("organiserId").GetInt32());
        Assert.Equal("Cycle", json.GetProperty("eventType").GetString());
    }

    [Fact]
    public async Task CreateEvent_AsParticipant_Returns403()
    {
        var participant = await Api.CreateUserAsync(_factory, "Participant");
        var res = await participant.Client.PostAsJsonAsync("/api/events", EventBody());
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_Unauthenticated_Returns401()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/events", EventBody());
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_InPast_Returns400()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var res = await org.Client.PostAsJsonAsync("/api/events", new
        {
            eventName = "Old", eventDate = DateTime.UtcNow.Date.AddDays(-5), location = "X",
            description = "X", eventType = "Run", distanceKm = 5
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ---------- Update / delete: ownership ----------

    [Fact]
    public async Task UpdateEvent_ByOwner_Returns200()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var id = await Api.CreateEventAsync(org.Client);

        var res = await org.Client.PutAsJsonAsync($"/api/events/{id}", EventBody("Renamed"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed", json.GetProperty("eventName").GetString());
    }

    [Fact]
    public async Task UpdateEvent_ByDifferentOrganiser_Returns403()
    {
        var owner = await Api.CreateUserAsync(_factory, "Organiser");
        var other = await Api.CreateUserAsync(_factory, "Organiser");
        var id = await Api.CreateEventAsync(owner.Client);

        var res = await other.Client.PutAsJsonAsync($"/api/events/{id}", EventBody());

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_ByParticipant_Returns403()
    {
        var owner = await Api.CreateUserAsync(_factory, "Organiser");
        var participant = await Api.CreateUserAsync(_factory, "Participant");
        var id = await Api.CreateEventAsync(owner.Client);

        var res = await participant.Client.PutAsJsonAsync($"/api/events/{id}", EventBody());

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_ByDifferentOrganiser_Returns403()
    {
        var owner = await Api.CreateUserAsync(_factory, "Organiser");
        var other = await Api.CreateUserAsync(_factory, "Organiser");
        var id = await Api.CreateEventAsync(owner.Client);

        var res = await other.Client.DeleteAsync($"/api/events/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_ByOwner_Returns204_ThenEventIsGone()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var id = await Api.CreateEventAsync(org.Client);

        var del = await org.Client.DeleteAsync($"/api/events/{id}");
        var get = await _factory.CreateClient().GetAsync($"/api/events/{id}");

        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ---------- Weather (uses the fake service, no network) ----------

    [Fact]
    public async Task GetWeather_ReturnsForecastFromService()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var id = await Api.CreateEventAsync(org.Client);

        var res = await _factory.CreateClient().GetAsync($"/api/events/{id}/weather");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Clear sky", json.GetProperty("conditions").GetString());
        Assert.Equal(24.5, json.GetProperty("tempC").GetDouble());
    }

    [Fact]
    public async Task GetWeather_UnknownEvent_Returns404()
    {
        var res = await _factory.CreateClient().GetAsync("/api/events/999999/weather");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
