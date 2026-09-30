# CartSync in-store prototype

Throwaway UI prototype for comparing three presentations of Department setup and the in-store checklist.

Run from the repository root:

```sh
python3 -m http.server 4174 --directory prototypes/in-store-mode
```

Then open [http://localhost:4174/?variant=A](http://localhost:4174/?variant=A). Use the floating switcher or the left/right arrow keys to compare:

- `A` — Route Stream
- `B` — Route Navigator
- `C` — Next Stop

State is intentionally kept in memory and resets on reload.
