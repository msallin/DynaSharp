#!/usr/bin/env bash
# Moves the public API that a release ships from PublicAPI.Unshipped.txt to PublicAPI.Shipped.txt in every project that has them.
# A line of Unshipped is appended to Shipped; a *REMOVED* line deletes its signature from Shipped. Unshipped keeps its header.
#
#   build/ship-public-api.sh            moves the lines (run it before a release, commit the result as chore)
#   build/ship-public-api.sh --check    fails while any Unshipped file holds a line (the release workflow runs this)
set -euo pipefail

check=false
[[ "${1:-}" == "--check" ]] && check=true

status=0
for unshipped in src/*/PublicAPI.Unshipped.txt; do
  shipped="${unshipped%Unshipped.txt}Shipped.txt"
  # The files are CRLF; the line endings are stripped here and written back as they were.
  lines=$(tr -d '\r' < "$unshipped" | grep -v '^#' | grep -v '^$' || true)
  [[ -z "$lines" ]] && continue

  if $check; then
    echo "::error file=$unshipped::$(wc -l <<< "$lines") lines of public API are not marked as shipped. Run build/ship-public-api.sh and commit the result before the release."
    status=1
    continue
  fi

  eol=$'\n'
  grep -q $'\r' "$shipped" && eol=$'\r\n'
  body=$(tr -d '\r' < "$shipped" | grep -v '^#' | grep -v '^$' || true)
  while IFS= read -r line; do
    if [[ "$line" == '*REMOVED*'* ]]; then
      signature="${line#\*REMOVED\*}"
      body=$(grep -vxF -- "$signature" <<< "$body" || true)
    else
      body+=$'\n'"$line"
    fi
  done <<< "$lines"

  header=$(tr -d '\r' < "$shipped" | grep '^#' || true)
  { printf '%s\n' "$header"; printf '%s\n' "$body" | grep -v '^$' | LC_ALL=C sort -u; } | sed "s/\$/${eol%$'\n'}/" > "$shipped.tmp"
  mv "$shipped.tmp" "$shipped"

  uheader=$(tr -d '\r' < "$unshipped" | grep '^#' || true)
  printf '%s\n' "$uheader" | sed "s/\$/${eol%$'\n'}/" > "$unshipped"
  echo "$unshipped: $(wc -l <<< "$lines") lines shipped"
done
exit $status
