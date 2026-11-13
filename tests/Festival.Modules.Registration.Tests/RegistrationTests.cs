using Festival.Modules.Registration.Domain;
using RegistrationAggregate = Festival.Modules.Registration.Domain.Registration;

namespace Festival.Modules.Registration.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void Create_produces_confirmed_registration_with_identity_and_utc_timestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var attendee = new AttendeeDetails(" Jane Doe ", " Jane@EXAMPLE.com ");

        var registration = RegistrationAggregate.Create(attendee);

        Assert.NotEqual(Guid.Empty, registration.RegistrationId);
        Assert.Equal(attendee, registration.AttendeeDetails);
        Assert.Equal(RegistrationStatus.Confirmed, registration.Status);
        Assert.Equal(TimeSpan.Zero, registration.RegisteredAt.Offset);
        Assert.InRange(registration.RegisteredAt, before, DateTimeOffset.UtcNow);
        Assert.NotEqual(registration.RegistrationId, RegistrationAggregate.Create(attendee).RegistrationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    public void Attendee_rejects_missing_name(string? name) =>
        Assert.ThrowsAny<ArgumentException>(() => new AttendeeDetails(name!, "jane@example.com"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("invalid")]
    [InlineData("@example.com")]
    [InlineData("jane@")]
    [InlineData("jane doe@example.com")]
    [InlineData("Jane Doe <jane@example.com>")]
    [InlineData("jane@example.com,other@example.com")]
    public void Attendee_rejects_missing_or_invalid_email(string? email) =>
        Assert.ThrowsAny<ArgumentException>(() => new AttendeeDetails("Jane Doe", email!));

    [Fact]
    public void Attendee_trims_name_and_normalizes_email_with_value_equality()
    {
        var attendee = new AttendeeDetails(" Jane Doe\t", " Jane@EXAMPLE.COM ");

        Assert.Equal("Jane Doe", attendee.Name);
        Assert.Equal("jane@example.com", attendee.Email);
        Assert.Equal(new AttendeeDetails("Jane Doe", "jane@example.com"), attendee);
        Assert.NotEqual(new AttendeeDetails("Other Name", "jane@example.com"), attendee);
    }

    [Fact]
    public void Create_rejects_missing_attendee() =>
        Assert.Throws<ArgumentNullException>(() => RegistrationAggregate.Create(null!));
}
