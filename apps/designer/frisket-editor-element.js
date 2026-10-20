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
/// Every element makes an editor of its own - `frisketEditor(seams)` - so a page may hold
/// as many as it likes, each with its document, its selection and its findings. What it
/// HANDS that call: the shadow root the markup lives in, the div it is planted in, itself
/// as what carries the theme, what this face shows, what is about to be produced, where
/// the findings go, and whether to take the focus. What it CALLS on what comes back:
/// `mount()`, `loadReport()` and `report()`. The list of seams lives once, at the end of
/// `frisketEditor`, and is not repeated here.
///
/// It used to SET those seams instead, as variables of the one editor the page had - so
/// a second element set them over the first one's. That is the whole reason this is a
/// call now.
(function () {
  // Where our own files are, taken from this script's own URL rather than assumed to be
  // beside the host page - an element is meant to be loaded from somewhere else.
  const here = new URL(".", document.currentScript.src).href;

  class FrisketEditor extends HTMLElement {
    connectedCallback() {
      if (this.shadowRoot) return;   // moved in the DOM, not a second editor

      if (typeof frisketEditor !== "function") {
        this.textContent = "frisket-editor.js has not been loaded.";
        return;
      }
      const element = this;

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

      const root = this.attachShadow({ mode: "open" });
      root.innerHTML =
        '<link rel="stylesheet" href="' + here + 'frisket-chrome.css">' +
        '<link rel="stylesheet" href="' + here + 'frisket-editor.css">' +
        '<div id="frisket-editor-root"></div>';

      this._editor = frisketEditor({
        root,
        host: root.getElementById("frisket-editor-root"),
        themeTarget: this,

        // What this host wants shown. Absent means the lean shape, because an element is
        // by definition inside somebody else's application: it already has a header, it
        // already knows what it is producing, and a second set of controls answering the
        // same questions is worse than none. The plain page is the other way round and
        // says so itself - it is the whole application, so it keeps the lot.
        shown: {
          chrome: on("chrome", false),
          panel: on("panel", false),
          files: this.getAttribute("files") || "toolbar",
        },

        margins: this.getAttribute("margins"),

        // The room, and the families: what the host declared, if anything. A face with
        // no picker does not read the preference stored by the face that has one, so an
        // editor with no font control gets the web-safe families unless the host says
        // fonts="library" - with `server` saying where the engine is, since an element is
        // told of none it was not given, and it has no control to change its mind with.
        theme: this.getAttribute("theme"),
        fonts: this.getAttribute("fonts"),

        // Where the engine is whose fonts fonts="library" draws with - a base URL answering
        // what apps/server answers, "/" for this page's own origin - and how to reach it.
        // Absent, there is none: never a guess at localhost, which is what the editor did
        // for every host until 20 October 2026.
        server: this.getAttribute("server"),
        get fetch() { return element._fetch || null; },

        // What is about to be produced. The engine's own defaults - the richest output -
        // for the engine's own reason: a caller that says nothing is told nothing, rather
        // than warned about a loss that is not happening. `paged="false"` is what a host
        // building an email says, and it is the only way can.absoluteInFlow ever speaks.
        target: {
          Tagged: on("tagged", true),
          Interactive: on("interactive", true),
          Paged: on("paged", true),
        },

        // The findings, to whoever embedded this. They are computed on every redraw
        // whether or not there is a panel to paint them on - hiding the panel hides the
        // machinery from somebody typing a letter, it does not stop the checking. What a
        // host does with them is the host's to decide, which is the same answer this
        // project gives for a font's fsType and for an imported page.
        onChecks: found => {
          this._issues = found;
          this.dispatchEvent(new CustomEvent("checks", { detail: found }));
        },

        // HTML's own rule for a control: it takes the focus when it says autofocus, and
        // not otherwise. A page holding three editors that each took it on mounting
        // would open scrolled to the last one.
        autofocus: on("autofocus", false),
      });

      this._editor.mount();
      if (this._pending) { this._editor.loadReport(this._pending); this._pending = null; }
    }

    /// The document, out and in.
    ///
    /// Not a convenience. A host that hides Save - which is what an embedded editor
    /// does - has no other way to the JSON: the Save button built a Blob and clicked an
    /// anchor, and that was the only egress this editor had. Hiding it without putting
    /// this here would be shipping an editor whose work cannot be collected.
    get report() { return this._editor ? this._editor.report() : (this._pending ?? null); }

    set report(value) {
      // Before connectedCallback, there is no editor to put it in yet. Kept rather than
      // dropped: a host setting a property on an element it has just created, before
      // the browser has upgraded it, is ordinary and must not lose the document.
      if (this._editor) this._editor.loadReport(value); else this._pending = value;
    }

    /// What the checks found, last time anything was drawn. Also delivered as a
    /// `checks` event, for a host that would rather be told than ask.
    get issues() { return this._issues || []; }

    /// How the editor reaches the engine `server` names: a function shaped like the
    /// browser's fetch, for a host that adds its own credentials. The browser's own
    /// otherwise. Read at every request, so it may be set whenever the token is ready.
    get fetch() { return this._fetch ?? null; }

    set fetch(value) { this._fetch = value || null; }
  }

  if (!customElements.get("frisket-editor"))
    customElements.define("frisket-editor", FrisketEditor);
})();
