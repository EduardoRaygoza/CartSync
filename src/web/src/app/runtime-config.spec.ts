import { describe, expect, it } from 'vitest';
import { validateRuntimeConfig } from './runtime-config';

describe('runtime configuration', () => {
  it('accepts the complete public configuration', () => {
    expect(validateRuntimeConfig({
      environment: 'staging', apiOrigin: 'https://api.example',
      externalIdAuthority: 'https://tenant.ciamlogin.com/tenant.onmicrosoft.com',
      externalIdClientId: 'client', externalIdScope: 'api://api/access_as_user', buildVersion: '0.1.0',
    }).environment).toBe('staging');
  });

  it('rejects a missing field', () => {
    expect(() => validateRuntimeConfig({ environment: 'staging' })).toThrow(/apiOrigin/);
  });
});
