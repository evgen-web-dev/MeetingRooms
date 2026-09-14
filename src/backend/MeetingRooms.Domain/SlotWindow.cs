namespace MeetingRooms.Domain;

/// <summary>
/// One generated slot's window, before it becomes a row. Half-open: <c>[StartUtc, EndUtc)</c>,
/// so consecutive windows are contiguous without overlapping.
/// <para>
/// A struct rather than a class because the generator produces a few hundred of these per call
/// and nothing ever mutates or identifies one.
/// </para>
/// </summary>
public readonly record struct SlotWindow(DateTime StartUtc, DateTime EndUtc);
