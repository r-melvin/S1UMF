# Contributing to S1UMF

S1UMF patches other people's mods. A wrong fix breaks someone's game in a way that is hard to trace back
here, so the bar for a change is **evidence from a running game**, not plausibility. Please read this before
opening an issue or a pull request. Anything that ignores it will be closed without review.

## Before you open anything

- **Is it S1UMF?** Remove `S1UMF.dll` and try again. If the problem stays, it belongs to the mod's author
  (or to Polyfill), not here.
- **Is it Polyfill?** A `MissingMethodException`, `MissingFieldException` or "unresolved" line in the
  Polyfill report is a missing bridge. Those go to
  [Polyfill](https://github.com/DooDesch-Mods/ScheduleOne-Polyfill/issues), not here.
- **Are you on the latest Polyfill?** Your log must contain `[Polyfill <version>] has the fixes S1UMF relies on`.
  `is older than S1UMF needs` means update Polyfill first. Reports without the first line are closed.
- **Did a fix stand down?** `standing down` in the log means your version of that mod is not the one the fix
  was written for. That is working as intended. Ask for the fix to be updated to the new version (an issue
  with the log is enough), rather than reporting it as a bug.

## Issues

Use the issue form. It asks for:

- Schedule I version and branch, S1UMF version, Polyfill line, MelonLoader version, OS.
- The **exact** name and version of the mod involved, from its load line in the log.
- What you did, what you expected, what happened.
- `MelonLoader/Latest.log` attached as a file. A pasted excerpt is not enough: the start-up lines are
  where most answers are.

One problem per issue. "Mod X doesn't work" with no log is closed.

## Pull requests

Every fix in S1UMF follows the same rules. A PR that breaks one of them will not be merged.

1. **One fix per PR**, for **one exact version** of **one mod** (or one game build). It is gated with
   `Gate(...)` / `Apply(...)` or `GameFits()` and **stands down** on any other version. No open-ended
   version ranges.
2. **It fixes a bug in the mod's own logic** that a game update exposed. A member the update renamed or
   removed is a Polyfill bridge, not a S1UMF fix. So is a problem that is not one mod's, such as a patch
   running on the wrong objects: Polyfill fixes it for everyone.
3. **Say why, with sources.** The comment above the fix states the cause, with file and line references
   in the game or the mod (decompiled source is fine), the way the existing fixes do. "This seems to help"
   is not a cause.
4. **Evidence from a running game** in the PR description:
   - the log lines before the fix (the error, with its count per session) and after it (the S1UMF
     `fixed:` line, and the error gone);
   - the game build, the mod version, and the Polyfill line;
   - what you did in game to exercise it.
5. **Smallest change that works.** Prefer a guard or containment that does nothing when the bug does not
   happen. Never swallow exceptions broadly. Contain the one you diagnosed, log it once, and let everything
   else through.
6. **Cheap on hot paths.** Many patched methods run every frame. No reflection lookups, LINQ, allocations or
   logging per call. Cache what you look up, and log at most a few times per session.
7. **No new dependencies**, and nothing that writes files, opens network connections, or changes other
   mods' files.
8. **Debugging aids go behind `S1UMF_DEV`** (see [DEVELOPING.md](DEVELOPING.md)). The release build must
   contain nothing a player can trip over.
9. **Update the README tables** (the fix, and "Mods it fixes") in the same PR.

**AI-assisted PRs are welcome if they meet the same bar**, which in practice means you ran the game and the
evidence is yours. Generated changes without in-game evidence, speculative "hardening", or reformatting and
renaming will be closed.

## Building

See [DEVELOPING.md](DEVELOPING.md). `dotnet build -c Release -p:GameDir="<Schedule I folder>"`.

## Code of conduct

Be kind to the mod authors. S1UMF exists because their mods are worth keeping alive. A fix here is a
workaround until they ship their own, never a criticism.
