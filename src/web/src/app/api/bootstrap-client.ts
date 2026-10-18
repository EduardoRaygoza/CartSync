import { HttpClient, HttpHeaders } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { BootstrapPage } from '@generated/model/bootstrapPage';
import { AuthPort } from '../auth/auth.port';
import { RuntimeConfig } from '../runtime-config';

export class BootstrapClient {
  constructor(
    private readonly http: HttpClient,
    private readonly auth: AuthPort,
    private readonly config: RuntimeConfig,
  ) {}

  async getEmptyScope(): Promise<BootstrapPage> {
    const token = await this.auth.acquireApiToken();
    return firstValueFrom(
      this.http.get<BootstrapPage>(`${this.config.apiOrigin}/api/v1/sync/bootstrap`, {
        headers: new HttpHeaders({ Authorization: `Bearer ${token}` }),
      }),
    );
  }
}
