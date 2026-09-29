# The native library

`frisket.dll` / `libfrisket.so` — the engine compiled ahead of time, behind a
flat C interface. No runtime to install, nothing managed beside it, one file.

The contract is [`frisket.h`](frisket.h). Fifteen entry points, and three kinds
of thing crossing: null-terminated UTF-8 strings, opaque handles, status codes.

## Why it is shaped like this

**No struct is passed by value.** Agreeing on field order, padding and enum width
across C, Pascal, Python and whatever comes next is a decade of small mistakes, and a
struct can never gain a field without breaking every caller at once. Options travel as
JSON instead — one parse per section, nothing beside laying out a page, and a caller
compiled last year still works.

**No callback is taken.** A callback means the foreign side has to get a calling
convention right *and* survive being re-entered from a garbage-collected runtime.

**Every export catches.** An exception reaching the boundary ends the process with
nothing to read, so the rule is absolute rather than a matter of care. Failures come
back as a status, with `fk_last_error()` holding the reason, per thread.

**The library owns what it hands out.** Anything returned through an out parameter is
freed with the matching `er_*_free`. A pointer into managed memory would be a pointer
the collector is free to move while C is reading it.

## Building it

One script per side. Both encode what is not obvious, so it need not be rediscovered.

```powershell
./bindings/native/build.ps1 -Target win-x64      # or win-arm64
```

```bash
./bindings/native/build.sh                       # linux-x64
./bindings/native/build.sh linux-arm64
```

| Target | State |
|---|---|
| `win-x64` | built and run, and the section compared |
| `linux-x64` | built and run, in WSL, and the section compared |
| `win-arm64` | provided for, **never built**: needs the ARM64 C++ build tools, a separate Visual Studio component |
| `linux-arm64` | provided for, **never built**: needs a cross linker and an arm64 sysroot, or an ARM64 machine |

The two ARM targets are one line of script each and nothing more is expected of them,
but neither has been compiled, let alone run, so they are listed as untried rather
than as supported.

### What the scripts know that a plain `dotnet publish` does not

ILCompiler locates its linker through `vswhere.exe`. Where that is missing — as it is
on this machine — the way through is to load the C++ environment yourself and pass
`-p:IlcUseEnvironmentalTools=true`, which tells it to use what is already on `PATH`.

.NET names a shared library after the assembly, so Linux gets `frisket.so` where
every convention there — `dlopen`, `-l`, `ldconfig` — expects the `lib` prefix. The
project renames it, hooked **after `CopyNativeBinary`** rather than after `Publish`:
the native artifact is copied into the publish directory by that later target, so
renaming any earlier just gets the old name put back.

Note that the project **compiles the engine's sources in** rather than referencing the
project: what ships is one native file, so a second assembly buys nothing — and
referencing one would collide anyway, since Windows matches file names without regard
to case and this library has to be called `frisket`.

## Publishing

The settings live in `Properties/PublishProfiles/<target>.pubxml`, which Visual
Studio's Publish dialog and the command line read alike - one file rather than a
script and a dialog agreeing to say the same thing:

```bash
dotnet publish bindings/native/Frisket.Native.csproj -p:PublishProfile=win-x64
./bindings/native/build.ps1 -Target win-x64      # the same, plus the toolchain
./bindings/native/build.sh  linux-x64            # on Linux, or in WSL
```

One thing is worth knowing before it costs an hour. ILCompiler finds the linker by
running `findvcvarsall.bat`, which calls `vcvarsall.bat` and takes the **first line of
its output** as the path to `link.exe`. Where `vswhere.exe` is not reachable,
`vcvarsall` still works but prints a complaint first - and the complaint becomes the
linker path. The build then fails naming `'vswhere.exe' is not recognized` as a
program, which says almost nothing about what is actually wrong.

`vswhere.exe` sits beside the installer and is not on `PATH` by default. Putting it
there is enough - no `vcvars`, no `IlcUseEnvironmentalTools` - and it is also what
makes the Publish button work from inside Visual Studio, whose process reaches its own
installer.

`win-arm64` was published for the first time on 20 August 2026 and has **never been
run**: there is no ARM machine here. Built is not verified, which is the whole subject
of the next section.

## A font the caller chose

Three doors, and they differ on purpose:

| | |
|---|---|
| `fk_fonts_add_directory` | every font in a directory; a file that is not one is stepped over |
| `fk_fonts_add_file` | one file, named on purpose; a failure comes back |
| `fk_fonts_add_memory` | one font from its bytes - posted, stored, picked in a browser |

The difference between the first and the other two is the whole point. A scan was never
asked about any file in particular, so shrugging is right. A font somebody named is a
font somebody wants, and skipping it in silence leaves them looking at a list it is not
in with nothing to say why.

**Parsing is the check.** There is no cheaper one worth having. A header sniff proves
the first twelve bytes look like a font; getting `FK_OK` back proves the tables are
there, the cmap reads and the `H` can be measured - which is what the engine will
actually need, and the reason section 3 of CLAUDE.md exists. A second, weaker reader
run first could only agree with this one or contradict it, and the contradiction is the
interesting case: a font declared good and then refused, or refused and perfectly
usable.

Whether the font may lawfully be embedded is **not asked**. It is the caller's font and
the caller's decision. What this project owes is not to *ship* one it has no right to,
which is a different promise and one it keeps.

## Stopping a render

`fk_render` does not return until it is finished, so a caller cannot check anything
while it runs. The word has to come from somewhere else:

