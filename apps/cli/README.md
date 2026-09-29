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
| `check`  | the template validator, with nothing produced. Exits non-zero on an error, so it belongs in a build as much as in a shell |
| `fonts`  | what the library holds and what the engine measured in it. A template names families by name, and a name that does not resolve renders silently in the fallback face |

`-d` takes a file or a directory; a directory stands for the `*.json` inside it.

## Worth knowing

`--conformance pdfa3b --tagged` produces the archivable, readable-aloud form. Without
`--conformance` the file claims nothing, and a validator falls back to PDF/A-1b -
which forbids things the engine now writes, transparency first among them.

`--flat` draws form fields instead of making them fill-in, for a section meant to be
printed and written on rather than typed into.

The assembly is `Frisket.Cli`, not `frisket`: Windows matches file names
without regard to case, so an `frisket.exe` beside `Frisket.dll` makes the
project reference ambiguous and restore refuses it.
