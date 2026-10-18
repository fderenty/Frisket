#!/usr/bin/env bash
# Assembles one target's two release archives - the native library and the command line,
# each with what must travel with it - into artifacts/release/, which git already ignores.
#
# Usage: stage-release.sh <rid> <version> <native publish dir> <cli publish dir>
#
# Both carry the bundled typefaces inside one file, so both carry their licence texts -
# the OFL asks it of anything that bundles a font (OFL FAQ 1.20). Those come from the
# publish directories, where Frisket.Fonts copies them, rather than from a list kept here.
set -euo pipefail

rid="${1:?rid}"; version="${2:?version}"; native="${3:?native publish dir}"; cli="${4:?cli publish dir}"
root="$(git rev-parse --show-toplevel)"
stage="$(mktemp -d)"
out="$root/artifacts/release"
mkdir -p "$out"

case "$rid" in
  win-*) windows=1 ;;
  *)     windows=0 ;;
esac

# A runner hands bash Windows paths (D:\a\_temp); read them the way bash reads paths.
if command -v cygpath > /dev/null; then
  native="$(cygpath -u "$native")"; cli="$(cygpath -u "$cli")"
fi

common() {   # <dir>: what every archive carries
  cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$1/"
  cp -r "$2/licenses" "$1/licenses"
}

archive() {  # <name>
  if [ "$windows" = 1 ] && command -v 7z > /dev/null; then
    (cd "$stage" && 7z a -tzip "$out/$1.zip" "$1" > /dev/null)
  elif [ "$windows" = 1 ]; then
    powershell -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$stage/$1")' -DestinationPath '$(cygpath -w "$out/$1.zip")' -Force"
  else
    tar -czf "$out/$1.tar.gz" -C "$stage" "$1"
  fi
}

# The library, its header, the wrappers that load it, and on Windows the import library a
# C or C++ program links against.
name="frisket-$version-$rid-native"
d="$stage/$name"
mkdir -p "$d/wrappers/cpp" "$d/wrappers/pascal" "$d/wrappers/python"
if [ "$windows" = 1 ]; then
  cp "$native/frisket.dll" "$d/"
  cp "$root/bindings/native/bin/Release/net10.0/$rid/native/frisket.lib" "$d/"
else
  cp "$native/libfrisket.so" "$d/"
fi
cp "$root/bindings/native/frisket.h" "$root/bindings/native/README.md" "$d/"
cp "$root/bindings/native/wrappers/cpp/frisket.hpp" "$d/wrappers/cpp/"
cp "$root/bindings/native/wrappers/pascal/Frisket.pas" "$d/wrappers/pascal/"
cp "$root/bindings/native/wrappers/python/frisket.py" "$d/wrappers/python/"
common "$d" "$native"
archive "$name"

# The command line, under the name people type. The assembly is Frisket.Cli only because
# a frisket.exe beside Frisket.dll confuses a build; nothing sits beside it here.
name="frisket-$version-$rid-cli"
d="$stage/$name"
mkdir -p "$d"
if [ "$windows" = 1 ]; then
  cp "$cli/Frisket.Cli.exe" "$d/frisket.exe"
else
  cp "$cli/Frisket.Cli" "$d/frisket"
  chmod +x "$d/frisket"
fi
cp "$root/apps/cli/README.md" "$d/"
common "$d" "$cli"
archive "$name"

ls -l "$out"
