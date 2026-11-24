#!/usr/bin/env python3
"""Static repository checks that need no Vintage Story installation.

- Every JSON asset, modinfo.json and DEFAULT-*.json parses as JSON.
- modinfo.json version matches <Version> in TheLastWatchZombies.csproj.
- modinfo.json targets the expected game version.
- Every lang key referenced as "seventhnightzombies:entity-<code>" for the six
  zombie codes exists in assets/seventhnightzombies/lang/en.json.

Run from the repository root:  python3 scripts/validate-assets.py
Exit status is non-zero when any check fails.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
EXPECTED_GAME = "1.22.7"
ZOMBIE_CODES = ["zombie", "alphazombie", "grabber", "hivezombie", "screamer", "bloater"]

failures = []


def fail(msg):
    failures.append(msg)
    print("FAIL:", msg)


json_files = sorted(ROOT.glob("assets/**/*.json")) + [ROOT / "modinfo.json"] + sorted(ROOT.glob("DEFAULT-*.json"))
parsed = {}
for path in json_files:
    try:
        with open(path, encoding="utf-8-sig") as fh:
            parsed[path] = json.load(fh)
    except Exception as exc:  # noqa: BLE001 - report every parse problem
        fail(f"{path.relative_to(ROOT)} does not parse: {exc}")
print(f"Parsed {len(parsed)}/{len(json_files)} JSON files")

modinfo = parsed.get(ROOT / "modinfo.json", {})
csproj = (ROOT / "TheLastWatchZombies.csproj").read_text(encoding="utf-8")
match = re.search(r"<Version>([^<]+)</Version>", csproj)
if not match:
    fail("TheLastWatchZombies.csproj has no <Version>")
elif modinfo.get("version") != match.group(1):
    fail(f"modinfo.json version {modinfo.get('version')} != csproj Version {match.group(1)}")
else:
    print(f"Version {match.group(1)} consistent between modinfo.json and csproj")

game = (modinfo.get("dependencies") or {}).get("game")
if game != EXPECTED_GAME:
    fail(f"modinfo.json game dependency is {game!r}, expected {EXPECTED_GAME!r}")

lang = parsed.get(ROOT / "assets/seventhnightzombies/lang/en.json", {})
for code in ZOMBIE_CODES:
    if f"entity-{code}" not in lang:
        fail(f"lang/en.json lacks entity-{code}")
    if not (ROOT / f"assets/seventhnightzombies/entities/{code}.json").exists():
        fail(f"missing entity asset {code}.json")

# Mycobiotic Cure must be craftable with a clean bandage OR a horsetail poultice.
cure = parsed.get(ROOT / "assets/seventhnightzombies/recipes/grid/mycobioticcure.json")
cure = cure if isinstance(cure, list) else [cure] if cure else []
binders = sorted({ing.get("code") for r in cure for k, ing in r.get("ingredients", {}).items()
                  if ing.get("code") != "seventhnightzombies:mycobiotic"})
if binders != ["game:bandage-clean", "game:poultice-*-horsetail"]:
    fail(f"mycobioticcure recipes should accept bandage or horsetail poultice, found {binders}")
for r in cure:
    if r.get("output", {}).get("code") != "seventhnightzombies:mycobioticcure":
        fail("a mycobioticcure recipe has the wrong output")

if failures:
    print(f"{len(failures)} check(s) failed")
    sys.exit(1)
print("All static asset checks passed")
