# RaceDay API (Part 2)

ASP.NET Core Web API for **RaceDay**, an event management system for South African road running, walking and cycling events. Organisers create events, categories and results; participants browse events, enrol and track their performance. The MVC front end is built in Part 3 on top of this API.

## Tech stack

- ASP.NET Core Web API (Visual Studio)
- SQL Server with Entity Framework Core, **Code-First** migrations
- JWT bearer authentication with role-based authorisation (`Organiser`, `Participant`)
- BCrypt password hashing
- Swagger UI (Swashbuckle) with XML documentation
- xUnit + `WebApplicationFactory` tests (in-memory SQLite), GitHub Actions CI

## Project structure

```
RaceDay.Api/
  Controllers/   Auth, Users, Events, Categories, Enrollments, Results
  Data/          Entities and RaceDayDbContext (matches the Part 1 ERD)
  Dtos/          Request/response models with validation
  Services/      TokenService (JWT), WeatherService (Open-Meteo)
  Swagger/       Swagger operation filter
  Program.cs     Service registration and middleware pipeline
RaceDay.Tests/   Unit/integration tests
.github/workflows/ci.yml
```

## Getting started

**Prerequisites:** Visual Studio with the ASP.NET workload, the .NET SDK matching `TargetFramework` in the `.csproj` files (default `net8.0`), and SQL Server / SQL Server Express.

1. Open `RaceDay.sln` in Visual Studio and let it restore NuGet packages.
2. **Connection string:** check `ConnectionStrings:RaceDayDb` in `RaceDay.Api/appsettings.json` (default `Server=.\SQLEXPRESS;Database=RaceDayApiDB;...`). Change the server name if yours differs, e.g. `(localdb)\MSSQLLocalDB`. The default database name is `RaceDayApiDB` so it does not clash with the `RaceDayDB` created by your Part 1 SQL script.
3. **Secrets:** `appsettings.Development.json` holds a development-only JWT key and organiser invite code so the project runs immediately. For anything real, override them with user secrets and never commit them:
   ```
   dotnet user-secrets set "Jwt:Key" "<random string of 32+ characters>"
   dotnet user-secrets set "Registration:OrganiserInviteCode" "<your organiser invite code>"
   ```
4. Set `RaceDay.Api` as the startup project and press **F5**. Swagger opens at `https://localhost:7080/swagger`.
5. **Database:** on first run the database is created automatically from the EF Core model, and `Roles` is seeded with `Organiser` and `Participant`. To use proper Code-First migrations (recommended for submission), open the Package Manager Console with `RaceDay.Api` as the default project and run:
   ```
   Drop-Database
   Add-Migration InitialCreate
   Update-Database
   ```
   (or `dotnet ef migrations add InitialCreate` and `dotnet ef database update`). Once migrations exist, the app applies them on startup.

Development invite code for registering Organisers: `RACEDAY-ORGANISER-2026` (from `appsettings.Development.json`).

### Testing in Swagger

1. `POST /api/auth/register` (Participants need no code; Organisers need `inviteCode`) or `POST /api/auth/login`.
2. Copy the `token` from the response, click **Authorize**, and paste it (no `Bearer ` prefix).
3. Padlock icons mark protected endpoints. Each endpoint documents its purpose, the data it expects and its possible responses.

## Authentication and security design

- **JWT bearer tokens** carry the user id and role as claims. This is how the user's authenticated state and role are maintained across requests (see the note on session management below).
- **Role-based access** is enforced with `[Authorize(Roles = ...)]`. Organiser functionality is unavailable to Participants and vice versa. Public endpoints use `[AllowAnonymous]`; everything else requires a valid token.
- **Ownership checks:** an Organiser can only modify events, categories, enrolment lists and results belonging to their own events. A Participant can only view/cancel their own enrolments and see their own results. Another user's resource returns `403 Forbidden`.
- **Passwords** are stored only as BCrypt hashes. The login endpoint returns the same `401` for an unknown email and a wrong password so accounts cannot be enumerated.
- **Role selection at registration (managed with an invite code):** anyone can register as a Participant, but registering as an Organiser requires the organiser invite code (`Registration:OrganiserInviteCode`), compared in constant time. This prevents users from granting themselves the Organiser role.
- **Profile updates** cannot change email or role.
- The public results leaderboard shows participant names only, never email or phone number.

> **Session management note:** the Part 1 plan specifies JWT. The token (signed, with an expiry) stores the user's identity and role and is sent with every request, which satisfies the requirement to maintain authenticated state and role for all subsequent requests without server-side session storage.

## Endpoints

