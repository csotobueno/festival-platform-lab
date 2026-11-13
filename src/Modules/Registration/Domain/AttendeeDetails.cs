using System.Net.Mail;

namespace Festival.Modules.Registration.Domain;

public sealed record AttendeeDetails
{
    public string Name { get; }
    public string Email { get; }

    public AttendeeDetails(string? name, string? email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (normalizedEmail.Any(char.IsWhiteSpace)
            || !MailAddress.TryCreate(normalizedEmail, out var address)
            || address.Address != normalizedEmail)
        {
            throw new ArgumentException("Email must be a valid email address.", nameof(email));
        }

        Name = name.Trim();
        Email = normalizedEmail;
    }
}
