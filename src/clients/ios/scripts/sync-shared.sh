#!/usr/bin/env bash
#
# Copies the files shared with Android from src/clients/shared into the iOS app's Resources,
# so the copies in the app stay identical to the shared originals.
#
#   ../shared/mock-data/*.json  ->  HoloRepository/Resources/MockData/
#   ../shared/viewer/           ->  HoloRepository/Resources/Viewer/   (skipped while empty)
#
# Files in the destination that no longer exist in shared/ are removed.
# Usage: scripts/sync-shared.sh   (from anywhere)

set -euo pipefail

ios_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
shared_dir="$(cd "$ios_dir/../shared" && pwd)"
resources_dir="$ios_dir/HoloRepository/Resources"

# Mock data: only the JSON files.
mock_src="$shared_dir/mock-data"
mock_dst="$resources_dir/MockData"
if ! compgen -G "$mock_src/*.json" > /dev/null; then
    echo "error: no JSON files in $mock_src" >&2
    exit 1
fi
mkdir -p "$mock_dst"
rsync -a --delete --include='*.json' --exclude='*' "$mock_src/" "$mock_dst/"
echo "Synced mock data: $(ls "$mock_dst"/*.json | wc -l | tr -d ' ') JSON files -> ${mock_dst#"$ios_dir/"}"

# Viewer bundle: the whole folder, once it has content.
viewer_src="$shared_dir/viewer"
viewer_dst="$resources_dir/Viewer"
if [ -d "$viewer_src" ] && [ -n "$(ls -A "$viewer_src" 2>/dev/null | grep -v '^\.DS_Store$')" ]; then
    mkdir -p "$viewer_dst"
    rsync -a --delete --exclude='.DS_Store' "$viewer_src/" "$viewer_dst/"
    echo "Synced viewer -> ${viewer_dst#"$ios_dir/"}"
else
    echo "Skipped viewer: $viewer_src is empty or missing"
fi
