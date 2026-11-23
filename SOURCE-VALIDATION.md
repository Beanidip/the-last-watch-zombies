# 2.0.9 validation status

**Stable source release (same gameplay code as 2.0.9-rc.2):** The `/zombieween` command, dormant count addition, persistent loaded-zone ledger and cure grants were checked with static source assertions. The previous 2.0.9-rc.2 code **successfully compiled on Windows using .NET 10** according to the user-provided build log. Only release metadata, documentation and Windows packaging were changed for 2.0.9. A live Vintage Story server/client gameplay test has not been confirmed. Do not treat an existing 2.0.9-rc.1 DLL as containing this feature. Build on a Windows PC with the included script before installing on a server.

Recommended acceptance checks: confirm `/zombieween` is admin-only; each run adds exactly five dormant normals per counted zone even above the natural global storage cap; no zombies appear until normal activation; two cures arrive online (or drop at feet if inventory is full); repeat invocation adds another five; save/reload preserves counts and the generated-zone index; migration retains population counts.

---

# Earlier validation

See RELEASE-REVIEW.md for evidence and limitations. Run TEST-WINDOWS.ps1 and RELEASE-PLAYTEST.md before using the build for a public multiplayer event.
