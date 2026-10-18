# Frisket

A document engine for .NET 10, with **no third-party dependencies** — it writes its
own PDF, measures its own type, encodes its own barcodes and brings its own typefaces.
A template is a JSON file, the data is another, and the two are kept apart on purpose.

> **frisket**, *n.* — on a hand press, the frame of oiled paper cut away where the
> impression should fall. It holds the sheet down and keeps ink off everything meant
> to stay clean. From the Old French *frisquette*, from *frisquet*, "fresh, trim":
> the part whose whole job was to bring the sheet out unsmudged.

```bash
frisket render -t invoice.json -d data/ -o out --format pdf,html
```

Invoices are only the obvious case: it is a layout engine, and what it lays out is
whatever the template says. The same document comes out as a PDF or as SVG for a
browser, from the same layout — and the same template and data, given a date, come
out **byte for byte identical** every time, which is the point of the whole thing. The
date is `GeneratedAt` on the report; left out, it is the moment of rendering, because a
PDF records when it was made and that is what the field is for. Across platforms the
*document* is the same and its compressed bytes need not be: the runtime brings its own
zlib, and Ubuntu's .NET uses the system's. `tools/compare-pdf.py` compares two PDFs with
every stream inflated, which is the comparison that means something there.

## What it does

| | |
|---|---|
| **Layout** | flowing or pinned to coordinates, headers and footers, back pages, tables whose rows split across pages, columns that balance, text that justifies |
| **Sections** | a report is a list of sections, each with its own page size — A4 portrait, then an appendix in A3 landscape, in one file |
| **Type** | cap heights measured from the glyph outline — TrueType and CFF alike — never from what a font claims about itself; fonts subset into the file |
| **PDF** | PDF/A-3B and PDF/UA-1, both validated; tagged structure down to `Table`/`TR`/`TH`/`TD`; Factur-X attachments; outlines; interactive forms that work in real readers |
| **Marks** | watermarks drawn as content, so a browser cannot drop them when printing |
| **Codes** | QR and Code 128, generated here |
| **Checks** | a template validator that reports what will go wrong before any document exists |

## The pieces

| | |
|---|---|
| [`engine/`](engine) | the engine. No third-party dependencies, AOT-clean — and under `engine/Fonts` the typefaces it is tested with |
| [`fonts/`](fonts) | `Frisket.Fonts`, which carries four of those families inside an assembly, so a font library nobody filled still draws — in Mulish |
| [`apps/`](apps) | what is built on it: the [CLI](apps/cli/README.md), the [preview server](apps/server/README.md), and the [content editor and template designer](apps/designer/README.md) — one in the flow, one at coordinates |
| [`release/`](release) | the content editor as a single HTML file, generated from its parts by the test suite |
| [`bindings/`](bindings/native/README.md) | the same engine as a shared library behind a flat C interface, with wrappers for C++, Pascal and Python |
| [`examples/`](examples) | the showcase template and data, and the [JSON Schema](examples/schema/README.md) generated from the model |
| [`tests/`](tests/README_TESTS.md) | the test suite, and everything it renders under `tests/output/` |
| [`tools/`](tools) | the typeface specimen sheet, for choosing what to ship |

## Building it

```bash
dotnet build frisket.slnx
dotnet test tests/test.csproj
```

.NET 10, and nothing else. The native library needs a platform linker; see
[`bindings/native/README.md`](bindings/native/README.md).

## The content editor

Beside the engine sits an editor for the common case: somebody writing a letter, a
note or a section of a report, not laying out a form. Blocks one under another —
paragraphs, pictures, tables, rules — a picture beside a paragraph, and mixed styles
within a line. What it produces is a template like any other, which the engine renders.

[`release/frisket-editor.standalone.html`](release/frisket-editor.standalone.html) is
the whole editor in one file: open it in a browser, with nothing beside it. The test
suite generates it from its parts in [`apps/designer/`](apps/designer/), so those are
what to edit, never the file in `release/`.

To put it inside an application, serve the parts together and write one tag:

```html
<frisket-editor paged="false" margins="10"></frisket-editor>
<script src="frisket-core.js"></script>
<script src="frisket-runs.js"></script>
<script src="frisket-editor.js"></script>
<script src="frisket-editor-element.js"></script>
```

