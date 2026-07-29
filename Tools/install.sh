#!/usr/bin/env bash
#
# Copies the built mod into RimWorld's Mods folder, so it can be tested without
# going anywhere near Steam Workshop.
#
#   ./Tools/install.sh              find RimWorld, copy the mod in
#   ./Tools/install.sh --uninstall  remove it again
#
# Set RIMWORLD_DIR to point at the install directly. Otherwise the usual Steam
# and GOG locations are searched, same as RimWorld.targets does for the build.

set -euo pipefail

cd "$(dirname "$0")/.."

readonly MOD_DIR=RandomPlusPlus

fail() { echo "error: $*" >&2; exit 1; }

# Every layout RimWorld ships in. On macOS the Mods folder lives inside the app
# bundle, which is the one people expect to be wrong and is not.
find_mods_dir() {
  local candidates=()
  # Spelled out rather than `[[ ... ]] && ...`, whose interaction with set -e
  # differs between bash 3.2 (macOS) and bash 5.
  if [[ -n ${RIMWORLD_DIR:-} ]]; then
    candidates+=("$RIMWORLD_DIR")
  fi
  candidates+=(
    "$HOME/Library/Application Support/Steam/steamapps/common/RimWorld"
    "$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app"
    "$HOME/.steam/steam/steamapps/common/RimWorld"
    "$HOME/.local/share/Steam/steamapps/common/RimWorld"
    "/Applications/RimWorld.app"
    "$HOME/Applications/RimWorld.app"
    "$HOME/GOG Games/RimWorld"
  )

  local base
  for base in "${candidates[@]}"; do
    [[ -d $base ]] || continue
    # <install>/Mods, or inside the macOS bundle one level down.
    if [[ -d "$base/Mods" ]]; then
      printf '%s\n' "$base/Mods"
      return 0
    fi
    if [[ -d "$base/RimWorldMac.app/Mods" ]]; then
      printf '%s\n' "$base/RimWorldMac.app/Mods"
      return 0
    fi
  done
  return 1
}

mods=$(find_mods_dir) || fail "could not find RimWorld. Set RIMWORLD_DIR to the install folder
       macOS: export RIMWORLD_DIR=\"\$HOME/Library/Application Support/Steam/steamapps/common/RimWorld\""

target="$mods/$MOD_DIR"

if [[ ${1:-} == --uninstall ]]; then
  if [[ -d $target ]]; then
    rm -rf "$target"
    echo "removed $target"
  else
    echo "nothing to remove at $target"
  fi
  exit 0
fi

[[ -f Resources/About/About.xml ]] || fail "run make build first - Resources/ is not populated"

# Replace rather than merge, so a stale assembly from a previous version folder
# cannot survive into the copy being tested.
rm -rf "$target"
mkdir -p "$target"
cp -R Resources/. "$target/"
find "$target" \( -name '*.pdb' -o -name '*.deps.json' \) -delete

echo "installed to $target"
printf '  versions: '
for d in "$target"/[0-9].[0-9]; do [[ -d $d ]] && printf '%s ' "$(basename "$d")"; done
echo
echo
echo "In RimWorld: Mods -> enable Harmony, then RandomPlusPlus, in that order."
echo "Then work through docs/SMOKE-TEST.md."
