# Frisket — things to check every time

A dependency-free layout engine (.NET 10). This file does not describe the code:
it lists what goes silently out of step, and what the tests let through.

## 1. A property added to the model touches five places

The compiler flags none of them. For every addition in `engine/Model/`:

| To do | Where | What happens otherwise |
|---|---|---|
| Serialisation attribute | `engine/Model/*.cs` | An enum comes out as a number while every other one comes out as text — the JSON round trip works, but the file becomes unreadable and inconsistent |
| Default value | `apps/designer/frisket-designer.html`, object created by `data-add` | The designer produces templates with the property missing |
| Control in the panel | same file, `if (item.$type === …)` block | The property exists but stays out of reach |
| JS echo | same file, drawing functions | The preview lies about what the engine will do |
| Harness | `apps/designer/check-against-engine.js`, and `check-editor.js` for the flow face | The gap is no longer measured |

The **JSON schema** (`examples/schema/`) is not a sixth place: it is **generated** from the
model by reflection (`tests/TemplateSchema.cs`, test 47) and therefore cannot drift.
It lives in the tests because reflection is precisely what native AOT does not
keep, and the engine stays clean for AOT. The test reads back a real bound template
(881 objects) and fails if a property is not declared in it.

Every enum carries `[JsonConverter(typeof(JsonStringEnumConverter<T>))]`. Forgetting
one **has broken test 47 since 22 August 2026**, which names them one by one: the schema
declares each enum `"type": "string"` with the list of names, and it can only do that
if the model really writes names. It was an assumption written in a comment beside the
generator — an enum without its converter comes out as a number, the schema goes on
promising a name, and **nothing falls over**: the round trip still works, only a human
reading the file back would have found it.

**And "generated, therefore unable to drift" is only true of the names.** `Dock` is a
`[Flags]`: the converter joins the members with ", ", so an item docked on both
sides writes `"Left, Right"` — which is in no list of single names. The schema
declared a closed `enum` that **56 values in the samples violated**, 15 in the invoice
alone: a JSON Schema validator would have refused every template in the repository.

Nobody saw it because the walk over the 881 objects read **property names** and never
once a value — and it said so, in as many words, in its own summary: "Not a
validator - it checks names, not values". *A stated limit is still a limit*; writing it
down does not close it. It reads values since, and a `[Flags]` now comes out as a `pattern`
rather than an `enum`.

Found while answering "check the schema still lines up", which the 65 green tests
already claimed.

**The template writes only what it decides** (`DefaultIgnoreCondition = WhenWritingDefault`):
everything at its default value is absent from the file. Two consequences.

Engine side, a property whose default is *not* `default(T)` — a position
starting at `NaN`, a true `RepeatHeader`, `RootFontSize = 3` — must carry
`[JsonIgnore(Condition = JsonIgnoreCondition.Never)]`, otherwise its absence reads back
as something else. What means "not set" is projected to `null` through a
mirror property (`LeftJson`, `MarginJson`…), so it disappears from the file.

Designer side, **nothing can assume a key is there**. `hydrate()` fills the
holes on the way into `layout()`; that is safer than guarding every read, because
a forgotten read does not crash — it is `undefined` and positions at `NaN`.

## 2. Rendering is checked three ways, never just one

A text can be "correct" and wrong:

- **the measurement** — cap height, line height, baseline, widths;
- **the PDF** — objects, references, streams;
- **the eye** — rendered SVG, or the PDF opened in a real reader.

And a fourth, missing for a long time: **comparing the two outputs against each
other**. The watermark turned one way in SVG and the other in PDF; each file was
consistent with itself, and no assertion put them together. A sample rendered in
both formats, opened side by side, finds what neither reveals alone — hence
`conformance.pdf` and `conformance.html`.

A rule borne out five times: **an assertion that looks for bytes proves nothing**.
`/Subtype /Widget` present, `/Annots [` present, `/AcroForm` present — and the
form worked in no reader, because `/Annots` pointed at object 0. Check that the parts
of the file **reach each other**, not that they exist.

## 2b. Neutral is the default, French an option you ask for

`NumberFormats.Invariant` (comma for thousands, decimal point) has always been the
engine's default — `BindingOptions.Numbers`, `RenderOptions` on the native side, the CLI,
both wrappers, the designer. `NumberFormats.French` (narrow no-break space, decimal
comma) is nothing but an explicit option now, never a hidden default.

It was not so everywhere: the CLI, the native library and its C++/Pascal wrappers
each had their own `"french"` default, discovered while answering "and on the
C# side?". The designer had a checkbox on top of that — removed rather than set to
"unticked", because a default preference is still a preference: the question to
ask is "what would the engine do if nobody told it anything?", and
answering with a box ticked somewhere is already a wrong answer.

**And the ACME invoice pulled both ways.** English wording, `BoundAcmeInvoice`
asking for `NumberFormats.French`, `N°` instead of `No.`, and **no** `Language`
declared at all. The summary therefore showed three conventions in a single block:

```
11 876 769,42   bound    U+202F + comma         (French)
22 545.60       typed    U+0020 + dot           (no locale)
1,000,000.00    data     comma + dot            (neutral)
```

The second is the most instructive: French grouping, English decimal, set next
to a properly French number from which it differs **in two ways at once**.
Nobody had chosen it; it was typed by hand to look like its neighbour.

The invoice has been `en-GB` since, bound neutral, and says `Page 3 of 3`. What that
cost in coverage: **nothing**, because `NumberFormats.French` already had its own two
assertions (`1037,50`, `1 037,50`). What did have to be **replaced**, on the other hand:
the invoice was the only document writing a narrow no-break space into a PDF, so
`MissingGlyphs.Count == 0` stopped covering U+202F the day its numbers stopped
being French — staying green, on a document that no longer carries the character the
check was written for. *A check that loses its matter never says so.* A dedicated
render does it again, and it catches a sabotage of the writer alone that the
assertion on the font does not see.

**An invisible character is read back in the file, not in the terminal**: `GlyphOf(' ')`
displays exactly like `GlyphOf(' ')`, so I declared the assertion tautological and was about
to "fix" it — §5b, the regular expression pattern re-read with `cat -A`, seen from a C#
source instead of a harness.

## 3. Never take a font at its word

Declared metrics are wrong often enough that they can no longer be trusted:

```
TimeBurner     OS/2 says 0.940   the H measures 0.732
ChopinScript   OS/2 says 0.640   the H measures 0.730
Glametrix      OS/2 says 0.480   the H measures 0.480   (exact, and legitimately low)
```

The first two are **no longer in the repository**: removed on 27 July 2026 before
publication, for want of being able to prove the right to redistribute them. The
numbers stay here because they were measured and because they are the reason the code
exists. Test 33 now demonstrates the mechanism rather than the pattern: the font named
by `DmsFont` for a TrueType outline, Glametrix for a CFF outline, and no font
estimated.

**A test never names a font.** The font cull of 1 August 2026 dropped Josefin Sans,
which was the default: twenty-six tests fell, and eighteen `DefaultFamily = "Josefin
Sans"` had to be found by hand — the very thing the `DmsFont` constant was supposed to
cover. Two numbers were hiding in assertions as well — `/CapHeight 723` and the space
glyph's id — both derived from the font since, via `Resolve(DmsFont, …).CapHeight` and
`GlyphOf(' ')`. A hard-coded value that belongs to the font fails the day the font
changes, not the day the code breaks.

A licence label ≠ the right to redistribute. Minimo was advertised as "Freeware" and
its own file forbids redistribution in so many words. Every font added must bring
**its own licence text**, not a mention on a download page.

Cap height, line height and baseline come **from the outline and the size asked
for**, never from `hhea` or `OS/2`. What stays variable and must not be normalised:
the **widths**, which are the drawing of the font itself.

The outline is measured in CFF too (`engine/Text/CffOutlines.cs`): no font in the
library is "estimated".

**A weight has no place in the key, so it goes into the name.** `FontLibrary` keys on
`(family, bold, italic)` — one string and two booleans. A Light is neither bold nor
italic: it therefore lands on the regular. Glametrix ships three files all called
`Glametrix`, told apart only by nameID 2 (Regular / Bold / Light), and the third
overwrote the first — `_fonts[key] = font`, an indexer assignment, last one enumerated
wins. A template asking for `Glametrix` got the **Light**.

**Nothing showed it**, and that is the whole of §3: both cuts measure the same cap
height (0.480), so every assertion in the suite went on passing. What differs is the
**widths** — the one thing the engine never normalises, and the one thing nothing
asserted on. Found by putting `RegisterDirectory`, which returns **29**, next to the
library, which held **28**. The proof was in a return value nobody read.

Two fixes, answering two different halves:

- an unrecognised subfamily becomes part of the **family name** — `Glametrix Light`.
  This is not an invented convention: **Elms Sans does it itself** (`Elms Sans Light`
  in nameID 1, `Elms Sans` in nameID 16), so it only makes the ill-mannered archive
  resemble the well-mannered one. The list is of what counts as a **flag** (regular,
  bold, italic, oblique, normal, book, roman), never of the weights: listing the
  weights would leave a hole for the next unexpected word — a Caption cut, a Poster —
  which would overwrite the regular in silence. The other way round there is no hole.

  And **by whole words**. The rule it replaced asked whether the subfamily *contained*
  `bold`, which makes a `SemiBold` a bold that lands on the real one. Nothing here
  triggered it, which is precisely why it would have stayed invisible;
- a key claimed twice is **recorded** (`FontLibrary.Clashes`) rather than taken in
  silence, for the case the first fix cannot cover: the same file twice, where the
  subfamilies are identical and only the path tells them apart. Hence `FontFile.Source`,
  and a message that names the files — "Regular hidden by Regular" says nothing.
  `frisket fonts` reports them on standard error.

The name comes from the `name` table, **not** from OS/2's `usWeightClass`: Glametrix's
regular declares 500 there, that is to say Medium. Same lesson as cap height — what a
font says about itself is a claim, and here the name the type designer gave the cut is
the better of the two.

**And the order of a directory decided the typeface.** `RegisterDirectory` took files in
the order the file system listed them, and that order is not a detail: the first family
registered becomes `DefaultFamily`, and a key claimed twice goes to the file registered
last. NTFS lists alphabetically, ext4 by a hash of the name - so the same template came
out in Elms Sans Black on Windows and in Mulish on Linux, and an HTML proof embedded its
faces in another order. Every font was a legal answer, so nothing failed. Found the first
time the suite ran on Linux, by comparing what each platform *wrote* rather than by any
assertion.

