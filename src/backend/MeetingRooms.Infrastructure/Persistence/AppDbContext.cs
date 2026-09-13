using MeetingRooms.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MeetingRooms.Infrastructure.Persistence;

/// <summary>
/// The application's database context.
/// <para>
/// All three generic arguments are supplied deliberately. <c>IdentityDbContext&lt;AppUser&gt;</c>
/// compiles just as happily and silently gives every Identity table a string primary key, which
/// would reach the slot table phase 5 writes to as an <c>nvarchar(450)</c> foreign key.
/// </para>
/// </summary>
public sealed class AppDbContext : IdentityDbContext<AppUser, IdentityRole<int>, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's own model configuration. Without this call there is no schema at all -
        // the base implementation is what maps AspNetUsers, AspNetRoles and the join tables.
        base.OnModelCreating(modelBuilder);

        // Finds nothing yet: the first IEntityTypeConfiguration arrives with Room and Slot in
        // phase 4. It is here now so that adding one is only ever adding a file, and so that no
        // entity is ever configured by data annotations instead (docs/decisions.md).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
