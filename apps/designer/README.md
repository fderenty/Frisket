# The two faces

Two editors over one core, because the model already named the seam: placing things
at coordinates is `LayoutMode.Absolute`, typing and letting them flow is
`LayoutMode.Stack`. They are not a full product and a cut-down one - they are the two
layout modes, and CLAUDE.md §7 says which one the engine is actually built for.

| file | what it edits | the gesture |
|---|---|---|
| `frisket-designer.html` | a template at coordinates | pick a node, set properties |
| `frisket-editor.html` | a flow of blocks | put the caret in it and type |
| `frisket-core.js` | — | the model, layout, rendering, binding, validation, wording |
| `frisket-runs.js` | — | a list of `TextRun` bound to an editable surface, and back |
| `frisket-chrome.css` | — | the palette and the handful of controls both wear |

Open either from disk and it works; there is no build step. The core is loaded as a
classic script rather than a module on purpose, since a module from a `file://` URL is
refused outright by CORS.

**The core touches no element, reads no control and stores no preference**, and the
harness proves it by running it in a context with no `document` and no `window` at
all. Its one declared dependency is a way to measure text - `setTextRuler`, the same
boundary the engine draws and calls `ITextMeasurer`. A browser gets a canvas by
default, so no caller has to think about it.

## The designer

`frisket-designer.html`: tree on the left, page preview in the middle, properties on
the right, editing the very JSON the engine reads and writes.

## The flow editor

`frisket-editor.html` is the other face. Type and it becomes an `AdvancedTextItem`;
drop a picture in and it becomes an `ImageItem` carrying its own bytes. There is no
second format and no conversion step - the engine reads exactly what is saved, which
`check-editor.js` demonstrates by writing a document and having the CLI render it.

**One `contenteditable` per text block, never one for the sheet.** That single
decision is what the whole file is built around. An editor whose page is one editable
surface has to treat a picture or a table as a foreign body inside flowing text, which
is why images and tables are half-broken in every editor of that shape. `Body.Childs`
is already a list of peers - a text, a picture and a table are siblings - so the DOM is
built the same way and the problem never arises.

Enter starts a new block rather than a new line, which is what keeps a paragraph a
paragraph in the model. Backspace in an empty block removes it.

**Tables** are edited here too, since a table is the answer to most of what people
reach for a layout for. Each cell is its own editable surface - the same decision as
everywhere else in this face - which is why a picture can go in one without becoming a
foreign body inside somebody's paragraph. Rails in the margins add and remove rows and
columns; a new row always arrives with a cell per column, because a row handing back
fewer cells than the table has columns is refused by a conformance checker and
unreadable aloud. A blank cell is a blank cell and not a fault: the checker used to
report four errors the moment a table was inserted, which is how a panel teaches people
to stop reading it.

**Dropping a block on the side of another** sets the two beside each other, and what is
drawn while dragging is the arrangement that will result rather than a line saying which
edge. Above and below reorder the flow; left and right pair the two into one container.
Those are different enough that guessing between them is a poor way to spend a drag.

What this face cannot edit - a barcode, a form field - it names and keeps
exactly as it is, saying where it can be edited. Showing half of it would be worse.

Two things it deliberately does not do: it does not paginate (the sheet grows, and
pretending otherwise would be a lie the exact preview would immediately contradict),
and it has no header or footer, those being bands at coordinates and so the other
face's subject.

**The checks panel is not one of the things a face may drop.** A simplified editor
that quietly produces a document `frisket check` refuses - a picture with no
alternative text, a missing language - is the worst thing this project could ship, so
it is there, with the same `Capable` render-target checkboxes the designer has.

## Two previews

The one in the page is an **approximation**. It measures with the browser's fonts
rather than the report's, it does not paginate, and it shows a template as it
stands - the prototype row, not the rows a data section would produce. It is
instant, and it is what makes the file work anywhere with nothing installed.

**Aperçu exact** asks the engine itself:

```
dotnet run --project apps/server
```

The page then posts its template and data to `http://localhost:5080/preview` and
displays the pages the real engine laid out - real font metrics, real pagination,
real repetition. The **PDF** button is the same round trip through the PDF backend.
The dot beside the address shows whether the last call got through.

Without the server the page falls back to its own preview and says so. Nothing is
lost, and no server is required to design a template.

### Two screens, and very wide ones

