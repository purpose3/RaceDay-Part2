using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api")]
public class CategoriesController : ControllerBase
{
    private readonly RaceDayDbContext _db;
    public CategoriesController(RaceDayDbContext db) => _db = db;

    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private static CategoryResponse ToResponse(Category c) =>
        new(c.CategoryId, c.EventId, c.CategoryName, c.DistanceKm, c.EntryFee, c.MaxParticipants);

    /// <summary>Lists all categories for an event.</summary>
    /// <response code="200">Array of categories (may be empty).</response>
    /// <response code="404">No event with that id.</response>
    [HttpGet("events/{eventId:int}/categories")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<CategoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCategories(int eventId)
    {
        if (!await _db.Events.AnyAsync(e => e.EventId == eventId))
            return NotFound(new { error = "Event not found." });

        var cats = await _db.Categories.AsNoTracking()
            .Where(c => c.EventId == eventId).OrderBy(c => c.DistanceKm).ToListAsync();
        return Ok(cats.Select(ToResponse).ToList());
    }

    /// <summary>Adds a category to an event owned by the logged-in Organiser.</summary>
    /// <response code="201">The created category.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="403">The event belongs to a different Organiser, or the caller is a Participant.</response>
    /// <response code="404">No event with that id.</response>
    [HttpPost("events/{eventId:int}/categories")]
    [Authorize(Roles = "Organiser")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateCategory(int eventId, [FromBody] CategoryRequest req)
    {
        var ev = await _db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.EventId == eventId);
        if (ev is null) return NotFound(new { error = "Event not found." });
        if (ev.OrganiserId != CurrentUserId) return Forbid();

        var cat = new Category
        {
            EventId = eventId,
            CategoryName = req.CategoryName.Trim(),
            DistanceKm = req.DistanceKm,
            EntryFee = req.EntryFee,
            MaxParticipants = req.MaxParticipants
        };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCategories), new { eventId }, ToResponse(cat));
    }

    /// <summary>Updates a category belonging to one of the logged-in Organiser's events.</summary>
    /// <remarks>MaxParticipants cannot be lowered below the number of existing enrolments.</remarks>
    /// <response code="200">The updated category.</response>
    /// <response code="400">Validation failed, or MaxParticipants is below current enrolments.</response>
    /// <response code="403">The category's event belongs to a different Organiser, or the caller is a Participant.</response>
    /// <response code="404">No category with that id.</response>
    [HttpPut("categories/{categoryId:int}")]
    [Authorize(Roles = "Organiser")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCategory(int categoryId, [FromBody] CategoryRequest req)
    {
        var cat = await _db.Categories.Include(c => c.Event).SingleOrDefaultAsync(c => c.CategoryId == categoryId);
        if (cat is null) return NotFound(new { error = "Category not found." });
        if (cat.Event.OrganiserId != CurrentUserId) return Forbid();

        var enrolled = await _db.Enrollments.CountAsync(en => en.CategoryId == categoryId);
        if (req.MaxParticipants < enrolled)
            return BadRequest(new { error = $"MaxParticipants cannot be lower than the {enrolled} existing enrolment(s)." });

        cat.CategoryName = req.CategoryName.Trim();
        cat.DistanceKm = req.DistanceKm;
        cat.EntryFee = req.EntryFee;
        cat.MaxParticipants = req.MaxParticipants;
        await _db.SaveChangesAsync();

        return Ok(ToResponse(cat));
    }

    /// <summary>Removes a category from one of the logged-in Organiser's events.</summary>
    /// <remarks>Categories that already have enrolments cannot be deleted.</remarks>
    /// <response code="204">Category deleted.</response>
    /// <response code="403">The category's event belongs to a different Organiser, or the caller is a Participant.</response>
    /// <response code="404">No category with that id.</response>
    /// <response code="409">The category has enrolments, so it cannot be deleted.</response>
    [HttpDelete("categories/{categoryId:int}")]
    [Authorize(Roles = "Organiser")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(int categoryId)
    {
        var cat = await _db.Categories.Include(c => c.Event).SingleOrDefaultAsync(c => c.CategoryId == categoryId);
        if (cat is null) return NotFound(new { error = "Category not found." });
        if (cat.Event.OrganiserId != CurrentUserId) return Forbid();

        if (await _db.Enrollments.AnyAsync(en => en.CategoryId == categoryId))
            return Conflict(new { error = "This category has enrolments and cannot be deleted." });

        _db.Categories.Remove(cat);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
