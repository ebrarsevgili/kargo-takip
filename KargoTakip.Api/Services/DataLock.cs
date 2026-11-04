namespace KargoTakip.Api.Services;

/// <summary>
/// One lock for every change that reads data and then writes it back (adding an event, assigning a courier,
/// deleting a courier and releasing their shipments ...). The JSON files live in a single process, so a single
/// static lock is enough and keeps those read-then-write steps from overwriting each other.
/// With a real database this is replaced by transactions.
/// </summary>
internal static class DataLock
{
    public static readonly SemaphoreSlim Gate = new(1, 1);
}
