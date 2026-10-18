import {
  AccountInfo,
  BrowserCacheLocation,
  PublicClientApplication,
} from '@azure/msal-browser';
import { AuthPort } from './auth.port';
import { RuntimeConfig } from '../runtime-config';

export class MsalAuthAdapter implements AuthPort {
  private readonly client: PublicClientApplication;
  private account: AccountInfo | null = null;

  constructor(private readonly config: RuntimeConfig) {
    this.client = new PublicClientApplication({
      auth: {
        authority: config.externalIdAuthority,
        clientId: config.externalIdClientId,
        redirectUri: window.location.origin,
        postLogoutRedirectUri: window.location.origin,
      },
      cache: { cacheLocation: BrowserCacheLocation.MemoryStorage },
      system: { allowPlatformBroker: false },
    });
  }

  async initialize(): Promise<void> {
    await this.client.initialize();
    const redirect = await this.client.handleRedirectPromise();
    this.account = redirect?.account ?? this.client.getAllAccounts()[0] ?? null;
  }

  isSignedIn(): boolean {
    return this.account !== null;
  }

  async signIn(): Promise<void> {
    await this.client.loginRedirect({ scopes: [this.config.externalIdScope] });
  }

  async signOut(): Promise<void> {
    if (!this.account) return;
    await this.client.logoutRedirect({ account: this.account });
  }

  async acquireApiToken(): Promise<string> {
    if (!this.account) throw new Error('Sign in before connecting to CartSync.');
    const result = await this.client.acquireTokenSilent({
      account: this.account,
      scopes: [this.config.externalIdScope],
    });
    return result.accessToken;
  }
}
