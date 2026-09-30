# CartSync list-builder prototype

Throwaway UI prototype for comparing three information hierarchies for list creation and catalog search.

Run from the repository root:

```sh
python3 -m http.server 4173 --directory prototypes/list-builder
```

Then open [http://localhost:4173/?variant=A](http://localhost:4173/?variant=A). Use the floating switcher or the left/right arrow keys to compare:

- `A` — Composer
- `B` — Split Workspace
- `C` — Trip First

State is intentionally in memory and resets when the page reloads.
