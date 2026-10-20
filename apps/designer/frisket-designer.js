/// The template designer.
///
/// Lifted out of frisket-designer.html on 20 October 2026, so that something other than
/// that one page can load it - the step frisket-editor.js took for the content editor on
/// 24 August. A custom element cannot reach a function declared inside a page's inline
/// script.
///
/// And a designer is a call - `frisketDesigner(seams)` - rather than the file itself, for
/// the reason the content editor became one on 18 October: a host application embeds it,
/// and a shape that allows one of something is a requirement nobody wrote down. Every
/// declaration of the designer now lives inside `mount()`, one copy per designer, in the
/// order it always ran; check-against-engine.js reaches them by planting its handles at
/// the `@harness` line, inside, where they can see.
///
/// What decides where a designer draws is the root and the theme target it is handed: the
/// document for the plain page, a shadow root for each <frisket-designer>.

// ---------------------------------------------------------------------------
// One designer.
//
// `seams` is what the caller declares:
//
//   root         where the markup lives and the ids are looked up: the document, or this
//                designer's own shadow root
//   host         what the markup is planted in
//   themeTarget  what carries data-theme and --paper
//   theme        a room the host declared, over the stored preference
//   onChecks     where the findings go, as well as to the panel
//   subDocuments the host's library of sub-documents - list() and find(reference), either
//                of which may answer with a promise (see "the host's library" below)
//   server       where the engine is: a base URL answering what apps/server answers -
//                POST /preview, POST /pdf, GET /fonts and /fonts/file. Declared, it is the
//                host's, and the field that types one is not shown; declared null, there is
//                none, and nothing that would ask it is shown either
//   fetch        how to reach it, for a host that adds its own authentication - the
//                browser's fetch otherwise
//   live         where the engine's pages go at first: "off", "canvas", "beside" or
//                "window". The person's own choice, once made, wins - it is their screens
//   liveDelay    how long the template must stay still before the engine is asked, in
//                milliseconds - half a second unless a slower engine wants longer
//   lang         the interface's language, which is the page's
//   shown        what this face shows of its header - files, language, rooms, server
// ---------------------------------------------------------------------------
let designersMade = 0;

