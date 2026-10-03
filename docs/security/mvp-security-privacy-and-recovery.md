# CartSync MVP Security, Privacy, and Recovery

Status: accepted security baseline

This specification resolves
[Define security, privacy, and account recovery](https://github.com/EduardoRaygoza/CartSync/issues/8)
within CartSync's accepted Angular, ASP.NET Core, IndexedDB, and Azure SQL
architecture. It defines product policy and implementation boundaries; it does
not replace a threat model or release-time security verification.

## Eligibility and identity

CartSync launches as a Canada-first beta for adults. At Account creation, a
person attests that they have reached the age of majority where they live.
CartSync does not collect a birth date or province for that attestation.

Microsoft Entra External ID provides passwordless authentication through its
hosted email one-time-code experience. Angular uses browser-delegated OAuth 2.0
authorization code with PKCE and validates state and nonce. The API validates
token signature, issuer, audience, expiry, and the Account's current
authorization state on every request. Access tokens live only in memory and are
never written to IndexedDB, web storage, URLs, or logs.

CartSync offers no password, social login, MFA, or support-assisted identity
recovery in the MVP. Loss of the verified mailbox must be recovered through the
email provider; CartSync staff cannot override mailbox ownership.

Changing an Account's sign-in email requires a recent hosted sign-in through
the current address and a separate ten-minute, single-use code sent to the new
unique address. Five failed attempts invalidate the challenge. After both
addresses are verified, the Identity Access module replaces the External ID
email identity through Microsoft Graph, revokes existing sessions and offline
leases, and notifies both addresses. Email keys are trimmed and invariant-case
folded; CartSync never rewrites dots or plus-address tags.

## Member identity and invitations

An Account chooses a Member Alias when creating or joining a Household. Member
Aliases use the same Unicode NFKC, trimmed and collapsed-whitespace, invariant
case-folded normalized key as other domain names, preserve accents and
punctuation for display, and are unique among active or deletion-suspended
members of that Household. A member controls their own alias. Renaming it changes
all visible attribution. Ending the membership replaces the alias everywhere
with **Former Member**.

Accepted-member emails are not visible to any other Household Member, including
the Household Owner. The Owner sees a target email only while its Household
Invitation is pending. Membership administration uses Member Aliases and stable
member identities.

A Household Invitation:

- expires seven days after issue;
- uses a cryptographically random 256-bit, single-use opaque token whose hash is
  the only form stored by CartSync;
- can be accepted only by an Account whose verified email exactly matches the
  normalized target email and which belongs to no Household;
- is invalidated, together with every prior link, when resent;
- loses its raw target email immediately on acceptance, revocation, or expiry;
  and
- names only CartSync and the inviter's Member Alias in email content.

Creation, resend, acceptance, and revocation are rate-limited and return
non-enumerating responses. Azure Communication Services Email uses Canada as its
data location for invitations, verification codes, security notices, and
incident messages.

## Browser trust and offline authorization

After first sign-in on a browser, CartSync asks the member to choose:

- **Trusted Device** persists canonical projections, pending operations,
  checkpoints, aliases, personal Sync Issues, and a signed authorization lease
  in IndexedDB for offline use.
- **Shared Device** keeps authorized projections in volatile memory, clears them
  when the session ends, and does not offer offline use.

The Trusted Device lease identifies the Account, Household membership, and
installation, and expires 30 days after issue. It renews only after successful
online authentication and membership validation. The client records trusted
server time and locks local views until online validation if the device clock
moves backward. The lease is an application UI boundary, not a remote-erasure
or local-forensics guarantee.

CartSync relies on browser-profile sandboxing and operating-system storage
protection for offline data. It does not apply application encryption backed by
a key stored beside the IndexedDB data. The trust prompt warns against enabling
offline storage in a shared browser profile.

When an installation learns that membership ended, the Account was disabled,
or the Household entered deletion, it immediately locks and deletes Household
projections, pending operations, checkpoints, identity aliases, Sync Issues,
leases, and tokens. Only a non-sensitive removal notice may remain. A device
that stays disconnected cannot learn of revocation and may remain usable until
its signed lease expires.

Voluntary sign-out first requires pending operations to synchronize or asks for
explicit confirmation to discard them. It then purges CartSync data from that
browser. Every Account can sign out the current device or revoke all sessions;
connected devices purge promptly, while offline devices remain bounded by their
existing lease.

## Authorization and privileged actions

The API performs application authorization and Azure SQL row-level-security
checks for every operation. Immutable Account, member, and Household values are
set and cleared through `SESSION_CONTEXT` for each transaction, including pooled
connection reuse. The API's user-assigned managed identity authenticates only
the service to Azure SQL and Microsoft Graph under least-privilege roles; it
never authenticates an application Account.

Only Account or Household deletion and restoration require a fresh hosted email
code completed within five minutes. Member removal, ownership transfer, direct
export, and all-device sign-out rely on the current valid session. An email
change independently verifies both old and new addresses.

There is no routine operator impersonation or Household-content access.
Production access is time-limited, least-privilege, and audited break-glass
access justified by a security incident or an Owner-approved investigation.

## Account lifecycle

An ordinary member may request Account deletion after explicit confirmation and
fresh authentication. A Household Owner must first transfer ownership or delete
the Household. Deletion immediately revokes sessions and leases, suspends any
membership, and starts a 30-day recovery quarantine.

Signing in with the same verified email and completing fresh authentication
during quarantine can restore the Account. Its former membership returns only
if the Household still exists and its Owner did not revoke that suspended
membership. After 30 days, CartSync deletes the External ID account and direct
identity links, ends any remaining membership, and changes visible contribution
attribution to Former Member. Shared contributions remain with the Household.

Security records that have not reached their normal expiry lose their Account
link and email at final deletion, retain only a random investigation identifier,
and expire on schedule.

## Household deletion

Only the Household Owner may request deletion, after explicit confirmation and
fresh authentication. The request immediately revokes every membership's
access, session, and lease; invalidates Invitations; purges reachable caches;
and places the Household in a 30-day inaccessible quarantine.

Only the former Owner may restore during that period. Restoration returns prior
memberships, including their reserved Member Aliases, but does not restore
sessions, browser caches, pending Invitations, or unsynchronized local actions.
Every member signs in and bootstraps again.

After 30 days, CartSync permanently deletes the Household's canonical shopping
data, membership links, Invitations, Sync Issues, and operation receipts. Azure
SQL point-in-time backups are zone-redundant within the region, retained for
seven days, and have neither long-term nor geo-redundant retention; hard-deleted
data becomes unrecoverable as those backups age out.

## Data access, export, and policy acceptance

Every member may directly stream a JSON export of their Account data. The
Household Owner may also stream the Household's shared shopping data and Member
Aliases. An ordinary member's export does not include shared Household content,
and no export includes another Account's email. CartSync does not stage or
retain an export file on the server.

Account creation records the accepted terms and privacy-notice versions. Only a
material policy change blocks continued use pending renewed acceptance.
Correction is self-service through Member Alias and verified-email changes.

## Data location and protection

CartSync shopping data, Azure Monitor logs, Azure Communication Services email
resources, and Azure SQL regional backups remain in Canada. Microsoft Entra
External ID identity data uses Microsoft's North America geography; the privacy
notice states this boundary and does not promise Canada-only identity storage.
Azure Communication Services processes email content using its configured
Canada data location, while its documented global endpoints may transit or
process delivery data in other geographies.

HTTPS and TLS protect data in transit. Azure-managed keys protect Azure SQL,
backups, and supported service storage at rest. The MVP does not use
customer-managed keys or application-level field encryption.

## Telemetry, notices, and retention

CartSync collects only first-party security, reliability, performance, and cost
metadata. It has no behavioral analytics, advertising, marketing email, or
user-facing security-activity history. Logs never contain Product names,
Product notes, Trip contents, invitation tokens, email codes, access tokens, or
JSON export bodies.

Retention is:

| Data | Retention |
| --- | --- |
| Reliability, performance, and cost metadata | 30 days |
| Authentication, authorization, abuse, and privileged-access events | 90 days |
| Metadata-only synchronization operation receipts | 180 days while the Household exists |
| Pending Invitation target email | Until acceptance, revocation, or seven-day expiry |
| Account or Household deletion quarantine | 30 days |
| Regional Azure SQL point-in-time backups | 7 days |
| Completed Trip history | Until the Household is permanently deleted |

CartSync emails affected Accounts after sign-in email changes, ownership
changes, all-device sign-out, and Account or Household deletion or restoration.
It does not email for every sign-in or new installation.

For a confirmed security or privacy incident, CartSync notifies affected
Accounts by email and a persistent in-app incident notice without unreasonable
delay after assessment. The notice identifies affected data, likely impact,
mitigations, and available member actions, and CartSync completes any legally
required reporting.

## Platform references

These implementation constraints were verified from Microsoft documentation on
October 3, 2026:

- [Microsoft Entra ID and data residency](https://learn.microsoft.com/en-us/entra/fundamentals/data-residency)
- [Update a user with Microsoft Graph](https://learn.microsoft.com/en-us/graph/api/user-update)
- [Azure Communication Services data residency and privacy](https://learn.microsoft.com/en-us/azure/communication-services/concepts/privacy)
- [Azure SQL automated backups](https://learn.microsoft.com/en-us/azure/azure-sql/database/automated-backups-overview)
