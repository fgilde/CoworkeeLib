namespace Coworkee.Application.Calendar;

/// <summary>Organizer or attendee of an event, written as a mailto address with the name as CN.</summary>
public sealed record IcsPerson(string Email, string? Name = null)
{
    internal string Property(string name) =>
        $"{name}{(string.IsNullOrWhiteSpace(Name) ? "" : $";CN={IcsText.Parameter(Name)}")}:mailto:{Email}";
}
