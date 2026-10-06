# CartSync Azure foundation

The foundation is deployed in three explicit phases. No long-lived Azure
credential or Static Web Apps deployment token is stored in GitHub.

1. An Owner runs `bootstrap.bicep` once for `staging` and once for `production`.
   It creates each resource group, a deployment identity, an environment-bound
   GitHub federated credential, and resource-group-scoped deployment roles.
2. `platform.bicep` creates each environment's independent long-lived Azure
   resources. Pull, API, migration, database, registry, and monitoring identities
   are never shared between environments.
3. `release.bicep` deploys only pinned image digests, migration configuration,
   runtime configuration, and the API revision for a particular release.

## One-time Azure bootstrap

The operator needs Azure subscription Owner access for the two bootstrap
deployments. Replace the subscription id and run:

```bash
az login
az account set --subscription '<subscription-id>'
az deployment sub create --name cartsync-staging-bootstrap --location canadacentral \
  --template-file infra/bootstrap.bicep --parameters environment=staging
az deployment sub create --name cartsync-production-bootstrap --location canadacentral \
  --template-file infra/bootstrap.bicep --parameters environment=production
```

Copy each deployment's `deploymentClientId`, `tenantId`, and `subscriptionId`
outputs into non-secret variables on the matching GitHub environment.

## External ID

Use one Microsoft Entra External ID tenant with email one-time passcode as the
local-account sign-in method. Register separate SPA and protected API
applications for staging and production. Each API exposes the delegated
`access_as_user` scope; each SPA is granted that scope and has only its Azure
Static Web Apps origin as a redirect URI. The API registration does not use a
client secret.

The following GitHub environment variables are required in both environments:

| Variable | Meaning |
| --- | --- |
| `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | Workload-federated deployment identity |
| `NOTIFICATION_EMAIL`, `BUDGET_START_DATE` | Cost alert recipient and first day of the current month |
| `EXTERNAL_ID_INSTANCE`, `EXTERNAL_ID_TENANT_ID` | API token validation settings |
| `EXTERNAL_ID_API_CLIENT_ID` | Protected API application id |
| `EXTERNAL_ID_AUTHORITY`, `EXTERNAL_ID_SPA_CLIENT_ID`, `EXTERNAL_ID_SCOPE` | Public Angular runtime settings |
| `FOUNDATION_EXTERNAL_SUBJECT` | External ID object/subject claim for the single walking-skeleton operator |
| `FOUNDATION_ACCOUNT_ID`, `FOUNDATION_HOUSEHOLD_ID`, `FOUNDATION_MEMBER_ID` | Pre-generated UUIDv7 public identities for the temporary seed |

The temporary seed proves the accepted authorization boundary; the Account and
Household delivery slice replaces it with real onboarding.

The migrator creates the API's contained database principal from the API
managed identity client ID with `CREATE USER ... WITH SID ..., TYPE = E`. This
avoids granting Microsoft Graph Directory Readers to the SQL server identity.

## GitHub environments

Create `staging` and `production` environments. Restrict staging deployments to
the `staging` branch and production deployments to `main`. Production requires
Eduardo's approval, with self-review allowed for the solo-maintainer workflow.
Static Web Apps is linked to the repository with GitHub deployment
authorization. The workflows request a short-lived GitHub OIDC identity token
for each upload; no Static Web Apps deployment token is created or stored in
GitHub.
