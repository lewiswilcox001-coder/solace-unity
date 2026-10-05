#!/usr/bin/env bash
# Level 1 verification: compile every C# file in the Solace Unity project
# against the REAL UnityEngine.dll / UnityEditor.dll from the pinned editor.
# Usage: ./verification/compile-check.sh /path/to/extracted/editor
# Exit 0 = zero errors against the real Unity 6 API. Anything else = fail.
set -u

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
OUT_DIR="$PROJECT_DIR/verification/out"
STUBS_DIR="$PROJECT_DIR/verification/stubs"
EDITOR_DIR="${1:-}"

fail() { echo "COMPILE-CHECK FAILED: $*" >&2; exit 1; }
info() { echo "[compile-check] $*"; }

[ -n "$EDITOR_DIR" ] || fail "usage: $0 /path/to/extracted/editor"
[ -d "$EDITOR_DIR/Editor/Data/Managed" ] || fail "not an extracted Unity editor: $EDITOR_DIR"

UNITY_ENGINE_DLL="$EDITOR_DIR/Editor/Data/Managed/UnityEngine/UnityEngine.dll"
UNITY_EDITOR_DLL="$EDITOR_DIR/Editor/Data/Managed/UnityEditor.dll"
[ -f "$UNITY_ENGINE_DLL" ] || fail "missing $UNITY_ENGINE_DLL"
[ -f "$UNITY_EDITOR_DLL" ] || fail "missing $UNITY_EDITOR_DLL"
info "UnityEngine: $UNITY_ENGINE_DLL"
info "UnityEditor: $UNITY_EDITOR_DLL"

# --- Find a C# compiler (dotnet SDK's Roslyn preferred) ---
CSC=""
if command -v dotnet >/dev/null 2>&1; then
    SDK_DIR="$(dirname "$(readlink -f "$(command -v dotnet)")")/sdk"
    CSC_DLL="$(ls -d "$SDK_DIR"/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
    [ -n "$CSC_DLL" ] && CSC="dotnet $CSC_DLL"
fi
if [ -z "$CSC" ] && command -v csc >/dev/null 2>&1; then CSC="csc"; fi
if [ -z "$CSC" ] && command -v mcs >/dev/null 2>&1; then CSC="mcs"; fi
[ -n "$CSC" ] || fail "no C# compiler found (need dotnet SDK, csc, or mcs)"

# netstandard ref assembly for the pure-Core compile
NETSTANDARD_REF=""
if command -v dotnet >/dev/null 2>&1; then
    SDK_ROOT="$(dirname "$(readlink -f "$(command -v dotnet)")")"
    NETSTANDARD_REF="$(ls "$SDK_ROOT"/packs/NETStandard.Library.Ref/*/ref/netstandard.dll 2>/dev/null | sort -V | tail -1)"
fi

mkdir -p "$OUT_DIR"
PASS=1

compile() { # name, outfile, refs..., -- files...
    local name="$1"; local outfile="$2"; shift 2
    info "compiling $name -> $outfile"
    # shellcheck disable=SC2086
    if $CSC -nologo -t:library -langversion:9.0 -nullable:disable \
            -nowarn:1701,1702 -o:"$outfile" "$@" >"$OUT_DIR/$name.log" 2>&1; then
        info "$name: OK"
    else
        echo "--- $name ERRORS ---"
        grep -iE "error" "$OUT_DIR/$name.log" | head -40
        echo "--- (full log: $OUT_DIR/$name.log) ---"
        PASS=0
    fi
}

# --- 0. Package-API stubs (honest list; must stay as small as possible) ---
STUB_DLL="$OUT_DIR/Stubs.dll"
STUB_SRCS="$(find "$STUBS_DIR" -name '*.cs' 2>/dev/null)"
if [ -n "$STUB_SRCS" ]; then
    info "WARNING: compiling against $(echo "$STUB_SRCS" | wc -l) stubbed package API file(s):"
    echo "$STUB_SRCS" | sed 's/^/  stub: /'
    compile "stubs" "$STUB_DLL" -r:"$UNITY_ENGINE_DLL" -- $STUB_SRCS
    STUB_REF="-r:$STUB_DLL"
else
    info "no package stubs needed"
    STUB_REF=""
fi

# --- 1. Solace.Core: pure .NET, zero Unity references (by design) ---
CORE_SRCS="$(find "$PROJECT_DIR/Assets/Scripts/Solace.Core" -name '*.cs' | sort)"
[ -n "$CORE_SRCS" ] || fail "no Core sources found"
if [ -n "$NETSTANDARD_REF" ]; then
    compile "Solace.Core" "$OUT_DIR/Solace.Core.dll" -r:"$NETSTANDARD_REF" -nostdlib -- $CORE_SRCS
else
    # last resort: compile without explicit refs (compiler default libs)
    compile "Solace.Core" "$OUT_DIR/Solace.Core.dll" -- $CORE_SRCS
fi
CORE_DLL="$OUT_DIR/Solace.Core.dll"

# --- 2. Solace.Unity runtime glue: real UnityEngine.dll ---
UNITY_SRCS="$(find "$PROJECT_DIR/Assets/Scripts/Solace.Unity" -name '*.cs' -not -path '*/Editor/*' | sort)"
if [ -n "$UNITY_SRCS" ]; then
    # shellcheck disable=SC2086
    compile "Solace.Unity" "$OUT_DIR/Solace.Unity.dll" \
        -r:"$UNITY_ENGINE_DLL" -r:"$CORE_DLL" $STUB_REF -- $UNITY_SRCS
else
    info "no Solace.Unity runtime sources yet (glue phase pending)"
fi
UNITY_DLL="$OUT_DIR/Solace.Unity.dll"

# --- 3. Editor scripts: real UnityEditor.dll ---
EDITOR_SRCS="$(find "$PROJECT_DIR/Assets/Editor" "$PROJECT_DIR/Assets/Scripts/Solace.Unity" -path '*Editor*' -name '*.cs' 2>/dev/null | sort -u)"
if [ -n "$EDITOR_SRCS" ]; then
    EXTRA_REF=""; [ -f "$UNITY_DLL" ] && EXTRA_REF="-r:$UNITY_DLL"
    # shellcheck disable=SC2086
    compile "Editor" "$OUT_DIR/Solace.Editor.dll" \
        -r:"$UNITY_ENGINE_DLL" -r:"$UNITY_EDITOR_DLL" -r:"$CORE_DLL" $EXTRA_REF $STUB_REF -- $EDITOR_SRCS
else
    info "no Editor sources yet"
fi

if [ "$PASS" -eq 1 ]; then
    echo "COMPILE-CHECK PASSED: zero errors against the real Unity 6 API."
    exit 0
else
    fail "one or more assemblies failed to compile (see logs in $OUT_DIR)"
fi
