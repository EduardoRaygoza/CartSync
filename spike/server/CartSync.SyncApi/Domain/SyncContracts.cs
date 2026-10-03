using System.Text.Json;

namespace CartSync.SyncApi.Domain;

public sealed record OperationEnvelope(
    Guid OperationId,
    int OperationVersion,
    Guid InstallationId,
    long DeviceSequence,
    long LastAppliedHouseholdCursor,
    string Kind,
    JsonElement Payload);

public sealed record OperationBatch(Guid HouseholdId, Guid MemberId, IReadOnlyList<OperationEnvelope> Operations);

public sealed record OperationResult(
    Guid OperationId,
    string Outcome,
    long HouseholdCursor,
    Guid? CanonicalId = null,
    SyncIssue? SyncIssue = null);

public sealed record SyncIssue(Guid Id, Guid MemberId, string Reason, string SuggestedAction, JsonElement OriginalOperation);

public sealed record ChangePage(long After, long NextCursor, IReadOnlyList<CanonicalChange> Changes);

public sealed record CanonicalChange(long Cursor, string EntityType, Guid EntityId, string ChangeKind, object Projection);
