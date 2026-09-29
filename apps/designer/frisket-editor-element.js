/// `<frisket-editor>` - the content editor as a custom element.
///
/// Load frisket-core.js, frisket-runs.js, frisket-editor.js and then this, and a page
/// gets an editor by writing one tag. Everything it draws lives in a shadow root, so
/// the host's stylesheet cannot reach in and this one cannot leak out.
///
/// What a host CAN reach in with, and is meant to, is the palette: every colour the
/// chrome uses is a custom property (`--accent`, `--panel`, `--ink`…), and custom
/// properties inherit through a shadow boundary. Setting them on the element - or on
/// anything above it - themes the editor without touching a line of it.
///
/// What a host cannot change is the paper. `ON_PAPER` and `--paper` hold one value in
/// every theme, because the canvas is showing a printed page and a mark that moved with
/// the interface would make the tool disagree with the printer.
///
/// This works because frisket-editor.js was made to ask rather than assume. What this
/// element SETS: `uiRoot`, where its markup lives; `uiHost`, what the markup is planted
/// in; `themeTarget`, what carries the theme; `SHOWN`, what this face shows at all;
/// `renderTarget`, what is about to be produced; and `onChecks`, where the findings go.
/// What it CALLS: `applyTheme()`, `mount()` and `loadReport()`.
///
/// No total is given, here or in CLAUDE.md 5c-quater, because the two used to carry one
/// and disagreed: this comment said four and that section said five, the difference
/// being `selection()`, which is derived from `uiRoot` rather than set. A number in
/// prose is a claim like any other.
(function () {
  // Where our own files are, taken from this script's own URL rather than assumed to be
  // beside the host page - an element is meant to be loaded from somewhere else.
  const here = new URL(".", document.currentScript.src).href;

  class FrisketEditor extends HTMLElement {
    connectedCallback() {
      if (this.shadowRoot) return;   // moved in the DOM, not a second editor

      if (typeof mount !== "function") {
        this.textContent = "frisket-editor.js has not been loaded.";
        return;
      }

      // What this host wants shown, before a line of the editor runs. Absent means the
      // lean shape, because an element is by definition inside somebody else's
      // application: it already has a header, it already knows what it is producing,
      // and a second set of controls answering the same questions is worse than none.
      //
      // The plain page is the other way round and says so itself - it is the whole
      // application, so it keeps the lot.
      const on = (name, fallback) => this.hasAttribute(name)
        ? this.getAttribute(name) !== "false"
        : fallback;

      SHOWN = {
        chrome: on("chrome", false),
        panel: on("panel", false),
        files: this.getAttribute("files") || "toolbar",
      };

      // The web-safe families unless the host says otherwise. An embedded editor has no
      // preview server to ask - and no control to change its mind with - so asking one
      // means a request that fails and a poorer list to show for it. A host that DOES
      // have a font server writes fonts="library".
      // The room, before applyTheme() below stamps it. A face with no picker does not
      // read the preference stored by the face that has one - see chooseTheme.
      margins = this.getAttribute("margins");
      theme = chooseTheme(this.getAttribute("theme"));

      fontSource = chooseFontSource(this.getAttribute("fonts"));

      // What is about to be produced. The engine's own defaults - the richest output -
      // for the engine's own reason: a caller that says nothing is told nothing, rather
      // than warned about a loss that is not happening. `paged="false"` is what a host
      // building an email says, and it is the only way can.absoluteInFlow ever speaks.
      renderTarget = {
        Tagged: on("tagged", true),
        Interactive: on("interactive", true),
        Paged: on("paged", true),
      };

      // The findings, to whoever embedded this. They are computed on every redraw
      // whether or not there is a panel to paint them on - hiding the panel hides the
      // machinery from somebody typing a letter, it does not stop the checking. What a
      // host does with them is the host's to decide, which is the same answer this
      // project gives for a font's fsType and for an imported page.
      onChecks = found => {
        this._issues = found;
        this.dispatchEvent(new CustomEvent("checks", { detail: found }));
      };

      const root = this.attachShadow({ mode: "open" });
      root.innerHTML =
        '<link rel="stylesheet" href="' + here + 'frisket-chrome.css">' +
        '<link rel="stylesheet" href="' + here + 'frisket-editor.css">' +
        '<div id="frisket-editor-root"></div>';

      // The seams, set before a line of the editor runs.
      uiRoot = root;
      uiHost = root.getElementById("frisket-editor-root");
      themeTarget = this;

      applyTheme();   // it ran once at load against the document; this is our copy
      mount();

      if (this._pending) { loadReport(this._pending); this._pending = null; }
    }

    /// The document, out and in.
    ///
    /// Not a convenience. A host that hides Save - which is what an embedded editor
    /// does - has no other way to the JSON: the Save button built a Blob and clicked an
    /// anchor, and that was the only egress this editor had. Hiding it without putting
    /// this here would be shipping an editor whose work cannot be collected.
    get report() { return forEngine(report); }

    set report(value) {
      // Before connectedCallback, there is no editor to put it in yet. Kept rather than
      // dropped: a host setting a property on an element it has just created, before
      // the browser has upgraded it, is ordinary and must not lose the document.
      if (this.shadowRoot) loadReport(value); else this._pending = value;
    }

    /// What the checks found, last time anything was drawn. Also delivered as a
    /// `checks` event, for a host that would rather be told than ask.
    get issues() { return this._issues || []; }
  }

  if (!customElements.get("frisket-editor"))
    customElements.define("frisket-editor", FrisketEditor);
})();
