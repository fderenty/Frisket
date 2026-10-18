#!/usr/bin/env bash
# Installs veraPDF for tests 53 and 92, and points FRISKET_VERAPDF at it.
#
# Without it both tests declare themselves Skipped - honest on a developer's machine, and
# exactly the silent loss a CI run must not have, so the workflow also refuses any skip it
# did not expect. The version is the one the suite has been validated against on Windows
# and in WSL-Frisket, and the download is checked against its hash: a check whose checker
# can change underneath it is not the same check from one run to the next.
#
# On a runner: RUNNER_TEMP, RUNNER_OS and GITHUB_ENV come from GitHub Actions.
set -euo pipefail

version=1.30.2
sha256=6cc6341cb1af644044054b81f00a6590a7918abb18f762243de115258bcad838
url="https://software.verapdf.org/releases/${version%.*}/verapdf-greenfield-${version}-installer.zip"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Bash wants a path it can read; the installer and .NET, on Windows, want a Windows one.
if [ "${RUNNER_OS:-}" = Windows ]; then
  temp="$(cygpath -u "${RUNNER_TEMP:?}")"
  target="$(cygpath -m "$temp/verapdf")"
  launcher="$target/verapdf.bat"
else
  temp="${RUNNER_TEMP:?}"
  target="$temp/verapdf"
  launcher="$target/verapdf"
fi

work="$temp/verapdf-download"
rm -rf "$work" && mkdir -p "$work"
curl -sSfL --retry 3 -o "$work/installer.zip" "$url"
echo "$sha256  $work/installer.zip" | sha256sum -c -
unzip -q "$work/installer.zip" -d "$work"

sed "s|INSTALL_PATH|$target|" "$here/verapdf-auto-install.xml" > "$work/auto-install.xml"
java -jar "$work/verapdf-greenfield-${version}/verapdf-izpack-installer-${version}.jar" \
     "$work/auto-install.xml"

"$launcher" --version
echo "FRISKET_VERAPDF=$launcher" >> "${GITHUB_ENV:?}"
