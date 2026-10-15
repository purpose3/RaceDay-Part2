using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Organiser,Participant")]
public class UsersController : ControllerBase
{
    private readonly RaceDayDbContext _db;
    public UsersController(RaceDayDbContext db) => _db = db;

    // The user id always comes from the token, never from the request,
    // so a user can only ever see or change their own profile.
    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private Task<User?> LoadCurrentUser() =>
        _db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.UserId == CurrentUserId);

    private static UserProfileResponse ToResponse(User u) =>
        new(u.UserId, u.FirstName, u.LastName, u.Email, u.PhoneNumber, u.Role.RoleName, u.CreatedAt);

    /// <summary>Returns the logged-in user's own profile.</summary>
    /// <response code="200">The profile of the authenticated user.</response>
    /// <response code="401">Missing/invalid token, or the account no longer exists.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMe()
    {
        var user = await LoadCurrentUser();
        return user is null ? Unauthorized() : Ok(ToResponse(user));
    }

    /// <summary>Updates the logged-in user's first name, last name and phone number.</summary>
    /// <remarks>Email and role cannot be changed through this endpoint.</remarks>
    /// <response code="200">The updated profile.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="401">Missing/invalid token, or the account no longer exists.</response>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest req)
    {
        var user = await LoadCurrentUser();
        if (user is null) return Unauthorized();

        user.FirstName = req.FirstName.Trim();
        user.LastName = req.LastName.Trim();
        user.PhoneNumber = req.PhoneNumber.Trim();
        await _db.SaveChangesAsync();

        return Ok(ToResponse(user));
    }

    /// <summary>Changes the logged-in user's password.</summary>
    /// <remarks>Requires the current password. The new password is stored as a BCrypt hash.</remarks>
    /// <response code="204">Password changed.</response>
    /// <response code="400">Current password incorrect, new password too short, or same as the current one.</response>
    /// <response code="401">Missing/invalid token, or the account no longer exists.</response>
    [HttpPut("me/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.UserId == CurrentUserId);
        if (user is null) return Unauthorized();

        // 400 (not 401): the caller IS authenticated, the supplied current password is just wrong.
        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { error = "Current password is incorrect." });

        if (req.CurrentPassword == req.NewPassword)
            return BadRequest(new { error = "New password must be different from the current password." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
