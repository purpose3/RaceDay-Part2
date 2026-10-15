using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RaceDay.Tests.Infrastructure;

namespace RaceDay.Tests;

public class CategoriesTests : IClassFixture<RaceDayFactory>
{
    private readonly RaceDayFactory _factory;
    public CategoriesTests(RaceDayFactory factory) => _factory = factory;

    private static object CategoryBody(string name = "5km Fun Run") => new
    {
        categoryName = name, distanceKm = 5, entryFee = 50, maxParticipants = 500
    };

    [Fact]
    public async Task GetCategories_Anonymous_ReturnsCategoriesForEvent()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var eventId = await Api.CreateEventAsync(org.Client);
        await Api.CreateCategoryAsync(org.Client, eventId);

        var res = await _factory.CreateClient().GetAsync($"/api/events/{eventId}/categories");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetArrayLength());
    }

    [Fact]
    public async Task GetCategories_UnknownEvent_Returns404()
    {
        var res = await _factory.CreateClient().GetAsync("/api/events/999999/categories");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_ByEventOwner_Returns201()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var eventId = await Api.CreateEventAsync(org.Client);

        var res = await org.Client.PostAsJsonAsync($"/api/events/{eventId}/categories", CategoryBody());

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_ByParticipant_Returns403()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var participant = await Api.CreateUserAsync(_factory, "Participant");
        var eventId = await Api.CreateEventAsync(org.Client);

        var res = await participant.Client.PostAsJsonAsync($"/api/events/{eventId}/categories", CategoryBody());

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_ByDifferentOrganiser_Returns403()
    {
        var owner = await Api.CreateUserAsync(_factory, "Organiser");
        var other = await Api.CreateUserAsync(_factory, "Organiser");
        var eventId = await Api.CreateEventAsync(owner.Client);

        var res = await other.Client.PostAsJsonAsync($"/api/events/{eventId}/categories", CategoryBody());

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_InvalidData_Returns400()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var eventId = await Api.CreateEventAsync(org.Client);

        var res = await org.Client.PostAsJsonAsync($"/api/events/{eventId}/categories",
            new { categoryName = "", distanceKm = -1, entryFee = 50, maxParticipants = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task UpdateCategory_ByOwner_Returns200()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var catId = await Api.CreateCategoryAsync(org.Client, await Api.CreateEventAsync(org.Client));

        var res = await org.Client.PutAsJsonAsync($"/api/categories/{catId}", CategoryBody("Renamed"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed", json.GetProperty("categoryName").GetString());
    }

    [Fact]
    public async Task UpdateCategory_ByDifferentOrganiser_Returns403()
    {
        var owner = await Api.CreateUserAsync(_factory, "Organiser");
        var other = await Api.CreateUserAsync(_factory, "Organiser");
        var catId = await Api.CreateCategoryAsync(owner.Client, await Api.CreateEventAsync(owner.Client));

        var res = await other.Client.PutAsJsonAsync($"/api/categories/{catId}", CategoryBody());

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_ByOwner_Returns204()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var catId = await Api.CreateCategoryAsync(org.Client, await Api.CreateEventAsync(org.Client));

        var res = await org.Client.DeleteAsync($"/api/categories/{catId}");

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_ByParticipant_Returns403()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var participant = await Api.CreateUserAsync(_factory, "Participant");
        var catId = await Api.CreateCategoryAsync(org.Client, await Api.CreateEventAsync(org.Client));

        var res = await participant.Client.DeleteAsync($"/api/categories/{catId}");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_Unknown_Returns404()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var res = await org.Client.DeleteAsync("/api/categories/999999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
