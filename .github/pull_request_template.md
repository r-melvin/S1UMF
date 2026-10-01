<!-- Read CONTRIBUTING.md first. PRs without in-game evidence are closed. -->

**Mod and exact version gated:** <!-- e.g. K9 Patrol 1.1.0, or game 0.4.7f7 -->

**Cause** (with file:line references in the game or mod):

**Fix** (what it does, and when it stands down):

**Evidence from a running game**
- Game build / MelonLoader / Polyfill line:
- Before (log lines and count per session):
```
```
- After (the S1UMF `fixed:` line, and the error gone):
```
```
- What I did in game to exercise it:

**Checklist**
- [ ] One fix, one exact mod version (or game build), stands down otherwise
- [ ] A bug in the mod's logic, not a missing member (those go to Polyfill)
- [ ] Nothing per-call that allocates, reflects or logs on a hot path
- [ ] Debug aids only behind `S1UMF_DEV`
- [ ] README tables updated
