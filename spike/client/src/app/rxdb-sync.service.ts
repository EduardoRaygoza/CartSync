import { Injectable, signal } from '@angular/core';
import { addRxPlugin, createRxDatabase, RxCollection, RxDatabase, RxDocument, toTypedRxJsonSchema } from 'rxdb';
import { RxDBLeaderElectionPlugin } from 'rxdb/plugins/leader-election';
import { getRxStorageDexie } from 'rxdb/plugins/storage-dexie';
import { InspectorState, OperationEnvelope, SyncStatus, TripEntryProjection } from './sync-types';

addRxPlugin(RxDBLeaderElectionPlugin);

type EntryDocument = TripEntryProjection & { pendingOperations: string[] };
type MetadataDocument = { key: string; value: number };
type Collections = { entries: RxCollection<EntryDocument>; metadata: RxCollection<MetadataDocument> };

const entrySchemaLiteral = {
  title: 'trip entry with atomic pending intent', version: 0, keyCompression: false,
  primaryKey: 'id', type: 'object', additionalProperties: false,
  properties: {
    id: { type: 'string', maxLength: 64 }, tripId: { type: 'string' }, productId: { type: 'string' },
    name: { type: 'string' }, amount: { type: 'number', minimum: 0.001 }, unit: { type: 'string' },
    acquired: { type: 'boolean' }, department: { type: 'string' }, cursor: { type: 'number', minimum: 0, multipleOf: 1 },
    pendingOperations: { type: 'array', items: { type: 'string' } },
  },
  required: ['id', 'tripId', 'productId', 'name', 'amount', 'unit', 'acquired', 'department', 'cursor', 'pendingOperations'],
  indexes: ['tripId', 'department'],
} as const;

const metadataSchemaLiteral = {
  title: 'sync metadata', version: 0, keyCompression: false, primaryKey: 'key', type: 'object', additionalProperties: false,
  properties: { key: { type: 'string', maxLength: 64 }, value: { type: 'number', minimum: 0, multipleOf: 1 } },
  required: ['key', 'value'],
} as const;

@Injectable({ providedIn: 'root' })
export class RxdbSyncService {
  readonly status = signal<SyncStatus>('Saving');
  readonly inspector = signal<InspectorState>({ engine: 'RxDB 17 / Dexie', durable: false, entries: [], pending: [], checkpoint: 0, leaseOwner: null, lastCommitMs: 0, messages: [] });
  private database?: RxDatabase<Collections>;
  private sequence = 0;
  private readonly installationId = localStorage.getItem('cartsync-installation') ?? crypto.randomUUID();

  async initialize(): Promise<void> {
    localStorage.setItem('cartsync-installation', this.installationId);
    try {
      this.database = await createRxDatabase<Collections>({ name: 'cartsync_rxdb_sync_spike', storage: getRxStorageDexie(), multiInstance: true });
      await this.database.addCollections({
        entries: { schema: toTypedRxJsonSchema(entrySchemaLiteral) },
        metadata: { schema: toTypedRxJsonSchema(metadataSchemaLiteral) },
      });
      void navigator.storage?.persist?.();
      this.inspector.update(state => ({ ...state, durable: true }));
      await this.seed();
      this.database.entries.find().$.subscribe(documents => void this.refresh(documents));
      this.database.metadata.findOne('checkpoint').$.subscribe(() => void this.refresh());
      void this.database.waitForLeadership().then(() => this.refresh());
      await this.refresh();
      this.status.set(navigator.onLine ? 'Synced' : 'Offline');
    } catch (error) {
      this.status.set('Online only');
      this.note(`Durable storage unavailable: ${String(error)}`);
    }
    addEventListener('online', () => this.status.set('Synced'));
    addEventListener('offline', () => this.status.set('Offline'));
  }

  async setAcquired(entry: TripEntryProjection, acquired: boolean): Promise<void> {
    if (!this.database) throw new Error('Online-only mode cannot accept offline writes');
    this.status.set('Saving');
    const started = performance.now();
    const operation = this.operation('trip.entry.field.set', { tripId: entry.tripId, entryId: entry.id, field: 'acquired', value: acquired });
    const document = await this.database.entries.findOne(entry.id).exec();
    if (!document) throw new Error(`Entry ${entry.id} is unavailable`);
    await document.incrementalModify(data => ({ ...data, acquired, pendingOperations: [...data.pendingOperations, JSON.stringify(operation)] }));
    this.inspector.update(state => ({ ...state, lastCommitMs: performance.now() - started }));
    await this.refresh();
    this.status.set(navigator.onLine ? 'Saving' : 'Offline');
  }

