# frisket

Applies a template to data and writes what comes out. The engine keeps the two apart
on purpose; this is the smallest thing that puts them back together from a shell, so
a template can be looked at without a test being written for it, and a folder of
records becomes a folder of sections with no code at all.

```bash
dotnet build apps/cli/Frisket.Cli.csproj

cli/bin/Debug/net10.0/Frisket.Cli.exe render \
    -t note.json -d data/ -o out --format pdf,html
```

## The three commands

| | |
|---|---|
| `render` | one document per data file, named after it. With no `-d`, the template is rendered as it stands - which is how a template is looked at while it is being drawn |
| `check`  | the template validator, with nothing produced. Exits non-zero on an error, so it belongs in a build as much as in a shell. It reads the text first: a property no element carries is reported rather than skipped. `--fields catalogue.json` checks every token against what the host's data will hold, `-d` asks what the data itself brings - a chart the PDF would refuse, which the template knows only by name - and `--json` writes the findings for a program, each with a JSON Pointer to where to act - see [`examples/ai`](../../examples/ai/README.md) |
| `fonts`  | what the library holds and what the engine measured in it - the typefaces bundled with Frisket unless `--fonts` or `--font` names others. A template names families by name, and a name that does not resolve is refused, with the list of what does |

`-d` takes a file or a directory; a directory stands for the `*.json` inside it.

## Worth knowing

`--conformance pdfa3b --tagged` produces the archivable, readable-aloud form. Without
`--conformance` the file claims nothing, and a validator falls back to PDF/A-1b -
which forbids things the engine now writes, transparency first among them.

`--flat` draws form fields instead of making them fill-in, for a section meant to be
printed and written on rather than typed into.

`--missing` says what a token the data has no answer for becomes. `show`, the default,
leaves `{Ref}` on the page, so a typo shows instead of hiding. `blank` writes nothing in
its place, for data that leaves out what a record does not have rather than writing every
field on every line. `fail` refuses the document and names the field. `check -d` reads it
too, where `fail` says which field the data lacks.

`--zopfli` deflates the PDF with [Zopfli](https://github.com/fderenty/ZopfliSharp) rather
than zlib: the same document, which every reader inflates exactly as before, and smaller -
6.6% off the ACME invoice, whose weight is its logo, and 11.5% off the showcase, a report
of fonts, pictures, a form and a structure tree. It takes about a hundred times zlib's time,
spread over every core: 1.5 s for the invoice and 9 s for the showcase on eight, against a
few hundredths with zlib. Worth it for a file written once and kept or sent many times - an
archive, a batch of invoices - and not while somebody waits. `--zopfli-iterations <n>` sets
how many passes it makes over each stream, 15 by default; fewer is faster and a little
larger, and on these documents 5 costs a few bytes.

This is the one thing the command line carries that the engine does not: ZopfliSharp, under
the Apache License 2.0, whose licence and notice travel beside the binary in
`licenses/zopflisharp/` (see [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md)).

The assembly is `Frisket.Cli`, not `frisket`: Windows matches file names
without regard to case, so an `frisket.exe` beside `Frisket.dll` makes the
project reference ambiguous and restore refuses it.
