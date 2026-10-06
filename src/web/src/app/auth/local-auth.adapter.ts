import { AuthPort } from './auth.port';

/** Deterministic adapter used only when runtime-config declares the test environment. */
export class LocalAuthAdapter implements AuthPort {
  private signedIn = false;

  initialize(): Promise<void> { return Promise.resolve(); }
  isSignedIn(): boolean { return this.signedIn; }
  signIn(): Promise<void> { this.signedIn = true; return Promise.resolve(); }
  signOut(): Promise<void> { this.signedIn = false; return Promise.resolve(); }
  acquireApiToken(): Promise<string> {
    if (!this.signedIn) return Promise.reject(new Error('Not signed in.'));
    return Promise.resolve('local-test-token');
  }
}
