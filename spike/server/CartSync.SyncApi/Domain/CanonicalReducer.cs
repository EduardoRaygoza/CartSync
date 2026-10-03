using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CartSync.SyncApi.Domain;

public sealed partial class CanonicalReducer
{
    private static readonly HashSet<string> Units = ["each", "pack", "g", "kg", "oz", "lb", "mL", "L", "fl oz", "gal"];

    public OperationResult Apply(HouseholdState state, Guid memberId, OperationEnvelope operation)
    {
        if (state.Receipts.TryGetValue(operation.OperationId, out var receipt))
            return receipt with { Outcome = "already applied" };

        var stamp = new VersionStamp(operation.LastAppliedHouseholdCursor, operation.DeviceSequence,
            operation.InstallationId, operation.OperationId);

        var result = operation.Kind switch
        {
            "product.create" => CreateProduct(state, operation, stamp),
            "product.note.set" => SetProductNote(state, operation, stamp),
            "product.archive" => ArchiveProduct(state, operation),
            "trip.create" => CreateTrip(state, memberId, operation),
            "trip.entry.add" => AddTripEntry(state, memberId, operation, stamp),
            "trip.entry.field.set" => SetTripEntryField(state, memberId, operation, stamp),
            "trip.entry.remove" => RemoveTripEntry(state, operation, stamp),
            "trip.complete" => CompleteTrip(state, memberId, operation),
            "department.move" => MoveDepartment(state, operation, stamp),
            "sync-issue.resolve" => ResolveIssue(state, memberId, operation),
            _ => NeedsAttention(state, memberId, operation, "unsupported-operation")
        };

        state.Receipts[operation.OperationId] = result;
        return result;
    }

    public static string NormalizeName(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        normalized = Whitespace().Replace(normalized, " ");
        return normalized.ToUpper(CultureInfo.InvariantCulture);
    }

    private static OperationResult CreateProduct(HouseholdState state, OperationEnvelope operation, VersionStamp stamp)
    {
        var id = RequiredGuid(operation.Payload, "productId");
        var name = RequiredString(operation.Payload, "name");
        var normalized = NormalizeName(name);
        var existing = state.Products.Values.FirstOrDefault(p => p.NormalizedName == normalized && !p.Archived);
        if (existing is not null)
        {
            state.ProductAliases[id.ToString()] = existing.Id;
            return Accepted(state, operation, "coalesced", existing.Id, "Product", existing.Id, existing);
        }

        var product = new ProductState { Id = id, Name = name.Trim(), NormalizedName = normalized };
        product.FieldVersions["name"] = stamp;
        state.Products[id] = product;
        return Accepted(state, operation, "accepted", id, "Product", id, product);
    }

    private static OperationResult SetProductNote(HouseholdState state, OperationEnvelope operation, VersionStamp stamp)
    {
        var product = FindProduct(state, RequiredGuid(operation.Payload, "productId"));
        if (product is null || product.Archived) return NeedsAttention(state, Guid.Empty, operation, "product-unavailable");
        if (!Wins(product.FieldVersions, "note", stamp)) return Accepted(state, operation, "coalesced", product.Id, "Product", product.Id, product);
        product.Note = OptionalString(operation.Payload, "note") ?? "";
        product.FieldVersions["note"] = stamp;
        return Accepted(state, operation, "accepted", product.Id, "Product", product.Id, product);
    }

    private static OperationResult ArchiveProduct(HouseholdState state, OperationEnvelope operation)
    {
        var product = FindProduct(state, RequiredGuid(operation.Payload, "productId"));
        if (product is null) return Accepted(state, operation, "already applied", null, "Product", Guid.Empty, new { });
        product.Archived = true;
        return Accepted(state, operation, "accepted", product.Id, "Product", product.Id, product);
    }

