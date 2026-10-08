namespace Authentication.Domain.Administration;

/// <summary>
/// The system must always keep at least one enabled user holding the <c>Administrator</c> role.
/// A user is an enabled administrator when it is enabled and holds that role; temporary lockout
/// does not change this.
/// </summary>
public static class AdministratorContinuity
{
    public static bool Permits(
        bool isEnabledAdministratorNow,
        bool remainsEnabledAdministrator,
        int enabledAdministratorCount)
    {
        return !isEnabledAdministratorNow || remainsEnabledAdministrator || enabledAdministratorCount >= 2;
    }
}
