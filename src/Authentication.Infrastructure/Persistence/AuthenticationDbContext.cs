using Authentication.Infrastructure.Identity;
using Authentication.Domain.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Infrastructure.Persistence;

public sealed class AuthenticationDbContext(DbContextOptions<AuthenticationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<string>, string>(options)
{
    public DbSet<RenewableSessionFamily> RenewableSessionFamilies => Set<RenewableSessionFamily>();
    public DbSet<RefreshCredential> RefreshCredentials => Set<RefreshCredential>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("EmailIndex")
            .IsUnique();

        builder.Entity<RenewableSessionFamily>(entity =>
        {
            entity.HasKey(family => family.Id);
            entity.HasIndex(family => new { family.UserId, family.RevokedAtUtc });
            entity.HasMany<RefreshCredential>().WithOne().HasForeignKey(credential => credential.FamilyId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<RefreshCredential>(entity =>
        {
            entity.HasKey(credential => credential.Id);
            entity.Property(credential => credential.TokenHash).HasMaxLength(32).IsRequired();
            entity.HasIndex(credential => credential.TokenHash).IsUnique();
            entity.HasIndex(credential => credential.FamilyId);
            entity.HasOne<RefreshCredential>().WithOne().HasForeignKey<RefreshCredential>(credential => credential.ReplacedByTokenId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
