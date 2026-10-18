#!/usr/bin/env bash
# Did running everything leave the repository as it was committed?
#
# The suite writes its evidence into tracked files - tests/output, the schema, the
# single-file faces under release/ - so a commit whose outputs were not regenerated, or a
# sample that does not come out the same twice, shows up here as a difference.
#
# Usage: check-tree.sh content    - tracked files compared by content, and nothing new
#        check-tree.sh untracked  - nothing new only
#
# "content" belongs on Windows alone. On Linux the system zlib compresses the PNGs the suite
# builds and every PDF, so those differ there by design (CLAUDE.md 3). And it is asked of
# `git diff`, never `git status`: on a CRLF checkout status lists every text file the suite
# rewrites in LF, identical once normalised - 75 of them the day this was written.
set -euo pipefail

mode="${1:?content or untracked}"

if [ "$mode" = content ] && ! git diff --quiet; then
  echo "::error::the run changed tracked files - commit what the suite writes, or find what is not reproducible:"
  git diff --stat
  exit 1
fi

untracked=$(git ls-files --others --exclude-standard)
if [ -n "$untracked" ]; then
  echo "::error::the run wrote files nobody committed:"
  echo "$untracked"
  exit 1
fi

echo "the run reproduced the commit ($mode)"
