using Authentication.Api.Security;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class SecurityConfigurationTests
{
    [Fact]
    public void WithoutAnySettingTheDocumentedDefaultsApply()
    {
        using var factory = new AuthenticationApiFactory(liftRateLimits: false);

        var limits = factory.Services.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var lockout = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Lockout;

        Assert.Equal(5, lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), lockout.DefaultLockoutTimeSpan);
        Assert.True(lockout.AllowedForNewUsers);
        Assert.Equal(new RateLimitPolicy(10, 60), limits.Login);
        Assert.Equal(new RateLimitPolicy(30, 60), limits.Refresh);
        Assert.Equal(new RateLimitPolicy(5, 900), limits.ForgotPassword);
        Assert.Equal(new RateLimitPolicy(10, 900), limits.ResetPassword);
        Assert.Equal(new RateLimitPolicy(3, 3600), limits.ForgotPasswordAddress);
    }

    [Fact]
    public void ExternalSettingsOverrideOnlyWhatTheyNameAndTheRestKeepTheirDefaults()
    {
        using var factory = new AuthenticationApiFactory(
            liftRateLimits: false,
            additionalSettings: new Dictionary<string, string>
            {
                ["RateLimiting__Login__PermitLimit"] = "7",
                ["RateLimiting__Login__WindowSeconds"] = "30",
                ["RateLimiting__ForgotPasswordAddress__PermitLimit"] = "2",
                ["Identity__Lockout__MaxFailedAccessAttempts"] = "3",
                ["Identity__Lockout__DefaultLockoutTimeSpan"] = "00:30:00"
            });

        var limits = factory.Services.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var lockout = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Lockout;

        Assert.Equal(new RateLimitPolicy(7, 30), limits.Login);
        Assert.Equal(new RateLimitPolicy(2, 3600), limits.ForgotPasswordAddress);
        Assert.Equal(new RateLimitPolicy(30, 60), limits.Refresh);
        Assert.Equal(new RateLimitPolicy(10, 900), limits.ResetPassword);
        Assert.Equal(3, lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(30), lockout.DefaultLockoutTimeSpan);
    }

    [Theory]
    [InlineData("RateLimiting__Refresh__PermitLimit", "0", "RateLimiting:Refresh:PermitLimit")]
    [InlineData("RateLimiting__ForgotPassword__WindowSeconds", "not-a-number", "RateLimiting:ForgotPassword:WindowSeconds")]
    [InlineData("RateLimiting__ForgotPasswordAddress__PermitLimit", "-5", "RateLimiting:ForgotPasswordAddress:PermitLimit")]
    [InlineData("Identity__Lockout__MaxFailedAccessAttempts", "0", "Identity:Lockout:MaxFailedAccessAttempts")]
    [InlineData("Identity__Lockout__DefaultLockoutTimeSpan", "not-a-span", "Identity:Lockout:DefaultLockoutTimeSpan")]
    [InlineData("Identity__Lockout__DefaultLockoutTimeSpan", "00:00:00", "Identity:Lockout:DefaultLockoutTimeSpan")]
    public void AnInvalidValueStopsStartupNamingOnlyTheSetting(string key, string value, string expectedSetting)
    {
        using var factory = new AuthenticationApiFactory(additionalSettings: new Dictionary<string, string> { [key] = value });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var messages = new List<string>();
        for (var current = failure; current is not null; current = current.InnerException!)
        {
            messages.Add(current.Message);
            if (current.InnerException is null)
            {
                break;
            }
        }

        var message = string.Join(" | ", messages);
        Assert.Contains(expectedSetting, message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, message.Replace(expectedSetting, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
