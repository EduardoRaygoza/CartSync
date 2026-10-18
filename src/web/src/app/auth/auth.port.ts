import { InjectionToken } from '@angular/core';

export interface AuthPort {
  initialize(): Promise<void>;
  isSignedIn(): boolean;
  signIn(): Promise<void>;
  signOut(): Promise<void>;
  acquireApiToken(): Promise<string>;
}

export const AUTH = new InjectionToken<AuthPort>('auth');
