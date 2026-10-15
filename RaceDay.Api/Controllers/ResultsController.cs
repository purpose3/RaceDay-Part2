using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api")]
public class ResultsController : ControllerBase
{
    private readonly RaceDayDbContext _db;
    public ResultsController(RaceDayDbContext db) => _db = db;

    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private static ResultResponse ToResponse(Result r) =>
        new(r.ResultId, r.EnrollmentId, r.FinishTime, r.OverallPosition, r.CategoryPosition, r.RecordedAt);

    private static string? ValidateTime(ResultRequest req) =>
        req.FinishTime <= TimeSpan.Zero || req.FinishTime >= TimeSpan.FromHours(24)
            ? "Finish time must be greater than 00:00:00 and less than 24 hours."
            : null;

    /// <summary>Captures the finish time and positions for an enrolment.</summary>
    /// <remarks>Only the Organiser who owns the event can do this, and only on or after the event date. Each enrolment can have one result.</remarks>
    /// <response code="201">The captured result.</response>
    /// <response code="400">Invalid finish time, or the event has not taken place yet.</response>
    /// <response code="403">The event belongs to a different Organiser, or the caller is a Participant.</response>
    /// <response code="404">No enrolment with that id.</response>
    /// <response code="409">A result has already been captured for this enrolment (use PUT to correct it).</response>
    [HttpPost("enrollments/{enrolmentId:int}/result")]
    [Authorize(Roles = "Organiser")]
    [ProducesResponseType(typeof(ResultResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CaptureResult(int enrolmentId, [FromBody] ResultRequest req)
    {
        var timeError = ValidateTime(req);
        if (timeError is not null) return BadRequest(new { error = timeError });

        var enrolment = await _db.Enrollments
            .Include(e => e.Category).ThenInclude(c => c.Event)
            .SingleOrDefaultAsync(e => e.EnrollmentId == enrolmentId);
        if (enrolment is null) return NotFound(new { error = "Enrolment not found." });

        var ev = enrolment.Category.Event;
        if (ev.OrganiserId != CurrentUserId) return Forbid();

        if (ev.EventDate.Date > DateTime.UtcNow.Date)
            return BadRequest(new { error = "Results can only be captured on or after the event date." });

        if (await _db.Results.AnyAsync(r => r.EnrollmentId == enrolmentId))
            return Conflict(new { error = "A result already exists for this enrolment. Use PUT /api/results/{resultId} to correct it." });

        var result = new Result
        {
            EnrollmentId = enrolmentId,
            FinishTime = req.FinishTime,
            OverallPosition = req.OverallPosition,
            CategoryPosition = req.CategoryPosition,
            RecordedAt = DateTime.UtcNow
        };
        _db.Results.Add(result);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index on EnrollmentId: a concurrent request captured it first.
            return Conflict(new { error = "A result already exists for this enrolment." });
        }

        return CreatedAtAction(nameof(GetEventResults), new { eventId = ev.EventId }, ToResponse(result));
    }

    /// <summary>Corrects a previously captured result.</summary>
    /// <response code="200">The updated result.</response>
    /// <response code="400">Invalid finish time or positions.</response>
    /// <response code="403">The event belongs to a different Organiser, or the caller is a Participant.</response>
    /// <response code="404">No result with that id.</response>
    [HttpPut("results/{resultId:int}")]
    [Authorize(Roles = "Organiser")]
    [ProducesResponseType(typeof(ResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateResult(int resultId, [FromBody] ResultRequest req)
    {
        var timeError = ValidateTime(req);
        if (timeError is not null) return BadRequest(new { error = timeError });

        var result = await _db.Results
            .Include(r => r.Enrollment).ThenInclude(e => e.Category).ThenInclude(c => c.Event)
            .SingleOrDefaultAsync(r => r.ResultId == resultId);
        if (result is null) return NotFound(new { error = "Result not found." });
        if (result.Enrollment.Category.Event.OrganiserId != CurrentUserId) return Forbid();

        result.FinishTime = req.FinishTime;
        result.OverallPosition = req.OverallPosition;
        result.CategoryPosition = req.CategoryPosition;
        await _db.SaveChangesAsync();

        return Ok(ToResponse(result));
    }

    /// <summary>Public leaderboard: all results for an event, best overall position first.</summary>
    /// <remarks>Shows participant names only. Results without a position are listed after ranked ones, fastest first.</remarks>
    /// <response code="200">Array of results (may be empty).</response>
    /// <response code="404">No event with that id.</response>
    [HttpGet("events/{eventId:int}/results")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<LeaderboardEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEventResults(int eventId)
    {
        if (!await _db.Events.AnyAsync(e => e.EventId == eventId))
            return NotFound(new { error = "Event not found." });

        var results = await _db.Results.AsNoTracking()
            .Include(r => r.Enrollment).ThenInclude(e => e.Participant)
            .Include(r => r.Enrollment).ThenInclude(e => e.Category)
            .Where(r => r.Enrollment.Category.EventId == eventId)
            .ToListAsync();

        // Sorted in memory: TimeSpan ordering is not reliable across database providers.
        return Ok(results
            .OrderBy(r => r.OverallPosition ?? int.MaxValue)
            .ThenBy(r => r.FinishTime)
            .Select(r => new LeaderboardEntryResponse(
                r.ResultId, r.Enrollment.BibNumber,
                $"{r.Enrollment.Participant.FirstName} {r.Enrollment.Participant.LastName}",
                r.Enrollment.CategoryId, r.Enrollment.Category.CategoryName,
                r.FinishTime, r.OverallPosition, r.CategoryPosition))
            .ToList());
    }

    /// <summary>The logged-in Participant's personal performance history, most recent event first.</summary>
    /// <response code="200">Array of the participant's own results (may be empty).</response>
    [HttpGet("users/me/results")]
    [Authorize(Roles = "Participant")]
    [ProducesResponseType(typeof(List<MyResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyResults()
    {
        var results = await _db.Results.AsNoTracking()
            .Include(r => r.Enrollment).ThenInclude(e => e.Category).ThenInclude(c => c.Event)
            .Where(r => r.Enrollment.ParticipantId == CurrentUserId)
            .ToListAsync();

        return Ok(results
            .OrderByDescending(r => r.Enrollment.Category.Event.EventDate)
            .Select(r =>
            {
                var cat = r.Enrollment.Category;
                return new MyResultResponse(
                    r.ResultId, cat.EventId, cat.Event.EventName, cat.Event.EventDate, cat.Event.Location,
                    cat.CategoryId, cat.CategoryName, r.Enrollment.BibNumber,
                    r.FinishTime, r.OverallPosition, r.CategoryPosition);
            })
            .ToList());
    }
}
