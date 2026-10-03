using System.Text.Json;
using CartSync.SyncApi.Domain;
using FsCheck;
using FsCheck.Xunit;

namespace CartSync.SyncApi.Tests;

public sealed class CanonicalReducerTests
{
    private static readonly Guid Member = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid DeviceA = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid DeviceB = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid Store = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid Trip = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid Product = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid Entry = Guid.Parse("60000000-0000-0000-0000-000000000001");

    [Fact]
    public void Duplicate_products_coalesce_and_trip_entries_use_the_canonical_identity()
    {
        var (state, reducer) = Seed();
        var alias = Guid.NewGuid();
        var duplicate = Apply(reducer, state, "product.create", new { productId = alias, name = "  MILK  " }, DeviceB, 1, 0);
        var add = Apply(reducer, state, "trip.entry.add", new { tripId = Trip, entryId = Entry, productId = alias }, DeviceB, 2, 0);

        Assert.Equal("coalesced", duplicate.Outcome);
        Assert.Equal(Product, duplicate.CanonicalId);
        Assert.Equal("accepted", add.Outcome);
        Assert.Equal(Product, state.Trips[Trip].Entries[Entry].ProductId);
    }

    [Fact]
    public void Duplicate_upload_has_one_visible_effect()
    {
        var (state, reducer) = Seed();
        var operation = Op("trip.entry.add", new { tripId = Trip, entryId = Entry, productId = Product }, DeviceA, 2, state.Cursor);
        var first = reducer.Apply(state, Member, operation);
        var second = reducer.Apply(state, Member, operation);

        Assert.Equal("accepted", first.Outcome);
        Assert.Equal("already applied", second.Outcome);
        Assert.Single(state.Trips[Trip].Entries);
    }

    [Fact]
    public void Deterministic_metadata_breaks_a_concurrent_check_uncheck_tie()
    {
        var (left, leftReducer) = SeedWithEntry();
        var (right, rightReducer) = SeedWithEntry();
        var check = Op("trip.entry.field.set", new { tripId = Trip, entryId = Entry, field = "acquired", value = true }, DeviceA, 9, 3);
        var uncheck = Op("trip.entry.field.set", new { tripId = Trip, entryId = Entry, field = "acquired", value = false }, DeviceB, 1, 3);

        leftReducer.Apply(left, Member, check);
        leftReducer.Apply(left, Member, uncheck);
        rightReducer.Apply(right, Member, uncheck);
        rightReducer.Apply(right, Member, check);

        Assert.Equal(left.Trips[Trip].Entries[Entry].Acquired, right.Trips[Trip].Entries[Entry].Acquired);
        Assert.False(left.Trips[Trip].Entries[Entry].Acquired);
    }

    [Fact]
    public void Remove_wins_over_an_unseen_edit_and_preserves_the_edit_as_an_issue()
    {
        var (state, reducer) = SeedWithEntry();
        Apply(reducer, state, "trip.entry.remove", new { tripId = Trip, entryId = Entry }, DeviceA, 4, state.Cursor);
        var edit = Apply(reducer, state, "trip.entry.field.set", new { tripId = Trip, entryId = Entry, field = "amount", value = 2.5m }, DeviceB, 3, 3);

        Assert.Equal("needs attention", edit.Outcome);
        Assert.Equal("entry-removed", edit.SyncIssue?.Reason);
        Assert.True(state.Trips[Trip].Entries[Entry].Removed);
    }

    [Fact]
    public void First_completion_wins_and_later_completion_requires_attention()
    {
        var (state, reducer) = SeedWithEntry();
        var first = Apply(reducer, state, "trip.complete", new { tripId = Trip }, DeviceA, 4, state.Cursor);
        var second = Apply(reducer, state, "trip.complete", new { tripId = Trip }, DeviceB, 4, state.Cursor - 1);

        Assert.Equal("accepted", first.Outcome);
        Assert.Equal("needs attention", second.Outcome);
        Assert.Equal("trip-already-completed", second.SyncIssue?.Reason);
    }

    [Fact]
    public void A_new_trip_for_an_archived_store_requires_attention()
    {
        var state = new HouseholdState();
        var result = Apply(new CanonicalReducer(), state, "trip.create", new { tripId = Trip, storeId = Store, name = "Costco", storeArchived = true }, DeviceA, 1, 0);
        Assert.Equal("needs attention", result.Outcome);
        Assert.Empty(state.Trips);
    }

    [Fact]
    public void Independent_fields_merge()
    {
        var (state, reducer) = SeedWithEntry();
        Apply(reducer, state, "trip.entry.field.set", new { tripId = Trip, entryId = Entry, field = "amount", value = 3m }, DeviceA, 4, 3);
        Apply(reducer, state, "trip.entry.field.set", new { tripId = Trip, entryId = Entry, field = "unit", value = "pack" }, DeviceB, 1, 3);
        Assert.Equal(3m, state.Trips[Trip].Entries[Entry].Amount);
        Assert.Equal("pack", state.Trips[Trip].Entries[Entry].Unit);
    }

    [Property(MaxTest = 100)]
    public bool Name_normalization_is_idempotent(NonNull<string> source)
    {
        var once = CanonicalReducer.NormalizeName(source.Get);
        return CanonicalReducer.NormalizeName(once) == once;
    }

    [Property(MaxTest = 100)]
    public bool Replaying_any_valid_amount_operation_is_idempotent(PositiveInt amount)
    {
        var (state, reducer) = SeedWithEntry();
        var operation = Op("trip.entry.field.set", new { tripId = Trip, entryId = Entry, field = "amount", value = (decimal)amount.Get }, DeviceA, 4, 3);
        var first = reducer.Apply(state, Member, operation);
        var cursor = state.Cursor;
        var second = reducer.Apply(state, Member, operation);
        return first.Outcome == "accepted" && second.Outcome == "already applied" && cursor == state.Cursor;
    }

    private static (HouseholdState, CanonicalReducer) Seed()
    {
        var state = new HouseholdState();
        var reducer = new CanonicalReducer();
        Apply(reducer, state, "product.create", new { productId = Product, name = "Milk" }, DeviceA, 1, 0);
        Apply(reducer, state, "trip.create", new { tripId = Trip, storeId = Store, name = "Costco Saturday" }, DeviceA, 2, 1);
        return (state, reducer);
    }

    private static (HouseholdState, CanonicalReducer) SeedWithEntry()
    {
        var (state, reducer) = Seed();
        Apply(reducer, state, "trip.entry.add", new { tripId = Trip, entryId = Entry, productId = Product }, DeviceA, 3, 2);
        return (state, reducer);
    }

    private static OperationResult Apply(CanonicalReducer reducer, HouseholdState state, string kind, object payload, Guid installation, long sequence, long cursor) =>
        reducer.Apply(state, Member, Op(kind, payload, installation, sequence, cursor));

    private static OperationEnvelope Op(string kind, object payload, Guid installation, long sequence, long cursor)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return new OperationEnvelope(Guid.NewGuid(), 1, installation, sequence, cursor, kind, document.RootElement.Clone());
    }
}
