/// The content editor.
///
/// Lifted out of frisket-editor.html on 24 August 2026, unchanged, so that something
/// other than that one page can load it - a custom element cannot reach a function
/// declared inside a page's inline script.
///
/// Since 18 October 2026 an editor is a call - `frisketEditor(seams)` - rather than the
/// file itself, so a page can hold as many as it likes. check-editor.js still reaches
/// every declaration by name: it plants its handles at the `@harness` line near the end
/// of that function, inside, where they can see.
///
/// What decides where an editor draws is the root and the theme target it is handed: the
/// document for the plain page, a shadow root for each <frisket-editor>.
// ---------------------------------------------------------------------------
// Frisket - the flow editor.
//
// The second face over the same core. Where the designer places things at
// coordinates - LayoutMode.Absolute, the rare path - this one edits a flow:
// LayoutMode.Stack, which is what the engine is actually built for. That is why the
// two exist rather than one with fewer buttons: they are the two layout modes, and
// the model named the seam before anybody drew it.
//
// The one rule this file is built around: ONE CONTENTEDITABLE PER TEXT BLOCK, never
// one for the sheet. An editor whose whole page is editable has to treat a picture
// or a table as a foreign body inside flowing text, which is exactly why pictures
// and tables are half-broken in every editor of that shape. Body.Childs is already
// a list of peers, so the DOM is built the same way and the problem never arises.
//
// It produces the same JSON the designer does. Typing makes an AdvancedTextItem,
// inserting a picture makes an ImageItem with its bytes; there is no second format
// and no conversion step.
// ---------------------------------------------------------------------------

// The core needs one thing from whoever loads it, and a browser has it.
setTextRuler((text, font) => {
  const ruler = setTextRuler.canvas ??= document.createElement("canvas").getContext("2d");
  ruler.font = font;
  return ruler.measureText(text).width;
});

// The interface's language is the page's: one dictionary in the core, chosen once.
let lang0 = "en";
try { lang0 = localStorage.getItem("frisket.lang") || "en"; } catch {}
setLang(lang0);

// The engine's fonts, asked of each server once per page however many editors name it,
// and loaded once: a font belongs to the document, so a second editor loading the same
// files would only add them again. A failed answer is not kept, so an editor switched to
// the server's list later asks afresh.
//
// Kept by server since 20 October 2026. The address was one constant here, written for the
// preview server on this machine, so a host could not say where its own engine was - and
// two editors could not be told two engines. Each editor is told its own now (`server`),
// and these two remember, per address, what has been asked and what has been loaded.
let serverFontsAsked = new Map();   // base URL -> the promise of its /fonts list
let facesLoaded = new Set();        // base URLs whose files are in the document already

function serverFonts(base, send) {
  if (!serverFontsAsked.has(base))
    serverFontsAsked.set(base, send(base + "/fonts")
      .then(r => r.ok ? r.json() : Promise.reject(new Error(String(r.status))))
      .catch(e => { serverFontsAsked.delete(base); throw e; }));
  return serverFontsAsked.get(base);
}

/// The engine's own files, so the sheet draws with what the PDF will embed rather than
/// with whatever this machine has installed under that name - or, when it has nothing,
/// with the browser's default face, which is a serif. Added to the document's set: a
/// font belongs to the document, and a shadow root draws from the same set. Nothing to
/// redraw once they arrive - the sheet is laid out by the browser, which repaints.
function loadFaces(base, faces) {
  if (facesLoaded.has(base) || typeof FontFace !== "function" || !document.fonts) return;
  facesLoaded.add(base);
  for (const d of faces)
    new FontFace(d.family, `url("${d.url}")`, { weight: d.weight, style: d.style })
      .load().then(face => document.fonts.add(face)).catch(() => {});
}

