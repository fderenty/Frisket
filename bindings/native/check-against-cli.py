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

And what each side says, since 4 November 2026, when the library was found saying less
than the command line: fk_template_check_for against `frisket check --json` - the template
test 42 builds to be wrong asked of three outputs, the one test 97 has a model write asked
with its catalogue, and data that is not JSON - with every family of rule heard at least
once; and fk_render_issues against what `frisket render` prints on standard error, over
the invoice held to a page fewer than it takes. Each side has to say something, or two
silences would agree.

And Zopfli, since 6 November 2026, when both learnt to deflate with it: the invoice through
fk_render with "compression": "zopfli" and through `frisket render --zopfli`, which must be
the same document as each other and as the zlib one - Zopfli changes the size and nothing
else - and smaller than it. Five passes rather than fifteen, since the question is agreement
and not the last byte, and the key that sets them is then asked too. The keys a host can get
wrong are asked to be refused, each in its own words.

And what a field the data leaves out becomes, since 9 November 2026, when both learnt to be
told: the invoice filled from data that answers nothing, through fk_render with "missing":
"blank" and through `frisket render --missing blank` - the same document as each other, and
not the one "show" makes, or the option would have reached neither - and "fail" asked of a
check on both sides, which must name the same field.

Exit code 0 when every case agrees, 1 when one does not.
"""
import json
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

# Built to be wrong by test 42, so a check has something to say; and the template test 97
# has a model write, with a property misspelt and fields its catalogue does not hold.
FAULTY = os.path.join(ROOT, "tests", "output", "validator", "faulty.template.json")
MODEL = os.path.join(ROOT, "tests", "output", "model", "written.template.json")
CATALOGUE = os.path.join(ROOT, "tests", "output", "model", "fields.json")

# Every family a check can speak in, each of which some case below must reach: a branch of
# fk_template_check_for that no case reaches is one the comparison would pass without.
FAMILIES = {"read", "sound", "can", "fields", "data"}


def check_cases(out):
    """The same question asked both ways: what the library is handed, and the CLI's flags."""
    unreadable = os.path.join(out, "unreadable.data.json")
    with open(unreadable, "w", encoding="utf-8") as f:
        f.write("{ not json")
    silent = os.path.join(out, "silent.data.json")
    with open(silent, "w", encoding="utf-8") as f:
        f.write("{}")
    with open(CATALOGUE, encoding="utf-8") as f:
        catalogue = json.load(f)

    return [
        ("(defaults)", FAULTY, {}, []),
        ("--tagged --accessible", FAULTY, {"tagged": True, "accessible": True}, ["--tagged", "--accessible"]),
        ("--flat", FAULTY, {"interactive": False}, ["--flat"]),
        ("--fields, a model's template", MODEL, {"fields": catalogue}, ["--fields", CATALOGUE]),
        ("-d, data that is not JSON", TEMPLATE, {"data": "{ not json"}, ["-d", unreadable]),
        ("-d --missing fail, data that answers nothing", TEMPLATE, {"data": "{}", "missing": "fail"},
         ["-d", silent, "--missing", "fail"]),
    ]


def command(cli):
    """How to run one CLI: the managed build through dotnet, a linked binary as it is."""
    return ["dotnet", cli] if cli.endswith(".dll") else [os.path.abspath(cli)]


def run(args):
    return subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")


def compare_checks(api, cli, out):
    """fk_template_check_for and `frisket check --json`, finding for finding."""
    failures = 0
    heard = set()
    for label, path, options, flags in check_cases(out):
        with frisket.Template(api, path=path) as template:
            native = template.check_for(**options)
        said = json.loads(run(command(cli) + ["check", "-t", path, "--json"] + flags).stdout)
        heard |= {issue["rule"].split(".")[0] for issue in native}

        agree = native == said and len(native) > 0
        print(f"  check {label}: native {len(native)}, {os.path.basename(cli)} {len(said)} - "
              f"{'the same findings' if agree else 'DIFFERENT'}")
        if not agree:
            for issue in [i for i in native if i not in said]:
                print(f"      only native: {issue['rule']} {issue['where']}")
            for issue in [i for i in said if i not in native]:
                print(f"      only cli:    {issue['rule']} {issue['where']}")
            failures += 1

    unheard = FAMILIES - heard
    if unheard:
        print(f"  check: no case made the library say anything in {sorted(unheard)}")
        failures += 1

    # And the older check, which asks of the template alone: what the fuller one says about
    # the template itself - neither the text nor the output - in the same shape, pointer and
    # all. It wrote a shape of its own until 4 November 2026.
    with frisket.Template(api, path=FAULTY) as faulty:
        alone = faulty.check(accessible=True)
        fuller = [i for i in faulty.check_for(accessible=True, tagged=True)
                  if i["rule"].split(".")[0] not in ("read", "can")]
    agree = alone == fuller and len(alone) > 0
    print(f"  fk_template_check: {len(alone)}, the template's own part of the fuller check "
          f"{len(fuller)} - {'the same findings' if agree else 'DIFFERENT'}")
    return failures + (0 if agree else 1)


