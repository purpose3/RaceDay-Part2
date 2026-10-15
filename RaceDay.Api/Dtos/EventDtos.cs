using System.ComponentModel.DataAnnotations;
using RaceDay.Api.Data;

namespace RaceDay.Api.Dtos;

public class EventRequest
{
    [Required, MaxLength(100)] public string EventName { get; set; } = "";
    [Required] public DateTime EventDate { get; set; }
    [Required, MaxLength(100)] public string Location { get; set; } = "";
    [Required, MaxLength(300)] public string Description { get; set; } = "";
    [MaxLength(100)] public string? RouteInfo { get; set; }

    /// <summary>Run, Walk or Cycle.</summary>
    [Required] public EventType EventType { get; set; }

    /// <summary>Overall event distance in km.</summary>
    [Range(0.1, 1000)] public decimal DistanceKm { get; set; }
}

public record EventSummaryResponse(
    int EventId, string EventName, DateTime EventDate, string Location,
    EventType EventType, decimal DistanceKm);

public record CategoryResponse(
    int CategoryId, int EventId, string CategoryName,
    decimal DistanceKm, decimal EntryFee, int MaxParticipants);

public record EventDetailResponse(
    int EventId, int OrganiserId, string EventName, DateTime EventDate, string Location,
    string Description, string? RouteInfo, EventType EventType, decimal DistanceKm,
    DateTime CreatedAt, List<CategoryResponse> Categories);

public record WeatherResponse(
    int EventId, DateTime ForecastDate, double TempC, string Conditions, double WindSpeed);
