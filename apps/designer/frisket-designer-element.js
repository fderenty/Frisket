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
/// theme, the room a host declared, and where the findings go. What it calls on what comes
/// back: `mount()`, `loadReport()` and `report()`.
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

        // The findings, to whoever embedded this, as well as to the panel. Computed on
        // every redraw: a template its author is building should not reach the engine
        // with faults nobody was told about.
        onChecks: found => {
          this._issues = found;
          this.dispatchEvent(new CustomEvent("checks", { detail: found }));
        },
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

    /// What the checks found, last time anything was drawn. Also delivered as a `checks`
    /// event, for a host that would rather be told than ask.
    get issues() { return this._issues || []; }
  }

  if (!customElements.get("frisket-designer"))
    customElements.define("frisket-designer", FrisketDesigner);
})();
