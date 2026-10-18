#!/usr/bin/env python3
"""The native library and the command line, handed the same template and data, must write
the same document.

    python bindings/native/check-against-cli.py <native library> <Frisket.Cli.dll>

A library that loads and returns FK_OK proves nothing (CLAUDE.md 8). What does is the
document: rendered once through the C boundary - here by the Python wrapper, over ctypes -
and once by the CLI, then set side by side with tools/compare-pdf.py, which inflates the
streams first because the two may compress with different zlibs.

Two cases, the same template: the invoice's own data, and data written in accents and
typographic quotes, which is what caught the Pascal wrapper mangling text that never
reached the library. Neither names a font directory, so both sides draw with the
typefaces bundled with Frisket - the case nobody configures, and the one that ships.

Exit code 0 when every case agrees, 1 when one does not.
"""
import os
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "bindings", "native", "wrappers", "python"))
import frisket  # noqa: E402

TEMPLATE = os.path.join(ROOT, "tests", "output", "acme", "acme.template.json")
CASES = [
    os.path.join(ROOT, "tests", "output", "acme", "acme.data.json"),
    os.path.join(ROOT, "bindings", "native", "samples", "accented.data.json"),
]


def main(library, cli):
    api = frisket._Api(os.path.abspath(library))
    failures = 0
    with tempfile.TemporaryDirectory() as out, frisket.Fonts(api) as fonts, \
            frisket.Template(api, path=TEMPLATE) as template:
        for data in CASES:
            name = os.path.splitext(os.path.basename(data))[0]

            native = os.path.join(out, name + ".native.pdf")
            with open(data, encoding="utf-8") as f:
                pdf = template.render(fonts, f.read())
            with open(native, "wb") as f:
                f.write(pdf)

            # The CLI names its output after the data file and writes it into -o.
            subprocess.run(["dotnet", cli, "render", "-t", TEMPLATE, "-d", data,
                            "-o", out, "--format", "pdf"], check=True,
                           stdout=subprocess.DEVNULL)
            managed = os.path.join(out, name + ".pdf")

            compared = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "compare-pdf.py"),
                                       native, managed], capture_output=True, text=True)
            verdict = "the same document" if compared.returncode == 0 else "DIFFERENT"
            print(f"  {name}: native {len(pdf)} bytes, CLI {os.path.getsize(managed)} bytes - {verdict}")
            if compared.returncode != 0:
                print(compared.stdout + compared.stderr)
                failures += 1

    print("all agree" if not failures else f"{failures} case(s) disagree")
    return 1 if failures else 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    sys.exit(main(sys.argv[1], sys.argv[2]))