| # | Method | Route | Role | Purpose |
|---|--------|-------|------|---------|
| 1 | POST | `/api/auth/register` | Public | Register as Participant, or Organiser with invite code |
| 2 | POST | `/api/auth/login` | Public | Log in and receive a JWT |
| 3 | GET | `/api/users/me` | Any logged in | View own profile |
| 4 | PUT | `/api/users/me` | Any logged in | Update own name and phone |
| 5 | PUT | `/api/users/me/password` | Any logged in | Change own password |
| 6 | GET | `/api/events` | Public | List upcoming events (`?location=`, `?date=`) |
| 7 | GET | `/api/events/{eventId}` | Public | Event details with categories |
| 8 | POST | `/api/events` | Organiser | Create an event |
| 9 | PUT | `/api/events/{eventId}` | Organiser (owner) | Update an event |
| 10 | DELETE | `/api/events/{eventId}` | Organiser (owner) | Delete an event (blocked if it has enrolments) |
| 11 | GET | `/api/events/{eventId}/weather` | Public | Forecast for the event's location and date |
| 12 | GET | `/api/events/{eventId}/categories` | Public | List categories |
| 13 | POST | `/api/events/{eventId}/categories` | Organiser (owner) | Add a category |
| 14 | PUT | `/api/categories/{categoryId}` | Organiser (owner) | Update a category |
| 15 | DELETE | `/api/categories/{categoryId}` | Organiser (owner) | Remove a category (blocked if it has enrolments) |
| 16 | POST | `/api/categories/{categoryId}/enrollments` | Participant | Enrol and receive a bib number |
| 17 | GET | `/api/users/me/enrollments` | Participant | List own enrolments |
| 18 | GET | `/api/categories/{categoryId}/enrollments` | Organiser (owner) | List enrolled participants |
| 19 | DELETE | `/api/enrollments/{enrolmentId}` | Participant (owner) | Cancel own enrolment |
| 20 | POST | `/api/enrollments/{enrolmentId}/result` | Organiser (owner) | Capture a finish time and positions |
| 21 | PUT | `/api/results/{resultId}` | Organiser (owner) | Correct a result |
| 22 | GET | `/api/events/{eventId}/results` | Public | Event leaderboard |
| 23 | GET | `/api/users/me/results` | Participant | Own performance history |

### Business rules

- **Enrolment:** one enrolment per participant per category; a full category (`MaxParticipants`) returns `409`; enrolling in an event that has already taken place returns `400`.
- **Bib numbers:** event id (3 digits) + a 4-digit running number for that event, e.g. `0060001`. A unique index guarantees no duplicates.
- **Cancelling** is not allowed once the event has passed or a result exists.
- **Results** can only be captured on or after the event date, once per enrolment (use PUT to correct). Finish time is `hh:mm:ss`, greater than zero and under 24 hours.
- **Event deletion / category deletion** are blocked with `409` when enrolments exist; `MaxParticipants` cannot be lowered below current enrolments.
- **Weather** uses the free Open-Meteo API. Forecasts only exist for roughly the next 16 days; otherwise the endpoint returns `404` with an explanatory message.

## Changes from the Part 1 plan

The API was built to the Part 1 plan. These small changes were needed and the Part 1 documents were updated to match:

1. **Events gained `eventType` (Run/Walk/Cycle) and `distanceKm`**, as required by the Part 2 brief.
2. **`POST /api/auth/register` accepts an optional `inviteCode`**, required for Organisers (see security design).
3. **SQL script fixes:** table creation order corrected (Events before Categories), the `CaategoryID` typo corrected to `CategoryID` to match the ERD, a missing comma fixed, and `Email` widened. `PasswordHash` was widened to hold BCrypt hashes safely.
4. **Unique constraints added:** `Users.Email`, `Enrollments.BibNumber`, `(ParticipantId, CategoryId)` on enrolments and `Results.EnrollmentId` (one result per enrolment).

## Testing

```
dotnet test
```

Tests run the real API in memory using SQLite and a fake weather service, so they need no SQL Server, secrets or internet. They cover success and failure scenarios for:

- registration and login (including invite-code gating, duplicates and password hashing)
- profile and password changes
- event management by Organisers, and ownership enforcement
- role rejection (Participants and other Organisers blocked from restricted endpoints)
- categories, enrolments (recorded correctly, duplicates, full categories, cancelling) and results

## Continuous integration

`.github/workflows/ci.yml` restores, builds and runs all tests on every push and pull request to `main`/`master`.

## References

- Open-Meteo. (n.d.) *Free weather forecast API*. https://open-meteo.com
- API plan and SQL script drafted with assistance from Claude AI (Anthropic, 2026), as declared in Part 1.
