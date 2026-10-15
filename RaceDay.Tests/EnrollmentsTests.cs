using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Api.Data;
using RaceDay.Tests.Infrastructure;

namespace RaceDay.Tests;

public class EnrollmentsTests : IClassFixture<RaceDayFactory>
{
    private readonly RaceDayFactory _factory;
    public EnrollmentsTests(RaceDayFactory factory) => _factory = factory;

    private static Task<HttpResponseMessage> EnrolAsync(HttpClient client, int categoryId) =>
        client.PostAsJsonAsync($"/api/categories/{categoryId}/enrollments", new { });

    /// <summary>Creates an organiser, an event and one category; returns the organiser client and ids.</summary>
    private async Task<(TestUser Organiser, int EventId, int CategoryId)> SetupCategoryAsync()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var eventId = await Api.CreateEventAsync(org.Client);
        var categoryId = await Api.CreateCategoryAsync(org.Client, eventId);
        return (org, eventId, categoryId);
    }

    // ---------- Participants can enrol and it is recorded ----------

    [Fact]
    public async Task Enrol_AsParticipant_Returns201WithBibAndStatus()
    {
        var (_, _, categoryId) = await SetupCategoryAsync();
        var participant = await Api.CreateUserAsync(_factory);

        var res = await EnrolAsync(participant.Client, categoryId);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("bibNumber").GetString()));
        Assert.Equal("Confirmed", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Enrol_RecordsLinkBetweenParticipantEventAndCategory()
    {
        var (_, eventId, categoryId) = await SetupCategoryAsync();
        var participant = await Api.CreateUserAsync(_factory);

        var res = await EnrolAsync(participant.Client, categoryId);
        var enrolmentId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enrolmentId").GetInt32();

        // Verify directly in the database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        var row = db.Enrollments.Single(e => e.EnrollmentId == enrolmentId);
        Assert.Equal(participant.UserId, row.ParticipantId);
        Assert.Equal(categoryId, row.CategoryId);
        Assert.Equal(eventId, db.Categories.Single(c => c.CategoryId == row.CategoryId).EventId);
    }

    [Fact]
    public async Task Enrol_ThenAppearsInMyEnrolments()
    {
        var (_, eventId, categoryId) = await SetupCategoryAsync();
        var participant = await Api.CreateUserAsync(_factory);
        await EnrolAsync(participant.Client, categoryId);

        var json = await participant.Client.GetFromJsonAsync<JsonElement>("/api/users/me/enrollments");

        Assert.Equal(1, json.GetArrayLength());
        Assert.Equal(categoryId, json[0].GetProperty("categoryId").GetInt32());
        Assert.Equal(eventId, json[0].GetProperty("eventId").GetInt32());
    }

    [Fact]
    public async Task Enrol_TwoParticipants_GetDifferentBibNumbers()
    {
        var (_, _, categoryId) = await SetupCategoryAsync();
        var p1 = await Api.CreateUserAsync(_factory);
        var p2 = await Api.CreateUserAsync(_factory);

        var b1 = (await (await EnrolAsync(p1.Client, categoryId)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("bibNumber").GetString();
        var b2 = (await (await EnrolAsync(p2.Client, categoryId)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("bibNumber").GetString();

        Assert.NotEqual(b1, b2);
    }

    // ---------- Expected failures ----------

    [Fact]
    public async Task Enrol_AsOrganiser_Returns403()
    {
        var (org, _, categoryId) = await SetupCategoryAsync();
        var res = await EnrolAsync(org.Client, categoryId);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Enrol_Unauthenticated_Returns401()
    {
        var (_, _, categoryId) = await SetupCategoryAsync();
        var res = await EnrolAsync(_factory.CreateClient(), categoryId);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Enrol_UnknownCategory_Returns404()
    {
        var participant = await Api.CreateUserAsync(_factory);
        var res = await EnrolAsync(participant.Client, 999999);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Enrol_Twice_InSameCategory_Returns409()
    {
        var (_, _, categoryId) = await SetupCategoryAsync();
        var participant = await Api.CreateUserAsync(_factory);
        await EnrolAsync(participant.Client, categoryId);

        var res = await EnrolAsync(participant.Client, categoryId);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Enrol_WhenCategoryFull_Returns409()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var eventId = await Api.CreateEventAsync(org.Client);
        var created = await org.Client.PostAsJsonAsync($"/api/events/{eventId}/categories",
            new { categoryName = "Tiny", distanceKm = 5, entryFee = 10, maxParticipants = 1 });
        var categoryId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("categoryId").GetInt32();

        var first = await Api.CreateUserAsync(_factory);
        var second = await Api.CreateUserAsync(_factory);
        Assert.Equal(HttpStatusCode.Created, (await EnrolAsync(first.Client, categoryId)).StatusCode);

        var res = await EnrolAsync(second.Client, categoryId);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Enrol_InEventThatAlreadyHappened_Returns400()
    {
        var (_, eventId, categoryId) = await SetupCategoryAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
            db.Events.Single(e => e.EventId == eventId).EventDate = DateTime.UtcNow.Date.AddDays(-3);
            db.SaveChanges();
        }
        var participant = await Api.CreateUserAsync(_factory);

        var res = await EnrolAsync(participant.Client, categoryId);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ---------- Organiser view of enrolments ----------

    [Fact]
    public async Task GetCategoryEnrolments_ByOwner_ReturnsParticipantDetails()
    {
        var (org, _, categoryId) = await SetupCategoryAsync();
        var participant = await Api.CreateUserAsync(_factory);
        await EnrolAsync(participant.Client, categoryId);

        var res = await org.Client.GetAsync($"/api/categories/{categoryId}/enrollments");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetArrayLength());
        Assert.Equal(participant.Email, json[0].GetProperty("participant").GetProperty("email").GetString());
    }

    [Fact]
    public async Task GetCategoryEnrolments_ByDifferentOrganiser_Returns403()
    {
        var (_, _, categoryId) = await SetupCategoryAsync();
        var other = await Api.CreateUserAsync(_factory, "Organiser");

        var res = await other.Client.GetAsync($"/api/categories/{categoryId}/enrollments");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task GetCategoryEnrolments_ByParticipant_Returns403()
    {
        var (_, _, categoryId) = await SetupCategoryAsync();
        var participant = await Api.CreateUserAsync(_factory);

        var res = await participant.Client.GetAsync($"/api/categories/{categoryId}/enrollments");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task GetMyEnrolments_AsOrganiser_Returns403()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var res = await org.Client.GetAsync("/api/users/me/enrollments");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // ---------- Cancelling ----------

    [Fact]
    public async Task Cancel_OwnEnrolment_Returns204_AndRemovesIt()
    {
        var (_, _, categoryId) = await SetupCategoryAsync();
        var participant = await Api.CreateUserAsync(_factory);
        var created = await EnrolAsync(participant.Client, categoryId);
        var enrolmentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enrolmentId").GetInt32();

        var del = await participant.Client.DeleteAsync($"/api/enrollments/{enrolmentId}");
        var list = await participant.Client.GetFromJsonAsync<JsonElement>("/api/users/me/enrollments");

        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(0, list.GetArrayLength());
    }

    [Fact]
    public async Task Cancel_SomeoneElsesEnrolment_Returns403()
    {
        var (_, _, categoryId) = await SetupCategoryAsync();
        var owner = await Api.CreateUserAsync(_factory);
        var other = await Api.CreateUserAsync(_factory);
        var created = await EnrolAsync(owner.Client, categoryId);
        var enrolmentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enrolmentId").GetInt32();

        var res = await other.Client.DeleteAsync($"/api/enrollments/{enrolmentId}");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Cancel_AsOrganiser_Returns403()
    {
        var (org, _, categoryId) = await SetupCategoryAsync();
        var participant = await Api.CreateUserAsync(_factory);
        var created = await EnrolAsync(participant.Client, categoryId);
        var enrolmentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enrolmentId").GetInt32();

        var res = await org.Client.DeleteAsync($"/api/enrollments/{enrolmentId}");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Cancel_UnknownEnrolment_Returns404()
    {
        var participant = await Api.CreateUserAsync(_factory);
        var res = await participant.Client.DeleteAsync("/api/enrollments/999999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ---------- Effects on categories ----------

    [Fact]
    public async Task DeleteCategory_WithEnrolments_Returns409()
    {
        var (org, _, categoryId) = await SetupCategoryAsync();
        var participant = await Api.CreateUserAsync(_factory);
        await EnrolAsync(participant.Client, categoryId);

        var res = await org.Client.DeleteAsync($"/api/categories/{categoryId}");

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task UpdateCategory_MaxBelowCurrentEnrolments_Returns400()
    {
        var (org, _, categoryId) = await SetupCategoryAsync();
        var p1 = await Api.CreateUserAsync(_factory);
        var p2 = await Api.CreateUserAsync(_factory);
        await EnrolAsync(p1.Client, categoryId);
        await EnrolAsync(p2.Client, categoryId);

        var res = await org.Client.PutAsJsonAsync($"/api/categories/{categoryId}",
            new { categoryName = "10km Road Race", distanceKm = 10, entryFee = 90, maxParticipants = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
