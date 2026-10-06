# Release and rollback operations

## Staging

Every successful `CI` run on `staging` builds one immutable dogfood candidate.
The workflow publishes a prerelease containing the Angular shell, generated
client, database migrator, Bicep package, OCI image archives, manifest, and
provenance attestations. `Deploy staging` consumes that prerelease, runs Azure
`what-if`, executes the idempotent migration job, and deploys by digest.

After deployment, sign in through the hosted email-code flow and confirm the
page reports: **Connected. Your authorized CartSync scope is ready.** Link that
manual evidence and the staging deployment record to the implementation issue.

## Production promotion

1. Merge the accepted `staging` tree to `main` through a reviewed pull request.
2. Run **Promote production** from `main` with the accepted dogfood tag.
3. Approve the protected `production` environment.
4. The workflow verifies that the manifest source is contained in `main`,
   recreates the exact image digests from retained archives, runs migrations,
   deploys the exact web shell, and records environment-specific configuration.
5. Perform the same hosted email-code smoke test and attach its evidence.

No release workflow rebuilds an artifact. Database rollback is forward-only:
all schema changes remain expand-compatible when an earlier application
manifest is restored.

## Controlled rollback rehearsal

1. Establish a successful production baseline and retain its dogfood tag.
2. Promote a harmless second candidate with `simulateSmokeFailure=true`.
3. When the post-deployment smoke step fails, create a normal revert pull
   request on `main`; do not reset or rewrite history.
4. Run **Rollback production** with the baseline tag. It dispatches the same
   pinned-manifest deployment in rollback mode.
5. Confirm health and the authenticated empty bootstrap, then merge the rollback
   record from `main` back into `staging`.
6. Attach both workflow runs, the revert, deployment records, and digest
   comparison to the implementation issue.

## Branch protections

After `CI / required` has completed once on both protected branches, run the
GitHub setup script. It preserves the existing PR, admin, conversation,
force-push, and deletion protections and adds one strict stable required check.
