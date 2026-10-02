# CartSync

CartSync coordinates collaborative household shopping through store-specific product catalogs, planned trips, and completed trip history.

## Membership

**Household**:
The persistent collaboration boundary that owns shared stores, catalogs, planned trips, and completed-trip history, with exactly one owner and any number of members.
_Avoid_: Group, family

**Household Owner**:
The sole household member authorized to manage membership, transfer ownership, or delete the household.
_Avoid_: Administrator, admin

**Household Member**:
An authenticated person with full access to the household's shared shopping data and retained history.
_Avoid_: Collaborator, guest

**Household Invitation**:
An expiring, revocable offer sent to an email address that allows the matching household-free account to join.
_Avoid_: Share link, guest link

## Shopping

**Store**:
A household-defined physical shopping location with a normalized-unique label; an archived Store retains its identity and reserves that label.
_Avoid_: Retailer, chain

**Product**:
A household-wide reusable item with a normalized-unique name and one live note shared by planned and completed Trip views; an archived Product retains its identity and reserves that name.
_Avoid_: Store item, catalog item

**Store Product**:
The unique association between one Product and one Store, carrying that Product's optional Department placement at the Store.
_Avoid_: Catalog entry, Store placement

**Store Catalog**:
The derived collection of a Store's Store Products, with no identity or lifecycle separate from the Store.
_Avoid_: Product history, master list

**Department**:
A Store-owned grouping with a normalized-unique name and one position in the Store's manually managed shopping sequence.
_Avoid_: Aisle, category

**Trip**:
A named collaborative shopping visit bound to one Store whose identity transitions from planned to completed.
_Avoid_: Shopping list

**Planned Trip**:
A Trip that has not been completed and whose entries use current Product names and Department placements.
_Avoid_: Active list

**Completed Trip**:
The same Trip after explicit completion, retaining editable entry history with Product-name and Department snapshots while Product notes remain live.
_Avoid_: Archived list, past list

**Trip Entry**:
The unique association between one Trip and one Product, recording whether it was acquired and a positive decimal amount with a predefined unit.
_Avoid_: List item, cart item

## Collaboration

**Sync Issue**:
A member-specific preserved shopping action that could not automatically join the Household's shared state and requires that member's decision.
_Avoid_: Conflict, synchronization error, queue item
