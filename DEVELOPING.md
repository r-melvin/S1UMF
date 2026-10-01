# Developing S1UMF

Player instructions are in [README.md](README.md). This is for working on S1UMF itself.

## Build

```
dotnet build -c Release -p:GameDir="/path/to/Schedule I"
```

(or set `S1_GAME_DIR`). It references the game's MelonLoader and the interop assemblies MelonLoader
generates, so the game needs MelonLoader installed and launched once.


## Developer build

`dotnet build -c Release -p:Dev=true -p:GameDir=...` compiles in switches for chasing crashes. They are
**not in the release build**, so a stray file can never change a player's game. Each is a file in
`UserData/`; delete it to switch it off.

- `S1UMF.autoload` (text: save slot 1-5) loads that save at the main menu without a click, so a load
  crash can be reproduced launch after launch.
- `S1UMF.skip`, one `Assembly|Namespace.Type|Method` per line, makes those mod methods do nothing, to
  bisect a crash down to one postfix.
- `S1UMF.dumppatches` writes every Harmony-patched game method, its owners and its native RVA to
  `UserData/S1UMF.patches.tsv`, once after mod init and again 30 s after a save loads.

Patches that run on the wrong objects because IL2CPP folded their method with other classes' are not swept for
here: Polyfill's folded-code guard reads them from the running game.
