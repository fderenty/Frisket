# Frisket Test Suite Documentation

## Overview
Comprehensive test suite for validating the Frisket layout engine's positioning, sizing, anchoring, and constraint systems.

## Test Results
**The count is not written here, on purpose.** It was, and it drifted: this file said
72 cases while the suite ran 93, and the table below had eight of its counts wrong and
three files missing. `dotnet test` prints the number, which is the one place it cannot
be out of date.

It once went *down*, on 4 August 2026: one of the 55 tests of the time could not fail, and
removing it is the reason the number was smaller and the suite stronger.
See "What the tests write" below.

## Where a test lives

Split by subject on 24 August 2026. Until then the whole suite was two files -
`Program.cs` and `AcmeInvoice.cs` - and the second was 8,160 lines holding everything
from font fuzzing to the JSON schema. The name sent people looking elsewhere: the
report that started this was somebody unable to find the test for a feature they had
asked for the day before.

Every file below is `public partial class EngineTests`, so **placement costs nothing**:
one class, one xUnit collection, run in series because the tests write into the same
directories under `tests/output/`. Nothing is hidden from anything, and the only
question a file name answers is where a person would look.

| file | what is in it |
|---|---|
| `AcmeInvoice.cs` | the invoice fixture the suite is built on, and the whole-document run over it |
| `AdvancedTextAndColumns.cs` | mixed-style runs on one baseline, justification, and columns that fill then balance |
| `Barcodes.cs` | QR and Code 128 held to their published standards rather than to themselves |
| `BindingAndTokens.cs` | tokens and formats, scope and its fallback, show/hide, mail merge, heading numbers |
| `Checks.cs` | what frisket check answers: the three profiles, and the rules that must fall silent |
| `EngineTests.cs` | the assertion, the paths, the small builders - and no tests, deliberately |
| `Fonts.cs` | cap-height sizing and the cascade, measured metrics, family names, a font as bytes, and the typefaces a library nobody filled draws with |
| `FormsAndSignatures.cs` | the quote fillable or flattened, field names that stay unique, a control inside a table cell, and signature fields fit to hand to a signing platform |
| `MailOutput.cs` | --format mail: flowing HTML with no pages, the inverse of the SVG proof |
| `NativeBoundary.cs` | the published native libraries export every entry point the C header declares |
| `PagesBandsAndSections.cs` | bands, the flow across pages, breaks, keeping a block whole or with what follows, back pages, and sections with a page size of their own |
| `PdfImport.cs` | a page read out of another PDF, drawn as an item, and what it costs a conformance claim |
| `PdfOutput.cs` | the written PDF read back: structure, subset fonts, PDF/A-3B, Factur-X, veraPDF |
| `Placement.cs` | where an item lands in the box it was given: units, constraints, margins, alignment |
| `Robustness.cs` | bytes and callers the engine does not trust: bounded counts, fuzzing, cancellation |
| `Standalone.cs` | each editor face built into one HTML file, with nothing left pointing outside it |
| `SvgOutput.cs` | SVG pages and fragments - the engine's proof of what the printer will do |
| `TablesGridsAndSubTemplates.cs` | tables and their columns, a split row, group bands, image grids, and sub-templates - spliced or appended, and held to their root |
| `TemplateOnDisk.cs` | what a template writes down and reads back, plus the schema and reference page |
| `WatermarkBackdropAndPaper.cs` | what is laid over and behind the page, confronted between the SVG and the PDF |

## Built-in Tests

### Test 1: Absolute Positioning (Millimeters)
**Purpose**: Validates basic absolute positioning using millimeter units
- **Input**: Left=10mm, Top=20mm, Width=50mm, Height=30mm
- **Expected**: Item positioned at (10, 20) with size 50×30
- **Status**: passing

### Test 2: Percentage Positioning
**Purpose**: Validates percentage-based positioning relative to parent
- **Input**: Left=5%, Top=15mm, Width=40mm, Height=30mm
- **Expected**: Left position = 10.5mm (5% of 210mm section width)
- **Status**: passing