Sorted now (`InRegistrationOrder`): case-insensitive then ordinal, on the path relative to
the directory with one separator, because `\` and `/` do not sort in the same place. That
is NTFS's own order, so Windows output did not move by a byte, and Linux now writes the
same HTML to the byte and a PDF whose 32 streams are identical once inflated. The
compressed bytes still differ - Ubuntu's .NET compresses with the system zlib - and that
is the runtime's, not the engine's. The same goes for every picture the suite builds for
itself: a PNG is deflated too, so an HTML or SVG carrying one differs on Linux by exactly
that line - all 78 changed lines of a fresh Linux clone on 18 October 2026, and nothing
else. A byte-for-byte check of the tree therefore holds on Windows only. Checked as a function with a shuffled list, because a
real directory proves nothing on Windows, where NTFS already sorts.

**That the default is Elms Sans *Black* is itself an accident of the alphabet** - `Black`
sorts before `Bold` and `Regular`. It is deterministic now. Whether it is the right
default was an open decision until 30 September 2026, and the accident was worse than
it looked: Black has **no bold cut**, so in every template naming no family - every
document the content editor writes - bold text was drawn exactly like the rest. Settled
as Mulish, and declared by the directory rather than by the alphabet (§3b).

## 3b. A missing space is drawn, not boxed - and a no-break space does not break

Asked which face should be the default, I built a specimen page and told the author that
a face without U+202F would put a box in every French amount and have its PDF/A refused.
**The author had never seen a box in a PDF, and was right.** `FontFile.GlyphOf` folds a
missing white space onto the plain one: no `.notdef`, nothing in `MissingGlyphs`, no
refusal - test 25 already said so. What differs is only the width: 0.166 em for
Noto's real narrow space, 0.261 for Mulish's plain one, 0.429 for Elms Sans's.

The page had been built to model the engine - the same files, the same leading, no
kerning, no synthesised bold, the fallback cut `Resolve` picks - and it modelled every
rule but the one I had not read. *A preview that guesses is §2's fault even when the
preview is mine.*

Checking that answer, rather than the question, found **two faults older than either**:

- **Both line breakers split at any white space**, the no-break ones included, so
  `12 480,00 EUR` could end a line on `12`. `LineBreaks.At` is `char.IsWhiteSpace` less
  the three spaces UAX #14 calls glue - U+00A0, U+2007, U+202F - and both breakers ask
  it, so they cannot come to disagree;
- **ToUnicode kept the last character drawn with each glyph.** A folded U+202F shares
  the space's glyph, so a French amount *closing* the text turned every space in the
  document into a narrow no-break one for whoever copied, searched or listened to it,
  and an amount anywhere else did nothing - an answer decided by the order of the text.
  The smallest code point wins now, whatever the order.

Neither shows on the page. The break falls where a reader expects one, at a space; and a
ToUnicode map is only ever read by copying, searching or a screen reader - the three
things nobody does while looking at a proof.

**The echo's check nearly checked less than it said.** Asked only about the spaces the
engine *names*, it would have gone on passing the day the engine stopped naming one,
with the echo still holding it - and the C# tests could not see that either, since
they iterate the same list (sabotaged: test 19 green, the engine breaking at U+2007).
So test 19 writes `tests/output/text/line-breaks.json`, where the engine breaks for
every character of the basic plane, and the harness holds the echo to it one character
at a time. A check that follows its subject's list is one deletion away from checking
less, exactly as one that keeps its own is one addition away (§16).

**And an invisible character, again (§2b).** The `Glue` constant came out of the editing
tool as the three characters themselves rather than `\u` escapes: it compiled, it
worked, and it read as nothing - `"   "`. The harness's first guard, *the engine names
the spaces a line may not break at*, failed with an empty-looking detail, which is the
only reason it was seen. An escape that must survive an intermediary is built from
`chr(92)`, and read back with `cat -A`.

**The default typeface lives with the typefaces.** `engine/Fonts/default-family.txt`
names Mulish, and `FontLibrary.RegisterDirectory` honours it: a default the caller had
already set wins, and a family the directory does not hold is refused rather than left
to the alphabet it replaced. The CLI, the server and the native library all load that
directory, so a family name typed into each host would be §1 three times over; the
file carries the reasons beside the choice. Test 56 asks that the alphabet alone would
have chosen otherwise, or the check would be true for want of matter.

**And the faces showed a face of their own - two, in fact.** Text naming no family came
out in `sans-serif` from the core, in `Georgia, serif` from the content editor, and in
Mulish in the PDF. Worse, neither face had ever loaded the engine's files:
`font-family: Mulish` draws Mulish on a machine that has it installed and the browser's
default serif everywhere else, so even a *named* family was a guess.

The default is declared now - `setDefaultFace`, beside `setTextRuler` - and the page
says which output it is showing: the server's default family when the list is the
server's (`/fonts` marks it with `IsDefault`, `/fonts/file` serves each exact cut, and
the page loads them with `FontFace`), FlowingHtml's stack when the list is the web's
own. `font-synthesis: none` stops the browser inventing a bold or a slant a family does
not have, so it falls back to another cut exactly as `Resolve` does - a slanted Glametrix
on screen would be a promise the PDF does not keep. The harness reads the mail stack out
of `FlowingHtml.cs` and the route out of the server's `Program.cs`, and ten sabotages fall
on ten checks; Edge, driven over the DevTools protocol, shows the 29 cuts loaded and the
sheet in Mulish.

The designer's banner said *the browser measures with its own fonts*. True until it
loaded the engine's; it says the other thing once it has.

## 3c. A library nobody filled draws with the bundled faces - and only then

**The commonest first failure of a PDF library is a font nobody said was needed.**
`new FontLibrary()` was empty, and an empty library refuses every text: `dotnet add
package Frisket`, a template, a render - and an exception naming a family the newcomer
never typed. Hence `fonts/Frisket.Fonts.csproj`, an assembly carrying Mulish, Elms Sans,
Noto Sans Display and Glametrix as embedded resources, which the engine depends on.

**A package of its own because the licence is.** The engine is MIT, the faces are the
OFL 1.1, and a package states one licence; folded into `Frisket` they would make the
engine's own `LICENSE` a lie about what the package holds. The files themselves **stay in
`engine/Fonts`**, where the tests, the CLI, the server and a dozen paragraphs of this file
look for them - the project only chooses which ship. Elms Sans ships four of its eighteen
cuts. `default-family.txt` is embedded too, so the package's default **is** the
directory's declaration rather than a second one (§1), and test 94 holds every embedded
face to its file on disk byte for byte.

**They stand in; they never mix.** An empty library draws with them, filled on first use,
once. The first font registered - one face, a directory, bytes - makes them step aside
entirely, and with them any default they brought. Mixing would be the substitution
`FailOnMissingFont` exists to refuse: a family missing from the caller's fonts would
quietly be found among ours, and the document would come out in a face nobody chose. A
default the caller set on an empty library is kept; a directory given after the faces
stood in still names its own.

**Standing in happens on a read, so it needs a lock.** The first `Resolve` may come from
several renders sharing one library. The check that the lock is there is a race, and a
race check is only worth the rounds it runs: `Parallel.For` caught the lock's removal two
times out of three, which is a check that will one day go green on the fault. Sixteen
threads held at a `Barrier`, two hundred rounds, caught it five times out of five.

**The native library is not the engine's assembly.** It compiles the engine's sources in,
so a reference the engine gains is one it does not - the first native publish failed for
want of `BundledFonts`. It references `Frisket.Fonts` itself, and AOT folds the faces into
the one file: `fk_fonts_list` on a library nobody filled names fifteen.

**The CLI stopped looking for a directory.** It walked up from its own executable to find
`engine/Fonts`, so the same command worked inside the repository and stopped with *no
fonts found beside the executable* everywhere a CLI is actually installed. Naming nothing now means the bundled faces; naming anything -
`--fonts`, `--font` - means only those, and `frisket fonts` says which on standard error.

**And the refusal said `family ''`** for as long as it existed, in exactly the case a
mistyped `--default-font` produces: text naming no family asks for the default, so the
default is what was not found. It names the default now, and lists what the library does
hold - a refusal that says what was missing and not what was there sends people to read
the code.

## 4. External checks, after any touch to text, fonts or the PDF

**And a warning-free build can be a build that did not happen.** §6 says a stale
binary looks exactly like an up-to-date one; the same is true of a clean compile. Two
broken `cref`s introduced by the import work went unreported for a day because Windows
had nothing to recompile - `dotnet build` answered *0 warnings* about assemblies it did
not touch. They surfaced the moment the same solution was built inside WSL from cold.
**`--no-incremental` before believing a count of nothing.**

**Test 53 runs veraPDF itself**, on both profiles **and both output modes** since
25 August 2026 — the
instruction below is no longer the only line of defence. It looks for the launcher via
`FRISKET_VERAPDF`, then in the usual places, and declares itself **`Skipped`** if it
cannot find one: a silent green would be exactly the vacuous check this file complains
about everywhere else. It **produces its own document** rather than validating
whichever one is lying around — the first sabotage of this test went green because it
read back a PDF older than the reintroduced fault (§6).

**And for three weeks it validated a lesser document than the one that ships.** It
produces its own rather than reading whichever file is lying around - written for a good
reason, since the first sabotage of this test went green against a stale file - and the
document it produced carried no Factur-X attachment. So PDF/A-**3**B's attachment rules,
which are the whole reason the profile is 3B and not 2B, were never once put to a
validator. Found while writing a list of what must survive a round trip through a signing
platform, by asking that list about our own file first.

*A document produced for a check is a sample like any other*, and the shapes it lacks are
exactly the ones no profile can refuse.

That does not excuse the eye or a real reader, and running it by hand stays useful for
`--format xml`, the only way to see *which* rule failed:

```bash
"$FRISKET_VERAPDF" --format text --flavour 3b  tests/output/conformance/conformance.pdf
"$FRISKET_VERAPDF" --format text --flavour ua1 tests/output/conformance/conformance.pdf
```

Both profiles must pass.

The validated document is generated from `examples/showcase.json`, written by
`tests/Showcase.cs` then **read back by `Report.FromFile`** before any rendering. The
round trip is not decorative: it is the only check that every element type survives
being written and read back, and it is the path the CLI and the native library take. A
report handed straight to the renderer never proves that.

**The document now says what it demonstrates.** It was a handsome report that said
nothing about itself: a split table row, a translucent watermark and a signature look
like a table, a tint and a frame — as long as nothing says **what you are supposed to
see, and what would be wrong**. Seven demonstrations, each with a heading, a
full-width rule and a sentence: what it does, and what to observe. The rule is *drawn*
and not typed, so that it spans the page whatever its width.

Three of them leave **no trace on the page** — the Factur-X attachment, the tagging,
the conformance claim. Nothing on screen is wrong, and nothing on screen is proof
either: the only honest thing a caption can do is say **where to look elsewhere** (the
attachments pane, the structure tree, a validator).

And the limit not to cross: **a caption never replaces a check** — writing "two
barcodes below" puts none in the file, so test 40 still questions the tree before a
byte is written (§2).

A duplicate found while doing it: the "Appendix" heading set a bookmark, and the
`SubTemplateItem` element set a second — the outline announced two appendices. The
test's count caught it, which is why it counts instead of sampling.

Three things this document cost, and that must not be learned again:

- **there is no default page size.** `DocumentDimension` is 0 × 0 if the template says
  nothing. Pages then come out with no width, every column is too narrow to be
  divided, and the flow places one item per page and throws the rest away — without a
  word;
- **a handwritten signature is a figure** (`PdfRole.cs`), so it must carry an
  `AltText` like an image, or PDF/UA 7.3 refuses the file;
- **headings cannot start at H2.** PDF/UA 7.4.2 requires an H1 above.

- **PDF/A validator** on `tests/output/conformance/conformance.pdf` — a single file
  that brings everything together: PDF/A-3B, Factur-X attached, multipage, back
  pages, translucent watermark, form with handwritten and digital signature, two
  barcodes, split table row, image and subset fonts.

  Test 40 checks that each of these things is there **before** writing it: a single
  file to validate is only worth something if it really carries everything,
  otherwise a satisfied validator says nothing.

  A PDF written **without** `Conformance` claims nothing, and a validator then falls
  back on PDF/A-1b by default — which forbids things the engine now writes,
  transparency first among them;
- **a real reader** — Edge and Foxit do not behave the same, and neither of the two
  behaves like the SVG.

Four faults out of five were found outside, not by the test suite.

**And from the day it existed it validated one output mode out of two.** Three weeks,
not the eight months first written here - the check dates from 4 August 2026, and the
engine's age is not the check's. The document was rendered `Interactive`, always. Flattened, it writes no widget — so a `Form` structure
element has nothing under it, PDF/UA 7.18.4 refuses the file, and the XMP goes on
claiming PDF/UA-1 while the 3B profile passes without a murmur. Half of what this
engine produces was outside the only check that reads a file from the outside, and the
half that was covered is the one that happens to be right.

The rule it produced is the one §4 already had for a handwritten signature, applied
wherever the widget fails to arrive rather than in the single place it was first
noticed: **a `Form` that will get no widget becomes a `Figure`**, and a figure owes an
`AltText`. Three ways to arrive there — the output is flattened, the signature is to be
given by hand, or an authored `Role = Form` walked past `PdfRoles.Resolve` on something
that is not a field at all.

*A validator rule is worth having only if it predicts what the renderer really does*, so
`can.flattenedFieldAltText` and the demotion are checked in one test rather than two.
And the conformance document did **not** satisfy the rule the day it was written: three
figures with nothing to say for them, which is the sample being mended rather than the
rule being loosened.

**Two more of the same family, neither of which any assertion could see.** `PdfForm`
handed out an invented name without recording it, so a later field asking for that
name in so many words was given it too — and two widgets sharing a name are **one
field with two appearances**, so filling either fills both. And an `/AcroForm` holding
a signature declared no `/SigFlags`, so a reader shows the box and offers no way to
sign it.

The check for the first was **vacuous on its first writing**: its bait was `a-2`,
which is what the *mended* code invents. The fault invented `a.1`, which collides with
nothing in that sample — so the sabotage went straight through. A separator is an
implementation detail; the bait is now every shape the invention might take. §10c
again, seen from a name rather than from a calibre.

**And a rule reporting only the first of its kind cannot be confronted.**
`can.flattenedSignature` returned one finding whatever the template held, so taking the
by-hand exclusion out of the designer's echo changed no count and the harness said
`all good`. One per signature — §5a-bis's test applies, each *is* a different place to
go and put right — and the sabotage falls at once, 2 against 1.

**The `Capable` pairs carry the target they were asked under.** The harness kept its
own copy of it in JavaScript: the engine could change the question while the two went
on agreeing about the answers. It is also what made a second target free, and one was
needed — `can.flattenedFieldAltText` fires only on a *tagged* output, and the single
target had everything switched off, so the rule was reachable by neither side.

### 4a. A signature field is a place for somebody else to sign

Frisket writes signature **fields** and signs nothing. Rendered interactive, a
`Signing.Digital` field comes out as an ordinary unsigned signature field - `/FT /Sig`, a
name of its own, a rectangle, listed in `/AcroForm /Fields` under `/SigFlags 1` - which is
what a signing platform looks for. The transfer is the host's: §8d's reasoning, the engine
never waits for anything.

`NotFitToSign` (`tests/FormsAndSignatures.cs`) is the list of what stops that working, and
it is **one list asked twice**: of the document going out, and of the one that comes back.
Written as two they would drift, and the drift would be invisible - a document that went
out fit to sign and came back without its attachment looks identical on screen.

The half nobody can reason about: a signature applied as an **incremental update** leaves
the file underneath intact, while a platform that rewrites the document can produce a
perfectly valid signed PDF that has lost the PDF/A claim, the Factur-X invoice and the
whole structure tree. Which one a provider does is a fact about that provider, found by
sending it a file.

**A checker meant for foreign bytes has to be shown saying no.** Written with only "it
says nothing about a good document", two of its branches were true for want of matter -
the showcase has no two fields sharing a name and no signature missing from `/AcroForm`.
Every complaint is now fired by spoiling the real document in the one way that complaint
is about. That is the same rehearsal a fuzzer does (§8c), pointed at a checker instead of
a parser.

### 4b. The tree was right and the map was wrong

Everything above was found by writing checks. What follows was found by **reviewing the
session afterwards** — 39 claims, 27 surviving a two-voice refutation, and the three
graded worst were all real, all older than the session, and all in the corner it had
been working in. *A pass over one's own work an hour later is not the same faculty as
the one that wrote it.*

**A tagged page carries two halves, and only one of them was ever asserted on.** The
tree says what each element is; the number tree is indexed **by mark** — position n is
the element owning mark n on that page (ISO 32000-1 14.7.4.4). A `Form` owns no marked
content at all, being reached through the object reference under its own key, and it was
listed in the page's array all the same. So it took a slot no mark claims and **shifted
every element after it by one**: on the conformance document, a reader over the H1 was
told it was the signature box.

Nothing could see it. Reading order comes from `/K` and was correct; veraPDF passes both
profiles, because a well-formed array of the wrong length is not something either profile
tests; and every assertion in the suite questioned the tree. It is §2 in its purest form —
*check that the parts of the file reach each other* — and the method's own doc comment
already named the symptom it was producing. One condition, and a test that walks the
array position by position.

**A widget with no `Form` over it is refused as surely as a `Form` with no widget under
it,** and closing one half left the other open. `PdfRoles.Resolve` answers `TableCell`
for anything whose parent is a `TableRow` **before** it looks at `FieldItem`, so a field
used as a cell was tagged `TD`, its widget written, listed in `/Annots` and in `/AcroForm
/Fields`, and pointed at by nothing.

Forcing `Form` instead was the obvious fix and **traded one violation for another**:
veraPDF answered *"TR element may contain only TH and TD elements"*, and the row was then
one cell short of its header — a second rule broken by the first repair. What the
standard wants is both: **one item, two elements**, the cell keeping its place in the row
and a `Form` under it taking the widget. `Marked.Owner` says so outright, because
ownership is otherwise read off the item tree and one item then makes one element.

The lesson is not about tables. It is that **the validator answered a question I was
about to decide by argument** — twice, in the order I tried them. §4 says run it; this is
what that is worth.

**And it had no example anywhere**, which is why nothing refused it: the conformance
document carried a table and carried fields, and never a field *in* a table. A single
file to validate is only worth something if it really carries everything (§4), and the
shapes it lacks are exactly the ones no profile can refuse.

**A split row lays its non-text cells again, at zero height**, so the row keeps its full
count of cells (§11). For anything drawn that placeholder is an empty rectangle; for a
field it was a **second widget** — unfillable, invisible, and advertised in `/AcroForm
/Fields` under an invented name that is in no template. Hence `BeginElement` taking the
frame: told only the item, a backend cannot tell one placement from another.

**Not colliding is half the job.** `PdfForm.Unique` invents a name for a duplicate; an
invented name that a *later* field asks for by name does not collide — the loop sees to
that — it **renames the author's field**, and a form back end then receives the value
under a key nobody wrote. Every author name is claimed before any is invented.

Its check was vacuous twice, the same way both times. First the bait was the name the
*mended* code invents, so the fault — which invented a different one — collided with
nothing and the sabotage went green. Then, rewritten to ask "is every author name still
present", it passed on a document where the duplicate held `a-2` and the author's field
had become `a-2-2`: all six names distinct, all five strings present, and exactly the
wrong field carrying the name. *A name and the field it was written for have to reach
each other* — §2 again, in a fourth domain.

**The panel read one section out of many.** The designer hydrates the section being
edited; `validate()` walks them all — so everywhere else a default was simply missing,
and a missing default is not a default. `Signing` is the case that showed it: `ByHand` is
the zero of the enum, the engine never writes it, so `f.Signing === "ByHand"` was false
for every pen-signed signature read back from a file. The panel then invented two Errors
against a field that loses nothing **and stayed silent about the one real PDF/UA fault it
had**. `validate` hydrates what it walks now, exactly as `layout` does.

Its check was vacuous too, and instructively: written by doubling a file **neither**
section of which was hydrated, both were read wrongly in the same way and the counts
still doubled. The fault is an **asymmetry**, so the check has to reproduce the page's own
sequence — hydrate the one being edited, then validate the report. *A check that puts the
page in a state it never reaches proves nothing* (§5c), seen from a second face.

And the arithmetic it settles on says two things are legal and everything else is the
fault: a rule about an item doubles, a rule about the document stays put. Naming which is
which would be a list the harness keeps, one rule away from checking less than it says
(§16); the two legal answers can be stated without it.

## 4b. An image can come from the data, not only from the template

`ImageItem.SourceField` names a path in the data whose value is raw base64 or a
complete `data:` URI — the shape a browser posts when it captures a signature on a
canvas. `ReportBinder.BindImage` already accepted both shapes and made the image
disappear (`Visibility.Gone`) rather than draw an empty frame when nothing is signed
(test 31); what was missing was the designer, which knew how to embed a file in the
template but had no field to say "this will come from the data".

Added in the three places: the panel (`SourceField`), the echo (resolving the
base64/`data:` and disappearing if the path leads nowhere), and the validator —
`sound.emptyImage`, an image with neither an image of its own nor a path, symmetric
with `sound.emptyBarcode`.

## 5. The template validator exists twice

`engine/Model/TemplateValidator.cs` and its echo in the designer say the same thing about the same file, or the designer clears a document veraPDF will refuse. The harness confronts them: test 42 writes a template **built to be wrong** into `tests/output/validator/`, together with what the engine found in it; the echo reads that file back and must report the same rules, in the same number and at the same severity.

Any rule added on one side must be added on the other, plus its wording in both languages (`issue.<rule>`).

**And "or the message shows as its key" was only half true**, which is what let a wording go missing without a sound. `t()` falls back to English when a language has no entry, so a missing English wording shows the key and a missing French one shows *perfectly good English* — the second is unaskable through that door. The harness reads the dictionaries themselves now, one language at a time, found rather than named. Before that it counted `"issue.x":` into a **set**, where the two languages collapse to one entry and a rule labelled in either alone passed.

**Its wording, not the engine's sentence.** What the two must agree on is the rule: its
id, that it fires, and at what severity. The words are the designer's own, because the
designer is a separate application with a reader in front of it — and the difference is
earned, not drift. Measured on 25 August 2026, over 26 rules: 14 messages are word
for word the engine's and **12 differ**, and all twelve are the panel being right. `can.untagged` ends
in the engine with "Render with Tagged, or stop maintaining what is being dropped" and
stops earlier on screen, because the panel has a checkbox labelled *tagged output* an
inch away; `ua.language` drops "Set Language to a tag such as fr-FR" because the panel
*is* that field. `sound.raggedTable` interpolates `{row.Childs.Count}` in C# and `{count}`
through `t(rule, args)` — two mechanisms, not two wordings.

This is written down because the boundary was implicit and I crossed it within a day of
building the check that guards it: I compared the two texts, found eleven differences and
called them a defect. *A difference is not drift until you have asked whether it was
earned.* The generated reference page therefore quotes the **engine's** message, which is
what `frisket check` prints and what `fk_check` returns — it documents the engine, and the
designer is not the engine.

A rule only counts if it **falls silent once the fault is mended**: the test mends the faults one by one and demands zero at the end. That is what showed that a filled-in barcode is still a mute figure until it has an alternative text.

### The third profile does not read the template alone

`Sound` and `Accessible` answer from the template. `Capable` needs to be told **what is about to be produced** (`RenderTarget`), because the same template is faultless for one output and half-lost in another: a form is a form until it is flattened; a role and an alternative text reach the file only if it is tagged. Five rules — `can.untagged`, `can.flattenedFields`, `can.flattenedSignature`, `can.flattenedFieldAltText` and `can.absoluteInFlow`.

It is the only class of fault where **the document looks exactly right**. A flattened signature is a picture of a place to sign; nothing says so, and the page is identical. Hence test 42 writing a **second** pair of files — the template once *mended*, so with no fault at all, and what the engine finds in it — which the harness confronts like the first.

**The findings file holds one case per output aimed at, and each carries its target.** The harness kept a copy of the target in JavaScript, so the engine could change the question while the two went on agreeing perfectly about the answers (§16). It is also what made a second case free, and one was needed: `can.flattenedFieldAltText` fires only on a **tagged** output, and the single target had everything switched off — so a rule written on both sides by hand was reachable by neither, which is the one most likely to drift.

With no target named, both stay silent: warning about a loss that is not happening is the surest way to teach people to ignore the panel (§15). The defaults are therefore the richest output.

And the rule has to be **reachable**, or it is §10 under another name: two checkboxes in the designer ("tagged output", "fields stay fillable"), and `frisket check` reading `--tagged` / `--flat`, the same flags as `render`. The `Capable` profile is always on there and is **not** what `--accessible` turns on: the two answer different questions.

## 5b. A colour has a name, or it drifts

Two families, and the line between them is the whole point.

The **chrome** follows the theme — and one day the theme of whatever page embeds the designer — so every one of its colours is a custom property a host can override. Semantic rather than literal (`--accent`, not `--blue`), and twenty-four names: too few and the host cannot match; too many and the contract becomes unspecifiable and nobody holds it.

The **paper** does not follow anything. What the canvas shows is a printed page, and a mark that changed with the interface would make this tool disagree with the printer — §2 in CSS clothing. Those colours live in one JavaScript object, `ON_PAPER`, and hold one value in every theme. The selection marker is deliberately among them rather than hung off `--accent`: a host setting a pale accent would make it invisible on white.

Neither survives being a discipline. The harness enforces both: a hex or an `rgba()` outside the palette or outside `ON_PAPER` fails the run, **every theme it finds** must set exactly the same names, the light values — written **twice**, once for `prefers-color-scheme` and once for the explicit choice, because a preference must be able to contradict the machine — must agree name for name, and `--paper` must be one colour only.

**And the room `auto` gives was the one block nothing looked at.** The audit finds a theme by its `[data-theme="..."]` selector, so the unnamed `:root, :host` block — what every face falls back to, and what `auto` shows on a dark machine — was compared with nothing. It got away with it for exactly as long as it was the byte-for-byte twin of the explicit `dark`, which *is* audited: **the thing covering it was another block, not a check.** Deleting that twin would have taken the mask with it, in the same pass that rewrote the block. It is asked now to be one of the rooms the selector can reach, told no name, so a cull cannot make it stale - and one mistyped digit answers *matches none: graphite differs on 1*.

Before that check: 40 distinct literals, `#14161a` doing **two jobs** (panel background and the surround of the canvas) and `#8b939e` / `#8b949e` doing **one**, a digit apart. Nobody had decided either.

