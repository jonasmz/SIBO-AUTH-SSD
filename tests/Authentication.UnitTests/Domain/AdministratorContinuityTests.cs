using Authentication.Domain.Administration;
using Xunit;

namespace Authentication.UnitTests.Domain;

public sealed class AdministratorContinuityTests
{
    [Theory]
    [InlineData(false, false, 1, true)]
    [InlineData(false, true, 1, true)]
    [InlineData(false, false, 5, true)]
    [InlineData(true, true, 1, true)]
    [InlineData(true, true, 3, true)]
    [InlineData(true, false, 1, false)]
    [InlineData(true, false, 2, true)]
    [InlineData(true, false, 3, true)]
    public void PermitsOnlyChangesThatKeepAnEnabledAdministrator(
        bool enabledAdministratorNow,
        bool remainsEnabledAdministrator,
        int enabledAdministratorCount,
        bool expected)
    {
        var permitted = AdministratorContinuity.Permits(
            enabledAdministratorNow,
            remainsEnabledAdministrator,
            enabledAdministratorCount);

        Assert.Equal(expected, permitted);
    }
}
