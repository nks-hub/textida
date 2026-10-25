#!/bin/bash
# Runs the P0 performance scenarios with the "mac" size profile (corpus up to 10 GiB, tree ~2 GiB).
# Usage: bench/run-mac.sh [base-dir]   (default ~/nkspad; needs ~/.dotnet with SDK 10.0.402 and rg)
set -u
BASE="${1:-$HOME/nkspad}"
export PATH="$HOME/.dotnet:/opt/homebrew/bin:$PATH" DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$(dirname "$0")/.."
OPT="--corpus $BASE/corpus --results $BASE/results --profile mac"
mkdir -p "$BASE/results" "$BASE/build"
dotnet build bench/NksPad.Bench -c Release -o "$BASE/build" 2>&1 | tail -n 3
B="dotnet $BASE/build/NksPad.Bench.dll"
R="$BASE/results"
{ date; sysctl -n machdep.cpu.brand_string; sw_vers -productVersion; rg --version | head -n 1; } > "$R/environment.txt" 2>&1
$B corpus $OPT > "$R/corpus.txt" 2>&1
{ dotnet test tests/NksPad.Core.Tests 2>&1 | tail -n 5; } > "$R/tests.txt"
$B a6 $OPT > "$R/a6.txt" 2>&1
$B a4 $OPT > "$R/a4.txt" 2>&1
$B a3 text-1GiB.log --reps 3 --inplace $OPT > "$R/a3-text-1GiB.log.txt" 2>&1
$B a3 text-10GiB.log --reps 3 $OPT > "$R/a3-text-10GiB.log.txt" 2>&1
$B a5 file text-1GiB.log text-10GiB.log $OPT > "$R/a5-file.txt" 2>&1
$B a5 file-count text-1GiB.log text-10GiB.log $OPT > "$R/a5-file-count.txt" 2>&1
$B a5 cancel text-10GiB.log 40 $OPT > "$R/a5-cancel.txt" 2>&1
$B a5 tree $OPT > "$R/a5-tree.txt" 2>&1
$B a5 tree-count $OPT > "$R/a5-tree-count.txt" 2>&1
for f in text-10MiB.log text-100MiB.log text-1GiB.log; do
  $B a2 $f $OPT > "$R/a2-$f.txt" 2>&1
  $B a2 $f --chunked $OPT > "$R/a2-$f-chunked.txt" 2>&1
done
$B a2 text-10GiB.log --probe $OPT > "$R/a2-probe.txt" 2>&1
echo finished > "$R/finished.txt"