def compare_render_issues(api, fonts, cli, out):
    """fk_render_issues and what `frisket render` prints on standard error, over the
    invoice told it may have one page fewer than it takes - a limit it cannot keep."""
    data = CASES[0]
    with open(data, encoding="utf-8") as f:
        record = f.read()
    with frisket.Template(api, path=TEMPLATE) as template:
        pages = len(template.render(fonts, record, format="svg")["pages"])

    with open(TEMPLATE, encoding="utf-8") as f:
        held = json.load(f)
    held["Sections"][0]["PageLimit"] = pages - 1
    limited = os.path.join(out, "limited.template.json")
    with open(limited, "w", encoding="utf-8") as f:
        json.dump(held, f, ensure_ascii=False)

    with frisket.Template(api, path=limited) as template:
        _, native = template.render_with_issues(fonts, record)

    printed = run(command(cli) + ["render", "-t", limited, "-d", data,
                                  "-o", os.path.join(out, "limited"), "--format", "pdf"]).stderr
    # "frisket: rule, where: message" - the place may lose a character to the console's
    # code page on Windows, so the rule and the message are what is held to each other.
    said = sorted((line.split(",", 1)[0][len("frisket: "):], line.split(": ", 2)[2])
                  for line in printed.splitlines() if line.startswith("frisket: "))
    mine = sorted((i["rule"], i["message"]) for i in native)
    agree = mine == said and any(rule == "fits.pageLimit" for rule, _ in mine)
    print(f"  render issues: native {[r for r, _ in mine]}, {os.path.basename(cli)} "
          f"{[r for r, _ in said]} - {'the same warnings' if agree else 'DIFFERENT'}")
    return 0 if agree else 1


def same_document(one, other):
    """tools/compare-pdf.py's verdict on two files, and what it said when it was no."""
    compared = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "compare-pdf.py"), one, other],
                              capture_output=True, text=True)
    return compared.returncode == 0, compared.stdout + compared.stderr


def compare_zopfli(template, fonts, cli, out):
    """The invoice deflated by Zopfli on both sides: the same document as each other and as
    zlib's, and smaller - then the options a host can get wrong, refused."""
    data = CASES[0]
    name = os.path.splitext(os.path.basename(data))[0]
    with open(data, encoding="utf-8") as f:
        record = f.read()

    zlib = os.path.join(out, name + ".native.pdf")
    native = os.path.join(out, name + ".zopfli.native.pdf")
    with open(native, "wb") as f:
        f.write(template.render(fonts, record, compression="zopfli", iterations=5, threads=-1))

    where = os.path.join(out, "zopfli-" + os.path.basename(cli))
    subprocess.run(command(cli) + ["render", "-t", TEMPLATE, "-d", data, "-o", where, "--format", "pdf",
                                   "--zopfli", "--zopfli-iterations", "5"],
                   check=True, stdout=subprocess.DEVNULL)
    produced = os.path.join(where, name + ".pdf")

    failures = 0
    for label, one, other in [(f"native and {os.path.basename(cli)}, both Zopfli", native, produced),
                              ("Zopfli and zlib, native", native, zlib)]:
        same, said = same_document(one, other)
        print(f"  zopfli, {label}: {os.path.getsize(one)} and {os.path.getsize(other)} bytes - "
              f"{'the same document' if same else 'DIFFERENT'}")
        if not same:
            print(said)
            failures += 1

    if os.path.getsize(native) >= os.path.getsize(zlib):
        print(f"  zopfli: {os.path.getsize(native)} bytes is not smaller than zlib's {os.path.getsize(zlib)}")
        failures += 1

    malformed = [({"compression": "lzma"}, "unknown compression"),
                 ({"iterations": 5}, "goes with"),
                 ({"compression": "zopfli", "iterations": 0}, "at least 1"),
                 ({"threads": 0}, "-1 or at least 1")]
    wrong = 0
    for options, said in malformed:
        try:
            template.render(fonts, record, **options)
            print(f"  zopfli: {options} was taken")
            wrong += 1
        except frisket.FrisketError as refused:
            if said not in str(refused):
                print(f"  zopfli: {options} was refused, but not for its fault: {refused}")
                wrong += 1
    if not wrong:
        print(f"  zopfli: {len(malformed)} malformed options refused, each for its own fault")
    return failures + wrong


def compare_missing(template, fonts, cli, out):
    """The invoice filled from data that answers nothing, its tokens left blank on both sides:
    one document, and not the one left showing them - then a word neither knows, refused."""
    silent = os.path.join(out, "silent.data.json")
    with open(silent, "w", encoding="utf-8") as f:
        f.write("{}")

    shown = os.path.join(out, "silent.shown.native.pdf")
    with open(shown, "wb") as f:
        f.write(template.render(fonts, "{}"))
    blank = os.path.join(out, "silent.blank.native.pdf")
    with open(blank, "wb") as f:
        f.write(template.render(fonts, "{}", missing="blank"))

    where = os.path.join(out, "missing-" + os.path.basename(cli))
    subprocess.run(command(cli) + ["render", "-t", TEMPLATE, "-d", silent, "-o", where, "--format", "pdf",
                                   "--missing", "blank"],
                   check=True, stdout=subprocess.DEVNULL)
    produced = os.path.join(where, "silent.data.pdf")   # named after the data file, less its extension

    failures = 0
    same, said = same_document(blank, produced)
    print(f"  missing blank, native and {os.path.basename(cli)}: {'the same document' if same else 'DIFFERENT'}")
    if not same:
        print(said)
        failures += 1

    differs, _ = same_document(blank, shown)
    print(f"  missing blank and show, native: {'THE SAME' if differs else 'different documents'}")
    if differs:
        failures += 1

    try:
        template.render(fonts, "{}", missing="blanc")
        print("  missing: 'blanc' was taken")
        failures += 1
    except frisket.FrisketError as refused:
        if "show, blank or fail" not in str(refused):
            print(f"  missing: 'blanc' was refused, but not for its fault: {refused}")
            failures += 1
        else:
            print("  missing: a word it does not know is refused")
    return failures


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

        for cli in clis:
            failures += compare_checks(api, cli, out)
            failures += compare_render_issues(api, fonts, cli, out)
            failures += compare_zopfli(template, fonts, cli, out)
            failures += compare_missing(template, fonts, cli, out)

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