**At 21:9 and beyond** a second preview appears on the right, and the designer keeps
every part of itself exactly where it was — tree, canvas, properties. That is what two
monitors give, in one window. A bar between them decides how the room is shared, and
where it is left is remembered.

The large preview is always fitted, whatever the toolbar zoom says: it exists to be
read whole, and the zoom belongs to the small canvas beside the properties. At 1911
pixels the two halves come out 951 and 956, and the page in the large one is 546 by
772 rather than 419 by 592.

It shows the same pages as the small one, copied rather than laid out again — one
layout per refresh, and the two cannot disagree about what they are showing.

Two earlier attempts are worth recording, because both looked reasonable and neither
was what anybody wanted:

- moving **both panels to one side** and giving the sheet the rest. It changed the
  designer, which was not the thing that needed changing, and left the panels at 260
  and 300 pixels — so it read as two narrow strips and an enormous canvas;
- then having them **share the left half**. Better, and still wrong: the point was
  never to rearrange the designer at all.

**Preview window** does the same thing in a window of its own, for a real second
monitor. It is pushed to on every refresh, so the other screen is never showing an
older section. Where the browser will name the screens — Chrome and Edge, after
asking once — it opens on the second and fills it; refused or unsupported, it opens a
plain window and says nothing more about it.

A window handle rather than a message channel, deliberately: this page is opened from
a `file://` URL as often as not, and there every window shares the same null origin,
where `BroadcastChannel` is no use at all.

**The zoom**, in the toolbar, is set to *fit* by default — A4 is 297 mm tall and almost
no window is, so at 100 % the page is scrolled from the first keystroke. Sizing is done
with a width in millimetres rather than a CSS transform: a transform scales the paint
and not the box, and the scrollbars would then lie about what is there.

Pages are laid out **in a row that wraps**, so a wide preview carries more than one.

### What the standalone page does with the data pane

It fills the template from it: dotted paths like `{Client.Name}`, number formats
enough to make an amount read as an amount, and `ShowWhen` / `HideWhen` evaluated
against what the data actually holds.

Two things still need the engine, and the page says so rather than pretending:

- **repeated rows.** A container with `RepeatOver` is stamped out once per record by
  the binder, and tokens inside it resolve against that record. The preview shows the
  prototype with its own tokens still visible — `{Label}`, `{Amount}` — which is a
  fair picture of the row and not of the table;
- **the measurements.** The echo has no font file. It guesses a cap-height ratio, so
  a line breaks a few millimetres from where the engine breaks it.
- **a sub-template's content.** `SubTemplateItem.TemplatePath` names another file on
  disk, and only the CLI and the native library ever read one — this page and the
  exact-preview server receive a template as JSON text with no file of their own to
  resolve a relative path against. The panel says so, and the item previews empty
  rather than pretending to have spliced something in.

**A grid of pictures** (`ImageGridItem`) lays a fixed number of columns — `GridColumns`,
down to one — across its own width, each cell `RowHeight` tall and the picture inside
it fitted proportionally, the same `ImageScaling.Uniform` any other image already
defaults to. Bound from data it repeats one prototype `ImageItem` per record exactly
the way a table repeats a row, `SourceField` and all. The one thing this preview does
not attempt is the engine's guarantee that a row is never split across a page: the
local preview never paginates anything, images included, so a sheet with more rows
than the page is tall simply runs past the foot of it here — the exact preview is
where that boundary is real.