**A default that is never reached looks like a fault.** The watermark is grey at one fifth in the engine; the echo wrote `font.Color || { R: 128, G: 128, B: 128, A: 51 }` and yet drew a **solid black** DRAFT over the page. The cascade goes through `Object.assign(newFont(), w.Font)`, and `newFont()` already hands out `Color: opaque black`: `font.Color` was therefore **always** true, the second branch never taken. The number was right, in a place nothing executed.

The lesson is not "re-read your cascades" but: *a default lands on what the author wrote, not on what a neighbouring default has already filled in*. `w.Font && w.Font.Color` asks the right thing. And a watermark's ink is not a text's — borrowing the neighbour's default was the fault before it was dead code.

Found by writing the check, never by reading: the harness now reads the alpha out of `engine/Model/Watermark.cs` rather than retyping it, or it would be comparing the harness with itself (§16), and it is sabotaged **in both directions**, because a check that catches drift on one side only leaves the other copy free.

**And that check held the branch that never runs.** It compared the *fallback* - the ink
used when a mark carries no colour of its own - while every mark the panel makes carries
one: `readWatermark` wrote its own `A: 77` literal, and the engine had lowered it to 51
after measuring it too dark over a paragraph. So a watermark made in the designer was half
again as heavy as one the engine makes itself, for months, with a green harness sitting
next to it. The number is `WATERMARK_INK` now, in one place, and the harness also asserts
that the panel **does not retype it**. *A check aimed at a default is aimed at the case
nobody hits* - the §5b story above and this one are the same ink, seen from its two ends.

Four places, not three: the page's stylesheet, the JS echo that draws on the paper, the detached preview window — which wrote its own stylesheet by hand and would have been the one piece not to follow — and the room selector itself. `auto` is the default, because a preference stored somewhere is still a preference (§2b).

**And the audit read one face out of two.** It followed the `<link>` tags of the page it
was handed, and it is handed the designer — so `apps/designer/frisket-editor.css`, part
of what ships, carried **fifteen loose literals** nobody checked while §5b went on
promising that a hex outside the palette fails the run. *A stated guarantee is still a
guarantee.* It reads **every** `.css` in the directory now, found rather than named, for
the reason §16 gives: a check that keeps its own list is one addition away from checking
less than it says. Two of the fifteen were a `#666` and a `#555` doing one job a shade
apart, which is §5b's own opening story turning up again in the face that ships.

**A colour on the paper needs a name too, and a misspelt one is invisible.** The seven
that were missing live in the paper block — the ink, a rule, a table's borders and its
header tint, two greys for notes about the page, and the shadow under a chip. And since
`var(--on-paper-tekst)` resolves to nothing and *inherits*, which on white paper looks
very nearly right, a second rule says every `--on-paper-*` used must be declared. Drawn
round that family on purpose: `--sheet-pad` and `--sheet-width` are set from JS and carry
their own fallback, so widening the rule would mean writing an exemption — and an
exemption is where the next hole goes.

**And the check itself was vacuous at first.** Written through an intermediate Python file, `` became a real 0x08 character in the pattern: the hex branch could never match, only the `rgba(` were counted, and the harness announced "no colour outside the palette" on a page that carried one. Found by sabotage, not by re-reading. A regular expression pattern written by an intermediary must be **re-read in the file itself** (`cat -A`), never only in the source that generates it.

## 5a-bis. Email is not a backend

`engine/Html/FlowingHtml.cs` writes the report as **flowing** HTML. This is not a backend and cannot be one: `IReportBackend` is a drawing surface (`BeginPage(Page)`, `DrawText(Rect, …)`) — everything reaches it already laid out, in millimetres, on a page. Any implementation therefore produces **a picture of a page**, which is exactly what an email must not be: it has no pages and rewraps to whatever width the phone that opens it happens to be.

It is a **second consumer of the model**, parallel to the resolver:

```
Report ─ ReportBinder ─┬─ LayoutResolver ─ IReportBackend   (paged)
                       └─ FlowingHtml                       (reflows)
```

Not to be confused with `SvgBackend.ToHtml`, which is also HTML and is the other thing entirely: SVG pages with the fonts embedded, for opening in a browser and seeing exactly what the printer will do. That one is a **proof**, this one a **delivery**. `--format html` and `--format mail` on the CLI side.

Its rules are email's, older and narrower than the web's: tables for layout, styles inline rather than in a sheet, widths in per cent, and **no embedded fonts at all** — the reader supplies the font, the exact inverse of the PDF rule where a missing family is refused.

**Side by side survives; what overlaps does not.** The first version stacked every `Absolute` group and left a rule to apologise for it, on the grounds that coordinates mean nothing without a page. That was too quick: a picture beside a paragraph has a **perfectly good** flowing form, and it is the oldest one there is — a row of cells. What has no flowing form is an arrangement whose pieces **overlap**, since a cell cannot be half over its neighbour.

So the question asked of a group is not "was it placed at coordinates" but "do its pieces sit beside one another", and `FlowingHtml.SideBySide` answers it **for the validator too**, so the warning and the output cannot disagree about which groups survive (§15).

**One issue per group, never one carrying a count.** Each is a different place to go and put right. And an issue saying "n groups" cannot be told apart from one saying a different n: the harness passed over an echo that had stopped testing anything at all, because 1 issue carrying n=2 and 1 issue carrying n=1 count the same. One per group and it sees 2 against 1.

`RenderTarget` gains `Paged`. A section or a group in `Absolute` has **no** flowing form: its children can only be stacked in the order they were declared, which is certainly not the arrangement that was drawn. `can.absoluteInFlow` says so **before** a byte is written — afterwards it is too late to be told.

Two faults found by building it, none by reading:

- `ImageSource.Data` is a **`byte[]`** the serialiser writes as base64, so it *looks* like a base64 string everywhere a template is read. Appending it to a string gives `"System.Byte[]"` — a `data:` URI a browser accepts and draws nothing for. Found by opening the file;
- and the rule counted **every table row**: a `TableRow` is a `ContainerItem` in `Absolute` too, but its cells are placed by the table, not by an author. The designer's echo matched on the JSON discriminant and so was exact from the start — **it was the harness that caught the two drifting apart**, which is what it exists for.

## 5b-bis. Two places where absolute layout drew nothing

Putting a picture beside a paragraph — a `ContainerItem` in `Absolute`, the picture at a coordinate with a width, the paragraph starting after it — found **two engine faults**, both in the corner §7 is about.

**An `AdvancedTextItem` placed absolutely drew nothing at all.** It is drawn through the synthetic `TextItem`s `PlaceAdvancedLines` makes, and `Arrange` never called it: the item was laid down as an empty rectangle and produced no marks. Silently, and only off the flow.

**And a child with a width of its own was measured across the whole container.** `ContainerHeight` passed `content` to `ChildHeight`: a 60 mm picture beside a text came back at the height of a 140 mm picture, and the group was twice as tall as anything in it. It is measured at the width `ComputeHorizontal` will actually give it.

**`Dock` is not Delphi's `Align`.** `ArrangeChildren` passes **the same area to every child** — no client area is taken away. "Picture `Dock=Left` + stretched text" lays them one on top of the other. It is an **anchoring**: stuck to an edge, or stretched between two. Hence the flow editor computing the coordinates instead of setting a `Dock` and hoping.

## 5c. Two faces, one core — and the core knows nothing of the page

`apps/designer/` now carries twelve files rather than one:

| file | what it holds |
|---|---|
| `frisket-core.js` | the model, layout, SVG rendering, binding, validator, wording |
| `frisket-runs.js` | a list of `TextRun` bound to an editable surface, and back |
| `frisket-chrome.css` | the palette and the handful of controls both wear |
| `frisket-designer.html` | the face at coordinates (`LayoutMode.Absolute`) |
| `frisket-editor.js` | the flow face (`LayoutMode.Stack`), and all of its behaviour |
| `frisket-editor.css` | that face's own sheet - **not** audited by the palette check |
| `frisket-editor.html` | the face that **ships**: the editor, no header, no panel, files in the toolbar |
| `frisket-editor-full.html` | the same editor with all of it showing, for working on the editor |
| `frisket-editor-element.js` | the same face as `<frisket-editor>`, in a shadow root (§5c-quater) |
| `frisket-editor-embedded.html` | a host page with two editors, proving each is its own and the palette crosses the boundary |
| `check-against-engine.js` | the harness that confronts the echo with the engine |
| `check-editor.js` | the harness for the flow face alone |

**The table sent people to a shell.** It said five files and named
`frisket-editor.html` as the flow face; that file has been seventeen lines of tags
since the behaviour moved into `frisket-editor.js`, and an agent navigating by this
table opened it and found nothing. *A file table is a set of paths in prose* (§6).

**The sharing is not a product decision, the model dictates it**: the two faces are
the two layout modes. And §7 says which one is the main path — the flow — so the
"light" editor is the face of the common case, not a cut-down designer.

**The core touches no element, reads no control and stores no preference.** The
harness runs it in a `vm` context with no `document`, no `window` and not one stub: a
single `document.getElementById` put back inside it fails the pass. Its one dependency
is **declared** — `setTextRuler`, the same boundary as `ITextMeasurer` on the engine
side. A browser gets a canvas by default; elsewhere one is supplied.

What had to move for that to be true rather than merely tidy: the ruler (above),
`readData`, which read a text area and becomes `setData(text)`, `render`, which read
`ui.showBands`/`ui.showBoxes` and now takes an argument, and the other way round the
theme, `setLanguage` and `applyStaticText` pushed down into the page. The core keeps
the dictionary: **the wording of an issue is not chrome**.

**The rule that holds the flow editor up: one `contenteditable` per text block, never
one for the sheet.** An editor whose whole page is editable has to treat a picture or
a table as a foreign body inside the text — that is why images and tables are
half-broken everywhere else. `Body.Childs` is already a list of peers; the DOM is
built the same way and the problem does not exist.

**A child of a `contenteditable` is text.** The editor showed the block's kind
("Advanced text") in a tag placed inside the editable block: it appeared in the middle
of the sentence, you could type on it, and above all reading the runs back
**concatenated it into the paragraph** — `"Advanced textBonjour"` went into the
document. It is a pseudo-element (`::after` on `data-kind`) since: it is not in the
tree, so none of that is possible.

The check had not seen it because it **laid the runs directly** instead of going
through the typing — it skipped exactly the path it was checking. And the first
sabotage went green twice more: the probe overwrote `childNodes` (wiping the label
before reading it), then the sabotage placed the label *before* `runsToSurface`, which
empties the node. **A sabotage that does not reproduce the exact fault proves nothing
either** (§6) — it has to be put where the fault was, not where it is convenient.

**A tag with no text is thrown away by a reader that counts text.** `surfaceToRuns`
skipped every element whose `textContent` is empty — which a `<br>` is by definition.
Every line break in a paragraph therefore came back as one block at the next redraw.
The engine has **always** known how to split on a `\n` in a run
(`BreakAdvancedText` does a `Split('\n')` and closes the line): it was the reader that
lost what the writer knew to keep.

