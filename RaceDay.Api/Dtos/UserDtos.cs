using System.ComponentModel.DataAnnotations;

namespace RaceDay.Api.Dtos;

public record UserProfileResponse(
    int UserId, string FirstName, string LastName, string Email,
    string PhoneNumber, string Role, DateTime CreatedAt);

public class UpdateProfileRequest
{
    [Required, MaxLength(30)] public string FirstName { get; set; } = "";
    [Required, MaxLength(30)] public string LastName { get; set; } = "";
    [Required, Phone, MaxLength(30)] public string PhoneNumber { get; set; } = "";
}

public class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = "";
    [Required, MinLength(8), MaxLength(100)] public string NewPassword { get; set; } = "";
}
