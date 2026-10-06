import { provideHttpClient } from '@angular/common/http';
import { isDevMode } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideServiceWorker } from '@angular/service-worker';
import { App } from './app/app';
import { AUTH } from './app/auth/auth.port';
import { MsalAuthAdapter } from './app/auth/msal-auth.adapter';
import { LocalAuthAdapter } from './app/auth/local-auth.adapter';
import { loadRuntimeConfig, RUNTIME_CONFIG } from './app/runtime-config';

const config = await loadRuntimeConfig();
const auth = config.environment === 'test' ? new LocalAuthAdapter() : new MsalAuthAdapter(config);

await bootstrapApplication(App, {
  providers: [
    provideHttpClient(),
    { provide: RUNTIME_CONFIG, useValue: config },
    { provide: AUTH, useValue: auth },
    provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode(), registrationStrategy: 'registerWhenStable:30000' }),
  ],
});
