#!/usr/bin/env bash
# Runs exactly what CI runs (every `run: dotnet ...` step of .github/workflows/ci.yml, in order), from a clean
# build output, and stops at the first failure. Use it before every push so a PR is never red for a reason
# a local run could have caught.
#
#   tools/ci/verify.sh                 # verify the working tree as it is
#   tools/ci/verify.sh --merge-master  # verify HEAD merged onto the latest origin/master (what GitHub tests)
#
# --merge-master builds in a throwaway worktree, so the current checkout is never touched. A merge conflict
# is reported as a failure: merge origin/master into your branch and resolve it first.
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
WORKFLOW="$REPO_ROOT/.github/workflows/ci.yml"
MERGE_MASTER=0

for arg in "$@"; do
    case "$arg" in
        --merge-master) MERGE_MASTER=1 ;;
        -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
        *) echo "verify: unknown argument '$arg'" >&2; exit 2 ;;
    esac
done

if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
fi

mapfile -t STEPS < <(sed -n 's/^[[:space:]]*run:[[:space:]]*\(dotnet .*\)$/\1/p' "$WORKFLOW")
if [ "${#STEPS[@]}" -eq 0 ]; then
    echo "verify: no 'run: dotnet ...' steps found in $WORKFLOW" >&2
    exit 2
fi

WORK_DIR="$REPO_ROOT"
CLEANUP=""
if [ "$MERGE_MASTER" -eq 1 ]; then
    git -C "$REPO_ROOT" fetch -q origin master
    WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/carbon-verify.XXXXXX")"
    CLEANUP="$WORK_DIR"
    trap 'git -C "$REPO_ROOT" worktree remove --force "$CLEANUP" >/dev/null 2>&1 || true' EXIT
    git -C "$REPO_ROOT" worktree add -q --detach "$WORK_DIR" origin/master
    if ! git -C "$WORK_DIR" -c user.name=verify -c user.email=verify@localhost merge -q --no-edit "$(git -C "$REPO_ROOT" rev-parse HEAD)"; then
        echo "verify: FAILED — HEAD does not merge cleanly onto origin/master:" >&2
        git -C "$WORK_DIR" diff --name-only --diff-filter=U >&2
        exit 1
    fi
    echo "verify: testing HEAD $(git -C "$REPO_ROOT" rev-parse --short HEAD) merged onto origin/master $(git -C "$REPO_ROOT" rev-parse --short origin/master)"
fi

cd "$WORK_DIR"
rm -rf artifacts
for step in "${STEPS[@]}"; do
    echo "verify: $step"
    # shellcheck disable=SC2086 # each step is a plain dotnet command line from ci.yml
    if ! $step -warnaserror -v quiet -nologo; then
        echo "verify: FAILED at: $step" >&2
        exit 1
    fi
done

echo "verify: OK — all ${#STEPS[@]} CI steps passed"