```js
const editor = document.querySelector("frisket-editor");
editor.report = draft;                                   // a template, or its JSON
editor.addEventListener("checks", e => show(e.detail));  // what the validator found
save(editor.report);
```

It draws in a shadow root, so the host's styles do not reach in, and its colours are
custom properties a host sets on the tag. Embedded, it shows the editor and nothing
about the machinery: the page size, the fonts and what is being produced belong to the
host. The checks still run on every change — they go to the host instead of onto the
screen. The attributes and what each one decides are listed in
[`apps/designer/README.md`](apps/designer/README.md).

A page may hold as many as it needs — a letter and its footnote, one per item of a list.
Each tag is an editor of its own: its document, its selection, its findings and its
colours. None takes the focus on loading unless it says `autofocus`.
[`apps/designer/frisket-editor-embedded.html`](apps/designer/frisket-editor-embedded.html)
shows two side by side.

## Three ideas worth knowing before reading the code

**A size is a cap height in millimetres, not a point size.** Ask for 3 mm and the
capitals are 3 mm tall in every typeface, because the height is measured from the
outline of an `H`. Fonts declare that height and get it wrong often enough that
believing them printed capitals a fifth too small.

**A template writes only what it decides.** Everything left at its default is absent
from the file, which keeps a template readable and honest about what it actually
chooses. The consequence is that nothing downstream may assume a key is there.

**A font library nobody filled is not empty.** `new FontLibrary()` draws with the
typefaces in `Frisket.Fonts` — Mulish for text that names no family — so a first render
needs nothing but a template. Register one font of your own and the bundled ones step
aside entirely: the two are never mixed, because a name that would quietly have found a
bundled face is a name that should have been refused.

## Checking it

Three ways, never one: the measurements, the bytes of the PDF, and the page itself
opened in a real reader. And a fourth that took longest to learn — comparing the two
outputs against each other. A watermark once turned one way in the SVG and the other
in the PDF; each file was consistent with itself, and no assertion compared them.

`tests/output/conformance/conformance.pdf` is one document carrying everything, so there is one
file to validate rather than a dozen.

```bash
verapdf --flavour 3b  tests/output/conformance/conformance.pdf
verapdf --flavour ua1 tests/output/conformance/conformance.pdf
```

## The example

[`examples/showcase.json`](examples/showcase.json) is that document's template, and it is
plain JSON you can open in the designer or hand to the CLI without building anything:

```bash
frisket render -t examples/showcase.json -d examples/showcase.data.json \
               --conformance pdfa3b --tagged -o out
```

It is an invented field report for an invented company, set in lorem ipsum — a report
rather than an invoice, because the engine lays out whatever the template says and a
sample that looks like accounting suggests otherwise. It carries a repeating header and
footer, flowing prose, a table that breaks across pages with one row too long for any
single page, a mixed-style paragraph, a contact sheet bound from the data, an appendix
spliced in from [`examples/showcase-appendix.json`](examples/showcase-appendix.json), a QR
code and a Code 128, a form with a tick box, a text field and two signatures — one by
hand and one digital — a translucent watermark, and a back page set in two balanced
columns.

The test writes those three files, reads the template back with `Report.FromFile`, and
validates what comes out. So the example cannot drift from the document that is checked:
they are the same document.

## Bringing in a page from another PDF

[`import/`](import/) is a separate assembly that reads one page out of an existing PDF so
it can be copied into a document this engine is writing - a back page of terms drawn by
somebody else, a certificate, a fixed appendix.

```csharp
IImportedPage page = PdfPageSource.Read(File.ReadAllBytes("terms.pdf"), 0);
```

It references the engine and the engine does not reference it, on purpose: everything
else here produces bytes we chose, while a reader consumes bytes somebody else chose, and
a document that imports nothing should not be carrying a parser for the privilege.

In a template it is an `ImportedPageItem`, which lays out like a picture - in a band, in a
cell, at a coordinate, several to a sheet:

