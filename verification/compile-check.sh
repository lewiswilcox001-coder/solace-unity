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
# Compiler selection. SOLACE_CSC overrides the compiler command entirely.
# (Set it when the default hangs: in some sandboxed environments the shared
# VBCSCompiler server wedges and `dotnet <csc.dll>` never returns. An
# in-process Roslyn driver lives at ~/workspace/tools/roslyncc and compiles
# the same sources with identical flags in ~2s:
#   SOLACE_CSC="dotnet ~/workspace/tools/roslyncc/bin/Debug/net8.0/roslyncc.dll")
if [ -n "$SOLACE_CSC" ]; then CSC="$SOLACE_CSC"; fi
if [ -z "$CSC" ] && command -v dotnet >/dev/null 2>&1; then
    SDK_DIR="$(dirname "$(readlink -f "$(command -v dotnet)")")/sdk"
    CSC_DLL="$(ls -d "$SDK_DIR"/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
    [ -n "$CSC_DLL" ] && CSC="dotnet $CSC_DLL"
fi
if [ -z "$CSC" ] && command -v csc >/dev/null 2>&1; then CSC="csc"; fi
if [ -z "$CSC" ] && command -v mcs >/dev/null 2>&1; then CSC="mcs"; fi
[ -n "$CSC" ] || fail "no C# compiler found (need dotnet SDK, csc, or mcs)"

# netstandard ref assembly for the pure-Core compile. The dotnet SDK packs
# may be hollow in some environments; fall back to the reference assembly
# Unity itself ships for its scripting runtime.
NETSTANDARD_REF=""
if command -v dotnet >/dev/null 2>&1; then
    SDK_ROOT="$(dirname "$(readlink -f "$(command -v dotnet)")")"
    NETSTANDARD_REF="$(ls "$SDK_ROOT"/packs/NETStandard.Library.Ref/*/ref/netstandard.dll 2>/dev/null | sort -V | tail -1)"
fi
if [ -z "$NETSTANDARD_REF" ]; then
    UNITY_NETSTANDARD="$EDITOR_DIR/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll"
    [ -f "$UNITY_NETSTANDARD" ] && NETSTANDARD_REF="$UNITY_NETSTANDARD"
fi

# Engine module assemblies. In Unity 6 the engine API is split across
# UnityEngine.*Module.dll; UnityEngine.dll alone is not enough to resolve
# everything (e.g. SerializeFieldAttribute lives in CoreModule). The real
# editor references all modules when compiling scripts, so this check does
# the same. (Editor-side UnityEditor.*Module.dll are intentionally NOT added:
# UnityEditor.dll already covers the editor API and passing both creates
# ambiguous-type errors.)
MODULE_DLLS=""
for _m in "$EDITOR_DIR"/Editor/Data/Managed/UnityEngine/UnityEngine.*Module.dll; do
    [ -f "$_m" ] && MODULE_DLLS="$MODULE_DLLS -r:$_m"
done

# Framework reference. The real editor always compiles scripts against its
# scripting-runtime reference assembly (netstandard 2.1), so every step gets
# it — not just the pure-Core step.
FRAMEWORK_REF=""
[ -n "$NETSTANDARD_REF" ] && FRAMEWORK_REF="-r:$NETSTANDARD_REF"

mkdir -p "$OUT_DIR"
PASS=1

compile() { # name, outfile, refs..., -- files...
    local name="$1"; local outfile="$2"; shift 2
    info "compiling $name -> $outfile"
    # shellcheck disable=SC2086
    if $CSC -nologo -t:library -langversion:9.0 -nullable:disable \
            -nowarn:1701,1702 -out:"$outfile" "$@" >"$OUT_DIR/$name.log" 2>&1; then
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
    compile "stubs" "$STUB_DLL" $FRAMEWORK_REF -r:"$UNITY_ENGINE_DLL" $MODULE_DLLS -- $STUB_SRCS
    STUB_REF="-r:$STUB_DLL"
else
    info "no package stubs needed"
    STUB_REF=""
fi

# --- 0b. Built-in uGUI package (com.unity.ugui): NOT a stub. ---
# In Unity 6, UnityEngine.UI / UnityEngine.EventSystems ship as the built-in
# com.unity.ugui package (source-only, always present in the editor install).
# The real editor compiles that source automatically; this check does the same
# so UGUI code is validated against the real API instead of stubs.
UGUI_PKG="$EDITOR_DIR/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui"
UIELEMENTS_DLL="$EDITOR_DIR/Editor/Data/Managed/UnityEngine/UnityEngine.UIElementsModule.dll"
UGUI_DLL=""
if [ -d "$UGUI_PKG/Runtime/UGUI" ]; then
    UGUI_SRCS="$(find "$UGUI_PKG/Runtime/UGUI" "$UGUI_PKG/Runtime/InternalBridge" -name '*.cs' | sort)"
    UGUI_REF=""
    [ -f "$UIELEMENTS_DLL" ] && UGUI_REF="-r:$UIELEMENTS_DLL"
    # shellcheck disable=SC2086
    compile "UnityEngine.UI" "$OUT_DIR/UnityEngine.UI.dll" \
        $FRAMEWORK_REF -r:"$UNITY_ENGINE_DLL" $MODULE_DLLS $UGUI_REF $STUB_REF -- $UGUI_SRCS
    [ -f "$OUT_DIR/UnityEngine.UI.dll" ] && UGUI_DLL="$OUT_DIR/UnityEngine.UI.dll"
else
    info "WARNING: built-in com.unity.ugui package not found under $EDITOR_DIR; UGUI code cannot be validated"
fi
UI_REF=""
[ -n "$UGUI_DLL" ] && UI_REF="-r:$UGUI_DLL"

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
        $FRAMEWORK_REF -r:"$UNITY_ENGINE_DLL" $MODULE_DLLS -r:"$CORE_DLL" $STUB_REF $UI_REF -- $UNITY_SRCS
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
        $FRAMEWORK_REF -r:"$UNITY_ENGINE_DLL" $MODULE_DLLS -r:"$UNITY_EDITOR_DLL" -r:"$CORE_DLL" $EXTRA_REF $STUB_REF $UI_REF -- $EDITOR_SRCS
else
    info "no Editor sources yet"
fi

if [ "$PASS" -eq 1 ]; then
    echo "COMPILE-CHECK PASSED: zero errors against the real Unity 6 API."
    exit 0
else
    fail "one or more assemblies failed to compile (see logs in $OUT_DIR)"
fi
