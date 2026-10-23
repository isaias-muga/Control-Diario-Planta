#!/usr/bin/env bash
# Decides whether a CI job has work to do (writes run=true|false to $GITHUB_OUTPUT). Used by build-test, client and e2e: the job always starts
# and reports, this only skips its steps. Inputs (env): EVENT, BEFORE, DRAFT, SKIP_DRAFT. An empty diff counts as "nothing to run".
set -euo pipefail
is_doc() {
  case "$1" in
    *.md|docs/*|.github/ISSUE_TEMPLATE/*|.github/pull_request_template.md|LICENSE|.gitignore) return 0 ;;
  esac
  return 1
}
run=true
reason="code changed"
if [ "$SKIP_DRAFT" = "true" ] && [ "$DRAFT" = "true" ]; then
  run=false
  reason="draft pull request"
else
  # A pull request is checked out as a merge commit whose first parent is the base: HEAD^1..HEAD is what the PR changes.
  # A push to main is compared with the previous tip (for a merge commit that is HEAD^1), or HEAD^1 if that is unknown.
  base=""
  if [ "$EVENT" != "pull_request" ] && git cat-file -e "${BEFORE}^{commit}" 2>/dev/null; then
    base="$BEFORE"
  else
    base="$(git rev-parse --verify --quiet HEAD^1 || true)"
  fi
  if [ -n "$base" ]; then
    only_docs=true
    while IFS= read -r f; do
      if ! is_doc "$f"; then only_docs=false; break; fi
    done < <(git diff --name-only --no-renames "$base" HEAD)
    if [ "$only_docs" = "true" ]; then
      run=false
      reason="only non-code files changed"
    fi
  fi
fi
echo "run=$run" >> "$GITHUB_OUTPUT"
echo "::notice::run=$run ($reason)"