### Test 3: Anchor Layouts
**Purpose**: Tests anchor-based layouts for Header/Body/Footer
- **Scenario**: Header (Top+Height), Body (Client fill), Footer (Bottom+Height)
- **Expected**: 
  - Header: Full width, 50mm height at top
  - Body: Full width and height (fills section)
  - Footer: Full width, 50mm height at bottom
- **Status**: passing

### Test 4: Constraints
**Purpose**: Validates min/max width and height constraints
- **Input**: Width=150mm, Height=80mm with MaxWidth=100, MaxHeight=50
- **Expected**: Dimensions clamped to 100×50mm
- **Status**: passing

### Test 5: Visibility States
**Purpose**: Tests Visible, Invisible, and Gone visibility states
- **Scenarios**:
  - Visible: Normal rendering with calculated positions
  - Gone: Not rendered, positions not calculated (NaN)
  - Invisible: Rendered but not visible (positions calculated)
- **Status**: passing

### Test 6: Mixed Units (MM + Percentage)
**Purpose**: Validates mixing millimeter and percentage units
- **Input**: Left=10%, Width=50%, Top=15mm, Height=20mm
- **Expected**: 
  - Left = 21mm (10% of 210)
  - Width = 105mm (50% of 210)
- **Status**: passing

## What the tests write

Everything the suite renders goes under `tests/output/`, one directory per subject -
`acme/`, `conformance/`, `quote/`, `sections/`, `validator/` and the rest. The tree is
anchored on the repository root, walked up from the binary rather than from the working
directory, so it is the same tree whichever directory the run started in.

Some of it is read back by other checks rather than merely looked at: the designer
harness reads `acme/acme.bound.json` and `acme/acme.bands.json`, and the two validator
fixtures in `validator/` are what its echo is held to. Those are not artefacts, they are
the interface between the engine and the page.

A note on what used to be here. This file described a `TestReports/` directory of JSON
expectation files - `ExpectedLeft`, `ExpectedTop`, a tolerance - loaded by a
`Test34_JsonDefinedReports`. All of it was removed on 4 August 2026, because that test
could not fail: it looked for the directory beside the repository root while the files
sat under `tests/`, so it printed "TestReports directory not found" and returned green;
had it found them it only deserialised and printed a count, never comparing anything;
and it swallowed every exception, so a malformed file passed too. The expectations
themselves restated what tests 1 to 5 already assert in C#, which made them a second
place to keep in sync rather than a check. The suite went from 55 to 54 and got more
truthful for it.

## Features Tested

- **Absolute positioning** (mm units)
- **Percentage-based positioning** (% units)
- **Anchor systems** (Left, Top, Right, Bottom, Client)
- **Width/Height constraints** (Min/Max)
- **Visibility states** (Visible, Invisible, Gone)
- **Mixed units** (mm + % in same report)
- **Parent-relative calculations**
- **Auto-sizing** (when anchors but no explicit sizes)

## Running Tests

```bash
dotnet test tests/test.csproj
```

One test at a time, which is what you want while chasing one down:

```bash
dotnet test tests/test.csproj --filter FullyQualifiedName~Test30_FormFields
```

The suite writes its artifacts as it runs - PDFs, SVGs, the font gallery - under
`tests/output/` at the repository root, wherever the run was started from. Reading
those has caught more defects than the assertions have, so they are not a
by-product.

## Test Output Format

Assertions are silent when they pass; a failure names the property, the value and
the expected one. The running commentary each test prints - the font metrics
table, the field counts, where each file went - needs asking for:

```bash
dotnet test tests/test.csproj -l "console;verbosity=detailed"
```

A test that throws now fails alone. Under the previous hand-rolled runner a single
exception - a PDF left open in a reader was enough - aborted the whole run and
reported nothing.

## Next Steps

To extend the test suite:
1. Add the `[Fact]` to the file whose name names the subject, and add a new file if
   none does - `namespace test`, `public partial class EngineTests`, and nothing else
   needed. `EngineTests.cs` holds no tests on purpose: a file named after the class is
   where unclassified tests go to be lost
2. Assert with `AssertEqual(name, property, expected, actual, tolerance)`; the property
   is what makes a failure readable
3. Then break the thing you fixed and confirm the test goes red. A check nobody has
   seen fail is a check nobody should trust - CLAUDE.md §2

## Template and data