    private static OperationResult CreateTrip(HouseholdState state, Guid memberId, OperationEnvelope operation)
    {
        var id = RequiredGuid(operation.Payload, "tripId");
        if (state.Trips.ContainsKey(id)) return Accepted(state, operation, "already applied", id, "Trip", id, state.Trips[id]);
        if (operation.Payload.TryGetProperty("storeArchived", out var archived) && archived.GetBoolean())
            return NeedsAttention(state, memberId, operation, "store-archived");
        var trip = new TripState
        {
            Id = id,
            StoreId = RequiredGuid(operation.Payload, "storeId"),
            Name = RequiredString(operation.Payload, "name")
        };
        state.Trips[id] = trip;
        return Accepted(state, operation, "accepted", id, "Trip", id, trip);
    }

    private static OperationResult AddTripEntry(HouseholdState state, Guid memberId, OperationEnvelope operation, VersionStamp stamp)
    {
        var trip = FindTrip(state, operation);
        if (trip is null || trip.Discarded) return NeedsAttention(state, memberId, operation, "trip-unavailable");
        if (trip.CompletionCursor is not null) return NeedsAttention(state, memberId, operation, "trip-completed");
        var requestedProductId = RequiredGuid(operation.Payload, "productId");
        var product = FindProduct(state, requestedProductId);
        if (product is null) return NeedsAttention(state, memberId, operation, "product-missing");
        if (trip.EntryByProduct.TryGetValue(product.Id, out var existingId))
            return Accepted(state, operation, "coalesced", existingId, "TripEntry", existingId, trip.Entries[existingId]);
        var entryId = RequiredGuid(operation.Payload, "entryId");
        var amount = operation.Payload.TryGetProperty("amount", out var amountNode) ? amountNode.GetDecimal() : 1;
        var unit = OptionalString(operation.Payload, "unit") ?? "each";
        if (amount <= 0 || !Units.Contains(unit)) return NeedsAttention(state, memberId, operation, "invalid-amount-or-unit");
        var entry = new TripEntryState { Id = entryId, ProductId = product.Id, Amount = amount, Unit = unit };
        entry.FieldVersions["amount"] = stamp;
        entry.FieldVersions["unit"] = stamp;
        trip.Entries[entryId] = entry;
        trip.EntryByProduct[product.Id] = entryId;
        return Accepted(state, operation, "accepted", entryId, "TripEntry", entryId, entry);
    }

    private static OperationResult SetTripEntryField(HouseholdState state, Guid memberId, OperationEnvelope operation, VersionStamp stamp)
    {
        var trip = FindTrip(state, operation);
        var entryId = RequiredGuid(operation.Payload, "entryId");
        if (trip is null || !trip.Entries.TryGetValue(entryId, out var entry)) return NeedsAttention(state, memberId, operation, "entry-missing");
        if (entry.Removed) return NeedsAttention(state, memberId, operation, "entry-removed");
        var field = RequiredString(operation.Payload, "field");
        if (!Wins(entry.FieldVersions, field, stamp)) return Accepted(state, operation, "coalesced", entry.Id, "TripEntry", entry.Id, entry);
        switch (field)
        {
            case "acquired": entry.Acquired = operation.Payload.GetProperty("value").GetBoolean(); break;
            case "amount":
                var amount = operation.Payload.GetProperty("value").GetDecimal();
                if (amount <= 0) return NeedsAttention(state, memberId, operation, "invalid-amount");
                entry.Amount = amount;
                break;
            case "unit":
                var unit = operation.Payload.GetProperty("value").GetString()!;
                if (!Units.Contains(unit)) return NeedsAttention(state, memberId, operation, "invalid-unit");
                entry.Unit = unit;
                break;
            case "departmentId": entry.DepartmentId = operation.Payload.GetProperty("value").ValueKind == JsonValueKind.Null ? null : operation.Payload.GetProperty("value").GetGuid(); break;
            default: return NeedsAttention(state, memberId, operation, "unsupported-field");
        }
        entry.FieldVersions[field] = stamp;
        return Accepted(state, operation, "accepted", entry.Id, "TripEntry", entry.Id, entry);
    }

    private static OperationResult RemoveTripEntry(HouseholdState state, OperationEnvelope operation, VersionStamp stamp)
    {
        var trip = FindTrip(state, operation);
        var entryId = RequiredGuid(operation.Payload, "entryId");
        if (trip is null || !trip.Entries.TryGetValue(entryId, out var entry))
            return Accepted(state, operation, "already applied", null, "TripEntry", entryId, new { removed = true });
        entry.Removed = true;
        entry.FieldVersions["removed"] = stamp;
        return Accepted(state, operation, "accepted", entry.Id, "TripEntry", entry.Id, entry);
    }

