---
status: accepted
---

# Use Operation-Level Local-First Convergence

Core shopping changes apply locally before server confirmation and converge at operation and field level rather than replacing whole records. Independent fields merge, causally later valid actions win the same field, deterministic metadata breaks true ties, removals and the first Trip completion remain authoritative, and a member's intent is preserved as a personal Sync Issue when it cannot safely converge. This favors uninterrupted collaborative shopping and preservation of intent over routine manual conflict resolution, while leaving the synchronization technology, storage model, transport, and operation envelope to the architecture decision.

## Considered Options

- Whole-record last-write-wins was rejected because unrelated changes would overwrite one another.
- Requiring members to compare every concurrent edit was rejected because routine shopping would become interruptive and fragile offline.

## Consequences

- The selected architecture must durably represent pending changes, causal ordering, deterministic ties, canonical identity collisions, ordered Department moves, and one-way Trip completion.
- Normal synchronization remains automatic; only permanent validation, authorization, removal, archive, or lifecycle conflicts create Sync Issues.
- Sync Issues remain personal and non-blocking while shared Household data continues converging for other members.