**Enter goes to a new line, shift+Enter creates a block** — the reverse of the usual
convention, and the right choice here: a block is a paragraph of the document, and
wanting a second line of the same paragraph is far more common than wanting a second
paragraph. It is the rare gesture that takes the modifier.

**The block is the furniture, the surface is the text.** Twice, something added to a
block ended up *inside* the paragraph and saved into the document: the kind label,
then the drag handle. The first time I made a pseudo-element — a fix that settles one
case and leaves the trap in place. The second showed that the trap was the structure:
the block **was** the `contenteditable`.

Since 6 August 2026, a text block is a `.block` holding a `.surface`: the handle, the
label, a resize grip are added to the first without ever touching the second.
`editableIn(node)` answers "where the text lives", asked for rather than assumed, so
that the next change of shape breaks one line.

**And a drag handle has to be an element of its own.** The block was its own handle: a
paragraph was therefore dragged by its `contenteditable`, where the browser would
rather select text, and a pair was dragged by the picture in one of its cells, where
the gesture is ambiguous even for a person. Both read as "dragging no longer works"
without either being visibly broken.

**An editable element accepts whatever the browser cares to put in it.** Dragging a
picture to move it **duplicated** it: the native drag drops a copy of the HTML into
the nearest editable element. The model gained nothing from it — `surfaceToRuns` reads
one tag per run and text nodes — so the page showed two pictures and the document
carried one, until the next redraw. Pasting rich HTML does the same.

The answer is not to forbid dragging — that was the first response and the wrong one,
it took the gesture away instead of repairing it. The two are **separate**: a foreign
drop is not taken (`dragover` does no `preventDefault`, so the browser refuses on its
own), and a drag started from one of our blocks **moves** it. `paste` inserts plain
text, for the same reason. Cleaning up afterwards is exactly how the display and the
model drift apart.

**And a list that draws nothing is the same fault with the signs reversed.** The size
and family lists never reported what was under the caret: the line that touched them
only ever set `disabled`, and the size list went further and put itself back to Normal
after acting - "it acts, it does not hold a state". That was a decision, and it was the
wrong one for a control sitting in a row of stateful ones: B, I, U, S and both
alignments all report what is there, so a list that looks the same and reports nothing
is the odd one out. *Reported by somebody moving the caret about*, which is the only way
this kind of thing is ever found.

A mixed selection puts the list on **nothing at all** rather than on its first run's
value - a list stating a size the text has not got is worse than one saying nothing.
Which needed the DOM double mended first: `value` and `selectedIndex` were two plain
fields there, so assigning one left the other lying about, and the sabotage that made a
mixed selection report its first value went green on a stale `-1`. They are one state
now, faithful in both directions. The Range that answered `''` to `toString`, the
`dataset` that kept numbers, and this: three times the double failed to model exactly
the property the check was aimed at.

**A face that records a choice without drawing it is worse than a face that cannot
make it.** Text alignment was right in the model, the button lit up, the JSON was good
— and the paragraph stayed where it had always been, because the block never set
`text-align`. The exact symmetry of the fault next door (the label on screen and
absent from the document): both times, the display and the model said different things.

**A style was applied, and the words stopped being selected.** Reported from the
browser after months of it: select, press Bold, and the caret is at the foot of the
paragraph with nothing selected — so italic after bold means selecting again, and the
gesture a person actually makes is select, bold, italic. The buttons already went to
the trouble of a `preventDefault` on `mousedown` to hold the selection while pressed;
`draw()` rebuilds the sheet from the model a line later, every node the selection
pointed at goes with it, and `focusBlock` collapses to `"end"`. *Care taken in one line
and undone in the next reads as no care at all.*

`rangeAt` is the inverse of `offsetsIn` — flat character offsets back to a range — and
`reselect` lays it in the redrawn block. Three call sites had the same three lines
(`applyStyle`, `applyAlignment`, `applyVertical`), so all three had the same fault.

**And its check could not have been written against the harness as it stood.** The DOM
double's `Range` answered `''` to `toString()` and dropped `setStart` on the floor,
which is enough while nothing asks what is selected and makes the question
unanswerable the moment something does — two empty strings compare equal. It has a real
`Range` now, a `createTreeWalker`, and a selection that keeps what is put in it. A
second gap fell out of the same work: `dataset` stored what it was given, so
`node.dataset.index = 0` read back as the number `0` and `dataset.index === "0"` — how
the page finds a block after a redraw — was false for every block. A real `dataset`
stores strings, always.

Four sabotages, and **two of the four were green on the first writing**:

- taking out the read-back (`cached = offsetsIn(node, range)`) changed nothing, because
  the value it recomputes was already right. The line guards the case where the offsets
  asked for cannot be honoured, and nothing reached it — *unfalsifiable is not the same
  as correct*. A call to `reselect` past the end of the text reaches it now;
- taking out `addRange` itself left the check green, because it read the **characters**
  and a detached node still carries its text. So it asks whether the sheet on screen
  **contains** the selection's node — §2 again, in a third domain: check that the parts
  reach each other, never that they exist.

**A guard at the top of a key handler swallows everything after it.**
`if (!current.isContentEditable) return;` at the head of the `keydown` made the picture
— the only block that is not editable — the only block that could not be **moved**,
right after a drag had been taken away from it. What must hold for every block goes
**before** the guard, not after.

And its check was vacuous a third way — it **searched the source for `altKey`**
rather than pressing the key, so replacing the guard left it green (§2); it fires
the event on a non-editable block now.

**And the same guard fell over again, worse, the moment the block/surface split
arrived.** The surface *and* the block both called `markCurrent`, the event bubbles, so
the block ran last: `current` is **always** the block, never the surface — the two
lines put on the surface never decided anything. `current.isContentEditable` was
therefore false for **every** paragraph of the document, and the guard written to let a
picture through now swallowed Enter, Backspace and the arrows. Further down, the same
test in `selectionchange` left `cached` forever null: bold, italic, colour and size all
read `cached`, so **the whole style bar was mute**.

Three distinct lessons:

- a property one **assumes** (`current.isContentEditable`) survives a change of shape
  by lying; one that is **asked for** (`editableIn(current)`) fails to compile or leaps
  out. The handler now names its two needs, `caret` and `block`, and no longer reads
  `current` raw;
- a guard that lets **one** known exception through is a guard that will break at the
  next shape. This one broke twice for the same reason;
- **the harness set `cached` by hand**, so it skipped `selectionchange`, the only thing
  that writes it — and the style-bar check said yes for the whole time the bar was
  dead. The Enter check did worse: it put `current` on the **surface**, a state the
  browser never reaches. *A check that puts the page in a state it never reaches proves
  nothing* — a variant of §2, seen from the state rather than from the path.

**Two handles in the same gutter, and one covers the other.** `.block > .handle` sits
at `left: -24px`; the handle of a pair's **first cell** was there too, since the
left-hand cell starts at the block's edge. Exactly one over the other — so the picture,
which is the left-hand cell in the arrangement that was reported, could not be taken
hold of at all, while the text moved perfectly well. A half's handle now sits **inside
its own corner**, with a background of its own: over a photograph the gutter was never
legible anyway. The harness reads the stylesheet, because that is where the collision
was and nothing in the model can see it.

**An editor that draws a photograph at its size in pixels is talking about the file,
not the page.** The picture is shown at its width in millimetres, the one that will be
printed, and a corner is dragged to change it — width only, the height following the
picture's proportions. On insertion the dimensions are **measured**: `newItem` hands
out 40 × 20 mm, a reasonable stopgap and the wrong shape for almost any real picture.

A face may drop tools; it may **never** drop the **checking**. A simplified editor that
quietly produces a document `frisket check` refuses is the worst thing this project
could ship.

**That sentence said "the checks panel" until 28 August 2026, and the word was wrong.**
The rule is about *silence*, not about a rectangle on the right of the screen. Somebody
typing a letter into a host application has no business being shown a render target, a
page size or a font source — the host chose those when it decided what it was producing,
and offering them again invites two answers to one question. What that person must not
get is a document nobody ever questioned.

So `panel: false` hides the findings and does **not** stop them: `runChecks` runs on
every redraw as it always did, and hands the result to `onChecks` — the panel when there
is one, and otherwise the host, through the `checks` event on `<frisket-editor>`. A host
holding the findings and deciding what to do with them is not silence; it is the same
answer this project already gives twice, for a font's `fsType` (§8b) and for an imported
page (§8h): **only the caller can know, and the engine says so.**

The amendment is only honest because it is checked. `check-editor.js` mounts the lean
shape and asserts the findings still arrive at the seam, and the sabotage that moves
`onChecks` below the panel's early return — the exact way a face would go quiet — takes
that check down. It was written *before* the shape it guards, because the harness that
owns this face could not see a control being removed at all: `getElementById` invented a
node for every id it was ever handed, and a copy of the page with twelve ids deleted and
every reader left in place ran 124 checks with no failure.

**And hiding Save is only legitimate because there is another door.** The Save button
built a Blob and clicked an anchor, and that was the editor's **only** egress: a host
that switches it off without `element.report` would be shipping an editor whose work
cannot be collected.

And what a face cannot edit, it **names and keeps exactly as it is**, saying where it
can be edited. Showing half of it would be worse.

## 5b-ter. Each face builds into one file, and there are four rooms now

