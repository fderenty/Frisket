#!/usr/bin/env bash
# Runs the suite, and refuses any skip the job did not name in EXPECTED_SKIPS.
#
# Three tests may decline - veraPDF's two when it is not installed, and test 75 when no
# library it can read was published - and on a developer's machine that is the truth told
# out loud. On a runner it is a check quietly not happening, and "Passed!" does not say so:
# the summary counts a skip as nothing at all. So the names are read back from the run.
set -uo pipefail

log="${RUNNER_TEMP:?}/tests.log"
dotnet test tests/test.csproj -c Release --no-build --nologo -l "console;verbosity=normal" | tee "$log"
status=${PIPESTATUS[0]}

skipped=$(grep -oE "Skipped test\.EngineTests\.[A-Za-z0-9_]+" "$log" | sed 's/.*\.//' | sort -u | paste -sd' ' -)
echo "skipped: ${skipped:-nothing}; expected: ${EXPECTED_SKIPS:-nothing}"

[ "$status" -eq 0 ] || exit "$status"
if [ "$skipped" != "${EXPECTED_SKIPS:-}" ]; then
  echo "::error::the suite skipped '${skipped:-nothing}' where this job expects '${EXPECTED_SKIPS:-nothing}'"
  exit 1
fi
