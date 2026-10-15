using System.ComponentModel.DataAnnotations;

namespace RaceDay.Api.Dtos;

public class ResultRequest
{
    /// <summary>Finish time as hh:mm:ss, e.g. "00:48:32". Must be greater than zero and under 24 hours.</summary>
    [Required] public TimeSpan FinishTime { get; set; }

    /// <summary>Finishing position across the whole event (optional).</summary>
    [Range(1, 100000)] public int? OverallPosition { get; set; }

    /// <summary>Finishing position within the participant's category (optional).</summary>
    [Range(1, 100000)] public int? CategoryPosition { get; set; }
}

public record ResultResponse(
    int ResultId, int EnrolmentId, TimeSpan FinishTime,
    int? OverallPosition, int? CategoryPosition, DateTime RecordedAt);

/// <summary>Public leaderboard row. Shows the participant's name only, never contact details.</summary>
public record LeaderboardEntryResponse(
    int ResultId, string BibNumber, string ParticipantName, int CategoryId, string CategoryName,
    TimeSpan FinishTime, int? OverallPosition, int? CategoryPosition);

public record MyResultResponse(
    int ResultId, int EventId, string EventName, DateTime EventDate, string Location,
    int CategoryId, string CategoryName, string BibNumber,
    TimeSpan FinishTime, int? OverallPosition, int? CategoryPosition);
