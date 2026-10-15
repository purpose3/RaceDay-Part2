using System.ComponentModel.DataAnnotations;

namespace RaceDay.Api.Dtos;

public class CategoryRequest
{
    [Required, MaxLength(100)] public string CategoryName { get; set; } = "";
    [Range(0.1, 1000)] public decimal DistanceKm { get; set; }
    [Range(0, 100000)] public decimal EntryFee { get; set; }
    [Range(1, 100000)] public int MaxParticipants { get; set; }
}