```json
{ "$type": "ImportedPageItem", "SourcePath": "terms.pdf", "Page": 2,
  "AltText": "The terms, page 2",
  "Style": { "Location": { "WidthValue": 150, "WidthUnit": "Millimeter" } } }
```

`frisket render` reads it; anything embedding the library calls `ImportedPages.Resolve`
and hands over a reader, since the engine has none and is not to grow one.

**It is a copy, not a rendering.** Nothing in it interprets a content operator or knows
what a font is - the page's marks and the objects they reach travel across as they are,
renumbered into the new file. A page full of JPEG images and CCITT faxes therefore
imports without a line of code for either. Encrypted files are refused rather than
half-read.

**And any import puts a conformance claim in question.** The engine cannot vouch for
bytes it did not write: the page may carry a font that is not embedded, or transparency a
profile forbids. That is reported through `LayoutResult.Issues` and refused by nothing -
the caller knows whether this document was ever meant to be PDF/A, and a report being
assembled to be read has no need of it at all.

Both halves of that are put to a validator rather than assumed. A page out of a
well-formed document costs the claim nothing; a page drawing Helvetica with no font file
behind it costs it everything. veraPDF says both, and it does not know where the page came
from - which is exactly why the engine notes rather than refuses.

## Getting a document signed

Frisket writes signature **fields**; it does not sign. A `FieldItem` with
`Kind = Signature` and `Signing = Digital`, rendered interactive, comes out as an
ordinary unsigned signature field — `/FT /Sig`, a name of its own, a rectangle on a
page, listed in `/AcroForm /Fields` under `/SigFlags 1`. That is the shape a signing
platform looks for, so the usual arrangement needs nothing from this library beyond the
file itself: render, send, wait, receive. The transfer is the host program's, and
deliberately so — the engine never waits for anything (see `CLAUDE.md` §8d).

Four things decide whether that works, and none of them is visible on the page.

**Do not flatten it.** `--flat`, or `Interactive = false`, draws a picture of a place to
sign. The page is identical, every reader opens it, and there is no field for anyone to
fill. `frisket check` has a rule for exactly this; it is the one fault in this area that
announces itself nowhere.

**Name the field.** A platform targets it by name, and the name comes from the template,
so it is stable from one render to the next: an integration can say *sign
`customer_signature`*. Two fields answering to one name are not two fields in a PDF —
they are one field with two appearances, so filling either fills both.

**A signature given by hand is not a field.** `Signing = ByHand` is a ruled box drawn on
the page, for a pen or a reader's own annotation tool. A platform cannot fill it; at best
it places its own mark over it by matching nearby text.

**Check what comes back.** A signature applied as an incremental update leaves the file
underneath it intact. A platform that rewrites the document instead can produce a
perfectly valid signed PDF that has quietly lost the PDF/A claim, the embedded Factur-X
invoice and the whole structure tree. Which of the two a given provider does is a fact
about that provider, and it is discovered by sending it a file and reading what comes
back — not from its documentation.

`NotFitToSign` in `tests/FormsAndSignatures.cs` is that list, written once and meant to
be asked twice: of the document going out, and of the document coming back. Test 89 asks
the first half today. The second is a network call this repository does not make, and the
day a provider is chosen it is the same method, pointed at the bytes it returns.

**Signing in process** — PAdES, where you hold the certificate and the engine writes the
signature itself — is not built. It needs an incremental-update writer, a `/ByteRange`, a
digest over the file minus the reserved hole, and the CMS supplied from outside the core
so its dependency stays out of `Frisket`.

## How this was built

The idea, the direction and every decision about what this should be are mine.
The code was written by an AI — Claude Opus 5 — working to that direction.

## Licence

MIT — see [LICENSE](LICENSE). Do what you like with it; there is no warranty of any
kind.

The four bundled typefaces are **not** covered by that licence — all four are under
the SIL Open Font License, and so is the `Frisket.Fonts` package that carries them.
Each carries its own licence text and copyright notice in its own folder, and those
must travel with it. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Fonts that could not produce a licence were removed rather than shipped on the
strength of a label. "Freeware" means free to use, which is not the same as free to
pass on — one of them said exactly that on its download page while its own licence
file forbade redistribution in as many words.
