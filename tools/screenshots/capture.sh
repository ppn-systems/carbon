#!/usr/bin/env bash
# World screenshot review tool: boots a throwaway local server per scenario, captures the desktop client
# headless with auto-login, converts the captures to PNG and writes a labelled contact sheet.
#
#   tools/screenshots/capture.sh [--lang <code>] [out_dir] [name_regex]
#
# See tools/screenshots/README.md for the scenario format and the environment knobs.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLIENT_LANG="${CARBON_LANG:-en}"
while [[ "${1:-}" == --* ]]; do
    case "$1" in
        --lang) CLIENT_LANG="${2:?--lang needs a locale code, e.g. vi}"; shift 2 ;;
        *) echo "capture.sh: unknown option '$1'" >&2; exit 2 ;;
    esac
done
[[ -f "$ROOT/resources/localization/$CLIENT_LANG.ini" ]] || { echo "capture.sh: no locale file resources/localization/$CLIENT_LANG.ini" >&2; exit 2; }
TOOL_DIR="$ROOT/tools/screenshots"
OUT_DIR="$(mkdir -p "${1:-$ROOT/artifacts/screenshots}" && cd "${1:-$ROOT/artifacts/screenshots}" && pwd)"
FILTER="${2:-.}"
SCENARIOS="${SCENARIOS:-$TOOL_DIR/scenarios.txt}"
SEED="${CARBON_SEED:-1337}"
WIDTH="${WIDTH:-1280}"
HEIGHT="${HEIGHT:-720}"
BASE_PORT="${BASE_PORT:-$((40000 + ($$ % 2000) * 4))}"
CLIENT_TIMEOUT="${CLIENT_TIMEOUT:-90}"
SERVER_BOOT_TIMEOUT="${SERVER_BOOT_TIMEOUT:-60}"

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

for tool in dotnet xvfb-run python3; do
    command -v "$tool" >/dev/null || { echo "capture.sh: '$tool' is required" >&2; exit 2; }
done

SERVER_DLL="$ROOT/artifacts/bin/Carbon.Host/debug/Carbon.Host.dll"
CLIENT_DLL="$ROOT/artifacts/bin/Carbon.Desktop/release/Carbon.Desktop.dll"
if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
    echo "[capture] building the Debug host (debug overrides are compiled out of Release) and the Release client"
    dotnet build "$ROOT/server/Carbon.Host/Carbon.Host.csproj" -c Debug -v q -nologo >/dev/null
    dotnet build "$ROOT/desktop/Carbon.Desktop/Carbon.Desktop.csproj" -c Release -v q -nologo >/dev/null
fi
[[ -f "$SERVER_DLL" && -f "$CLIENT_DLL" ]] || { echo "capture.sh: build outputs missing (run without SKIP_BUILD)" >&2; exit 2; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/carbon-shots.XXXXXX")"
SERVER_PID=""
cleanup() {
    if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
        kill "$SERVER_PID" 2>/dev/null || true
        wait "$SERVER_PID" 2>/dev/null || true
    fi
    rm -rf "$WORK"
}
trap cleanup EXIT

start_server() { # $1 = scenario name, $2 = server env words
    local name="$1" env_words="$2" log="$WORK/$1.server.log"
    rm -f "$WORK/world.db"*
    # shellcheck disable=SC2086 # env words are KEY=VALUE pairs from scenarios.txt
    env CARBON_DB_PROVIDER=sqlite CARBON_DB_CONNECTION="Data Source=$WORK/world.db" \
        CARBON_WORLD_SEED="$SEED" CARBON_TCP_PORT="$BASE_PORT" CARBON_UDP_PORT="$((BASE_PORT + 1))" \
        CARBON_WS_PORT=0 $env_words \
        dotnet "$SERVER_DLL" >"$log" 2>&1 &
    SERVER_PID=$!
    local waited=0
    until grep -q "Carbon.Host ready" "$log" 2>/dev/null; do
        if ! kill -0 "$SERVER_PID" 2>/dev/null || (( waited >= SERVER_BOOT_TIMEOUT * 10 )); then
            echo "[capture] $name: server failed to start, see log below" >&2
            tail -n 15 "$log" >&2
            stop_server
            return 1
        fi
        sleep 0.1
        waited=$((waited + 1))
    done
}

stop_server() {
    if [[ -n "$SERVER_PID" ]]; then
        kill "$SERVER_PID" 2>/dev/null || true
        wait "$SERVER_PID" 2>/dev/null || true
        SERVER_PID=""
    fi
}

run_client() { # $1 = scenario name, $2 = client env words
    local name="$1" env_words="$2"
    # shellcheck disable=SC2086
    (cd "$(dirname "$CLIENT_DLL")" && env LIBGL_ALWAYS_SOFTWARE=1 \
        CARBON_SERVER_HOST=127.0.0.1 CARBON_TCP_PORT="$BASE_PORT" CARBON_SEED="$SEED" \
        CARBON_LANG="$CLIENT_LANG" CARBON_AUTO_LOGIN=1 CARBON_AUTO_EXIT=1 CARBON_USERNAME="shots_$name" CARBON_PASSWORD=carbon123 \
        CARBON_SCREENSHOT_PATH="$OUT_DIR/$name.bmp" $env_words \
        timeout "$CLIENT_TIMEOUT" xvfb-run -a -s "-screen 0 ${WIDTH}x${HEIGHT}x24" dotnet "$CLIENT_DLL") \
        >"$WORK/$name.client.log" 2>&1
}

captured=0
failed=()
while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%%#*}"
    [[ -z "${line//[[:space:]]/}" ]] && continue
    IFS='|' read -r name server_env client_env <<<"$line"
    name="$(echo "$name" | xargs)"
    [[ "$name" =~ $FILTER ]] || continue
    server_env="$(echo "${server_env:-}" | xargs)"
    client_env="$(echo "${client_env:-}" | xargs)"

    echo "[capture] $name ($CLIENT_LANG)"
    rm -f "$OUT_DIR/$name.bmp" "$OUT_DIR/$name.png"
    if ! start_server "$name" "$server_env"; then
        failed+=("$name")
        continue
    fi

    if ! run_client "$name" "$client_env" || [[ ! -f "$OUT_DIR/$name.bmp" ]]; then
        echo "[capture] $name: client produced no capture, last log lines:" >&2
        tail -n 15 "$WORK/$name.client.log" >&2
        failed+=("$name")
    else
        grep -E "^\[(Focus|Terrain|Biome|FrameStats)\]" "$WORK/$name.client.log" | sed "s/^/    /" || true
        captured=$((captured + 1))
    fi

    stop_server
done <"$SCENARIOS"

python3 -I "$TOOL_DIR/contact_sheet.py" "$OUT_DIR" "$SCENARIOS"

echo "[capture] $captured scenario(s) captured into $OUT_DIR"
if (( ${#failed[@]} > 0 )); then
    echo "[capture] failed: ${failed[*]}" >&2
    exit 1
fi
