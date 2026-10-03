---
status: accepted
---

# Use Entra External ID for Passwordless Accounts

CartSync will use Microsoft Entra External ID browser-delegated authentication
with hosted email one-time-code screens and authorization code with PKCE. This
keeps passwords and primary authentication flows outside CartSync while fitting
email-targeted Household Invitations; the tradeoff is that identity data uses
Microsoft's North America geography rather than Canada-only storage.

## Consequences

- CartSync has no password, social-login, MFA, or support-assisted identity
  recovery path in the MVP.
- Access tokens remain in memory and never enter IndexedDB.
- Changing the sign-in email requires verification of both the old and new
  addresses before CartSync updates the External ID identity.
