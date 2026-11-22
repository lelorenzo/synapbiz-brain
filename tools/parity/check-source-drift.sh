#!/usr/bin/env bash
# Source drift guard: proves every moved file still equals its original in the source repository
# at the pinned commit, except for the three files carrying the declared patch.
# Usage: check-source-drift.sh <nuvora-root>
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: check-source-drift.sh <nuvora-root>" >&2
  exit 2
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
nuvora="$1"

contracts_src="$nuvora/src/Shared/Nuvora.Decisions.Contracts"
engine_src="$nuvora/src/Shared/Nuvora.Decisions.Engine"
contracts_dst="$repo/src/Synapbiz.Brain.Contracts/Decisions"
engine_dst="$repo/src/Synapbiz.Brain.Engine/Decisions"

offenders=0
report() { echo "$1"; offenders=$((offenders + 1)); }

# (1) The source repository sits at the pinned commit.
pinned="$(tr -d '[:space:]' < "$here/SOURCE.lock")"
actual="$(git -C "$nuvora" rev-parse HEAD)"
if [[ "$actual" != "$pinned" ]]; then
  report "pin: source HEAD is $actual, SOURCE.lock says $pinned"
fi

patched=("Contracts:DecisionEngineException.cs" "Engine:Facts/CanonicalFacts.cs" "Engine:Facts/FactSet.cs")
is_patched() {
  local entry
  for entry in "${patched[@]}"; do
    [[ "$entry" == "$1:$2" ]] && return 0
  done
  return 1
}

total=0
excluded=0
patched_seen=0

# (2) Every source file exists here and is byte-identical, except the patched ones.
compare_project() {
  local label="$1" src="$2" dst="$3" rel
  while IFS= read -r -d '' file; do
    rel="${file#"$src"/}"
    total=$((total + 1))
    if [[ "$label" == "Contracts" && "$rel" == Traces/* ]]; then
      excluded=$((excluded + 1))
      continue
    fi
    if [[ ! -f "$dst/$rel" ]]; then
      report "missing: $label/$rel"
    elif is_patched "$label" "$rel"; then
      patched_seen=$((patched_seen + 1))
    elif ! cmp -s "$file" "$dst/$rel"; then
      report "differs: $label/$rel"
    fi
  done < <(find "$src" -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | sort -z)
}
compare_project Contracts "$contracts_src" "$contracts_dst"
compare_project Engine "$engine_src" "$engine_dst"

# (3) No extra *.cs here.
check_extra() {
  local label="$1" src="$2" dst="$3" rel
  while IFS= read -r -d '' file; do
    rel="${file#"$dst"/}"
    if [[ ! -f "$src/$rel" ]]; then
      report "extra: $label/$rel"
    elif [[ "$label" == "Contracts" && "$rel" == Traces/* ]]; then
      report "extra: $label/$rel (excluded in the source mapping)"
    fi
  done < <(find "$dst" -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | sort -z)
}
check_extra Contracts "$contracts_src" "$contracts_dst"
check_extra Engine "$engine_src" "$engine_dst"

# (4) The declared patch is exactly the committed one.
regenerated="$(mktemp)"
trap 'rm -f "$regenerated"' EXIT
{
  diff -u --label a/DecisionEngineException.cs --label b/DecisionEngineException.cs \
    "$contracts_src/DecisionEngineException.cs" "$contracts_dst/DecisionEngineException.cs" || true
  diff -u --label a/Facts/CanonicalFacts.cs --label b/Facts/CanonicalFacts.cs \
    "$engine_src/Facts/CanonicalFacts.cs" "$engine_dst/Facts/CanonicalFacts.cs" || true
  diff -u --label a/Facts/FactSet.cs --label b/Facts/FactSet.cs \
    "$engine_src/Facts/FactSet.cs" "$engine_dst/Facts/FactSet.cs" || true
} > "$regenerated"
if ! cmp -s "$regenerated" "$here/expected.patch"; then
  report "patch: regenerated patch differs from tools/parity/expected.patch"
fi

# (5) The rule schema and the conformance data are byte-identical to the source repository (no patch).
data_files=(
  "src/Platform/Spec/Synapbiz.Spec.Brain/Schemas/brain.rule.v1.json:src/Synapbiz.Brain.Spec/Schemas/brain.rule.v1.json"
  "tests/fixtures/spec/brain/conformance.json:tests/fixtures/spec/brain/conformance.json"
  "tests/fixtures/spec/brain/contracts/contract-accounting.json:tests/fixtures/spec/brain/contracts/contract-accounting.json"
  "tests/fixtures/spec/brain/contracts/contract-inventory.json:tests/fixtures/spec/brain/contracts/contract-inventory.json"
  "tests/fixtures/spec/brain/contracts/contract-procurement.json:tests/fixtures/spec/brain/contracts/contract-procurement.json"
  "tests/fixtures/spec/brain/contracts/contract-sales.json:tests/fixtures/spec/brain/contracts/contract-sales.json"
  "tests/fixtures/spec/brain/contracts/contract-synthetic.json:tests/fixtures/spec/brain/contracts/contract-synthetic.json"
)
data_total=0
for entry in "${data_files[@]}"; do
  from="${entry%%:*}"
  to="${entry##*:}"
  data_total=$((data_total + 1))
  if [[ ! -f "$repo/$to" ]]; then
    report "missing: data $to"
  elif [[ ! -f "$nuvora/$from" ]]; then
    report "source missing: data $from"
  elif ! cmp -s "$nuvora/$from" "$repo/$to"; then
    report "differs: data $to"
  fi
done

if [[ $offenders -ne 0 ]]; then
  echo "source drift: $offenders offender(s)"
  exit 1
fi

echo "source drift: none ($total files, $patched_seen patched, $excluded excluded; $data_total data files)"