**Rich text within one item** (`AdvancedTextItem`) is a list of runs, each free to
override the item's own font — bold, italic, underline, strikethrough, colour, size —
so a sentence can carry a bold word and a struck-through one without splitting into
several text items. The panel edits the list directly: a text box per run, a row of
toggles, an optional colour and an optional size, "+ run" to add another. Wrapping
happens word by word across runs, the same "a word plus the spaces that follow it"
rule a plain text already wraps on, and a *line* — not a run — is what a page break
falls between, so a run half in bold and half not is never the reason a line splits
oddly. Two things a plain text can do that this cannot, both called out by the
checker rather than left to be discovered later: `Justify` does nothing here (each
run places its own box; there is no one line's gaps to stretch), and `{Pages}` inside
a run keeps whichever guess the first pass made rather than being corrected to the
real total once every page is known.

A conditional element with **no** data loaded is drawn anyway and marked in the tree.
Hiding it would be worse: somebody moves a rule away from an overlap that was never
going to happen.

**An image can come from the data too.** `ImageItem.SourceField` names a path whose
value is a bare base64 string or a full `data:` URI — the shape a browser posts when
it captures a signature off a canvas. Set it in the panel and the preview resolves it
the same way the binder does: nothing loaded shows the file picked at design time (or
the grey placeholder), data present draws the real picture, and data present but empty
at that path drops the image entirely — an unsigned quote is left with no signature on
it, never an empty box. `frisket check` reports an image with neither a picture of
its own nor a `SourceField` as `sound.emptyImage`, since both backends draw nothing at
all for one and say nothing about it.

## Checking the echo has not drifted

The preview in the page is a small reimplementation of the resolver, and two
implementations drift. `check-against-engine.js` runs the designer's layout code
headlessly against the report the engine bound and the bands it computed, both
written by the test suite:

It is handed the page itself and pulls the script out of the `<script>` blocks - there
is no build step, and nothing is duplicated on disk. It reads the `<style>` block for
the same reason: the palette is checked here too, and a pre-extracted `.js` file never
carried it. A `.js` path still works, minus that one check.

```bash
dotnet test tests/test.csproj        # writes tests/output/**/*.json

node apps/designer/check-against-engine.js apps/designer/frisket-designer.html \
     tests/output/acme/acme.bound.json \
     tests/output/acme/acme.bands.json \
     tests/output/acme/acme.template.json \
     tests/output/quote/quote.template.json      tests/output/validator/faulty.template.json    tests/output/validator/faulty.issues.json      tests/output/validator/flattened.template.json tests/output/validator/flattened.issues.json
```

Arguments six and seven hold a template built to be wrong and what the engine's
`TemplateValidator` found in it; the page's own validator reads the same file and
must report the same faults, in the same number and at the same severity. Two
validators that disagree are worse than one, because the designer would then be
clearing a section that gets refused three steps later.

The last two are the same confrontation for the `Capable` profile, on a template
that is *faultless*: what it reports is what the chosen output drops - roles and a
language that never reach an untagged file, fields drawn rather than left fillable,
a signature turned into a picture of a place to sign. Both sides must also stay
silent when no output is named, since with none there is no loss to speak of.

That last file holds **one case per output aimed at**, and each carries the target
it was asked under:

```json
[ { "target": { "Tagged": false, "Interactive": false, "Paged": false },
    "issues": [ { "rule": "can.untagged", "level": "warning" } ] } ]
```

The target travels with the question because this harness used to keep its own copy
of it in JavaScript: the engine could then change what it was asking while the two
went on agreeing perfectly about the answers. It is also what makes a second case
free, and one was needed - `can.flattenedFieldAltText` only fires on a *tagged*
output, so with a single target that had everything switched off the rule could not
be reached at all, and a rule written on both sides by hand and confronted nowhere
is the one most likely to drift.

One more file is read without being passed: `tests/output/text/line-breaks.json`,
written by test 19, says for every character of the basic plane whether the engine
may break a line at it. The echo's two line breakers are held to it character by
character - a no-break space the engine stopped naming would otherwise go on being
held here in silence, since the checks on amounts only ask about the ones it names.
Missing, it fails and says to run `dotnet test` first.

## The face that ships shows the editor, and nothing about the machinery

`frisket-editor.html` is what gets built into `release/` and what somebody embeds. It
shows the sheet and the toolbar, and none of the machinery behind them: no header, no
Document panel, no Typefaces, no findings on screen. Open and Save move into the
toolbar, because a page with no header still needs a way in and out - and the hidden
file input travels with them, since the button only clicks it.

The reason is not tidiness. Somebody typing a letter into a host application has no
business being shown a render target, a page size or a font source: **the host chose
those when it decided what it was producing**, and offering them again invites two
answers to one question.

`frisket-editor-full.html` is the same editor with all of it showing, for working on the
editor itself - the title and page size have no other home in that face. It is not built
into `release/`.

**What is not dropped is the checking.** `panel: false` hides the findings; it does not
stop them. `runChecks` runs on every redraw exactly as before and hands the result to
`onChecks` - the panel when there is one, and otherwise the host, through the `checks`
event. CLAUDE.md 5c used to say a face may never drop *the checks panel*; the word was
wrong and has been amended, because the rule is about **silence**. A host holding the
findings and deciding what to do with them is not silence - it is the same answer this
project gives for a font's `fsType` and for an imported page: only the caller can know,
and the engine says so.

### What a page declares, and what a host declares

A page says what it wants on the div it hands over; `<frisket-editor>` reads the same
names as attributes. One vocabulary, two ways in - and the **fallbacks differ on
purpose**: a page IS the application, so it keeps its header and panel unless it says
otherwise; an element is inside somebody else's, which already has both.

```html
<!-- a page -->
<div id="frisket-editor-root" chrome="false" panel="false" files="toolbar"></div>

<!-- a host -->
<frisket-editor paged="false" interactive="false" language="fr-FR"></frisket-editor>
```

| attribute | what it decides | page default | element default |
|---|---|---|---|
| `chrome` | the header: title, interface language, theme | shown | hidden |
| `panel` | Document, Typefaces, and the findings on screen | shown | hidden |
| `files` | where Open and Save live: `header`, `toolbar` or `none` | `header` | `toolbar` |
| `theme` | which room: `auto`, `graphite`, `light`, `frost` | stored, else `auto` | `auto` |
| `margins` | the document's own margin, in millimetres | `20` | `2` |
| `fonts` | where the family list comes from: `library` or `websafe` | stored, else `library` | `websafe` |
| `tagged` | whether the structure a reader needs is written | true | true |
| `interactive` | whether form fields stay fillable | true | true |
| `paged` | whether the output has pages at all - `false` for a message | true | true |

`theme` is the sharper of the two, and it was found in an inspector rather than
reasoned about. A host page painting the editor in its own dark colours got
`data-theme="light"` stamped on the tag, because somebody had once picked Light on the
standalone page and both faces share one localStorage key. That host sets seven of the
twenty-four palette names; the other seventeen then came from the **light** theme inside
the shadow root - so `--panel` was the host's near-black and `--control` was white, and
a list came out white on a dark toolbar. Two half-palettes composited.

`auto` is the answer for a face that cannot show the choice: it stamps nothing, so the
base palette applies and all twenty-four are consistent. A host with an opinion names a
room - or, better, sets the custom properties, which is what the palette is for. And if
it sets only some of them, it should expect the rest to come from whichever room is
active: **the contract is twenty-four names, and a host holding seven of them is holding
part of a palette.**

`margins` is the third of the same kind, and the only one that is a property of the
**document** rather than of the face. Twenty millimetres is a letter's margin and what
somebody laying out a printed page expects; inside a host application it is twenty
millimetres of that application's screen given to a margin nobody there can change and
few will print. What an embedded editor produces is content, and where it sits on a page
is the host's decision. Two rather than zero, because zero is not a margin but the
absence of one - the first letter would touch the edge of the sheet, and the handles in
the gutter would have nothing to sit beside.

`fonts` follows the same reasoning as the two above it. A face with no font control
does not read the preference stored by the face that has one: somebody set *from the
server* on the full page, where they could see the control, and letting that decide
inside a host application means an embedded editor asking a preview server that is not
there, failing, and offering a poorer list for it - with no way to change its mind. A
host that really does have a font server writes `fonts="library"`.

The last three are the engine's own `RenderTarget`, name for name, and their defaults
are the engine's too: the richest output, so a caller that says nothing is told nothing
rather than warned about a loss that is not happening. `paged="false"` is what a host
building an email says, and it is the only way `can.absoluteInFlow` ever speaks - that
rule had never fired in this face, because the target was two checkboxes read off the
page and `Paged` was not among them.

### Getting the document out

```js
const editor = document.querySelector("frisket-editor");

editor.report = draft;                       // a report, or JSON text
editor.addEventListener("checks", e => showBanner(e.detail));
save(editor.report);                         // what to store
editor.issues                                // the same findings, on demand
```

**This is not a convenience.** The Save button built a Blob and clicked an anchor, and
that was the editor's *only* egress: hiding it without putting this here would be
shipping an editor whose work cannot be collected. Setting `report` before the browser
has upgraded the element is kept rather than dropped, because a host creating an element
and setting a property on it in the next line is ordinary.

## One file per face

The parts are the source; `release/frisket-editor.standalone.html` and
`release/frisket-designer.standalone.html` are **generated** from them by the test suite,
in the order the page itself names them. Open either in a browser with nothing beside it.

They go to `release/` at the top of the repository rather than beside their parts, and
that directory carries the whole distinction: what is **built** lives under `bin/` and is
not tracked, what is **shipped** lives there and is. Somebody looking for what to download
then has one place to look instead of a directory of sources with two answers hidden in
it. Both faces are written there, symmetrically, though only the editor is held to release
quality today - which of them is *presented* as a product is a matter for a README and for
wherever it is published, and it should stay one.

The `.standalone` in the name survives the move on purpose. `frisket-editor.html` in two
directories is two things a fuzzy file open cannot tell apart, and the one somebody
downloads had better say what it is.

Do not edit them: the next run of the suite overwrites what you changed, and the page
everybody else works on is the split one.

```bash
dotnet test tests/test.csproj --filter Test93     # rebuilds both

# and either face can be handed to its own harness, split or whole. A bare name is one
# of this directory's faces; anything with a separator is a path from the repository.
node apps/designer/check-editor.js release/frisket-editor.standalone.html
```

Handing a built page to a harness is not decoration - the move broke one, and only
running it said so. `check-against-engine.js` reached the engine's sources by counting
two levels up from the page, which was true for exactly as long as every face lived in
`apps/designer/`; from `release/` it read one directory above the repository and died.
It walks up to `frisket.slnx` now, the same way `RepositoryRoot()` does on the C# side.

The palette audit needed the same care for a subtler reason. It sweeps **every** `.css`
beside the parts - never only the ones a page links, which is the hole 5b describes - and
`release/` has no `.css` beside it at all, so sweeping next to the page would have audited
whatever the page happened to inline and called it a pass. It sweeps the parts directory,
found from the repository root; and since a built page carries a copy of every sheet it
swallowed, the two readings are deduplicated - otherwise a theme ends up being
compared with its own duplicate. Both faces report the same numbers
now, which is itself the assertion that the built one carries the same material.

## The palette, and the line it draws

Two families of colour, and the check is about the line between them.

The **chrome** follows the theme - and one day the theme of whatever page embeds this -
so every one of its colours is a custom property a host can override: `--accent`,
`--panel`, `--ink`. Semantic rather than literal, and about a dozen of them, because a
contract nobody can hold is not a contract. The interface offers auto / graphite /
light / frost, `auto` being the default: a preference stored somewhere is still a
preference, so the question worth answering is what this looks like when nobody has
said anything.

There were seven. Four dark rooms were proposed against stated directions and then
measured rather than admired, which was the right way to build them and told nobody
which to live in - only using them did that. Graphite won, and it is not merely kept:
it **is** the dark room now, written into the stylesheet's unnamed base block, which is
what `auto` reads on a dark machine. Leaving the old blue-leaning dark there would have
made Auto and Graphite two different rooms while the selector offered only one of them.

And the rooms are one list, in `frisket-core.js`, which both faces read. Before that it
was written by hand in two faces and confronted with nothing - not with each other, not
with the stylesheet, not with the wordings - and every way of getting it wrong was
silent. An option whose rule has gone stamps `data-theme` on the root, nothing matches,
and the page falls through to the base palette: the picker says one room and shows
another, with no error anywhere.

The **paper** does not follow anything. What the canvas shows is a printed page, and a
mark that changed with the interface would make this tool disagree with the printer.
Those live in one JavaScript object, `ON_PAPER`, and hold one value in every theme -
the selection marker deliberately among them, since a host setting a pale accent would
otherwise make the selection invisible on white.

Neither survives being a discipline, so the harness enforces both: a hex or `rgba()`
outside the palette or outside `ON_PAPER` fails the run, every theme it **finds**
must set exactly the same names - found rather than named, so a cull cannot make the
check stale - the unnamed base block must be one of them value for value, every room
offered must have a rule to paint it and every rule a room somebody can pick, both
languages must word all of them, a stored room that no longer exists must fall back to
`auto`, the light values written twice - once for
`prefers-color-scheme`, once for the explicit choice - must agree name for name, and
`--paper` must be one colour everywhere. Before that check there were 40 distinct
literals, one grey doing two jobs and two greys doing one, a digit apart.

It compares bands, column edges and form fields, and takes the quote template as a
fifth argument so the fields are covered too. Exact equality is not the bar - the echo has no
font file and assumes a cap-height ratio - but the two must follow the same rules
and land within a few millimetres. It has already caught three real divergences: the engine learnt to size a band from
its content, then to cascade the text style from the section, and the echo had
neither - the second left every measurement as NaN the moment a template stopped
writing out its defaults.
