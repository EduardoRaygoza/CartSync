import { InjectionToken } from '@angular/core';

export interface RuntimeConfig {
  readonly environment: string;
  readonly apiOrigin: string;
  readonly externalIdAuthority: string;
  readonly externalIdClientId: string;
  readonly externalIdScope: string;
  readonly buildVersion: string;
}

export const RUNTIME_CONFIG = new InjectionToken<RuntimeConfig>('runtime-config');

export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  const response = await fetch('/runtime-config.json', { cache: 'no-store' });
  if (!response.ok) throw new Error('CartSync configuration is unavailable.');
  return validateRuntimeConfig(await response.json());
}

export function validateRuntimeConfig(value: unknown): RuntimeConfig {
  if (!value || typeof value !== 'object') throw new Error('CartSync configuration is invalid.');
  const config = value as Record<string, unknown>;
  const keys = ['environment', 'apiOrigin', 'externalIdAuthority', 'externalIdClientId', 'externalIdScope', 'buildVersion'];
  for (const key of keys) {
    if (typeof config[key] !== 'string' || config[key].length === 0) {
      throw new Error(`CartSync configuration field ${key} is invalid.`);
    }
  }
  return config as unknown as RuntimeConfig;
}
