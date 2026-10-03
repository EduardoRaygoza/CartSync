using CartSync.SyncApi.Domain;

namespace CartSync.SyncApi.Infrastructure;

public sealed class InMemoryHouseholdStore
{
    private readonly Dictionary<Guid, HouseholdState> _households = [];
    private readonly Dictionary<Guid, SemaphoreSlim> _locks = [];
    private readonly object _gate = new();

    public HouseholdState Get(Guid householdId)
    {
        lock (_gate)
        {
            if (!_households.TryGetValue(householdId, out var state)) _households[householdId] = state = new HouseholdState();
            return state;
        }
    }

    public SemaphoreSlim GetLock(Guid householdId)
    {
        lock (_gate)
        {
            if (!_locks.TryGetValue(householdId, out var value)) _locks[householdId] = value = new SemaphoreSlim(1, 1);
            return value;
        }
    }
}
