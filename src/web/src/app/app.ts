import { ChangeDetectionStrategy, Component, Inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { HttpClient } from '@angular/common/http';
import { AUTH, AuthPort } from './auth/auth.port';
import { BootstrapClient } from './api/bootstrap-client';
import { RUNTIME_CONFIG, RuntimeConfig } from './runtime-config';

@Component({
  selector: 'cs-root',
  imports: [MatButtonModule, MatCardModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  readonly status = signal('Initializing CartSync…');
  readonly signedIn = signal(false);
  readonly busy = signal(true);
  private readonly bootstrap: BootstrapClient;

  constructor(
    http: HttpClient,
    @Inject(AUTH) private readonly auth: AuthPort,
    @Inject(RUNTIME_CONFIG) readonly config: RuntimeConfig,
  ) {
    this.bootstrap = new BootstrapClient(http, auth, config);
    void this.initialize();
  }

  async signIn(): Promise<void> {
    await this.auth.signIn();
    this.signedIn.set(this.auth.isSignedIn());
    if (this.signedIn()) await this.connect();
  }

  async signOut(): Promise<void> {
    await this.auth.signOut();
  }

  async retry(): Promise<void> {
    await this.connect();
  }

  private async initialize(): Promise<void> {
    try {
      await this.auth.initialize();
      this.signedIn.set(this.auth.isSignedIn());
      if (this.signedIn()) await this.connect();
      else this.status.set('Sign in to verify the CartSync connection.');
    } catch {
      this.status.set('CartSync could not initialize authentication.');
    } finally {
      this.busy.set(false);
    }
  }

  private async connect(): Promise<void> {
    this.busy.set(true);
    this.status.set('Connecting securely…');
    try {
      const page = await this.bootstrap.getEmptyScope();
      this.status.set(
        page.terminal && page.projections.length === 0
          ? 'Connected. Your authorized CartSync scope is ready.'
          : 'Connected to CartSync.',
      );
    } catch {
      this.status.set('The authorized CartSync scope is unavailable.');
    } finally {
      this.busy.set(false);
    }
  }
}
