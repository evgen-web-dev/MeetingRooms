using MeetingRooms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MeetingRooms.Infrastructure.EntityTypeConfigurations;

public sealed class SlotEntityTypeConfiguration : IEntityTypeConfiguration<Slot>
{
    /// <summary>
    /// Whole seconds. Grid rows always sit at :00, and the extra precision would only ever store
    /// noise. Note that datetime2(0) <em>rounds</em> rather than truncates, which matters for
    /// BookedAtUtc in phase 5 and not at all for the window columns.
    /// </summary>
    private const string InstantColumnType = "datetime2(0)";

    /// <summary>
    /// Stamps <see cref="DateTimeKind.Utc"/> back onto every instant read from the database.
    /// <para>
    /// SQL Server's <c>datetime2</c> stores no zone, so EF materialises these columns as
    /// <see cref="DateTimeKind.Unspecified"/> - the value is right and the label is missing. That
    /// is invisible in C#, where the two compare equal, and not invisible at all on the wire:
    /// <c>System.Text.Json</c> writes an Unspecified instant with no trailing <c>Z</c>, and a
    /// browser parsing <c>"2026-09-14T05:00:00"</c> reads it as local time and shifts every slot
    /// by the viewer's offset.
    /// </para>
    /// <para>
    /// Applied here rather than at each mapping site so that no read path can forget it, phase
    /// 5's included. The provider-side expression is the identity, so nothing about the stored
    /// value or the generated SQL changes.
    /// </para>
    /// </summary>
    private static readonly ValueConverter<DateTime, DateTime> UtcInstantConverter =
        new(instant => instant, instant => DateTime.SpecifyKind(instant, DateTimeKind.Utc));

    public void Configure(EntityTypeBuilder<Slot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Slots");

        builder.HasKey(slot => slot.Id);

        builder.Property(slot => slot.StartUtc)
            .HasColumnType(InstantColumnType)
            .HasConversion(UtcInstantConverter)
            .IsRequired();

        builder.Property(slot => slot.EndUtc)
            .HasColumnType(InstantColumnType)
            .HasConversion(UtcInstantConverter)
            .IsRequired();

        // A nullable property takes the same non-nullable converter: EF applies it to values and
        // passes null straight through.
        builder.Property(slot => slot.BookedAtUtc)
            .HasColumnType(InstantColumnType)
            .HasConversion(UtcInstantConverter);

        // Deleting a room takes its grid with it. Admins may only delete a room that has no
        // booked slot - refused with RoomHasBookedSlots - so this can never silently erase
        // someone's booking, which would be a cancellation the requirements say does not exist.
        builder.HasOne(slot => slot.Room)
            .WithMany(room => room.Slots)
            .HasForeignKey(slot => slot.RoomId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, because a user who has booked something is referenced by history. There is
        // no delete-user flow, so this is a guard rather than a workflow. Cascade from Rooms and
        // Restrict from AspNetUsers can both target Slots: SQL Server only refuses multiple
        // *cascade* paths into one table.
        builder.HasOne(slot => slot.BookedByUser)
            .WithMany()
            .HasForeignKey(slot => slot.BookedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Integrity for the seeder and the startup top-up, so a re-run cannot produce two rows
        // meaning the same window. It plays NO part in the no-double-booking guarantee - that
        // lives in phase 5's conditional UPDATE, and the two must not be confused. It is also
        // the index the schedule read seeks on: a (RoomId, StartUtc) range, no join.
        builder.HasIndex(slot => new { slot.RoomId, slot.StartUtc })
            .IsUnique();

        // No index declared on BookedByUserId: EF Core creates one for every foreign key, so
        // declaring it would be documentation rather than schema.
    }
}