**One file.** The content editor ships, and it shipped as five files that must sit beside
one another - which is fine for a repository and wrong for everything else: a server
needs a route for each (§6's dead page), a mail attachment has nowhere to put the other
four, and "open this in a browser" stops being true the moment one is missing.

So the parts stay the source and `release/*.standalone.html` is **generated** from them
by test 93, in the order the page itself names them. Not written twice: a second copy of
an editor is §1's whole subject, and it is always the one nobody updates.

**`release/` is the line between built and shipped.** What is built lives under `bin/`
and is not tracked; what is shipped lives there and is, on the same side of that line as
the schema and the documents under `tests/output/`. Both faces are written there even
though only the editor is held to release quality: which one is *presented* as a product
belongs to a README and to wherever it is published, not to a path, so the day the
designer joins it nothing has to move. The `.standalone` suffix survives the move because
§6's rule outlives the directory - `frisket-editor.html` in two places is two things a
fuzzy file open cannot separate.

It needed a **negation in `.gitignore`**, and the shape of it matters: `[Rr]elease/` is
MSBuild's output and matches that name at any depth, so the pages were generated,
committed, and simply absent. The re-inclusion has to name the **directory** - git stops
walking an excluded one, so `!/release/*.html` would never be reached.

**And moving the pages broke a harness that nothing would have reported.**
`check-against-engine.js` found the engine's sources by counting two levels up from the
page it was handed - true while every face lived in `apps/designer/`, one level too many
from `release/`, where it read above the repository and died. It walks up to
`frisket.slnx` now, like `RepositoryRoot()` (§12). *A path computed by counting is a path
that moves when its subject does.*

The palette audit was the quieter half of the same move. It sweeps **every** `.css`
beside the parts rather than the ones a page links (§5b), and `release/` has none beside
it - so the sweep would have found nothing, audited only what the page inlined, and said
so in a green line. It sweeps the parts directory, found from the root. Then a built page
carries a *copy* of every sheet it swallowed, which read on top of the parts gave 278
declarations against 139 and made "light sets the same names as dark" compare dark with
its own duplicate - §16 in miniature. Deduplicated on content with the generator's
`/* name.css */` provenance line discounted, both faces report the same numbers, and that
agreement is the assertion that the built one carries the same material.

Sabotaged in the three directions that matter: a colour out of palette in a **part** is
caught on both faces; one injected into the **built page alone** is caught there and
rightly not on the split one; and the same page copied to a third depth reports exactly
what it reports from `release/`, which is what says the anchor is found rather than
counted.

**The trap this kind of build always has** is that a script folded into a page ends at
the first `</script>` in it - in a string, in a comment, in a regular expression - because
a browser looks for those characters and does not read the JavaScript around them. It is
broken up on the way in, and *the check for that was true for want of matter*: no part
carries the sequence today, so removing the escaping left the test green. The build is
handed a part that does carry it now (§10c: build the window, do not widen the rule).

**And the harness hid a hole the standalone found.** Pointed at a single-file face it
reported 164 against 162 - no failure anywhere, four checks simply not run, including the
only one proving the core touches no DOM, because the block read *linked* scripts alone.
Made to read the merged parts instead it failed **honestly and for the wrong reason**: a
merged page cannot answer "is the core separable", since the core and the page are one
script there. Three ways to be unhelpful, and the third is the only true one - it says so
now, in a line of its own, rather than passing or failing:

```
-  the core is separable: not asked, this face is one file
```

**Seven themes, then four.** Four dark ones were added - `press`, `graphite`, `indigo`,
`slate` - each proposed against a stated direction and then **measured**, never admired.

What the measuring was for, since a palette is exactly the thing one is tempted to judge
by eye:

- the **elevation rule** this project already paid for - `--line` barely above the
  `--panel` it bounds, `--control` at or just below it. All four landed in +3.0..+3.9 and
  -1.6..-2.4, against the then-shipped dark's +4.4 and -1.9;
- and the pairs easy to leave colliding. **`--accent` against `--muted` is the commonest
  adjacency in a panel** - a label beside a lit control - and the first draft of three of
  them put the two under 4 L\* apart where that dark kept 21.7.

Two faults found by arithmetic and by nothing else. `slate`'s drop target lifted 3.5 L\*
over its panel while its hover wash composited to 6.3, so **a merely hovered row read
louder than a live drop zone** - the signal hierarchy upside down. And `slate` and
`graphite` both had `--warn` and `--caution` about one L\* apart: §5b's opening story, two
names for one colour, reappearing inside a fresh palette.

The passage above ended *"nothing here has been opened in a browser, which §4 says is the
check none of the others replace. The numbers say the contrasts and the elevations are
right; they do not say the rooms are pleasant."* **Using them is what answered it**, and
the answer was three fewer rooms: `press`, `indigo` and `slate` are gone, and what is
offered is **Auto, Graphite, Light, Frost**. The measurements stay here for §3's reason -
they were the work, and a number deleted with its subject teaches nothing.

**Graphite is not merely kept, it *is* the dark room.** The stylesheet's unnamed
`:root, :host` block is what `auto` reads on a dark machine, so leaving the old
blue-leaning dark in it would have made Auto and Graphite two different rooms while the
selector offered one of them. Measured after the move: `--line` +4.7 over the panel,
`--control` -1.9, `--accent` 11.2 L\* clear of `--muted`, `--chrome-ink` 10.1:1 on the
panel - and 10.1:1 to 13.4:1 across the four, where the comment claiming "9.8:1 to 13.4:1,
measured across all seven" had to be re-measured rather than carried over.

**The rooms are one list now** (`THEMES`, in `frisket-core.js`), read by both faces and by
the harness. Before that the list was written by hand in two faces and confronted with
nothing - not with each other, not with the stylesheet, not with the wordings. Four
lists, four silent ways to disagree, and the commonest is the worst: an option whose rule
has gone stamps `data-theme` on the root, nothing matches, and the page falls through to
the base palette. The picker says Slate and the room is Graphite.

And a **stored** name outlives its option. The preference sits under one localStorage key
shared by both faces and was handed straight to `setAttribute`, so anyone who had once
picked Slate got the base palette in silence and a picker left blank - assigning a `value`
with no matching option leaves a real select at `selectedIndex -1`. `themeOrAuto` answers
it. Its check was the one sabotage of four that **went green on the first writing**: three
lists confronting each other cannot see a guard, and replacing it with `name => name ||
"auto"` changed no count and no line of the run. It is asked of the function now.

## 5c-bis. The face was narrower than the engine, twice

Two refusals that looked alike, and were the same: the flow editor forbade what the
engine has always been able to do.

**A cell held one item, so the second was refused.** It was the right refusal for the
shape of the time and the wrong shape: a cell holds **one item, and an item can be a
container**. An occupied cell keeps what it has and gains what arrives, stacked
underneath. A third joins the same stack rather than making a stack of stacks; a stack
back down to one item **stops being a stack**, exactly as a pair back down to one half
does — otherwise the tree keeps a wrapper nobody asked for.

**A pair accepted only a picture and a paragraph.** Measured before a line was written
(§5d): a table at a coordinate with a width sits perfectly beside a paragraph — table
0–60, paragraph 64–210, wrapped inside its own column. The engine had nothing to learn.

What decides is therefore **not the type but the geometry**: `naturalWidth(item)`
returns a width or `null`, one side is fixed and the other takes the rest, and two with
no opinion share the band. Hence three new arrangements without one line of special
case: table + text, text + text, picture + table.

**And the difference between a picture and a table is real, not cosmetic.** A picture
has no size of its own in the document — the width is a choice — so it is resized. A
table's columns are in millimetres and **do not stretch**: measured, a table of 30 + 30
forced to 85 gives a frame 0–85 whose cells still stop at 60. Capping a table would
**lie**. A pairing that would leave nothing readable beside it is therefore refused —
and refused by **not lighting the zone**, the rule this face already follows: never
offer a drop you will decline.

Two vacuous checks on the first writing, both of the same family: **an assertion true
for want of matter**. `newTable()` states no column width, so `stated` was `null` and
the comparison passed whatever the code did — the test now asserts first that the width
exists. And `find(isPair)` returned the **first** pair on the sheet, left there by an
earlier check, not the one just made.

## 5c-ter. A panel shows what is selected, and nothing else

The designer showed seven controls under a section's heading, of which **only one
belonged to it**: the page size. The other six — root font size, title, author,
subject, keywords, language — belong to the **file**. Hence the symptom reported:
changing the title with one section selected changed it for all of them.

The behaviour was right — `Report.Title` is the PDF's `/Title`, and there is only one —
and it was the panel that lied. As long as a report had one section the two levels were
the same; sections separated them and the panel did not follow.

**Two headings were tried and rejected on sight**, rightly: a properties panel shows
**what is selected**. An honest heading above somebody else's fields does not earn them
a place, it only **labels the category error**. The fix is structural, not typographic.

The tree is therefore rooted on the **report**, with the sections as children.
Selecting the report shows its fields through the ordinary mechanism — no special
heading, no exception — and selecting a section shows only its own. It makes the shape
of the file visible, until then legible only through a button that counted
"Sections (2/3)", and **selecting a section is now the way to open it**.

Three things that asked for nothing, and had to be verified rather than deduced:

- **a section can be neither dragged nor deleted**, because `parentOf` searches from
  `activeRoot()` and returns `null` for a section. The existing guards already
  answered; the check now asks instead of hoping;
- **the canvas still draws one section**, which is right: selecting the report has
  nothing new to draw;
- **the report is not an item on a page** — no position, no role, no deletion. It is
  the early `return` that says so, not a heading that would claim it.

One detail caught by the check and not by reading: the panel's heading called
`labelOf(item.$type)`, and a report **has no `$type`** — the page showed
`type.undefined`, a missing key for a name.

**And the harness had to grow for it.** Its DOM stub threw away every `appendChild`; as
long as nothing asks what a panel produced that is enough, and perfectly useless the
moment the question is "which fields did it show". It now keeps its children. The probe
reads `innerHTML` too, without which it declared the tree empty: the stub cannot parse
markup, and that is how the tree writes its rows.

## 5c-quater. The editor packaged as an element, and what a shadow root breaks

`<frisket-editor>` puts the content editor in a shadow root, so a host page's stylesheet
cannot reach in and the editor's cannot leak out. What crosses on purpose is the palette:
custom properties inherit through the boundary, which is what lets a host theme it by
setting `--accent`, `--panel`, `--ink` on the tag.

The boundary is named, and it is **handed over** rather than counted: an editor is
`frisketEditor(seams)`, and the seams - the root its markup lives in, the div it is
planted in, what carries `data-theme` and `--paper`, what this face shows (§5c), what is
about to be produced, where the findings go, whether to take the focus - are listed once,
at the end of that function. The element and the plain page pass the same names, and
call `mount()`, `loadReport()` and `report()` on what comes back.

*It was stated twice and the two disagreed before any of this*: "five named seams" here
against "the four of them are the whole of the boundary" in the element's own doc
comment, which never counted `selection()`. A number in prose is a claim like any other,
and two claims about one thing drift the moment either moves — so the list lives in one
place, and neither this section nor the element carries a total.

**One editor per call, not one per page.** Until 18 October 2026 the seams were
variables at the top of `frisket-editor.js`, which the element *set* - so a second
`<frisket-editor>` set them over the first one's. The first went on showing its sheet
while its root, its document and its selection were the second's: type into it and the
words landed next door. Nothing failed, and nothing could, because the harness had one
editor and one id space, and the element was covered by no check at all. *A shape that
allows one of something is a requirement nobody wrote down* - the author's, as it turned
out: several editors on one screen, "limite prioritaire".

Everything an editor changes as somebody types lives inside the function now. What stays
at the top is the page's - the interface language, and the preview server's fonts, asked
once and loaded once however many editors want them - and check-editor.js holds the top
level to exactly that list, because the checks on two editors would catch the document or
the root going back out and would not notice the selection cache doing the same
(sabotaged: one failure, and only that check). It drives two real `<frisket-editor>` in
shadow roots of their own: two documents, typing that reaches one, findings to the right
host, a room each.

Two faults the second editor exposed, both present with one: an embedded editor set
`document.title` on every redraw of its wording, **renaming the host's tab**; and every
editor took the focus on mounting, so a page of three opened scrolled to the last. The
title is the page's now, and an element takes the focus when it says `autofocus` - HTML's
own rule. And side by side showed what one editor across the window hid: a face with no
panel still reserved the panel's 300 pixels, and the editor stopped where its content did
rather than filling the element a host had sized.

The harness had to follow the editor inside: it plants its handles at the `// @harness`
line of the function, and refuses to run if there is not exactly one. And the core being
`"use strict"`, an `eval` keeps its functions to itself where a browser would make them
global - so the element found no `frisketEditor` until the harness put it where a page
would.

One line
already funnelled every lookup - `const el = id => document.getElementById(id)` - which
is why 59 uses of `document` turned out to be about fifteen sites. **Everything else
`document` does there is the browser's and was left alone**: `createElement`,
`createRange` - a `ShadowRoot` has no `createRange`, so pointing that at `uiRoot` is a
`TypeError` - `execCommand`, the drag listeners that must outlive the pointer leaving
us, and the detached preview's own document.

Three faults, all of them invisible until there was a shadow root to fail in.

**A Range is not scoped to a tree, a Selection is.** `focusBlock` did `node.focus()` and
then `window.getSelection().addRange(range)`. The second is **ignored, silently**, for a
range whose nodes live in a shadow root: the caret is drawn where `focus()` put it and
the selection is somewhere else. A page where the cursor blinks and nothing types, with
no error anywhere. `uiRoot.getSelection()` is right for both a document and a shadow
root.

**`:root` matches nothing inside a shadow root, so the whole palette was missing.** The
chrome declares its 24 names on `:root`, nine times. The editor came up with only what
the host page happened to set on the tag; `--paper` was not among them, the sheet fell
through to the host's dark ground, and the text kept its ink - characters the colour of
the paper they are written on. The contract written in July was right, and said so:
"every colour is a custom property a host can override, **and one day the theme of
whatever page embeds this**". *The selector was wrong, and there was nothing to fail in
until today.* Every rule names `:host` beside `:root` now.

**A listener registered before a seam is set can never follow it.** `keydown` and
`selectionchange` were top-level statements, run when `uiRoot` was still `document`.
`selectionchange` is fired **at** the root of the selection rather than bubbled up to the
document, it is the only writer of `cached`, and the whole style toolbar reads `cached` -
so bold, italic, underline, strike, colour, size and family are silent while the
alignment buttons go on working, which reads as a broken toolbar rather than a missing
selection. Both are bound in `mount()`.

**Widening the palette audit meant editing a check in the same pass as its subject**
(§16), so it was sabotaged afterwards: with `--accent` taken out of the light theme it
still names `--accent`, `--on-accent` and `--drop` as missing.

**And VS Code's integrated browser is not a real reader.** It showed the dark sheet from
a cached stylesheet long after the fix was in, while Edge showed it working. It joins
Edge and Foxit on §4's list, and it cost the better part of a diagnosis: *the thing that
disagrees with the others is not always the code.*

## 5d. Look for the property before adding one

Aligning a picture first produced `ImageItem.Alignment`, written, wired and rendered —
before discovering that `Style.AlignHorizontal` had existed all along, documented with
exactly the question being asked: "how to centre an image, a table or a field". In
`ComputeHorizontal` it already places a frame of given width inside its band.

The two would have shared the subject without ever meeting — until the day a picture
had a width. The new property was removed; `ReportRenderer.Fit` reads the same
`AlignHorizontal` for the other half of the question, that of a picture given the whole
band and fitted inside it. **One property must answer both, or the two will contradict
each other.**

Its vocabulary differs from a text's — `None/Start/Center/End` against
`Left/Center/Right/Justify` — because it must also be able to say "wherever the
coordinates put it", which a text alignment does not say. The editor translates at the
seam rather than unifying: they are two neighbouring questions, not the same one.

## 6. Running the tests

```bash
dotnet test tests/test.csproj                                 # the whole suite
dotnet test tests/test.csproj --filter FullyQualifiedName~Test30
dotnet test tests/test.csproj -l "console;verbosity=detailed"  # the narrative output
```

**A split file is served differently.** The server sent `frisket-designer.html` as *content*, one route for one file. Since the core was split out the page loads `frisket-core.js`, `frisket-runs.js` and `frisket-chrome.css` beside it — and the server had a route for none of the three: **the designer as the server served it was dead**, every request a 404. The harness loads the files itself and a `file://` URL finds them beside it, so the only way of opening the page that goes through the server was the only one nobody was trying. The whole directory has been served since 5 August 2026, and `/editor` serves the second face.

**A command in the prose is a path like any other.** The repository reorganisation moved four directories and the docs kept the old names: `dotnet run --project server` when the project is at `apps/server`, `cli/Frisket.Cli.csproj` when it is at `apps/cli/`. It reads as a missing project — you run the command, nothing starts, and you go looking for the fault in the solution. Fixed on 5 August 2026, with an audit comparing **every path the docs tell you to type** against what exists on disk.

`frisket.slnx` carried **only** the suite: the CLI, the server and the C boundary compiled nowhere, and a compile error sat there waiting for someone to run the thing by hand. Worse, a stale binary looks exactly like an up-to-date one — a CLI fix was judged to have had no effect because `dotnet build frisket.slnx` said "Build succeeded" without recompiling it. The four projects have been in it since 4 August 2026.

**The count went from 55 to 54 on 4 August 2026, and the suite gained by it.**
`Test34_JsonDefinedReports` could not fail, for three reasons any one of which sufficed: it looked for `TestReports/` beside the root when the files were under `tests/`, so it printed "directory not found" and handed back green; had it found them, it did no more than deserialise and print a number, never comparing anything; and it swallowed every exception, so malformed JSON passed too. The expectations it was meant to check also restated what tests 1 to 5 already assert in C# — a second place to keep in agreement, §1 applied to the tests.

Hence the rule: **a test you have never seen fail is not a test.**

**And a test can hold on one platform only.** Test 29 searched a template's JSON for a line
ending in `\n`. The JSON writer ends lines with the platform's newline, so on Windows the
line could not occur and the assertion held whatever the item carried; on Linux it found a
family the *report* declares, legitimately, and failed. It never once looked at the text it
names. Asked of the parsed tree now. A second platform is a second reader of every check,
and `WSL-Frisket` exists to be one. Sabotage is not a courtesy paid to the important tests, it is what separates them from an executable comment. And a falling test count can be good news: what counts is the number of things checked, not the number of methods.

**And a harness can hold on one line ending only.** A fresh Windows clone hands every file
over in CRLF - `core.autocrlf`, git's own default there - and the palette audit tells a
theme's own rule from the last line of a selector list by the comma before the break,
`(?<!,)\n`. In CRLF the character before `\n` is `\r`, so the list that declares `--paper`
for every room at once read as a fourth room missing all twenty-four names. The run
failed on a clone and passed in the tree it was written in, where the edited files had
come back LF; found by checking a commit in a worktree of its own, not by any check. Both
harnesses read text through `readText`, which normalises, and each asks of its own source
that no read goes round it - the next raw read is how this comes back.

**And a sample can follow the checkout too.** A clone built the way a Windows runner builds
it - CRLF, then the suite - left six tracked files different from the commit, and nothing
failed. Five PDFs carried the Factur-X invoice, a C# raw string, and a raw string takes the
line endings of its source file *as git wrote it*: 17 more bytes attached. The sixth was test
57's "not a font", which was the README's bytes. Both are fixed at the source, and the check
that finds the next one is the same run: build a CRLF clone, run everything, ask
`git diff --exit-code`. Not `git status`, which on that checkout listed 81 files - every
text file the suite rewrites in LF over a CRLF copy, identical once normalised.

xUnit, one `[Fact]` per test, all in `EngineTests` — so a single collection, run serially: the tests write into the same directories.

**One class, one file per subject.** The suite was two files until 24 August 2026, and the
larger — `AcmeInvoice.cs`, 8,160 lines — held everything from font fuzzing to the JSON
schema. It was reported the only way this kind of thing ever is: somebody could not find
the test for a feature they had asked for the day before. Every file is now
`public partial class EngineTests`, which is what keeps the single collection *and* means
**placement costs nothing** — no member is hidden from any other, so a file name answers
one question only, which is where a person would look. `EngineTests.cs` holds the
scaffolding and **no tests**, deliberately: a file named after the class is where an
unclassified test goes to be lost.

Two names were refused for shadowing an engine source — `TemplateValidator.cs` and
`FlowingHtml.cs` — because in a fuzzy file open the subject and its test are then
indistinguishable. They are `Checks.cs` (the word the CLI already uses) and
`MailOutput.cs` (`--format mail`).

The moving was mechanical and checked before a byte was written: every member claimed
exactly once, every name in the plan real, the multiset of non-blank lines unchanged, and
the count of test attributes unchanged. That last one read 70 where the suite runs 72
until `[SkippableTheory]` was added to the list — *the attribute nobody thinks of is the
one on the only test allowed to decline*. And the cleanup then deleted `AcmeInvoice.cs`,
which is both an input and an output: forty-eight compile errors, not one of which named
the missing file.

`AssertEqual(testName, property, expected, actual, tolerance)` is kept exactly as it was and delegates to `Assert.True`. The first argument has been redundant since the migration, but the second is what makes a failure readable.

## 7. Absolute layout is the rare case, and that is where it breaks

The whole engine is built for flow; a form pinned to coordinates is the exception, and so the least travelled path. Two faults slept there, found by reproducing a real fee note and fixed together (test 43):

- `Layout` is `Absolute` (the zero of the enum) at the property level, but
  the four containers that have to flow — **`Body`, `TableItem`, `ImageGridItem`,
  `SubTemplateItem`** — set themselves to `Stack` in their constructors. A `Header`, a
  `Footer`, a `Section` and a plain group keep `Absolute`, and an absolute container
  hands every child the same box, so two children of a band that says nothing land on
  top of one another. An explicit `Absolute` was **omitted** on writing, and read back
  as `Stack`.

  This sentence has been wrong twice in one day. It said "Body, Header and Footer" for
  eight months; the correction said "Body alone, and the other two have no constructor",
  which is wrong about all four — Header and Footer *do* have constructors, setting
  `Dock` and not `Layout`. The second reading looked at a five-line `sed` window and
  generalised from it. *A window is not a class*, and a correction is a claim like any
  other: it deserves the reading the original did not get.
  Binding does a JSON round trip, so **every** bound document was stacked;
- a width imposed on a text served to compute its height — the box was indeed three
  lines tall — then the text was drawn on **a single line** across whatever sat
  beside it. Backends split on `\n` and wrap nothing;
  only the flow did.

Corollary of the second: only re-wrap on a width that was **wanted**. When the width comes from measuring the text, the two numbers are equal to the bit and the last word drops to the next line — which broke test 23's total.

Third lesson from the same exercise, this one visible only on screen: **one document's coordinates are not another's**. The source shifts its strokes by a few points from one copy to the next while leaving the text where it is; take the strokes from one file and the baselines from another and you get a drawn rule across a word. Take every measurement off **one** copy.

`ShowWhen` / `HideWhen` on `BaseItem` answer the gap: a path into the data, and the item goes to `Gone` — so taking no space — when there is nothing at the end of it. Empty is the useful reading: absent, `null`, blank string, zero, false, empty list. Added to the engine, to the designer and to both languages' wordings (§1).

## 8. The C boundary is checked by the document, never by the return code

`bindings/native/` publishes the engine as a shared library behind twenty flat entry points. Three things not to learn again:

- **native publishing needs a linker, and the message that is missing is not the one
  you would think.** ILCompiler finds it by running `findvcvarsall.bat`, which calls
  `vcvarsall.bat` and takes **the first line of its output** for the path to
  `link.exe`. When `vswhere.exe` cannot be reached, `vcvarsall` still works
  but complains first — and it is the complaint that becomes the linker
  path. The build then fails naming `'vswhere.exe' is not recognized`
  as a program, which is a very long way from what is actually wrong.

  `vswhere.exe` lives beside the installer and is **not on the PATH** by default.
  Putting it there is enough: `dotnet publish` then succeeds without `vcvars` and
  without `-p:IlcUseEnvironmentalTools=true`, and it is also what makes the
  Publish button work from Visual Studio, whose process reaches its own installer.
  Checked both ways on 20 August 2026. `build.ps1` does it, and keeps the fallback
  through `vcvars64.bat` for a machine where that would not be enough.

  The settings — RID, configuration, output directory — are no longer in the
  script but in `Properties/PublishProfiles/*.pubxml`, which the VS dialog and the
  command line **both** read. The script kept the target name and the output path
  in duplicate, and looked for the output in two places taking whichever existed —
  a way of not knowing. What a profile cannot say stays in the script: which
  toolchain to find, and whether what came out is really there.

  **And the PATH is the fragile half.** On 25 August 2026 the bare `dotnet publish`
  failed with that same `vswhere.exe` message: the entry was gone, and the only VS on
  the machine was 2026, which by then *did* have the ARM64 C++ tools that July's note
  said it lacked. `build.ps1` built it without a murmur, because it puts vswhere on the
  PATH itself. *The line above is what to do once; the script is what knows.* Reach for
  the script, not for the command a working machine happens to accept.

  **`win-arm64` was compiled for the first time** on 20 August 2026 (5,207,552 bytes),
  and **never run**: there is no ARM machine here. Built ≠ checked, and the whole of
  §8 says why the difference matters.

  **`linux-arm` and `linux-arm64` are built and run since 27 August 2026**, under
  `qemu-user`, and each produces the document the CLI produces. Four artefacts now
  agree - three native libraries and the managed CLI - checked with
  `tools/compare-pdf.py`, never `cmp`. `win-arm64` stays built and unrun: qemu-user
  runs Linux binaries, so it does not answer for that one.

  **`build.sh`'s guard was checking a linker the build never called.** It looked for
  `aarch64-linux-gnu-ld` and then published without saying which linker to use, so
  ILCompiler passed `-fuse-ld=bfd` and clang reached for the **host** `/usr/bin/ld.bfd`
  - single-target, answering *"unrecognised emulation mode: armelf_linux_eabi"* and
  listing only x86 ones. A check and the thing it checks not reaching each other, which
  is §2 in a shell script.

  Three things a cross build needs that a native one does not, each failing a long way
  from its cause: **lld** rather than bfd, because lld is multi-target; **that
  architecture's objcopy**, or the link succeeds and the build dies one step later
  splitting debug symbols; and **libssl for the target**, which is not needed to build
  and is needed to run - the runtime aborts with *"No usable version of libssl was
  found"* the first time anything reaches `System.Security.Cryptography`, which the
  engine does for every image it embeds.

  And two traps in getting the packages at all, both of which read as something else.
  Ubuntu serves nothing but amd64 and i386 from `archive.ubuntu.com` - every other
  architecture lives on `ports.ubuntu.com` - and the existing entries carry no
  `Architectures:` line, so adding one foreign architecture makes apt ask the wrong
  host for it and fail for all of them at once. Then `libssl3` is `libssl3t64` on
  noble, after the 64-bit `time_t` transition; the error names the new package, four
  lines below where one stops reading;
- **a library that loads and returns `FK_OK` proves nothing** — §2, in a new domain.
  The only check that counts is comparing the produced document with the CLI's.

  **But no longer with `cmp`.** The native library links the system zlib, the managed
  CLI carries its own: the same bytes come out compressed differently. Measured on
  20 August 2026, 57,960 against 57,909 — 51 bytes apart and **the same document**,
  which an identical `/Length1` on either side announced before anything was
  decompressed. Read with `cmp`, it looks exactly like a broken boundary, and it is
  the worst kind of false negative: the one that accuses correct code.
  `tools/compare-pdf.py` decompresses the streams before comparing and neutralises
  the four things allowed to differ — timestamp, `/ID`, stream lengths, and the xref
  offsets that follow from them. It has been seen to say no in three ways, including
  the only one that really counts: a stream recompressed with one byte changed
  inside it, so the same length and the same structure;
- it is this check that caught the Pascal wrapper **mangling the accents**.
  `string` in FPC's default mode is an `AnsiString` in the system code
  page: anything outside CP1252 vanished before reaching the
  library. Nothing failed; the PDF was merely 380 bytes bigger, the mangled
  characters demanding more glyphs. Hence `UTF8String` end to end, and
  `{$CODEPAGE UTF8}` in every `.pas` carrying an accented literal.

## 8b. What a hostile template gets, and what it no longer gets

Review of 20-21 August 2026, on `engine/` and `bindings/native/` only. Three things
that were not typos but assumptions.

**A template chose which files the engine reads.** `SubTemplateExpander` resolved
through `Path.Combine(baseDirectory, TemplatePath)`, where `TemplatePath` comes from the
template. `..` walked up, and above all **an absolute path makes `baseDirectory`
disappear** — that is the semantics of `Path.Combine`, not a forgotten guard. The file
was read, its body composed into the document; and if it was not JSON, the parse error
**quoted its first byte**. There is now a root the caller imposes, checked on the
**canonical** path through `Path.GetRelativePath` — never a string prefix, which takes
`root-evil` for a child of `root`. Messages now give back only the path **as the author
wrote it**: the resolved path describes the machine to whoever sends the template.

**The number of times round the loop was written in the font.** `ReadCmap` followed a
count that came from the file. The byte reader is forgiving — past either end it returns
zero rather than throwing — and that generosity is precisely what makes an unbounded
count dangerous rather than merely wrong: **the loop never runs out of bytes**, so it
turns exactly as many times as it was told to. Measured: 212 bytes announcing a hundred
million groups cost 0.69 s and were **accepted**. Every count is now capped by `Room()`,
arithmetic on the length of the file. 0.69 s → 0.001 s, at the same size.

**`Guarded` did not guard handles.** It turns every exception into a code, but
`GCHandle.FromIntPtr` on a value that was never a handle is not an exception: it is
`0xC0000409`, out of reach of any `catch`. And a **magic label laid beside the object
settles nothing** — reading it is already a dereference, which is the very thing being
guarded against. Hence a register of the handles handed out, on the managed side:
recognise before following. A fanciful handle returns `FK_ARGUMENT`, and a double free
does nothing.

**`fsType` is read and never enforced.** Those bits say what the **foundry** allows — and
the caller may *be* the foundry. Refusing on them would amount to forbidding someone a
font they drew themselves. Exposed (`EmbeddingRights`, `embeddingRestricted` in
`fk_fonts_list`), held by a test so that it stays a decision and not an oversight.

What held, tried with a payload rather than read over: PDF escaping (`\(`, `\)`, the
UTF-16BE switch), XMP, SVG, HTML, `/T`; colours, which pass through a numeric type and so
offer no attribute to break; and the refusal of an unknown family, a rule written for
typographic correctness which closes injection by font name as a side effect.

**Three times the method lied before the code did.** The first injection pass announced
"nothing leaks" after rummaging through **four exception messages** — the renders had all
failed. The PDF detector then cried wolf over inert payloads **inside a correctly escaped
string**. And the check on the path boundary went green with the boundary replaced by a
string prefix, because my twin directory was a sibling of the wrong folder: the default
root is the **template's**, not the test folder's. *A control case that fails too turns a
hole into a false absence of a hole* — without it, F1 was written off.

## 8c. Fuzzing both byte readers, and what it found

`FontFile.Parse` and `PngImage.TryRead` take bytes from somewhere else. Test 60
disturbs them on purpose, and in a few thousand rounds it found **what two readings
had declared safe**.

**The oracle counts for more than the mutations.** "It threw" is not a failure: both
readers are entitled to refuse, and one does it by exception. A failure is one of the
three things a parser never has a right to do — read outside the array it was given,
believe a size it was told and allocate it, or take longer than the file could possibly
justify.

**Blind bytes are nearly useless here**: both formats open with a signature, so a random
mutation is rejected in the first eight bytes and never reaches the parser. What finds
things is taking a real file and spoiling the **numbers the parser trusts** — lengths,
counts, offsets. Hence mutations on 16- and 32-bit big-endian words rather than on
isolated bytes.

Seeded, so a failure can be repeated: the seed and the round are printed, and
`FRISKET_FUZZ_ROUNDS` turns the same loop into a soak without changing it.

Three faults, all of the same kind — **a bound that guards only one end**:

- **`BigEndianReader` guarded only the top.** `at + 1 < Length` is perfectly true of a
  **negative** index, and a negative index is not exotic: a table offset is a `uint` in
  the file and an `int` here, so anything past 0x7FFFFFFF arrives below zero.
  `Has(at, count)` guards both ends now. `Room()` lied symmetrically: a negative offset
  made the room look enormous, so the `Math.Min` meant to bound a count bounded nothing;
- **`PngImage` added instead of subtracting.** `at + 12 + length` **overflows** for a
  length near `int.MaxValue`, comes out negative, and sails past the test written
  precisely to stop it — after which `new byte[length]` asks for two gigabytes on behalf
  of a thirty-byte file. Written `length > data.Length - at - 12`, it can no longer
  overflow;
- **an IHDR shorter than a header** was read on thirteen bytes all the same. The guard
  above proved the *chunk* fits the file, not that it holds a header.

**And two of my three regression cases were vacuous on the first writing** — the
truncated PNG was 29 bytes where the entry guard demands 33, so it never reached the
branch it claimed to cover and the sabotage went green (§6, §10b).

## 8d. Cancelling asks for no `async`, and is decided in three places

The engine is **entirely synchronous**, and rightly so: it never waits for anything, it
computes — layout, glyph measurement, writing PDF objects. `async` exists to hand back a
thread during a **wait**; on computation it speeds nothing up, frees nothing, and adds
only a state machine. Moving the work off the request thread is a `Task.Run` at the
**host**, which alone knows how many renders it is running at once. The engine's six I/O
calls are conveniences over the primitives (`FromFile` over `FromJson`, `Load(path)` over
`Load(byte[])`, `Save` over `ToArray`): a server touches none of them.

**So one cancels by asking, not by waiting.** Optional `CancellationToken` on `Resolve`
and `Render`, `ThrowIfCancellationRequested()` in the loops that last. Nothing is
asynchronous and nothing needs to be.

Three moments, three owners — and the distinction is what matters:

| Where | Who | How |
|---|---|---|
| inside a document | the library | asked in the pagination loop, beside `MaxPages`, and once per page when drawing |
| between composing and drawing | C#, for free | `Resolve()` then `Render()` are two calls; the overload that chains them takes the control for the caller |
| between two documents of a batch | the caller | their loop, their `if`. **What has been produced stays produced**: the library does not know where it went |

**It throws, and returns nothing.** A layout abandoned halfway is not half a layout — and
a document abandoned halfway is worse: *a PDF cut short looks exactly like a PDF*, the one
output that does not announce itself.

**The token is a handle of its own**, never a flag on the template or on the font
library. A cancellation token belongs to **one** operation: hung on a template,
cancelling one render would cancel the next render that used it — a fault nobody would
look for, since the second call did nothing wrong. It is also what makes cancellation
possible despite a synchronous boundary: `fk_render` does not return until it is
finished, so the word necessarily comes **from another thread**.

**`FK_CANCELLED` is a code of its own, not an `FK_FAILED`.** A caller retries a failure
and does not retry a cancellation; folded together they make a storm at the first timeout.
Checked by reading the three codes: cancellation 3, a misspelt option 2, a fanciful
cancellation token 1.

Measured across the ABI, from a second thread: a render of 0.15 s stopped at 0.07 s, and
nothing returned.

## 8e. Mail merge asked nothing of the engine

Two forms, as the author named them: *one document holding all of them* (a bundle of
letters in one PDF) and *one document per record* (one file per recipient). Three things
already did them, and checking showed it before a line was written (§5d):

- **N files → N documents**: the CLI has always looped, "every data file gives
  one document";
- **an array → N letters in one document**: `RepeatOver` on a container,
  `PageBreak.Before` on the letter. Measured: 3 pages, one letter each;
- **N emails**: the same path, `--format mail`.

Only *one file holding N records → N documents* was missing.

**And the semantics kept is the one that invents nothing: the array cut down to one
element.** The rest of the file is left alone, so the **same template** serves both forms
— the mode is chosen at the command line, not by writing a second template that would
have to be kept in step (§1 at the scale of a document).

It works for free thanks to a rule that already existed: `ReportData.Resolve` walks up the
chain of scopes **then falls back to the root** — "which is what lets a line read `{Ref}`
from itself and `{Currency}` from the invoice without either having to say where to
look". `{Company}` therefore resolves in both modes. The sabotage that removes that
fallback brings the test down, which shows it is indeed what carries everything.

The two other semantics considered and set aside: the record **made the root** (classic
mail merge) forces the shared part to be repeated in every record; the record **inside
with the file around it** would have wanted a public seam in `ReportBinder`, and above all
a template incapable of producing the other form.

**Two guards, both in the CLI and not in the engine.** A cap on documents is **policy** —
the same reasoning written for `PageLimit` against `MaxPages` (§16), one storey up. It is
counted **before** anything is bound, because counting is free and binding is not.

**And an output name that comes from the data does not choose its directory.** `{Nom}`
being `../../evade` is written `.--.--evade.pdf` in the output folder — that is F1 one
storey up, and it had to be thought of while writing the function, not afterwards. Two
records of the same name give `Dupont.pdf` and `Dupont-2.pdf`: two people called Dupont is
ordinary; one of their letters silently replacing the other is not.

A path that names no array is **refused** — a typo returning one document instead of a
hundred would look like a success. An **empty** array returns zero documents without an
error: nobody to write to today is an answer.

## 8f. `RepeatOver` clones the children, never the container

The trap, met while building the mail merge: a `PageBreak.Before` set on the container
that carries `RepeatOver` gives **a single page**, in silence. Repeating clones what is
**inside** the container; what has to hold per record therefore goes **one notch further
in**.

**And this is not a case of §10**, contrary to what I first thought. Measured, the four
placements are honoured exactly:

| Where | Result |
|---|---|
| on the run, nothing before it | 1 page |
| on the run, a note before it | 2 pages — "start the bundle on a fresh page" |
| on the repeated block | 3 pages — one letter per page |
| on both | 4 pages |

All four are **right and wanted by someone**: §10's "legal value, silently wrong output"
does not apply where the output answers exactly what was asked — what was wrong was
whoever assumed `RepeatOver` cloned the container. So the rule nearly did not exist: one
that accuses a template that is right teaches people to ignore the panel exactly as a
stale one does (§15), and this panel is the only thing between a simplified editor and a
document veraPDF will refuse (§5c).

`sound.repeatPageBreak` exists because a **discriminant** finally appeared: it fires only
**where the break had a better place to be** — a run wrapping a single block that carries
none. Without an inner container there is nowhere else to put it; where the inner one
already carries one, the author has plainly understood the difference. Both stay quiet.

That is also what `Warning` means here, and the enum said so already: "Legal, and probably
not what was meant." The level existed; it just had to be checked that the case really
belonged in it.

**Silence is the half that must be tested.** Test 63 checks once that it speaks and
**three times that it stays quiet**; the sabotage that widens the rule brings the silences
down, the one that switches it off brings the speech down. And §5 applies in full: the
fault is in the template made to be wrong, **mended in place** — not doubled by a correct
version beside it, which left the fault present and the rule noisy.

## 8g. The reader is a separate assembly, and a page is a copy

`import/Frisket.Import.csproj` reads one page out of another PDF. It references the
engine; **the engine must never reference it**, and that is not tidiness. Everything
else here produces bytes we chose; a reader consumes bytes somebody else chose, so
folding it in would make every document that imports nothing pay for a parser it will
never use. The seam is `IPdfSink` and `IImportedPage` in `engine/Rendering/`, the same
arrangement `ISignatureProvider` was designed with.

**A page is copied, not rendered**, and that one decision is what separates this from
"a PDF reader". Nothing in it interprets a content operator or knows what a font is:
the page's marks and the objects they reach travel across as they are, renumbered and
no more. So the work is the file's *structure* - the cross-reference table, object
streams, the page tree - and a page full of JPEG, JBIG2 and CCITT imports perfectly
well without a line of code for any of them. Flate is decoded only where the file's own
scaffolding is written in it.

**Renumbering is why there is an object model at all.** A copied object names others by
number, those numbers belong to the file it came from, and rewriting `12 0 R` in the raw
bytes would also rewrite a title, a bookmark or a caption that happens to read `12 0 R`.
The only safe rewrite is one that knows which of those it is looking at.

Three things that had to be built rather than assumed:

- **the cross-reference table may be wrong**, and every reader in the world has a repair
  path. A file assembled by concatenation or truncated in transit has offsets pointing at
  nothing; refusing those refuses documents every other reader opens. So an object that
  is not where the table says triggers one sweep of the file for what it actually holds;
- **a media box very often does not start at the origin**, and a reader that assumes it
  does imports a blank page - the content is drawn entirely off the sheet. Nothing about
  it looks wrong;
- **`/Rotate` is not decoration.** A landscape page is routinely stored upright with a
  quarter turn asked for, so a reader that ignores the key brings it in on its side.

**And what blind mutation cannot reach.** The reader joined the fuzzer (§8c) the day it
was written - 140,000 mutations over three seeds, nothing escaped - and two of its guards
are **unreachable that way**: a classic cross-reference table writes its counts as ASCII
digits, so spoiling bytes gives another small number and the bound on it is never
approached; and a cycle has to be *constructed*, which a random edit never does. Those
two are checked by files built to be hostile instead.

The cycle guard is the sharper lesson: taken out, the sabotage prints **no test result at
all**. A stack overflow aborts the run, out of reach of any `catch` - §8b's handle
register, met again - so *a check cannot report the fault it prevents when the fault
kills the process*. The evidence is "Test Run Aborted", and reading it takes looking at
the run rather than at a pattern in it (§13).

### 8h. An imported page is an item, and it notes what it costs

`ImportedPageItem` lays out like a picture - in a band, in a cell, at a coordinate,
several to a sheet - and reaches the file as a Form XObject. The engine does not read
it: `ImportedPages.Resolve` is where a caller hands over an `IPageImporter`, and until
then the item knows only where to look. `Frisket.Import.PdfPageImporter` is four lines,
and the CLI is what holds it, because the CLI is the thing with a file system.

**Resolved before layout**, for §14's reason: one choice answers three questions. A page
whose size is known before measuring flows like a picture; a repeated container clones
children that already carry what was read, so the same file is opened once and not once
per record; and the fact that the document now holds somebody else's bytes can be stated
in `LayoutResult.Issues`, which is composed and not drawn.

**`fits.importedPage` is a `Warning` and refuses nothing** - the author's own rule: an
import puts a conformance claim in question, that is the responsibility of whoever uses
the library, and the engine says so. It is the same decision as reading a font's
`fsType` and never enforcing it (§8b): only the caller can know, and a report meant to be
read has no need of PDF/A at all.

**Both halves of that rule are put to a validator rather than assumed** (test 92), because
neither is provable by reading the code. A page out of a well-formed document costs the
claim **nothing** - so a rule that refused imports outright would refuse correct
documents. A page drawing Helvetica with no font file behind it costs it **everything** -
so a note nobody wrote would be a file quietly lying about itself. veraPDF says both, and
it does not know where the page came from.

**And the check worth keeping is `Dangling`**: every object number a finished document
refers to must be one it defines. A copied graph is renumbered on the way in, and a
renumbering that drops one entry gives a file that opens, draws most of itself and is
missing a font. §2, on a whole document rather than on one structure.

**A converter belonged to 28 enums and was written on their properties instead.**
`ImportedPageItem.Scaling` reuses `ImageScaling`, whose `JsonStringEnumConverter` sat on
`ImageItem.Scaling` and nowhere else - so the new property wrote a **number** while the
schema went on promising a name, which is §1's opening story exactly. Test 47 caught it,
because a property existed for it to catch.

The fix is not the property, it is the type: 28 of 29 enums carried it that way, so the
model was one new property away from this every time. Every enum declares it on itself
now, and the schema did not move by a byte - which is how you know nothing had been
writing numbers yet.

## 9. Columns do not save paper

`ContainerItem.ColumnCount` divides the flow of a stacked container. What the first
column does not take is a `Continuation` — exactly what the next one starts from, and
what the last one leaves goes to the next page by the usual mechanism. They fill **in
order**, without balancing: a page of terms and conditions wants its first column full.

Measured: 40 articles make 2 pages in one column **and** 2 in two. The same text at the
same size takes up the same area. What columns change is **line length** — 63 lines on
the first page against 144. An assertion of "fewer pages" is false, and was written
before being corrected.

**Balancing** touches only the page where the text stops: that is the only one with room
to spare. `BalanceColumns` finds the shallowest height that still holds everything, by
halving — eight tries, each only a re-flow of what has already been measured. Every try
places items, so `Page.Rewind(count)` undoes those of the try before; a try that leaves
its items behind it is a page drawn twice.

Named `ColumnCount` and not `Columns`: `TableItem.Columns` is already the list of column
widths, and a table **is** a container. The compiler had said so (CS0108); `-v q` hides
warnings, so **re-run a `-t:Rebuild -v n` before concluding there are none**.

## 10. `Justify` was in the enum and nowhere else

`HorizontalAlignment.Justify` existed from the start; **no backend implemented it**, and
a template asking for it got left-aligned text without a word. It is the quietest form
of the problem in §1: a legal value, a silently wrong output.

The two backends now stretch by the **same amount**, worked out once in
`engine/Rendering/Justification.cs`. They go about it differently, and that is
unavoidable:

- in PDF, **not `Tw`** — word spacing applies only to code 32 in a **single byte**, and
  our fonts are composite and two-byte, so `Tw` does strictly nothing. It is the gaps
  between words that move, through a `TJ` array, whose numbers are thousandths of a text
  space unit **subtracted** from the position: widening a space takes a negative number;
- in SVG, the `word-spacing` attribute, which has the same semantics.

Test 46 sets the two against each other in millimetres, to within 0.002. It is the only
check that catches "one stretches, the other does not".

The last line of a block is never stretched: it is what tells the reader the paragraph
is finished. A **hard** line break inside justified text is stretched like a wrapped
line, though — the validator flags it (`sound.justifiedBreak`), for want of being able
to tell the two apart once the lines are assembled.

## 10b. `KeepTogether` was read in two paths out of five

Reported by looking at the document: "for the barcodes, do I have no heading, or am I
not using the right file?". The right file — the heading had stayed **on the previous
page**, with its caption, while the codes arrived alone on the next.

The engine has `Splitting.KeepTogether`, documented "always pushes it whole to the next
page". It was consulted only in `FlowText` and `FlowAdvancedText`: on a **stacked
container**, `FlowStack` never asked `CanSplit`. A legal value, without the slightest
effect, in silence — §10 in a new place, and found the same way it was: by asking for
something reasonable and watching it not happen.

Measured before mending, and it is the only measurement that proves anything: the same
block, split by `Auto`, was split **exactly the same way** with `KeepTogether`.

**A check on this subject is worth nothing outside a narrow window.** "It did not split"
proves nothing: a block that fits never splits, and a block taller than a page has to
split. It takes a sample where `Auto` splits *and* where a fresh page holds the block —
found **by sweeping**, not guessed: at 26–28 lines of filler and from 35 up, but not in
between. Hence a `27` in the test that is not a round number, and an assertion that
first checks `Auto` does split — without which the comparison is between two things that
both fit.

The cure `KeepTogether` cannot give is `Style.KeepWithNext` (§10c); the interim answer,
one `KeepTogether` container around heading, rule, caption and content, worked for short
content and never could for a table longer than a page.

## 10c. "Keep with next", and two vacuous checks to prove it

`Style.KeepWithNext`: do not let the page end immediately after this item. The cure for
the stranded heading that `KeepTogether` did not give — that one keeps a block
**whole**, it attaches nothing to what follows.

**Its own property, not a third `Splitting` value.** The two are orthogonal: "do not
split me" and "do not leave me at the bottom" are two requests, a one-line heading never
needs the first, and a block of heading, rule and caption wants both. An enum growing by
combinations is the start of a matrix nobody keeps up to date. Price paid: §1's tax,
once.

The implementation is a **look-ahead** in `FlowColumn`: at the moment of breaking before
child *i*, walk back while the one before asks for it, and rewind the page. Two guards,
both classic:

- **the cascade** — stepping back a single notch strands the ones above, which is the
  same fault one item further up;
- **the floor** — never go back past the first item placed on the page. Without it, a
  run of items all asking for it push one another forever and the layout **stops
  converging**.

**And both my checks of these traps were vacuous.** The sabotages went green the first
time:

- the cascade check used a sample calibrated at 27 lines of filler, where the split
  **already** falls between the heading and the rule — a single step was enough, and the
  cascade was never exercised. Found by sweeping: **25** is the only calibre where the
  three lead-in items fit on the page and only the content moves over;
- the floor check marked only **three** items. The walk back therefore stopped at the
  first one that did not ask, never reaching the floor at all. **Every** line has to ask
  for it, filler included.

Same lesson as §10b, twice over — *a sample outside its window describes a document that
never had the problem*, and when the sabotage passes it is the check to mend, not the
code (§6).

## 10d. Every sample says what it demonstrates

What §4 did for `conformance` now holds for the eight others: `watermark` (tiled and
once only), `backpage`, `split`, `columns`, `advanced-text` (styles and wrapping) and
the three sections of `sections`. A heading, a rule, a sentence saying **what it does
and what to look for** — because the file is what someone opens six months later, and a
page of columns is a page of columns: nothing on it says which of the things it does is
the one being demonstrated.

The caption block goes **first whatever was added before it** — the invoice-based
samples are assembled long before the test says what it is doing with them — and every
piece asks for `KeepWithNext`: a heading stranded at the foot of a page would be this
document demonstrating a fault by accident, in the middle of demonstrating another.

**It hands back what it added**, and that is not a convenience. Two checks counted "all
the text on the page": five pieces for a line of mixed styles, and every word of a
wrapped paragraph. The caption block is text like the rest — five became seven, and the
paragraph gained the caption describing it. A check that counts the marks on a page must
be able to leave out the ones it has just put there itself.

**And three lines of caption moved a calibrated sample out of its window.** The columns
test held at 25 articles; with the caption block, the second column no longer stopped
short enough and balancing had nothing left to demonstrate. Swept again rather than
loosened (§10c): the window runs from 14 to 23, **20** sits in the middle, and at 26
both columns are full — balanced and unbalanced give the same 278/276, so an assertion
comparing them would pass while proving nothing.

The sweep itself lied on the first writing: it read the **default** for the unbalanced
case, and `BalanceColumns` is **true** by default. So it was measuring balanced against
balanced, and all thirteen calibres gave identical numbers — which, re-reading it, was
the only thing that could have raised the alarm.

**A file name is a claim like any other.** The `watermark/` folder held two samples of
one thing under **three** names: `specimen*` saying SPECIMEN, `draft*` saying BROUILLON.
Nothing was wrong, and yet nothing named what **actually differs** between the two files
— the mode, tiled against a single copy. A reader saw two words and concluded that was
the subject.

Both say DRAFT since, and the names say the mode: `draft*` and `draft-single*`. *What
varies between two samples must be what they compare, and nothing else* — otherwise the
noise passes for the signal.

Along the way, `new Watermark("SPECIMEN")` proved nothing: `Watermark.Text` is already
`"SPECIMEN"`, so the default alone would have drawn the same page — §5b again, seen from
a test rather than a cascade.

## 11. A split row must still be a whole row

When a table row splits, the short cells have nothing left to draw on the next page.
They were simply left out — and a row handing back fewer cells than the table has
columns is **refused by veraPDF** and unreadable aloud. They are now placed empty.

This fault slept because test 40's long row landed in a table where it did not split.
The check that matters: **each half shows as many cells as the table has columns**,
not "the row appears twice".

Corollary for that test: a row shorter than a page can arrive at the top of a fresh
page, where the flow lays it whole rather than looping. A row never asked to split
proves nothing — it needs enough in it to overrun a full page.

## 12. Test outputs

One tree only: `tests/output/`, anchored on the repository root through
`RepositoryRoot()` (walking up from the binary, not from the current directory).
Two trees had coexisted depending on the launch directory, and a comparison between
one and the other wrongly concluded that a fix did not work.

A PDF open in a reader **locks the file**: the test that writes it fails on
`IOException`. Under xUnit it fails alone, but closing the tab is still simpler
than interpreting the result.

**A sample dated by the clock is never the same file twice.** Twelve PDFs here took their
date from `DateTimeOffset.Now`, so every run rewrote them - the timestamp and the `/ID`
that follows from it - and no run ever left the tree clean: a real change had to be found
among twelve that were not one. Every PDF a test writes goes through `WriteSamplePdf`,
which refuses one dated now; the first run with it named ten tests, which is the check
seen failing, and `SampleDate` dated them. Two runs now give the same 197 files to the
byte. A copy made by one test from another's output (`outside.pdf`) settles one run
later than its source - expected once, not a fault.

## 13. Writing into files

Bash heredocs turn `\n` into a real newline in the middle of a C# string.
Go through a python file in the scratchpad, or through `Edit`.

**Two habits that cost four and three times in one day.**

*Anchor on the doc comment, not on the declaration.* A member inserted before
`private static X Foo(` lands **between** `Foo`'s `/// <summary>` and `Foo` itself, so
the comment now describes the new thing and the compiler says `CS1734: paramref for
'item', but there is no parameter by that name`. Four times, in four files. The anchor
is the `/// <summary>` above it, or nothing.

**A fifth time, and this one compiled.** `WillHaveWidget` inserted above `DrawField`
took its `<summary>`, leaving two stacked on the new member and `DrawField` with none.
Nothing said a word: two `<summary>` blocks are not an error, they are the first one
winning. So the compiler is not the check either - what the anchor rule protects is a
comment quietly coming to describe the wrong thing, and only a reader finds that.

*Read the run, not a pattern in it.* Grepping the output for `(expected` finds a failed
assertion and finds **nothing at all** for a test that threw, for a build that did not
compile, and for a run that never happened - each of which then reads exactly like a
clean pass. Test 81 failed on its own malformed data for three runs while its output was
read that way, and two sabotage harnesses reported "green, so the check is hollow" about
builds that had not compiled. Look at the `Passed!`/`Failed!` line before concluding
anything.

## 14. An image grid and a sub-template — two elements, one principle

Two additions to the model (§1) that turned out simpler than expected, and for the
same reason: look for what already exists before writing anything (§5d).

**`ImageGridItem`** lays its children out in a grid — `GridColumns` to a row, each
cell `RowHeight` tall. The name is not `ColumnCount`: that property already exists
on `ContainerItem` and means something else (fill one column before the next, §9) —
the same lesson as `Columns`/`ColumnCount` on `TableItem`, again.
What needed **nothing** new:

- **splitting an image** — `LayoutResolver.CanSplit` already says that only text, a
  stacked container and a table know how to continue on the next page; an image is
  therefore already atomic in a `Stack` flow and already moves whole rather than
  being sliced. `FlowImageGrid` treats a row exactly as a table row — never split,
  moved whole;
- **filling from the data** — `RepeatOver` already exists on every `ContainerItem`,
  not only on a table, and `ReportBinder.BindItem` already knows how to resolve an
  `ImageItem.SourceField`. A data-bound grid needed no line in `ReportBinder.cs`;
- **proportional fitting** — `ImageScaling.Uniform` is already the type's default.

**`SubTemplateItem`** splices in another template's body, resolved by
`SubTemplateExpander` **before** binding and **before** layout — not at render
time. That one choice answers three questions at once: a nested body's continuation
drives the parent's pagination, `ShowWhen` / `HideWhen` condition the splice, and
the included template's margins never count — because by the time `LayoutResolver`
runs there is no sub-template left at all, only an ordinary flattened tree. Zero
lines changed in the layout engine.

The cycle guard (a stack of absolute paths being expanded) stays a hard guard: an
undetected cycle crashes the engine, which is not the same thing as a badly chosen
format that merely renders badly — that one was deliberately left to the author's
discipline rather than checked.

### 14b. A sub-template brings a block, or a document

`SubTemplateMode.Append` takes the referenced report's **sections**, whole, and puts
them after the section that carried the item — its page size, its header, its footer.
`Splice` (the default, and what this element has always done) takes its `Body` and
drops the rest, which was the only honest answer while a report had one document.

**A document is not a flow item, and two things follow.** The item's *position* decides
nothing but which section the piece follows: anything declared after it stays in the
host's section, and therefore comes out **before** the piece. And what a file has one
of — title, author, subject, keywords — belongs to the host; the pieces' are dropped,
which is not a limitation to lift later. A dossier assembling three reports has one
title and it is the dossier's.

**The language is the exception, because PDF makes it one.** `/Lang` belongs to the file
*and* may sit on any structure element, so a piece declaring its own keeps it and a
German appendix is announced in German. Hence `BaseItem.Language` — on every item, not
only on a section, because PDF/UA 7.2 asks it of any passage in another language and
assembly is merely the first thing that made it unavoidable. Written **only where it
differs from the file's**: a `/Lang` repeating the file's says nothing and hides the ones
that do.

Three things found by building it:

- **expansion read `Report.Section`** — the façade over `Sections[0]` — so a sub-template
  anywhere but in the opening section was **never expanded**: no error, no splice, an
  empty container where a block was meant to be. Invisible while a report had one
  section, and no sample had ever put an include in a second one;
- **the designer's preview would have spliced an appended piece into the flow**, drawing
  a page that will never exist. It is the same fault `expandCarried`'s own comment warns
  about, met from the other end;
- **a rule about the item's position could not be written, and that is now a decision
  rather than an accident.** `frisket check` loads through `Report.FromFile`, which
  expands, so by the time the validator runs there is no resolvable `SubTemplateItem`
  left. **That is the right tree to check**: a template carrying sub-templates is not a
  whole document on its own, and what ships is the composition. Settled 25 August 2026.

  Two things follow. A rule about a *resolvable* sub-template is unwritable by
  construction, so none should be attempted - `sound.emptySubTemplate` fires only because
  an **empty** item survives expansion, which is a different case. And the check now reads
  the pieces themselves: a figure with no alternative text inside an appended report is
  the host's fault to answer for, which is exactly right when the host is what gets sent.

  The cost, stated: `frisket check` can now refuse a template because of a file its author
  does not control. The message names the element, which is the most that can be done. And
  the designer validates the *unexpanded* tree, having no file system to resolve anything
  with - so it checks strictly less, and the panel says so rather than pretending.

`Report.FromFile` is what knows how to resolve a relative path — `FromJson`
alone cannot, having no idea where the text came from. Wired into `apps/cli`
(`LoadTemplate`) and into `native` (`fk_template_load`), deliberately **not** into
`apps/server/Program.cs` nor the designer's local preview: both receive only JSON as
text, with no file of their own to resolve anything relative against.
The panel says so rather than letting the preview pretend it spliced something in.

## 15. `AdvancedTextItem` — the line, not the segment, is the unit

Mixed-style text (bold, italic, underline, strikethrough, colour, size within one
sentence) could have demanded an entirely new text measurer. It needed none:
`AdvancedTextItem` carries a list of `TextRun` (text + a partial `TextStyle`
overriding the item's base font, itself overriding `Report.DefaultFont` — the same
two-level cascade a `TextItem` has, with one level more), and all the new work fits
in `LayoutResolver.BreakAdvancedText`.

**The line is the unit, not the segment.** Word-by-word wrapping ("one word plus the
spaces that follow it", the rule `FontTextMeasurer.WrapParagraph` already uses)
groups words from several runs into lines; each line becomes a placed row, never
split — the same guarantee a table row and an image-grid row have, through the same
mechanism (`CanSplit`, extended to recognise this type; §14). Pagination therefore
needed nothing new: a line that does not fit moves whole to the next page.

**Each piece of a line draws as an ordinary `TextItem`.** Rather than teaching the
renderer to draw several styles on one line, `PlaceAdvancedLines` synthesises an
ephemeral `TextItem` per piece, with its own resolved font, and places it through
the same `Place()` as everything else — `PdfBackend` and `SvgBackend` learned
**nothing** new about `AdvancedTextItem` itself, only the strikethrough
(`Strikethrough`), added to `TextStyle` because plain text benefits from it too.

**Baseline alignment** comes from the same formula that already centres the cap in
its line (`(LineHeight + CapHeight) / 2`), applied once to the whole row (its
tallest font) and then, for each smaller piece, to its own font — which sits them
all on the **same** baseline rather than each centred in its own height. Verified to
a tenth of a micrometre in test 50 by computing each piece's baseline from its
placed frame.

**`Justify` works since 5 August 2026**, and the limitation this section used to
state is lifted.
It came of each piece laying its own box: `PlaceAdvancedLines` now counts the spaces
of **the whole line**, not of each piece — the blanks a reader sees do not stop at a
run's boundary — and lays a justified piece **word by word**, its spaces becoming
gaps rather than glyphs. The backends were taught nothing: the stretch is entirely
in the geometry, so the two outputs cannot drift apart on a line. The last line is
never stretched, same rule and same reason as plain text.

The proof that counts: **a justified line ends exactly where the same line
right-aligned ends** — verified to a hundredth of a millimetre (test 50).
`sound.advancedTextJustify` was **deleted** from both validators rather than kept —
a rule that no longer describes the engine is worse than no rule: it teaches people
to ignore the panel.

**One owned limitation stays:** `{Pages}` in a run keeps the first pass's guess rather than
being corrected once the page total is known, because the existing correction
(`ResolvePageCount`) re-sweeps only those `TextItem` whose text still carries the
token — already-resolved fragments are never revisited there. It is a validator rule
(`sound.advancedTextPageCount`) rather than a surprise.

**The designer's echo dropped `Strikethrough` in `newFont()`, `fontOf()` and `text()` at
once** — §1's five places seen from the cascade, and found only by comparing the SVG
produced against what was expected, never by reading the code.

## 15b. A heading numbers itself from the text, not from a style

`{HeadingNumber}` in the heading's own text, `Report.HeadingNumbering` to say how it is written. That is all, and it is what separates it from a word processor's linked lists: the number is not painted to the left of the paragraph by a list machine, it **is** in the text. The author chooses where it sits, what comes before it, and **types the punctuation that follows it** — so the question "1.1 or 1.1." never arises in the format.

Two counters, `{HeadingNumber}` across the file and `{SectionHeadingNumber}` restarting at each section, equal while there is only one — the pattern §16 had already paid for with `{SectionPage}`, reused rather than inventing a `resetOnSection` flag that would have forced a choice before there was anything to know.

**The level comes from the role, not from a second place.** `PdfRole.Heading1..6` already exists and PDF/UA requires it anyway. `{HeadingNumber[2]}` forces the level for something that is not a heading; without brackets, the role answers. One source of truth.

**"As it prints" is right in intent and wrong in implementation.** The numbering is done in **one walk of the tree before any measurement**, never at placement, and that is the only decision that matters here:

| what the engine already does | what a counter advanced at placement would give |
|---|---|
| `BalanceColumns` searches by halving, **eight tries**, each placing then `Rewind` | the heading counts eight times |
| `KeepWithNext` walks back and rewinds | it counts twice |
| the `Resolve` loop starts over up to three times for the digit count | three more times |
| SVG and PDF walk the same items | two different numberings for one document |

None would announce itself: the document would come out numbered, only wrong. Filling a token from a number **stored on the item** is a pure function, so all of that becomes moot by construction. The sabotage that advances the counter as it is used gives `2 Alpha` and `1.3 Gamma` — that is what test 66 catches.

A number must also be set **before measurement**, because it widens the heading that carries it. The opposite of `{Pages}`, known only at the end and pasted back into fixed-width fragments.

**The format is an alternation of symbol and literal.** `1 A a I i` are the alphabets; everything else is a separator, and it belongs **to the level that follows it**. Hence `"1.1.1"` truncated to two gives `1.1` and never `1.1.`. A separator therefore cannot contain a symbol letter; in practice the literal words go around the token, not inside the format.

`A` runs to Z then `AA`, the way a spreadsheet names its columns — twenty-seven clauses in a contract is ordinary. `I` falls back to arabic figures above 3999, which will never happen: both are written **on purpose** rather than left to happen.

**What has no answer stays visible.** No format, no level, or nothing reached at that level — a page header is composed before the body it heads: the token stays on the page as it was typed. A `0.` in front of a heading is a fault nobody sees; `{HeadingNumber}` on the page is a fault nobody misses. Three rules of both validators say so beforehand: `sound.headingNumberNoFormat`, `sound.headingNumberOutsideHeading`, `sound.headingNumberDepth`.

**The conformance document revealed that its H1 was in the wrong place.** Its seven sections were **H2** under the title: `{HeadingNumber}` gave `1.1 Summary` there, structurally right and visibly absurd. The fault was not in the numbering — it only made legible that **a document's title is not a section of that document**. The title is no longer a heading at all (it remains the `/Title`, which the reader announces on opening), the sections are the H1s, and veraPDF passes both profiles. The document numbers itself: `1.` to `7.`, `4.1. Method` coming from the sub-template — possible only because expansion precedes everything else (§14) — and `5.1. QR code`, `5.2. Code 128`. **No number is typed anywhere.**

**And the harness check was vacuous at first**: its sample went `H1 H2 H3 H2 H1`, where every displayed number is the same with or without the deeper levels being reset, so removing the reset from the echo left the pass green — it takes `H1 H2 H3 H1 H2 H2`, where Epsilon reads `B) b` instead of `B) a` (§10c again, seen from a sample rather than a calibre).

## 16. A report carries a **list** of sections

`Report.Sections` is the run of sections; `Report.Section` names the first and exists so that everything written before it goes on saying the same thing. **`Dimension` is on `Section`**, not on the report — it is the page size of *that section*. `Report.Dimension` remains, a façade over the first.

The model was nearly there: `Section` is a `ContainerItem` like `Header`/`Body`/`Footer`, and it was **already** declared to the serialiser. Only those two gestures were missing. A section break is therefore not a concept here: it is the next document.

It is also the shape of the PDF, which makes the choice right and not merely convenient: the size is `/MediaBox` on **each page object**, never a property of the file. `PdfBackend` already wrote one per page — and filled them all from `_pageWidth`/`_pageHeight`, two fields holding what the **last** page had left. Invisible while a report has one page size, wrong the moment it has two. The size is remembered per page at `EndPage`.

**Nothing crosses a section.** What does not fit adds pages *to its own section*. A deliberate limit: it makes the only genuinely hard case — a table split between two page sizes, changing width mid-continuation — **inexpressible** rather than discouraged. And it simplifies the resolver: the pagination loop is unchanged, wrapped in a `foreach` over the documents.

**`Report.Title` belongs to the file, not to the section** — it is the `/Title` of the information dictionary and the `dc:title` of the XMP packet, of which there is one per PDF. Changing it from any section therefore changes it everywhere, and `{Title}` gives the same thing in all of them: that is intended, not a fault. The panel files it under "Report — the whole file" rather than letting anyone believe otherwise. There is no *per*-section title; a report assembling two documents puts its own in the header of each.

`{Page}`/`{Pages}` keep the whole file — that is what they have always meant. `{SectionPage}`/`{SectionPages}` count within the document, and **equal the first pair** when there is only one section, so that a template can use them without knowing whether the report has several. Both totals are set by the final pass that already existed for `{Pages}`.

`Section.PageLimit` is **declarative**: exceeding it is reported in `LayoutResult.Issues` and refuses nothing. That is possible because composing and drawing have been separate **since day one** — `Report.Resolve()` composes and produces no file — so the caller looks, then decides. Not to be confused with `MaxPages`, a runaway guard at 5000, on the resolver, outside the template, and whose message speaks of non-convergence.

`LayoutResult.Issues` reuses `TemplateIssue`, but **they are not** the same as `frisket check`: that one reads the template and speaks before a document exists, these have had to compose. Two families that resemble each other without being worth the same.

A `Section` inside a `Body` means nothing and does not draw; the typing allows it, so `sound.nestedSection` refuses it.

Three traps met, all silent:

- **old templates** put `Document` and `Dimension` at the root. Two properties read them back without ever writing them again — and `Dimension` must be **set aside then applied in `Reattach`**, because the two arrive in file order and `Dimension` before `Document` set the size on an object the next line replaced;
- **the designer's `layout(report, index)` takes its report *and its section* as parameters.** Making it read the global report worked on screen and broke the harness, which passes another — hence the parameter. But the section itself stayed hard-coded to `Sections[0]`: opening the second drew the first's header on the second's page, while the tree beside it listed the second's children correctly. **Each half was right; only the two together were wrong**, and that is why it survived being looked at. The bands are now drawn from the section `layout` says it composed, not from the one the tree selected;
- **never `git checkout` a file to undo a sabotage** when the work is not committed: it restores the last commit, so everything. Copy the file first, as everywhere else in this file.

**The vocabulary was unified on 4 August 2026**: the class is called `Section`, the property `Report.Sections`, the JSON discriminator `"Section"`, and the tokens already said `{SectionPage}`. Three vocabularies agreeing at last. No compatibility is kept with the old form — nothing was published.

The mass rename bit in **three** places no test saw:

- **`/S /Document`**, the root of the PDF structure tree, became `/S /Section`. `Document` is a **standard** structure type of ISO 32000-1; `Section` is not — the standard division type is written `/Sect`. Result: the whole tree passed for a non-standard type with no role map, which **PDF/UA 7.1 refuses**. Found by veraPDF, not by the 52 tests;
- **the prose** where "document" meant the whole file — "rescales the whole document", "the document's outline" — became "section" and then said the opposite of what it means.

- **`document`, the browser's object**, in the designer: 142 calls became `section.getElementById`, `section.createElement`… of an identifier that exists nowhere. The page died at its **first** top-level statement — the designer no longer opened at all. The harness's DOM stub went the same way (`global.section = {…}`), so the two breakages cancelled out: the harness said `all good` about a page that does not start. Found on 4 August 2026, a week after the rename, while instrumenting the harness for something else.

Hence the rule: a global rename must **protect the strings that go into a file** (PDF types, JSON keys, rule names), **re-read the prose** — the same word there often means something other than the class — and **never touch an identifier of the host**. `document`, `window`, `history` are names the browser gives, not names of the project.

The sorting is by case and by shape: model properties are capitalised (`section.Dimension`, `section.Childs`), DOM members are lower case (`document.createElement`). No ambiguity, once you think to look.

And the lesson behind the lesson: **the harness walks only what it is made to walk**. It evaluates the whole script — so it would have caught this one had its own stub not been renamed in the same pass. A check and its subject changed by the same pass no longer check anything. That is the same fault as "an assertion that looks for bytes proves nothing" (§2), seen from the tool.
