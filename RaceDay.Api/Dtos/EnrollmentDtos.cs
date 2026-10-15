namespace RaceDay.Api.Dtos;

public record EnrolmentCreatedResponse(int EnrolmentId, string BibNumber, string Status);

public record MyEnrolmentResponse(
    int EnrolmentId, int CategoryId, string CategoryName,
    int EventId, string EventName, DateTime EventDate, string Location,
    string BibNumber, DateTime EnrolmentDate, string Status);

public record ParticipantDetails(int UserId, string FirstName, string LastName, string Email, string PhoneNumber);

public record CategoryEnrolmentResponse(
    int EnrolmentId, int CategoryId, string BibNumber, DateTime EnrolmentDate, string Status,
    ParticipantDetails Participant);
