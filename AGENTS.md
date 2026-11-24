# Agent instructions — The Last Watch Zombies (also see CLAUDE.md; keep both identical)

Vintage Story 1.22.7 code mod (C#, .NET 10). Mod ID `seventhnightzombies`.

Start every session by reading `docs/AGENT_HANDOFF.md` (state, ground rules,
dev log), then `docs/ARCHITECTURE.md` and `docs/KNOWN_ISSUES.md`.

Essentials:
- Build: `bash scripts/fetch-vs-server.sh 1.22.7 vs-server` then
  `dotnet build TheLastWatchZombies.csproj -c Release -p:VintageStoryPath=$PWD/vs-server`.
- Test: `dotnet run --project tests/Regression.csproj -c Release -p:VintageStoryPath=$PWD/vs-server`.
- Never touch branch `baseline/v2.0.9`; never commit game assemblies, DLLs, ZIPs or worlds.
- Preserve save compatibility (codes, SaveGame keys, JSON field names).
- Gameplay changes: branch + regression test + CHANGELOG entry.
- No releases or compiled-mod uploads without the author's authorization.
- Only report tests or in-game behaviour you actually ran.
- Before stopping, update the State table and Dev log in `docs/AGENT_HANDOFF.md` and commit.