  async simulateUpload(): Promise<void> {
    if (!this.database) return;
    if (!this.database.isLeader()) { this.note('Another RxDB instance owns replication leadership.'); return; }
    if (!navigator.onLine) { this.status.set('Offline'); return; }
    const pending = this.inspector().pending;
    if (!pending.length) { this.status.set('Synced'); return; }
    if (location.search.includes('liveApi=1')) {
      const response = await fetch('http://localhost:5050/api/v1/sync/operations', {
        method: 'POST', headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ householdId: IDS.household, memberId: IDS.member, operations: pending }),
      });
      if (!response.ok) throw new Error(`Upload failed: ${response.status}`);
    }
    const acknowledged = new Set(pending.map(operation => operation.operationId));
    const documents = await this.database.entries.find().exec();
    await Promise.all(documents.map(document => document.incrementalModify(data => ({
      ...data,
      pendingOperations: data.pendingOperations.filter(serialized => !acknowledged.has((JSON.parse(serialized) as OperationEnvelope).operationId)),
    }))));
    const checkpoint = this.inspector().checkpoint + pending.length;
    await this.database.metadata.upsert({ key: 'checkpoint', value: checkpoint });
    await this.refresh(); this.status.set('Synced');
  }

  private async seed(): Promise<void> {
    if ((await this.database!.entries.count().exec()) === 0) {
      await this.database!.entries.bulkInsert(SAMPLE_ENTRIES.map(entry => ({ ...entry, pendingOperations: [] })));
    }
    await this.database!.metadata.upsert({ key: 'checkpoint', value: 0 });
  }

  private async refresh(existing?: RxDocument<EntryDocument>[]): Promise<void> {
    if (!this.database) return;
    const documents = existing ?? await this.database.entries.find().exec();
    const checkpoint = await this.database.metadata.findOne('checkpoint').exec();
    const entries = documents.map(document => {
      const value = document.toJSON();
      const { pendingOperations: _, ...entry } = value;
      return entry as TripEntryProjection;
    }).sort((a, b) => a.department.localeCompare(b.department) || a.name.localeCompare(b.name));
    const pending = documents.flatMap(document => document.get('pendingOperations').map((serialized: string) => JSON.parse(serialized) as OperationEnvelope));
    this.sequence = Math.max(this.sequence, ...pending.map(operation => operation.deviceSequence), 0);
    this.inspector.update(state => ({ ...state, entries, pending, checkpoint: checkpoint?.get('value') ?? 0, leaseOwner: this.database!.isLeader() ? 'this RxDB instance' : 'another RxDB instance' }));
  }

  private operation(kind: string, payload: Record<string, unknown>): OperationEnvelope {
    return { operationId: crypto.randomUUID(), operationVersion: 1, installationId: this.installationId, deviceSequence: ++this.sequence, lastAppliedHouseholdCursor: this.inspector().checkpoint, kind, payload, createdAt: new Date().toISOString() };
  }

  private note(message: string): void { this.inspector.update(state => ({ ...state, messages: [...state.messages.slice(-4), message] })); }
}

const IDS = { household: '018f0000-0000-7000-8000-000000000001', member: '018f0000-0000-7000-8000-000000000002', trip: '018f0000-0000-7000-8000-000000000003' };
const SAMPLE_ENTRIES: TripEntryProjection[] = [
  { id: '018f0000-0000-7000-8000-000000000011', tripId: IDS.trip, productId: '018f0000-0000-7000-8000-000000000021', name: 'Bananas', amount: 6, unit: 'each', acquired: false, department: 'Produce', cursor: 0 },
  { id: '018f0000-0000-7000-8000-000000000012', tripId: IDS.trip, productId: '018f0000-0000-7000-8000-000000000022', name: 'Milk', amount: 2, unit: 'L', acquired: false, department: 'Dairy', cursor: 0 },
  { id: '018f0000-0000-7000-8000-000000000013', tripId: IDS.trip, productId: '018f0000-0000-7000-8000-000000000023', name: 'Rice', amount: 1, unit: 'pack', acquired: false, department: 'Pantry', cursor: 0 },
];