    private static OperationResult CompleteTrip(HouseholdState state, Guid memberId, OperationEnvelope operation)
    {
        var trip = FindTrip(state, operation);
        if (trip is null) return NeedsAttention(state, memberId, operation, "trip-missing");
        if (trip.CompletionCursor is not null) return NeedsAttention(state, memberId, operation, "trip-already-completed");
        trip.CompletionCursor = state.Cursor + 1;
        return Accepted(state, operation, "accepted", trip.Id, "Trip", trip.Id, trip);
    }

    private static OperationResult MoveDepartment(HouseholdState state, OperationEnvelope operation, VersionStamp stamp)
    {
        var id = RequiredGuid(operation.Payload, "departmentId");
        if (!state.Departments.TryGetValue(id, out var department))
        {
            department = new DepartmentState { Id = id, StoreId = RequiredGuid(operation.Payload, "storeId"), Name = RequiredString(operation.Payload, "name") };
            state.Departments[id] = department;
        }
        if (stamp.CompareTo(department.OrderVersion) <= 0) return Accepted(state, operation, "coalesced", id, "Department", id, department);
        department.BeforeId = OptionalString(operation.Payload, "beforeId");
        department.OrderVersion = stamp;
        return Accepted(state, operation, "accepted", id, "Department", id, department);
    }

    private static OperationResult ResolveIssue(HouseholdState state, Guid memberId, OperationEnvelope operation)
    {
        var id = RequiredGuid(operation.Payload, "syncIssueId");
        if (!state.SyncIssues.TryGetValue(id, out var issue) || issue.MemberId != memberId)
            return NeedsAttention(state, memberId, operation, "sync-issue-unavailable");
        state.SyncIssues.Remove(id);
        return Accepted(state, operation, "accepted", id, "SyncIssue", id, new { resolved = true });
    }

    private static bool Wins(Dictionary<string, VersionStamp> versions, string field, VersionStamp proposed) =>
        !versions.TryGetValue(field, out var current) || proposed.CompareTo(current) > 0;

    private static ProductState? FindProduct(HouseholdState state, Guid id)
    {
        if (state.Products.TryGetValue(id, out var direct)) return direct;
        return state.ProductAliases.TryGetValue(id.ToString(), out var canonical) && state.Products.TryGetValue(canonical, out var aliased) ? aliased : null;
    }

    private static TripState? FindTrip(HouseholdState state, OperationEnvelope operation) =>
        state.Trips.GetValueOrDefault(RequiredGuid(operation.Payload, "tripId"));

    private static OperationResult Accepted(HouseholdState state, OperationEnvelope operation, string outcome, Guid? canonicalId,
        string entityType, Guid entityId, object projection)
    {
        var cursor = ++state.Cursor;
        state.Changes.Add(new CanonicalChange(cursor, entityType, entityId, outcome, projection));
        return new OperationResult(operation.OperationId, outcome, cursor, canonicalId);
    }

    private static OperationResult NeedsAttention(HouseholdState state, Guid memberId, OperationEnvelope operation, string reason)
    {
        var issue = new SyncIssue(Guid.NewGuid(), memberId, reason, Suggest(reason), operation.Payload.Clone());
        state.SyncIssues[issue.Id] = issue;
        return new OperationResult(operation.OperationId, "needs attention", state.Cursor, null, issue);
    }

    private static string Suggest(string reason) => reason switch
    {
        "entry-removed" => "Apply as new or discard",
        "trip-completed" => "Review as a historical correction",
        "store-archived" => "Choose an active Store or discard",
        _ => "Review or discard"
    };

    private static Guid RequiredGuid(JsonElement payload, string name) => payload.GetProperty(name).GetGuid();
    private static string RequiredString(JsonElement payload, string name) => payload.GetProperty(name).GetString() ?? throw new JsonException($"{name} is required");
    private static string? OptionalString(JsonElement payload, string name) => payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