// ---------------------------------------------------------------------------
// One editor.
//
// Everything that changes as somebody types - the document, the block being edited, the
// cached selection, the toolbar's state, the markup it draws into - lives inside this
// function, one copy per call. Until 18 October 2026 it lived at the top of this file,
// one copy per PAGE: a second <frisket-editor> took over the first one's root, its
// document and its selection, and the first went on showing a sheet whose edits landed
// in the second. Nothing failed. A host with two editors on one form would simply have
// lost a document.
//
// `seams` is what the caller declares - the end of this function says what each is.
// ---------------------------------------------------------------------------
function frisketEditor(seams = {}) {
  let report = blankReport();
  let current = null;                    // the block element being edited

  /// A style chosen with nothing selected, waiting for the next character.
  ///
  /// `{ font, at }` - what was asked for, and the offset it was asked at. It lives for
  /// exactly one keystroke: that keystroke is inserted inside a span carrying the font,
  /// and every character after it lands in that span on its own, which is how the model
  /// and the page agree without either being told twice.
  ///
  /// Dropped when the caret moves without typing, and when the block changes. Pressing
  /// the button again toggles the key back off rather than arming a second time.
  let pending = null;
  // The sheets come from PAPER_SIZES in the core, which both faces share. This file
  // kept a list of its own, and the two had drifted: five sizes here, four there, and
  // only two in common.



  /// The editor's own markup, so that whatever it is mounted into gets the same one.
  ///
  /// A string rather than the page's body, because a shadow root cannot be handed markup
  /// that lives in a document it is not part of - and a second copy in the element would
  /// be the thing section 1 is about.
  /// What a face shows. One object, set before mount() and read by nothing else.
  ///
  /// The editor somebody EMBEDS is not the editor somebody develops. A person typing a
  /// letter into a host application has no business being shown a render target, a page
  /// size or a font source: the host chose those when it decided what it was producing,
  /// and showing them again invites two answers to one question. It has no business
  /// being shown a Save button either - the host owns the document and reads it back
  /// through the element.
  ///
  /// What that person must not get is a document `frisket check` would refuse, produced
  /// in silence. So `panel: false` hides the findings, it does NOT stop them: runChecks
  /// still runs on every redraw and hands them to `onChecks`, which paints the panel when
  /// there is one and otherwise gives them to the host. Section 5c's rule was written
  /// against an editor that quietly ships a broken document, and a host holding the
  /// findings and choosing what to do with them is not that - it is the same answer this
  /// project gives for a font's fsType and for an imported page: only the caller can
  /// know, and the engine says so.
  ///
  /// `files` is where Open and Save live, and "toolbar" exists because it was asked for:
  /// a page with no chrome still needs a way in and out, and the toolbar is where a
  /// person is already looking.
  let SHOWN = { chrome: true, files: "header", panel: true };

  /// What a page or a host asked for as a margin, in millimetres, or null for "you
  /// decide" - which chooseMargins answers from what this face can show.
  let margins = null;

  /// Where the engine is whose fonts the sheet draws with - a base URL answering GET /fonts
  /// and /fonts/file as apps/server does, "" for the page's own origin - or null when this
  /// editor was told of none. And how to reach it: the host's fetch, for a host that adds
  /// its own credentials, read at every request.
  let server = null;
  const send = (url, init) => seams.fetch ? seams.fetch(url, init) : fetch(url, init);

  /// What is about to be produced, so the Capable profile can say what the template loses
  /// on the way there. The engine's own `RenderTarget` record, name for name.
  ///
  /// A VARIABLE, not two checkboxes read at every check. It used to live nowhere else -
  /// `{ Tagged: el("targetTagged").checked, Interactive: el("targetInteractive").checked }`
  /// built fresh on each run - so a face without those controls had no target at all, and
  /// an absent one is not the safe answer: the Capable profile gates on the object being
  /// present rather than on its fields, so an empty-but-present target fires every rule in
  /// it on every document. A host sets this and there are no controls to disagree with.
  ///
  /// The defaults are the richest output, for the reason the engine gives: a caller that
  /// says nothing is told nothing, rather than warned about a loss that is not happening.
  ///
  /// `Paged` was never passed by this face at all, so `can.absoluteInFlow` - the one rule
  /// that speaks for a message rather than a page - has never once fired here.
  let renderTarget = { Tagged: true, Interactive: true, Paged: true };

  /// Puts a document in, from wherever it came - a file, a host, a draft on a server.
  ///
  /// The same path the file picker takes, which is the point: a host handing over JSON
  /// and a person choosing a file must arrive at the same place, or one of the two gets a
  /// tree the rest of the editor was not written for. `normaliseReport` and `hydrate` are
  /// what make that true - the second because nothing here may assume a key is present,
  /// and a forgotten read does not crash, it positions at NaN.
  function loadReport(json) {
    report = normaliseReport(typeof json === "string" ? JSON.parse(json) : json);
    hydrate(report.Sections[0]);
    current = null;
    syncPanel();
    draw();
  }

  /// Where the findings go. Painting the panel is one answer and not the only one.
  ///
  /// A face that hides the panel does not stop checking - section 5c's rule is against an
  /// editor that quietly produces a document `frisket check` would refuse, and silence is
  /// exactly what it forbids. So the findings are always computed and always handed over;
  /// what a host does with them is the host's to decide, which is the same answer this
  /// project gives for a font's fsType and for an imported page.
  let onChecks = null;

  /// The header: what the page is called, and the two choices that belong to a page
  /// rather than to a document - which language the interface speaks, and which room it
  /// is in. A host has its own answers to both, which is why this whole block goes.
  const EDITOR_HEADER = `
  <header>
    <h1>Frisket</h1>
    <button id="new" data-i18n="action.new">New</button>
    <button id="open" data-i18n="action.open">Open JSON…</button>
    <button id="save" class="primary" data-i18n="action.save">Save JSON</button>
    <button id="html" data-i18n="editor.openHtml" data-i18n-title="editor.htmlWhat">HTML…</button>
    <button id="mail" data-i18n="editor.openMail" data-i18n-title="editor.mailWhat">Mail…</button>
    <input type="file" id="file" accept=".json,application/json" hidden>
    <span class="grow"></span>
    <select id="lang" title="Language">
      <option value="en">EN</option>
      <option value="fr">FR</option>
    </select>
    <!-- Filled from THEMES. A list written twice is two lists, and the one nobody
         updates is the one that ships. -->
    <select id="theme"></select>
  </header>
  `;

  /// Open and Save as toolbar buttons, for a face with no header. The hidden file input
  /// travels with them: the button only clicks it, so leaving it behind gives a control
  /// that throws when pressed rather than one that is missing.
  const EDITOR_FILES = `
          <button id="open" data-i18n-title="action.open">
            <svg viewBox="0 0 16 16"><path d="M1.5 4.5h5l1.5 2h6.5v7h-13z"/></svg>
          </button>
          <button id="save" data-i18n-title="action.save">
            <svg viewBox="0 0 16 16"><path d="M2.5 2.5h8l3 3v8h-11z"/><path d="M5 2.5v4h5v-4"/>
              <path d="M5 13.5v-4h6v4"/></svg>
          </button>
          <input type="file" id="file" accept=".json,application/json" hidden>
          <span class="sep"></span>
  `;

  const EDITOR_MARKUP = () => `
  ${SHOWN.chrome ? EDITOR_HEADER : ""}

  <main${SHOWN.panel ? "" : ' class="alone"'}>
    <div id="desk">
      <div id="frame">
        <div id="toolbar">
  ${SHOWN.files === "toolbar" ? EDITOR_FILES : ""}
          <button data-style="Bold" data-i18n-title="style.bold"><b class="glyph">B</b></button>
          <button data-style="Italic" data-i18n-title="style.italic"><i class="glyph">I</i></button>
          <button data-style="Underline" data-i18n-title="style.underline"><u class="glyph">U</u></button>
          <button data-style="Strikethrough" data-i18n-title="style.strikethrough"><s class="glyph">S</s></button>

          <select id="family" data-i18n-title="field.font" style="max-width:176px"></select>

          <span class="sep"></span>

          <select id="size" data-i18n-title="field.size">
            <option value="" data-i18n="size.normal">Normal</option>
            <option value="0.75" data-i18n="size.small">Small</option>
            <option value="1.5" data-i18n="size.large">Large</option>
            <option value="2.5" data-i18n="size.huge">Huge</option>
          </select>

          <div class="ink">
            <!-- The native picker anchors its popup to this input's box, so it lives here
                 over the control rather than inside the menu: put in the menu, it opened
                 in the corner of the window. Invisible and not clickable - the swatch and
                 the chevron take the clicks, and "New colour" opens this one by hand. -->
            <input type="color" id="colour" value="#111111">
            <button id="colourApply" data-i18n-title="richText.applyColour"><span id="well"></span></button>
            <button id="colourOpen" class="chevron" data-i18n-title="field.colour">
              <svg viewBox="0 0 16 16"><path d="M4 6.5l4 4 4-4"/></svg>
            </button>
            <!-- Two kinds of thing, and they are separated now rather than stacked.
                 The grid holds COLOURS, "no colour" among them, because that is what it
                 is - the choice of not having one, which is a choice about colour and
                 not an errand. It went at the foot under "New colour", which put a
                 value below an action and read as an afterthought.

                 "New colour" opens a picker: it is the errand, so it sits under a rule
                 where every colour menu in the world puts its custom entry. -->
            <div id="inkMenu" hidden>
              <div id="inkSwatches"></div>
              <button id="colourPick" data-i18n="richText.newColour">New colour</button>
            </div>
          </div>

          <span class="sep"></span>

          <button data-align="Left" data-i18n-title="HorizontalAlignment.Left">
            <svg viewBox="0 0 16 16"><path d="M2 4h12M2 7h8M2 10h12M2 13h7"/></svg>
          </button>
          <button data-align="Center" data-i18n-title="HorizontalAlignment.Center">
            <svg viewBox="0 0 16 16"><path d="M2 4h12M4 7h8M2 10h12M4.5 13h7"/></svg>
          </button>
          <button data-align="Right" data-i18n-title="HorizontalAlignment.Right">
            <svg viewBox="0 0 16 16"><path d="M2 4h12M6 7h8M2 10h12M7 13h7"/></svg>
          </button>
          <button data-align="Justify" data-i18n-title="HorizontalAlignment.Justify">
            <svg viewBox="0 0 16 16"><path d="M2 4h12M2 7h12M2 10h12M2 13h12"/></svg>
          </button>

          <span class="sep"></span>

          <button data-valign="Start" data-i18n-title="ItemAlign.Top">
            <svg viewBox="0 0 16 16"><path d="M2 2.5h12M5 5.5h6v8H5z"/></svg>
          </button>
          <button data-valign="Center" data-i18n-title="ItemAlign.Middle">
            <svg viewBox="0 0 16 16"><path d="M2 8h12M5 4h6v8H5z"/></svg>
          </button>
          <button data-valign="End" data-i18n-title="ItemAlign.Bottom">
            <svg viewBox="0 0 16 16"><path d="M2 13.5h12M5 2.5h6v8H5z"/></svg>
          </button>

          <span class="sep"></span>

          <button id="addImage" data-i18n-title="editor.addImage">
            <svg viewBox="0 0 16 16"><rect x="1.5" y="2.5" width="13" height="11" rx="1.5"/>
              <circle cx="5.5" cy="6" r="1.2"/><path d="M2 11.5l3.5-3 3 2.5 2.5-2 3 3"/></svg>
          </button>
          <button id="addTable" data-i18n-title="add.table">
            <svg viewBox="0 0 16 16"><rect x="1.5" y="3" width="13" height="10" rx="1"/>
              <path d="M1.5 6.5h13M6 6.5V13M10.5 6.5V13"/></svg>
          </button>
          <button id="addRule" data-i18n-title="add.rule">
            <svg viewBox="0 0 16 16"><path d="M2 8h12"/></svg>
          </button>
          <button id="addText" data-i18n-title="editor.addText">
            <svg viewBox="0 0 16 16"><path d="M2 3.5h12M2 7h12M2 10.5h8"/></svg>
          </button>
          <!-- The host's merge fields. Hidden until a host offers some: a menu of nothing
               would be a button that does nothing. -->
          <div class="fields" id="fieldsTool" hidden>
            <button id="addField" data-i18n-title="editor.addField">
              <svg viewBox="0 0 16 16"><path d="M6 2.5c-1.6 0-2 .8-2 2.2v1.6c0 1-.6 1.7-1.6 1.7 1 0 1.6.7 1.6 1.7v1.6c0 1.4.4 2.2 2 2.2M10 2.5c1.6 0 2 .8 2 2.2v1.6c0 1 .6 1.7 1.6 1.7-1 0-1.6.7-1.6 1.7v1.6c0 1.4-.4 2.2-2 2.2"/></svg>
            </button>
            <div id="fieldMenu" hidden></div>
          </div>

          <span class="sep"></span>

          <button id="removeBlock" data-i18n-title="editor.removeBlock">
            <svg viewBox="0 0 16 16"><path d="M3 4.5h10M6.5 4.5V3h3v1.5M4.5 4.5l.7 8.5h5.6l.7-8.5"/></svg>
          </button>
        </div>

        <div id="sheet"></div>
      </div>
    </div>
  ${SHOWN.panel ? EDITOR_ASIDE : ""}
  </main>
  `;


  /// The side panel: what the FILE is (title, language, page size), what typefaces it is
  /// allowed, and what the checks found. The first two are the host's decision when this
  /// is embedded; the third is not hidden because it stopped mattering, but because the
  /// person typing is not the person who answers for it.
  const EDITOR_ASIDE = `
    <aside>
      <h2 data-i18n="editor.document">Document</h2>
      <div class="field">
        <label data-i18n="field.title">Title</label>
        <input type="text" id="title">
      </div>
      <div class="field">
        <label data-i18n="field.language">Language</label>
        <input type="text" id="language" placeholder="en-GB">
      </div>
      <div class="field">
        <label data-i18n="field.pageSize">Page size</label>
        <select id="paper"></select>
      </div>

      <h2 data-i18n="editor.fonts">Typefaces</h2>
      <div class="field">
        <label data-i18n="editor.fonts">Typefaces</label>
        <select id="fontSource">
          <option value="library" data-i18n="fonts.library">From the server</option>
          <option value="websafe" data-i18n="fonts.websafe">Web-safe (email)</option>
        </select>
      </div>
      <div class="hint" id="fontHint"></div>

      <h2 data-i18n="panel.issues">Checks</h2>
      <div class="hint" data-i18n="hint.target"></div>
      <label class="check"><input type="checkbox" id="targetTagged" checked>
        <span data-i18n="target.tagged">tagged output</span></label>
      <label class="check"><input type="checkbox" id="targetInteractive" checked>
        <span data-i18n="target.interactive">fields stay fillable</span></label>
      <div id="count" class="count good"></div>
      <ul id="issues" class="issues"></ul>

      <div class="hint" data-i18n="editor.hint"></div>
    </aside>
  `;


  /// Where this editor's own markup lives, and which element carries its theme.
  ///
  /// `document` today, a shadow root the day the editor is packaged as an element. Only
  /// two things differ between the two: where one of our own nodes is looked up, and
  /// which element holds `data-theme` and `--paper`. Everything else `document` does in
  /// this file is genuinely the browser's and stays - `createElement`, `createRange`,
  /// `execCommand`, the drag listeners that must outlive the pointer leaving us, the
  /// `selectionchange` that fires nowhere else, and the detached preview's own document.
  ///
  /// One named seam, asked rather than assumed. A blanket rename of `document` is what
  /// killed the designer for a week (section 16); this is the opposite move, and the
  /// host's identifiers are left alone.
  let uiRoot = document;
  let themeTarget = document.documentElement;

  /// What the markup is planted in. The page hands over a div of its own; an element
  /// hands over its shadow root.
  let uiHost = null;

  /// The selection, asked of whatever we are drawing in rather than of the window.
  ///
  /// `window.getSelection()` cannot address a range inside a shadow root: `addRange` with
  /// nodes that live in one is ignored, silently. focusBlock then leaves a caret exactly
  /// where `focus()` drew it and a selection that is somewhere else, which reads as a page
  /// where the cursor blinks and nothing types. That is what the element did on its first
  /// morning, and the standalone page never could have shown it.
  ///
  /// A ShadowRoot has `getSelection()` of its own, and so does a Document, so asking
  /// `uiRoot` is right in both - and the fallback is there for a root that has neither
  /// rather than as a guess.
  const selection = () => uiRoot.getSelection ? uiRoot.getSelection() : window.getSelection();

  const el = id => uiRoot.getElementById(id);
  const uiAll = selector => uiRoot.querySelectorAll(selector);
  const body = () => report.Sections[0].Childs.find(c => c.$type === "Body");

  // ---------------------------------------------------------------------------
  // Wording and theme - the page's business, as in the designer.
  // ---------------------------------------------------------------------------

  let theme = "auto";
  try { theme = themeOrAuto(localStorage.getItem("frisket.theme")); } catch {}

  function applyTheme() {
    if (theme === "auto") themeTarget.removeAttribute("data-theme");
    else themeTarget.setAttribute("data-theme", theme);
  }

  /// The stock the template says it is meant for, over the stylesheet's white.
  ///
  /// Set at run time and never in the sheet, so --paper still holds exactly one colour
  /// there - which is what the palette audit checks. Clearing it puts the white back
  /// rather than leaving the last template's stock behind.
  ///
  /// Called where the report is drawn and deliberately NOT from applyTheme: the paper
  /// follows the template and nothing else, which is the whole of section 5b. Hanging it
  /// off the theme also ran it before the report existed, and a typeof guard does not
  /// save a let in its dead zone - the error said the coupling was wrong before it said
  /// anything about JavaScript.
  function applyPaper() {
    const paper = paperColour(report);
    if (paper) themeTarget.style.setProperty("--paper", paper);
    else themeTarget.style.removeProperty("--paper");
  }

  /// Fills the markup's wording. A toolbar of icons says what it is through its
  /// tooltips, so those are translated like everything else rather than left in one
  /// language because they are short.
  function applyStaticText() {
    // Only when this editor IS the page. An editor embedded in somebody else's page
    // renamed their tab, on every redraw of its wording.
    if (uiRoot === document) document.title = t("editor.title");
    uiAll("[data-i18n]").forEach(e => { e.textContent = t(e.dataset.i18n); });
    uiAll("[data-i18n-title]").forEach(e => { e.title = t(e.dataset.i18nTitle); });
  }

  // ---------------------------------------------------------------------------
  // A blank document for this face: one section, one body that stacks, one empty
  // paragraph to type into. No header and no footer - they are bands at coordinates,
  // which is the other face's subject.
  // ---------------------------------------------------------------------------
  function blankFlow() {
    const rep = blankReport();
    const section = rep.Sections[0];
    section.Childs = [];
    section.Dimension = { Width: 210, Height: 297 };

    const bodyItem = newItem("Body");
    bodyItem.Layout = "Stack";
    const pad = chooseMargins(margins);
    bodyItem.Style.Padding = { Left: pad, Top: pad, Right: pad, Bottom: pad };
    bodyItem.Childs = [emptyParagraph()];
    section.Childs.push(bodyItem);

    rep.Title = "";
    return rep;
  }

  function emptyParagraph() {
    const item = newItem("AdvancedTextItem");
    item.Runs = [{ Text: "", Font: null }];
    return item;
  }

  // ---------------------------------------------------------------------------
  // Drawing the sheet. One element per child of the body, in order.
  // ---------------------------------------------------------------------------
  function draw() {
    applyPaper();   // the stock is the template's, so it follows the template
    normaliseReport(report);
    hydrate(report.Sections[0]);

    const section = report.Sections[0];
    const bodyItem = body();
    const pad = (bodyItem && bodyItem.Style && bodyItem.Style.Padding) || {};

    el("frame").style.setProperty("--sheet-width", (section.Dimension.Width || 210) + "mm");

    const sheet = el("sheet");
    sheet.style.setProperty("--sheet-pad",
      `${pad.Top ?? 20}mm ${pad.Right ?? 20}mm ${pad.Bottom ?? 20}mm ${pad.Left ?? 20}mm`);

    // The document's own type size, so what is typed is the size it will print at.
    const rem = report.RootFontSize || 3;
    sheet.style.fontSize = (rem / 0.7) + "mm";
    sheet.style.fontFamily = sheetFace();

    const keep = current && current.dataset.index;
    sheet.innerHTML = "";
    (bodyItem ? bodyItem.Childs : []).forEach((item, i) => sheet.appendChild(blockFor(item, i)));

    if (keep != null) {
      const back = sheet.querySelector(`.block[data-index="${keep}"]`);
      if (back) markCurrent(back);
    }
    runChecks();
  }

  /// One element of the sheet. The node carries the model object it draws, so anything
  /// asking "what is being edited" reads it off the node rather than indexing back into
  /// the body - which stopped being possible the moment a block could hold another one.
  function blockFor(item, index) {
    const node = document.createElement("div");
    node.className = "block";
    node.dataset.index = index;
    node._item = item;
    if (item.$type === "AdvancedTextItem") node.dataset.plain = "";

    fillBlock(node, item);

    // A div holds focus only if it is told it may. Without this a picture could be
    // clicked but never keyed, so alt+arrow reached nothing.
    if (!editableIn(node)) node.tabIndex = -1;

    dragBehaviour(node, item);
    node.addEventListener("focusin", () => markCurrent(node));
    node.addEventListener("mousedown", () => markCurrent(node));
    return node;
  }

  /// Draws one item into a node. Split out from blockFor because a paired container
  /// draws its two children the same way, and two copies of this is how a face starts
  /// disagreeing with itself about what a picture looks like.
  function fillBlock(node, item) {
    if (item.$type === "AdvancedTextItem") {
      // A surface inside the block rather than the block itself. Everything the block
      // carries - the handle, the kind label, a grip - is then furniture beside the
      // text instead of inside it, which is the fault that showed up twice: the label
      // was read back into the paragraph, and so was the handle.
      const surface = document.createElement("div");
      surface.className = "surface";
      surface.contentEditable = "true";
      surface.spellcheck = true;
      surface._item = item;

      // The model held the alignment and the page never showed it: the button lit up,
      // the JSON was right, and the paragraph sat where it always had. A face that
      // records a choice without drawing it is worse than one that cannot make it.
      const how = (item.Font && item.Font.HorizontalAlignment) || "Left";
      surface.style.textAlign = how === "Center" ? "center"
                              : how === "Right" ? "right"
                              : how === "Justify" ? "justify" : "left";

      runsToSurface(item.Runs || [], surface);

      // An editable element accepts anything the browser cares to put in it. A pasted
      // paragraph of styled HTML means nothing to the model, so it comes in as text.
      surface.addEventListener("paste", e => {
        e.preventDefault();
        const text = (e.clipboardData || window.clipboardData).getData("text/plain");
        if (text) document.execCommand("insertText", false, text);
      });

      // Before the character arrives, not after: a style applied to the model afterwards
      // would not be on the page until a redraw, and redrawing mid-word takes the caret
      // with it. Inserted as a span instead, so the DOM and the model agree by the same
      // route they always do - surfaceToRuns reads data-font back out.
      //
      // One keystroke is all it has to last. The caret ends up inside the span, and every
      // character after it lands there on its own.
      surface.addEventListener("beforeinput", e => {
        if (!pending || e.inputType !== "insertText" || !e.data) return;

        const range = selection().rangeCount ? selection().getRangeAt(0) : null;
        if (!range || !range.collapsed) return;

        e.preventDefault();
        const span = runElement({ Text: e.data, Font: pending.font });
        range.insertNode(span);

        const after = document.createRange();
        after.setStart(span.firstChild, span.firstChild.length);
        after.collapse(true);
        selection().removeAllRanges();
        selection().addRange(after);

        pending = null;
        const runs = surfaceToRuns(surface);
        item.Runs = runs.length ? runs : [{ Text: "", Font: null }];
        runChecks();
      });

      surface.addEventListener("input", () => {
        const runs = surfaceToRuns(surface);
        item.Runs = runs.length ? runs : [{ Text: "", Font: null }];
        runChecks();
      });

      node.appendChild(surface);
      return;
    }

    if (item.$type === "ImageItem") {
      const img = document.createElement("img");

      // A browser makes every picture draggable of its own accord, and that drag is not
      // ours: it carries the HTML rather than the block, so it lands wherever the
      // browser feels like putting it and the model learns nothing. Once a block has a
      // handle there is no reason left to allow it - two ways to start a drag, one of
      // which does something else, is worse than one.
      img.draggable = false;

      const source = item.Source;
      const fromData = (item.SourceField || "").trim();
      if (source && source.Data) img.src = `data:${source.MediaType || "image/png"};base64,${source.Data}`;
      else if (fromData) {
        // Filled from the data when the document is printed, so there is nothing to draw
        // yet: the host's sample when it gave one, otherwise a frame naming where the
        // picture will come from. Still an <img>, so the grip resizes it like any other.
        const field = fieldAt(fromData);
        img.src = field && field.sample ? field.sample : pictureFromData(fromData, item);
        img.classList.add("from-data");
      }

      const width = item.Style.Location.WidthValue;
      if (Number.isFinite(Number(width))) img.style.width = Number(width) + "mm";

      // The same three positions the engine will use, so the block shows where the
      // picture will actually sit rather than always against the left.
      const across = (item.Style && item.Style.AlignHorizontal) || "None";
      img.style.marginLeft = across === "Center" || across === "End" ? "auto" : "";
      img.style.marginRight = across === "Center" ? "auto" : "";

      node.appendChild(img);
      node.appendChild(gripFor(item, node));

      const alt = document.createElement("input");
      alt.className = "alt";
      alt.placeholder = t("field.altText");
      alt.value = item.AltText || "";
      alt.oninput = () => { item.AltText = alt.value; runChecks(); };
      node.appendChild(alt);
      return;
    }

    if (item.$type === "LineItem") {
      node.appendChild(document.createElement("hr"));
      return;
    }

    if (item.$type === "TableItem") {
      fillTable(node, item);
      return;
    }

    // A picture beside a paragraph. In the model this is an absolutely laid out
    // container: the picture at a coordinate with a width, the paragraph starting
    // after it and stuck to the far edge. Dock alone will not do it - ArrangeChildren
    // hands every child the same rectangle, so a docked picture and a stretched
    // paragraph would sit on top of one another - which is why the coordinates are
    // computed here rather than left to the engine to infer.
    if (isPair(item)) {
      node.classList.add("pair");
      for (const child of item.Childs) {
        const cell = document.createElement("div");
        cell.className = "cell";
        cell._item = child;
        if (child.$type === "AdvancedTextItem") cell.dataset.plain = "";
        // Whichever side has a width of its own keeps it; the other takes what is left.
        // The same question naturalWidth answers for the geometry, asked of the drawing.
        cell.style.flex = naturalWidth(child) != null ? "0 0 auto" : "1 1 auto";

        // Where the cell sits down the height of the pair. The engine reads
        // Style.AlignVertical through ComputeVertical, which is only consulted in an
        // absolutely laid out container - which is exactly what a pair is, and why
        // these buttons mean nothing anywhere else.
        const down = (child.Style && child.Style.AlignVertical) || "None";
        cell.style.alignSelf = down === "Center" ? "center"
                             : down === "End" ? "flex-end" : "flex-start";
        fillBlock(cell, child);

        // A handle of its own, so a half can be taken back out. Only lifted, never
        // dropped onto: the pair around it is the drop target, and a zone inside a
        // cell would mean nothing the model can express.
        liftBy(cell, child, "editor.moveHalf");

        // BOTH stopped, not just the one. focusin bubbles like any other event, so a
        // cell that stopped mousedown and let focusin through had the block outside it
        // claim `current` a moment later - and that block's item is the container, not
        // the paragraph that was clicked. Everything reading currentItem() then answered
        // about the wrong thing: the lists went blank and the swatch fell back to the
        // last colour chosen, which is a colour from somewhere else entirely.
        cell.addEventListener("focusin", e => { e.stopPropagation(); markCurrent(cell); });
        cell.addEventListener("mousedown", e => { e.stopPropagation(); markCurrent(cell); });
        node.appendChild(cell);
      }
      return;
    }

    // A stack: several blocks one under another, which is what a cell holding a caption
    // under a picture is. Drawn by the same fillBlock as everything else, so a stack of a
    // table and a paragraph needs no case of its own here - and each part keeps a handle,
    // so nothing put in one is stuck in it.
    if (item.$type === "ContainerItem" && item.Layout !== "Absolute") {
      node.classList.add("stack");
      for (const child of item.Childs || []) {
        const slot = document.createElement("div");
        slot.className = "slot";
        slot._item = child;
        if (child.$type === "AdvancedTextItem") slot.dataset.plain = "";

        fillBlock(slot, child);
        liftBy(slot, child, "editor.moveHalf");

        slot.addEventListener("focusin", e => { e.stopPropagation(); markCurrent(slot); });
        slot.addEventListener("mousedown", e => { e.stopPropagation(); markCurrent(slot); });
        node.appendChild(slot);
      }
      return;
    }

    // Everything this face does not edit. It is not lost and it is not hidden: it is
    // named, and the page says where it can be edited. Showing half of it would be the
    // worse answer.
    const note = document.createElement("div");
    note.className = "foreign";
    note.innerHTML = `<b>${esc(labelOf(item.$type))}</b> — ${esc(t("editor.foreign"))}`;
    node.appendChild(note);
  }

  /// A table, and the rails that add and remove its rows and columns.
  ///
  /// Each cell is its own editable surface rather than one for the table, which is the
  /// same decision the whole face rests on - and the reason a picture can go in a cell
  /// without becoming a foreign body inside somebody's paragraph.
  ///
  /// The model was ready for this: a TableItem holds TableRow children, a row holds a
  /// cell per column, and Columns is the list of widths. Nothing here is new to the
  /// engine; what was missing was a way to touch it without the designer.
  function fillTable(node, item) {
    // A table repeated over a list says so above itself, in the block's furniture rather
    // than in a cell: the one row on the sheet is the one the engine stamps out per entry.
    const over = (item.RepeatOver || "").trim();
    if (over) {
      const list = fields.find(field => field.type === "list" && field.path === over);
      const mark = document.createElement("div");
      mark.className = "repeat-mark";
      mark.textContent = t("editor.repeats", { list: list ? list.label : over });
      node.appendChild(mark);
    }

    const table = document.createElement("table");
    table.className = "grid";
    const columns = (item.Columns || []).length || 1;

    for (const row of item.Childs || []) {
      if (row.$type !== "TableRow") continue;
      const tr = document.createElement("tr");

      for (let c = 0; c < columns; c++) {
        // A row that hands back fewer cells than the table has columns is refused by a
        // conformance checker and unreadable aloud (CLAUDE.md §11), so a missing cell is
        // filled in rather than skipped.
        let cell = (row.Childs || [])[c];
        if (!cell) {
          cell = newItem("AdvancedTextItem");
          cell.Runs = [{ Text: "", Font: null }];
          (row.Childs ??= []).push(cell);
        }

        const td = document.createElement(row.IsHeader ? "th" : "td");
        td._item = cell;
        td._row = row;
        td._at = c;
        td._table = item;
        const width = (item.Columns || [])[c];
        if (width && Number.isFinite(Number(width.Width)))
          td.style.width = Number(width.Width) + "mm";

        fillBlock(td, cell);

        // A picture in a cell is dragged out of it the way half of a pair is - by a
        // handle of its own. Without one it would be a one-way door again: in through
        // the drop, and out only through the panel.
        if (cell.$type === "ImageItem") liftBy(td, cell, "editor.moveHalf");

        cellDrop(td, row, c);
        td.addEventListener("focusin", e => { e.stopPropagation(); markCurrent(td); });
        td.addEventListener("mousedown", e => { e.stopPropagation(); markCurrent(td); });
        tr.appendChild(td);
      }
      table.appendChild(tr);
    }

    node.classList.add("tabular");
    node.appendChild(table);

    const across = rail("columns", columns, item);
    const down = rail("rows", (item.Childs || []).length, item);
    node.appendChild(across);
    node.appendChild(down);

    // Measured, never spaced. A row is as tall as what somebody typed in it and a
    // column as wide as its width in millimetres, so a rail of evenly spaced buttons
    // points at nothing after the first one - which is what it did: two crosses and a
    // plus bunched in the corner, and no telling which column any of them meant.
    const follow = () => placeRails(table, across, down);
    requestAnimationFrame(follow);

    // Placed once, they stay where the table was when it was drawn - and a row grows
    // the moment somebody types a second line in it, so the crosses end up pointing at
    // where the rows used to be. That is worse than pointing nowhere: it is a button
    // that means one row and looks like it means another.
    //
    // input covers typing, which is the way a row changes height most often; the
    // observer covers everything else - a picture arriving, the window narrowing, a
    // font finishing loading - none of which passes through a keystroke.
    table.addEventListener("input", follow);
    if (typeof ResizeObserver === "function") new ResizeObserver(follow).observe(table);
  }

  /// Puts each button over the row or column it takes away, from the table's own
  /// geometry. The same reasoning as the picture's grip: a browser knows where things
  /// ended up and we do not, so it is asked rather than guessed at.
  function placeRails(table, across, down) {
    const box = table.getBoundingClientRect();
    const rows = [...table.childNodes].filter(r => r.tagName === "TR");
    const head = rows[0];
    if (!head) return;

    const centre = (button, along) => {
      if (along === null) return;
      button.style[across.contains(button) ? "left" : "top"] = (along - 8) + "px";
    };

    // Past the last one, never past the table: a table box is not its cells - it can be
    // wider than them or narrower - so measuring the plus against it puts it inside the
    // grid on one table and off the sheet on the next.
    const past = (list, edge) => {
      const last = list[list.length - 1];
      const r = last ? last.getBoundingClientRect() : null;
      return r ? r[edge] - (edge === "right" ? box.left : box.top) + 18 : null;
    };

    const cells = [...head.childNodes];
    [...across.childNodes].forEach((button, i) => {
      // The last button is the "+", which belongs past the final column rather than
      // over one: adding a column is not a question about any column that exists.
      const r = i < cells.length ? cells[i].getBoundingClientRect() : null;
      centre(button, r ? r.left - box.left + r.width / 2 : past(cells, "right"));
    });

    [...down.childNodes].forEach((button, i) => {
      const r = i < rows.length ? rows[i].getBoundingClientRect() : null;
      centre(button, r ? r.top - box.top + r.height / 2 : past(rows, "bottom"));
    });
  }

  /// A cell as a place to drop a picture.
  ///
  /// The model was ready: a cell is one item of a row, fillBlock already draws a
  /// picture, and the engine has never cared which kind of item a cell holds. What was
  /// missing was the gesture - so a picture could be beside a table and never in it.
  ///
  /// A cell holds one item, so it only accepts one where there is nothing to lose, and
  /// it says so by not lighting up. Refusing after the fact is the fault the side zones
  /// had: a drop that is offered and then quietly declined teaches people to distrust
  /// what they are shown.
  function cellDrop(td, row, at) {
    const takes = () => canDropInCell(lifted, row.Childs[at]);

    td.addEventListener("dragover", e => {
      if (!takes()) return;
      e.preventDefault();
      e.stopPropagation();
      e.dataTransfer.dropEffect = "move";
      td.classList.add("landing");
    });

    td.addEventListener("dragleave", () => td.classList.remove("landing"));

    td.addEventListener("drop", e => {
      if (!takes()) return;
      e.preventDefault();
      e.stopPropagation();               // the block around it would call this a reorder

      const moving = lifted;
      lifted = null;
      detach(moving);
      loosen(moving);
      putInCell(row, at, moving);
      fitToCell(moving, td._table, at);
      current = null;
      draw();
    });
  }

  /// Narrows a picture to the column it has landed in.
  ///
  /// A picture arrives capped at the width of the body's band, which is right when it
  /// goes into the flow and much too wide for a cell - a 160 mm photograph in a 60 mm
  /// column is one that overflows its own table. The height follows from the picture's
  /// proportions, the same rule the corner grip obeys, so nothing is squashed.
  function fitToCell(item, table, at) {
    if (!item || item.$type !== "ImageItem" || !table) return;

    const columns = (table.Columns || []).length || 1;
    const stated = Number((table.Columns || [])[at]?.Width);
    // A column with no width of its own takes an even share, which is what the engine
    // does with it as well.
    const room = Number.isFinite(stated) && stated > 0 ? stated : bandWidth() / columns;

    const wide = Number(item.Style.Location.WidthValue);
    if (!Number.isFinite(wide) || wide > room) item.Style.Location.WidthValue = room;
  }

  /// Whether a cell can take what is being dragged.
  ///
  /// It used to be a picture, and only into an empty cell - a cell holds one item, so
  /// anywhere else the drop would have been a deletion wearing the clothes of a move.
  /// That refusal was the right one for the shape at the time and the wrong shape: a cell
  /// holds one *item*, and an item can be a stack. So an occupied cell now keeps what it
  /// has and gains what arrives, one under the other.
  function canDropInCell(moving, cell) {
    if (!moving || !cell || moving === cell) return false;
    if (!canDraw(moving) && !isStack(moving)) return false;
    if (cell === moving) return false;

    // Never into itself: a stack dropped on a cell it already lives in.
    if (isStack(cell) && (cell.Childs || []).includes(moving)) return false;

    return isEmptyText(cell) || canDraw(cell) || isStack(cell);
  }

  /// A container drawn as one block above another. What a cell becomes when it is asked
  /// to hold two things, and what a pair is not - a pair is the absolute one.
  function isStack(item) {
    return !!item
        && item.$type === "ContainerItem"
        && item.Layout !== "Absolute";
  }

  /// A paragraph with nothing typed in it. What a fresh cell is, and the one thing worth
  /// replacing outright rather than stacking under.
  function isEmptyText(item) {
    return !!item
        && item.$type === "AdvancedTextItem"
        && (item.Runs || []).every(r => !r.Text || !r.Text.trim());
  }

  /// Puts an item into a cell, keeping whatever was there.
  ///
  /// An empty paragraph is replaced, since stacking under nothing would leave a blank
  /// line above everything. Anything else is stacked, and a cell that is already a stack
  /// simply gains one more.
  function putInCell(row, at, item) {
    const held = row.Childs[at];

    if (isEmptyText(held) || held === undefined) { row.Childs[at] = item; return; }

    if (isStack(held)) { held.Childs.push(item); return; }

    const stack = newItem("ContainerItem");
    stack.Layout = "Stack";
    stack.Childs = [held, item];
    row.Childs[at] = stack;
  }

  /// One rail of handles: a "+" per column or row, and a "×" to take it away. In the
  /// margin rather than in the toolbar, because which column is a question about a
  /// place, and a toolbar has no way to point at one.
  function rail(which, count, item) {
    const bar = document.createElement("div");
    bar.className = "rail " + which;

    for (let i = 0; i < count; i++) {
      const remove = document.createElement("button");
      remove.textContent = "\u00d7";
      remove.title = t(which === "columns" ? "editor.dropColumn" : "editor.dropRow");
      remove.onclick = e => {
        e.stopPropagation();
        if (which === "columns") dropColumn(item, i);
        else dropRow(item, i);
      };
      bar.appendChild(remove);
    }

    const add = document.createElement("button");
    add.textContent = "+";
    add.title = t(which === "columns" ? "editor.addColumn" : "editor.addRow");
    add.onclick = e => {
      e.stopPropagation();
      if (which === "columns") addColumn(item);
      else addRow(item);
    };
    bar.appendChild(add);
    return bar;
  }

  function emptyCell() {
    const cell = newItem("AdvancedTextItem");
    cell.Runs = [{ Text: "", Font: null }];
    return cell;
  }

  function addColumn(item) {
    (item.Columns ??= []).push({ Width: "NaN", WidthUnit: "Millimeter" });
    for (const row of item.Childs || [])
      if (row.$type === "TableRow") (row.Childs ??= []).push(emptyCell());
    current = null;
    draw();
  }

  function dropColumn(item, at) {
    if ((item.Columns || []).length <= 1) return;
    item.Columns.splice(at, 1);
    for (const row of item.Childs || [])
      if (row.$type === "TableRow" && (row.Childs || []).length > at) row.Childs.splice(at, 1);
    current = null;
    draw();
  }

  function addRow(item) {
    const row = newItem("TableRow");
    row.IsHeader = false;
    row.Childs = Array.from({ length: (item.Columns || []).length || 1 }, emptyCell);
    (item.Childs ??= []).push(row);
    current = null;
    draw();
  }

  function dropRow(item, at) {
    if ((item.Childs || []).length <= 1) return;
    item.Childs.splice(at, 1);
    current = null;
    draw();
  }

  /// A table to start from: a heading row and one row under it, two columns. Small
  /// enough to be a beginning rather than something to cut down.
  function newTable() {
    const item = newItem("TableItem");
    item.Columns = [{ Width: "NaN", WidthUnit: "Millimeter" },
                    { Width: "NaN", WidthUnit: "Millimeter" }];

    const head = newItem("TableRow");
    head.IsHeader = true;
    head.Childs = [emptyCell(), emptyCell()];

    const body = newItem("TableRow");
    body.IsHeader = false;
    body.Childs = [emptyCell(), emptyCell()];

    item.Childs = [head, body];
    return item;
  }

  /// The kinds this face draws itself, rather than naming as foreign.
  ///
  /// The list is here once. Widening a pair, or letting a cell hold a stack, is then a
  /// question of geometry and not of which types are allowed where - and adding a kind
  /// to fillBlock is one edit rather than four.
  const DRAWABLE = ["AdvancedTextItem", "ImageItem", "TableItem", "LineItem"];

  function canDraw(item) {
    return !!item && DRAWABLE.includes(item.$type);
  }

  /// A container this face knows how to draw side by side: two things it can draw, laid
  /// out absolutely. It used to be exactly one picture and exactly one paragraph, which
  /// was the narrowest thing that worked and turned out to be narrower than the engine -
  /// a table at a coordinate with a width lays out perfectly well beside a paragraph.
  function isPair(item) {
    return item.$type === "ContainerItem"
        && item.Layout === "Absolute"
        && (item.Childs || []).length === 2
        && item.Childs.every(canDraw);
  }

  /// How wide a block is on its own account, in millimetres, or null when it has no
  /// opinion and simply takes what it is given.
  ///
  /// The distinction decides what a pairing may do to it. A picture has no size of its
  /// own in the document - the width is a choice - so it is scaled to fit. A table's
  /// columns are stated in millimetres and do not stretch: the engine draws the frame at
  /// whatever width it is told and leaves the cells where the columns put them, so a
  /// table squeezed into half a band comes out as a wide frame around a narrow grid.
  /// Measured, not assumed: a table of 30 + 30 given a width of 85 renders 0..85 with
  /// its cells still ending at 60.
  function naturalWidth(item) {
    if (!item) return null;

    if (item.$type === "ImageItem") {
      const stated = Number(((item.Style || {}).Location || {}).WidthValue);
      return Number.isFinite(stated) && stated > 0 ? stated : null;
    }

    if (item.$type === "TableItem") {
      const columns = item.Columns || [];
      if (!columns.length) return null;
      let total = 0;
      for (const column of columns) {
        const width = Number(column.Width);
        if (!Number.isFinite(width) || width <= 0) return null;   // an even share: no opinion
        total += width;
      }
      return total;
    }

    return null;                       // a paragraph and a rule take what is left
  }

  /// The narrowest column worth putting words in. Below this a side-by-side is not an
  /// arrangement, it is a block with a stripe beside it.
  const MIN_COLUMN = 20;

  /// How wide the band is, in millimetres: the sheet less the body's padding. What a
  /// picture may not exceed, and what a width is measured against.
  // ---------------------------------------------------------------------------
  // Typefaces.
  //
  // Two lists, because there are two kinds of document and they want opposite things.
  //
  // A PDF embeds its fonts, so the only families that mean anything are the ones the
  // engine has a file for - and a family it has no file for is *refused*, never quietly
  // swapped, since another face would relay out every line. That list can only come
  // from the engine, and the preview server already publishes it at /fonts.
  //
  // An email embeds nothing: whoever opens it supplies the face, so naming Georgia is
  // reasonable and naming a family only this machine has is not. Perfection is not on
  // offer there and is not wanted.
  //
  // So it is an option rather than a merged list with warnings beside half of it: say
  // which kind of document is being written, and the choices follow.
  // ---------------------------------------------------------------------------
  const WEB_SAFE = ["Arial", "Helvetica", "Georgia", "Times New Roman",
                    "Verdana", "Tahoma", "Trebuchet MS", "Courier New"];

  /// The two answers, and the only list of them - so a stored name that is no longer
  /// one of them cannot be handed on. The same guard `themeOrAuto` gives the palette,
  /// and for the same reason: a preference outlives the option that set it.
  const FONT_SOURCES = ["library", "websafe"];
  const fontsOr = (name, fallback) => FONT_SOURCES.includes(name) ? name : fallback;

  /// Where the family list comes from. "library" asks the preview server what it can
  /// really embed; "websafe" is the handful every reader already has, which is what an
  /// email wants and what a page with no server of its own can honestly offer.
  ///
  /// The plain page keeps its stored preference, because it HAS the control that sets it.
  /// A face without that control does not - see chooseFontSource below.
  let fontSource = "library";
  try { fontSource = fontsOr(localStorage.getItem("frisket.fonts"), "library"); } catch {}

  /// What this face should be asking, given what it shows and what it was told.
  ///
  /// A declared source wins - a host that does have a font server says so. Otherwise a
  /// face with no font control falls to the web-safe families, and NOT to whatever is in
  /// localStorage: that preference was set on the full page, by somebody who could see
  /// the control, and letting it decide here means an embedded editor silently asking a
  /// server that is not there, failing, and offering a poorer list for it. It is the
  /// theme trap again - a stored name deciding in a face that cannot show it.
  /// Which room this face should be in, given what it shows and what it was told.
  ///
  /// The same shape as chooseFontSource below, and for the same reason - which is the
  /// third time this session that a preference stored by the face WITH a control decided
  /// inside a face without one.
  ///
  /// Found in Chrome's inspector, and it is not a Chrome fault: a host page painting the
  /// editor in its own dark colours got `data-theme="light"` stamped on the tag, because
  /// somebody had once picked Light on the standalone page and both faces share one
  /// localStorage key. The host sets seven of the twenty-four palette names; the other
  /// seventeen then came from the LIGHT theme inside the shadow root, so `--panel` was the
  /// host's near-black and `--control` was white. Two half-palettes composited, and the
  /// select came out white on a dark toolbar.
  ///
  /// `auto` is the answer for a face that cannot show the choice: it stamps nothing, so
  /// the base palette applies and every one of the twenty-four is consistent. A host with
  /// an opinion writes theme="light" or theme="graphite" - or, better, sets the custom
  /// properties, which is what the palette is for.
  /// How much white the document starts with round its body, in millimetres.
  ///
  /// The full page keeps twenty, which is a letter's margin and what somebody laying out
  /// a printed page expects. A face with no control for it does not: twenty millimetres
  /// of paper inside a host application is twenty millimetres of that application's
  /// screen given to a margin nobody can change and few will print.
  ///
  /// Same rule as the font source and the room, and the third of its kind - a default
  /// that belongs to the face that can show the control does not follow into the face
  /// that cannot. What an embedded editor produces is content, and where it sits on a
  /// page is the host's decision, not this editor's.
  ///
  /// Two rather than zero, because zero is not a margin but the absence of one: a
  /// paragraph whose first letter touches the edge of the sheet reads as a fault, and the
  /// handles in the gutter would have nothing to sit beside.
  function chooseMargins(declared) {
    // Nothing asked is not zero asked, and Number() disagrees: Number(null) and Number("")
    // are both 0, which is finite and in range - so "you decide" came back as "touch the
    // edge of the sheet". Caught by the check written for the other half of this function.
    const nothing = declared === null || declared === undefined || declared === "";
    const asked = nothing ? NaN : Number(declared);
    if (Number.isFinite(asked) && asked >= 0 && asked <= 100) return asked;
    return SHOWN.panel ? 20 : 2;
  }

  function chooseTheme(declared) {
    if (declared) return themeOrAuto(declared);
    return SHOWN.chrome ? theme : "auto";
  }

  function chooseFontSource(declared) {
    if (declared) return fontsOr(declared, "websafe");
    return SHOWN.panel ? fontSource : "websafe";
  }

  let libraryFamilies = null;          // null until the server has been asked
  let libraryDefault = null;           // the family the server's engine gives unnamed text

  /// What text that names no family is shown in: the face the output will give it. With
  /// the server's list that is the engine's default, which its font directory names;
  /// with the web's own families it is the stack a mail reader is handed. A document
  /// naming its own default is shown in that, as the engine would print it.
  ///
  /// This editor's own, not the core's page-wide one: two editors on a page may be showing
  /// two outputs, one the server's fonts and one the web's, and a single face would let
  /// whichever asked last decide for both.
  let unnamedHere = "sans-serif";

  function applyDefaultFace() {
    unnamedHere = fontSource === "websafe" ? MAIL_FACE
                : libraryDefault ? faceOf(libraryDefault) : "sans-serif";
    wire("sheet", n => { n.style.fontFamily = sheetFace(); });
  }

  function sheetFace() {
    const named = report && report.DefaultFont && report.DefaultFont.Family;
    return named ? faceOf(named) : unnamedHere;
  }

  /// Every family the document already names. What the list falls back to with no
  /// server: it cannot be wrong, since these are in use whatever the engine holds.
  function familiesInUse() {
    const seen = new Set();
    const walk = node => {
      if (!node || typeof node !== "object") return;
      if (node.Family) seen.add(node.Family);
      for (const value of Object.values(node)) {
        if (value && typeof value === "object") walk(value);
      }
    };
    walk(report);
    return [...seen].filter(Boolean).sort();
  }

  function fillFamilies() {
    const select = el("family");
    const chosen = select.value;
    const empty = select.selectedIndex < 0;
    select.innerHTML = "";

    const first = document.createElement("option");
    first.value = "";
    first.textContent = t("fonts.default");
    select.appendChild(first);

    const list = fontSource === "websafe" ? WEB_SAFE
               : libraryFamilies !== null ? libraryFamilies
               : familiesInUse();

    for (const family of list) {
      const option = document.createElement("option");
      option.value = family;
      option.textContent = family;
      option.style.fontFamily = family;
      select.appendChild(option);
    }
    // Nothing selected stays nothing selected. Rebuilding the options throws the
    // selection away, and restoring `chosen` - which reads "" when the list was showing
    // nothing - would put "Document default" on a list that is deliberately blank
    // because the selection spans two families. The rebuild happens when the server's
    // font list arrives, a second or so after the page opens, so it lands squarely on
    // whatever the toolbar had decided by then.
    if (empty) select.selectedIndex = -1;
    else select.value = chosen || "";

    wire("fontHint", n => { n.textContent =
      fontSource === "websafe" ? t("fonts.approximate")
      : libraryFamilies === null ? t("fonts.offline") : ""; });
  }

  /// Asks the preview server what it can actually embed. Best effort: the page opens
  /// from a file:// URL as often as not, and saying so is better than an empty list.
  function askServerForFonts() {
    const offline = () => { libraryFamilies = null; libraryDefault = null; fillFamilies(); applyDefaultFace(); };
    if (server === null) { offline(); return; }
    serverFonts(server, send)
      .then(receiveFonts)
      .catch(offline);
  }

  /// What the server answered: the families to offer, the one it gives unnamed text, and
  /// the files themselves to draw with.
  function receiveFonts(list) {
    const got = fontsFromServer(list, server ?? "");
    libraryFamilies = got.families;
    libraryDefault = got.defaultFamily;
    loadFaces(server ?? "", got.faces);
    fillFamilies();
    applyDefaultFace();
  }


  function bandWidth() {
    const section = report.Sections[0];
    const pad = (body() && body().Style && body().Style.Padding) || {};
    return Math.max(10, (section.Dimension.Width || 210) - (pad.Left ?? 20) - (pad.Right ?? 20));
  }

  /// The corner grip. Dragging it sets the picture's width and nothing else: the height
  /// follows from the picture's own proportions, which keeps a photograph from being
  /// squashed by a careless drag.
  function gripFor(item, node) {
    const grip = document.createElement("div");
    grip.className = "grip";
    grip.title = t("editor.resize");

    const place = () => {
      const img = node.querySelector("img");
      if (!img) return;
      const box = img.getBoundingClientRect(), host = node.getBoundingClientRect();
      grip.style.left = (box.right - host.left - 7) + "px";
      grip.style.top = (box.bottom - host.top - 7) + "px";
    };

    grip.addEventListener("mousedown", down => {
      down.preventDefault();
      node.classList.add("resizing");

      const img = node.querySelector("img");
      const startX = down.clientX;
      const startPx = img.getBoundingClientRect().width;
      const perMm = startPx / Math.max(1, Number(item.Style.Location.WidthValue) || bandWidth());

      const move = at => {
        const mm = (startPx + (at.clientX - startX)) / perMm;
        item.Style.Location.WidthValue = Math.round(Math.max(5, Math.min(bandWidth(), mm)) * 10) / 10;
        img.style.width = item.Style.Location.WidthValue + "mm";
        place();
      };
      const up = () => {
        document.removeEventListener("mousemove", move);
        document.removeEventListener("mouseup", up);
        node.classList.remove("resizing");
        runChecks();
      };
      document.addEventListener("mousemove", move);
      document.addEventListener("mouseup", up);
    });

    requestAnimationFrame(place);
    return grip;
  }

  // ---------------------------------------------------------------------------
  // Dragging a block, to move it or to set it beside another.
  //
  // The browser's own drag is what duplicated a picture: dropped into an editable
  // element it inserts a copy of the HTML, and the model gains nothing. So the two are
  // separated rather than the whole thing forbidden - a foreign drop is refused, and a
  // drag that started on one of our own blocks is ours to place.
  //
  // Four sides, not two. Above and below reorder the body, which is what a flow is.
  // Left and right mean something else: the two blocks become one container, laid out
  // absolutely, the picture at a coordinate and the paragraph after it. Dock alone
  // would not do it - ArrangeChildren hands every child the same rectangle, so a
  // docked picture and a stretched paragraph would sit on top of one another - which
  // is why the coordinates are worked out here.
  //
  // The index travels in a variable rather than only in dataTransfer, which is empty
  // during dragover in several browsers - exactly when the decision has to be made.
  // ---------------------------------------------------------------------------
  let lifted = null;

  /// Which side of a block the pointer is nearest. The outer fifth either way is a
  /// side; anything else is above or below. A side band reaching to the middle would
  /// make "drop it after this paragraph" nearly unhittable.
  function dropZone(node, event, sides = true) {
    const box = node.getBoundingClientRect();
    const x = (event.clientX - box.left) / Math.max(1, box.width);
    if (sides && x < 0.2) return "left";
    if (sides && x > 0.8) return "right";
    return event.clientY > box.top + box.height / 2 ? "below" : "above";
  }

  /// Whether putting one block beside another means anything.
  ///
  /// This face draws exactly one picture beside exactly one paragraph, and pairUp
  /// refuses everything else - silently, which is the worst way to refuse. So the zone
  /// is not lit in the first place, and the pointer falls back to above and below.
  ///
  /// A pair is named by the block a drop lands on, so half of one dropped on the other
  /// side of its own pair names the pair rather than its sibling: that is asked about
  /// here and answered in pairUp, or swapping sides would be the one arrangement the
  /// gesture could not reach.
  function canPair(moving, target) {
    if (!moving || !target || moving === target) return false;

    const other = pairPartner(moving, target);
    if (!other || other === moving) return false;
    if (!canDraw(moving) || !canDraw(other)) return false;

    // Refused here rather than drawn badly. A table keeps the width its columns state,
    // so two wide tables cannot both be a column - and saying so by not lighting the
    // zone is the rule this face already follows: never offer a drop it will decline.
    return sideBySideWidth(moving, other) !== null;
  }

  /// How wide the left-hand side would be, or null when the two do not fit beside one
  /// another at all.
  ///
  /// One side is fixed and the other takes the rest. A picture is scaled to fit, since
  /// its width is a choice; a table is not, since its columns are stated and do not
  /// stretch. Two things that both take what they are given - two paragraphs - split the
  /// band evenly, which is what a person dropping one beside the other plainly meant.
  function sideBySideWidth(left, right) {
    const band = bandWidth();
    const gap = 4;
    const room = band - gap;

    const fixedLeft = naturalWidth(left);
    const fixedRight = naturalWidth(right);

    let width;
    if (fixedLeft != null) width = left.$type === "ImageItem"
                                    ? Math.min(fixedLeft, room - MIN_COLUMN)
                                    : fixedLeft;
    else if (fixedRight != null) width = right.$type === "ImageItem"
                                    ? room - Math.min(fixedRight, room - MIN_COLUMN)
                                    : room - fixedRight;
    else width = room / 2;

    if (!Number.isFinite(width)) return null;
    if (width < MIN_COLUMN || room - width < MIN_COLUMN) return null;

    return Math.round(width * 10) / 10;
  }

  /// What a block is really being put beside. Usually the target itself - but a drop
  /// lands on a whole pair, so half of one dropped on its own pair means the other half.
  function pairPartner(moving, target) {
    return isPair(target) && (target.Childs || []).includes(moving)
         ? target.Childs.find(c => c !== moving)
         : target;
  }

  /// Draws the arrangement a drop would make: two halves for a side, a band across the
  /// top or bottom for a move. Positioned rather than classed, so one pair of elements
  /// serves all four answers and there is no state to keep in sync.
  function showZone(node, zone) {
    let zones = node.querySelector(".zones");
    if (!zones) {
      zones = document.createElement("div");
      zones.className = "zones";
      zones.appendChild(Object.assign(document.createElement("div"), { className: "zone taken" }));
      zones.appendChild(Object.assign(document.createElement("div"), { className: "zone" }));
      node.appendChild(zones);
    }

    const [taken, rest] = zones.querySelectorAll(".zone");
    node.classList.add("landing");

    // Left and right pair the two into one container, so both halves are drawn: this is
    // the arrangement, not a position. Above and below reorder the body, so the band is
    // where the block would sit and the rest is the target staying where it is.
    const place = (el, top, right, bottom, left) => {
      el.style.top = top; el.style.right = right;
      el.style.bottom = bottom; el.style.left = left;
      el.style.display = "block";
    };

    if (zone === "left")       { place(taken, "0", "52%", "0", "0");   place(rest, "0", "0", "0", "52%"); }
    else if (zone === "right") { place(taken, "0", "0", "0", "52%");   place(rest, "0", "52%", "0", "0"); }
    else if (zone === "above") { place(taken, "0", "0", "62%", "0");   place(rest, "42%", "0", "0", "0"); }
    else                       { place(taken, "62%", "0", "0", "0");   place(rest, "0", "0", "42%", "0"); }
  }

  function hideZone(node) {
    node.classList.remove("landing");
  }

  function dragBehaviour(node, item) {
    liftBy(node, item);

    node.addEventListener("dragover", e => {
      if (!lifted || lifted === item) return;            // not ours, or onto itself
      e.preventDefault();
      e.dataTransfer.dropEffect = "move";
      showZone(node, dropZone(node, e, canPair(lifted, item)));
    });

    node.addEventListener("dragleave", () => hideZone(node));

    node.addEventListener("drop", e => {
      if (!lifted || lifted === item) return;            // a foreign drop is not taken
      e.preventDefault();

      const zone = dropZone(node, e, canPair(lifted, item));
      const moving = lifted;
      lifted = null;

      if (zone === "left" || zone === "right") { pairUp(moving, item, zone); return; }

      // Out of wherever it was first - which may dissolve the pair it was half of, and
      // may therefore be the pair we are dropping onto. That is why the target is looked
      // up again afterwards rather than measured beforehand.
      const was = detach(moving);
      const kids = body().Childs;
      let to = kids.indexOf(item);
      if (to < 0) to = Math.max(0, Math.min(was, kids.length));
      if (zone === "below") to++;

      loosen(moving);
      kids.splice(Math.max(0, Math.min(to, kids.length)), 0, moving);
      current = null;
      draw();
      focusBlock(body().Childs.indexOf(moving));
    });
  }

  /// The one thing a block - or half of one - is dragged by.
  ///
  /// Its own element, never the block and never something inside it. A block is often a
  /// contenteditable, where the browser would rather select text than start a drag; and
  /// half of a pair is a picture, where the gesture is ambiguous even to a person.
  function liftBy(node, item, what = "editor.moveBlock") {
    const grab = document.createElement("div");
    grab.className = "handle";
    grab.textContent = "\u2237";
    // What it is AND what this does. The kind used to float beside the text on hover,
    // which is a great deal of chrome for something you want to know once - and it sat in
    // the page's own margin, over the paper, where nothing else in this face draws.
    // Pointing at the handle is the moment the question is actually being asked.
    grab.title = labelOf(item.$type) + " \u2014 " + t(what);
    grab.draggable = true;
    node.appendChild(grab);

    grab.addEventListener("dragstart", e => {
      lifted = item;
      e.dataTransfer.effectAllowed = "move";
      e.dataTransfer.setData("text/plain", "");     // Firefox starts no drag without it
      node.classList.add("lifted");
    });

    grab.addEventListener("dragend", () => {
      lifted = null;
      el("sheet").querySelectorAll(".block, .cell").forEach(b => {
        b.classList.remove("landing", "lifted");
      });
    });
  }

  /// Takes an item out of wherever it lives and says where in the body that was.
  ///
  /// A pair that loses a child stops being a pair: the one left over takes its place in
  /// the body. That is what makes the gesture reversible - without it, dropping a
  /// picture beside a paragraph turned two blocks into one and there was no way back.
  /// Worse, with nothing else on the sheet there was then nothing left to drop onto,
  /// which reads as "drag and drop stopped working" rather than as a one-way door.
  function detach(item) {
    const kids = body().Childs;
    const at = kids.indexOf(item);
    if (at >= 0) { kids.splice(at, 1); return at; }

    for (let i = 0; i < kids.length; i++) {
      const pair = kids[i];
      if (!isPair(pair)) continue;
      const j = (pair.Childs || []).indexOf(item);
      if (j < 0) continue;

      pair.Childs.splice(j, 1);
      const alone = pair.Childs[0];
      loosen(alone);
      kids.splice(i, 1, alone);
      return i;
    }

    // And out of a table cell. What is left behind is an empty cell rather than a hole:
    // a row handing back fewer cells than the table has columns is refused outright by a
    // conformance checker and unreadable aloud (CLAUDE.md §11).
    for (let i = 0; i < kids.length; i++) {
      if (kids[i].$type !== "TableItem") continue;
      for (const row of kids[i].Childs || []) {
        const j = (row.Childs || []).indexOf(item);
        if (j >= 0) { row.Childs[j] = emptyCell(); return i; }

        // Or out of a stack inside one. A stack down to a single block stops being a
        // stack, the same way a pair down to one stops being a pair: the block left over
        // takes its place, or the tree keeps a wrapper nobody asked for.
        for (let c = 0; c < (row.Childs || []).length; c++) {
          const held = row.Childs[c];
          if (!isStack(held)) continue;

          const k = (held.Childs || []).indexOf(item);
          if (k < 0) continue;

          held.Childs.splice(k, 1);
          if (held.Childs.length === 1) row.Childs[c] = held.Childs[0];
          else if (held.Childs.length === 0) row.Childs[c] = emptyCell();
          return i;
        }
      }
    }

    // A stack loose in the body, for the same reason.
    for (let i = 0; i < kids.length; i++) {
      if (!isStack(kids[i])) continue;

      const j = (kids[i].Childs || []).indexOf(item);
      if (j < 0) continue;

      kids[i].Childs.splice(j, 1);
      if (kids[i].Childs.length === 1) { loosen(kids[i].Childs[0]); kids.splice(i, 1, kids[i].Childs[0]); }
      else if (kids[i].Childs.length === 0) kids.splice(i, 1);
      return i;
    }

    return kids.length;
  }

  /// Undoes what pairUp pinned down. A block back in the flow is placed by the flow, so
  /// it carries no coordinates - but it keeps its width, which is the author's and not
  /// something the pair gave it.
  function loosen(item) {
    const loc = ((item.Style ??= {}).Location ??= {});
    loc.LeftValue = "NaN";
    loc.RightValue = "NaN";
    loc.Dock = "None";
  }

  /// Puts one block beside another, as a container laid out absolutely.
  ///
  /// Anything this face can draw may go beside anything else it can draw - a picture, a
  /// paragraph, a table, a rule - because the engine has always been able to lay them out
  /// that way and only this face was narrower. What decides it is geometry rather than
  /// type: one side is fixed and the other takes the rest, and a pairing with no room
  /// left to read in is refused before the zone is ever lit.
  ///
  /// Wrapped on demand rather than always. Giving every paragraph a container of its own
  /// would double the depth of the tree and the size of the file so that a rare
  /// arrangement could be expressed - and every reader of the template, the designer's
  /// tree and the validator's path alike, would say Body > Group > AdvancedText for a
  /// plain paragraph.
  function pairUp(moving, target, side) {
    if (!moving || !target || moving === target) { draw(); return; }

    // Dropping half of a pair on the other side of its own pair is a swap, and what it
    // landed on is the pair, which is neither a picture nor a paragraph - so this used
    // to fall straight through the test below and do nothing at all. The only way to
    // move a picture from the right to the left was to take it out first, which is
    // exactly how it was reported.
    target = pairPartner(moving, target);
    if (moving === target) { draw(); return; }

    if (!canDraw(moving) || !canDraw(target)) { draw(); return; }

    // Which one ends up on the left. `side` says where the moving block was dropped.
    const leftItem = side === "left" ? moving : target;
    const rightItem = side === "left" ? target : moving;

    const width = sideBySideWidth(leftItem, rightItem);
    if (width === null) { draw(); return; }

    const gap = 4;
    const band = bandWidth();

    // A picture is resized to the column it was given; anything else keeps what it has,
    // because its width is stated rather than chosen. Writing a width onto a table would
    // widen its frame and leave its cells where the columns put them.
    if (leftItem.$type === "ImageItem") leftItem.Style.Location.WidthValue = width;
    if (rightItem.$type === "ImageItem")
      rightItem.Style.Location.WidthValue = Math.round((band - width - gap) * 10) / 10;

    leftItem.Style.Location.LeftValue = 0;
    leftItem.Style.Location.RightValue = "NaN";
    leftItem.Style.Location.Dock = "None";

    rightItem.Style.Location.LeftValue = Math.round((width + gap) * 10) / 10;
    rightItem.Style.Location.RightValue = "NaN";
    rightItem.Style.Location.Dock = "Right";

    const pair = newItem("ContainerItem");
    pair.Layout = "Absolute";
    pair.Childs = [leftItem, rightItem];

    // Both originals leave wherever they are - either may be half of another pair, and
    // taking one out dissolves that pair, which moves everything after it. So the
    // target's place is asked for after the moving one has gone, never before.
    detach(moving);
    const at = detach(target);
    const kids = body().Childs;
    kids.splice(Math.max(0, Math.min(at, kids.length)), 0, pair);

    current = null;
    draw();
    focusBlock(body().Childs.indexOf(pair));
  }

  /// Takes what is in the editable and puts it in the model. The input handler does
  /// this too; this is for the paths that change the text without typing it.
  function readSurface() {
    const item = currentItem();
    if (!item || item.$type !== "AdvancedTextItem") return;

    // Never `current` itself. Both the surface and the block around it call
    // markCurrent, the event bubbles, and the block is the one that runs last - so
    // `current` is the block and reading it would take the drag handle in along with
    // the paragraph. Where the text lives is asked for, not assumed.
    const surface = editableIn(current);
    if (!surface) return;

    const runs = surfaceToRuns(surface);
    item.Runs = runs.length ? runs : [{ Text: "", Font: null }];
    runChecks();
  }

  /// The editable inside a block, or the block's own if it is one. What the caret and
  /// the arrow keys are really about - the block around it is furniture.
  function editableIn(node) {
    if (!node) return null;
    if (node.isContentEditable) return node;
    return node.querySelector ? node.querySelector(".surface") : null;
  }

  function markCurrent(node) {
    if (current === node) return;
    pending = null;              // a style armed in one block is not owed to the next

    // AND NEITHER IS THE SELECTION. `cached` is a pair of offsets into the block it was
    // made in; carried into another block it addresses whatever happens to sit at those
    // numbers, and showActiveStyles - called on the line below - paints the toolbar from
    // it. Reported as the swatch flashing the colour of a run in the block you just left
    // before settling on the right answer a moment later, when selectionchange arrives
    // with the truth. Firefox never showed it; Chrome did.
    //
    // Null is not "no answer" here, it is "not yet": the lists fall to the document's
    // default and the swatch to nothing, which is what an unasked question looks like.
    cached = null;
    if (current) current.classList.remove("here");
    current = node;
    if (current) current.classList.add("here");
    showActiveStyles();
  }

  function currentItem() {
    return current ? current._item || null : null;
  }

  /// The top-level block the caret is in, whether it is that block or a cell inside a
  /// pair. What move and remove operate on: moving half a pair would mean taking a
  /// picture out of the paragraph it belongs beside.
  function currentBlock() {
    let node = current;
    while (node && node.dataset.index === undefined) node = node.parentNode;
    return node;
  }

  // ---------------------------------------------------------------------------
  // Typing gestures. Enter makes a new block rather than a line inside this one -
  // that is the whole difference between a block editor and a text box, and it is
  // what keeps a paragraph a paragraph in the model.
  // ---------------------------------------------------------------------------
  const onKeyDown = e => {
    if (!current) return;

    // Moving comes first, and on purpose: it is the one gesture that has to work on
    // every block. Guarding the whole handler on isContentEditable is what made a
    // picture unmovable - it is the only block that is not editable, and moving it is
    // exactly what somebody had just been refused a drag for.
    if (e.altKey && (e.key === "ArrowUp" || e.key === "ArrowDown")) {
      e.preventDefault();
      moveBlock(e.key === "ArrowUp" ? -1 : 1);
      return;
    }

    // Two different things, asked for by name.
    //
    // `current` is the block, or a cell of a pair - never the editable inside it: the
    // surface and the block both marked themselves and the block, running last, always
    // won. So `current.isContentEditable` was false for every paragraph in the
    // document, and the guard below - meant to let a picture past - was instead
    // swallowing Enter, Backspace and the arrow keys for everything else.
    // The same fault as the one this guard was written to fix, one split later.
    const caret = editableIn(current);
    const block = currentBlock();

    // A picture has no caret, so Delete is the only way to be rid of it from the
    // keyboard. Backspace below needs one and would do nothing here.
    if (!caret && (e.key === "Delete" || e.key === "Backspace")) {
      e.preventDefault();
      removeCurrent();
      return;
    }

    if (!caret) return;                       // everything below is about a caret

    // Enter breaks the line inside the paragraph; shift+Enter starts a new block.
    //
    // The other way round is the usual convention and it is wrong here: a block is a
    // paragraph of the document, and wanting a second line of the same paragraph is
    // far commoner than wanting a second paragraph. The rarer thing takes the modifier.
    if (e.key === "Enter") {
      e.preventDefault();
      if (e.shiftKey) { insertAfterCurrent(emptyParagraph()); return; }

      document.execCommand("insertLineBreak");

      // Read back at once rather than waiting for an input event. execCommand does not
      // reliably raise one, and a break the model never heard about is a break that is
      // on the page and not in the document: it looks right until the next redraw - the
      // next redraw being whenever a picture is inserted, which is exactly when somebody
      // reported the whole paragraph collapsing onto one line.
      readSurface();
      return;
    }

    // Up and down have to be carried across the boundary by hand. A browser moves a
    // caret inside one editable element and stops at its edge; with a contenteditable
    // per block - the decision this whole face rests on - the blocks are separate
    // elements, so nothing crosses between them unless this does it.
    //
    // It is not a cost of the architecture so much as its bill: the same separation
    // that keeps a picture from being a foreign body inside flowing text is what
    // makes two paragraphs two elements.
    if (e.key === "ArrowUp" || e.key === "ArrowDown") {
      const step = e.key === "ArrowUp" ? -1 : 1;
      if (!atEdge(caret, step)) return;               // still room to move inside this block
      if (!block) return;

      const kids = body().Childs;
      const next = Number(block.dataset.index) + step;
      if (next < 0 || next >= kids.length) return;    // the ends of the document stay put

      e.preventDefault();
      focusBlock(next, step < 0 ? "end" : "start");
      return;
    }

    // Backspace in an empty block removes it and puts the caret in the one before -
    // the gesture everyone already has in their fingers.
    // The surface, not the block: a block also carries a drag handle, whose text is
    // never empty, so asked of the block this was false in every paragraph there is.
    if (e.key === "Backspace" && caret.textContent.trim() === "" && block) {
      const kids = body().Childs;
      const at = Number(block.dataset.index);
      if (kids.length > 1) {
        e.preventDefault();
        kids.splice(at, 1);
        current = null;
        draw();
        focusBlock(Math.max(0, at - 1));
      }
    }
  };

  /// Moves the block being edited one place up or down, and keeps the caret in it.
  function moveBlock(step) {
    const block = currentBlock();
    if (!block) return;
    const kids = body().Childs;
    const at = Number(block.dataset.index);
    const to = at + step;
    if (to < 0 || to >= kids.length) return;

    const [item] = kids.splice(at, 1);
    kids.splice(to, 0, item);
    current = null;
    draw();
    focusBlock(to);
  }

  function insertAfterCurrent(item) {
    // Standing in an empty cell, a picture goes in the cell - not after the whole
    // table, which is what "after the current block" means for anything inside one.
    const cell = currentItem();
    if (current && current._row && canDropInCell(item, cell)) {
      putInCell(current._row, current._at, item);
      fitToCell(item, current._table, current._at);
      current = null;
      draw();
      return;
    }

    const kids = body().Childs;
    const block = currentBlock();
    const at = block ? Number(block.dataset.index) + 1 : kids.length;
    kids.splice(at, 0, item);
    current = null;
    draw();
    focusBlock(at);
  }

  // --- merge fields -----------------------------------------------------------------
  //
  // What the host will fill in when it prints: a recipient's name, a case number, an
  // amount, a date, a signature. The host offers them (`fields`), and a field goes in as
  // its token - `{Recipient.Name}` - which the engine has always bound from the data.
  //
  // The type is for the menu and nothing else: values arrive formatted by the host, the
  // engine stays neutral, and a document mixing euros, dollars and Pacific francs is the
  // host's to write - "le métier n'est pas là". The one type that changes what is
  // inserted is a picture, which is a block of its own filled from the data
  // (ImageItem.SourceField), not a token in a sentence.
  //
  // And a list - the lines of an invoice, the parties to a case - which is inserted as a
  // table repeated over it (RepeatOver): a header of its fields' labels, one row of their
  // tokens, stamped out by the engine once per entry. Its fields are the host's too, named
  // relative to an entry, and offered first in the menu while the caret is in that row.
  const FIELD_KINDS = ["text", "number", "decimal", "currency", "date", "datetime", "list", "image"];
  let fieldSource = seams.fields || null;
  let fields = [];

  /// A catalogue as a host may hand it over: entries with a path, a label, a kind and a
  /// sample - a path alone will do, braces or not - and anything else left out.
  function fieldsFrom(list) {
    return (Array.isArray(list) ? list : []).map(entry => {
      const raw = typeof entry === "string" ? entry : (entry && entry.path) || "";
      const path = String(raw).trim().replace(/^\{\s*|\s*\}$/g, "").trim();
      const said = entry && typeof entry === "object" ? entry : {};
      const kind = String(said.type || "text").toLowerCase();
      const type = FIELD_KINDS.includes(kind) ? kind : "text";
      return {
        path,
        label: String(said.label || path),
        type,
        sample: said.sample == null ? "" : String(said.sample),
        // A list's own fields, one level down: a list inside a list would be a table in a
        // cell, which this face does not draw.
        fields: type === "list" ? fieldsFrom(said.fields).filter(child => child.type !== "list") : [],
      };
    }).filter(field => field.path);
  }

  /// The list the caret's row repeats over, when it is in a repeated row of a table whose
  /// list the host described - null anywhere else, the header row included, since a header
  /// is printed once and a token there would read the whole data, not an entry.
  function listHere() {
    const table = current && current._table;
    const row = current && current._row;
    const over = table && row && !row.IsHeader ? (table.RepeatOver || "").trim() : "";
    return over ? fields.find(field => field.type === "list" && field.path === over) || null : null;
  }

  /// Asks for the catalogue - a list, a function returning one, or a promise of either -
  /// and shows the button once there is something in it.
  function loadFields() {
    // Said rather than left to the markup's `hidden`: the button shows the catalogue in
    // hand - none at first - until the one asked for arrives.
    wire("fieldsTool", n => { n.hidden = fields.length === 0; });
    const asked = fieldSource;
    const take = list => {
      if (fieldSource !== asked) return;
      fields = fieldsFrom(list);
      wire("fieldsTool", n => { n.hidden = fields.length === 0; });
      draw();                // a picture from the data may have a sample to show now
    };
    let answer;
    try { answer = typeof asked === "function" ? asked() : asked; } catch { answer = []; }
    if (answer && typeof answer.then === "function") answer.then(take, () => take([]));
    else take(answer);
  }

  function fieldAt(path) {
    return fields.find(field => field.path === path) || null;
  }

  /// The menu: grouped by kind, each entry its label over its path and, when the host gave
  /// one, the sample it stands for - which is what tells two similar labels apart.
  function fillFieldMenu() {
    const menu = el("fieldMenu");
    if (!menu) return;
    menu.innerHTML = "";

    // In a repeated row, what that row can say first: its list's fields, by their names
    // within an entry. They are what belongs there; the rest still follows.
    const here = listHere();
    const groups = [];
    if (here && here.fields.length)
      groups.push({ heading: t("fields.eachLine", { list: here.label }), entries: here.fields });
    for (const kind of FIELD_KINDS)
      groups.push({ heading: t("fieldType." + kind), kind, entries: fields.filter(field => field.type === kind) });

    for (const group of groups) {
      if (!group.entries.length) continue;
      const kind = group.kind;
      const heading = document.createElement("div");
      heading.className = "kind";
      heading.textContent = group.heading;
      menu.appendChild(heading);
      for (const field of group.entries) {
        const button = document.createElement("button");
        const label = document.createElement("span");
        label.textContent = field.label;
        const detail = document.createElement("small");
        detail.textContent = "{" + field.path + "}"
          + (field.type === "list" ? " - " + field.fields.map(child => child.label).join(", ")
             : field.sample && field.type !== "image" ? " - " + field.sample : "");
        button.appendChild(label);
        button.appendChild(detail);
        // Pressed without taking the focus, so the caret is still where it goes.
        button.addEventListener("mousedown", e => e.preventDefault());
        button.onclick = e => { e.stopPropagation(); closeFields(); insertField(field); };
        menu.appendChild(button);
      }
    }
  }

  function closeFields() { const menu = el("fieldMenu"); if (menu) menu.hidden = true; }

  /// Puts a field where the caret is. Typed in the way a paste is - execCommand, so the
  /// token lands in the run the caret is in, takes that run's style, and is undone by the
  /// same Ctrl+Z as a word typed by hand - and read back at once, since execCommand does
  /// not reliably raise the input event (the lesson the line break paid for).
  ///
  /// With no paragraph under the caret it starts one; a picture is a block of its own.
  function insertField(field) {
    if (field.type === "image") { insertAfterCurrent(pictureField(field)); return; }
    if (field.type === "list") { insertAfterCurrent(listTable(field)); return; }

    const token = "{" + field.path + "}";
    const item = currentItem();
    const surface = editableIn(current);
    if (!item || item.$type !== "AdvancedTextItem" || !surface) {
      const paragraph = emptyParagraph();
      paragraph.Runs = [{ Text: token, Font: null }];
      insertAfterCurrent(paragraph);
      return;
    }

    // Where the caret was when the menu opened: `cached` kept it, the menu kept the focus
    // off, and putting it back costs nothing when it never moved.
    const end = surface.textContent.length;
    const at = cached || { start: end, end };
    surface.focus();
    const sel = selection();
    sel.removeAllRanges();
    sel.addRange(rangeAt(surface, at.start, at.end));
    document.execCommand("insertText", false, token);
    readSurface();
  }

  /// A table repeated over a list: a column per field of the list, its label in the header
  /// row and its token in the one row the engine stamps out per entry. A list the host gave
  /// no fields for still makes a table - one column, headed by the list, waiting for tokens.
  function listTable(field) {
    const table = newTable();
    const columns = field.fields.length ? field.fields : [{ path: "", label: field.label, type: "text" }];
    const text = words => { const cell = emptyCell(); cell.Runs = [{ Text: words, Font: null }]; return cell; };

    table.Columns = columns.map(() => ({ Width: "NaN", WidthUnit: "Millimeter" }));
    const [head, line] = table.Childs;
    head.Childs = columns.map(column => text(column.label));
    line.Childs = columns.map(column => {
      if (column.type !== "image") return text(column.path ? "{" + column.path + "}" : "");
      const picture = pictureField(column);
      picture.Style.Location.WidthValue = Math.min(40, bandWidth() / columns.length);
      return picture;
    });
    table.RepeatOver = field.path;
    return table;
  }

  /// A picture the data fills in: no bytes of its own, the field's path to find them by,
  /// and its label as what a screen reader says - a description the person can change, and
  /// better than the nothing PDF/UA refuses.
  function pictureField(field) {
    const item = newItem("ImageItem");
    item.SourceField = field.path;
    item.AltText = field.label;
    item.Style.Location.WidthValue = Math.min(40, bandWidth());
    return item;
  }

  /// The frame shown for a picture from the data with no sample: dashed, in the paper's
  /// placeholder colours - the ones the designer draws an empty picture in - and saying
  /// where the picture will come from.
  function pictureFromData(path, item) {
    const w = 400;
    const h = Math.max(60, Math.round(w * (Number(item.NaturalHeight) || 20) / (Number(item.NaturalWidth) || 40)));
    const words = t("editor.fromData", { path });
    const size = Math.max(10, Math.min(24, Math.floor(700 / Math.max(1, words.length))));
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${h}">`
      + `<rect x="2" y="2" width="${w - 4}" height="${h - 4}" fill="${ON_PAPER.placeholderFill}"`
      + ` stroke="${ON_PAPER.placeholderLine}" stroke-width="3" stroke-dasharray="12 8"/>`
      + `<text x="${w / 2}" y="${h / 2}" text-anchor="middle" dominant-baseline="middle"`
      + ` font-family="sans-serif" font-size="${size}" fill="${ON_PAPER.placeholderInk}">${esc(words)}</text></svg>`;
    return "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg);
  }

  /// Puts the caret in a block. Coming from above it lands at the start and from
  /// below at the end, so arrowing through the document reads as one continuous move
  /// rather than jumping to the same corner of every block.
  function focusBlock(index, where = "end") {
    const block = el("sheet").querySelector(`.block[data-index="${index}"]`);
    if (!block) return;

    markCurrent(block);

    const node = editableIn(block);
    if (!node) { block.focus(); return; }

    node.focus();
    const range = document.createRange();
    range.selectNodeContents(node);
    range.collapse(where === "start");
    const sel = selection();
    sel.removeAllRanges();
    sel.addRange(range);
  }

  /// Whether the caret has nowhere left to go inside this block in that direction.
  /// Measured by where the caret is drawn rather than by counting characters, because
  /// a wrapped paragraph has several visual lines and only the first and last of them
  /// are edges - which is what makes the arrow feel right in a long paragraph.
  function atEdge(node, step) {
    const sel = selection();
    if (!sel || sel.rangeCount === 0) return true;

    const caret = sel.getRangeAt(0).cloneRange();
    caret.collapse(true);
    const at = caret.getClientRects()[0] || node.getBoundingClientRect();
    const box = node.getBoundingClientRect();

    // Half a line of tolerance: the caret's own rectangle is the height of the line
    // it sits on, so comparing edges exactly would never match.
    const slack = at.height ? at.height / 2 : 4;
    return step < 0 ? at.top - box.top <= slack : box.bottom - at.bottom <= slack;
  }

  // ---------------------------------------------------------------------------
  // The style toolbar. The selection is cached the moment it changes, because
  // focusing any control clears it - the lesson the designer's dialog paid for:
  // never re-read the live selection at the moment a control fires.
  // ---------------------------------------------------------------------------
  let cached = null;

  const onSelectionChange = () => {
    const sel = selection();
    // The surface, asked for. `current` is the block, which is not editable - so this
    // gave up on every selection there has ever been since the text moved into a
    // surface of its own, and the whole toolbar went quiet with it: bold, italic,
    // colour and size all read `cached`, and nothing ever set it.
    const caret = editableIn(current);
    if (!sel || sel.rangeCount === 0 || !caret) return;
    const range = sel.getRangeAt(0);
    if (!caret.contains(range.commonAncestorContainer)) return;
    cached = offsetsIn(caret, range);
    showActiveStyles();
  };

  /// The selection as offsets into the block's flat text. A Range speaks in nodes and
  /// offsets within them; runs are a flat string. This converts one to the other.
  function offsetsIn(host, range) {
    const before = document.createRange();
    before.selectNodeContents(host);
    before.setEnd(range.startContainer, range.startOffset);
    const start = before.toString().length;
    return { start, end: start + range.toString().length };
  }

  /// The inverse of `offsetsIn`: a range over the flat character offsets of a host's
  /// text, so a selection can be put back where it was once the sheet has been rebuilt.
  ///
  /// `document.createRange` and not `uiRoot.createRange`: a ShadowRoot has no such
  /// method, which is the seam §5c-quater is about, and `focusBlock` already asks the
  /// document for the same reason.
  function rangeAt(host, start, end) {
    const range = document.createRange();
    range.selectNodeContents(host);

    const walker = document.createTreeWalker(host, NodeFilter.SHOW_TEXT);
    let at = 0, node, opened = false;

    while ((node = walker.nextNode())) {
      const to = at + node.textContent.length;
      if (!opened && start <= to) { range.setStart(node, start - at); opened = true; }
      if (opened && end <= to) { range.setEnd(node, end - at); return range; }
      at = to;
    }
    return range;              // past the end: the whole of it, which is the honest answer
  }

  /// Puts a block back under the caret with the same characters selected as before the
  /// sheet was redrawn.
  ///
  /// Applying a style rebuilds the sheet from the model, so every node the selection
  /// pointed at is gone; `focusBlock` then collapsed the caret to the **end** of the
  /// paragraph. Bold worked, and the words stopped being selected - which is exactly the
  /// gesture a person repeats: select, bold, italic. The buttons already went to the
  /// trouble of a `preventDefault` on mousedown to hold the selection while pressed, and
  /// the redraw undid it a line later.
  ///
  /// `cached` is recomputed from the range actually laid rather than from the numbers
  /// asked for, so what the toolbar reads and what the page shows cannot disagree.
  function reselect(index, at) {
    const block = el("sheet").querySelector(`.block[data-index="${index}"]`);
    const node = block && editableIn(block);
    if (!node) { focusBlock(index); return; }

    markCurrent(block);
    node.focus();

    const range = rangeAt(node, at.start, at.end);
    const sel = selection();
    sel.removeAllRanges();
    sel.addRange(range);

    cached = offsetsIn(node, range);
    showActiveStyles();
  }

  /// Which runs the selection covers. Shared by applying a style and by lighting the
  /// buttons that are already on, so the two can never disagree about what is
  /// selected.
  function selectedRuns(runs) {
    const touched = [];
    let at = 0;
    for (const run of runs) {
      const from = at, to = at + run.Text.length;
      at = to;
      if (from >= cached.start && to <= cached.end) touched.push(run);
    }
    return touched;
  }

  /// The run the caret is standing in - the one the character to its left belongs to,
  /// which is the one a new character would join. At the very start of a paragraph there
  /// is nothing to the left, so the first run answers.
  function runAtCaret(item) {
    const runs = item && item.Runs ? item.Runs : [];
    if (!runs.length || !cached) return null;

    const want = Math.max(0, cached.start - 1);
    let at = 0;
    for (const run of runs) {
      const to = at + run.Text.length;
      if (want < to) return run;
      at = to;
    }
    return runs[runs.length - 1];
  }

  /// The ink the swatch button applies, and shows.
  ///
  /// It used to be the last colour chosen and nothing else - "not read off the selection,
  /// which is what the same colour again means". That was a decision and it was the wrong
  /// one, for the reason written fifteen lines below about the size list: B, I, U, S and
  /// both alignments all report what is there, so a control in that row that reports
  /// nothing is the odd one out. Reported from the browser exactly that way.
  ///
  /// So it follows the caret WHERE THE CARET HAS AN OPINION, and otherwise keeps what was
  /// last chosen. That is what saves "the same colour again": move onto plain text and
  /// the swatch still holds your red, ready to press. Move onto red text and it says red,
  /// which is the truth and applies harmlessly.
  let currentInk = null;

  /// What the swatch shows: the caret's colour if the text has one, else the last chosen.
  /// One value, so the button cannot show one colour and apply another.
  let shownInk = null;

  function showInk(colour) {
    shownInk = colour;
    const well = el("well");
    if (well) well.style.background = colour ? hex(colour) : "transparent";
  }

  /// How many colours a document remembers. Enough to cover a house style twice over,
  /// few enough that the grid stays one glance.
  const SWATCH_LIMIT = 16;

  function closeInk() { const menu = el("inkMenu"); if (menu) menu.hidden = true; }

  /// Applies a colour and remembers it. `null` is "no colour of its own", which is a
  /// choice and not an absence, so it applies and is not remembered.
  function setInk(colour) {
    currentInk = colour;
    showInk(colour);
    if (colour) rememberInk(colour);
    applyStyle("Color", colour);
    fillSwatches();
  }

  /// Most recent first, and never the same colour twice. Kept on the report, so it is in
  /// the file: a history in the editor's own settings would be somebody else's history
  /// the moment the document was opened anywhere but here.
  function rememberInk(colour) {
    const key = hex(colour);
    const kept = (report.Swatches || []).filter(c => hex(c) !== key);
    report.Swatches = [colour, ...kept].slice(0, SWATCH_LIMIT);
  }

  /// The colours actually on the page. What a document that carries no list is offered
  /// instead - an imported template would otherwise open with an empty grid while its own
  /// text is in three colours.
  function inkInUse() {
    const seen = new Map();
    const note = font => {
      if (font && font.Color) seen.set(hex(font.Color), font.Color);
    };
    (function walk(item) {
      if (!item) return;
      note(item.Font);
      (item.Runs || []).forEach(run => note(run.Font));
      (item.Childs || []).forEach(walk);
    })(body());
    return [...seen.values()].slice(0, SWATCH_LIMIT);
  }

  /// The grid inside the menu. Built each time it opens rather than kept in step, because
  /// the list changes on every colour applied and a grid that is one behind is worse than
  /// none - it offers the colour before last under the name of the last.
  function fillSwatches() {
    const host = el("inkSwatches");
    if (!host) return;

    const list = (report.Swatches && report.Swatches.length) ? report.Swatches : inkInUse();
    host.innerHTML = "";

    // First, and it is a swatch like the others: "no colour" is the choice of not
    // having one, which is a choice about colour. First because that is where every
    // colour menu puts "none" or "automatic", and because it is the only cell that is
    // always there - a grid whose first cell moves as the document gains colours is a
    // grid you have to look at twice.
    // Which one is ON. A menu of choices that does not say which is current is a menu
    // you have to try - the same sentence written above showActiveStyles, and it applies
    // to a grid of colours as much as to a row of buttons. "No colour" needed it most:
    // it is the state most documents are in, and it looked exactly like the two
    // alternatives beside it.
    //
    // Absent rather than null when a selection disagrees, so a mixed one marks nothing.
    // Undefined here means "not asked" - the menu can be filled before there is any
    // selection at all - and that marks nothing either.
    const item = currentItem();
    const seen = item && item.$type === "AdvancedTextItem" && cached
               ? styled(item, cached.start !== cached.end) : {};
    const now = "Color" in seen ? (seen.Color ? hex(seen.Color) : "") : null;

    const none = document.createElement("button");
    none.id = "colourClear";
    none.className = "none";
    none.title = t("richText.noColor");
    if (now === "") none.classList.add("chosen");
    none.onclick = e => { e.stopPropagation(); setInk(null); closeInk(); };
    host.appendChild(none);

    for (const colour of list) {
      const swatch = document.createElement("button");
      swatch.style.background = hex(colour);
      swatch.title = hex(colour);
      if (now === hex(colour)) swatch.classList.add("chosen");
      swatch.onclick = e => { e.stopPropagation(); setInk(colour); closeInk(); };
      host.appendChild(swatch);
    }
  }

  /// A toolbar that does not show what is already on is a toolbar you have to try.
  function showActiveStyles() {
    const item = currentItem();
    const on = new Set();

    // Everything on the left of the toolbar goes through applyStyle, and applyStyle does
    // nothing at all with a caret: "nothing selected: nothing to style". So with a caret
    // sitting inside bold text, B was dark and inert - which reads as "this is not bold"
    // when it means "there is nothing to make bold". Reported exactly that way, and in
    // both faces.
    //
    // Dimmed rather than lit, because lighting it would be worse: pressing it would then
    // do nothing, and a button that looks pressable and is not is section 10 wearing a
    // toolbar. Telling the truth about the caret's own style needs a pending style the
    // next keystroke picks up, which is a feature and not a fix.
    //
    // The same move the alignment buttons below already make, for the same reason.
    const advanced = !!(item && item.$type === "AdvancedTextItem" && cached);
    const styling = advanced && cached.start !== cached.end;

    // With a caret rather than a selection, the buttons say what the NEXT character
    // would be: the style of the run the caret stands in, with anything already armed
    // laid over it. That is the question a person is asking when they look - and
    // pressing one now arms it rather than doing nothing, so the button stays honest.
    if (advanced && !styling) {
      const run = runAtCaret(item);
      const font = Object.assign({}, run && run.Font, pending && pending.font);
      for (const key of ["Bold", "Italic", "Underline", "Strikethrough"])
        if (font[key]) on.add(key);
    }

    if (styling) {
      const runs = splitRunsAt(item.Runs || [], [cached.start, cached.end]);
      const touched = selectedRuns(runs);
      for (const key of ["Bold", "Italic", "Underline", "Strikethrough"])
        if (touched.length && touched.every(r => r.Font && r.Font[key])) on.add(key);
    }
    uiAll("#toolbar [data-style]").forEach(b => {
      b.classList.toggle("on", on.has(b.dataset.style));
      b.disabled = !advanced;
    });

    for (const id of ["family", "colour", "colourClear", "colourApply", "colourOpen", "size"]) {
      const control = el(id);
      if (control) control.disabled = !advanced;
    }

    // And the two lists SAY what is under the caret, which they did not.
    //
    // Reported from the browser: move onto text set in Huge and the buttons all switch
    // over while the size list stays where it was. The loop above only ever enabled
    // them - it never gave either one a value - and the size list went further and put
    // itself back to Normal after acting, on the reasoning that "it acts, it does not
    // hold a state".
    //
    // That was a decision, and it was the wrong one for a control sitting in a row of
    // stateful ones. B, I, U, S and both alignments all report what is there; a list
    // that looks the same and reports nothing is the odd one out, and looking at it is
    // how the fault was found.
    //
    // The colour swatch is deliberately NOT this, and stays as it is: it is a split
    // button whose left half means "that colour again", so what it shows is the last
    // colour chosen rather than the one under the caret. A different gesture, and it
    // looks like one.
    const shown = advanced ? styled(item, styling) : null;

    // The swatch, in the same breath as the lists and for the same reason: it says what
    // is there, INCLUDING that there is nothing there.
    //
    // It kept the last colour chosen over uncoloured text at first, on the argument that
    // this is what saves "the same colour again". Reported as wrong, and rightly: every
    // other control in this row reports absence as absence - B is unlit over plain text,
    // it does not stay lit because you were bold a moment ago. A swatch showing red over
    // black text is the same lie.
    //
    // What it costs: pressing the swatch over plain text now applies no colour, which is
    // a no-op. Re-applying a remembered one is the chevron and a swatch - the menu keeps
    // sixteen of them, which is where "the same colour again" really lives.
    showInk(shown && "Color" in shown ? (shown.Color || null) : null);

    // Blank is for a MIXED selection - setList says so itself, and it is the right answer
    // there. "Nothing selected at all" is a third state and was being given the same
    // answer: at mount, before any caret exists, `cached` is null, so both lists came up
    // empty on a page that had just finished loading. Reported as a family list that has
    // not loaded, on a page that had.
    //
    // With nothing selected the honest reading is the document's own default, which is
    // what the next character would take - the same argument styled() already makes for a
    // caret standing in a run that states nothing. The lists are disabled either way, so
    // this changes what they SAY and not what they do.
    const nothing = advanced ? null : "";

    setList("family", shown && "Family" in shown ? (shown.Family || "") : nothing);
    setList("size", shown && "Size" in shown
                      ? (shown.Size == null ? "" : String(shown.Size))
                      : nothing);

    // Alignment is about the block, so it lights up from the block rather than from
    // the selection - and it is dimmed where it would do nothing at all, which is
    // every block that is neither a paragraph nor a picture.
    const here = alignmentOf(item);
    const down = verticalOf(item);
    uiAll("#toolbar [data-valign]").forEach(b => {
      b.classList.toggle("on", down === b.dataset.valign);
      b.disabled = down === null;
    });

    uiAll("#toolbar [data-align]").forEach(b => {
      b.classList.toggle("on", here === b.dataset.align);
      b.disabled = here === null;
      // Justify says nothing to a picture: the engine reads it as Left rather than
      // distorting the picture, so offering it here would be a button that lies.
      if (b.dataset.align === "Justify" && item && item.$type === "ImageItem") b.disabled = true;
    });
  }

  /// Alignment belongs to the block, not to the selection: it is where the whole
  /// paragraph sits between the margins, or where a picture sits across the frame the
  /// layout granted it. Two properties, because a picture has no font - ImageItem
  /// carries its own Alignment, and the engine reads Justify there as Left, since a
  /// stretched picture is a distorted one.
  function applyAlignment(how) {
    const item = currentItem();
    if (!item) return;

    // A picture has no font, and the property that answers this for it already
    // exists: Style.AlignHorizontal, which is what places any item of stated width
    // across its band. Its words differ from a text's - Start/Center/End - because it
    // also has to mean "wherever the coordinates put it", which alignment does not.
    if (item.$type === "ImageItem")
      item.Style.AlignHorizontal = how === "Center" ? "Center" : how === "Right" ? "End" : "Start";
    else if (item.$type === "AdvancedTextItem") {
      item.Font = item.Font || newFont();
      item.Font.HorizontalAlignment = how;
    } else return;

    const index = Number((currentBlock() || {}).dataset?.index ?? 0);
    const was = cached && { start: cached.start, end: cached.end };
    draw();
    if (was) reselect(index, was); else focusBlock(index);
  }

  /// Down the height of the pair. Only an absolutely laid out container consults it -
  /// a block alone in the flow is as tall as it is, and there is nothing to sit inside.
  function applyVertical(how) {
    const item = currentItem();
    if (!item || !insidePair()) return;
    item.Style.AlignVertical = how;

    const index = Number((currentBlock() || {}).dataset?.index ?? 0);
    const was = cached && { start: cached.start, end: cached.end };
    draw();
    if (was) reselect(index, was); else focusBlock(index);
  }

  function verticalOf(item) {
    if (!item || !insidePair()) return null;
    return (item.Style && item.Style.AlignVertical) || "Start";
  }

  /// Whether what is being edited is half of a pair. The top-level block is the
  /// container; the cell is the child, and only a child has room to move.
  function insidePair() {
    const block = currentBlock();
    return !!block && block !== current && isPair(block._item);
  }

  /// Which alignment the block being edited already has, so the row shows it.
  function alignmentOf(item) {
    if (!item) return null;
    if (item.$type === "ImageItem") {
      const a = (item.Style && item.Style.AlignHorizontal) || "None";
      return a === "Center" ? "Center" : a === "End" ? "Right" : "Left";
    }
    if (item.$type === "AdvancedTextItem") return (item.Font && item.Font.HorizontalAlignment) || "Left";
    return null;
  }

  /// What the two lists should be showing: the font of the run under the caret, or the
  /// font the whole selection agrees on.
  ///
  /// Agreement is the same rule the buttons already use - "every touched run" - and
  /// disagreement returns the key ABSENT rather than null, because null is a value here
  /// (it is what "Document default" and "Normal" mean). The two have to be told apart or
  /// a mixed selection would claim to be normal.
  function styled(item, styling) {
    if (!styling) {
      const run = runAtCaret(item);
      const font = Object.assign({}, run && run.Font, pending && pending.font);

      // Both keys named, whatever the caret found. A caret ALWAYS has an answer - the
      // style the next character would take - and where the run says nothing, or there
      // is no run at all because the paragraph is empty, that answer is the document's
      // default. It is not "no answer".
      //
      // Written as a plain Object.assign this returned {} for an empty paragraph, both
      // keys absent, and both lists drew themselves empty on a document that had only
      // just opened. Then, a second later, the font list filled itself in on its own -
      // which was the server's font list arriving and rebuilding the options, and looked
      // for all the world like a page repairing itself.
      return { Family: font.Family ?? null, Size: font.Size ?? null,
               Color: font.Color ?? null };
    }

    const touched = selectedRuns(splitRunsAt(item.Runs || [], [cached.start, cached.end]));
    if (!touched.length) return {};

    const font = {};
    for (const key of ["Family", "Size", "Color"]) {
      const first = (touched[0].Font || {})[key] ?? null;
      if (touched.every(r => ((r.Font || {})[key] ?? null) === first))
        font[key] = first;
    }
    return font;
  }

  /// Puts a list on a value, or on nothing at all.
  ///
  /// Nothing at all - selectedIndex -1, which draws the list empty - is what a mixed
  /// selection deserves and what a value with no option deserves. Leaving the previous
  /// choice showing would be the list stating a size the text has not got, which is
  /// worse than saying nothing: section 5c's "a face that records a choice without
  /// drawing it", turned the other way round.
  function setList(id, value) {
    const list = el(id);
    if (!list) return;

    if (value === null) { list.selectedIndex = -1; return; }

    list.value = value;
    if (list.value !== value) list.selectedIndex = -1;
  }

  function applyStyle(key, value) {
    const item = currentItem();
    if (!item || item.$type !== "AdvancedTextItem" || !cached) return;

    // Nothing selected: arm it for the next character instead of doing nothing. The
    // button that lights above has to be pressable, or it is a lit button that lies.
    if (cached.start === cached.end) {
      const font = Object.assign({}, runAtCaret(item) && runAtCaret(item).Font,
                                 pending && pending.font);
      if (value === undefined) font[key] = !font[key];   // a boolean toggles
      else font[key] = value;

      pending = { font, at: cached.start };
      showActiveStyles();
      return;
    }

    const runs = splitRunsAt(item.Runs || [], [cached.start, cached.end]);
    const touched = selectedRuns(runs);

    // A boolean turns off only when every selected run already has it on - the same
    // "apply it twice to remove it" any toolbar gives.
    const boolean = value === undefined;
    const allOn = boolean && touched.every(r => r.Font && r.Font[key]);

    for (const run of touched) {
      run.Font = run.Font || {};
      if (boolean) run.Font[key] = allOn ? null : true;
      else run.Font[key] = value;
      if (Object.keys(run.Font).every(k => run.Font[k] == null)) run.Font = null;
    }

    item.Runs = mergeSameStyleRuns(runs);
    const index = Number((currentBlock() || {}).dataset?.index ?? 0);
    const was = { start: cached.start, end: cached.end };
    draw();
    reselect(index, was);
  }

  // ---------------------------------------------------------------------------
  // The checks. A face may drop tools; it may never drop this panel - a simplified
  // editor that quietly produces a document veraPDF refuses is the worst thing this
  // project could ship.
  // ---------------------------------------------------------------------------
  function runChecks() {
    const issues = validate(report, {
      sound: true, accessible: true, capable: true, target: renderTarget
    });

    // Always, whoever is listening. Before the panel, so a host is told even about a
    // document the panel is in the middle of failing to paint.
    if (onChecks) onChecks(issues);

    const count = el("count");
    if (!count) return issues;

    const errors = issues.filter(i => i.level === "error").length;
    count.textContent = issues.length
      ? t("issues.count", { errors, warnings: issues.length - errors })
      : t("issues.none");
    count.className = "count " + (errors ? "bad" : issues.length ? "warn" : "good");

    const host = el("issues");
    host.innerHTML = "";
    for (const issue of issues) {
      const li = document.createElement("li");
      li.className = "issue " + issue.level;
      li.innerHTML = `<span class="dot"></span><span>${esc(issue.message)}</span>`;
      if (issue.item) li.onclick = () => {
        const at = (body().Childs || []).indexOf(issue.item);
        if (at >= 0) focusBlock(at);
      };
      host.appendChild(li);
    }

    return issues;
  }

  // ---------------------------------------------------------------------------
  // Wiring
  // ---------------------------------------------------------------------------
  function removeCurrent() {
    const kids = body().Childs;
    const block = currentBlock();
    if (!block || kids.length <= 1) return;
    const at = Number(block.dataset.index);
    kids.splice(at, 1);
    current = null;
    draw();
    focusBlock(Math.max(0, at - 1));
  }

  function askEngineFor(route, title) {
    const tab = window.open("", "_blank");
    if (!tab) { alert(t("hint.popup")); return; }
    tab.document.write(`<!doctype html><meta charset="utf-8"><title>${esc(title)}</title>`
                       + `<p style="font:14px system-ui;padding:24px">${esc(t("editor.asking"))}</p>`);

    fetch("http://localhost:5080/" + route, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ template: JSON.stringify(report), data: null, showBands: false })
    })
      .then(r => r.ok ? r.text() : r.text().then(body => Promise.reject(new Error(body))))
      .then(html => { tab.document.open(); tab.document.write(html); tab.document.close(); })
      .catch(err => {
        tab.document.open();
        tab.document.write(`<!doctype html><meta charset="utf-8">`
          + `<p style="font:14px system-ui;padding:24px;white-space:pre-wrap">`
          + `${esc(t("editor.noServer"))}\n\n${esc(String(err.message || err))}</p>`);
        tab.document.close();
      });
  }

  function fillPapers() {
    const select = el("paper");
    // The guard before the read that needs it. Written the other way round it threw on
    // exactly the face it was added for - which is the guard being decorative.
    if (!select) return;
    const chosen = select.value;
    select.innerHTML = "";
    for (const [key, [w, h]] of Object.entries(PAPER_SIZES)) {
      const option = document.createElement("option");
      option.value = `${w}x${h}`;
      // The name and the millimetres both: "A4 portrait" is what somebody asks for and
      // "210 × 297 mm" is what they check.
      option.textContent = `${t("paper." + key)} — ${w} × ${h} mm`;
      select.appendChild(option);
    }
    select.value = chosen || "210x297";
  }

  /// Puts the file's own properties back on screen, where there is a screen for them.
  ///
  /// A face without the panel has nothing to sync, and that is not the same as having
  /// nothing to say: the properties are still on the report, and the host reads them off
  /// the element. This function is about the view alone.
  function syncPanel() {
    const title = el("title");
    if (!title) return;
    title.value = report.Title || "";
    el("language").value = report.Language || "";
    const d = report.Sections[0].Dimension || {};
    el("paper").value = `${d.Width || 210}x${d.Height || 297}`;
  }

  /// Everything the editor does when it starts: wiring the toolbar and the panel to
  /// their handlers, then building the first document and drawing it.
  ///
  /// Gathered into one function rather than left as two hundred lines of top-level
  /// statements, so that something other than this page can decide WHEN it happens -
  /// an element has to point uiRoot at its shadow root before any of this runs.
  /// Every declaration stays top level, which is what check-editor.js reaches by name;
  /// only the statements moved, and the four functions that were among them were
  /// hoisted out rather than shut in.
  /// Wire a control if this face has it.
  ///
  /// Only for the controls SHOWN can take away - the header's buttons, the panel's
  /// fields, the two target checkboxes. Everything else keeps its bare el(id) on purpose:
  /// a missing toolbar button or a missing sheet is a fault, and a fault should throw
  /// where it happens rather than be quietly skipped. A guard that covers everything is a
  /// guard that hides the next mistake, which is what this file already learned twice
  /// about `if (!current.isContentEditable) return`.
  const wire = (id, fn) => { const node = el(id); if (node) fn(node); return node; };

  function mount() {
    // The two listeners that were registered at load, when uiRoot was still `document`
    // and an element had not yet said otherwise. A registration made before a seam is set
    // can never follow it: inside an element they went on listening to the host page's
    // document, which is not where our events are.
    //
    // selectionchange is the one that matters. It is fired at the ROOT of the selection -
    // the shadow root, for a caret in our surface - not bubbled up from it, so a document
    // listener is not the one being fired at. It is the only writer of `cached`, and the
    // whole style toolbar reads `cached`: bold, italic, underline, strike, the colour,
    // the size, the family. All of them are silent when it never runs, and the alignment
    // buttons still work, so the toolbar looks half-broken rather than unfed.
    //
    // Bound to both when they differ, because the handler recomputes from scratch and
    // running twice costs nothing, while guessing which one Chromium fires costs a
    // silent toolbar.
    uiRoot.addEventListener("keydown", onKeyDown);
    uiRoot.addEventListener("selectionchange", onSelectionChange);
    if (uiRoot !== document) document.addEventListener("selectionchange", onSelectionChange);
    // The markup first: everything below looks its own nodes up, and none of them exist
    // until this line has run.
    uiHost = uiHost || document.getElementById("frisket-editor-root") || document.body;
    uiHost.innerHTML = EDITOR_MARKUP();

    uiAll("#toolbar [data-style]").forEach(b => {
      // mousedown before click would move focus out of the surface; preventing the
      // default keeps the selection highlighted while the button is pressed.
      b.addEventListener("mousedown", e => e.preventDefault());
      b.onclick = () => applyStyle(b.dataset.style);
    });

    uiAll("#toolbar [data-align]").forEach(b => {
      b.addEventListener("mousedown", e => e.preventDefault());
      b.onclick = () => applyAlignment(b.dataset.align);
    });

    uiAll("#toolbar [data-valign]").forEach(b => {
      b.addEventListener("mousedown", e => e.preventDefault());
      b.onclick = () => applyVertical(b.dataset.valign);
    });

    el("family").onchange = () => {
      const family = el("family").value;
      applyStyle("Family", family || null);
    };

    wire("fontSource", n => { n.onchange = e => {
      fontSource = e.target.value;
      try { localStorage.setItem("frisket.fonts", fontSource); } catch {}
      applyDefaultFace();
      if (fontSource === "library" && libraryFamilies === null) askServerForFonts();
      else fillFamilies();
    }; });

    // The swatch applies; the chevron opens. Two gestures, because they are two
    // questions - "this colour again" and "which colour" - and the control used to
    // answer only the second: a native picker fires `change` when it closes, so pressing
    // it again with the same colour chosen fired nothing at all and the button did
    // nothing. Reported exactly that way.
    // What it SHOWS, not what was last chosen - the two are the same until the caret
    // moves onto coloured text, and a button showing one colour and applying another is
    // the fault this whole change is about, wearing the other face.
    el("colourApply").onclick = () => setInk(shownInk);

    el("colourOpen").onclick = e => {
      e.stopPropagation();
      const menu = el("inkMenu");
      if (menu.hidden) fillSwatches();
      menu.hidden = !menu.hidden;
    };

    // Live on the well while the picker is being dragged, applied when it is settled.
    // Applying on every `input` would redraw the whole sheet per pixel of the gradient.
    el("colourPick").onclick = e => { e.stopPropagation(); el("colour").click(); };

    el("colour").oninput = () => { el("well").style.background = el("colour").value; };
    el("colour").onchange = () => { setInk(hexToRgb(el("colour").value)); closeInk(); };
    // No handler for the clearing cell here: it is a swatch now, made by fillSwatches
    // and wired there. Left behind, this line ran at mount against an element the markup
    // no longer holds - getElementById returns null in a browser, so the page would have
    // died at its first statement, and the harness's own stub never returns null and
    // would not have said a word about it (section 16).

    // Anywhere else closes it. On the document rather than on uiRoot: a click that lands
    // outside a shadow root still has to close a menu inside it.
    document.addEventListener("click", closeInk);

    el("size").onchange = () => {
      const v = el("size").value;
      applyStyle("Size", v === "" ? null : Number(v));
      // It holds a state now, and showActiveStyles is what puts it there - so it is not
      // put back by hand here. Written the other way the list flicked to Normal and then
      // back to the chosen size on the next redraw, which is a flicker rather than a
      // fault and would have been blamed on the browser.
    };

    el("addImage").onclick = () => {
      const picker = document.createElement("input");
      picker.type = "file";
      picker.accept = "image/*";
      picker.onchange = () => {
        const file = picker.files[0];
        if (!file) return;
        const reader = new FileReader();
        reader.onload = () => {
          const item = newItem("ImageItem");
          // The bytes travel in the template, the way the designer embeds them: one
          // file to carry about, and the engine reads exactly this.
          item.Source = { MediaType: file.type || "image/png",
                          Data: String(reader.result).split(",")[1] || "" };
          item.AltText = "";

          // Measured rather than assumed. newItem hands out 40 x 20 mm, which is a
          // reasonable placeholder and the wrong shape for almost every real picture;
          // left alone, every image would come in at the same proportions.
          const probe = new Image();
          probe.onload = () => {
            item.NaturalWidth = probe.width / 3.7795;      // 96 dpi
            item.NaturalHeight = probe.height / 3.7795;
            // Wider than the band it is going into is never what anybody meant.
            item.Style.Location.WidthValue = Math.min(item.NaturalWidth, bandWidth());
            insertAfterCurrent(item);
          };
          probe.onerror = () => insertAfterCurrent(item);
          probe.src = String(reader.result);
        };
        reader.readAsDataURL(file);
      };
      picker.click();
    };

    el("addTable").onclick = () => insertAfterCurrent(newTable());

    // The fields' menu opens and closes as the colours' does, and its button keeps the
    // caret where it is while pressed, as every button in this row does.
    el("addField").addEventListener("mousedown", e => e.preventDefault());
    el("addField").onclick = e => {
      e.stopPropagation();
      const menu = el("fieldMenu");
      const open = menu.hidden === false;
      if (!open) fillFieldMenu();
      menu.hidden = open;
    };
    document.addEventListener("click", closeFields);

    // A paragraph is the one block that had no button, because it is the one a person
    // usually makes by typing. But a table or a picture at the very top of the sheet
    // leaves nowhere to type: there is no caret above it, and shift+Enter needs one to
    // start from. So a block that is only reachable by keystroke is not reachable at all
    // on a document that starts with a table.
    el("addText").onclick = () => insertAfterCurrent(emptyParagraph());
    el("addRule").onclick = () => insertAfterCurrent(newItem("LineItem"));

    el("removeBlock").onclick = removeCurrent;

    wire("title", n => { n.oninput = () => { report.Title = n.value; runChecks(); }; });
    wire("language", n => { n.oninput = () => { report.Language = n.value; runChecks(); }; });

    wire("paper", n => {
      n.onchange = () => {
        const [w, h] = n.value.split("x").map(Number);
        report.Sections[0].Dimension = { Width: w, Height: h };
        draw();
      };
    });

    // The two checkboxes are a VIEW of the target now, not the target itself. They were
    // the only place it lived - `{ Tagged: el("targetTagged").checked, ... }`, read off
    // the DOM at every check - so a face without them had no target at all, and an empty
    // one is the worst of the three possibilities: the Capable profile gates on the
    // object being present, not on its fields, so every rule in it would have fired on
    // every document.
    for (const id of ["targetTagged", "targetInteractive"])
      wire(id, n => {
        n.onchange = () => {
          renderTarget = { ...renderTarget,
                           Tagged: el("targetTagged").checked,
                           Interactive: el("targetInteractive").checked };
          runChecks();
        };
      });

    wire("lang", n => { n.onchange = e => {
      setLang(e.target.value);
      try { localStorage.setItem("frisket.lang", e.target.value); } catch {}
      applyStaticText();
      fillPapers();
      fillFamilies();
      draw();
    }; });

    wire("theme", n => { n.onchange = e => {
      theme = e.target.value;
      try { localStorage.setItem("frisket.theme", theme); } catch {}
      applyTheme();
    }; });

    wire("new", n => { n.onclick = () => {
      report = blankFlow();
      current = null;
      syncPanel();
      draw();
      focusBlock(0);
    }; });

    wire("open", n => { n.onclick = () => el("file").click(); });
    wire("file", n => { n.onchange = e => {
      const file = e.target.files[0];
      if (!file) return;
      const reader = new FileReader();
      reader.onload = () => {
        try {
          report = normaliseReport(JSON.parse(String(reader.result)));
          hydrate(report.Sections[0]);
          current = null;
          syncPanel();
          draw();
        } catch (err) {
          alert(t("alert.unreadable") + "\n" + err.message);
        }
      };
      reader.readAsText(file);
      e.target.value = "";
    }; });

    /// Opens what the engine would write, in a tab of its own.
    ///
    /// Two documents rather than one with a switch. `/html` is the pages themselves, drawn
    /// as SVG with the fonts embedded: real metrics, real pagination, real repetition - the
    /// truth the sheet on the left only approximates. `/mail` has no pages at all: HTML that
    /// reflows, tables for layout, nothing embedded, for a message. Neither is a variant of
    /// the other, so neither hides behind a flag on the other.
    ///
    /// The tab is opened *before* the request and told what happened afterwards. Opening it
    /// in the promise's callback is opening it outside a click, which every browser blocks
    /// as a pop-up.
    wire("html", n => { n.onclick = () => askEngineFor("html", t("editor.openHtml")); });
    wire("mail", n => { n.onclick = () => askEngineFor("mail", t("editor.openMail")); });

    wire("save", n => { n.onclick = () => {
      const text = JSON.stringify(report, null, 2);
      const a = document.createElement("a");
      a.href = URL.createObjectURL(new Blob([text], { type: "application/json" }));
      a.download = (report.Title || "document").replace(/[^\w.-]+/g, "-").toLowerCase() + ".json";
      a.click();
      URL.revokeObjectURL(a.href);
    }; });

    wire("lang", n => { n.value = lang0; });
    wire("theme", n => { n.innerHTML = themeOptions(); n.value = theme; });
    wire("fontSource", n => { n.value = fontSource; });
    fillPapers();
    applyStaticText();
    fillFamilies();
    applyDefaultFace();
    if (fontSource === "library") askServerForFonts();
    report = blankFlow();
    syncPanel();
    loadFields();
    draw();
    if (autofocus) focusBlock(0);
  }

  // --- what the caller declared -------------------------------------------------------
  // Set once, after everything above exists and before anything is drawn - the seams of
  // CLAUDE.md 5c-quater. A plain page and an element pass the same names, read from the
  // same attributes: one vocabulary, two ways in.
  //
  //   root         where the markup lives and the ids are looked up: the document, or
  //                this editor's own shadow root
  //   host         what the markup is planted in
  //   themeTarget  what carries data-theme and --paper
  //   shown        what this face shows (chrome, panel, files)
  //   margins      a margin in millimetres, or nothing for "you decide"
  //   theme, fonts what was declared, if anything - chooseTheme and chooseFontSource
  //                answer the rest, and a face without the control ignores the stored
  //                preference of the face that has it
  //   target       what is about to be produced, for the Capable profile
  //   server       where the engine is whose fonts it draws with, "/" for this page's
  //                origin; none declared, there is no engine to ask
  //   fetch        how to reach it, for a host that adds its own credentials
  //   fields       the host's merge fields - [{ path, label, type, sample }], a function
  //                returning them, or a promise of either (see "merge fields" above)
  //   onChecks     where the findings go when there is no panel to paint them on
  //   autofocus    whether mounting takes the focus. A page holding three editors that
  //                each took it would end up scrolled to the last one
  uiRoot = seams.root || document;
  uiHost = seams.host || null;
  themeTarget = seams.themeTarget || document.documentElement;
  if (seams.shown) SHOWN = seams.shown;
  margins = seams.margins ?? null;
  theme = chooseTheme(seams.theme);
  fontSource = chooseFontSource(seams.fonts);
  if (seams.target) renderTarget = seams.target;
  server = seams.server == null ? null : String(seams.server).trim().replace(/\/+$/, "");
  onChecks = seams.onChecks || null;
  const autofocus = seams.autofocus !== false;
  applyTheme();

  // @harness - check-editor.js plants its handles here, inside, where they can see.
  return {
    mount,
    loadReport,
    /// The document as the engine reads it.
    report: () => forEngine(report),
    /// Another catalogue of merge fields, or the same one changed.
    setFields: next => { fieldSource = next || null; if (el("fieldsTool")) loadFields(); },
  };
}

// A plain page hands over a div of its own and is mounted at once: one editor, owning
// the document. Inside a <frisket-editor> there is no such div in the document - the
// element makes an editor of its own, in its own shadow root, and mounts that.
//
// The div is also where a page SAYS what it wants shown, with the same attribute names
// the element reads - one vocabulary, two ways in. The fallback here is the full face
// and in the element it is the lean one, and that difference is the whole distinction:
// a page IS the application, so it keeps its header and its panel unless it says
// otherwise; an element is inside somebody else's, which already has both.
{
  const host = document.getElementById("frisket-editor-root");
  if (host) {
    const said = name => host.getAttribute ? host.getAttribute(name) : null;
    const on = (name, fallback) => host.hasAttribute && host.hasAttribute(name)
      ? host.getAttribute(name) !== "false"
      : fallback;

    frisketEditor({
      root: document,
      host,
      themeTarget: document.documentElement,
      shown: { chrome: on("chrome", true), panel: on("panel", true), files: said("files") || "header" },
      margins: said("margins"),
      theme: said("theme"),
      fonts: said("fonts"),
      // The preview server this page has always asked, unless the page names another.
      server: said("server") ?? "http://localhost:5080",
      autofocus: on("autofocus", true),
    }).mount();
  }
}
