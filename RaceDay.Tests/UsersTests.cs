using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RaceDay.Tests.Infrastructure;

namespace RaceDay.Tests;

public class UsersTests : IClassFixture<RaceDayFactory>
{
    private readonly RaceDayFactory _factory;
    public UsersTests(RaceDayFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        var res = await _factory.CreateClient().GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("Participant")]
    [InlineData("Organiser")]
    public async Task GetMe_ReturnsOwnProfile_ForBothRoles(string role)
    {
        var user = await Api.CreateUserAsync(_factory, role);

        var res = await user.Client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(user.Email, json.GetProperty("email").GetString());
        Assert.Equal(role, json.GetProperty("role").GetString());
    }

    [Fact]
    public async Task UpdateMe_PersistsChanges()
    {
        var user = await Api.CreateUserAsync(_factory);

        var put = await user.Client.PutAsJsonAsync("/api/users/me",
            new { firstName = "Thabo", lastName = "Mokoena", phoneNumber = "0679224112" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var json = await (await user.Client.GetAsync("/api/users/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Thabo", json.GetProperty("firstName").GetString());
        Assert.Equal("0679224112", json.GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task UpdateMe_CannotChangeRoleOrEmail()
    {
        var user = await Api.CreateUserAsync(_factory);

        await user.Client.PutAsJsonAsync("/api/users/me", new
        {
            firstName = "A", lastName = "B", phoneNumber = "0711234567",
            role = "Organiser", email = "hacker@test.com"
        });

        var json = await (await user.Client.GetAsync("/api/users/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Participant", json.GetProperty("role").GetString());
        Assert.Equal(user.Email, json.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ChangePassword_Succeeds_ThenOldPasswordStopsWorking()
    {
        var user = await Api.CreateUserAsync(_factory);

        var change = await user.Client.PutAsJsonAsync("/api/users/me/password",
            new { currentPassword = user.Password, newPassword = "NewPassw0rd!y" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var anon = _factory.CreateClient();
        var newLogin = await anon.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "NewPassw0rd!y" });
        var oldLogin = await anon.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });

        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Returns400()
    {
        var user = await Api.CreateUserAsync(_factory);

        var res = await user.Client.PutAsJsonAsync("/api/users/me/password",
            new { currentPassword = "NotMyPassword1!", newPassword = "NewPassw0rd!y" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
