using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly RaceDayDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IConfiguration _config;

    public AuthController(RaceDayDbContext db, ITokenService tokens, IConfiguration config)
    {
        _db = db;
        _tokens = tokens;
        _config = config;
    }

    /// <summary>
    /// Registers a new user as a Participant or an Organiser.
    /// Organisers must supply the organiser invite code. Passwords are stored as BCrypt hashes.
    /// </summary>
    /// <response code="201">User created; returns the user id, email, role and a JWT.</response>
    /// <response code="400">Validation failed or the role is not Organiser/Participant.</response>
    /// <response code="403">Organiser registration attempted without a valid invite code.</response>
    /// <response code="409">Email address is already registered.</response>
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var roleName = req.Role?.Trim();
        if (!string.Equals(roleName, "Organiser", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(roleName, "Participant", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Role must be 'Organiser' or 'Participant'." });

        var isOrganiser = string.Equals(roleName, "Organiser", StringComparison.OrdinalIgnoreCase);

        // Organiser accounts are gated by an invite code held in configuration.
        if (isOrganiser && !InviteCodeValid(req.InviteCode))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "A valid organiser invite code is required." });

        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { error = "An account with this email already exists." });

        var role = await _db.Roles.SingleAsync(r => r.RoleName == (isOrganiser ? "Organiser" : "Participant"));

        var user = new User
        {
            RoleId = role.RoleId,
            FirstName = req.FirstName.Trim(),
            LastName = req.LastName.Trim(),
            Email = email,
            PhoneNumber = req.PhoneNumber.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (token, _) = _tokens.CreateToken(user, role.RoleName);
        return StatusCode(StatusCodes.Status201Created,
            new RegisterResponse(user.UserId, user.Email, role.RoleName, token));
    }

    /// <summary>Authenticates a user with email and password and issues a JWT.</summary>
    /// <response code="200">Login succeeded; returns user id, role, token and expiry.</response>
    /// <response code="401">Invalid email or password.</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.Email == email);

        // Same message for unknown email and wrong password so accounts can't be enumerated.
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid email or password." });

        var (token, expiresAt) = _tokens.CreateToken(user, user.Role.RoleName);
        return Ok(new LoginResponse(user.UserId, user.Role.RoleName, token, expiresAt));
    }

    private bool InviteCodeValid(string? supplied)
    {
        var expected = _config["Registration:OrganiserInviteCode"];
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied)) return false;

        // Constant-time comparison
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
    }
}

/* Add to appsettings.json (and use user-secrets for the real value):
  "Registration": { "OrganiserInviteCode": "change-me-organiser-code" }
*/
