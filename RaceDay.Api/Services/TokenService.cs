using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RaceDay.Api.Data;

namespace RaceDay.Api.Services;

public class JwtSettings
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "RaceDay.Api";
    public string Audience { get; set; } = "RaceDay.Clients";
    public int ExpiryMinutes { get; set; } = 60;
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(User user, string roleName);
}

public class TokenService : ITokenService
{
    private readonly JwtSettings _jwt;
    public TokenService(JwtSettings jwt) => _jwt = jwt;

    public (string Token, DateTime ExpiresAt) CreateToken(User user, string roleName)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, roleName)   // drives [Authorize(Roles = ...)]
        };

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)),
            SecurityAlgorithms.HmacSha256);

        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpiryMinutes);
        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, expires: expires, signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

/* ---------- appsettings.json (don't commit a real key; use user-secrets) ----------
{
  "ConnectionStrings": {
    "RaceDayDb": "Server=.\\SQLEXPRESS;Database=RaceDayDB;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Jwt": {
    "Key": "replace-with-a-long-random-secret-of-32+chars",
    "Issuer": "RaceDay.Api",
    "Audience": "RaceDay.Clients",
    "ExpiryMinutes": 60
  }
}

Keep the key out of GitHub:  dotnet user-secrets set "Jwt:Key" "<your-long-random-key>"
In GitHub Actions the "Testing" environment needs a Jwt:Key too (set via the test factory).

---------- NuGet packages ----------
Microsoft.EntityFrameworkCore.SqlServer
Microsoft.EntityFrameworkCore.Tools
Microsoft.AspNetCore.Authentication.JwtBearer
Swashbuckle.AspNetCore
BCrypt.Net-Next

---------- .csproj additions ----------
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<NoWarn>$(NoWarn);1591</NoWarn>

---------- first run ----------
dotnet ef migrations add InitialCreate
dotnet ef database update
*/
