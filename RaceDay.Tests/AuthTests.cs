using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Api.Data;
using RaceDay.Tests.Infrastructure;

namespace RaceDay.Tests;

public class AuthTests : IClassFixture<RaceDayFactory>
{
    private readonly RaceDayFactory _factory;
    public AuthTests(RaceDayFactory factory) => _factory = factory;

    private static object RegisterBody(string email, string role, string? invite = null, string password = Api.DefaultPassword) => new
    {
        firstName = "Test", lastName = "User", email, password,
        phoneNumber = "0711234567", role, inviteCode = invite
    };

    private static string NewEmail() => $"{Guid.NewGuid():N}@test.com";

    // ---------- Registration: success ----------

    [Fact]
    public async Task Register_Participant_Returns201WithToken()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail(), "Participant"));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Participant", json.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Register_Organiser_WithValidInviteCode_Returns201()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            RegisterBody(NewEmail(), "Organiser", RaceDayFactory.InviteCode));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Organiser", json.GetProperty("role").GetString());
    }

    // ---------- Registration: failure ----------

    [Fact]
    public async Task Register_Organiser_WithoutInviteCode_Returns403()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail(), "Organiser"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Register_Organiser_WithWrongInviteCode_Returns403()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            RegisterBody(NewEmail(), "Organiser", "wrong-code"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, "Participant"));

        var res = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email.ToUpperInvariant(), "Participant"));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidRole_Returns400()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail(), "Admin"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_Returns400()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            RegisterBody(NewEmail(), "Participant", password: "short"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ---------- Password storage ----------

    [Fact]
    public async Task Register_StoresHashedPassword_NotPlainText()
    {
        var user = await Api.CreateUserAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        var stored = db.Users.Single(u => u.UserId == user.UserId).PasswordHash;

        Assert.NotEqual(user.Password, stored);
        Assert.StartsWith("$2", stored);   // BCrypt hash prefix
    }

    // ---------- Login ----------

    [Fact]
    public async Task Login_WithCorrectCredentials_Returns200WithToken()
    {
        var user = await Api.CreateUserAsync(_factory);

        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = user.Password });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Participant", json.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var user = await Api.CreateUserAsync(_factory);

        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = "WrongPassword1!" });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_Returns401()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = NewEmail(), password = Api.DefaultPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
