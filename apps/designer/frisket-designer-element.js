/// `<frisket-designer>` - the template designer as a custom element.
///
/// Load frisket-core.js, frisket-runs.js, frisket-designer.js and then this, and a page gets
/// a designer by writing one tag. Everything it draws lives in a shadow root, so the host's
/// stylesheet cannot reach in and this one cannot leak out - the arrangement
/// <frisket-editor> already has, for the same reasons (CLAUDE.md 5c-quater).
///
/// What a host CAN reach in with is the palette: every colour the chrome uses is a custom
/// property, and custom properties inherit through a shadow boundary. What it cannot change
/// is the paper, which holds one value in every theme because it is showing a printed page.
///
/// Every element makes a designer of its own - `frisketDesigner(seams)` - so a page may hold
/// several, each with its template, its selection and its findings. What it hands that call:
/// the shadow root the markup lives in, the div it is planted in, itself as what carries the
/// theme, the room a host declared, where the findings go, the host's library of
/// sub-documents, where its engine is and how to reach it, where the engine's pages go at
/// first, the interface's language, and what of the header to show. What it calls on what
/// comes back: `mount()`, `loadReport()`, `report()`, `setSubDocuments()` and `setLive()`.
///
/// Absent, an attribute means the lean shape, as on <frisket-editor>: an element lives in
/// somebody else's application, which has already decided its language, its room and its
/// server. So `server` absent is no engine at all - never a guess at localhost.
///
/// Built on 20 October 2026, when the author decided the designer goes into a host
/// application's front end - for the few who compose templates, and possibly an AI working
/// from the JSON Schema.
(function () {
  // Where our own files are, taken from this script's own URL rather than assumed to be
  // beside the host page - an element is meant to be loaded from somewhere else.
  const here = new URL(".", document.currentScript.src).href;

  class FrisketDesigner extends HTMLElement {
    connectedCallback() {
      if (this.shadowRoot) return;   // moved in the DOM, not a second designer

      if (typeof frisketDesigner !== "function") {
        this.textContent = "frisket-designer.js has not been loaded.";
        return;
      }

      const on = (name, fallback) => this.hasAttribute(name)
        ? this.getAttribute(name) !== "false"
        : fallback;
      const element = this;

      const root = this.attachShadow({ mode: "open" });
      root.innerHTML =
        '<link rel="stylesheet" href="' + here + 'frisket-chrome.css">' +
        '<link rel="stylesheet" href="' + here + 'frisket-designer.css">' +
        '<div id="frisket-designer-root"></div>';

      this._designer = frisketDesigner({
        root,
        host: root.getElementById("frisket-designer-root"),
        themeTarget: this,
        theme: this.getAttribute("theme"),

        // Where the engine is - a base URL answering what apps/server answers, "/" for this
        // page's own origin - and how to reach it. Read at every request, so a host may set
        // `fetch` whenever its credentials are ready.
        server: this.getAttribute("server"),
        get fetch() { return element._fetch || null; },

        // Where the engine's pages go at first, and after how long a pause. The person's own
        // choice of place, once made, is kept over this: it is their screens.
        live: this.getAttribute("live"),
        liveDelay: this.hasAttribute("live-delay") ? Number(this.getAttribute("live-delay")) : undefined,

        // The interface's language - the page's, since the dictionary is one.
        lang: this.getAttribute("lang"),

        // The files stay, as on <frisket-editor>: a person may still open or save one. The
        // language, the room and the server are the host's, and stay hidden unless asked.
        shown: {
          files: on("files", true),
          language: on("language", false),
          rooms: on("rooms", false),
        },

        // The findings, to whoever embedded this, as well as to the panel. Computed on
        // every redraw: a template its author is building should not reach the engine
        // with faults nobody was told about.
        onChecks: found => {
          this._issues = found;
          this.dispatchEvent(new CustomEvent("checks", { detail: found }));
        },

        // The host's own pieces - an article of law, an address block - named st://reference
        // in the template and looked up by the same host when the engine composes.
        subDocuments: this._subDocuments || null,
      });

      this._designer.mount();
      if (this._pending) { this._designer.loadReport(this._pending); this._pending = null; }
    }

    /// The template, out and in - the way a host stores what its author composed, and
    /// hands it back the next time. Set before the element is connected, it is kept and
    /// loaded when it is: a host setting a property on an element it has just created is
    /// ordinary and must not lose the template.
    get report() { return this._designer ? this._designer.report() : (this._pending ?? null); }

    set report(value) {
      if (this._designer) this._designer.loadReport(value); else this._pending = value;
    }

    /// The host's library of sub-documents: `{ list(), find(reference) }`, either of which
    /// may answer with a promise. `list()` gives `[{ reference, title }]` for the palette;
    /// `find(reference)` the template, as JSON text or an object, or null. Set before the
    /// element is connected or after, and set again - the same object will do - once what
    /// the library holds has changed.
    get subDocuments() { return this._subDocuments ?? null; }

    set subDocuments(value) {
      this._subDocuments = value || null;
      if (this._designer) this._designer.setSubDocuments(this._subDocuments);
    }

    /// How the designer reaches the engine `server` names: a function shaped like the
    /// browser's fetch, for a host that adds its own credentials to every request. The
    /// browser's own otherwise.
    get fetch() { return this._fetch ?? null; }

    set fetch(value) { this._fetch = value || null; }

    /// Where the engine's pages go: "off", "canvas", "beside" or "window".
    setLive(mode) { if (this._designer) this._designer.setLive(mode); }

    /// What the checks found, last time anything was drawn. Also delivered as a `checks`
    /// event, for a host that would rather be told than ask.
    get issues() { return this._issues || []; }
  }

  if (!customElements.get("frisket-designer"))
    customElements.define("frisket-designer", FrisketDesigner);
})();
