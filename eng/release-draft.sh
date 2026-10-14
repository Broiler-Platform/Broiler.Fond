#!/usr/bin/env bash
# Creates the draft GitHub pre-release for one Broiler.Fond version.
#
#   eng/release-draft.sh <version> <artifacts-dir>
#
# <artifacts-dir> holds the Publish workflow's artifacts, extracted as
# `gh run download` / actions/download-artifact leave them:
#
#   <artifacts-dir>/broiler-fond-win-x64-self-contained-<version>/Broiler.Fond.Host.Windows.exe ...
#   <artifacts-dir>/broiler-fond-win-x64-framework-dependent-<version>/Broiler.Fond.Host.Windows.exe ...
#
# Each directory is zipped on its own as Broiler.Fond-<version>-win-x64-<variant>.zip.
# Python writes the zips, so the script runs the same on the Linux runner and by hand from
# Git Bash on Windows.
#
# The tag fond-v<version> must already exist on the remote; the release is created for it as
# a draft, so nothing is public until someone publishes it. Needs the GitHub CLI with
# GH_TOKEN (or a login) that may write releases, and Python 3.
set -euo pipefail

version=${1:?usage: eng/release-draft.sh <version> <artifacts-dir>}
artifacts=${2:?usage: eng/release-draft.sh <version> <artifacts-dir>}
tag="fond-v$version"
executable=Broiler.Fond.Host.Windows.exe
out=$(mktemp -d)

asset() {
  local variant=$1
  local source="$artifacts/broiler-fond-win-x64-$variant-$version"
  local zip="$out/Broiler.Fond-$version-win-x64-$variant.zip"
  [ -f "$source/$executable" ] || { echo "missing $source/$executable" >&2; exit 1; }
  "$python" - "$source" "$zip" <<'PY'
import os, sys, zipfile
source, target = sys.argv[1:3]
with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as z:
    for directory, _, files in sorted(os.walk(source)):
        for name in sorted(files):
            path = os.path.join(directory, name)
            z.write(path, os.path.relpath(path, source).replace(os.sep, '/'))
PY
  echo "$zip"
}

python=$(command -v python3 || command -v python) || { echo "needs python3" >&2; exit 1; }

self_contained=$(asset self-contained)
framework_dependent=$(asset framework-dependent)

cat > "$out/notes.md" <<EOF
Broiler Fond - Finance on Demand **$version**, a development build of the inert Windows
console shell. It performs no banking, storage, credential, UI or network operation and is
not a banking-capable prerelease (see \`docs/release/prerelease-checklist.md\`).

## Downloads

| Platform | File | Run |
| --- | --- | --- |
| Windows x64 | \`Broiler.Fond-$version-win-x64-self-contained.zip\` | unzip, run \`$executable\` |
| Windows x64 | \`Broiler.Fond-$version-win-x64-framework-dependent.zip\` | install the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0), unzip, run \`$executable\` |

The self-contained zip carries the .NET runtime; the framework-dependent zip is the smaller
download for machines that already have the .NET 10 runtime. Keep each zip's files together.
The executables are not code-signed.
EOF

gh release create "$tag" "$self_contained" "$framework_dependent" \
  --verify-tag \
  --draft \
  --prerelease \
  --title "Broiler Fond $version" \
  --notes-file "$out/notes.md" \
  --generate-notes
