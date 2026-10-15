using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly RaceDayDbContext _db;
    private readonly IWeatherService _weather;

    public EventsController(RaceDayDbContext db, IWeatherService weather)
    {
        _db = db;
        _weather = weather;
    }

    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private static EventSummaryResponse ToSummary(Event e) =>
        new(e.EventId, e.EventName, e.EventDate, e.Location, e.EventType, e.DistanceKm);

    private static EventDetailResponse ToDetail(Event e) =>
        new(e.EventId, e.OrganiserId, e.EventName, e.EventDate, e.Location, e.Description, e.RouteInfo,
            e.EventType, e.DistanceKm, e.CreatedAt,
            e.Categories.Select(c => new CategoryResponse(
                c.CategoryId, c.EventId, c.CategoryName, c.DistanceKm, c.EntryFee, c.MaxParticipants)).ToList());

    /// <summary>Lists upcoming events, optionally filtered by location and/or date.</summary>
    /// <param name="location">Case-insensitive partial match on the event location.</param>
    /// <param name="date">Only events on this exact date (yyyy-MM-dd).</param>
    /// <response code="200">Array of upcoming event summaries, soonest first.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<EventSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvents([FromQuery] string? location, [FromQuery] DateTime? date)
    {
        var query = _db.Events.AsNoTracking().Where(e => e.EventDate >= DateTime.UtcNow.Date);

        if (!string.IsNullOrWhiteSpace(location))
        {
            var loc = location.Trim();
            query = query.Where(e => e.Location.Contains(loc));
        }

        if (date.HasValue)
        {
            var start = date.Value.Date;
            var end = start.AddDays(1);
            query = query.Where(e => e.EventDate >= start && e.EventDate < end);
        }

        var events = await query.OrderBy(e => e.EventDate).ToListAsync();
        return Ok(events.Select(ToSummary).ToList());
    }

    /// <summary>Returns full details for one event, including its categories.</summary>
    /// <response code="200">The event with nested categories.</response>
    /// <response code="404">No event with that id.</response>
    [HttpGet("{eventId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EventDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvent(int eventId)
    {
        var e = await _db.Events.AsNoTracking().Include(x => x.Categories)
            .SingleOrDefaultAsync(x => x.EventId == eventId);
        return e is null ? NotFound(new { error = "Event not found." }) : Ok(ToDetail(e));
    }

    /// <summary>Creates a new event owned by the logged-in Organiser.</summary>
    /// <response code="201">The created event.</response>
    /// <response code="400">Validation failed or the event date is in the past.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">Logged in as a Participant.</response>
    [HttpPost]
    [Authorize(Roles = "Organiser")]
    [ProducesResponseType(typeof(EventDetailResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateEvent([FromBody] EventRequest req)
    {
        if (req.EventDate.Date < DateTime.UtcNow.Date)
            return BadRequest(new { error = "Event date cannot be in the past." });

        var e = new Event
        {
            OrganiserId = CurrentUserId,
            EventName = req.EventName.Trim(),
            EventDate = req.EventDate.Date,
            Location = req.Location.Trim(),
            Description = req.Description.Trim(),
            RouteInfo = req.RouteInfo?.Trim(),
            EventType = req.EventType,
            DistanceKm = req.DistanceKm,
            CreatedAt = DateTime.UtcNow
        };
        _db.Events.Add(e);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetEvent), new { eventId = e.EventId }, ToDetail(e));
    }

    /// <summary>Updates an event owned by the logged-in Organiser.</summary>
    /// <response code="200">The updated event.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="403">The event belongs to a different Organiser, or the caller is a Participant.</response>
    /// <response code="404">No event with that id.</response>
    [HttpPut("{eventId:int}")]
    [Authorize(Roles = "Organiser")]
    [ProducesResponseType(typeof(EventDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateEvent(int eventId, [FromBody] EventRequest req)
    {
        var e = await _db.Events.Include(x => x.Categories).SingleOrDefaultAsync(x => x.EventId == eventId);
        if (e is null) return NotFound(new { error = "Event not found." });
        if (e.OrganiserId != CurrentUserId) return Forbid();

        e.EventName = req.EventName.Trim();
        e.EventDate = req.EventDate.Date;
        e.Location = req.Location.Trim();
        e.Description = req.Description.Trim();
        e.RouteInfo = req.RouteInfo?.Trim();
        e.EventType = req.EventType;
        e.DistanceKm = req.DistanceKm;
        await _db.SaveChangesAsync();

        return Ok(ToDetail(e));
    }

    /// <summary>Deletes an event owned by the logged-in Organiser.</summary>
    /// <remarks>Events that already have enrolments cannot be deleted.</remarks>
    /// <response code="204">Event (and its categories) deleted.</response>
    /// <response code="403">The event belongs to a different Organiser, or the caller is a Participant.</response>
    /// <response code="404">No event with that id.</response>
    /// <response code="409">The event has enrolments, so it cannot be deleted.</response>
    [HttpDelete("{eventId:int}")]
    [Authorize(Roles = "Organiser")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteEvent(int eventId)
    {
        var e = await _db.Events.SingleOrDefaultAsync(x => x.EventId == eventId);
        if (e is null) return NotFound(new { error = "Event not found." });
        if (e.OrganiserId != CurrentUserId) return Forbid();

        // Enrollments -> Categories is Restrict, so block deletes that would orphan enrolments.
        var hasEnrolments = await _db.Enrollments.AnyAsync(en => en.Category.EventId == eventId);
        if (hasEnrolments)
            return Conflict(new { error = "This event has enrolments and cannot be deleted." });

        _db.Events.Remove(e);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Returns the weather forecast for the event's location and date.</summary>
    /// <remarks>Forecasts only exist for roughly the next 16 days.</remarks>
    /// <response code="200">Forecast for the event day.</response>
    /// <response code="404">Event not found, or no forecast is available for its location/date.</response>
    [HttpGet("{eventId:int}/weather")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(WeatherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWeather(int eventId, CancellationToken ct)
    {
        var e = await _db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == eventId, ct);
        if (e is null) return NotFound(new { error = "Event not found." });

        var w = await _weather.GetForecastAsync(e.Location, e.EventDate, ct);
        if (w is null)
            return NotFound(new { error = "No forecast available for this event's location and date yet." });

        return Ok(new WeatherResponse(e.EventId, w.ForecastDate, w.TempC, w.Conditions, w.WindSpeed));
    }
}
