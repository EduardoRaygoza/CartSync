import { Injectable, signal } from '@angular/core';
import { InspectorState, Lease, OperationEnvelope, SyncStatus, TripEntryProjection } from './sync-types';

const DB_NAME = 'cartsync-custom-sync-spike';
const DB_VERSION = 1;
const STORES = ['projections', 'outbox', 'metadata', 'aliases', 'syncIssues', 'leases'] as const;

@Injectable({ providedIn: 'root' })
export class NativeSyncService {
  readonly status = signal<SyncStatus>('Saving');
  readonly inspector = signal<InspectorState>({ engine: 'Native IndexedDB', durable: false, entries: [], pending: [], checkpoint: 0, leaseOwner: null, lastCommitMs: 0, messages: [] });
  private db?: IDBDatabase;
  private sequence = 0;
  private readonly tabId = crypto.randomUUID();
  private readonly installationId = localStorage.getItem('cartsync-installation') ?? crypto.randomUUID();
  private readonly channel = typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel('cartsync-sync-spike');

  async initialize(): Promise<void> {
    localStorage.setItem('cartsync-installation', this.installationId);
    this.channel?.addEventListener('message', () => void this.refresh());
    try {
      this.db = await this.openDatabase();
      void navigator.storage?.persist?.();
      this.inspector.update(state => ({ ...state, durable: true }));
      await this.seed();
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
    if (!this.db) throw new Error('Online-only mode cannot accept offline writes');
    this.status.set('Saving');
    const started = performance.now();
    const next = { ...entry, acquired };
    const operation = this.operation('trip.entry.field.set', { tripId: entry.tripId, entryId: entry.id, field: 'acquired', value: acquired });
    const transaction = this.db.transaction(['projections', 'outbox'], 'readwrite', { durability: 'strict' });
    transaction.objectStore('projections').put(next);
    transaction.objectStore('outbox').put(operation);
    await complete(transaction);
    const elapsed = performance.now() - started;
    this.inspector.update(state => ({ ...state, lastCommitMs: elapsed }));
    this.channel?.postMessage({ kind: 'projection-committed', id: entry.id });
    await this.refresh();
    this.status.set(navigator.onLine ? 'Saving' : 'Offline');
  }

  async acquireUploaderLease(ttlMs = 5_000): Promise<boolean> {
    if (!this.db) return false;
    const transaction = this.db.transaction('leases', 'readwrite');
    const store = transaction.objectStore('leases');
    const current = await request<Lease | undefined>(store.get('uploader'));
    const now = Date.now();
    if (current && current.expiresAt > now && current.owner !== this.tabId) {
      await complete(transaction); await this.refresh(); return false;
    }
    store.put({ key: 'uploader', owner: this.tabId, expiresAt: now + ttlMs } satisfies Lease);
    await complete(transaction); await this.refresh(); return true;
  }

  async simulateUpload(): Promise<void> {
    if (!(await this.acquireUploaderLease())) { this.note('Another tab owns the uploader lease.'); return; }
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
    const transaction = this.db!.transaction(['outbox', 'metadata'], 'readwrite');
    for (const operation of pending) transaction.objectStore('outbox').delete(operation.operationId);
    const checkpoint = this.inspector().checkpoint + pending.length;
    transaction.objectStore('metadata').put({ key: 'checkpoint', value: checkpoint });
    await complete(transaction);
    this.channel?.postMessage({ kind: 'upload-complete', checkpoint });
    await this.refresh(); this.status.set('Synced');
  }

  private async seed(): Promise<void> {
    const transaction = this.db!.transaction('projections', 'readwrite');
    const store = transaction.objectStore('projections');
    if ((await request<number>(store.count())) === 0) for (const entry of SAMPLE_ENTRIES) store.put(entry);
    await complete(transaction);
  }

  private async refresh(): Promise<void> {
    if (!this.db) return;
    const transaction = this.db.transaction(['projections', 'outbox', 'metadata', 'leases'], 'readonly');
    const [entries, pending, checkpointRecord, lease] = await Promise.all([
      request<TripEntryProjection[]>(transaction.objectStore('projections').getAll()),
      request<OperationEnvelope[]>(transaction.objectStore('outbox').getAll()),
      request<{ key: string; value: number } | undefined>(transaction.objectStore('metadata').get('checkpoint')),
      request<Lease | undefined>(transaction.objectStore('leases').get('uploader')),
    ]);
    await complete(transaction);
    this.sequence = Math.max(this.sequence, ...pending.map(operation => operation.deviceSequence), 0);
    this.inspector.update(state => ({ ...state, entries: entries.sort((a, b) => a.department.localeCompare(b.department) || a.name.localeCompare(b.name)), pending, checkpoint: checkpointRecord?.value ?? 0, leaseOwner: lease && lease.expiresAt > Date.now() ? lease.owner : null }));
  }

  private operation(kind: string, payload: Record<string, unknown>): OperationEnvelope {
    return { operationId: crypto.randomUUID(), operationVersion: 1, installationId: this.installationId, deviceSequence: ++this.sequence, lastAppliedHouseholdCursor: this.inspector().checkpoint, kind, payload, createdAt: new Date().toISOString() };
  }

  private openDatabase(): Promise<IDBDatabase> {
    return new Promise((resolve, reject) => {
      const opening = indexedDB.open(DB_NAME, DB_VERSION);
      opening.onupgradeneeded = () => {
        const database = opening.result;
        for (const store of STORES) {
          if (database.objectStoreNames.contains(store)) continue;
          const keyPath = store === 'projections' ? 'id' : store === 'outbox' ? 'operationId' : store === 'aliases' ? 'aliasId' : store === 'syncIssues' ? 'id' : 'key';
          database.createObjectStore(store, { keyPath });
        }
      };
      opening.onerror = () => reject(opening.error);
      opening.onsuccess = () => resolve(opening.result);
      opening.onblocked = () => reject(new Error('Database migration blocked by another tab'));
    });
  }

  private note(message: string): void { this.inspector.update(state => ({ ...state, messages: [...state.messages.slice(-4), message] })); }
}

const IDS = { household: '018f0000-0000-7000-8000-000000000001', member: '018f0000-0000-7000-8000-000000000002', trip: '018f0000-0000-7000-8000-000000000003' };
const SAMPLE_ENTRIES: TripEntryProjection[] = [
  { id: '018f0000-0000-7000-8000-000000000011', tripId: IDS.trip, productId: '018f0000-0000-7000-8000-000000000021', name: 'Bananas', amount: 6, unit: 'each', acquired: false, department: 'Produce', cursor: 0 },
  { id: '018f0000-0000-7000-8000-000000000012', tripId: IDS.trip, productId: '018f0000-0000-7000-8000-000000000022', name: 'Milk', amount: 2, unit: 'L', acquired: false, department: 'Dairy', cursor: 0 },
  { id: '018f0000-0000-7000-8000-000000000013', tripId: IDS.trip, productId: '018f0000-0000-7000-8000-000000000023', name: 'Rice', amount: 1, unit: 'pack', acquired: false, department: 'Pantry', cursor: 0 },
];

function request<T>(value: IDBRequest<T>): Promise<T> { return new Promise((resolve, reject) => { value.onsuccess = () => resolve(value.result); value.onerror = () => reject(value.error); }); }
function complete(transaction: IDBTransaction): Promise<void> { return new Promise((resolve, reject) => { transaction.oncomplete = () => resolve(); transaction.onerror = () => reject(transaction.error); transaction.onabort = () => reject(transaction.error ?? new Error('IndexedDB transaction aborted')); }); }
