using Microsoft.AspNetCore.Identity;

namespace Authentication.Infrastructure.Identity;

/// <summary>Identity user with the single added attribute Phase 3 needs: the enabled state.</summary>
public sealed class ApplicationUser : IdentityUser<string>
{
    public bool IsEnabled { get; set; }
}
