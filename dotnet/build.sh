#!/usr/bin/env bash
#
# Build the native library for this machine, stage it as a NuGet runtime
# asset, and optionally build/test/pack the managed package.
#
# Usage:
#   ./build.sh                 # native + managed build
#   ./build.sh --test          # ... and run the test suite
#   ./build.sh --pack          # ... and produce a .nupkg in dotnet/artifacts
#   ./build.sh --rid linux-x64 # override the detected runtime identifier
#
# Cross-compiling: pass --target <rust-triple> together with --rid to stage a
# foreign build, e.g.
#   ./build.sh --target aarch64-unknown-linux-gnu --rid linux-arm64
# Repeat per platform before packing to produce a multi-RID package; the
# staged runtimes/ directory accumulates.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NATIVE_DIR="$SCRIPT_DIR/native"
RUNTIMES_DIR="$SCRIPT_DIR/runtimes"

run_tests=0
run_pack=0
rid=""
target=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --test) run_tests=1; shift ;;
    --pack) run_pack=1; shift ;;
    --rid) rid="$2"; shift 2 ;;
    --target) target="$2"; shift 2 ;;
    -h|--help) sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

# --- Detect the runtime identifier and native library name ----------------

detect_rid() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"
  case "$arch" in
    x86_64|amd64) arch="x64" ;;
    aarch64|arm64) arch="arm64" ;;
    *) echo "unsupported architecture: $arch" >&2; exit 1 ;;
  esac
  case "$os" in
    Linux) echo "linux-$arch" ;;
    Darwin) echo "osx-$arch" ;;
    MINGW*|MSYS*|CYGWIN*) echo "win-$arch" ;;
    *) echo "unsupported OS: $os" >&2; exit 1 ;;
  esac
}

[[ -n "$rid" ]] || rid="$(detect_rid)"

case "$rid" in
  win-*) library_name="pdf_inspector_ffi.dll" ;;
  osx-*) library_name="libpdf_inspector_ffi.dylib" ;;
  *) library_name="libpdf_inspector_ffi.so" ;;
esac

# --- Build the native library ---------------------------------------------

echo "==> cargo build --release (rid: $rid${target:+, target: $target})"
if [[ -n "$target" ]]; then
  (cd "$NATIVE_DIR" && cargo build --release --target "$target")
  built="$NATIVE_DIR/target/$target/release/$library_name"
else
  (cd "$NATIVE_DIR" && cargo build --release)
  built="$NATIVE_DIR/target/release/$library_name"
fi

if [[ ! -f "$built" ]]; then
  echo "expected native library not found: $built" >&2
  exit 1
fi

destination="$RUNTIMES_DIR/$rid/native"
mkdir -p "$destination"
cp "$built" "$destination/$library_name"
echo "==> staged $destination/$library_name"

# --- Managed build --------------------------------------------------------

echo "==> dotnet build"
dotnet build "$SCRIPT_DIR/AUSoftware.PdfInspector.sln" -c Release

if [[ "$run_tests" == "1" ]]; then
  echo "==> dotnet test"
  # The tests load the native library straight from the cargo output.
  PDF_INSPECTOR_NATIVE_LIBRARY="$built" \
    dotnet test "$SCRIPT_DIR/AUSoftware.PdfInspector.sln" -c Release --no-build
fi

if [[ "$run_pack" == "1" ]]; then
  echo "==> dotnet pack"
  dotnet pack "$SCRIPT_DIR/src/AUSoftware.PdfInspector/AUSoftware.PdfInspector.csproj" \
    -c Release --no-build -o "$SCRIPT_DIR/artifacts"
  echo "==> packages in $SCRIPT_DIR/artifacts"
fi
