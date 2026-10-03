---
status: accepted
---

# Treat Offline Cache as Trusted-Device Data

CartSync will offer durable offline shopping only after a member designates a
browser as a Trusted Device. Browser and operating-system isolation protect the
IndexedDB cache, while a signed rolling 30-day authorization lease bounds UI
access; CartSync will not claim that it can remotely erase a disconnected device
or add ineffective encryption whose key is stored beside the cached data.

## Consequences

- Shared Devices keep Household data in volatile memory and cannot work offline.
- Detected membership removal purges all locally retained Household data and
  pending intent immediately.
- Disconnected revoked devices can retain readable data until their lease
  expires, and someone with direct browser-profile access may bypass UI controls.