A template holds the shape and `{tokens}`; a data section holds the values.
`ReportBinder.Bind(template, data)` returns an ordinary `Report` with every
repetition stamped out - so the resolver is untouched, the filled template can be
serialized and opened in the designer, and each row is its own object.

`ContainerItem.RepeatOver` names the collection. In a table the header rows stay
and the remaining rows are prototypes, cycled - two of them band the rows; any
other container repeats its whole set of children. `EmptyText` says what to show
when the collection turns out empty; without it the container goes `Gone`.

`GroupBy` buckets a flat collection by one field before repeating, publishing per
group a `Key`, a `Count`, the rows under `GroupItems`, a `Sum` of every numeric
field - subtotals without an expression language - and the grouping field under its
own name. Grouping nests to any depth: a token is read from the innermost record
first, then each enclosing one, then the section, so an inner group still reaches
the group around it and the values beyond that. `Style.RepeatOnBreak` brings a group heading back at
the top of the pages its group runs onto, and only those.

Values keep their JSON type and the template decides how they read
(`{Total:#,##0.00}`). Number separators come from `BindingOptions.Numbers`, built
by hand rather than from a machine culture. `MissingField` chooses whether an
unanswered token stays visible, blanks, or refuses.

## Section style defaults

Every `TextStyle` property is optional. Null means "not decided here", and the value
comes from `Report.DefaultFont` instead - so a template names its typeface once and
can be restyled by changing one line. Whatever neither settles falls back to the
built-in default documented on each property. The resolver applies the cascade, so
nothing downstream ever sees a gap. Unset properties are left out of the JSON, which
keeps a template stating what it decides and nothing else.

## Form fields

`FieldItem` lays out like any other item and becomes an interactive field in the
PDF: `Text` (optionally multiline, with a length limit), `Checkbox`, and
`Signature` - a real signature field, which prints as an empty box to sign by hand
and can still be signed in a reader later.

Appearance streams are drawn by the engine rather than left to the viewer: PDF/A
forbids `NeedAppearances`, and a field only some readers can draw is not a section.
The value is always drawn on the page, never in the appearance, which is what makes
`Interactive = false` a true flattening - the same marks, minus the ability to
change them.

A field's value is bound like any other token, and an `ImageItem` can take its bytes
from the data through `SourceField` - base64 or a `data:` URI, which is what a
browser hands over for a signature drawn on a canvas. With nothing to show, the
image bows out rather than leaving an empty box. An unanswered token is not a tick:
`{Choices.Revision}` left as it is would otherwise tick every box on a blank form.

Repeated fields get an index appended to their name, since a prototype stamped out
over a collection would otherwise leave every copy answering to the same one.

## Dock, and aligning the item itself

`Dock` is the docking of Delphi's `Align`, not the distance-keeping of its
`Dock`: a parent here is never resized, so "keep 10mm from the right edge" and
"stick to the right edge" are the same thing. `Dock.Fill` fills, `Left|Right`
stretches across, `Right` plus a width is `alRight`.

`Style.AlignHorizontal` and `AlignVertical` answer what text alignment cannot: where
an *item* sits in the space it was given - how to centre an image, a table or a
field. They speak only when the item has a size of its own and neither an anchor nor
a coordinate has already placed it, so nothing that used to work changes.

## Known Limitations

Audited against the code on 24 August 2026, because a limitation is a claim like any
other and four of the twelve here described an engine that had moved on. Each of those
four is marked with what it used to say and what settled it.

- `PageScope` is honoured for overlays, header and footer, but ignored for items
  inside the body flow: removing one after pagination would invalidate the
  placement that pagination computed
- One header and one footer **per section**, not per report - `Report.Header` is a
  facade on the first section's, and a report holding several sections gives each its
  own. A band can hold several `PageScope` variants, which is what per-page differences
  are for. *Said "per report" until sections arrived.*
- A table row taller than what is left of the page **is** split, and both halves show
  as many cells as the table has columns - a half handing back fewer is refused by
  veraPDF and unreadable aloud, so short cells are placed empty. What is never split is
  a row of an *image grid*, and a line of an advanced text: those move whole.
  *Said a row is never split; test 39 has asserted the opposite since it was written.*
