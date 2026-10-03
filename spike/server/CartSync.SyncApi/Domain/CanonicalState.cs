namespace CartSync.SyncApi.Domain;

public sealed class HouseholdState
{
    public long Cursor { get; set; }
    public Dictionary<Guid, ProductState> Products { get; } = [];
    public Dictionary<string, Guid> ProductAliases { get; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, TripState> Trips { get; } = [];
    public Dictionary<Guid, DepartmentState> Departments { get; } = [];
    public Dictionary<Guid, SyncIssue> SyncIssues { get; } = [];
    public Dictionary<Guid, OperationResult> Receipts { get; } = [];
    public List<CanonicalChange> Changes { get; } = [];
}

public sealed class ProductState
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }
    public required string NormalizedName { get; init; }
    public string Note { get; set; } = "";
    public bool Archived { get; set; }
    public Dictionary<string, VersionStamp> FieldVersions { get; } = [];
}

public sealed class TripState
{
    public required Guid Id { get; init; }
    public required Guid StoreId { get; init; }
    public required string Name { get; set; }
    public bool Discarded { get; set; }
    public long? CompletionCursor { get; set; }
    public Dictionary<Guid, TripEntryState> Entries { get; } = [];
    public Dictionary<Guid, Guid> EntryByProduct { get; } = [];
}

public sealed class TripEntryState
{
    public required Guid Id { get; init; }
    public required Guid ProductId { get; init; }
    public decimal Amount { get; set; } = 1;
    public string Unit { get; set; } = "each";
    public Guid? DepartmentId { get; set; }
    public bool Acquired { get; set; }
    public bool Removed { get; set; }
    public Dictionary<string, VersionStamp> FieldVersions { get; } = [];
}

public sealed class DepartmentState
{
    public required Guid Id { get; init; }
    public required Guid StoreId { get; init; }
    public required string Name { get; set; }
    public string? BeforeId { get; set; }
    public bool Removed { get; set; }
    public VersionStamp OrderVersion { get; set; } = VersionStamp.Zero;
}

public readonly record struct VersionStamp(long BaseCursor, long DeviceSequence, Guid InstallationId, Guid OperationId)
    : IComparable<VersionStamp>
{
    public static VersionStamp Zero => new(0, 0, Guid.Empty, Guid.Empty);

    public int CompareTo(VersionStamp other)
    {
        var causal = BaseCursor.CompareTo(other.BaseCursor);
        if (causal != 0) return causal;
        var device = InstallationId.CompareTo(other.InstallationId);
        if (device != 0) return device;
        var sequence = DeviceSequence.CompareTo(other.DeviceSequence);
        return sequence != 0 ? sequence : OperationId.CompareTo(other.OperationId);
    }
}
