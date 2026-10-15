using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace RaceDay.Api.Data;

// ---------- Entities (match the Part 1 ERD) ----------

public class Role
{
    public int RoleId { get; set; }
    [Required, MaxLength(25)] public string RoleName { get; set; } = "";
    public ICollection<User> Users { get; set; } = new List<User>();
}

public class User
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    [Required, MaxLength(30)] public string FirstName { get; set; } = "";
    [Required, MaxLength(30)] public string LastName { get; set; } = "";
    [Required, MaxLength(100)] public string Email { get; set; } = "";
    [Required, MaxLength(255)] public string PasswordHash { get; set; } = "";
    [Required, MaxLength(30)] public string PhoneNumber { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Role Role { get; set; } = null!;
    public ICollection<Event> OrganisedEvents { get; set; } = new List<Event>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

public enum EventType { Run, Walk, Cycle }

public class Event
{
    public int EventId { get; set; }
    public int OrganiserId { get; set; }
    [Required, MaxLength(100)] public string EventName { get; set; } = "";
    public DateTime EventDate { get; set; }
    [Required, MaxLength(100)] public string Location { get; set; } = "";
    [Required, MaxLength(300)] public string Description { get; set; } = "";
    [MaxLength(100)] public string? RouteInfo { get; set; }

    // NOT in your Part 1 ERD, but the Part 2 brief says each event must capture
    // distance and event type. Add to your /docs ERD + SQL and commit the update.
    public EventType EventType { get; set; }
    public decimal DistanceKm { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Organiser { get; set; } = null!;
    public ICollection<Category> Categories { get; set; } = new List<Category>();
}

public class Category
{
    public int CategoryId { get; set; }   // Part 1 SQL has a typo: CaategoryID
    public int EventId { get; set; }
    [Required, MaxLength(100)] public string CategoryName { get; set; } = "";
    public decimal DistanceKm { get; set; }
    public decimal EntryFee { get; set; }
    public int MaxParticipants { get; set; }

    public Event Event { get; set; } = null!;
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

public class Enrollment
{
    public int EnrollmentId { get; set; }
    public int ParticipantId { get; set; }
    public int CategoryId { get; set; }
    [Required, MaxLength(10)] public string BibNumber { get; set; } = "";
    public DateTime EnrollmentDate { get; set; } = DateTime.UtcNow;
    [Required, MaxLength(15)] public string Status { get; set; } = "Confirmed";

    public User Participant { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public Result? Result { get; set; }
}

public class Result
{
    public int ResultId { get; set; }
    public int EnrollmentId { get; set; }
    public TimeSpan FinishTime { get; set; }
    public int? OverallPosition { get; set; }
    public int? CategoryPosition { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public Enrollment Enrollment { get; set; } = null!;
}

// ---------- DbContext ----------

public class RaceDayDbContext : DbContext
{
    public RaceDayDbContext(DbContextOptions<RaceDayDbContext> options) : base(options) { }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Result> Results => Set<Result>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Role>().HasData(
            new Role { RoleId = 1, RoleName = "Organiser" },
            new Role { RoleId = 2, RoleName = "Participant" });

        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.CreatedAt).HasDefaultValueSql("GETDATE()");
            e.HasOne(u => u.Role).WithMany(r => r.Users).HasForeignKey(u => u.RoleId);
        });

        b.Entity<Event>(e =>
        {
            e.Property(x => x.EventType).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.DistanceKm).HasPrecision(5, 2);
            e.HasOne(x => x.Organiser).WithMany(u => u.OrganisedEvents)
             .HasForeignKey(x => x.OrganiserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.DistanceKm).HasPrecision(5, 2);
            e.Property(x => x.EntryFee).HasPrecision(8, 2);
            e.HasOne(x => x.Event).WithMany(ev => ev.Categories)
             .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Enrollment>(e =>
        {
            // Prevent double-enrolling in the same category
            e.HasIndex(x => new { x.ParticipantId, x.CategoryId }).IsUnique();
            // Bib numbers embed the event id, so they are unique across the whole table
            e.HasIndex(x => x.BibNumber).IsUnique();
            // Restrict avoids SQL Server "multiple cascade paths" errors
            e.HasOne(x => x.Participant).WithMany(u => u.Enrollments)
             .HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany(c => c.Enrollments)
             .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Result>(e =>
        {
            e.Property(x => x.RecordedAt).HasDefaultValueSql("GETDATE()");
            // ERD shows Enrollment 1:1 Result
            e.HasOne(x => x.Enrollment).WithOne(en => en.Result)
             .HasForeignKey<Result>(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.EnrollmentId).IsUnique();
        });
    }
}
