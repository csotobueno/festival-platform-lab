namespace Festival.Modules.Registration.Domain;

public sealed class Registration
{
    public Guid RegistrationId { get; private set; }
    public AttendeeDetails AttendeeDetails { get; private set; } = null!;
    public RegistrationStatus Status { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }

    // EF Core reconstructs persisted state; callers use Create to enforce invariants.
    private Registration() { }

    public static Registration Create(AttendeeDetails attendeeDetails)
    {
        ArgumentNullException.ThrowIfNull(attendeeDetails);

        return new Registration
        {
            RegistrationId = Guid.NewGuid(),
            AttendeeDetails = attendeeDetails,
            Status = RegistrationStatus.Confirmed,
            RegisteredAt = DateTimeOffset.UtcNow
        };
    }
}

public enum RegistrationStatus
{
    Confirmed
}
