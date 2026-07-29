#!/usr/bin/env bash
#
# Builds the distributable zip and checks it is loadable before it ships.
#
# Run with no arguments to package what is currently in Resources/. Pass a tag
# (v1.6.0) to additionally require that the tag agrees with About.xml.
#
# The release workflow and the build workflow both run this, so the path that
# produces a release is exercised on every push rather than first on a tag.

set -euo pipefail

cd "$(dirname "$0")/.."

# The folder the zip extracts to. Deliberately not "RandomPlus": a player with
# the original installed would have it overwritten, and the two are declared
# incompatible.
readonly MOD_DIR=RandomPlusPlus
readonly OUT=dist

fail() { echo "::error::$*" >&2; exit 1; }

version=$(sed -n 's:.*<modVersion>\(.*\)</modVersion>.*:\1:p' Resources/About/About.xml)
[[ -n $version ]] || fail "no <modVersion> in Resources/About/About.xml"

if [[ ${1:-} != "" ]]; then
  tag=${1#v}
  [[ $tag == "$version" ]] || fail "tag ${1} does not match <modVersion> $version"
fi

zip_name="${MOD_DIR}-${version}.zip"

rm -rf "$OUT"
mkdir -p "$OUT/$MOD_DIR"
cp -r Resources/. "$OUT/$MOD_DIR/"

# Debug builds leave these beside the assembly. They are gitignored, but a
# release can be cut from a working tree that has them.
find "$OUT/$MOD_DIR" \( -name '*.pdb' -o -name '*.deps.json' -o -name '*.dll.xml' \) -delete

(cd "$OUT" && zip -qr "$zip_name" "$MOD_DIR")

# --- Checks against the packaged tree, not against Resources/ ---------------

[[ -f "$OUT/$MOD_DIR/About/About.xml" ]] ||
  fail "About/About.xml is missing from the package; RimWorld will not list the mod"

# Exact case: RimWorld looks for About/Preview.png, and file lookups are
# case-sensitive on Linux, so a lowercase name silently shows no preview there.
[[ -f "$OUT/$MOD_DIR/About/Preview.png" ]] ||
  fail "About/Preview.png is missing or differently cased; the mod list shows no preview on Linux"

# ModIcon is optional, but if one is shipped it has to be named and sized exactly:
# RimWorld draws it at 32x32 and finds it by name, with the same Linux casing trap.
#
# Case-insensitive match by hand rather than `find -iname ... -quit`: -quit is
# GNU-only and this has to run on the macOS the mod gets tested on.
icon=
for f in "$OUT/$MOD_DIR/About"/*.[Pp][Nn][Gg]; do
  [[ -e $f ]] || continue
  [[ $(basename "$f" | tr '[:upper:]' '[:lower:]') == modicon.png ]] || continue
  icon=$f
  break
done

if [[ -n $icon ]]; then
  [[ $(basename "$icon") == ModIcon.png ]] ||
    fail "About/$(basename "$icon") must be named ModIcon.png exactly, or Linux will not find it"

  # PNG IHDR: width and height are big-endian uint32 at byte offsets 16 and 20.
  # Read as hex and convert in the shell, because od's --endian is GNU-only.
  ihdr=$(od -An -tx1 -j16 -N8 "$icon" | tr -d ' \n')
  w=$((16#${ihdr:0:8}))
  h=$((16#${ihdr:8:8}))
  [[ $w -eq 32 && $h -eq 32 ]] ||
    fail "About/ModIcon.png is ${w}x${h}; RimWorld draws it at 32x32"
fi

! compgen -G "$OUT/$MOD_DIR/*/Assemblies/0Harmony.dll" > /dev/null ||
  fail "0Harmony.dll is in the package; it would conflict with the Harmony mod"

# RimWorld loads the version folder matching the running game, so every version
# About.xml claims needs one that actually made it into the zip.
while read -r v; do
  compgen -G "$OUT/$MOD_DIR/$v/Assemblies/*.dll" > /dev/null ||
    fail "About.xml supports $v but the package has no $v/Assemblies/*.dll"
done < <(sed -n 's:.*<li>\(1\.[0-9]*\)</li>.*:\1:p' Resources/About/About.xml)

# A zip built from a tree with build output in it loads none of that, but it
# does bloat the download and leak paths, so treat it as a packaging mistake.
if unzip -Z1 "$OUT/$zip_name" | grep -Eq '(^|/)(bin|obj)/'; then
  fail "build output (bin/ or obj/) was packaged"
fi

size=$(stat -c%s "$OUT/$zip_name" 2>/dev/null || stat -f%z "$OUT/$zip_name")
echo "$zip_name  ($((size / 1024)) KiB, $(unzip -Z1 "$OUT/$zip_name" | wc -l) entries)"
unzip -Z1 "$OUT/$zip_name" | grep -v '/$' | grep -v '^.*/Textures/' | sed 's/^/  /'
echo "  ... plus $(unzip -Z1 "$OUT/$zip_name" | grep -c '/Textures/.*[^/]$') texture files"