function frisketDesigner(seams = {}) {
  // Counted, so each can name the window it opens.
  const designersMade_ = ++designersMade;
  const uiRoot = seams.root || document;
  let uiHost = seams.host || null;
  const themeTarget = seams.themeTarget || document.documentElement;
  const byId = id => uiRoot.getElementById(id);

  /// The designer's markup, so that whatever it is mounted into gets the same one. A
  /// string rather than the page's body, because a shadow root cannot be handed markup
  /// that lives in a document it is not part of.
  const DESIGNER_MARKUP = `
<header>
  <h1 data-i18n="app.name">Frisket</h1>
  <button id="new" data-i18n="action.new">New</button>
  <button id="open" data-i18n="action.open">Open JSON…</button>
  <button class="primary" id="save" data-i18n="action.save">Save JSON</button>
  <button id="sections" data-i18n="sections.open">Sections…</button>
  <select id="lang" title="Language" style="margin-left:6px">
    <option value="en">EN</option>
    <option value="fr">FR</option>
  </select>
  <!-- Filled from THEMES, never typed here. -->
  <select id="theme" style="margin-left:6px"></select>
  <input type="file" id="file" accept=".json,application/json" hidden>
  <span class="grow"></span>
  <label style="color:var(--muted)"><input type="checkbox" id="show-bands" checked> bandes</label>
  <label style="color:var(--muted)"><input type="checkbox" id="show-boxes"> cadres</label>
  <span class="exact">
    <span class="dot" id="dot" title="état du serveur d'aperçu"></span>
    <input type="text" id="server" value="http://localhost:5080" title="serveur d'aperçu exact">
    <!-- Where the engine's own pages go, kept up to date: the person's choice, since it
         depends on their screens rather than on the document. -->
    <select id="live">
      <option value="off" data-i18n="live.off">Exact preview: off</option>
      <option value="canvas" data-i18n="live.canvas">Exact preview: on the canvas</option>
      <option value="beside" data-i18n="live.beside">Exact preview: beside</option>
      <option value="window" data-i18n="live.window">Exact preview: in a window</option>
    </select>
    <span class="stamp" id="live-status"></span>
    <button id="detach" data-i18n="action.detach">Preview window</button>
    <select id="zoom" title="Zoom" style="margin-left:6px">
      <option value="fit" data-i18n="zoom.fit">Fit</option>
      <option value="0.5">50 %</option>
      <option value="0.75">75 %</option>
      <option value="1">100 %</option>
      <option value="1.5">150 %</option>
      <option value="2">200 %</option>
    </select>
    <button id="pdf">PDF</button>
  </span>
</header>

<div class="banner" data-i18n="hint.approximate"></div>

<main>
  <aside>
    <div class="tabs">
      <button class="tab on" data-tab="doc" data-i18n="tab.section">Section</button>
      <button class="tab" data-tab="back" data-i18n="tab.back">Back</button>
      <button class="tab" data-tab="data" data-i18n="tab.data">Data</button>
      <button class="tab" data-tab="mark" data-i18n="tab.watermark">Watermark</button>
      <button class="tab" data-tab="paper" data-i18n="tab.background">Background</button>
    </div>

    <div class="pane" data-pane="doc">
    <div class="section">
      <h2 data-i18n="panel.add">Add</h2>

      <div class="family" data-i18n="family.display">Display</div>
      <div class="palette">
        <button data-add="TextItem" data-i18n="add.text">Text</button>
        <button data-add="LineItem" data-i18n="add.rule">Rule</button>
        <button data-add="ImageItem" data-i18n="add.image">Image</button>
        <button data-add="TableItem" data-i18n="add.table">Table</button>
        <button data-add="BarcodeItem" data-i18n="add.code">Code</button>
        <button data-add="ContainerItem" data-i18n="add.group">Group</button>
        <button data-add="ImageGridItem" data-i18n="add.imageGrid">Image grid</button>
        <button data-add="SubTemplateItem" data-i18n="add.subTemplate">Sub-template</button>
        <button data-add="ImportedPageItem" data-i18n="add.importedPage">PDF page</button>
        <button data-add="AdvancedTextItem" data-i18n="add.advancedText">Advanced text</button>
      </div>

      <div class="family" data-i18n="family.input">Input</div>
      <div class="palette">
        <button data-add="FieldText" data-i18n="add.text">Text</button>
        <button data-add="FieldCheckbox" data-i18n="add.checkbox">Checkbox</button>
        <button data-add="FieldSignature" data-i18n="add.signature">Signature</button>
      </div>

      <!-- Filled from the host's library, never typed here; hidden while it is empty. -->
      <div class="family" id="libraryFamily" data-i18n="family.library" hidden>Library</div>
      <div class="palette" id="libraryPalette" hidden></div>

      <div class="hint" data-i18n="hint.add"></div>
    </div>
    <div class="section">
      <h2 data-i18n="panel.structure">Structure</h2>
      <label class="only-faults">
        <input type="checkbox" id="onlyFaults">
        <span data-i18n="issues.onlyFaults">only what is wrong</span>
      </label>
      <ul class="tree" id="tree"></ul>
    </div>
    <div class="section">
      <h2 data-i18n="panel.issues">Checks</h2>
      <!-- What is about to be produced. Two of the checks read this and nothing
           else does: a template can be faultless and still lose half of itself on
           the way out, and only the output being aimed at says which half. -->
      <div class="hint" data-i18n="hint.target"></div>
      <label class="only-faults">
        <input type="checkbox" id="targetTagged" checked>
        <span data-i18n="target.tagged">tagged output</span>
      </label>
      <label class="only-faults">
        <input type="checkbox" id="targetInteractive" checked>
        <span data-i18n="target.interactive">fields stay fillable</span>
      </label>
      <div id="issueCount" class="issue-count good"></div>
      <ul id="issues" class="issues"></ul>
    </div>
    </div><!-- /doc -->

    <div class="pane" data-pane="back" hidden>
      <div class="section">
        <h2 data-i18n="panel.back">Back</h2>
        <label style="color:var(--muted);font-size:12px">
          <input type="checkbox" id="bpOn"> <span data-i18n="back.enable">print a back</span>
        </label>
        <div id="bpFields" hidden>
          <div class="field"><label data-i18n="field.scope">On</label><div class="val">
            <select id="bpScope">
              <option value="EveryPage" data-i18n="BackPageScope.EveryPage">Every sheet</option>
              <option value="LastPage" data-i18n="BackPageScope.LastPage">The last</option>
              <option value="FirstPage" data-i18n="BackPageScope.FirstPage">The first</option>
            </select></div></div>
          <div class="field"><label data-i18n="field.padding">Margins</label><div class="val">
            <input type="number" id="bpPad" value="12" step="1"></div></div>
        </div>
        <div class="hint" data-i18n-html="hint.back"></div>
      </div>

      <div id="bpEdit" hidden>
        <div class="section">
          <h2 data-i18n="panel.addBack">Add to the back</h2>
          <div class="palette">
            <button data-add="TextItem" data-i18n="add.text">Text</button>
            <button data-add="LineItem" data-i18n="add.rule">Rule</button>
            <button data-add="ImageItem" data-i18n="add.image">Image</button>
            <button data-add="TableItem" data-i18n="add.table">Table</button>
            <button data-add="BarcodeItem" data-i18n="add.code">Code</button>
            <button data-add="ContainerItem" data-i18n="add.group">Group</button>
          </div>
        </div>
        <div class="section">
          <h2 data-i18n="panel.backStructure">Structure of the back</h2>
          <ul class="tree" id="backTree"></ul>
        </div>
      </div>
    </div><!-- /back -->

    <div class="pane" data-pane="data" hidden>
    <div class="section">
      <h2 data-i18n="panel.data">Data</h2>
      <div class="palette">
        <button id="dataOpen" data-i18n="action.load">Load…</button>
        <button id="dataSave" data-i18n="action.saveShort">Save</button>
        <button id="dataClear" data-i18n="action.clear">Clear</button>
      </div>
      <input type="file" id="dataFile" accept=".json,application/json" hidden>
      <textarea id="data" spellcheck="false"></textarea>
      <div class="hint" data-i18n="hint.data"></div>
    </div>
    </div><!-- /data -->

    <div class="pane" data-pane="mark" hidden>
    <div class="section">
      <h2 data-i18n="panel.watermark">Watermark</h2>
      <label style="color:var(--muted);font-size:12px">
        <input type="checkbox" id="wmOn"> <span data-i18n="watermark.enable">enable</span>
      </label>
      <div id="wmFields" hidden>
        <div class="field"><label data-i18n="add.text">Text</label><div class="val">
          <input type="text" id="wmText" value="DRAFT "></div></div>
        <div class="field"><label data-i18n="field.mode">Placing</label><div class="val">
          <select id="wmMode">
            <option value="Tiled" data-i18n="WatermarkMode.Tiled">Repeated</option>
            <option value="Single" data-i18n="WatermarkMode.Single">Once, large</option>
          </select></div></div>
        <div class="field"><label data-i18n="field.layer">Layer</label><div class="val">
          <select id="wmLayer">
            <option value="InFront" data-i18n="WatermarkLayer.InFront">Over the content (translucent)</option>
            <option value="Behind" data-i18n="WatermarkLayer.Behind">Under the content</option>
          </select></div></div>
        <div class="field"><label data-i18n="field.angle">Angle</label><div class="val">
          <input type="number" id="wmAngle" value="45" step="5"></div></div>
        <div class="field"><label data-i18n="field.size">Size</label><div class="val">
          <input type="number" id="wmSize" value="8" step="0.5"></div></div>
      </div>
      <div class="hint" data-i18n="hint.watermark"></div>
    </div>
    </div><!-- /mark -->

    <div class="pane" data-pane="paper" hidden>
    <div class="section">
      <h2 data-i18n="panel.background">Background</h2>
      <label style="color:var(--muted);font-size:12px">
        <input type="checkbox" id="bgOn"> <span data-i18n="background.enable">enable</span>
      </label>
      <div id="bgFields" hidden>
        <div class="field"><label data-i18n="add.image">Image</label><div class="val">
          <input type="file" id="bgFile" accept="image/png,image/jpeg"></div></div>
        <div class="field"><label data-i18n="field.area">Covers</label><div class="val">
          <select id="bgArea">
            <option value="Sheet" data-i18n="BackdropArea.Sheet">The whole sheet</option>
            <option value="Margins" data-i18n="BackdropArea.Margins">Inside the margins</option>
          </select></div></div>
        <div class="field"><label data-i18n="field.scaling">Fitting</label><div class="val">
          <select id="bgScaling">
            <option value="Stretch" data-i18n="ImageScaling.Stretch">Stretch</option>
            <option value="Uniform" data-i18n="ImageScaling.Uniform">Keep proportions</option>
            <option value="None" data-i18n="ImageScaling.None">None</option>
          </select></div></div>
        <div class="field"><label data-i18n="field.alignH">Across</label><div class="val">
          <select id="bgAcross">
            <option value="Start" data-i18n="ItemAlign.Start">Left</option>
            <option value="Center" data-i18n="ItemAlign.Center">Centred</option>
            <option value="End" data-i18n="ItemAlign.End">Right</option>
          </select></div></div>
        <div class="field"><label data-i18n="field.alignV">Down</label><div class="val">
          <select id="bgDown">
            <option value="Start" data-i18n="ItemAlign.Top">Top</option>
            <option value="Center" data-i18n="ItemAlign.Middle">Middle</option>
            <option value="End" data-i18n="ItemAlign.Bottom">Bottom</option>
          </select></div></div>
        <div class="field"><label data-i18n="field.opacity">Opacity</label><div class="val">
          <input type="number" id="bgOpacity" value="1" min="0" max="1" step="0.05"></div></div>
      </div>
      <div class="hint" data-i18n="hint.background"></div>
    </div>
    </div><!-- /paper -->
  </aside>

  <div class="splitter" data-splits="left"></div>

  <div id="canvas"></div>

  <div class="splitter" data-splits="right"></div>

  <aside class="right" id="props-pane">
    <div class="section">
      <h2 data-i18n="panel.properties">Properties</h2>
      <div id="props"></div>
    </div>
  </aside>

  <!-- The large preview. Present only on a screen wide enough to hold it beside a
       designer that has not been squeezed to make room. Its own zoom, independent
       of the small canvas's: fitted by default, since that is what a second
       screen is usually for, but a page read at a size of its own choosing is
       not always the page fitted to a narrower one beside it. -->
  <div class="splitter" data-splits="stage" id="stage-splitter" hidden></div>
  <div id="stage" hidden>
    <div id="stage-bar">
      <select id="stage-zoom" title="Zoom">
        <option value="fit" data-i18n="zoom.fit">Fit</option>
        <option value="0.5">50 %</option>
        <option value="0.75">75 %</option>
        <option value="1">100 %</option>
        <option value="1.5">150 %</option>
        <option value="2">200 %</option>
      </select>
    </div>
    <div id="stage-pages"></div>
  </div>
</main>

<!-- Editing an AdvancedTextItem's runs by typing continuously and selecting a
     range, rather than one box per run - the run list stays, this is another way
     to produce the same list. -->
<div class="modal-overlay" id="richTextOverlay" hidden>
  <div class="modal">
    <h2 data-i18n="richText.title">Rich text</h2>
    <div class="toolbar">
      <button data-rt-style="Bold"><b>B</b></button>
      <button data-rt-style="Italic"><i>I</i></button>
      <button data-rt-style="Underline"><u>U</u></button>
      <button data-rt-style="Strikethrough"><s>S</s></button>
      <input type="color" id="richTextColor" title="Colour">
      <button id="richTextColorClear" data-i18n="richText.noColor">No colour</button>
      <input type="number" id="richTextSize" step="0.25" min="0.25" style="width:60px" title="Size">
      <button id="richTextSizeClear" data-i18n="richText.noSize">Default size</button>
    </div>
    <div class="rich-surface" id="richTextSurface" contenteditable="true"></div>
    <p class="hint" data-i18n="richText.hint">Select a range of the text, then click a style to apply it to the selection.</p>
    <div class="actions">
      <button id="richTextCancel" data-i18n="action.cancel">Cancel</button>
      <button class="primary" id="richTextSave" data-i18n="richText.apply">Apply</button>
    </div>
  </div>
</div>

<div class="modal-overlay" id="sectionOverlay" hidden>
  <div class="modal">
    <h2 data-i18n="sections.title">Sections</h2>
    <p class="hint" data-i18n="sections.hint">A report is a list of sections, each with
      its own page size. This page edits one of them at a time; everything else stays
      exactly where it is.</p>
    <div id="sectionList"></div>
    <div class="actions">
      <button id="sectionAdd" data-i18n="sections.add">Add a section</button>
      <button class="primary" id="sectionClose" data-i18n="action.close">Close</button>
    </div>
  </div>
</div>
`;

  let api = null;

  function mount() {
    if (api) return;
    // The markup first: everything below looks its own nodes up, in the order it always
    // did, and none of them exist until this line has run.
    uiHost = uiHost || document.getElementById("frisket-designer-root") || document.body;
    uiHost.innerHTML = DESIGNER_MARKUP;

    // ---------------------------------------------------------------------------
    // UI
    // ---------------------------------------------------------------------------
    // Remembering the language and repainting the markup with it: both are about this
    // page, so both are here. The core owns the dictionary and which language is being
    // read, because the wording of a validator issue is not chrome.
    function setLanguage(code) {
      setLang(code);
      try { localStorage.setItem("frisket.lang", lang); } catch {}
      applyStaticText();
      refresh();
    }

    /// Fills everything written in the markup. Elements name their key with data-i18n,
    /// so the page holds no wording of its own.
    function applyStaticText() {
      // The tab's title is the page's. An embedded designer renaming its host's tab on
      // every redraw is what the content editor did until 18 October 2026.
      if (uiRoot === document) document.title = t("app.title");
      uiRoot.querySelectorAll("[data-i18n]").forEach(el => {
        el.textContent = t(el.dataset.i18n);
      });
      uiRoot.querySelectorAll("[data-i18n-html]").forEach(el => {
        el.innerHTML = esc(t(el.dataset.i18nHtml)).replace(/\{(\w+)\}/g, "<b>{$1}</b>");
      });
    }

    // Which theme the interface wears. "auto" leaves it to the machine, which is the
    // only honest default - a preference stored somewhere is still a preference, and
    // the question worth answering is what this looks like when nobody has said.
    //
    // It lives here rather than in the core because it is entirely about the page: it
    // stamps an attribute on the root element and the palette follows from CSS. The core
    // draws paper, which no theme touches.
    let theme = "auto";
    try { theme = themeOrAuto(localStorage.getItem("frisket.theme")); } catch {}
    if (seams.theme) theme = themeOrAuto(seams.theme);

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

    function setTheme(next) {
      theme = next;
      try { localStorage.setItem("frisket.theme", theme); } catch {}
      applyTheme();
      // The detached preview holds a copy of the palette, taken when it opened.
      if (previewWindow && !previewWindow.closed) { previewWindow.close(); previewWindow = null; }
      refresh();
    }

    applyTheme();

    // How large the sheet is drawn. Declared before the controls are wired, since the
    // select is given its value there - and a `let` read ahead of its own line is a
    // reference error, not an undefined.
    let zoomChoice = "fit";
    try { zoomChoice = localStorage.getItem("frisket.zoom") || "fit"; } catch {}

    // The stage and the detached preview window each read at their own size, not the
    // small canvas's - a second monitor is usually for reading a page whole, but not
    // always, and there is no reason forcing one to match the other.
    let stageZoom = "fit";
    try { stageZoom = localStorage.getItem("frisket.zoom.stage") || "fit"; } catch {}

    let popupZoom = "fit";
    try { popupZoom = localStorage.getItem("frisket.zoom.preview") || "fit"; } catch {}

    const ui = {
      tree: byId("tree"),
      props: byId("props"),
      canvas: byId("canvas"),
      showBands: byId("show-bands"),
      showBoxes: byId("show-boxes"),
      server: byId("server"),
      data: byId("data"),
      dot: byId("dot"),
      onlyFaults: byId("onlyFaults"),
      targetTagged: byId("targetTagged"),
      targetInteractive: byId("targetInteractive"),
      live: byId("live"),
      liveStatus: byId("live-status"),
      stage: byId("stage"),
      stagePages: byId("stage-pages"),
      stageSplitter: byId("stage-splitter"),
      banner: uiRoot.querySelector(".banner")
    };

    // What this face shows of its header. Where the engine is, which language and which
    // room are decisions a host makes once for its whole application, so an element hides
    // those unless asked; the plain page is the whole application and keeps the lot.
    const declaredServer = "server" in seams;
    const shown = Object.assign(
      { files: true, language: true, rooms: true, server: !declaredServer }, seams.shown || {});
    const hide = ids => ids.forEach(id => { const node = byId(id); if (node) node.hidden = true; });
    if (!shown.files) hide(["new", "open", "save"]);
    if (!shown.language) hide(["lang"]);
    if (!shown.rooms) hide(["theme"]);
    if (!shown.server) hide(["server"]);

    // No engine at all: nothing that would ask it.
    if (declaredServer && seams.server == null) {
      for (const node of [ui.dot, ui.live, ui.liveStatus]) if (node) node.hidden = true;
      hide(["pdf"]);
    }

    // The interface's language is the page's - one dictionary, in the core. A host may
    // declare it; otherwise the face holding the picker reads back what was last picked,
    // which until 20 October 2026 it stored and never read.
    if (seams.lang) setLang(seams.lang);
    else if (shown.language) {
      try { const kept = localStorage.getItem("frisket.lang"); if (kept) setLang(kept); } catch {}
    }

    const zoomSelect = byId("zoom");
    if (zoomSelect) {
      zoomSelect.value = zoomChoice;
      zoomSelect.onchange = () => {
        zoomChoice = zoomSelect.value;
        try { localStorage.setItem("frisket.zoom", zoomChoice); } catch {}
        applyZoom();
      };
    }

    const stageZoomSelect = byId("stage-zoom");
    if (stageZoomSelect) {
      stageZoomSelect.value = stageZoom;
      stageZoomSelect.onchange = () => {
        stageZoom = stageZoomSelect.value;
        try { localStorage.setItem("frisket.zoom.stage", stageZoom); } catch {}
        applyZoom();
      };
    }

    const detachButton = byId("detach");
    if (detachButton) detachButton.onclick = detachPreview;

    if (ui.onlyFaults)
      ui.onlyFaults.onchange = () => { onlyFaults = ui.onlyFaults.checked; refresh(); };

    // Both default to ticked, which is the richest output and therefore the one that
    // loses nothing: a designer that warns about a loss nobody asked for teaches people
    // to ignore the panel.
    for (const box of [ui.targetTagged, ui.targetInteractive])
      if (box) box.onchange = () => refresh();

    // --- the engine's pages, kept up to date -----------------------------------------
    //
    // Asked for again a moment after the template stops changing, and shown where the person
    // wants them: on the canvas instead of the approximation, beside it, or in a window of
    // their own on another screen. Three places for three ways of working - a laptop, a wide
    // screen, two screens - which is not a property of the document, so it is not the host's
    // to fix for good either: the host says where it starts, the person's choice is kept.
    // The approximation stays what is clicked to select; the engine's pages are what the
    // printer will do.
    //
    // A change made while the engine is drawing does not wait for it: the newest request is
    // the one whose answer is shown, and an older answer arriving later is dropped. A template
    // that has not changed is not sent again, so a redraw for a font, a room or a piece that
    // changed nothing costs no round trip. And a failure is said in the header, never in an
    // alert, which would come up on every pause while the server is down.
    const LIVE = ["off", "canvas", "beside", "window"];
    const liveDelay = Number.isFinite(seams.liveDelay) ? seams.liveDelay : 500;
    const liveOr = (mode, fallback) => LIVE.includes(mode) ? mode : fallback;

    let live = liveOr(seams.live, "off");
    try { live = liveOr(localStorage.getItem("frisket.live"), live); } catch {}

    let livePages = null;       // the engine's last pages, null before its first answer
    let liveState = "idle";     // "idle" | "asking" | "failed"
    let liveFailure = "";
    let liveTimer = null;
    let liveGeneration = 0;     // which call to askEngine is the newest
    let liveAsked = 0;          // which request sent is the newest
    let liveSent = null;        // the body last sent: an unchanged one is not sent again

    /// Where the engine is, or null when there is none to ask: the host's when it declared
    /// one - "/" is this page's own origin - and otherwise whatever the field says.
    function serverBase() {
      if (declaredServer)
        return seams.server == null ? null : String(seams.server).trim().replace(/\/+$/, "");
      const typed = ui.server ? String(ui.server.value || "").trim() : "";
      return typed ? typed.replace(/\/+$/, "") : null;
    }

    /// How it is reached: the host's fetch when it gave one, to add its own credentials.
    const send = (url, init) => seams.fetch ? seams.fetch(url, init) : fetch(url, init);

    const liveOn = () => live !== "off" && serverBase() !== null;

    /// Called on every redraw; sends nothing until the template has been still a moment.
    function scheduleLive() {
      if (!liveOn()) return;
      clearTimeout(liveTimer);
      liveTimer = setTimeout(askEngine, liveDelay);
    }

    async function askEngine() {
      const generation = ++liveGeneration;
      let body;
      try { body = await payload(); } catch { return; }
      if (generation !== liveGeneration || !liveOn()) return;

      const key = JSON.stringify(body);
      if (key === liveSent) return;   // asked already - answered, or on its way
      liveSent = key;
      const asked = ++liveAsked;
      liveState = "asking";
      showLive();

      try {
        const result = await (await ask("/preview", body)).json();
        if (asked !== liveAsked) return;
        livePages = result.pages || [];
        liveState = "idle";
        setServerState(true);
      } catch (err) {
        if (asked !== liveAsked) return;
        liveState = "failed";
        liveFailure = (err && err.message) || String(err);
        liveSent = null;              // the same template is worth asking again
        setServerState(false);
      }
      showLive();
    }

    /// Where the pages go now, and asked for at once rather than after a pause.
    function setLive(mode) {
      live = liveOr(mode, "off");
      try { localStorage.setItem("frisket.live", live); } catch {}
      if (ui.live) ui.live.value = live;
      if (live === "window") detachPreview();
      applyShape();
      paint();
      clearTimeout(liveTimer);
      if (liveOn()) askEngine();
    }

    function liveStatusText() {
      if (!liveOn()) return "";
      if (live === "window" && !(previewWindow && !previewWindow.closed)) return t("live.windowClosed");
      if (liveState === "failed") return t("live.failed", { message: liveFailure });
      if (liveState === "asking" || !livePages) return t("live.asking");
      return t("live.ready", { count: livePages.length });
    }

    /// The engine's pages as the canvas, the stage and the window all draw them - or what is
    /// happening instead, since a page that did not come is not a page to show.
    function enginePages() {
      if (!livePages || liveState === "failed")
        return `<p class="stamp">${esc(liveStatusText())}</p>`;
      return livePages
        .map((svg, i) => `<figure class="sheet">${svg}<figcaption>`
                       + esc(t("live.page", { page: i + 1, pages: livePages.length }))
                       + "</figcaption></figure>")
        .join("");
    }

    /// Everything that shows the engine's pages, and the line that says how they stand.
    function showLive() {
      if (ui.liveStatus) ui.liveStatus.textContent = liveStatusText();
      if (live === "canvas") paint();
      else { fillStage(); applyZoom(); pushPreview(); }
    }

    // --- the host's library -----------------------------------------------------
    //
    // A sub-document is a template the host keeps - an article of law, the recipient's
    // address block, a letterhead - and this one names it st://reference, so that changing
    // it once changes every document that names it. The engine asks ISubTemplates for it
    // as it composes; this page asks the same host the same two questions:
    //
    //   list()           what can be inserted: [{ reference, title }], or a promise of it
    //   find(reference)  the template - JSON text or an object - or a promise of it; null
    //                    or undefined for a reference the host does not know
    //
    // What was found is kept, so a keystroke does not ask again, and a piece that arrives
    // later redraws the page. None of it is saved: the file keeps the reference
    // (forEngine), and the exact preview is sent the pieces carried (carryReferences),
    // because the engine behind it is not the host and cannot look a name up.
    let subDocuments = seams.subDocuments || null;
    let library = [];
    const pieces = new Map();   // reference -> { state: "asked" | "ready" | "missing", template, promise }

    /// The lookup layout() is handed: the template when it is known, undefined while it is
    /// not. Asking is a side effect of drawing, once per reference.
    function lookupPiece(reference) {
      if (!reference) return undefined;
      const known = pieces.get(reference);
      if (known) return known.state === "ready" ? known.template : undefined;
      if (!subDocuments || typeof subDocuments.find !== "function") return undefined;

      const entry = { state: "asked", template: null, promise: null };
      const asked = subDocuments;
      pieces.set(reference, entry);
      const settle = found => {
        try { entry.template = asTemplate(found); } catch { entry.template = null; }
        entry.state = entry.template ? "ready" : "missing";
      };

      let answer;
      try { answer = asked.find(reference); } catch { answer = null; }

      // A host holding its pieces in memory answers at once, and is drawn at once.
      if (!(answer && typeof answer.then === "function")) {
        settle(answer);
        return entry.template || undefined;
      }

      // One behind a network answers later, and the page is drawn again when it does -
      // unless the library was replaced meanwhile, in which case this answer is not one.
      entry.promise = Promise.resolve(answer).then(settle, () => settle(null)).then(() => {
        if (subDocuments === asked && pieces.get(reference) === entry) refresh();
      });
      return undefined;
    }

    /// The same, without asking: what the exact preview may carry once the asking is done.
    const knownPiece = reference => {
      const known = pieces.get(reference);
      return known && known.state === "ready" ? known.template : undefined;
    };

    /// Asks for every piece the template names, pieces inside pieces included, and waits
    /// for the answers. Before the exact preview: a piece still on its way would reach the
    /// engine as a name it cannot look up, and the page it drew would be less than this one.
    async function settlePieces() {
      for (let round = 0; round < 16; round++) {
        const named = new Set();
        const walk = node => {
          for (const child of node.Childs || []) {
            const reference = child.$type === "SubTemplateItem" ? referenceOf(child) : null;
            if (reference) {
              named.add(reference);
              const inner = knownPiece(reference);
              if (inner) walk({ Childs: inner.Sections || [] });
            } else walk(child);
          }
        };
        walk({ Childs: report.Sections || [] });
        if (report.BackPage && report.BackPage.Content) walk(report.BackPage.Content);

        named.forEach(lookupPiece);
        const waiting = [...named].map(r => pieces.get(r)).filter(e => e && e.state === "asked");
        if (!waiting.length) return;
        await Promise.all(waiting.map(e => e.promise));
      }
    }

    /// Asks the host what it offers, and lays the answer out as buttons.
    function loadLibrary() {
      const asked = subDocuments;
      library = [];
      const take = listed => {
        if (subDocuments !== asked) return;
        library = (Array.isArray(listed) ? listed : []).map(entry => {
          const raw = typeof entry === "string" ? entry : (entry && entry.reference) || "";
          const reference = String(raw).trim().replace(/^st:\/\//i, "").trim();
          const title = (entry && typeof entry === "object" && entry.title) || reference;
          return { reference, title: String(title) };
        }).filter(entry => entry.reference);
        drawLibrary();
      };

      drawLibrary();
      if (!asked || typeof asked.list !== "function") return;
      let answer;
      try { answer = asked.list(); } catch { return; }
      if (answer && typeof answer.then === "function")
        Promise.resolve(answer).then(listed => { take(listed); refresh(); }, () => {});
      else take(answer);
    }

    /// The library family of the palette: one button per piece, hidden while there is none.
    function drawLibrary() {
      const family = byId("libraryFamily"), palette = byId("libraryPalette");
      if (!family || !palette) return;
      palette.innerHTML = "";
      for (const entry of library) {
        const button = document.createElement("button");
        button.textContent = entry.title;
        button.title = "st://" + entry.reference;
        button.onclick = () => addToSelection(pieceItem(entry.reference));
        palette.appendChild(button);
      }
      family.hidden = palette.hidden = library.length === 0;
    }

    /// A sub-template naming a piece of the library: what its palette button inserts.
    function pieceItem(reference) {
      const item = newItem("SubTemplateItem");
      item.TemplatePath = "st://" + reference;
      return item;
    }

    /// Hands over another library, or the same one once its content has changed: what was
    /// found is forgotten, and asked again as the page is drawn.
    function setSubDocuments(next) {
      subDocuments = next || null;
      pieces.clear();
      loadLibrary();
      refresh();
    }

    /// The outermost sub-template whose content `item` was drawn from, or null. A piece's
    /// content is copied in afresh on every redraw, so selecting part of it would select a
    /// copy the next keystroke throws away, and editing it would edit nothing: what is
    /// selected, flagged or clicked inside a piece is the piece.
    function pieceHolding(item) {
      const roots = [...(report.Sections || [])];
      if (report.BackPage && report.BackPage.Content) roots.push(report.BackPage.Content);
      const walk = (node, piece) => {
        for (const child of node.Childs || []) {
          if (child === item) return piece;
          const found = walk(child, piece || (holdsPiece(child) ? child : null));
          if (found !== undefined) return found;
        }
        return undefined;
      };
      for (const root of roots) {
        const found = walk(root, null);
        if (found !== undefined) return found;
      }
      return null;
    }

    let report = blankReport();

    // The section being edited. A report holds a list of sections - that is what mixes
    // page sizes in one file - and this page edits the first of them: everything it draws
    // and every control it offers belongs to one section. Reports with several are laid
    // out correctly by the engine and shown here as their first, which is honest about
    // what this preview can and cannot say.
    // Which section the page is showing. Everything - tree, canvas, properties - reads
    // this, so switching section is a matter of what this returns rather than a change at
    // fifteen call sites. Clamped rather than trusted: a section can be removed while it
    // is the one being edited.
    let sectionIndex = 0;

    /// Opens a section. Clamped here rather than trusted, for the same reason doc0
    /// clamps: a section can be removed while it is the one being edited.
    function setSectionIndex(at) {
      sectionIndex = Math.max(0, Math.min(at, report.Sections.length - 1));
    }

    function doc0() {
      if (sectionIndex >= report.Sections.length) sectionIndex = report.Sections.length - 1;
      if (sectionIndex < 0) sectionIndex = 0;
      return report.Sections[sectionIndex];
    }

    let selected = doc0();

    // Which tree is being edited. The right-hand properties panel is shared, so the
    // two sides never need their own copy of anything but the root they hang from.
    let tab = "doc";

    function backPage() { return report.BackPage || null; }

    /// The container the palette adds to and the tree shows.
    function activeRoot() {
      const back = backPage();
      return tab === "back" && back ? back.Content : doc0();
    }

    /// The back laid out as a page of its own: no header, no footer, no band. The
    /// engine sets the content over the padded sheet, which a Fill dock reproduces.
    function backAsPage(rep) {
      const back = rep.BackPage;
      const doc = {
        $type: "Section", Layout: "Absolute", Style: newStyle(),
        Childs: [Object.assign({}, back.Content)]
      };
      doc.Style.Padding = Object.assign(edges(12, 12, 12, 12), back.Padding);
      doc.Childs[0].Style = Object.assign(newStyle(), back.Content.Style);
      doc.Childs[0].Style.Location = Object.assign(newStyle().Location, back.Content.Style.Location,
                                                   { Dock: "Fill" });
      // A report of one section whose only content is the back, and carrying the sheet
      // size of the section it belongs behind.
      doc.Dimension = Object.assign({ Width: 210, Height: 297 },
                                    (rep.Sections && rep.Sections[sectionIndex]
                                     || rep.Sections && rep.Sections[0] || {}).Dimension);
      const shown = Object.assign({}, rep, { Sections: [doc], Watermark: rep.Watermark });
      delete shown.Section;
      delete shown.Dimension;
      return shown;
    }

    function bandOf(type) {
      return (doc0().Childs || []).find(c => c.$type === type) || null;
    }

    function parentOf(item, node = activeRoot()) {
      for (const c of node.Childs || []) {
        if (c === item) return node;
        const found = parentOf(item, c);
        if (found) return found;
      }
      return null;
    }

    // --- what is wrong with this template --------------------------------------
    //
    // An echo of TemplateValidator on the engine side, and held to it by the
    // headless harness: the same template must give the same findings on both, or
    // the designer is reassuring somebody about a section that will be refused.
    //
    // Everything here is something an outside tool reports much later - veraPDF, a
    // screen reader, a scanner - by which time whoever could fix it has moved on.

    /// The findings of the last pass, and the elements they concern. Recomputed at
    /// each refresh, read by the tree as it draws.
    let issues = [];
    let faulty = new Map();

    function runValidator() {
      issues = validate(report, {
        sound: true, accessible: true, capable: true,
        target: {
          Tagged: !ui.targetTagged || ui.targetTagged.checked,
          Interactive: !ui.targetInteractive || ui.targetInteractive.checked
        }
      });
      faulty = new Map();
      for (const issue of issues) {
        if (!issue.item) continue;
        // `where` still names the place inside the piece; the item is what can be reached.
        issue.item = pieceHolding(issue.item) || issue.item;
        const level = faulty.get(issue.item);
        faulty.set(issue.item, level === "error" ? level : issue.level);
      }
      // To whoever embedded this, as well as to the panel. A host is told what is wrong
      // with the template its user is building - section 5c's rule, from the other side.
      if (seams.onChecks) seams.onChecks(issues);
    }

    /// True when this element, or anything under it, has a fault. What lets the
    /// filter hide a clean branch without hiding the path to a dirty one.
    function holdsFault(item) {
      if (faulty.has(item)) return true;
      return (item.Childs || []).some(holdsFault);
    }

    /// The list under the tree. Clicking a line selects the element it names, which
    /// is the whole point of the panel: a fault you cannot reach is a fault you
    /// will not fix.
    function drawIssues() {
      const host = byId("issues");
      if (!host) return;
      host.innerHTML = "";

      const errors = issues.filter(i => i.level === "error").length;
      const summary = byId("issueCount");
      if (summary) {
        summary.textContent = issues.length
          ? t("issues.count", { errors, warnings: issues.length - errors })
          : t("issues.none");
        summary.className = "issue-count" + (errors ? " bad" : issues.length ? " warn" : " good");
      }

      for (const issue of issues) {
        const li = document.createElement("li");
        li.className = "issue " + issue.level;
        li.innerHTML = `<span class="dot"></span><span><b>${esc(issue.where)}</b><br>${esc(issue.message)}</span>`;
        if (issue.item) li.onclick = () => { selected = issue.item; refresh(); };
        else li.onclick = () => { selected = doc0(); refresh(); };
        host.appendChild(li);
      }
    }

    function drawTree() {
      const host = tab === "back" ? byId("backTree") : ui.tree;
      if (!host) return;
      host.innerHTML = "";

      // The back is a page of its own and has no sections; it still hangs from its
      // content, which is what it is.
      if (tab === "back") { host.appendChild(treeNode(activeRoot())); return; }

      // Everywhere else the tree roots at the report, with the sections as its children.
      // Selecting the report then shows the file's own fields through the same mechanism
      // as everything else - no special heading, no exception - and selecting a section
      // shows only the section's. It also makes the shape of the file visible, where it
      // used to be legible only through a button counting "Sections (2 of 3)".
      host.appendChild(reportNode());
    }

    /// The report itself, and one child per section.
    ///
    /// Not a ContainerItem: a report holds Sections rather than Childs, so this is built
    /// here rather than by treeNode. That is the honest shape - the report is not an item
    /// on a page, it is the file the pages are in.
    function reportNode() {
      const li = document.createElement("li");
      const row = document.createElement("div");
      row.className = "node" + (selected === report ? " sel" : "");
      row.innerHTML = `<span>${esc(report.Title || t("tree.untitled"))}</span>`
                    + `<span class="kind">${esc(t("tree.report"))}</span>`;
      row.onclick = e => { e.stopPropagation(); selected = report; refresh(); };
      li.appendChild(row);

      const kids = document.createElement("ul");
      report.Sections.forEach((section, i) => {
        // Selecting a section is also how one is opened, which is what the dialog was
        // for. The canvas still draws exactly one, which is right and should stay.
        const node = treeNode(section, () => { setSectionIndex(i); });
        const first = node.querySelector(".node");
        if (first) {
          const size = section.Dimension || {};
          const label = first.querySelector(".kind");
          if (label) label.textContent = `${t("tree.section")} ${i + 1}`
                                       + (size.Width ? ` · ${size.Width}×${size.Height}` : "");
          if (i === sectionIndex) first.classList.add("open");
        }
        kids.appendChild(node);
      });

      li.appendChild(kids);
      return li;
    }
    // The item being dragged in the tree. Kept here rather than in the drag payload:
    // dataTransfer only carries strings, and these are live objects.
    let dragging = null;

    /// Whether the tree shows only the branches that lead to a fault.
    let onlyFaults = false;

    /// Moves `item` out of `from` and into `parent` at `index`.
    ///
    /// Removing it first shifts everything after it, so an index in the same parent
    /// and below the old position is then one too far. Without the adjustment a drag
    /// downwards lands one short while a drag upwards looks right - which is exactly
    /// how the mistake survives being tried once. Top-level so the headless harness
    /// can exercise it.
    function moveChild(from, item, parent, index) {
      const at = from.Childs.indexOf(item);
      if (at < 0) return;
      from.Childs.splice(at, 1);
      if (from === parent && at < index) index--;
      parent.Childs.splice(index, 0, item);
    }

    /// True when moving `item` into `host` would put it inside itself.
    function contains(item, host) {
      if (item === host) return true;
      return (item.Childs || []).some(c => contains(c, host));
    }

    function treeNode(item, onChosen) {
      const li = document.createElement("li");
      const row = document.createElement("div");
      row.className = "node" + (item === selected ? " sel" : "");
      const conditional = isConditional(item) ? '<span class="cond" title="' +
            esc((item.ShowWhen ? "= " + item.ShowWhen : "") +
                (item.HideWhen ? " ≠ " + item.HideWhen : "")) + '">?</span>' : "";
      const fault = faulty.get(item);
      const mark = fault ? `<span class="mark ${fault}" title="${esc(faultsOn(item))}">\u25CF</span>` : "";
      row.innerHTML = `<span>${esc(nameOf(item))}${conditional}${mark}</span>`
                    + `<span class="kind">${esc(labelOf(item.$type))}</span>`;
      row.onclick = e => {
        e.stopPropagation();
        if (onChosen) onChosen();
        selected = item;
        refresh();
      };

      // --- reparenting by dragging -----------------------------------------------
      // The section root and the three bands are structure, not content: they stay
      // where they are. Everything else can move into any container.
      const movable = item !== activeRoot() && parentOf(item) !== null
                   && !["Header", "Footer", "Body"].includes(item.$type);

      if (movable) {
        row.draggable = true;
        row.ondragstart = e => {
          dragging = item;
          e.dataTransfer.effectAllowed = "move";
          e.dataTransfer.setData("text/plain", nameOf(item));
          row.classList.add("dragging");
          e.stopPropagation();
        };
        row.ondragend = () => { dragging = null; row.classList.remove("dragging"); refresh(); };
      }

      // Where a drop on this row would land. The top and bottom thirds insert beside
      // it, the middle drops inside - which is the convention every file tree uses,
      // and it needs no extra elements: the row is measured, not subdivided.
      //
      // Order is not decoration. A band laid out in Stack flows its children in this
      // order, so moving a row here moves a block down the page; in Absolute it
      // decides what is drawn over what.
      function targetOf(e) {
        if (!dragging || dragging === item) return null;

        const r = row.getBoundingClientRect();
        const third = (e.clientY - r.top) / (r.height || 1);
        const structural = item === activeRoot() || ["Header", "Footer", "Body"].includes(item.$type);

        // Nothing may sit beside a band, so those only ever accept a drop inside.
        let where = structural ? "in"
                  : third < 0.3 ? "before"
                  : third > 0.7 ? "after"
                  : item.Childs ? "in" : (third < 0.5 ? "before" : "after");

        if (where === "in") {
          // A sub-template's content is another template's, and is dropped on saving.
          if (!item.Childs || item.$type === "SubTemplateItem" || contains(dragging, item)) return null;
          return { where, parent: item, index: item.Childs.length };
        }

        const parent = parentOf(item);
        if (!parent || contains(dragging, parent)) return null;
        return { where, parent, index: parent.Childs.indexOf(item) + (where === "after" ? 1 : 0) };
      }

      const clearDrop = () => row.classList.remove("drop-in", "drop-before", "drop-after");

      row.ondragover = e => {
        const t = targetOf(e);
        clearDrop();
        if (!t) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = "move";
        row.classList.add("drop-" + t.where);
      };
      row.ondragleave = clearDrop;

      row.ondrop = e => {
        const t = targetOf(e);
        clearDrop();
        if (!t) return;
        e.preventDefault();
        e.stopPropagation();

        const from = parentOf(dragging);
        if (!from) return;

        moveChild(from, dragging, t.parent, t.index);
        selected = dragging;
        dragging = null;
        refresh();
      };

      li.appendChild(row);
      // What a piece holds belongs to another template, and is not offered for editing.
      if (item.Childs && item.Childs.length && !holdsPiece(item)) {
        const ul = document.createElement("ul");

        // With the filter on, a branch is kept only if it leads to a fault. The
        // ancestors stay: an element shown with no path to it cannot be reached, and
        // the point of the filter is to walk straight to the thing that is wrong.
        item.Childs.filter(c => !onlyFaults || holdsFault(c))
                   .forEach(c => ul.appendChild(treeNode(c)));
        li.appendChild(ul);
      }
      return li;
    }

    /// Everything said about one element, for the marker's tooltip.
    function faultsOn(item) {
      return issues.filter(i => i.item === item).map(i => i.message).join("\n");
    }

    /// A line of explanation, styled like the hints elsewhere.
    function note(text) {
      const div = document.createElement("div");
      div.className = "hint";
      div.style.margin = "0";
      div.textContent = text;
      return div;
    }

    function field2(label, node) {
      const wrap = document.createElement("div");
      wrap.className = "field";
      const l = document.createElement("label"); l.textContent = label;
      const v = document.createElement("div"); v.className = "val"; v.appendChild(node);
      wrap.append(l, v);
      return wrap;
    }
    function input(type, value, onChange, attrs = {}) {
      const el = document.createElement("input");
      el.type = type;
      if (type === "checkbox") el.checked = !!value; else el.value = value ?? "";
      Object.assign(el, attrs);
      el.oninput = () => onChange(type === "checkbox" ? el.checked : el.value);
      return el;
    }
    /// A list of model values. `group` names the enum they belong to, and only the
    /// labels are translated - the value written into the template never is.
    function select(options, value, onChange, group) {
      const el = document.createElement("select");
      for (const o of options) {
        const opt = document.createElement("option");
        opt.value = o;
        opt.textContent = group ? tv(group, o) : o;
        el.appendChild(opt);
      }
      el.value = value;
      el.onchange = () => onChange(el.value);
      return el;
    }
    function measurePair(loc, key) {
      const wrap = document.createElement("div");
      wrap.className = "val";
      wrap.appendChild(input("number", has(loc[key + "Value"]) ? num(loc[key + "Value"]) : "",
        v => { loc[key + "Value"] = v === "" ? "NaN" : Number(v); refresh(); }, { step: 0.5 }));
      wrap.appendChild(select(["Millimeter", "Percentage"], loc[key + "Unit"],
        v => { loc[key + "Unit"] = v; refresh(); }, "Units"));
      return wrap;
    }
    function edgeGroup(target) {
      const grid = document.createElement("div");
      grid.className = "edges";
      for (const side of ["Left", "Top", "Right", "Bottom"]) {
        const cell = document.createElement("div");
        const tag = document.createElement("span"); tag.textContent = side.slice(0, 1).toLowerCase();
        cell.appendChild(tag);
        cell.appendChild(input("number", target[side], v => { target[side] = Number(v) || 0; refresh(); }, { step: 0.5 }));
        grid.appendChild(cell);
      }
      return grid;
    }

    function drawProps() {
      const p = ui.props;

      // Editing a value refreshes the section, and refreshing rebuilt this panel from
      // scratch - destroying the very field being typed into. The first keystroke went
      // in, focus was lost, and the rest went nowhere: a height would not move off the
      // value it started at, and a run's text - a textarea, not an input - lost every
      // keystroke but the first. So while a text field or a run's textarea has the
      // focus, the panel is left alone; it is rebuilt on the next refresh after focus
      // moves away.
      const editing = uiRoot.activeElement;
      if (editing && p.contains(editing)
          && (editing.tagName === "TEXTAREA" || (editing.tagName === "INPUT" && editing.type !== "checkbox")))
        return;

      p.innerHTML = "";
      const item = selected;
      if (!item) return;

      const title = document.createElement("div");
      title.style.cssText = "font-weight:600;margin-bottom:8px";
      // A report has no $type - it is the file, not an item in it - so labelOf would put
      // a missing key on the panel. Named for what it is instead.
      title.textContent = item === report ? t("tree.report") : labelOf(item.$type);
      p.appendChild(title);

      if (item.$type === "TextItem") {
        p.appendChild(field2("Texte", input("text", item.Text, v => { item.Text = v; refresh(); })));
        p.appendChild(field2(t("field.font"), input("text", item.Font.Family, v => { item.Font.Family = v; refresh(); })));
        const size = document.createElement("div"); size.className = "val";
        size.appendChild(input("number", item.Font.Size, v => { item.Font.Size = Number(v) || 0; refresh(); }, { step: 0.25 }));
        size.appendChild(select(["Rem", "Millimeter", "Point", "PercentOfFrame"], item.Font.SizeUnit,
          v => { item.Font.SizeUnit = v; refresh(); }, "FontSizeUnit"));
        p.appendChild(field2(t("field.size"), size));
        const style = document.createElement("div"); style.className = "val";
        for (const [key, tag] of [["Bold", t("style.bold")], ["Italic", t("style.italic")], ["Underline", t("style.underline")], ["Strikethrough", t("style.strikethrough")]]) {
          const b = document.createElement("label");
          b.style.cssText = "display:flex;gap:3px;align-items:center;color:var(--muted)";
          b.append(input("checkbox", item.Font[key], v => { item.Font[key] = v; refresh(); }), tag);
          style.appendChild(b);
        }
        p.appendChild(field2(t("field.style"), style));
        p.appendChild(field2(t("field.alignH"), select(["Left", "Center", "Right", "Justify"], item.Font.HorizontalAlignment,
          v => { item.Font.HorizontalAlignment = v; refresh(); }, "HorizontalAlignment")));

        // These three were in the model and in the echo, and nowhere in the panel: a
        // vertical alignment and a line spacing that the preview honoured and no one
        // could set.
        p.appendChild(field2(t("field.alignV"), select(["Top", "Middle", "Bottom"],
          item.Font.VerticalAlignment || "Top",
          v => { item.Font.VerticalAlignment = v; refresh(); }, "VerticalAlignment")));

        p.appendChild(field2(t("field.lineSpacing"), input("number",
          item.Font.LineSpacing ?? "", v => {
            item.Font.LineSpacing = v === "" ? null : Number(v);
            refresh();
          }, { step: 0.05, min: 0.5, placeholder: "1.5" })));

        p.appendChild(field2(t("field.wrap"), input("checkbox", item.Font.Wrap !== false,
          v => { item.Font.Wrap = v; refresh(); })));
        p.appendChild(field2(t("field.colour"), input("color", hex(item.Font.Color), v => {
          item.Font.Color = { R: parseInt(v.slice(1, 3), 16), G: parseInt(v.slice(3, 5), 16), B: parseInt(v.slice(5, 7), 16), A: 255 };
          refresh();
        })));
      }

      if (item.$type === "LineItem") {
        p.appendChild(field2(t("field.direction"), select(["Horizontal", "Vertical", "DiagonalDown", "DiagonalUp"], item.Orientation,
          v => { item.Orientation = v; refresh(); }, "LineOrientation")));
        p.appendChild(field2(t("field.thickness"), input("number", item.Pen.Width, v => { item.Pen.Width = Number(v) || 0.1; refresh(); }, { step: 0.05 })));
        p.appendChild(field2("Trait", select(["Solid", "Dash", "Dot", "DashDot"], item.Pen.DashStyle, v => { item.Pen.DashStyle = v; refresh(); }, "DashStyle")));
      }

      if (item.$type === "ImageItem") {
        p.appendChild(field2(t("field.naturalWidth"), input("number", item.NaturalWidth, v => { item.NaturalWidth = Number(v) || 0; refresh(); })));
        p.appendChild(field2(t("field.naturalHeight"), input("number", item.NaturalHeight, v => { item.NaturalHeight = Number(v) || 0; refresh(); })));
        p.appendChild(field2(t("field.scaling"), select(["Uniform", "Stretch", "None"], item.Scaling, v => { item.Scaling = v; refresh(); }, "ImageScaling")));
        p.appendChild(field2(t("field.sourceField"), input("text", item.SourceField ?? "",
          v => { item.SourceField = v || null; refresh(); })));
        p.appendChild(field2("", note(t("hint.sourceField"))));

        const pick = document.createElement("input");
        pick.type = "file"; pick.accept = "image/*";
        pick.onchange = () => {
          const f = pick.files[0]; if (!f) return;
          const reader = new FileReader();
          reader.onload = () => {
            item.Source = { Data: String(reader.result).split(",")[1], SourcePath: f.name, MediaType: f.type || "image/png" };
            const probe = new Image();
            probe.onload = () => { item.NaturalWidth = probe.width / 3.78; item.NaturalHeight = probe.height / 3.78; refresh(); };
            probe.src = String(reader.result);
          };
          reader.readAsDataURL(f);
        };
        p.appendChild(field2(t("field.file"), pick));
      }

      if (item.$type === "BarcodeItem") {
        p.appendChild(field2(t("field.value"), input("text", item.Value ?? "",
          v => { item.Value = v; refresh(); })));
        p.appendChild(field2(t("field.kind"), select(["Qr", "Code128"], item.Symbology || "Qr",
          v => { item.Symbology = v; refresh(); }, "Symbology")));

        if ((item.Symbology || "Qr") === "Qr") {
          p.appendChild(field2(t("field.correction"), select(["Low", "Medium", "Quartile", "High"],
            item.Correction || "Medium", v => { item.Correction = v; refresh(); }, "QrErrorCorrection")));
        } else {
          p.appendChild(field2(t("field.showText"), input("checkbox", item.ShowText !== false,
            v => { item.ShowText = v; refresh(); })));
        }

        p.appendChild(field2(t("field.quietZone"), input("number", item.QuietZone ?? 0,
          v => { item.QuietZone = Number(v) || 0; refresh(); }, { min: 0 })));
      }

      if (item.$type === "FieldItem") {
        p.appendChild(field2(t("field.name"), input("text", item.Name, v => { item.Name = v; refresh(); })));
        p.appendChild(field2(t("field.kind"), select(["Text", "Checkbox", "Signature"], item.Kind,
          v => { item.Kind = v; refresh(); }, "FieldKind")));
        p.appendChild(field2(t("field.value"), input("text", item.Value ?? "",
          v => { item.Value = v.length ? v : null; refresh(); })));
        p.appendChild(field2(t("field.tooltip"), input("text", item.Tooltip ?? "",
          v => { item.Tooltip = v.length ? v : null; refresh(); })));

        // ByHand draws a ruled box and writes no form field at all; Digital writes one, and
        // a reader that supports signing then asks for a certificate.
        if (item.Kind === "Signature") {
          p.appendChild(field2(t("field.signing"), select(["ByHand", "Digital"], item.Signing || "ByHand",
            v => { item.Signing = v; refresh(); }, "Signing")));
        }

        if (item.Kind === "Text") {
          p.appendChild(field2(t("field.multiline"), input("checkbox", item.Multiline,
            v => { item.Multiline = v; refresh(); })));
          p.appendChild(field2(t("field.maxLength"), input("number", item.MaxLength,
            v => { item.MaxLength = Number(v) || 0; refresh(); })));
        }

        const flags = document.createElement("div");
        flags.className = "val";
        for (const [key, label] of [["ReadOnly", t("option.readOnly")], ["Required", t("option.required")]]) {
          const l = document.createElement("label");
          l.style.cssText = "display:flex;gap:3px;align-items:center;color:var(--muted)";
          l.append(input("checkbox", item[key], v => { item[key] = v; refresh(); }), label);
          flags.appendChild(l);
        }
        p.appendChild(field2(t("field.options"), flags));

        const outline = document.createElement("div");
        outline.className = "val";
        outline.append(input("checkbox", !!item.Outline, v => {
          item.Outline = v ? { Color: { R: 90, G: 90, B: 90, A: 255 }, Width: .2, DashStyle: "Solid" } : null;
          refresh();
        }));
        if (item.Outline)
          outline.append(input("number", item.Outline.Width,
            v => { item.Outline.Width = Number(v) || .1; refresh(); }, { step: .05 }));
        p.appendChild(field2(t("field.outline"), outline));
      }

      if (item.$type === "TableItem") {
        p.appendChild(field2(t("field.repeatHeader"), input("checkbox", item.RepeatHeader, v => { item.RepeatHeader = v; refresh(); })));
        const cols = document.createElement("div");
        (item.Columns || []).forEach((c, i) => {
          const row = document.createElement("div"); row.className = "val"; row.style.marginBottom = "4px";
          row.appendChild(input("number", has(c.Width) ? num(c.Width) : "",
            v => { c.Width = v === "" ? "NaN" : Number(v); refresh(); }, { step: 1, placeholder: "auto" }));
          row.appendChild(select(["Millimeter", "Percentage"], c.WidthUnit, v => { c.WidthUnit = v; refresh(); }, "Units"));
          cols.appendChild(field2(t("field.column") + " " + (i + 1), row));
        });
        p.appendChild(cols);
        const bar = document.createElement("div"); bar.className = "val";
        bar.append(
          button("+ colonne", () => {
            item.Columns.push({ Width: "NaN", WidthUnit: "Millimeter" });
            (item.Childs || []).forEach(r => r.Childs.push(Object.assign(newItem("TextItem"), { Text: "" })));
            refresh();
          }),
          button("+ ligne", () => {
            item.Childs.push(tableRow(item.Columns.map(() => "valeur"), false));
            refresh();
          }));
        p.appendChild(bar);
      }

      if (item.$type === "ImageGridItem") {
        p.appendChild(field2(t("field.gridColumns"), input("number", item.GridColumns,
          v => { item.GridColumns = Math.max(1, Number(v) || 1); refresh(); }, { min: 1, step: 1 })));
        p.appendChild(field2(t("field.rowHeight"), input("number", item.RowHeight,
          v => { item.RowHeight = Number(v) || 0; refresh(); }, { step: 1, min: 0 })));
        p.appendChild(field2(t("field.gap"), input("number", item.Gap,
          v => { item.Gap = Math.max(0, Number(v) || 0); refresh(); }, { step: 0.5, min: 0 })));

        const bar = document.createElement("div"); bar.className = "val";
        bar.appendChild(button("+ image", () => { item.Childs.push(newItem("ImageItem")); refresh(); }));
        p.appendChild(field2("", bar));
      }

      if (item.$type === "ImportedPageItem") {
        p.appendChild(field2("", note(t("hint.importedPage"))));

        // Embedding it, the same gesture and the same reason as embedding a sub-template:
        // a path is resolved by whoever has the file, which is never this page.
        const carry = document.createElement("input");
        carry.type = "file";
        carry.accept = "application/pdf,.pdf";
        carry.onchange = () => {
          const chosen = carry.files && carry.files[0];
          if (!chosen) return;
          const reader = new FileReader();
          reader.onload = () => {
            const bytes = new Uint8Array(reader.result);
            let binary = "";
            for (const b of bytes) binary += String.fromCharCode(b);
            item.Data = btoa(binary);
            item.SourcePath = "";
            refresh();
          };
          reader.readAsArrayBuffer(chosen);
        };
        p.appendChild(field2(t("field.importedPdf"), carry));

        p.appendChild(field2(t("field.templatePath"), input("text", item.SourcePath ?? "",
          v => { item.SourcePath = v; if (v) item.Data = ""; refresh(); },
          { placeholder: "terms.pdf" })));

        p.appendChild(field2(t("field.importedPage"), input("number", item.Page ?? 1,
          v => { item.Page = Math.max(1, parseInt(v, 10) || 1); refresh(); })));

        p.appendChild(field2(t("field.scaling"), select(
          ["Stretch", "Uniform", "None"], item.Scaling || "Uniform",
          v => { item.Scaling = v; refresh(); }, "ImageScaling")));

        // Said here rather than left to the checks panel, because it is not a fault to be
        // put right: it is a fact about the document that only the author can act on.
        p.appendChild(field2("", note(t("hint.importedClaim"))));
      }

      if (item.$type === "SubTemplateItem") {
        p.appendChild(field2(t("field.subMode"), select(
          ["Splice", "Append"], item.Mode || "Splice",
          v => { item.Mode = v; refresh(); }, "SubTemplateMode")));

        // Append has a consequence nothing on the canvas can show, so it is said here:
        // the piece becomes sections of its own after this one, and whatever follows the
        // include in this section still comes out before it.
        if (item.Mode === "Append")
          p.appendChild(field2("", note(t("hint.appendMode"))));

        // Committed whole rather than per keystroke: a reference is one name, and asking
        // the host for "l", "lo" and "loi" on the way to it is three questions about nothing.
        const path = input("text", item.TemplatePath ?? "", () => {},
                           { placeholder: "fragment.json  |  st://letterhead" });
        path.oninput = null;
        path.onchange = () => { item.TemplatePath = path.value; refresh(); };
        p.appendChild(field2(t("field.templatePath"), path));

        if (library.length) {
          const current = referenceOf(item);
          const pick = document.createElement("select");
          for (const entry of [{ reference: "", title: "—" }, ...library]) {
            const option = document.createElement("option");
            option.value = entry.reference;
            option.textContent = entry.title;
            pick.appendChild(option);
          }
          pick.value = library.some(e => e.reference === current) ? current : "";
          pick.onchange = () => {
            if (!pick.value) return;
            item.TemplatePath = "st://" + pick.value;
            item.TemplateData = "";
            refresh();
          };
          p.appendChild(field2(t("field.libraryPiece"), pick));
        }

        // What the library said about the reference, since the canvas can only show it.
        const reference = referenceOf(item);
        if (reference) {
          const entry = pieces.get(reference);
          const listed = library.find(e => e.reference === reference);
          const args = { reference: "st://" + reference, title: listed ? listed.title : reference };
          p.appendChild(field2("", note(t(
            !subDocuments ? "hint.pieceNoLibrary"
            : !entry || entry.state === "asked" ? "hint.pieceLoading"
            : entry.state === "ready" ? "hint.pieceReady" : "hint.pieceMissing", args))));
        }

        // Embedding it: the same gesture as embedding an image, and for the same reason.
        // A path is resolved by whoever has the file, which is the CLI and the native
        // library and never this page; carried bytes travel with the template and are
        // spliced by anything that can read JSON, this preview included.
        const carry = document.createElement("input");
        carry.type = "file";
        carry.accept = "application/json,.json";
        carry.onchange = () => {
          const chosen = carry.files && carry.files[0];
          if (!chosen) return;
          const reader = new FileReader();
          reader.onload = () => {
            item.TemplateData = btoa(unescape(encodeURIComponent(String(reader.result))));
            item.TemplatePath = "";
            refresh();
          };
          reader.readAsText(chosen);
        };
        p.appendChild(field2(t("field.embed"), carry));

        if ((item.TemplateData || "").length) {
          const drop = document.createElement("button");
          drop.textContent = t("action.delete");
          drop.onclick = () => { item.TemplateData = ""; refresh(); };
          p.appendChild(field2(t("field.carried"), drop));
        }

        p.appendChild(field2("", note(t("hint.subTemplate"))));
      }

      if (item.$type === "AdvancedTextItem") {
        // The item's own base style: alignment and line spacing describe the whole
        // paragraph, so they belong here rather than on any one run.
        p.appendChild(field2(t("field.font"), input("text", item.Font.Family ?? "",
          v => { item.Font.Family = v; refresh(); })));
        const size = document.createElement("div"); size.className = "val";
        size.appendChild(input("number", item.Font.Size ?? 1,
          v => { item.Font.Size = Number(v) || 1; refresh(); }, { step: 0.25 }));
        size.appendChild(select(["Rem", "Millimeter", "Point", "PercentOfFrame"], item.Font.SizeUnit || "Rem",
          v => { item.Font.SizeUnit = v; refresh(); }, "FontSizeUnit"));
        p.appendChild(field2(t("field.size"), size));
        p.appendChild(field2(t("field.alignH"), select(["Left", "Center", "Right", "Justify"],
          item.Font.HorizontalAlignment || "Left",
          v => { item.Font.HorizontalAlignment = v; refresh(); }, "HorizontalAlignment")));
        if (item.Font.HorizontalAlignment === "Justify")
          p.appendChild(field2("", note(t("hint.advancedJustify"))));

        const runsHeading = document.createElement("h2");
        runsHeading.textContent = t("field.runs");
        runsHeading.style.marginTop = "12px";
        p.appendChild(runsHeading);

        const openRich = button(t("richText.open"), () => openRichText(item));
        openRich.style.marginBottom = "8px";
        p.appendChild(openRich);

        item.Runs.forEach((run, i) => {
          const row = document.createElement("div");
          row.style.cssText = "border:1px solid var(--line);border-radius: var(--radius);padding:6px;margin-bottom:6px";

          const text = document.createElement("textarea");
          text.value = run.Text; text.rows = 2; text.style.width = "100%";
          text.oninput = () => { run.Text = text.value; refresh(); };
          row.appendChild(text);

          const styleRow = document.createElement("div");
          styleRow.className = "val"; styleRow.style.cssText = "margin-top:4px;flex-wrap:wrap";
          for (const [key, tag] of [["Bold", t("style.bold")], ["Italic", t("style.italic")],
                                    ["Underline", t("style.underline")], ["Strikethrough", t("style.strikethrough")]]) {
            const l = document.createElement("label");
            l.style.cssText = "display:flex;gap:2px;align-items:center;color:var(--muted)";
            l.append(input("checkbox", run.Font?.[key] ?? false, v => {
              run.Font ??= {};
              run.Font[key] = v || null;
              refresh();
            }), tag);
            styleRow.appendChild(l);
          }

          const colorLabel = document.createElement("label");
          colorLabel.style.cssText = "display:flex;gap:2px;align-items:center;color:var(--muted)";
          colorLabel.append(input("checkbox", !!run.Font?.Color, v => {
            run.Font ??= {};
            run.Font.Color = v ? { R: 0, G: 0, B: 0, A: 255 } : null;
            refresh();
          }), t("field.runOwnColor"));
          styleRow.appendChild(colorLabel);

          if (run.Font?.Color) {
            const picker = document.createElement("input");
            picker.type = "color";
            picker.value = hex(run.Font.Color);
            picker.oninput = () => { run.Font.Color = hexToRgb(picker.value); refresh(); };
            styleRow.appendChild(picker);
          }

          row.appendChild(styleRow);

          const sizeRow = document.createElement("label");
          sizeRow.style.cssText = "display:flex;gap:4px;align-items:center;color:var(--muted);margin-top:4px";
          const sizeCheck = input("checkbox", run.Font?.Size != null, v => {
            run.Font ??= {};
            run.Font.Size = v ? (item.Font.Size ?? 1) : null;
            run.Font.SizeUnit = v ? (item.Font.SizeUnit || "Rem") : null;
            refresh();
          });
          sizeRow.append(sizeCheck, t("field.runOwnSize"));
          if (run.Font?.Size != null) {
            sizeRow.appendChild(input("number", run.Font.Size,
              v => { run.Font.Size = Number(v) || 1; refresh(); }, { step: 0.25 }));
          }
          row.appendChild(sizeRow);

          const del = button(t("action.delete"), () => { item.Runs.splice(i, 1); refresh(); });
          del.style.cssText = "margin-top:6px";
          row.appendChild(del);

          p.appendChild(row);
        });

        p.appendChild(button(t("field.addRun"), () => { item.Runs.push({ Text: "", Font: null }); refresh(); }));
      }

      if (item.Childs && (item.$type === "ContainerItem" || item.$type === "Body"))
        p.appendChild(field2(t("field.layout"), select(["Absolute", "Stack"], item.Layout, v => { item.Layout = v; refresh(); }, "LayoutMode")));

      if (item.$type === "TableRow")
        p.appendChild(field2(t("field.isHeader"), input("checkbox", item.IsHeader, v => { item.IsHeader = v; refresh(); })));

      const heading = t => { const h = document.createElement("h2"); h.textContent = t; h.style.marginTop = "12px"; p.appendChild(h); };

      // The report, selected in its own right. What is here belongs to the file: one
      // /Title per PDF, one language, one set of metadata. It used to sit under the
      // section's heading, where changing the title while a section was selected looked
      // as though it should change only that section - which is what was reported, and
      // the behaviour was right while the panel was not.
      //
      // No position, no style, no role: a report is not an item on a page. Returning
      // early is what says so, rather than a heading claiming it.
      if (item === report) {
        heading(t("section.report"));

        p.appendChild(field2(t("field.rootFontSize"), input("number", report.RootFontSize ?? 3,
          v => { report.RootFontSize = Number(v) || 3; refresh(); }, { step: 0.25, min: 1, max: 20 })));

        p.appendChild(field2(t("field.title"), input("text", report.Title ?? "",
          v => { report.Title = v; refresh(); })));
        p.appendChild(field2(t("field.author"), input("text", report.Author ?? "",
          v => { report.Author = v || null; refresh(); })));
        p.appendChild(field2(t("field.subject"), input("text", report.Subject ?? "",
          v => { report.Subject = v || null; refresh(); })));
        p.appendChild(field2(t("field.keywords"), input("text", report.Keywords ?? "",
          v => { report.Keywords = v || null; refresh(); })));
        p.appendChild(field2(t("field.language"), input("text", report.Language ?? "",
          v => { report.Language = v || null; refresh(); }, { placeholder: "fr-FR" })));
        p.appendChild(field2(t("field.headingNumbering"), input("text", report.HeadingNumbering ?? "",
          v => { report.HeadingNumbering = v || null; refresh(); }, { placeholder: "1.1.1" })));
        p.appendChild(field2("", note(t("hint.headingNumbering"))));

        const stock = document.createElement("div");
        stock.className = "row";
        stock.append(input("checkbox", !!report.ProofBackground, v => {
          report.ProofBackground = v ? { R: 250, G: 244, B: 227, A: 255 } : null;
          refresh();
        }));
        if (report.ProofBackground)
          stock.append(input("color", hex(report.ProofBackground), v => {
            report.ProofBackground = { R: parseInt(v.slice(1, 3), 16), G: parseInt(v.slice(3, 5), 16),
                                       B: parseInt(v.slice(5, 7), 16), A: 255 };
            refresh();
          }));
        p.appendChild(field2(t("field.proofBackground"), stock));
        p.appendChild(field2("", note(t("hint.proofBackground"))));
        p.appendChild(field2("", note(t("hint.metadata"))));
        return;
      }

      const loc = item.Style.Location;

      // Selecting the section itself shows what the file says about itself. It has no
      // position and no style worth editing, so this is the only place it belongs.
      if (item === doc0()) {
        heading(t("section.section"));

        const paperKey = Object.keys(PAPER_SIZES).find(k =>
          PAPER_SIZES[k][0] === doc0().Dimension.Width && PAPER_SIZES[k][1] === doc0().Dimension.Height) || "a4p";
        p.appendChild(field2(t("field.pageSize"), select(Object.keys(PAPER_SIZES), paperKey, v => {
          const [w, h] = PAPER_SIZES[v];
          doc0().Dimension = { Width: w, Height: h };
          refresh();
        }, "paper")));

        p.appendChild(field2(t("field.pageLimit"), input("number", doc0().PageLimit ?? "",
          v => { doc0().PageLimit = v === "" ? null : (Number(v) || null); refresh(); },
          { step: 1, min: 1, placeholder: t("field.noLimit") })));

        // Nothing about the file goes here any more. What used to follow - title, author,
        // language - belongs to the report, and is now reached by selecting the report in
        // the tree. A properties panel shows what is selected; an honest heading over
        // somebody else's fields only labels the category error.
      }

      // Accessibility: what the element is, and what to say about it when it cannot be
      // read. Declared from a closed list rather than guessed - a heading inferred from
      // being bold and large is wrong just often enough, and nobody notices until
      // somebody depends on it.
      if (item !== doc0()) {
        heading(t("section.accessibility"));
        p.appendChild(field2(t("field.role"), select(
          ["Auto", "Section", "Heading1", "Heading2", "Heading3", "Heading4", "Heading5",
           "Heading6", "Paragraph", "Caption", "List", "ListItem", "Table", "TableRow",
           "TableHeader", "TableCell", "Figure", "Form", "Artifact"],
          item.Role || "Auto", v => { item.Role = v; refresh(); }, "PdfRole")));

        // The language of this element and everything under it. Blank is the usual
        // answer and means the file's; a tag here marks a passage in another one, which
        // PDF/UA 7.2 asks for and which a dossier of pieces makes ordinary.
        p.appendChild(field2(t("field.language"), input("text", item.Language ?? "",
          v => { item.Language = v.trim() || null; refresh(); },
          { placeholder: t("field.languageSame") })));

        const figure = ["ImageItem", "BarcodeItem"].includes(item.$type)
                       || item.Role === "Figure";

        if (figure) {
          p.appendChild(field2(t("field.altText"), input("text", item.AltText ?? "",
            v => { item.AltText = v || null; refresh(); })));
          if (!item.AltText)
            p.appendChild(field2("", note(t("hint.altText"))));
        }
      }

      // How many columns a stacking container flows into. Absolute children carry their
      // own coordinates, so there would be nothing for a column to decide - and a table
      // or an image grid has its own dispatch that never consults ColumnCount either,
      // whatever Layout says.
      if (item.Childs && item.Layout === "Stack"
          && item.$type !== "TableItem" && item.$type !== "ImageGridItem") {
        heading(t("section.columns"));
        p.appendChild(field2(t("field.columnCount"), input("number", item.ColumnCount ?? 1,
          v => { item.ColumnCount = Math.max(1, Number(v) || 1); refresh(); }, { step: 1, min: 1 })));

        if ((item.ColumnCount ?? 1) > 1) {
          p.appendChild(field2(t("field.columnGap"), input("number", item.ColumnGap ?? 6,
            v => { item.ColumnGap = Math.max(0, Number(v) || 0); refresh(); }, { step: 0.5 })));
          p.appendChild(field2(t("field.balance"), input("checkbox", item.BalanceColumns !== false,
            v => { item.BalanceColumns = v; refresh(); })));
          p.appendChild(field2("", note(t("hint.balance"))));
        }
      }

      // Whether the element appears at all. Any item can say so, so this sits with the
      // general properties: a rule and a label both need to disappear together when the
      // box they belong to has nothing in it.
      if (item !== doc0()) {
        heading(t("section.condition"));
        p.appendChild(field2(t("field.showWhen"), input("text", item.ShowWhen ?? "",
          v => { item.ShowWhen = v || null; refresh(); })));
        p.appendChild(field2(t("field.hideWhen"), input("text", item.HideWhen ?? "",
          v => { item.HideWhen = v || null; refresh(); })));
        p.appendChild(field2("", note(t("hint.condition"))));
      }

      // Any item can name a place in the outline, so this sits with the general
      // properties rather than with one kind of item.
      if (item !== doc0()) {
        heading(t("section.outline"));
        p.appendChild(field2(t("field.bookmark"), input("text", item.Bookmark ?? "",
          v => { item.Bookmark = v || null; refresh(); })));

        if (item.Bookmark) {
          p.appendChild(field2(t("field.level"), input("number", item.BookmarkLevel ?? 0,
            v => { item.BookmarkLevel = Math.max(0, Number(v) || 0); refresh(); }, { min: 0, step: 1 })));
        }
      }

      heading(t("section.position"));
      for (const key of ["Left", "Top", "Right", "Bottom", "Width", "Height"])
        p.appendChild(field2(key, measurePair(loc, key)));

      heading(t("section.dock"));
      const sides = document.createElement("div");
      sides.className = "dock";
      const mask = dockOf(loc);
      for (const side of ["Left", "Top", "Right", "Bottom"]) {
        const l = document.createElement("label");
        l.append(input("checkbox", mask & DOCK[side], v => {
          const m = dockOf(loc);
          loc.Dock = dockToString(v ? m | DOCK[side] : m & ~DOCK[side]);
          refresh();
        }), side.slice(0, 1));
        sides.appendChild(l);
      }
      p.appendChild(sides);

      // Alignment sits under Position, and after Dock on purpose: it only moves an item
      // on an axis nothing else decided. These two were inside the ImageItem block, so a
      // table or a field could not be aligned from here at all - and Style carries them
      // for every item. The engine has answered for all of them since AlignHorizontal was
      // written; only this panel was narrower.
      p.appendChild(field2(t("field.alignAcross"), select(["None", "Start", "Center", "End"],
        item.Style.AlignHorizontal ?? "None", v => { item.Style.AlignHorizontal = v; refresh(); }, "ItemAlign")));
      p.appendChild(field2(t("field.alignDown"), select(["None", "Start", "Center", "End"],
        item.Style.AlignVertical ?? "None", v => { item.Style.AlignVertical = v; refresh(); }, "ItemAlign")));
      p.appendChild(field2("", note(t("hint.align"))));

      heading(t("section.margin"));   p.appendChild(edgeGroup(item.Style.Margin));
      heading(t("section.padding")); p.appendChild(edgeGroup(item.Style.Padding));

      heading(t("section.render"));
      p.appendChild(field2(t("field.visible"), select(["Visible", "Invisible", "Gone"], item.Style.Visibility,
        v => { item.Style.Visibility = v; refresh(); }, "Visibility")));
      p.appendChild(field2(t("field.pageBreak"), select(["None", "Before", "After", "BeforeAndAfter"], item.Style.PageBreak,
        v => { item.Style.PageBreak = v; refresh(); }, "PageBreak")));
      p.appendChild(field2(t("field.splitting"), select(["Auto", "KeepTogether"], item.Style.Splitting,
        v => { item.Style.Splitting = v; refresh(); }, "Splitting")));
      p.appendChild(field2(t("field.pageScope"), select(["All", "First", "Last", "ExceptFirst", "ExceptLast", "Odd", "Even"], item.Style.PageScope,
        v => { item.Style.PageScope = v; refresh(); }, "PageScope")));
      const bg = document.createElement("div"); bg.className = "val";
      bg.append(input("checkbox", !!item.Style.Background, v => {
        item.Style.Background = v ? { Color: { R: 238, G: 238, B: 238, A: 255 } } : null; refresh();
      }));
      if (item.Style.Background)
        bg.append(input("color", hex(item.Style.Background.Color), v => {
          item.Style.Background.Color = { R: parseInt(v.slice(1, 3), 16), G: parseInt(v.slice(3, 5), 16), B: parseInt(v.slice(5, 7), 16), A: 255 };
          refresh();
        }));
      p.appendChild(field2(t("field.background"), bg));

      if (item !== doc0() && item.$type !== "Header" && item.$type !== "Body" && item.$type !== "Footer") {
        const del = button(t("action.delete"), () => {
          const parent = parentOf(item);
          if (!parent) return;
          parent.Childs.splice(parent.Childs.indexOf(item), 1);
          selected = parent;
          refresh();
        });
        del.style.marginTop = "12px";
        p.appendChild(del);
      }
    }

    // ---------------------------------------------------------------------------
    // The rich-text dialog: type continuously, select a range, apply a style to it.
    // The run list stays the model and the source of truth - this is a second way
    // to produce the same list, not a second place it is stored. Editing plain text
    // inside a span is left entirely to the browser (that is what contenteditable is
    // for); only applying a style to a selection needs code, and that code works on
    // flat character offsets and the Runs array, never on raw DOM surgery.
    // ---------------------------------------------------------------------------
    let richTextItem = null;

    function openRichText(item) {
      richTextItem = item;
      runsToSurface(item.Runs, byId("richTextSurface"));
      cachedSelection = null;
      byId("richTextColor").value = item.Font.Color ? hex(item.Font.Color) : ON_PAPER.defaultInk;
      byId("richTextSize").value = item.Font.Size ?? 1;
      byId("richTextOverlay").hidden = false;
      byId("richTextSurface").focus();
    }

    function closeRichText() {
      byId("richTextOverlay").hidden = true;
      richTextItem = null;
    }

    /// The live selection, as offsets into the surface's flat text - the same
    /// technique any plain-text-over-a-rich-editor needs, since Range only speaks in
    /// DOM nodes and offsets within them, not in one flat string. Returns null
    /// without touching `cachedSelection` when there is nothing selected in the
    /// surface right now - a live read, not the one a style click should trust.
    function computeSurfaceOffsets() {
      const surface = byId("richTextSurface");
      // The root's selection: a document's, or a shadow root's - a range inside a shadow
      // root is not the window's to report (CLAUDE.md 5c-quater).
      const sel = uiRoot.getSelection ? uiRoot.getSelection() : window.getSelection();
      if (!sel.rangeCount) return null;
      const range = sel.getRangeAt(0);
      if (!surface.contains(range.startContainer) || !surface.contains(range.endContainer))
        return null;

      const toStart = document.createRange();
      toStart.selectNodeContents(surface);
      toStart.setEnd(range.startContainer, range.startOffset);
      const start = toStart.toString().length;

      const toEnd = document.createRange();
      toEnd.selectNodeContents(surface);
      toEnd.setEnd(range.endContainer, range.endOffset);
      const end = toEnd.toString().length;

      return start === end ? null : { start: Math.min(start, end), end: Math.max(start, end) };
    }

    // A plain <input> - the colour swatch, the size box - has its own internal caret
    // and stealing focus for it clears window.getSelection() outright in some
    // browsers (proven of the size field: rangeCount survives, but the range
    // collapses to nothing), so "type a size, then click Apply" would silently do
    // nothing. selectionchange fires continuously while the user is still dragging
    // inside the surface, before any of that focus theft happens, so caching the
    // last non-empty selection there survives it.
    let cachedSelection = null;
    // Fired at the root of the selection rather than bubbled to the document, so a shadow
    // root has to be listened to as well - bound to both when they differ, as the content
    // editor learnt (CLAUDE.md 5c-quater): recomputing twice costs nothing.
    const onSelectionChange = () => {
      if (!richTextItem) return;
      const offsets = computeSurfaceOffsets();
      if (offsets) cachedSelection = offsets;
    };
    uiRoot.addEventListener("selectionchange", onSelectionChange);
    if (uiRoot !== document) document.addEventListener("selectionchange", onSelectionChange);

    function richTextSelection() {
      return computeSurfaceOffsets() || cachedSelection;
    }

    /// Applies one style to the current selection - a boolean toggle for Bold and
    /// its siblings, a value (colour, size) for the rest. A boolean flips off only
    /// if every selected run already has it on, the same "select the same style
    /// twice to remove it" a normal rich-text toolbar gives.
    function applyRichStyle(key, value) {
      const offsets = richTextSelection();
      if (!offsets) return;

      const surface = byId("richTextSurface");
      let runs = splitRunsAt(surfaceToRuns(surface), [offsets.start, offsets.end]);
      const boolean = ["Bold", "Italic", "Underline", "Strikethrough"].includes(key);

      let allOn = true;
      let pos = 0;
      for (const run of runs) {
        if (pos >= offsets.start && pos < offsets.end && !(run.Font && run.Font[key]))
          allOn = false;
        pos += run.Text.length;
      }
      const next = boolean ? (allOn ? null : true) : value;

      pos = 0;
      for (const run of runs) {
        if (pos >= offsets.start && pos < offsets.end) {
          run.Font = Object.assign({}, run.Font, { [key]: next });
          if (run.Font[key] == null) delete run.Font[key];
          if (!Object.keys(run.Font).length) run.Font = null;
        }
        pos += run.Text.length;
      }

      runsToSurface(mergeSameStyleRuns(runs), surface);
    }

    // ---------------------------------------------------------------------------
    // Sections — a report is a list of sections; this page edits one at a time.
    //
    // Deliberately a dialog rather than a change to the main interface: the tree, the
    // canvas and the properties panel all describe exactly one section, which is what
    // they already did when a report could only have one. Choosing which section is a
    // separate act, so nothing else has to learn that there are several.
    // ---------------------------------------------------------------------------
    function paperName(dim) {
      const found = Object.keys(PAPER_SIZES).find(k =>
        PAPER_SIZES[k][0] === dim.Width && PAPER_SIZES[k][1] === dim.Height);
      return found ? t("paper." + found) : `${n3(dim.Width)} × ${n3(dim.Height)} mm`;
    }

    function showSectionCount() {
      const b = byId("sections");
      if (!b) return;
      b.textContent = report.Sections.length > 1
        ? `${t("sections.title")} (${sectionIndex + 1}/${report.Sections.length})`
        : t("sections.open");
    }

    function drawSections() {
      showSectionCount();
      const list = byId("sectionList");
      list.innerHTML = "";

      report.Sections.forEach((section, i) => {
        const row = document.createElement("div");
        row.className = "section-row" + (i === sectionIndex ? " is-current" : "");

        const label = document.createElement("div");
        label.innerHTML = `<strong>${t("sections.one")} ${i + 1}</strong>`
                        + `<div class="meta">${paperName(section.Dimension || {})}`
                        + ` · ${(section.Childs || []).length} ${t("sections.bands")}</div>`;
        row.appendChild(label);

        const buttons = document.createElement("div");
        buttons.className = "val";

        if (i !== sectionIndex) {
          buttons.appendChild(button(t("sections.edit"), () => {
            sectionIndex = i;
            selected = doc0();
            drawSections();
            refresh();
          }));
        }

        // A report with one section has nothing to remove: removing the last would leave
        // a report that cannot be laid out at all.
        if (report.Sections.length > 1) {
          const drop = button(t("sections.remove"), () => {
            if (!confirm(t("sections.confirmRemove", { n: i + 1 }))) return;
            report.Sections.splice(i, 1);
            if (sectionIndex >= report.Sections.length) sectionIndex = report.Sections.length - 1;
            selected = doc0();
            drawSections();
            refresh();
          });
          drop.className = "danger";
          buttons.appendChild(drop);
        }

        row.appendChild(buttons);
        list.appendChild(row);
      });
    }

    function openSections() {
      drawSections();
      byId("sectionOverlay").hidden = false;
    }

    byId("sections").onclick = openSections;
    byId("sectionClose").onclick = () => {
      byId("sectionOverlay").hidden = true;
    };

    byId("sectionAdd").onclick = () => {
      // A new section starts as a copy of the one being edited, minus its content: the
      // page size and margins are nearly always what was wanted, and an empty sheet of
      // unknown size is not a helpful starting point.
      const from = doc0();
      const added = {
        $type: "Section",
        Layout: from.Layout || "Absolute",
        Style: JSON.parse(JSON.stringify(from.Style)),
        Dimension: Object.assign({}, from.Dimension),
        Childs: [{ $type: "Body", Style: newStyle(), Layout: "Stack", Childs: [] }]
      };
      added.Childs[0].Style.Location.Dock = "Fill";

      report.Sections.push(added);
      sectionIndex = report.Sections.length - 1;
      selected = doc0();
      drawSections();
      refresh();
    };

    // A mousedown on a toolbar button fires before its click, and by default steals
    // focus from the contenteditable surface - which collapses the very selection
    // the button is about to act on. Suppressing it is what every rich-text toolbar
    // does; without it, clicking "Bold" would first clear what was selected.
    const richToolbar = uiRoot.querySelector("#richTextOverlay .toolbar");
    if (richToolbar) richToolbar.addEventListener("mousedown", e => {
      if (e.target.closest("button")) e.preventDefault();
    });

    uiRoot.querySelectorAll("#richTextOverlay [data-rt-style]").forEach(b => {
      b.onclick = () => applyRichStyle(b.dataset.rtStyle, true);
    });
    // Colour and size apply themselves as soon as a value is picked - a separate
    // "now click here to apply it" step was the exact confusion reported: nothing
    // visibly happened when that second click landed on a selection focus had
    // already cleared. "No colour" and "Default size" stay their own buttons since
    // clearing is not a value the input can represent on its own.
    byId("richTextColor").onchange = () =>
      applyRichStyle("Color", hexToRgb(byId("richTextColor").value));
    byId("richTextColorClear").onclick = () => applyRichStyle("Color", null);
    byId("richTextSize").onchange = () => {
      const v = Number(byId("richTextSize").value);
      applyRichStyle("Size", v > 0 ? v : null);
    };
    byId("richTextSizeClear").onclick = () => applyRichStyle("Size", null);
    byId("richTextCancel").onclick = closeRichText;
    byId("richTextSave").onclick = () => {
      if (richTextItem) richTextItem.Runs = surfaceToRuns(byId("richTextSurface"));
      closeRichText();
      refresh();
    };

    function button(label, onClick) {
      const b = document.createElement("button");
      b.textContent = label;
      b.onclick = onClick;
      return b;
    }

    // Everything drawn from the template, drawn again. The engine's pages are not thrown away
    // by an edit: they stay on show until newer ones arrive, which is what keeps a live
    // preview from flickering back to the approximation on every keystroke.
    function refresh() {
      normaliseReport(report);
      showSectionCount();
      applyPaper();   // the stock is the template's, so it changes when the template does

      // Before anything reads the tree. A template omits everything left at its
      // default, so an opened one may have no Location, no Margin, no Font at all -
      // and the properties panel used to reach into them before layout filled them in.
      hydrate(doc0());
      if (report.BackPage) hydrate(report.BackPage.Content);

      // Before anything is drawn or validated: the preview fills the template from it.
      setData(ui.data && ui.data.value);

      // And the pieces spliced in before the checks read the tree, so what they find is in
      // what is drawn - the engine checks the composition too (CLAUDE.md 14b) - rather than
      // in whatever the last redraw left there.
      expandCarried({ Childs: report.Sections || [] }, lookupPiece);
      if (report.BackPage && report.BackPage.Content)
        expandCarried({ Childs: [report.BackPage.Content] }, lookupPiece);

      runValidator();
      drawTree();
      drawIssues();
      drawProps();

      paint();
      scheduleLive();
    }

    /// The canvas, and whatever copies or follows it. The engine's pages on the canvas when
    /// that is where the person wants them and they have come - and the approximation
    /// otherwise, including while the engine is failing, so an edit is never invisible.
    function paint() {
      if (ui.liveStatus) ui.liveStatus.textContent = liveStatusText();

      // The banner says the canvas is an approximation, which stops being true the moment
      // the canvas shows the engine's pages - and it went on saying so under the old exact
      // preview's pages as well.
      const engineOnCanvas = live === "canvas" && liveOn() && livePages && liveState !== "failed";
      if (ui.banner) ui.banner.hidden = !!engineOnCanvas;

      if (engineOnCanvas) {
        ui.canvas.innerHTML = enginePages();
        ui.canvas.classList.add("pages");
        fillStage();
        applyZoom();
        pushPreview();
        return;
      }

      ui.canvas.classList.remove("pages");

      // Editing the back with the front on screen is editing blind.
      const shown = tab === "back" && backPage() ? backAsPage(report) : report;

      // The back is shown as a report of one section, so it is always that one; otherwise
      // it is whichever section the Sections dialog left open.
      const shownIndex = shown === report ? sectionIndex : 0;
      ui.canvas.innerHTML = render(shown, selected, shownIndex,
                                   { bands: ui.showBands.checked, boxes: ui.showBoxes.checked },
                                   lookupPiece);
      const svg = ui.canvas.querySelector("svg");
      if (svg) svg.onclick = e => {
        const pt = svg.getScreenCTM().inverse();
        const p = new DOMPoint(e.clientX, e.clientY).matrixTransform(pt);
        const laid = layout(shown, shownIndex, lookupPiece);
        const hit = laid.placed
          .filter(x => Number.isFinite(x.frame.left) && x.item !== laid.section
                       && p.x >= x.frame.left && p.x <= x.frame.right && p.y >= x.frame.top && p.y <= x.frame.bottom)
          .pop();
        if (hit) { selected = pieceHolding(hit.item) || hit.item; refresh(); }
      };

      fillStage();
      applyZoom();
      pushPreview();
    }

    // --- the exact preview -----------------------------------------------------
    async function payload() {
      await settlePieces();
      const copy = carryReferences(forEngine(report), knownPiece);
      const data = ui.data.value.trim();
      return {
        template: JSON.stringify(copy),
        data: data.length ? data : null,
        showBands: ui.showBands.checked,
        numbers: "invariant"
      };
    }

    function setServerState(state) {
      ui.dot.className = "dot" + (state ? " on" : state === false ? " off" : "");
    }

    async function ask(path, body) {
      const base = serverBase();
      if (base === null) throw new Error(t("live.noServer"));
      const answer = await send(base + path, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
      });
      if (!answer.ok) {
        let message = answer.statusText;
        try { message = (await answer.json()).error || message; } catch {}
        throw new Error(message);
      }
      return answer;
    }

    if (ui.live) {
      ui.live.value = live;
      ui.live.onchange = () => setLive(ui.live.value);
    }

    byId("pdf").onclick = async () => {
      try {
        const answer = await ask("/pdf", await payload());
        const blob = await answer.blob();
        const a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = (report.Title || "report").replace(/[^\w.-]+/g, "-").toLowerCase() + ".pdf";
        a.click();
        URL.revokeObjectURL(a.href);
        setServerState(true);
      } catch (err) {
        setServerState(false);
        alert(t("alert.noPdf") + err.message);
      }
    };

    ui.data.oninput = refresh;
    ui.server.oninput = () => setServerState(null);

    // --- toolbar ---------------------------------------------------------------
    byId("new").onclick = () => {
      if (!confirm(t("confirm.new"))) return;
      report = blankReport(); selected = doc0(); showWatermark(); showBackdrop(); showBack(); refresh();
    };
    /// Puts a template in, from wherever it came - the file picker, or a host through
    /// <frisket-designer>. One path, so a host handing over JSON and a person choosing a file
    /// arrive at the same place.
    function loadReport(loaded) {
      if (typeof loaded === "string") loaded = JSON.parse(loaded);

      if (!(Array.isArray(loaded.Sections) && loaded.Sections.length))
        throw new Error("no Sections in this JSON");

      report = normaliseReport(loaded);
      report.Fields = report.Fields || {};
      sectionIndex = 0;

      // The engine does not tag a section: the property is typed, so the
      // discriminator would be redundant. The designer needs it to know the kind.
      report.Sections.forEach(d => { d.$type = "Section"; });
      selected = doc0();
      showWatermark();
      showBackdrop();
      showBack();
      refresh();

      // Several sections, and this page shows one: say which, rather than opening on
      // the first and letting the others go unnoticed.
      if (report.Sections.length > 1) openSections();
    }

    byId("open").onclick = () => byId("file").click();
    byId("file").onchange = e => {
      const f = e.target.files[0]; if (!f) return;
      const reader = new FileReader();
      reader.onload = () => {
        try {
          loadReport(JSON.parse(String(reader.result)));
        } catch (err) {
          alert(t("alert.unreadable") + err.message);
        }
      };
      reader.readAsText(f);
      e.target.value = "";
    };
    function fileName(title, suffix) {
      return String(title).replace(/[^\w.-]+/g, "-").toLowerCase() + suffix;
    }

    function download(text, name) {
      const blob = new Blob([text], { type: "application/json" });
      const a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = name;
      a.click();
      URL.revokeObjectURL(a.href);
    }

    // The data file is the template's other half: same shape of round trip, and the
    // engine writes one next to every bound report it produces.
    byId("dataOpen").onclick = () => byId("dataFile").click();
    byId("dataFile").onchange = e => {
      const f = e.target.files[0]; if (!f) return;
      const reader = new FileReader();
      reader.onload = () => {
        try {
          // Parsed before it is accepted: a file that cannot be read is worth saying
          // so about now, rather than leaving the preview quietly showing tokens.
          const parsed = JSON.parse(String(reader.result));
          ui.data.value = JSON.stringify(parsed, null, 2);
          refresh();
        } catch (err) {
          alert(t("alert.badData") + err.message);
        }
      };
      reader.readAsText(f);
      e.target.value = "";
    };

    byId("dataSave").onclick = () => {
      const text = ui.data.value.trim();
      if (!text) return alert(t("alert.noData"));
      try {
        JSON.parse(text);
      } catch (err) {
        return alert(t("alert.badDataKept") + err.message);
      }
      download(text, fileName(report.Title || "donnees", ".data.json"));
    };

    byId("dataClear").onclick = () => {
      if (!ui.data.value.trim() || confirm(t("confirm.clearData"))) {
        ui.data.value = "";
        refresh();
      }
    };

    byId("save").onclick = () => {
      download(JSON.stringify(forEngine(report), null, 2),
               fileName(report.Title || "report", ".json"));
    };
    ui.showBands.onchange = refresh;
    ui.showBoxes.onchange = refresh;

    uiRoot.querySelectorAll("[data-add]").forEach(b => {
      b.onclick = () => {
        const kind = b.dataset.add;
        const item = newItem(kind.startsWith("Field") ? "FieldItem" : kind);
        if (kind.startsWith("Field")) {
          item.Kind = kind.slice(5);
          item.Name = item.Kind.toLowerCase();
          if (item.Kind !== "Text") item.Style.Location.HeightValue = item.Kind === "Signature" ? 20 : 5;
          if (item.Kind === "Checkbox") item.Style.Location.WidthValue = 5;
        }
        addToSelection(item);
      };
    });

    /// Puts a new item where the selection says: inside it when it holds things, beside it
    /// otherwise. What the palette and the library both do.
    function addToSelection(item) {
      // With nothing selected this used to land on the Section, beside the bands
      // rather than inside one - laid out nowhere and drawn nowhere. Content belongs
      // in the body; the bands are structure. And never inside a sub-template: what it
      // holds is another template's, and is dropped on saving.
      const host = selected && selected.Childs && selected.$type !== "SubTemplateItem" ? selected
                 : (parentOf(selected) || bandOf("Body") || doc0());
      host.Childs.push(item);
      selected = item;
      refresh();
    }

    // Once the field being typed into gives up the focus, the panel can be rebuilt -
    // which is what brings back any field that depends on what was just changed. The
    // timeout lets the focus land before we ask where it is.
    ui.props.addEventListener("focusout", () => setTimeout(() => drawProps(), 0));

    // --- tabs --------------------------------------------------------------------
    uiRoot.querySelectorAll(".tab").forEach(button => {
      button.onclick = () => {
        tab = button.dataset.tab;
        uiRoot.querySelectorAll(".tab").forEach(b => b.classList.toggle("on", b === button));
        uiRoot.querySelectorAll(".pane").forEach(p => { p.hidden = p.dataset.pane !== tab; });

        // Selection belongs to one tree; carrying it across leaves the properties
        // panel editing something the visible tree does not contain.
        selected = activeRoot();
        refresh();
      };
    });

    // --- back page ---------------------------------------------------------------
    const bp = {
      on: byId("bpOn"), fields: byId("bpFields"),
      edit: byId("bpEdit"), scope: byId("bpScope"),
      pad: byId("bpPad")
    };

    function readBack() {
      bp.fields.hidden = bp.edit.hidden = !bp.on.checked;

      if (!bp.on.checked) {
        report.BackPage = null;
      } else {
        const pad = Number(bp.pad.value) || 0;
        report.BackPage = {
          Content: (report.BackPage && report.BackPage.Content)
                   || { $type: "ContainerItem", Layout: "Stack", Style: newStyle(), Childs: [] },
          Scope: bp.scope.value,
          Padding: edges(pad, pad, pad, pad)
        };
      }

      selected = activeRoot();
      refresh();
    }

    /// Puts an opened template's back into the controls.
    function showBack() {
      const back = backPage();
      bp.on.checked = !!back;
      bp.fields.hidden = bp.edit.hidden = !back;
      if (!back) return;
      bp.scope.value = back.Scope || "EveryPage";
      bp.pad.value = (back.Padding && back.Padding.Left) ?? 12;
    }

    [bp.on, bp.scope, bp.pad].forEach(el => { el.oninput = readBack; el.onchange = readBack; });

    // --- watermark ---------------------------------------------------------------
    const wm = {
      on: byId("wmOn"), fields: byId("wmFields"),
      text: byId("wmText"), mode: byId("wmMode"),
      layer: byId("wmLayer"), angle: byId("wmAngle"),
      size: byId("wmSize")
    };

    function readWatermark() {
      wm.fields.hidden = !wm.on.checked;

      // Merged onto what is already there, not rebuilt: the panel offers neither Spacing
      // nor PageScope, and rebuilding reset both to a default every time a control moved.
      //
      // The ink comes from WATERMARK_INK rather than from a number typed here. This line
      // said 77 while the engine had said 51 since it was measured too dark over a
      // paragraph - so every mark the designer made was half again as heavy as one the
      // engine makes itself, and nothing compared the two.
      report.Watermark = wm.on.checked ? Object.assign(
        { Spacing: 0, PageScope: "All" },
        report.Watermark,
        {
          Text: wm.text.value || "DRAFT ",
          Mode: wm.mode.value,
          Layer: wm.layer.value,
          Angle: Number(wm.angle.value) || 0,
          Font: Object.assign(newFont(), report.Watermark && report.Watermark.Font, {
            Size: Number(wm.size.value) || 8,
            SizeUnit: "Millimeter",
            Bold: true,
            Color: Object.assign({}, WATERMARK_INK)
          })
        }) : null;

      refresh();
    }

    /// Puts an opened template's mark back into the controls, so the panel shows what
    /// the section actually carries rather than what was last typed.
    function showWatermark() {
      const w = report.Watermark;
      wm.on.checked = !!w;
      wm.fields.hidden = !w;
      if (!w) return;
      wm.text.value = w.Text ?? "DRAFT ";
      wm.mode.value = w.Mode ?? "Tiled";
      wm.layer.value = w.Layer ?? "InFront";
      wm.angle.value = w.Angle ?? 45;
      wm.size.value = (w.Font && w.Font.Size) ?? 8;
    }

    [wm.on, wm.text, wm.mode, wm.layer, wm.angle, wm.size]
      .forEach(el => { el.oninput = readWatermark; el.onchange = readWatermark; });

    // --- the picture behind the page ---------------------------------------------
    const bg = {
      on: byId("bgOn"), fields: byId("bgFields"),
      file: byId("bgFile"), area: byId("bgArea"),
      scaling: byId("bgScaling"),
      across: byId("bgAcross"), down: byId("bgDown"),
      opacity: byId("bgOpacity")
    };

    function readBackdrop() {
      bg.fields.hidden = !bg.on.checked;

      if (!bg.on.checked) { report.Backdrop = null; refresh(); return; }

      // Merged onto what is already there rather than rebuilt from the controls: the panel
      // does not offer PageScope, and a face that silently drops what it cannot edit is
      // worse than one that says it cannot edit it.
      report.Backdrop = Object.assign(
        { Source: { MediaType: "image/png", Data: "" }, PageScope: "All" },
        report.Backdrop,
        { Area: bg.area.value, Scaling: bg.scaling.value,
          AlignHorizontal: bg.across.value, AlignVertical: bg.down.value,
          Opacity: Number(bg.opacity.value) });

      refresh();
    }

    /// Puts an opened template's backdrop back into the controls, so the panel shows what
    /// the file carries rather than what was last typed.
    function showBackdrop() {
      const b = report.Backdrop;
      bg.on.checked = !!b;
      bg.fields.hidden = !b;
      if (!b) return;
      bg.area.value = b.Area ?? "Sheet";
      bg.scaling.value = b.Scaling ?? "Stretch";
      bg.across.value = b.AlignHorizontal ?? "Center";
      bg.down.value = b.AlignVertical ?? "Center";
      bg.opacity.value = b.Opacity ?? 1;
    }

    [bg.on, bg.area, bg.scaling, bg.across, bg.down, bg.opacity]
      .forEach(el => { el.oninput = readBackdrop; el.onchange = readBackdrop; });

    bg.file.onchange = () => {
      const chosen = bg.file.files && bg.file.files[0];
      if (!chosen) return;

      const reader = new FileReader();
      reader.onload = () => {
        if (!report.Backdrop) { bg.on.checked = true; readBackdrop(); }
        report.Backdrop.Source = {
          MediaType: chosen.type || "image/png",
          Data: String(reader.result).replace(/^data:[^,]*,/, "")
        };

        // Measured, not asked for. "Keep proportions" has nothing to preserve until the
        // engine is told what the proportions are - it is a declared number on the
        // template, exactly as on an ImageItem - and nobody is going to type the pixel
        // size of a letterhead by hand. The flow editor already does this on insert.
        const probe = new Image();
        probe.onload = () => {
          report.Backdrop.NaturalWidth = probe.width / 3.7795;     // 96 dpi
          report.Backdrop.NaturalHeight = probe.height / 3.7795;
          refresh();
        };
        probe.onerror = () => refresh();
        probe.src = String(reader.result);
        refresh();
      };
      reader.readAsDataURL(chosen);
    };

    // --- how big the sheet is drawn ----------------------------------------------
    // "fit" is the default because it is nearly always what is wanted: a sheet of A4 is
    // 297 mm tall and almost no window is, so at 100 % the page is scrolled from the
    // first keystroke.
    const MM = 96 / 25.4;                     // CSS millimetre, in pixels

    function applyZoom() {
      const sheet = doc0().Dimension || { Width: 210, Height: 297 };
      ui.canvas.style.setProperty("--sheet", sheet.Width);

      if (ui.stage && !ui.stage.hidden && ui.stagePages) {
        ui.stagePages.style.setProperty("--sheet", sheet.Width);
        ui.stagePages.style.setProperty("--zoom", stageZoom !== "fit"
          ? stageZoom : fitInto(ui.stagePages, sheet).toFixed(4));
      }

      applyPopupZoom(sheet);

      if (zoomChoice !== "fit") {
        ui.canvas.style.setProperty("--zoom", zoomChoice);
        return;
      }

      ui.canvas.style.setProperty("--zoom", fitInto(ui.canvas, sheet).toFixed(4));
    }

    /// The scale at which a sheet fits inside an element, whole.
    ///
    /// Measured, not guessed: the padding is real and so are the scrollbars. Whichever
    /// of the two directions runs out first decides. The element may belong to the
    /// preview window rather than this one, so the computed style comes from its own
    /// window - the opener's getComputedStyle is not guaranteed to answer for it.
    function fitInto(box, sheet) {
      const win = (box.ownerDocument && box.ownerDocument.defaultView) || window;
      const style = (win.getComputedStyle || getComputedStyle)(box);
      const width = box.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
      const height = box.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom);

      if (!(width > 0 && height > 0)) return 1;

      return Math.max(0.05, Math.min(width / (sheet.Width * MM), height / (sheet.Height * MM)));
    }

    // The window changing shape changes what fits, and so does moving it to the other
    // monitor - which is the same event.
    window.addEventListener("resize", applyZoom);

    // --- very wide screens -------------------------------------------------------
    // Automatic, and left to the browser to decide: a media query re-evaluates itself
    // on every resize and on a window dragged to another monitor, which no amount of
    // listening to resize events does as well.
    const ULTRAWIDE = window.matchMedia("(min-aspect-ratio: 21/9)");

    function applyShape() {
      // A very wide screen gets the stage by itself; any screen gets it for the engine's
      // pages when the person asked for them beside the canvas.
      const wide = ULTRAWIDE.matches || (live === "beside" && liveOn());
      uiRoot.querySelector("main").classList.toggle("wide", wide);

      if (ui.stage) ui.stage.hidden = !wide;
      if (ui.stageSplitter) ui.stageSplitter.hidden = !wide;

      applyPanels();
      fillStage();
      applyZoom();
    }

    /// The large preview shows the same pages as the small one - copied rather than laid
    /// out again, so the two cannot disagree - or the engine's, when that is what goes
    /// beside the canvas.
    function fillStage() {
      if (!ui.stage || ui.stage.hidden || !ui.stagePages) return;
      ui.stagePages.innerHTML = live === "beside" && liveOn() ? enginePages() : ui.canvas.innerHTML;
    }

    ULTRAWIDE.addEventListener("change", applyShape);
    // Called once the panel widths below exist: applyShape sets them, and reading a
    // const before its own line is a reference error rather than an undefined.

    // --- the preview, on the other screen ----------------------------------------
    // A window rather than a message channel: the page is opened from a file:// URL as
    // often as not, where every window shares the same null origin and BroadcastChannel
    // is no use. A handle to the window works everywhere and needs nothing.
    let previewWindow = null;

    // One window per designer: two designers in one page sharing a name would share the
    // window, each drawing over the other's pages.
    const windowName = "frisket-preview" + (designersMade_ > 1 ? "-" + designersMade_ : "");

    /// Opens it, or brings it back to the front if it is already there.
    async function detachPreview() {
      if (previewWindow && !previewWindow.closed) {
        previewWindow.focus();
        return;
      }

      let features = "width=1200,height=1000";

      // Where a second screen exists and the browser will say so, put it there and
      // fill it. The permission is asked for once; refused, the window still opens.
      try {
        if (window.getScreenDetails) {
          const screens = await window.getScreenDetails();
          const other = screens.screens.find(s => !s.isPrimary) || screens.currentScreen;
          features = `left=${other.availLeft},top=${other.availTop},`
                   + `width=${other.availWidth},height=${other.availHeight}`;
        }
      } catch {
        // Refused or unsupported: a plain window is a perfectly good answer.
      }

      previewWindow = window.open("", windowName, features);
      if (!previewWindow) {
        alert(t("hint.popup"));
        return;
      }

      // A window of its own means a stylesheet of its own, and it used to hold its own
      // literal greys - so it was the one piece that would not have followed the theme.
      // Read the live values instead: same palette, one source.
      const paint = getComputedStyle(themeTarget);
      const c = name => paint.getPropertyValue("--" + name).trim();

      previewWindow.document.write(
        `<!doctype html><html><head><meta charset="utf-8"><title>${esc(t("preview.title"))}</title>`
        + `<style>html,body{margin:0;height:100%;background:${c("surround")};color:${c("ink")};`
        + `color-scheme:${schemeOf(theme)};`
        + "font:13px system-ui}body{display:flex;flex-direction:column}"
        + "#bar{flex:none;display:flex;align-items:center;gap:8px;padding:6px 10px;"
        + `background:${c("panel")};border-bottom:1px solid ${c("line")}}`
        + `#bar select{background:${c("control")};color:inherit;border:1px solid ${c("line")};`
        + "border-radius: var(--radius);padding:3px 6px;font:inherit}"
        // Its own scrollable area, not the section itself: fitting needs the visible
        // viewport, and a native section scroll would count every stacked page as
        // room to grow into rather than the one screenful actually on show.
        + "#pages{flex:1;overflow:auto;padding:24px;display:flex;flex-direction:column;"
        // "safe center": the same overflow-clipping the canvas and stage were fixed
        // for applies here too, now that this page can be zoomed past its own width.
        + "align-items:safe center;gap:16px}"
        + "svg{width:calc(var(--zoom, 1) * var(--sheet, 210) * 1mm);height:auto;flex:none;"
        + `background:${c("paper")};box-shadow:${c("shadow")}}`
        + `.stamp{color:${c("muted")};font:12px system-ui;margin:-10px 0 18px}`
        + "</style></head><body>"
        + `<div id="bar"><label>${esc(t("zoom.label"))} <select id="zoom">`
        + `<option value="fit">${esc(t("zoom.fit"))}</option>`
        + "<option value=\"0.5\">50 %</option><option value=\"0.75\">75 %</option>"
        + "<option value=\"1\">100 %</option><option value=\"1.5\">150 %</option>"
        + "<option value=\"2\">200 %</option></select></label><span id=\"status\"></span></div>"
        + "<div id=\"pages\"></div></body></html>");
      previewWindow.document.close();

      // Maximising is not something a page may do; asking the window manager for the
      // screen's full working area is the closest there is, and it is what the
      // features above already did.
      try { previewWindow.moveTo(previewWindow.screenX, previewWindow.screenY); } catch {}

      // The window's own document - never this designer's root. A rewrite that sent every
      // lookup to byId() took these three with it on 20 October 2026, and the window came up
      // empty: previewWindow.byId is not a function, caught and taken for a closed window.
      const popupZoomSelect = previewWindow.document.getElementById("zoom");
      if (popupZoomSelect) {
        popupZoomSelect.value = popupZoom;
        popupZoomSelect.onchange = () => {
          popupZoom = popupZoomSelect.value;
          try { localStorage.setItem("frisket.zoom.preview", popupZoom); } catch {}
          applyPopupZoom();
        };
      }
      // The window changing shape changes what "fit" means, same as the main window.
      previewWindow.addEventListener("resize", () => applyPopupZoom());

      pushPreview();
      updateDetachLabel();
    }

    /// Sends the pages across. Called at the end of every refresh, so the other screen
    /// is never showing an older section than this one.
    function pushPreview() {
      if (!previewWindow || previewWindow.closed) {
        if (previewWindow) { previewWindow = null; updateDetachLabel(); }
        return;
      }

      try {
        const pages = previewWindow.document.getElementById("pages");
        if (pages)
          pages.innerHTML = live === "window" && liveOn() ? enginePages() : ui.canvas.innerHTML;
        const status = previewWindow.document.getElementById("status");
        if (status) status.textContent = live === "window" ? liveStatusText() : "";
        applyPopupZoom();
      } catch {
        // Navigated away, or closed between the check and the write.
        previewWindow = null;
        updateDetachLabel();
      }
    }

    /// The detached window's own zoom - fitted by default, like the stage, but
    /// picked independently since a page read at its own size on a second monitor
    /// is not always the page fitted to the small canvas beside the properties.
    function applyPopupZoom(sheet) {
      if (!previewWindow || previewWindow.closed) return;

      let pages;
      try { pages = previewWindow.document.getElementById("pages"); } catch { return; }
      if (!pages) return;

      sheet = sheet || doc0().Dimension || { Width: 210, Height: 297 };
      pages.style.setProperty("--sheet", sheet.Width);
      pages.style.setProperty("--zoom", popupZoom !== "fit"
        ? popupZoom : fitInto(pages, sheet).toFixed(4));
    }

    function updateDetachLabel() {
      const button = byId("detach");
      if (button)
        button.textContent = previewWindow && !previewWindow.closed
          ? t("action.attached") : t("action.detach");
    }

    // --- resizable side panels ---------------------------------------------------
    // Widths live on <main> as custom properties, so the grid picks them up without
    // any layout code here, and survive a reload.
    // The panels keep the same widths in both arrangements: the designer is not what
    // changes on a wide screen, a second preview is simply added beside it. The stage
    // is measured in pixels like the rest, so one bar sizes it and it is remembered.
    const PANELS = { left: 260, right: 300, stage: 0 };   // 0: work it out from the window

    function setPanel(side, px) {
      const most = side === "stage" ? Math.max(400, (window.innerWidth || 1600) - 700) : 700;
      PANELS[side] = Math.max(side === "stage" ? 260 : 180, Math.min(most, px));

      uiRoot.querySelector("main").style.setProperty("--" + side, PANELS[side] + "px");
      try { localStorage.setItem("frisket.panels", JSON.stringify(PANELS)); } catch {}
    }

    function applyPanels() {
      const main = uiRoot.querySelector("main");

      // The first time, half of what the two side panels leave, so the canvas and the stage
      // share it; after that, whatever the bar was last dragged to. It was half the WINDOW,
      // written for a very wide screen, where it is generous - and when the engine's pages
      // came beside the canvas on an ordinary one, it left the canvas about 130 pixels of a
      // 1400-pixel window: a page nobody could click on. Measured on the designer itself
      // rather than on the window, since an element is as wide as its host makes it.
      if (!PANELS.stage) {
        const room = (main && main.clientWidth) || window.innerWidth || 1600;
        PANELS.stage = Math.round(Math.max(260, (room - PANELS.left - PANELS.right - 12) / 2));
      }

      for (const side of ["left", "right", "stage"])
        main.style.setProperty("--" + side, PANELS[side] + "px");
    }

    try {
      Object.assign(PANELS, JSON.parse(localStorage.getItem("frisket.panels") || "{}"));
    } catch {}

    applyShape();

    uiRoot.querySelectorAll(".splitter").forEach(bar => {
      bar.onpointerdown = down => {
        const side = bar.dataset.splits;
        const start = down.clientX;
        const from = PANELS[side];
        bar.classList.add("dragging");
        bar.setPointerCapture(down.pointerId);

        // Everything to the right of its own bar grows as the pointer moves left.
        bar.onpointermove = move => {
          const delta = move.clientX - start;
          setPanel(side, side === "left" ? from + delta : from - delta);
          applyZoom();
        };

        bar.onpointerup = () => {
          bar.classList.remove("dragging");
          bar.onpointermove = bar.onpointerup = null;
        };
      };
    });

    byId("lang").value = lang;
    byId("lang").onchange = e => setLanguage(e.target.value);

    byId("theme").innerHTML = themeOptions();
    byId("theme").value = theme;
    byId("theme").onchange = e => setTheme(e.target.value);

    // On "auto", follow the machine while it is open rather than only at load.
    window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => {
      if (theme === "auto") refresh();
    });

    // The engine's own faces, when the exact-preview server answers: the files it measures
    // and embeds, and the family it gives text that names none. Without them this page
    // draws a family with whatever the machine has installed under its name, and unnamed
    // text in the browser's sans-serif while the PDF has the engine's default. Laid out
    // again once they arrive, since the measurements are taken with them.
    function askServerForFonts() {
      const base = serverBase();
      if (base === null) return;
      send(base + "/fonts")
        .then(r => r.ok ? r.json() : Promise.reject(new Error(String(r.status))))
        .then(list => {
          const got = fontsFromServer(list, base);
          if (got.defaultFamily) setDefaultFace(faceOf(got.defaultFamily));
          if (typeof FontFace !== "function" || !document.fonts) { refresh(); return; }
          Promise.all(got.faces.map(d =>
            new FontFace(d.family, `url("${d.url}")`, { weight: d.weight, style: d.style })
              .load().then(face => document.fonts.add(face)).catch(() => {})))
            .then(() => {
              // The banner said the browser measures with its own fonts. It no longer does.
              const banner = uiRoot.querySelector('.banner[data-i18n="hint.approximate"]');
              if (banner) banner.dataset.i18n = "hint.approximateEngineFonts";
              applyStaticText();
              refresh();
            });
        })
        .catch(() => {});
    }

    applyStaticText();
    loadLibrary();
    refresh();
    askServerForFonts();
    if (liveOn()) askEngine();

    // @harness - check-against-engine.js plants its handles here, inside, where they can see.
    api = { loadReport, report: () => forEngine(report), setSubDocuments, setLive };
  }

  return {
    mount,
    loadReport: json => { mount(); api.loadReport(json); },
    /// Another library, or the same one once what it holds has changed.
    setSubDocuments: next => { seams.subDocuments = next; if (api) api.setSubDocuments(next); },
    /// Where the engine's pages go: "off", "canvas", "beside" or "window".
    setLive: mode => { seams.live = mode; if (api) api.setLive(mode); },
    /// The template as the engine reads it.
    report: () => api ? api.report() : null,
  };
}

// A plain page hands over a div of its own and is mounted at once: one designer, owning
// the document. Inside a <frisket-designer> there is no such div in the document - the
// element makes a designer of its own, in its own shadow root, and mounts that.
{
  const host = document.getElementById("frisket-designer-root");
  if (host)
    frisketDesigner({ root: document, host, themeTarget: document.documentElement }).mount();
}
