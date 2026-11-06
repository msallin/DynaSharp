#!/usr/bin/env bash
# Moves what a release ships from the Unshipped files to the Shipped ones: the public API of every project (PublicAPI.*.txt)
# and the analyzer rules of the generator (AnalyzerReleases.*.md).
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

# The analyzer rules of the generator: the rows of AnalyzerReleases.Unshipped.md go into a "Release <version>" section of
# AnalyzerReleases.Shipped.md, for the version build/next-version.sh computes (or RELEASE_VERSION, if set).
for unshipped in src/*/AnalyzerReleases.Unshipped.md; do
  [[ -e "$unshipped" ]] || continue
  shipped="${unshipped%Unshipped.md}Shipped.md"
  rows=$(tr -d '\r' < "$unshipped" | grep -E '^[A-Z]+[0-9]+ \|' || true)
  [[ -z "$rows" ]] && continue

  if $check; then
    echo "::error file=$unshipped::$(wc -l <<< "$rows") analyzer rules are not marked as shipped. Run build/ship-public-api.sh and commit the result before the release."
    status=1
    continue
  fi

  version="${RELEASE_VERSION:-$(build/next-version.sh)}"
  if [[ -z "$version" ]]; then echo "No release version: the commits since the last tag warrant none." >&2; exit 1; fi
  eol=$'\n'
  grep -q $'\r' "$shipped" && eol=$'\r\n'
  {
    tr -d '\r' < "$shipped" | sed -e :a -e '/^\n*$/{$d;N;ba' -e '}'
    printf '\n## Release %s\n\n### New Rules\n\nRule ID | Category | Severity | Notes\n--------|----------|----------|-------\n' "$version"
    printf '%s\n' "$rows"
  } | sed "s/\$/${eol%$'\n'}/" > "$shipped.tmp"
  mv "$shipped.tmp" "$shipped"
  tr -d '\r' < "$unshipped" | grep -vE '^[A-Z]+[0-9]+ \|' | sed "s/\$/${eol%$'\n'}/" > "$unshipped.tmp"
  mv "$unshipped.tmp" "$unshipped"
  echo "$unshipped: $(wc -l <<< "$rows") rules shipped as $version"
done
exit $status
