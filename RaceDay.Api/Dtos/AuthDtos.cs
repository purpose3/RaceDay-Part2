using System.ComponentModel.DataAnnotations;

namespace RaceDay.Api.Dtos;

public class RegisterRequest
{
    [Required, MaxLength(30)] public string FirstName { get; set; } = "";
    [Required, MaxLength(30)] public string LastName { get; set; } = "";
    [Required, EmailAddress, MaxLength(100)] public string Email { get; set; } = "";
    [Required, MinLength(8), MaxLength(100)] public string Password { get; set; } = "";
    [Required, Phone, MaxLength(30)] public string PhoneNumber { get; set; } = "";

    /// <summary>"Organiser" or "Participant".</summary>
    [Required] public string Role { get; set; } = "";

    /// <summary>Required only when registering as an Organiser.</summary>
    public string? InviteCode { get; set; }
}

public class LoginRequest
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required] public string Password { get; set; } = "";
}

public record RegisterResponse(int UserId, string Email, string Role, string Token);
public record LoginResponse(int UserId, string Role, string Token, DateTime ExpiresAt);
