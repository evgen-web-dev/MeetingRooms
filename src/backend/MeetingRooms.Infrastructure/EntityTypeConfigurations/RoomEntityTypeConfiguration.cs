using MeetingRooms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingRooms.Infrastructure.EntityTypeConfigurations;

public sealed class RoomEntityTypeConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Load-bearing rather than cosmetic: there is no DbSet<Room> property on AppDbContext to
        // take a plural name from, so without this the table would be "Room".
        builder.ToTable("Rooms");

        builder.HasKey(room => room.Id);

        builder.Property(room => room.Name)
            .IsRequired()
            .HasMaxLength(Room.NameMaxLength);

        // Capacity needs no configuration: a non-nullable int is NOT NULL by convention, and
        // the 1-1000 bound is a request rule, enforced by the validator where it can be reported
        // against the field that failed.

        // No unique index on Name. docs/requirements.md does not ask for one, and it would add a
        // conflict error code plus failure paths on both create and edit for nothing.
    }
}
