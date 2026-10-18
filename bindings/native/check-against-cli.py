#!/usr/bin/env python3
"""The native library and the command line, handed the same template and data, must write
the same document.

    python bindings/native/check-against-cli.py <native library> <cli> [<cli> ...] [--keep <dir>]

Each <cli> is either the managed build, Frisket.Cli.dll, run through `dotnet`, or the CLI
linked ahead of time, run as it is - so a release can hold the library, the binary it ships
and the managed build to one document. --keep writes the PDFs into <dir> rather than a
temporary directory, for a comparison made somewhere else (a target emulated in a container,
compared against the host's own render).

A library that loads and returns FK_OK proves nothing (CLAUDE.md 8). What does is the
document: rendered once through the C boundary - here by the Python wrapper, over ctypes -
and once by each CLI, then set side by side with tools/compare-pdf.py, which inflates the
streams first because the two may compress with different zlibs.

Two cases, the same template: the invoice's own data, and data written in accents and
typographic quotes, which is what caught the Pascal wrapper mangling text that never
reached the library. Neither names a font directory, so every side draws with the
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


def command(cli):
    """How to run one CLI: the managed build through dotnet, a linked binary as it is."""
    return ["dotnet", cli] if cli.endswith(".dll") else [os.path.abspath(cli)]


def main(library, clis, out):
    api = frisket._Api(os.path.abspath(library))
    print(f"  library {os.path.basename(library)} {api.version().decode()}")
    failures = 0
    with frisket.Fonts(api) as fonts, frisket.Template(api, path=TEMPLATE) as template:
        for data in CASES:
            name = os.path.splitext(os.path.basename(data))[0]

            native = os.path.join(out, name + ".native.pdf")
            with open(data, encoding="utf-8") as f:
                pdf = template.render(fonts, f.read())
            with open(native, "wb") as f:
                f.write(pdf)

            for i, cli in enumerate(clis):
                # The CLI names its output after the data file and writes it into -o, so
                # each one gets a directory of its own.
                where = os.path.join(out, f"cli{i}")
                subprocess.run(command(cli) + ["render", "-t", TEMPLATE, "-d", data,
                                               "-o", where, "--format", "pdf"],
                               check=True, stdout=subprocess.DEVNULL)
                produced = os.path.join(where, name + ".pdf")

                compared = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "compare-pdf.py"),
                                           native, produced], capture_output=True, text=True)
                verdict = "the same document" if compared.returncode == 0 else "DIFFERENT"
                print(f"  {name}: native {len(pdf)} bytes, {os.path.basename(cli)} "
                      f"{os.path.getsize(produced)} bytes - {verdict}")
                if compared.returncode != 0:
                    print(compared.stdout + compared.stderr)
                    failures += 1

    print("all agree" if not failures else f"{failures} comparison(s) disagree")
    return 1 if failures else 0


if __name__ == "__main__":
    args = sys.argv[1:]
    keep = None
    if "--keep" in args:
        at = args.index("--keep")
        keep = args[at + 1]
        del args[at:at + 2]
    if len(args) < 2:
        sys.exit(__doc__)
    if keep:
        os.makedirs(keep, exist_ok=True)
        sys.exit(main(args[0], args[1:], keep))
    with tempfile.TemporaryDirectory() as out:
        sys.exit(main(args[0], args[1:], out))
