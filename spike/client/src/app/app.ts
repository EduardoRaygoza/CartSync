import { JsonPipe } from '@angular/common';
import { Component, computed, inject, OnInit } from '@angular/core';
import { RxdbSyncService } from './rxdb-sync.service';
import { TripEntryProjection } from './sync-types';

@Component({
  imports: [JsonPipe],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App implements OnInit {
  protected readonly sync = inject(RxdbSyncService);
  protected readonly remaining = computed(() => this.sync.inspector().entries.filter(entry => !entry.acquired).length);
  ngOnInit(): void { void this.sync.initialize(); }
  toggle(entry: TripEntryProjection): void { void this.sync.setAcquired(entry, !entry.acquired); }
  upload(): void { void this.sync.simulateUpload(); }
}