```c
fk_token token;
fk_token_create(&token);
/* ... another thread ... */  fk_cancel(token);

fk_status status = fk_render(tmpl, fonts, data, options, token, &out);
/* status == FK_CANCELLED, and out is untouched */
fk_token_free(token);
```

The token is its own handle rather than a flag on the template, and deliberately: it
belongs to one operation. Hung on a template, cancelling one render would cancel the
next render that happened to use it - a fault nobody would look for, since the second
call did nothing wrong. Pass `NULL` for a render that cannot be cancelled, which is
what every other example here does.

A cancelled render **produces nothing**. A PDF cut short still opens like a PDF, which
is the one failure that does not announce itself, so there is no partial result to
choose between.

`FK_CANCELLED` is not `FK_FAILED`, and the difference is worth honouring: a caller
retries a failure and does not retry a cancellation. Folded together, the first timeout
on a busy server becomes a storm.

Nothing here is asynchronous, and nothing needs to be. The engine never waits - it
computes - so cancelling means asking, in the loops that last, whether the answer is
still wanted. What belongs to the host rather than to this library: bounding the size
of what it is handed, how many renders run at once, and how long one may take.
`Report.Resolve()` composes without drawing, so a page count can be known before a
document is paid for.

## Checking it works

Two programs, in the two languages that matter most here, both of which write the PDF
out so it can be compared with what the command line produces from the same inputs.

```bash
# C
cl /nologo /W4 /O2 samples\smoke.c /Fe:<publish>\smoke.exe /link <publish>\..\native\frisket.lib

# Pascal
fpc -O2 -FE<publish> wrappers\pascal\smoke.pas
```

```bash
smoke <font-dir> <template.json> <data.json> <out.pdf> [font-file]
```

The fifth argument is optional and exercises the two doors above: the file named on
purpose, the same font as bytes, and what is not a font coming back as a failure.

Two things that cost time when building these on Windows, both of which show up as a
linker error naming the C runtime rather than the cause:

- `vcvarsall.bat` gives up **in silence** when the working directory is on a drive it
  cannot cope with. `cd` to the source drive before calling it;
- `LIB` is the linker's search path. A script that sets `LIB` to its own import
  library wipes it out, and the error that comes back is `LNK1104: cannot open file
  'LIBCMT.lib'` - which is the CRT, and a long way from what happened.

A library that loads and returns `FK_OK` is not evidence. A section identical to the
one the CLI produces is — and getting there found something.

The same sources build and run under Linux with `cc`/`c++`, against
`libfrisket.so`.

### `cmp` no longer answers the question

It did once: four binaries across two operating systems and three languages produced
the same 19 015 bytes, differing only inside the timestamps and the file `/ID`.

That is no longer true, and the reason is not a fault. The native library links the
**system's zlib**; the managed CLI carries **its own**. The same bytes therefore come
out compressed differently - identical content, different length. Measured on
20 August 2026, from `tests/output/acme/acme.template.json`:

| | |
|---|---|
| C, Linux | 57 960 bytes |
| C++, Linux | 57 960 bytes, identical to C byte for byte |
| Python over ctypes, Windows | 57 960 bytes, the same document |
| CLI, Windows | 57 960 bytes, the same document |
| CLI, Linux | 57 909 bytes - **51 fewer**, and still the same document |

Windows agrees with itself because the AOT build there carries the same compressor the
runtime does. Linux is where the two part: the native build links the system's zlib.

`/Length1` was identical on both sides, which is the tell: the font subset *before*
compression is the same size, so nothing about the document changed. Only `/Length`,
the compressed size, moved.

Read with `cmp`, that looks exactly like a broken boundary. So the comparison inflates
the streams first, and neutralises the four things allowed to differ - the timestamp,
the `/ID`, the stream lengths, and the xref offsets that follow from them:

```bash
python tools/compare-pdf.py from-c.pdf from-cli.pdf
```

Which says, for the run above: 7/7 streams identical once inflated, structure
identical, exit 0. It has been seen to say no three ways - a different document, a
byte changed in the structure, and a stream re-deflated with one byte moved inside it,
which is the subtle one: same length, same structure, different content.

## What the Pascal wrapper taught us

The first version took a `string`, called `UTF8Encode`, and handed the result over. On
Free Pascal in its default mode `string` is an AnsiString in the system code page, so
every accent outside CP1252 was gone before the library saw it.

**Nothing failed.** The section came out, every check went green, and the only sign
was that the PDF was 380 bytes larger than the same one produced from C: the mangled
characters needed more glyphs, so the font subset grew.

Hence the rule the unit now follows — `UTF8String` end to end for anything with
content in it, `string` only for names — and hence the byte comparison, which is the
check that actually caught it.

The same trap bites the test file itself: a literal with an accent in a `.pas` is read
in the system code page unless the file says `{$CODEPAGE UTF8}`.

## Wrappers

| | |
|---|---|
| [`wrappers/cpp`](wrappers/cpp) | A header, and nothing to build. Every handle lives in an object that frees it on the way out of the block - including when an exception is unwinding through it |
| [`wrappers/pascal`](wrappers/pascal) | Free Pascal and Delphi. Late-bound, nothing to link. Compiles clean on FPC 3.2.2 |
| [`wrappers/python`](wrappers/python) | ctypes. Also the cheapest proof that late binding works at all, which is what every other language does |

C needs no wrapper — it has the header.

Each sample writes its PDF out, and the three files agree byte for byte apart from
the timestamp and the file `/ID`. That comparison is not ceremony: it is what caught
the Pascal wrapper corrupting accented data, and it is what caught the C++ header
turning a value outside its own enum into a perfectly good SVG.