- Element alignment (`Style.AlignHorizontal`, `AlignVertical`) moves an item inside the
  room it was given, on any axis nothing else decided - including an item that measured
  its own size, which is most of them. A text's own `HorizontalAlignment` is a different
  question: it places the words inside the box, not the box inside the band.
  *Said alignment needed a stated size, which was true until it was applied after the
  sizing passes rather than inside them; test 69 measures a text with no width landing
  three different ways.*
- Column widths are fixed, a percentage, evenly-shared auto, or fitted to the
  widest cell (`AddFittedColumn`, with optional bounds); fitted columns asking for
  more than the table has are scaled back together
- Images declare their natural size; the engine decodes nothing, by design
- `Style.Border`, `Style.Background` and `TableItem.Grid` are carried for the
  backend to draw; the resolver only computes geometry
- `SvgBackend.ToHtml` carries a print stylesheet: the on-screen viewer chrome
  (grey surround, shadow, page counter) is dropped when printing, and `@page` is
  set to the report's own sheet size so one page maps to one sheet
- `PdfBackend.Conformance = PdfA3b` adds the XMP packet, the embedded sRGB ICC
  profile, the output intent and the file identifier PDF/A-3 requires;
  `AttachFacturX` carries the invoice XML and declares it, switching conformance
  on by itself. Level B is the only one offered. Level A would need a tagged structure
  tree - which the engine now writes, and test 53 validates against PDF/UA-1 - so this
  is a level not offered rather than one the engine could not produce.
  *Said level A would need a tree that did not exist yet.*
- The first baseline sits so the cap is centred in its line, rather than being taken
  from the font's ascender - which swings from 0.73 to 0.95 across the faces here and
  left the text sitting visibly higher in one face than another against a table border
- Leading is a multiple of the **cap height** - the size the author asked for - not
  of the em and not of the font's own line height. Both of those swing by half
  between typefaces, so a table row would change height for no reason but a change of
  font; with this rule the same section keeps the same shape whichever face it is
  set in. A face with unusually long ascenders needs `LineSpacing` raised.
- Cap height is taken from the glyph, not from the font's OS/2 declaration: one of
  the faces shipped here declares 0.940 and its H measures 0.732, so believing the
  table printed every capital a fifth too small. Where there is no outline to measure
  - CFF - the declaration is all there is, and `CapHeightIsEstimated` says so.
- A code point no font covers would be drawn as `.notdef`, which PDF/A forbids
  outright; `PdfBackend` collects them in `MissingGlyphs` and refuses to write an
  archivable file that contains one. A missing *space* falls back to the plain one
  instead, since Unicode has a dozen and most fonts carry only that.
- The ICC profile is written by the engine rather than shipped as a binary. It is
  a valid v2 matrix/TRC sRGB profile through a plain 2.2 gamma, not a byte copy of
  Adobe's, which differs only in the deepest shadows.
- `PdfBackend` writes PDF 1.7 directly, embedding the very TrueType files the
  layout was measured with (Type0/Identity-H plus a ToUnicode map, so text stays
  selectable). Fonts are embedded whole - there is no subsetting. Images may be
  JPEG, or non-interlaced 8-bit grayscale/RGB PNG, both passed through
  uncompressed-and-recompressed; palettes and alpha are refused.
- The SVG backend is the reference and debugging output, not a production one.
  A browser measures text with its own fonts, so the preview matches the layout
  only as closely as the measurer matches the browser.
- Test 17 writes a two-page invoice to `tests/output/invoice/invoice.html`
- Test 33 writes the same invoice once per font family to `tests/output/fonts/`, with
  `comparaison.html` stacking the first page of each - the fastest way to see what
  pinning the cap height does and does not make identical
- `FixedPitchTextMeasurer` remains the dependency-free default; `FontTextMeasurer`
  reads real TrueType/OpenType metrics from `engine/Fonts`
- Kerning and complex shaping are not applied when measuring
- Binding materialises every row, so memory grows with the row count: right for an
  invoice, wrong for a hundred-thousand-row listing
- `apps/designer/frisket-designer.html` edits the same JSON, but its preview is an
  approximation: it measures with the browser's fonts and does not paginate.
  The engine remains the reference.
