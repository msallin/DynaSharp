#!/usr/bin/env bash
# Prints the next semantic version derived from Conventional Commits since the last v* tag.
# Prints nothing when no commit warrants a release (only chore, docs, ci, test, ...).
#
#   feat!: / BREAKING CHANGE  -> major (minor while the major version is 0)
#   feat:                     -> minor
#   fix: / perf:              -> patch
set -euo pipefail

last_tag=$(git describe --tags --abbrev=0 --match 'v[0-9]*' 2>/dev/null || true)
if [[ -z "$last_tag" ]]; then
  current="0.0.0"
  range="HEAD"
else
  current="${last_tag#v}"
  range="$last_tag..HEAD"
fi

IFS=. read -r major minor patch <<< "$current"
bump=0 # 0 = none, 1 = patch, 2 = minor, 3 = major
breaking='^[a-z]+(\([^)]*\))?!:'
feature='^feat(\([^)]*\))?:'
patchlevel='^(fix|perf)(\([^)]*\))?:'

while IFS= read -r -d '' message; do
  subject=$(head -n1 <<< "$message" | tr -d '\r')
  if [[ "$subject" =~ $breaking ]] || grep -q '^BREAKING[ -]CHANGE:' <<< "$message"; then
    bump=3
  elif [[ "$subject" =~ $feature ]]; then
    bump=$(( bump > 2 ? bump : 2 ))
  elif [[ "$subject" =~ $patchlevel ]]; then
    bump=$(( bump > 1 ? bump : 1 ))
  fi
done < <(git log -z --format='%B' "$range")

if (( bump == 3 && major == 0 )); then
  bump=2
fi

case $bump in
  3) echo "$((major + 1)).0.0" ;;
  2) echo "$major.$((minor + 1)).0" ;;
  1) echo "$major.$minor.$((patch + 1))" ;;
esac
