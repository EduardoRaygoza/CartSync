export type SyncStatus = 'Offline' | 'Saving' | 'Synced' | 'Needs attention' | 'Online only';

export interface TripEntryProjection {
  id: string; tripId: string; productId: string; name: string; amount: number; unit: string;
  acquired: boolean; department: string; cursor: number;
}

export interface OperationEnvelope {
  operationId: string; operationVersion: 1; installationId: string; deviceSequence: number;
  lastAppliedHouseholdCursor: number; kind: string; payload: Record<string, unknown>; createdAt: string;
}

export interface Lease { key: 'uploader'; owner: string; expiresAt: number; }

export interface InspectorState {
  engine: 'Native IndexedDB' | 'RxDB 17 / Dexie'; durable: boolean; entries: TripEntryProjection[];
  pending: OperationEnvelope[]; checkpoint: number; leaseOwner: string | null;
  lastCommitMs: number; messages: string[];
}
