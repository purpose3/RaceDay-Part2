using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Api.Data;
using RaceDay.Tests.Infrastructure;

namespace RaceDay.Tests;

public class ResultsTests : IClassFixture<RaceDayFactory>
{
    private readonly RaceDayFactory _factory;
    public ResultsTests(RaceDayFactory factory) => _factory = factory;

    private record Setup(TestUser Organiser, TestUser Participant, int EventId, int CategoryId, int EnrolmentId);

    private void MakeEventPast(int eventId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        db.Events.Single(e => e.EventId == eventId).EventDate = DateTime.UtcNow.Date.AddDays(-2);
        db.SaveChanges();
    }

    private async Task<int> EnrolAsync(TestUser participant, int categoryId)
    {
        var res = await participant.Client.PostAsJsonAsync($"/api/categories/{categoryId}/enrollments", new { });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enrolmentId").GetInt32();
    }

    /// <summary>Organiser + event + category + one enrolled participant. Event is moved into the past by default.</summary>
    private async Task<Setup> SetupAsync(bool eventInPast = true)
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var eventId = await Api.CreateEventAsync(org.Client);
        var categoryId = await Api.CreateCategoryAsync(org.Client, eventId);
        var participant = await Api.CreateUserAsync(_factory);
        var enrolmentId = await EnrolAsync(participant, categoryId);
        if (eventInPast) MakeEventPast(eventId);
        return new Setup(org, participant, eventId, categoryId, enrolmentId);
    }

    private static Task<HttpResponseMessage> CaptureAsync(HttpClient client, int enrolmentId,
        string time = "00:48:32", int? overall = 12, int? category = 4) =>
        client.PostAsJsonAsync($"/api/enrollments/{enrolmentId}/result",
            new { finishTime = time, overallPosition = overall, categoryPosition = category });

    // ---------- Capture ----------

    [Fact]
    public async Task Capture_ByEventOwner_Returns201()
    {
        var s = await SetupAsync();

        var res = await CaptureAsync(s.Organiser.Client, s.EnrolmentId);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("00:48:32", json.GetProperty("finishTime").GetString());
        Assert.Equal(12, json.GetProperty("overallPosition").GetInt32());
    }

    [Fact]
    public async Task Capture_BeforeEventDate_Returns400()
    {
        var s = await SetupAsync(eventInPast: false);
        var res = await CaptureAsync(s.Organiser.Client, s.EnrolmentId);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Capture_Twice_Returns409()
    {
        var s = await SetupAsync();
        await CaptureAsync(s.Organiser.Client, s.EnrolmentId);

        var res = await CaptureAsync(s.Organiser.Client, s.EnrolmentId);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Capture_ZeroFinishTime_Returns400()
    {
        var s = await SetupAsync();
        var res = await CaptureAsync(s.Organiser.Client, s.EnrolmentId, time: "00:00:00");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Capture_ByDifferentOrganiser_Returns403()
    {
        var s = await SetupAsync();
        var other = await Api.CreateUserAsync(_factory, "Organiser");

        var res = await CaptureAsync(other.Client, s.EnrolmentId);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Capture_ByParticipant_Returns403()
    {
        var s = await SetupAsync();
        var res = await CaptureAsync(s.Participant.Client, s.EnrolmentId);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Capture_Unauthenticated_Returns401()
    {
        var s = await SetupAsync();
        var res = await CaptureAsync(_factory.CreateClient(), s.EnrolmentId);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Capture_UnknownEnrolment_Returns404()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var res = await CaptureAsync(org.Client, 999999);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ---------- Update ----------

    [Fact]
    public async Task Update_ByOwner_Returns200WithNewValues()
    {
        var s = await SetupAsync();
        var created = await CaptureAsync(s.Organiser.Client, s.EnrolmentId);
        var resultId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resultId").GetInt32();

        var res = await s.Organiser.Client.PutAsJsonAsync($"/api/results/{resultId}",
            new { finishTime = "00:47:10", overallPosition = 9, categoryPosition = 3 });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("00:47:10", json.GetProperty("finishTime").GetString());
        Assert.Equal(3, json.GetProperty("categoryPosition").GetInt32());
    }

    [Fact]
    public async Task Update_ByDifferentOrganiser_Returns403()
    {
        var s = await SetupAsync();
        var other = await Api.CreateUserAsync(_factory, "Organiser");
        var created = await CaptureAsync(s.Organiser.Client, s.EnrolmentId);
        var resultId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resultId").GetInt32();

        var res = await other.Client.PutAsJsonAsync($"/api/results/{resultId}",
            new { finishTime = "00:40:00", overallPosition = 1, categoryPosition = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Update_UnknownResult_Returns404()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var res = await org.Client.PutAsJsonAsync("/api/results/999999",
            new { finishTime = "00:40:00", overallPosition = 1, categoryPosition = 1 });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ---------- Public leaderboard ----------

    [Fact]
    public async Task Leaderboard_IsPublic_OrderedByPosition_AndHidesContactDetails()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var eventId = await Api.CreateEventAsync(org.Client);
        var categoryId = await Api.CreateCategoryAsync(org.Client, eventId);
        var p1 = await Api.CreateUserAsync(_factory);
        var p2 = await Api.CreateUserAsync(_factory);
        var e1 = await EnrolAsync(p1, categoryId);
        var e2 = await EnrolAsync(p2, categoryId);
        MakeEventPast(eventId);

        await CaptureAsync(org.Client, e1, "00:50:00", overall: 2, category: 2);
        await CaptureAsync(org.Client, e2, "00:45:00", overall: 1, category: 1);

        var res = await _factory.CreateClient().GetAsync($"/api/events/{eventId}/results");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetArrayLength());
        Assert.Equal(1, json[0].GetProperty("overallPosition").GetInt32());
        Assert.Equal(2, json[1].GetProperty("overallPosition").GetInt32());
        Assert.True(json[0].TryGetProperty("participantName", out _));
        Assert.False(json[0].TryGetProperty("email", out _));
        Assert.False(json[0].TryGetProperty("phoneNumber", out _));
    }

    [Fact]
    public async Task Leaderboard_UnknownEvent_Returns404()
    {
        var res = await _factory.CreateClient().GetAsync("/api/events/999999/results");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ---------- Participant's own history ----------

    [Fact]
    public async Task MyResults_ReturnsOnlyOwnResults()
    {
        var s = await SetupAsync();
        var otherParticipant = await Api.CreateUserAsync(_factory);
        var otherEnrolment = await EnrolAsync(otherParticipant, s.CategoryId);
        await CaptureAsync(s.Organiser.Client, s.EnrolmentId, "00:48:32", 12, 4);
        await CaptureAsync(s.Organiser.Client, otherEnrolment, "00:55:00", 20, 8);

        var json = await s.Participant.Client.GetFromJsonAsync<JsonElement>("/api/users/me/results");

        Assert.Equal(1, json.GetArrayLength());
        Assert.Equal("00:48:32", json[0].GetProperty("finishTime").GetString());
        Assert.Equal(s.EventId, json[0].GetProperty("eventId").GetInt32());
    }

    [Fact]
    public async Task MyResults_AsOrganiser_Returns403()
    {
        var org = await Api.CreateUserAsync(_factory, "Organiser");
        var res = await org.Client.GetAsync("/api/users/me/results");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task MyResults_Unauthenticated_Returns401()
    {
        var res = await _factory.CreateClient().GetAsync("/api/users/me/results");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
