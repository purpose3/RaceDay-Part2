using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api")]
public class EnrollmentsController : ControllerBase
{
    private readonly RaceDayDbContext _db;
    public EnrollmentsController(RaceDayDbContext db) => _db = db;

    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    /// <summary>Enrols the logged-in Participant into a category and generates a bib number.</summary>
    /// <remarks>
    /// The participant is identified from the token, so no request body is needed.
    /// Bib numbers are the event id (3 digits) followed by a 4-digit running number, e.g. 0060001.
    /// </remarks>
    /// <response code="201">Enrolment recorded; returns the enrolment id, bib number and status.</response>
    /// <response code="400">The event has already taken place.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">Logged in as an Organiser.</response>
    /// <response code="404">No category with that id.</response>
    /// <response code="409">Already enrolled in this category, or the category is full.</response>
    [HttpPost("categories/{categoryId:int}/enrollments")]
    [Authorize(Roles = "Participant")]
    [ProducesResponseType(typeof(EnrolmentCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Enrol(int categoryId)
    {
        var cat = await _db.Categories.AsNoTracking().Include(c => c.Event)
            .SingleOrDefaultAsync(c => c.CategoryId == categoryId);
        if (cat is null) return NotFound(new { error = "Category not found." });

        if (cat.Event.EventDate.Date < DateTime.UtcNow.Date)
            return BadRequest(new { error = "This event has already taken place." });

        if (await _db.Enrollments.AnyAsync(e => e.CategoryId == categoryId && e.ParticipantId == CurrentUserId))
            return Conflict(new { error = "You are already enrolled in this category." });

        var taken = await _db.Enrollments.CountAsync(e => e.CategoryId == categoryId);
        if (taken >= cat.MaxParticipants)
            return Conflict(new { error = "This category is full." });

        var enrolment = new Enrollment
        {
            ParticipantId = CurrentUserId,
            CategoryId = categoryId,
            BibNumber = await NextBibNumberAsync(cat.EventId),
            EnrollmentDate = DateTime.UtcNow,
            Status = "Confirmed"
        };
        _db.Enrollments.Add(enrolment);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index hit: a concurrent request took the same bib or the same enrolment.
            return Conflict(new { error = "Could not complete the enrolment. Please try again." });
        }

        return CreatedAtAction(nameof(GetMyEnrolments), null,
            new EnrolmentCreatedResponse(enrolment.EnrollmentId, enrolment.BibNumber, enrolment.Status));
    }

    /// <summary>Lists every event/category the logged-in Participant has entered.</summary>
    /// <response code="200">Array of the participant's enrolments, soonest event first.</response>
    [HttpGet("users/me/enrollments")]
    [Authorize(Roles = "Participant")]
    [ProducesResponseType(typeof(List<MyEnrolmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyEnrolments()
    {
        var list = await _db.Enrollments.AsNoTracking()
            .Include(e => e.Category).ThenInclude(c => c.Event)
            .Where(e => e.ParticipantId == CurrentUserId)
            .ToListAsync();

        return Ok(list
            .OrderBy(e => e.Category.Event.EventDate)
            .Select(e => new MyEnrolmentResponse(
                e.EnrollmentId, e.CategoryId, e.Category.CategoryName,
                e.Category.EventId, e.Category.Event.EventName, e.Category.Event.EventDate,
                e.Category.Event.Location, e.BibNumber, e.EnrollmentDate, e.Status))
            .ToList());
    }

    /// <summary>Lists all participants enrolled in a category of an event the Organiser owns.</summary>
    /// <response code="200">Array of enrolments with participant details.</response>
    /// <response code="403">The category's event belongs to a different Organiser, or the caller is a Participant.</response>
    /// <response code="404">No category with that id.</response>
    [HttpGet("categories/{categoryId:int}/enrollments")]
    [Authorize(Roles = "Organiser")]
    [ProducesResponseType(typeof(List<CategoryEnrolmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCategoryEnrolments(int categoryId)
    {
        var cat = await _db.Categories.AsNoTracking().Include(c => c.Event)
            .SingleOrDefaultAsync(c => c.CategoryId == categoryId);
        if (cat is null) return NotFound(new { error = "Category not found." });
        if (cat.Event.OrganiserId != CurrentUserId) return Forbid();

        var list = await _db.Enrollments.AsNoTracking().Include(e => e.Participant)
            .Where(e => e.CategoryId == categoryId)
            .OrderBy(e => e.EnrollmentDate)
            .ToListAsync();

        return Ok(list.Select(e => new CategoryEnrolmentResponse(
            e.EnrollmentId, e.CategoryId, e.BibNumber, e.EnrollmentDate, e.Status,
            new ParticipantDetails(e.Participant.UserId, e.Participant.FirstName,
                e.Participant.LastName, e.Participant.Email, e.Participant.PhoneNumber))).ToList());
    }

    /// <summary>Cancels the logged-in Participant's own enrolment.</summary>
    /// <remarks>Enrolments cannot be cancelled once the event has taken place or a result has been captured.</remarks>
    /// <response code="204">Enrolment cancelled.</response>
    /// <response code="403">The enrolment belongs to another participant, or the caller is an Organiser.</response>
    /// <response code="404">No enrolment with that id.</response>
    /// <response code="409">The event has passed or a result already exists.</response>
    [HttpDelete("enrollments/{enrolmentId:int}")]
    [Authorize(Roles = "Participant")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(int enrolmentId)
    {
        var enrolment = await _db.Enrollments
            .Include(e => e.Category).ThenInclude(c => c.Event)
            .SingleOrDefaultAsync(e => e.EnrollmentId == enrolmentId);
        if (enrolment is null) return NotFound(new { error = "Enrolment not found." });
        if (enrolment.ParticipantId != CurrentUserId) return Forbid();

        if (enrolment.Category.Event.EventDate.Date < DateTime.UtcNow.Date ||
            await _db.Results.AnyAsync(r => r.EnrollmentId == enrolmentId))
            return Conflict(new { error = "This enrolment can no longer be cancelled." });

        _db.Enrollments.Remove(enrolment);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string> NextBibNumberAsync(int eventId)
    {
        var prefix = eventId.ToString("D3");
        var existing = await _db.Enrollments
            .Where(e => e.Category.EventId == eventId)
            .Select(e => e.BibNumber)
            .ToListAsync();

        // Highest running number already used for this event (ignores any seed-style bibs like PTA1001)
        var highest = existing
            .Where(b => b.StartsWith(prefix) && b.Length > prefix.Length)
            .Select(b => int.TryParse(b[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return prefix + (highest + 1).ToString("D4");
    }
}
