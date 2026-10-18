#!/usr/bin/env bash
#
# Publishes the native library for one Linux target.
#
#   ./bindings/native/build.sh                 # linux-x64, on an x64 machine
#   ./bindings/native/build.sh linux-arm64     # 64-bit ARM - a Pi 4/5, most cloud ARM
#   ./bindings/native/build.sh linux-arm       # 32-bit ARM - an older Pi, embedded boards
#
# A third argument, after the configuration, is the version fk_version reports - a
# release passes its tag; left out, the build carries Directory.Build.props' 0.1.0-dev:
#
#   ./bindings/native/build.sh linux-x64 Release 1.2.3
#
# Both ARM targets need the cross toolchain when built from x64; see below.
#
# linux-x64 wants clang and the zlib headers:
#
#   sudo apt-get install -y clang zlib1g-dev
#
# Either ARM target from an x64 machine is a cross build, and needs a linker, an
# object copier and a sysroot for the other architecture.
#
# FIRST, and this is the step that looks like a broken mirror: Ubuntu does not serve
# arm packages from archive.ubuntu.com. That host carries amd64 and i386 and nothing
# else; everything other than x86 lives on ports.ubuntu.com. Add a foreign architecture
# without saying so and apt asks the wrong host for it, and answers with a screenful of
#
#   Err  http://archive.ubuntu.com/ubuntu noble/main armhf Packages
#     404  Not Found
#
# which reads as the archive being down rather than as the archive being the wrong one.
# The existing entries have no Architectures: line, so they are asked for every
# architecture enabled - which is why adding one breaks apt for all of them at once.
#
#   # say what the archive really has, and the 404s stop
#   sudo sed -i 's|^Components: main universe restricted multiverse$|&\nArchitectures: amd64|' \
#       /etc/apt/sources.list.d/ubuntu.sources
#
#   # and where the rest of them are
#   sudo tee /etc/apt/sources.list.d/ubuntu-ports.sources >/dev/null <<'EOF'
#   Types: deb
#   URIs: http://ports.ubuntu.com/ubuntu-ports/
#   Suites: noble noble-updates noble-backports noble-security
#   Components: main universe restricted multiverse
#   Architectures: armhf arm64
#   Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg
#   EOF
#
# THEN the packages:
#
#   sudo dpkg --add-architecture arm64          # or armhf, for linux-arm
#   sudo apt-get update
#   sudo apt-get install -y clang lld llvm binutils-aarch64-linux-gnu \
#                           gcc-aarch64-linux-gnu zlib1g-dev:arm64 libssl3t64:arm64
#
# libssl3t64 and not libssl3: Ubuntu 24.04 carried out the 64-bit time_t transition,
# which renamed a great many libraries with that suffix. Asking for the old name gets
#
#   Package libssl3:armhf is not available, but is referred to by another package.
#
# and then, further down, names the new one - so the message is worse than it looks
# only until it is read to the end. On a release before noble it is libssl3.
#
# For linux-arm read armhf for arm64 throughout, and
# binutils-arm-linux-gnueabihf / gcc-arm-linux-gnueabihf for the toolchain.
#
# Built natively on an ARM machine it needs nothing beyond the x64 line.
#
# THREE things a cross build needs that a native one does not, each of which was found
# the hard way and each of which fails a long way from its cause:
#
#   - lld, and not the default bfd. ILCompiler passes -fuse-ld=bfd, and the HOST
#     /usr/bin/ld.bfd is single-target: it answers "unrecognised emulation mode:
#     armelf_linux_eabi" and lists only x86 ones. The cross binutils do have an ld
#     that would work, but clang never reaches for it. lld is multi-target and does.
#
#   - that architecture's objcopy. The link then succeeds and the build fails one step
#     later, splitting debug symbols off an ARM object with the host's objcopy.
#
#   - libssl for the target. Not needed to build - needed to RUN: the runtime aborts
#     with "No usable version of libssl was found" the first time anything reaches
#     System.Security.Cryptography, which the engine does for every image it embeds.
set -euo pipefail

target="${1:-linux-x64}"
configuration="${2:-Release}"
version="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

case "$target" in
  linux-x64|linux-arm64|linux-arm) ;;
  *) echo "unknown target '$target'. Try linux-x64, linux-arm64 or linux-arm." >&2; exit 2 ;;
esac

command -v clang >/dev/null || {
  echo "clang is not installed; native AOT uses it to link." >&2
  exit 2
}

# Cross-linking needs that architecture's binutils. Checked here rather than left to
# ILCompiler, whose message about it is a good deal less direct.
case "$target" in
  linux-arm64)
    if [ "$(uname -m)" != aarch64 ]; then
      # objcopy rather than ld: ld is not what the build ends up calling, and a guard
      # that names a tool nobody uses passes on a machine that cannot build.
      command -v aarch64-linux-gnu-objcopy >/dev/null || {
        echo "cross-building for arm64 needs binutils-aarch64-linux-gnu." >&2
        exit 2
      }
      command -v ld.lld >/dev/null || command -v lld >/dev/null || {
        echo "cross-linking to arm64 needs lld: the host's bfd is single-target." >&2
        exit 2
      }
    fi
    ;;
  linux-arm)
    if [ "$(uname -m)" != armv7l ]; then
      command -v arm-linux-gnueabihf-objcopy >/dev/null || {
        echo "cross-building for arm needs binutils-arm-linux-gnueabihf." >&2
        exit 2
      }
      command -v ld.lld >/dev/null || command -v lld >/dev/null || {
        echo "cross-linking to arm needs lld: the host's bfd is single-target." >&2
        exit 2
      }
    fi
    ;;
esac

echo "Target    : $target"
echo "Machine   : $(uname -m)"

# The two properties a cross build cannot do without, and the guard above checks for
# exactly the tools they name. Without them the guard was worse than nothing: it looked
# for arm-linux-gnueabihf-ld, and then the build invoked the host's ld.bfd instead - a
# check and the thing it checks not reaching each other, which is section 2's subject in
# a shell script.
case "$target" in
  linux-arm64) cross=(-p:LinkerFlavor=lld -p:ObjCopyName=aarch64-linux-gnu-objcopy) ;;
  linux-arm)   cross=(-p:LinkerFlavor=lld -p:ObjCopyName=arm-linux-gnueabihf-objcopy) ;;
  *)           cross=() ;;
esac

# Native builds want none of it: on the machine itself the default linker is the right
# one, and its objcopy reads its own objects.
if [ "$target" = "linux-arm64" ] && [ "$(uname -m)" = aarch64 ]; then cross=(); fi
if [ "$target" = "linux-arm" ] && [ "$(uname -m)" = armv7l ]; then cross=(); fi

versioned=()
[ -n "$version" ] && versioned=(-p:Version="$version")

dotnet publish "$here/Frisket.Native.csproj" -c "$configuration" -r "$target" \
    "${versioned[@]+"${versioned[@]}"}" \
    "${cross[@]+"${cross[@]}"}" --nologo

published="$here/bin/$configuration/net10.0/$target/publish"
ls -la "$published"

# The name matters on Linux: dlopen, -l and ldconfig all want the lib prefix, and the
# project renames the artifact for exactly that reason.
test -f "$published/libfrisket.so" \
  && echo "ok: libfrisket.so" \
  || { echo "libfrisket.so is missing - the rename target did not run" >&2; exit 1; }
