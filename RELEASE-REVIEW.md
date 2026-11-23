# 2.0.9 release review

The Zombieween code compiled successfully on Windows (.NET 10, user-reported build log) in 2.0.9-rc.2. Stable 2.0.9 changes only metadata, packaging, and documentation; no zombie gameplay code was modified. Live server/client acceptance testing remains outstanding. Earlier detailed test evidence below concerns preceding release candidates.

# 2.0.9-rc.1 validation

- 1,825 regression assertions passed, retaining the current trap/variant coverage and adding bait identification, spherical 32-block distance, dimension isolation, sequential inventory removal and unrelated-item preservation.
- Vintage Story 1.22.7 dedicated server loaded the ground-storage patch without patch errors.
- Dedicated-server QA invoked the vanilla GroundStorable placement behavior using a simulated player and permitted-access test adapter. It created an actual ground pile with the bait behavior, discovered it, spawned a real zombie and verified bait selection, consumed one item, rejected an immediate second bite, and removed the exhausted pile after the cooldown. This is an isolated integration test, not a networked player session or claim-permission test.
- Mod JSON assets parse successfully. The release build has zero warnings/errors.
- Tests do not verify graphical client rendering, networked input, complete navigation through obstacles, or multiplayer behavior. Use RELEASE-PLAYTEST.md before a public event.
- Temporary QA mod is not included in the compiled release. Its source is retained under validation for reproducibility.
