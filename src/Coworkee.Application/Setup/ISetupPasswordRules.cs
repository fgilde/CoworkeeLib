namespace Coworkee.Application.Setup;

/// <summary>The password rules in force, for the setup status before anything is set up; the module that owns them registers it.</summary>
public interface ISetupPasswordRules
{
    Task<Contracts.Configuration.PasswordRules> GetAsync(CancellationToken cancellationToken);
}
