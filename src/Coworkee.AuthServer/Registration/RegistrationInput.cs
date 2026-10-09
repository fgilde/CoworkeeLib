namespace Coworkee.AuthServer.Registration;

/// <summary>What the wizard collects; between the steps it travels encrypted in a hidden field.</summary>
public sealed class RegistrationInput
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ConfirmPassword { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Street { get; set; }

    public string? ZipCode { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public List<Guid> RoleIds { get; set; } = [];

    /// <summary>The provider login of an external sign-up being completed; never bound from the form, only restored from the state.</summary>
    public ExternalAccount? External { get; set; }

    public string? ReturnUrl { get; set; }

    /// <summary>Takes the fields of <paramref name="step"/> from what was posted.</summary>
    public void Apply(string step, RegistrationInput posted)
    {
        switch (step)
        {
            case RegistrationSteps.Account:
                (Email, Password, ConfirmPassword) = (posted.Email.Trim(), posted.Password, posted.ConfirmPassword);
                break;
            case RegistrationSteps.Profile:
                (FirstName, LastName, PhoneNumber) = (Clean(posted.FirstName), Clean(posted.LastName), Clean(posted.PhoneNumber));
                (Street, ZipCode, City, Country) = (Clean(posted.Street), Clean(posted.ZipCode), Clean(posted.City), Clean(posted.Country));
                break;
            case RegistrationSteps.Roles:
                RoleIds = [.. posted.RoleIds.Distinct()];
                break;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ExternalAccount(string Provider, string Key, string? DisplayName);
