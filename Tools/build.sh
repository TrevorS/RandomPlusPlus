#!/usr/bin/env bash
#
# Builds the mod once per supported RimWorld version, and checks each build
# against that version's metadata.
#
#   ./Tools/build.sh              build and verify every version
#   ./Tools/build.sh 1.5          just one
#
# Each version is a separate `dotnet build`, so each gets its own restore
# against its own reference assemblies. Doing the fan-out here rather than in
# an MSBuild target keeps that guarantee obvious: nothing shares an assets file.

set -euo pipefail

cd "$(dirname "$0")/.."

# Must match the <RimWorldRefVersion> rows in RandomPlus.csproj and
# <supportedVersions> in About.xml. Tools/package.sh enforces the latter.
# Not readonly: bash 3.2, which is what macOS ships, rejects that on an array.
VERSIONS=(1.6 1.5)

readonly CONFIG=${CONFIG:-Release}

# Branch on $# rather than on the array's length: before bash 4.4 - and macOS
# ships 3.2 - expanding an empty array under `set -u` is an unbound-variable
# error, which would break the no-argument case, the usual one.
if [[ $# -gt 0 ]]; then
  versions=("$@")
else
  versions=("${VERSIONS[@]}")
fi

for v in "${versions[@]}"; do
  echo "==> RimWorld $v"
  dotnet build -c "$CONFIG" -p:RimWorldVersion="$v"

  # Guards against a build silently picking up another version's restore: the
  # manifest records what it actually compiled against.
  manifest="obj/$v/references.txt"
  if grep -q "krafs.rimworld.ref/" "$manifest"; then
    if ! grep -q "krafs.rimworld.ref/$v\." "$manifest"; then
      echo "::error::the $v build did not reference Krafs.Rimworld.Ref $v - check $manifest" >&2
      exit 1
    fi
  else
    # RIMWORLD_DIR won, which is a single installed version. It cannot stand in
    # for the others, so say so rather than quietly compiling 1.5 against 1.6.
    echo "    note: built against the local RimWorld install, not $v reference assemblies."
    echo "    note: unset RIMWORLD_DIR to check every version properly."
  fi

  dotnet run --project Tools/RandomPlus.Verify -c Release -- \
    "$manifest" "Resources/$v/Assemblies/RandomPlus.dll"
done
