// Drives the flow editor headlessly: types into it, styles a range, inserts a
// picture, and writes the JSON it saves.
//
// The question a second face has to answer is not "does it run" but "does what it
// produces go through the engine unchanged". So this writes tests/output/editor/flow.json
// and the run is only finished once the CLI has read that file back:
//
//     node apps/designer/check-editor.js
//     dotnet <cli> check  -t tests/output/editor/flow.json --tagged
//     dotnet <cli> render -t tests/output/editor/flow.json -o tests/output/editor --format pdf,svg,mail
//
// There is no second format and no conversion step: typing makes an AdvancedTextItem,
// a picture makes an ImageItem with its bytes, and the engine reads exactly that.
const fs = require('fs');
const path = require('path');

/// Text as this harness reads it: line endings normalised to \n.
///
/// A fresh Windows clone hands every file over in CRLF - core.autocrlf, which is git's
/// own default there - and a pattern written against \n then reads something else. The
/// palette audit tells a theme's own rule from the last line of a selector list by the
/// comma before the line break, `(?<!,)\n`; in CRLF the character before \n is \r, so the
/// list that declares --paper for every room at once was read as a fourth room missing
/// all twenty-four names, and the run failed on a clone and passed in the tree it was
/// written in. Every text read goes through here, and the run checks that it does.
const readText = file => fs.readFileSync(file, 'utf8').replace(/\r\n?/g, '\n');

const DIR = 'apps/designer';
// The split page by default, and any face on the command line - which is how the
// single-file build gets checked by the very thing that checks the split one, rather
// than by a second check that would have to be kept in step with this one.
//
// A bare name is one of this directory's own faces; anything with a separator in it is
// a path from the repository root, which is where the built pages went the day they
// stopped living beside their parts.
const asked = process.argv[2] || 'frisket-editor.html';
const page = readText(
  asked.includes('/') || asked.includes(path.sep) ? asked : path.join(DIR, asked));

// The stylesheet too. Two rules put a drag handle in the same place and one hid the
// other; nothing in the model can see that, so it is read from where it happened.
const styles = [...page.matchAll(/<style>([\s\S]*?)<\/style>/g)].map(m => m[1]).join('\n')
  + [...page.matchAll(/<link[^>]+href="([^"]+\.css)"/g)]
      .map(m => readText(path.join(DIR, m[1]))).join('\n');

/// One rule out of the stylesheet, by selector. Named `cssFor` and not `rule`
/// because `rule` is taken twice in this file already - once by a LineItem - and a
/// third would be a shadowing waiting to be read wrongly.
///
/// Here rather than inside whichever block first wanted it: a second block wanting
/// the same reader is how a file ends up with two that disagree.
const cssFor = sel => {
  const m = styles.match(new RegExp(sel.replace(/[.>:#]/g, c => '\\' + c)
                                    + '\\s*\\{([^}]*)\\}'));
  // Declarations only. This file explains its rules inside them, so a check
  // asking whether a rule declares `min-width: 0` found those very words in the
  // sentence saying why it is there, and passed with the declaration deleted.
  return m ? m[1].replace(/\/\*[\s\S]*?\*\//g, ' ') : '';
};

// --- a DOM small enough to be honest about what it is ----------------------
let selectionRange = null;

class Node2 {
  constructor(tag) {
    this.tagName = (tag || '').toUpperCase();
    this.childNodes = [];
    this.nodeType = 1;
    this.style = new Proxy({ setProperty(k, v) { this[k] = v; }, removeProperty() {} },
                           { get: (t, k) => (k in t ? t[k] : ''), set: (t, k, v) => (t[k] = v, true) });
    // A real dataset stores strings, always: `node.dataset.index = 0` reads back as
    // "0". The stub kept the number, so `dataset.index === "0"` - which is how the
    // page finds a block after a redraw - was false for every block, and a check that
    // walked that path found nothing and blamed the page.
    this.dataset = new Proxy({}, { set: (t, k, v) => (t[k] = String(v), true) });
    this.classList = {
      _: new Set(),
      add: (...c) => c.forEach(x => this.classList._.add(x)),
      remove: (...c) => c.forEach(x => this.classList._.delete(x)),
      contains: c => this.classList._.has(c),
      toggle: () => {}
    };
    this._listeners = {};
    this.checked = true;
    this._value = '';
    this.selectedIndex = 0;
    this.files = [];
  }

  // A select's value and its selectedIndex are one state, not two. Written as two
  // plain fields the double let them disagree: assigning a value left a stale
  // selectedIndex of -1 lying about, so a check asking "does this list report
  // nothing" read the leftover and passed however the page behaved. The sabotage
  // that made a mixed selection report its first run's value went green on it.
  //
  // Faithful in both directions, which is all this needs: giving it a value selects
  // something, and selecting nothing empties it - a real select returns "" when its
  // selectedIndex is -1. Section 5c, again: the Range that answered '' to toString
  // and the dataset that kept numbers were the same fault, and each one made exactly
  // the check aimed at it unanswerable.
  get value() { return this.selectedIndex < 0 ? '' : this._value; }
  set value(v) { this._value = String(v); this.selectedIndex = 0; }
  get className() { return [...this.classList._].join(' '); }
  set className(v) { this.classList._ = new Set(String(v).split(/\s+/).filter(Boolean)); }
  appendChild(n) { n.parentNode = this; this.childNodes.push(n); return n; }
  insertBefore(n, before) {
    n.parentNode = this;
    const at = this.childNodes.indexOf(before);
    this.childNodes.splice(at < 0 ? 0 : at, 0, n);
    return n;
  }
  removeChild(n) { this.childNodes = this.childNodes.filter(c => c !== n); }
  addEventListener(type, fn) { (this._listeners[type] ??= []).push(fn); }
  // Bubbling, because the page relies on it: a table listens for input so its rails
  // can follow a row that has just grown, and that input is raised on a cell three
  // levels down. A stub that delivered only to the target said yes to a rail that
  // never moved.
  fire(type, ev = {}) {
    let stopped = false;
    const already = ev.stopPropagation;
    ev.stopPropagation = () => { stopped = true; if (already) already(); };
    for (let node = this; node && !stopped; node = node.parentNode) {
      (node._listeners?.[type] || []).forEach(f => f(ev));
      // And the on<type> property, which a browser calls and this stub did not - so a
      // check that pressed a button wired with `el(id).onclick = ...` pressed nothing
      // and said the page did nothing. Every button in the toolbar is wired that way.
      if (typeof node["on" + type] === "function") node["on" + type].call(node, ev);
    }
  }
  focus() {}
  click() {}
  get isContentEditable() { return this.contentEditable === 'true'; }
  get textContent() { return this.childNodes.map(c => c.textContent).join(''); }
  set textContent(v) { this.childNodes = [new Text2(v)]; }
  set innerHTML(v) {
    // Markup laid into the tree declares its ids - and REPLACES what this node declared
    // before, because that is what assigning innerHTML does. Adding and never removing
    // was the obvious shortcut and it put the old hole straight back: mount the full
    // face, then the lean one, and the lean one still answered for the full one's panel.
    // The check written to prove the findings survive with no panel to paint them was
    // reading a panel that was still there.
    //
    // Per node, so one host being rebuilt does not forget another's.
    this._ids = new Set([...String(v).matchAll(/\bid="([A-Za-z][\w-]*)"/g)].map(m => m[1]));
    planted.add(this);
    if (v === '') this.childNodes = []; else this.childNodes = [new Text2(String(v))];
  }
  get innerHTML() { return this.textContent; }
  contains(n) {
    if (n === this) return true;
    return this.childNodes.some(c => c.contains && c.contains(n));
  }
  querySelector(sel) { return this.querySelectorAll(sel)[0] || null; }
  querySelectorAll(sel) {
    const out = [];
    // ANY single class, not the one class this happened to need. It knew `.surface`
    // and nothing else, so a check about a cell in a pair could not find the cell -
    // and a double that cannot be asked a question is a question nobody asks. The
    // fault that sent me looking lived inside exactly such a cell.
    const oneClass = /^\.([a-z][\w-]*)$/.exec(sel);
    if (oneClass || /^[a-z]+$/.test(sel)) {
      (function walk(n) {
        for (const c of n.childNodes) {
          if (!c.classList) continue;
          const hit = oneClass ? c.classList.contains(oneClass[1])
                    : c.tagName === sel.toUpperCase();
          if (hit) out.push(c);
          if (c.childNodes) walk(c);
        }
      })(this);
      return out;
    }
    const want = /\.block\[data-index="(\d+)"\]/.exec(sel);
    (function walk(n) {
      for (const c of n.childNodes) {
        if (want && c.classList && c.classList.contains('block') && c.dataset.index === want[1]) out.push(c);
        if (c.childNodes) walk(c);
      }
    })(this);
    return out;
  }
  // Real, because a page declares what it wants shown on the div it hands over and
  // three no-ops made every page identical. Kept as small as the thing it models: a
  // map of strings, which is what an attribute is.
  // A real <select> always has this, and the double not having it made a guard that
  // asks "does this list have anything to choose from" throw rather than answer. The
  // question is a fair one - "nothing chosen" and "nothing to choose from" are
  // different states, and reading them as one blanked the family list at startup.
  get options() {
    return this.childNodes.filter(n => n.tagName && n.tagName.toLowerCase() === 'option');
  }

  setAttribute(name, value) { (this._attrs ??= {})[name] = String(value); }
  removeAttribute(name) { if (this._attrs) delete this._attrs[name]; }
  getAttribute(name) { return this._attrs && name in this._attrs ? this._attrs[name] : null; }
  hasAttribute(name) { return !!(this._attrs && name in this._attrs); }
  // Enough geometry for the arrow keys - atEdge compares the caret's rectangle with
  // the block's, and a stub that answered nothing would make every key an edge - and
  // enough for a table, whose rails have to land on a particular column and a
  // particular row. Cells stand 100 wide side by side and rows 20 tall stacked: made
  // up, but made up consistently, which is all "is each button over its own column"
  // needs. One rect for every element answered yes to a rail piled in the corner.
  getBoundingClientRect() {
    if (this._rect) return this._rect;
    const at = this.parentNode ? this.parentNode.childNodes.indexOf(this) : 0;
    if (this.tagName === 'TD' || this.tagName === 'TH')
      return { left: at * 100, right: at * 100 + 100, width: 100,
               top: 0, bottom: 20, height: 20 };
    if (this.tagName === 'TR')
      return { left: 0, right: 300, width: 300,
               top: at * 20, bottom: at * 20 + 20, height: 20 };
    return { top: 0, bottom: 20, height: 20, left: 0, right: 100, width: 100 };
  }
}

class Text2 {
  constructor(text) { this.nodeType = 3; this.textContent = String(text); }
  get childNodes() { return []; }
  contains(n) { return n === this; }
}

const byId = {};
global.Node = { TEXT_NODE: 3, ELEMENT_NODE: 1 };
global.NodeFilter = { SHOW_TEXT: 4 };

/// Every text node under a host, in document order.
const textNodesIn = host => {
  const out = [];
  (function walk(n) {
    if (!n) return;
    if (n.nodeType === 3) { out.push(n); return; }
    (n.childNodes || []).forEach(walk);
  })(host);
  return out;
};

/// A Range that actually models two boundaries and the text between them.
///
/// The stub before this one answered '' to toString() and dropped setStart on the
/// floor, which was enough while nothing asked what was selected. The moment the
/// question became "does applying a style leave the words selected", a Range that
/// cannot say what it covers makes the check vacuous by construction - so it had to
/// grow before the check could be written, not after.
class Range2 {
  constructor() {
    this.startContainer = null; this.startOffset = 0;
    this.endContainer = null; this.endOffset = 0; this.collapsed = true;
  }
  setStart(n, o) { this.startContainer = n; this.startOffset = o; this._settle(); }
  setEnd(n, o) { this.endContainer = n; this.endOffset = o; this._settle(); }
  selectNodeContents(host) {
    this._host = host;
    const t = textNodesIn(host);
    this.startContainer = t[0] || host;
    this.startOffset = 0;
    this.endContainer = t[t.length - 1] || host;
    this.endOffset = t.length ? t[t.length - 1].textContent.length
                              : (host.childNodes || []).length;
    this._settle();
  }
  collapse(toStart) {
    if (toStart) { this.endContainer = this.startContainer; this.endOffset = this.startOffset; }
    else { this.startContainer = this.endContainer; this.startOffset = this.endOffset; }
    this._settle();
  }
  _settle() {
    this.collapsed = this.startContainer === this.endContainer
                     && this.startOffset === this.endOffset;
    this.commonAncestorContainer = this._root();
  }
  _root() {
    const up = n => { const c = []; for (let x = n; x; x = x.parentNode) c.push(x); return c; };
    if (!this.startContainer || !this.endContainer) return this._host || null;
    const mine = up(this.startContainer), theirs = new Set(up(this.endContainer));
    return mine.find(n => theirs.has(n)) || this._host || null;
  }
  /// Both boundaries flattened to a character position, so an element boundary - which
  /// counts children, not characters - and a text boundary can be compared at all.
  _flat(container, offset, nodes) {
    if (!container) return 0;
    if (container.nodeType === 3) {
      let n = 0;
      for (const t of nodes) { if (t === container) return n + offset; n += t.textContent.length; }
      return n;
    }
    const before = new Set();
    (container.childNodes || []).slice(0, offset).forEach(k => textNodesIn(k).forEach(t => before.add(t)));
    let n = 0;
    for (const t of nodes) if (before.has(t)) n += t.textContent.length;
    return n;
  }
  toString() {
    const root = this._root();
    if (!root) return '';
    const nodes = textNodesIn(root);
    const all = nodes.map(t => t.textContent).join('');
    const a = this._flat(this.startContainer, this.startOffset, nodes);
    const b = this._flat(this.endContainer, this.endOffset, nodes);
    return all.slice(Math.min(a, b), Math.max(a, b));
  }
}
let declared = null;

// Every node some markup was laid into, so the ids it currently declares can be asked
// for rather than accumulated.
const planted = new Set();

/// Does a node by this name exist right now? The page's own body and anything the
/// script named itself, plus whatever the markup currently in each host declares.
const exists = id => declared.has(id)
                  || [...planted].some(node => node._ids && node._ids.has(id));
global.document = {
  documentElement: new Node2('html'),
  createElement: t => new Node2(t),
  createTextNode: t => new Text2(t),
  createRange: () => new Range2(),
  createTreeWalker: host => {
    const nodes = textNodesIn(host);
    let i = -1;
    return { nextNode: () => (++i < nodes.length ? nodes[i] : null) };
  },
  // Only what some markup declares - never anything asked for.
  //
  // This invented a node for every id it was ever handed, which is the reason nothing
  // about the editor's markup could be checked here. `uiHost.innerHTML = EDITOR_MARKUP`
  // becomes one text node under this double (see the innerHTML setter above), so no
  // element the markup declares has ever existed in this tree; inventing them on demand
  // hid that completely. Delete a control from the markup, leave its reader in place,
  // and a browser throws at mount while this said `all good` - measured, twelve ids
  // deleted, 124 checks and not one failure.
  //
  // `declared` is filled once the page and its scripts have been read, below. Before
  // that it is null and the old behaviour stands, because the harness builds a few
  // nodes of its own while setting up and none of them is the subject of a check.
  getElementById: id => (declared && !exists(id))
    ? null
    : (byId[id] ??= new Node2('div')),
  querySelector: () => new Node2('div'),
  querySelectorAll: () => [],
  addEventListener(type, fn) { (this._l ??= {})[type] = [...(this._l?.[type] || []), fn]; },
  fire(type, ev) { (this._l?.[type] || []).forEach(f => f(ev)); },

  // Stands in for the browser, and on purpose does not raise an input event: a
  // browser does not reliably raise one for execCommand, and it was the editor
  // waiting for that event that lost every line break in a paragraph the moment
  // anything caused a redraw. Handing this stub the courtesy of an input event
  // would put the defect back out of reach.
  execCommand(command, _ui, value) {
    if (!currentEditable) return false;
    if (command === "insertLineBreak") { currentEditable.appendChild(new Node2("br")); return true; }
    if (command === "insertText") { currentEditable.appendChild(new Text2(String(value))); return true; }
    return false;
  },
  title: ''
};

// Where execCommand would act: the browser keeps this in the selection, we keep it
// beside it. Set by the checks that press a key.
let currentEditable = null;
global.window = { getSelection: () => ({ rangeCount: selectionRange ? 1 : 0,
                                         getRangeAt: () => selectionRange,
                                         removeAllRanges() { selectionRange = null; },
                                         addRange(r) { selectionRange = r; } }) };
global.localStorage = { getItem: () => null, setItem() {} };
global.alert = m => { throw new Error('alert: ' + m); };
global.FileReader = class { readAsDataURL() {} readAsText() {} };
global.Blob = class {};
global.URL = { createObjectURL: () => '', revokeObjectURL() {} };
// Run it, rather than dropping it on the floor. A table's rails place themselves the
// same way the picture's grip does - once the browser has measured - so a stub that
// never called back left the one path that decides where those buttons go unwalked,
// and they were reported piled in a corner over the block above.
global.requestAnimationFrame = fn => { fn(0); return 0; };

// --- load the same three files the page loads, in the same order -----------
const linked = [...page.matchAll(/<script src="([^"]+)"><\/script>/g)]
  .map(m => readText(path.join(DIR, m[1])));
const inline = [...page.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);

const script = [...linked, ...inline].join('\n');

// Every id any markup declares - the editor's own EDITOR_MARKUP, which is a template
// literal inside frisket-editor.js, and the page's body. Found rather than listed: a
// harness keeping its own roll of ids is one control away from checking less than it
// says (section 16), and the whole point here is to notice a control that has gone.
//
// The two sources are read together on purpose. The host div the editor mounts into
// (`frisket-editor-root`) is the PAGE's, not the editor's, and `mount()` is reached
// through `document.getElementById` finding it - so reading only the markup would stop
// the editor mounting at all, and every check after it would be measuring nothing.
// What exists is what something PLANTED, never what a string mentions.
//
// The page's own body, plus every id the script gives a node it built (`none.id =
// "colourClear"` for the colour menu's no-colour cell), plus - added as they are laid
// down, by the innerHTML setter above - the ids of whatever markup actually reaches the
// tree.
//
// Reading the script's text instead was right while one markup string was the only
// markup there was. The moment a face composes it is wrong: every piece is a literal in
// the file whether that face plants it or not, so the double would answer for the panel
// fields on a face that has no panel, and the hole this whole change closes would be
// back with a stricter coat on.
declared = new Set([
  ...[...page.matchAll(/\bid="([A-Za-z][\w-]*)"/g)].map(m => m[1]),
  ...[...script.matchAll(/\.id\s*=\s*"([A-Za-z][\w-]*)"/g)].map(m => m[1]),
]);

// What the page says on the div it hands over. Read from the page's own markup rather
// than assumed, because that declaration IS the difference between the face that ships
// and the face for working on the editor - and with three no-op attribute methods both
// fell back to the same shape and this harness reported the same run for either.
{
  const tag = page.match(/<div\s+id="frisket-editor-root"([^>]*)>/);
  if (tag) {
    const host = document.getElementById('frisket-editor-root');
    for (const m of tag[1].matchAll(/([a-z-]+)="([^"]*)"/g)) host.setAttribute(m[1], m[2]);
  }
}

// The handles go inside the editor, at the line that says so. An editor is a call since
// 18 October 2026, so its state is out of reach of anything appended after the file -
// and a harness that appended them anyway would reach nothing and check nothing, so a
// missing or doubled mark stops the run rather than letting it say all good.
const HARNESS_MARK = '// @harness';
const withHandles = (code, handles) => {
  const at = code.indexOf(HARNESS_MARK);
  if (at < 0 || code.indexOf(HARNESS_MARK, at + 1) >= 0)
    throw new Error(`the editor needs exactly one "${HARNESS_MARK}" line for the handles`);
  return code.slice(0, at) + handles + NL_FOR_HANDLES + code.slice(at);
};
const NL_FOR_HANDLES = '\n  ';

// What a browser makes global by loading a classic script - a function declared at its
// top level - this eval keeps to itself, the core being strict. The element looks the
// editor up by name, as it does in a page, so it is put where a page would put it.
const EXPOSE = '; globalThis.frisketEditor = frisketEditor;';

// `let`, because a page may hold several editors and each one plants its handles in
// whatever this names at the moment it is made - see the end of this file.
let held = {};
eval(withHandles(script, `
  held.report = () => report;
  held.body = body;
  held.insertAfterCurrent = insertAfterCurrent;
  held.emptyParagraph = emptyParagraph;
  held.applyStyle = applyStyle;
  held.reselect = reselect;
  held.setInk = setInk;
  held.inkInUse = inkInUse;
  held.fillSwatches = fillSwatches;
  held.swatchLimit = () => SWATCH_LIMIT;
  held.newItem = newItem;
  held.setCached = v => { cached = v; };
  held.showActiveStyles = showActiveStyles;
  held.el = el;
  held.loadReport = loadReport;
  held.markCurrent = markCurrent;
  held.shownInk = () => shownInk;
  held.currentItem = currentItem;
  held.current = () => current;
  held.currentInk = () => currentInk;
  held.chooseFontSource = chooseFontSource;
  held.chooseTheme = chooseTheme;
  held.chooseMargins = chooseMargins;
  held.setTheme = v => { theme = v; };
  held.fontSource = () => fontSource;
  held.setFontSource = v => { fontSource = v; };
  held.receiveFonts = receiveFonts;
  held.receiveFontsReset = () => { libraryFamilies = null; libraryDefault = null; };
  held.defaultFace = () => unnamedHere;
  held.mailFace = () => MAIL_FACE;
  held.webSafe = () => WEB_SAFE;
  held.mount = mount;
  held.shown = next => { SHOWN = next; };
  held.onChecks = fn => { onChecks = fn; };
  held.target = () => renderTarget;
  held.setTarget = t => { renderTarget = t; };
  held.runChecks = runChecks;
  held.pending = () => pending;
  held.clearPending = () => { pending = null; };
  held.cached = () => cached;
  held.setCurrent = n => { current = n; };
  held.draw = draw;
  held.blockFor = blockFor;
  held.applyAlignment = applyAlignment;
  held.alignmentOf = alignmentOf;
  held.moveBlock = moveBlock;
  held.bandWidth = bandWidth;
  held.surfaceToRuns = surfaceToRuns;
  held.pairUp = pairUp;
  held.detach = detach;
  held.canPair = canPair;
  held.canDropInCell = canDropInCell;
  held.putInCell = putInCell;
  held.naturalWidth = naturalWidth;
  held.isPair = isPair;
  held.newTable = newTable;
  held.addRow = addRow; held.dropRow = dropRow;
  held.addColumn = addColumn; held.dropColumn = dropColumn;
  held.fillBlock = fillBlock;
  held.validate = validate;
  held.applyVertical = applyVertical;
  held.verticalOf = verticalOf;
  held.uiRoot = () => uiRoot;
`) + EXPOSE);

let failures = 0;
let checked = 0;
const check = (name, ok, detail = '') => {
  console.log(`  ${ok ? '✓' : '✗'} ${name}${detail ? ': ' + detail : ''}`);
  checked++; if (!ok) failures++;
};

console.log('The flow editor, driven headlessly');

// Where the text of a block actually lives. It used to be the block; it is now a
// surface inside it, so that a handle or a label can be added to a block without
// becoming part of somebody's paragraph - which is exactly what happened twice.
// Asked for rather than assumed, so the next change of shape breaks one line.
const surfaceOf = node => node.querySelector('.surface') || node;

const rep = held.report();
check('it opens on one empty paragraph',
      held.body().Childs.length === 1 && held.body().Childs[0].$type === 'AdvancedTextItem');

// Type into the first block by firing the block's own input handler over a surface
// holding what a browser would hold. Setting item.Runs directly is what the first
// version of this check did, and it went past the very path under test: the block
// carried its kind label as a child element, so reading the runs back concatenated
// "Advanced text" into the paragraph and saved it into the document. A check that
// skips the path it is checking proves nothing - CLAUDE.md §2, from the inside.
const block = held.blockFor(held.body().Childs[0], 0);

// Appended, never assigned. Replacing childNodes would wipe out whatever blockFor
// put in the block - which is exactly the thing under test - and the check would
// then pass over a block carrying a stray label. It did, once.
const typing = surfaceOf(block);
typing.appendChild(new Text2('Frisket keeps the model in one place.'));
typing.fire('input');

const para = held.body().Childs[0];
check('typing reaches the runs, and nothing else does',
      para.Runs.length === 1 && para.Runs[0].Text === 'Frisket keeps the model in one place.',
      JSON.stringify(para.Runs.map(r => r.Text)));

// A second block, and a picture between them.
held.setCurrent(null);
held.insertAfterCurrent(held.emptyParagraph());
held.body().Childs[held.body().Childs.length - 1].Runs =
  [{ Text: 'A second paragraph, typed after the first.', Font: null }];

const picture = held.newItem('ImageItem');
// A picture the engine already embeds elsewhere, lifted out of a template the suite
// wrote. It proves the path rather than the bytes - and a made-up PNG header is
// refused, correctly, by the engine's own reader, which is worth knowing: a picture
// with an alpha channel (any screenshot) fails at render time, not at edit time.
const borrowed = JSON.parse(readText('tests/output/acme/acme.template.json'));
let bytes = null;
(function findImage(n) {
  if (bytes || !n || typeof n !== 'object') return;
  if (n.$type === 'ImageItem' && n.Source && n.Source.Data) { bytes = n.Source; return; }
  for (const v of Object.values(n)) findImage(v);
})(borrowed);
if (!bytes) { console.log('  ! no embedded picture in acme.template.json; run the suite first'); process.exit(1); }
picture.Source = bytes;
picture.AltText = 'A small picture';
held.body().Childs.push(picture);

check('a picture is an ImageItem carrying its own bytes',
      picture.$type === 'ImageItem' && picture.Source.Data.length > 0);

// Style a range of the first paragraph, the way the toolbar does - and get there the
// way the toolbar gets there, through a selection.
//
// Setting `cached` by hand went straight past the only thing that ever sets it, and
// so past the fault: selectionchange bailed on `!current.isContentEditable`, which is
// false for every paragraph since the text moved into a surface of its own. Bold,
// italic, colour and size all read `cached`, so the whole toolbar had gone quiet and
// this check said yes throughout.
{
  const para = held.body().Childs[0];
  const block = held.blockFor(para, 0);
  const surface = surfaceOf(block);
  held.setCurrent(block);

  // A range over the first seven characters, in the terms offsetsIn reads.
  const text = para.Runs.map(r => r.Text).join('');
  selectionRange = { commonAncestorContainer: surface, startContainer: surface,
                     startOffset: 0, toString: () => text.slice(0, 7) };

  document.fire('selectionchange');
  check('a selection reaches the toolbar at all', held.cached() !== null,
        JSON.stringify(held.cached()));

  // --- and a style leaves those same words selected -------------------------
  // Reported from the browser after months of it: select, press Bold, and the
  // selection is gone with the caret at the foot of the paragraph. Applying a style
  // rebuilds the sheet from the model, so every node the selection pointed at goes
  // with it, and focusBlock then collapsed to "end". The buttons already prevented
  // the default on mousedown to hold the selection while pressed - the redraw undid
  // that a line later.
  //
  // This check could not have been written against the old stub, whose Range answered
  // '' to toString() and dropped setStart: it would have compared two empty strings
  // and passed. Hence the length guard below, which is the same lesson from the other
  // end - an assertion true for want of matter.
  {
    held.draw();

    const drawn = held.el('sheet').querySelector('.block[data-index="0"]');
    check('the redrawn sheet holds a block to select in', !!drawn);

    if (drawn) {
      const node = surfaceOf(drawn);
      held.setCurrent(drawn);

      // Laid by hand rather than through rangeAt: a check that builds its expectation
      // with the code under test is comparing the code with itself.
      const texts = [];
      (function walk(n) {
        if (n.nodeType === 3) texts.push(n);
        else (n.childNodes || []).forEach(walk);
      })(node);

      const range = document.createRange();
      range.setStart(texts[0], 2);
      range.setEnd(texts[0], 7);
      selectionRange = range;
      document.fire('selectionchange');

      const wanted = String(range);
      check('a real selection covers real characters', wanted.length === 5,
            JSON.stringify(wanted));
      check('and the toolbar reads it', JSON.stringify(held.cached()) === '{"start":2,"end":7}',
            JSON.stringify(held.cached()));

      held.applyStyle('Bold');

      check('applying a style leaves the same words selected',
            String(selectionRange) === wanted && wanted.length === 5,
            `"${String(selectionRange)}" was "${wanted}"`);
      check('and the toolbar still knows what is selected',
            JSON.stringify(held.cached()) === '{"start":2,"end":7}',
            JSON.stringify(held.cached()));

      // And the selection has to reach the sheet on screen, not the nodes the redraw
      // threw away - which still carry their text, so a check reading only the
      // characters passes over a selection attached to nothing. Section 2: check that
      // the parts reach each other, never that they exist.
      check('and it is attached to the sheet on screen',
            held.el('sheet').contains(selectionRange.startContainer),
            selectionRange.startContainer && selectionRange.startContainer.textContent);

      // And where the numbers asked for cannot be honoured. reselect is handed
      // offsets; if the text is shorter than they say, the range laid is not the range
      // requested - so cached is computed from what was really selected rather than
      // from what was asked, and the toolbar cannot report a selection the page does
      // not have. Without a case that reaches it, that line was unfalsifiable: taking
      // it out left every check above green.
      const full = surfaceOf(held.el('sheet')
        .querySelector('.block[data-index="0"]')).textContent.length;
      held.reselect(0, { start: 2, end: 999 });
      check('a selection that cannot be honoured is reported as what was really made',
            JSON.stringify(held.cached()) === JSON.stringify({ start: 2, end: full }),
            `${JSON.stringify(held.cached())} of ${full}`);

      // Put the paragraph and the selection back where they were found. The checks
      // below are calibrated on a paragraph nobody has styled and on a selection of
      // its first seven characters, and one of them broke the moment this block was
      // written - which is how a check that leaves state behind announces itself.
      held.setCached({ start: 2, end: 7 });       // the impossible one left 2..37 behind
      held.applyStyle('Bold');                    // the same range, toggled off
      check('and pressing it again gives the paragraph back',
            held.body().Childs[0].Runs.length === 1,
            held.body().Childs[0].Runs.map(r => (r.Font && r.Font.Bold ? '[b]' : '') + r.Text).join(' | '));

      held.setCurrent(held.blockFor(held.body().Childs[0], 0));
      held.setCached({ start: 0, end: 7 });
    }
  }

  // What the left of the toolbar does with a caret rather than a selection.
  //
  // It used to do nothing, and the buttons were dimmed to say so. Now it arms the style
  // for the next character, so they are live again - and this check failed the moment
  // that changed, which is the only reason it is worth having. What must never come
  // back is the middle state: a button that looks pressable and does nothing.
  //
  // Only the four named controls are read, and that is a real limit rather than a
  // choice: the double returns [] for querySelectorAll, so the [data-style] buttons are
  // not reachable from here and an assertion about them would pass on an empty list.
  // They take the same flag on the same line.
  {
    const was = held.cached();
    const named = ['family', 'colour', 'colourClear', 'size'];
    const state = () => named.map(id => held.el(id).disabled);

    held.clearPending();
    held.setCached({ start: 2, end: 2 });
    held.showActiveStyles();
    const withCaret = state();

    check('a caret leaves the style controls live, because they now arm',
          withCaret.every(d => d === false),
          named.map((n, i) => n + '=' + withCaret[i]).join(' '));

    // And pressing one arms rather than doing nothing - the half that earns the line
    // above. Bold on a caret in plain text arms Bold; pressing it again puts it back.
    held.applyStyle('Bold');
    const armed = held.pending();
    check('bold at a caret arms it for the next character',
          !!(armed && armed.font && armed.font.Bold === true && armed.at === 2),
          JSON.stringify(armed));

    held.applyStyle('Bold');
    const off = held.pending();
    check('and pressing it again puts it back',
          !!(off && off.font && off.font.Bold === false), JSON.stringify(off));

    held.clearPending();
    held.setCached(was);
  }

  // --- and the two lists say what is under the caret --------------------------
  // Reported from the browser: move onto text set in Huge and every button switches
  // over while the size list sits where it was. The loop that touched those controls
  // only ever enabled them - it never gave either one a value - and the size list went
  // further and put itself back to Normal after acting, "it acts, it does not hold a
  // state". A decision, and the wrong one for a control in a row of stateful ones.
  //
  // What is asked here is what the toolbar COMPUTES. That it is asked at all when the
  // caret moves is settled above, where a real selection is laid and reaches `cached`
  // through selectionchange, which then calls showActiveStyles - so this drives that
  // one function directly rather than building three ranges to reach it three times.
  {
    const was = held.cached();
    const para = held.body().Childs[0];
    const before = JSON.parse(JSON.stringify(para.Runs));

    para.Runs = [{ Text: 'plain ', Font: null }, { Text: 'huge', Font: { Size: 2.5 } }];
    held.setCurrent(held.blockFor(para, 0));
    held.clearPending();

    const sizeAt = at => {
      held.setCached({ start: at, end: at });
      held.showActiveStyles();
      return held.el('size').value;
    };

    // The plain half first. Without it, a list stuck on Huge would pass the next
    // check and fail nothing - which is the shape the fault actually had, one value
    // short of moving.
    check('a caret in unstyled text reports Normal', sizeAt(3) === '', `"${sizeAt(3)}"`);
    check('and a caret in styled text reports the size that is there',
          sizeAt(9) === '2.5', `"${sizeAt(9)}"`);

    // A selection across both agrees on nothing, and a list that kept its last value
    // would be stating a size the text has not got - worse than saying nothing.
    held.setCached({ start: 0, end: 10 });
    held.showActiveStyles();
    check('and a selection spanning two sizes reports neither',
          held.el('size').selectedIndex === -1,
          `selectedIndex ${held.el('size').selectedIndex}, value "${held.el('size').value}"`);

    // An EMPTY paragraph, which is what a document looks like when it opens - and the
    // case that was reported. There is no run to read, so the first writing of this
    // returned no keys at all and both lists drew themselves blank; a second later the
    // font list filled itself in on its own, which was the server's font list arriving
    // and rebuilding the options, and looked like a page repairing itself.
    //
    // A caret always has an answer - the style the next character would take - and
    // where there is no run it is the document's default. Not "no answer": that is
    // reserved for a selection whose runs disagree, and the two have to be told apart
    // or a document with nothing in it reports the same as one with two fonts in it.
    //
    // Which is only askable because the double now models a select honestly: -1 and
    // "the empty option" both read "" through .value, so the state has to be read from
    // selectedIndex, and until an hour ago it kept whichever it was told last.
    para.Runs = [{ Text: '', Font: null }];
    held.setCurrent(held.blockFor(para, 0));
    held.setCached({ start: 0, end: 0 });
    held.showActiveStyles();

    check('an empty paragraph reports the document default, not silence',
          held.el('family').selectedIndex >= 0 && held.el('size').selectedIndex >= 0,
          `family ${held.el('family').selectedIndex}, size ${held.el('size').selectedIndex}`);

    check('and the default is what it reports',
          held.el('family').value === '' && held.el('size').value === '',
          `"${held.el('family').value}" / "${held.el('size').value}"`);

    para.Runs = before;
    held.setCurrent(held.blockFor(para, 0));
    held.setCached(was);
  }
}

// --- what is hidden is hidden ------------------------------------------------
// `hidden` works by way of `display: none` in the browser's own sheet, at the very
// bottom of the cascade, so any author rule that sets a display un-hides what it
// matches - and the button toggling the attribute then appears to do nothing. That is
// how the colour menu was reported: a panel that would not close, which had in fact
// never been hidden.
//
// Read from the stylesheet, because that is where the collision lives and nothing in
// the model can see it - the same reason the two handles in one gutter are checked
// here rather than in the tree.
{
  const guard = /\[hidden\][^{]*\{[^}]*display:\s*none\s*!important/.test(styles);
  check('the sheet restates the browser rule for hidden, so a display cannot beat it',
        guard, guard ? '' : 'no [hidden] { display: none !important } anywhere');

  // And the markup really declares it closed - a rule that honours `hidden` proves
  // nothing about an element that never carries it.
  //
  // Read from the markup rather than from the element, and that is a real limit: the
  // double builds nodes through createElement and takes innerHTML as one text node, so
  // it has no attributes to ask. What the two lines together do say is that the page
  // asks for a closed menu and that the sheet will not overrule it, which is the pair
  // that was broken.
  check('and the colour menu is declared closed',
        /<div id="inkMenu" hidden>/.test(script),
        (/<div id="inkMenu"[^>]*>/.exec(script) || [])[0]);
}

// --- the colour button: one click to apply, and again to apply again ---------
// Reported from the browser. A native <input type=color> raises `change` when it
// closes, so choosing the same colour a second time raises nothing at all and the
// button did nothing - the gesture "put that colour on this too" was impossible
// without going through the picker and changing the value first.
//
// The swatch half applies what is on it, every time it is pressed. That is the whole
// of the fix, and it is only checkable because the model can be read back.
{
  const para = held.body().Childs[0];
  held.setCurrent(held.blockFor(para, 0));
  held.setCached({ start: 0, end: 7 });

  const red = { R: 200, G: 30, B: 40, A: 255 };
  held.setInk(red);
  const inked = held.body().Childs[0].Runs.filter(r => r.Font && r.Font.Color);
  check('a colour reaches the runs it was applied to',
        inked.length > 0 && inked[0].Font.Color.R === 200, JSON.stringify(inked[0] && inked[0].Font));

  check('and the document remembers it', 
        (held.report().Swatches || []).length === 1
        && held.report().Swatches[0].R === 200, JSON.stringify(held.report().Swatches));

  // Cleared, then the swatch pressed again. This was written for the gesture that did
  // nothing before - same colour, same button, second press - and it asserted that the
  // colour came BACK, because the swatch then held the last colour chosen whatever the
  // text underneath said.
  //
  // It no longer does, and that is a decision rather than a regression: reported from
  // the browser, a swatch showing red over black text is the same lie as a B left lit
  // over plain text. So the swatch reports what is there, absence included, and the
  // button applies what it shows. What that costs is exactly this gesture; re-applying
  // a remembered colour is the chevron and a swatch, and the menu keeps sixteen.
  //
  // What is still asserted is that the button ACTS - which is the fault this check was
  // written for, and it is untouched by any of that.
  held.setCurrent(held.blockFor(held.body().Childs[0], 0));
  held.setCached({ start: 0, end: 7 });
  held.applyStyle('Color', null);
  const cleared = held.body().Childs[0].Runs.filter(r => r.Font && r.Font.Color);
  check('clearing it really clears it', cleared.length === 0, JSON.stringify(cleared));

  held.setCurrent(held.blockFor(held.body().Childs[0], 0));
  held.setCached({ start: 0, end: 7 });
  // The colour already at the head of the remembered list, so pressing it changes
  // nothing about that list - a check should not disturb the one next to it.
  held.setInk({ R: 200, G: 30, B: 40, A: 255 });
  held.setCurrent(held.blockFor(held.body().Childs[0], 0));
  held.setCached({ start: 0, end: 7 });
  held.el('colourApply').fire('click');
  const again = held.body().Childs[0].Runs.filter(r => r.Font && r.Font.Color);
  check('and pressing the swatch applies the colour it is showing',
        again.length > 0 && again[0].Font.Color.R === 200, JSON.stringify(again[0] && again[0].Font));

  // The same colour twice is one entry, and the newest is first - a history that grows
  // a duplicate every time somebody reuses a colour is a history that fills with one.
  held.setCurrent(held.blockFor(held.body().Childs[0], 0));
  held.setCached({ start: 0, end: 7 });
  held.setInk({ R: 30, G: 60, B: 200, A: 255 });
  held.setCurrent(held.blockFor(held.body().Childs[0], 0));
  held.setCached({ start: 0, end: 7 });
  held.setInk(red);
  const list = held.report().Swatches || [];
  check('the same colour twice is one entry, most recent first',
        list.length === 2 && list[0].R === 200 && list[1].B === 200,
        list.map(c => `${c.R},${c.G},${c.B}`).join(' | '));

  // A document that carries no list is not a document with no colours.
  const kept = held.report().Swatches;
  held.report().Swatches = [];
  const used = held.inkInUse();
  check('a document with no list is offered what is on its page',
        used.length > 0 && used.some(c => c.R === 200), JSON.stringify(used));
  held.report().Swatches = kept;

  // And the grid is built from the list rather than from whatever it held last -
  // plus the one cell that is always in it.
  //
  // "No colour" lives in the grid because that is what it is: the choice of not having
  // one, which is a choice about colour and not an errand. It sat at the foot of the
  // menu under "New colour", which put a value below an action, and it was reported as
  // reading like an afterthought.
  held.fillSwatches();
  const cells = held.el('inkSwatches').childNodes;

  check('the grid shows one swatch per remembered colour, and the clearing one',
        cells.length === kept.length + 1,
        `${cells.length} of ${kept.length} + 1`);

  // First, and always there. A grid whose first cell moves as the document gains
  // colours is a grid you have to look at twice - and the assertion above would pass
  // just as well with it stuck on the end, which is where it was.
  check('and the clearing one is the first of them',
        cells[0] && cells[0].id === 'colourClear',
        cells[0] ? `id "${cells[0].id}", class "${cells[0].className}"` : 'no cells');

  // --- and it says which colour is ON ---------------------------------------
  // A menu of choices that does not say which is current is a menu you have to try -
  // the sentence written over showActiveStyles, and as true of a grid of colours as of
  // a row of buttons. "No colour" needed it most: it is the state most documents are
  // in, and it looked exactly like the two alternatives beside it.
  {
    const para = held.body().Childs[0];
    const before = JSON.parse(JSON.stringify(para.Runs));
    const was = held.cached();
    const red = { R: 200, G: 30, B: 40, A: 255 };

    held.report().Swatches = [red];
    para.Runs = [{ Text: 'coloured', Font: { Color: red } }];
    held.setCurrent(held.blockFor(para, 0));
    held.setCached({ start: 3, end: 3 });
    held.fillSwatches();

    const marked = () => [...held.el('inkSwatches').childNodes]
      .map((c, i) => (c.classList.contains('chosen') ? i : -1)).filter(i => i >= 0);

    check('the colour under the caret is the one marked',
          JSON.stringify(marked()) === '[1]', JSON.stringify(marked()));

    // Nothing coloured: the clearing cell is the one that is on, which is the whole
    // reason it had to leave the foot of the menu and join the grid.
    para.Runs = [{ Text: 'plain', Font: null }];
    held.setCurrent(held.blockFor(para, 0));
    held.fillSwatches();
    check('and with no colour on the text, the clearing cell is marked',
          JSON.stringify(marked()) === '[0]', JSON.stringify(marked()));

    // A selection agreeing on nothing marks nothing - the same rule the two lists
    // follow, and for the same reason: marking one of them would be the menu claiming
    // a colour the text has not got.
    para.Runs = [{ Text: 'red', Font: { Color: red } }, { Text: 'plain', Font: null }];
    held.setCurrent(held.blockFor(para, 0));
    held.setCached({ start: 0, end: 8 });
    held.fillSwatches();
    check('and a selection agreeing on nothing marks nothing',
          marked().length === 0, JSON.stringify(marked()));

    para.Runs = before;
    held.report().Swatches = kept;
    held.setCurrent(held.blockFor(para, 0));
    held.setCached(was);
  }

  held.report().Swatches = [];
  held.setCurrent(held.blockFor(held.body().Childs[0], 0));
  held.setCached({ start: 0, end: 7 });
  held.applyStyle('Color', null);
}

held.setCurrent(held.blockFor(held.body().Childs[0], 0));
held.setCached({ start: 0, end: 7 });
held.applyStyle('Bold');

const runs = held.body().Childs[0].Runs;
check('a styled range becomes its own run',
      runs.length > 1 && runs[0].Font && runs[0].Font.Bold === true,
      runs.map(r => (r.Font && r.Font.Bold ? '[b]' : '') + r.Text.slice(0, 12)).join(' | '));
check('and the rest of the paragraph is untouched',
      runs.slice(1).every(r => !r.Font || !r.Font.Bold));

rep.Title = 'Written in the flow editor';
rep.Language = 'en-GB';

// --- alignment: the block's, not the selection's ----------------------------
held.setCurrent(Object.assign(new Node2('div'), { dataset: { index: '0' }, _item: held.body().Childs[0] }));
held.applyAlignment('Justify');
check('a paragraph takes an alignment on its own font',
      held.body().Childs[0].Font.HorizontalAlignment === 'Justify',
      JSON.stringify(held.body().Childs[0].Font));

const pictureAt = held.body().Childs.indexOf(picture);
held.setCurrent(Object.assign(new Node2('div'), { dataset: { index: String(pictureAt) }, _item: picture }));
held.applyAlignment('Center');
check('a picture is placed by Style.AlignHorizontal, the property that already existed',
      picture.Style.AlignHorizontal === 'Center' && !picture.Font,
      picture.Style.AlignHorizontal);

// Justify means nothing to a picture and the engine reads it as Left. The toolbar
// disables it there rather than offering a button that lies.
check('and the row knows which alignment each block already has',
      held.alignmentOf(held.body().Childs[0]) === 'Justify'
      && held.alignmentOf(picture) === 'Center'
      && held.alignmentOf({ $type: 'LineItem' }) === null);

// --- moving a block, and a picture's own width -----------------------------
// Trying to drag a picture duplicated it, because a browser's drag drops a copy of
// what is dragged into the nearest editable element. The gesture is alt+arrow now,
// and this is the check that it moves the model rather than only the page.
const order = () => held.body().Childs.map(c => c.$type).join(' ');
const before = order();

held.setCurrent(Object.assign(new Node2('div'),
                              { dataset: { index: String(held.body().Childs.length - 1) } }));
held.moveBlock(-1);
check('alt+arrow moves the block in the document, not just on the page',
      order() !== before && order().split(' ').length === before.split(' ').length,
      `${before}  ->  ${order()}`);

// A width in millimetres, measured from the picture rather than left at newItem's
// 40 x 20 placeholder - every picture would otherwise come in the same shape.
picture.NaturalWidth = 60;
picture.NaturalHeight = 45;
picture.Style.Location.WidthValue = 60;
check('a picture carries a width of its own, in millimetres',
      picture.Style.Location.WidthValue === 60 && picture.NaturalHeight === 45);
check('and it fits inside the band it is going into',
      picture.Style.Location.WidthValue <= held.bandWidth(),
      `${picture.Style.Location.WidthValue} of ${held.bandWidth()} mm`);

// --- a hard break inside a paragraph survives the round trip ---------------
// It did not. A <br> carries no text, so surfaceToRuns dropped it, and every line
// break a paragraph held came back as one long line on the next redraw. The engine
// has always understood a newline inside a run - BreakAdvancedText splits on it and
// closes the line - so this was the reader losing what the writer could keep.
{
  const surface = new Node2("div");
  surface.appendChild(new Text2("first line"));
  surface.appendChild(new Node2("br"));
  surface.appendChild(new Text2("second line"));

  const read = held.surfaceToRuns(surface);
  const text = read.map(r => r.Text).join("");
  check("a line break in a block is read back as a newline",
        text === ["first line", "second line"].join(String.fromCharCode(10)),
        JSON.stringify(text));
}

// --- the page has to show what the model holds -----------------------------
// The alignment was recorded and never drawn: the button lit, the JSON was right,
// the paragraph sat where it always had. A face that records a choice without
// drawing it is worse than one that cannot make the choice.
const aligned = held.body().Childs.find(c => c.$type === 'AdvancedTextItem');
aligned.Font.HorizontalAlignment = 'Justify';
check('a justified paragraph is drawn justified',
      surfaceOf(held.blockFor(aligned, 0)).style.textAlign === 'justify',
      surfaceOf(held.blockFor(aligned, 0)).style.textAlign);

aligned.Font.HorizontalAlignment = 'Right';
check('and a right-aligned one, right',
      surfaceOf(held.blockFor(aligned, 0)).style.textAlign === 'right');

// --- Enter puts the break in the document, not only on the page ------------
// The page showed the line breaks and the document did not have them, so everything
// looked right until the next redraw - and the next redraw is whenever a picture is
// inserted, which is how it was reported: "I type several lines, I add a picture,
// and the line breaks are gone."
// Pressed, not read: the handler is driven with a real keydown, and the browser is
// given its true behaviour of not raising an input event afterwards.
{
  const para = held.body().Childs[0];
  para.Runs = [{ Text: 'first', Font: null }];

  const block = held.blockFor(para, 0);
  const surface = surfaceOf(block);

  // The browser's selection is in the surface; what the page calls `current` is the
  // block around it. Both the surface and the block call markCurrent and the event
  // bubbles, so the block runs last and wins - every time, in every browser.
  // The first version of this check set current to the surface, which nothing ever
  // does, and so passed over a handler that read the wrong node. A check that puts
  // the page in a state it never reaches proves nothing.
  currentEditable = surface;
  held.setCurrent(block);

  document.fire('keydown', { key: 'Enter', shiftKey: false, preventDefault() {} });
  surface.appendChild(new Text2('second'));
  currentEditable = null;

  const written = (para.Runs || []).map(r => r.Text).join('');
  check('Enter puts the break in the document, without waiting for an input event',
        written.includes('\n'), JSON.stringify(written));

  // And it has to survive the redraw, which is where it was actually noticed.
  const again = surfaceOf(held.blockFor(para, 0));
  const back = held.surfaceToRuns(again).map(r => r.Text).join('');
  check('and it is still there once the block is drawn again',
        back.includes('\n'), JSON.stringify(back));

  para.Runs = [{ Text: 'Frisket keeps the model in one place.', Font: null }];
  held.setCurrent(null);
}

// --- and everything under the guard is still reachable ---------------------
// The handler lets a picture past a "this needs a caret" guard. Since the text moved
// out of the block and into a surface inside it, `current` is the block - which is
// not editable - so the guard was true for every paragraph in the document and ate
// Enter, Backspace and the arrow keys along with it. The exact shape of the fault
// this guard was written to fix, one split later.
// Backspace stands for the rest: it is under the guard, and it also has to read the
// surface rather than the block, whose drag handle is text and is never empty.
{
  const kids = held.body().Childs;
  const empty = held.emptyParagraph();
  kids.push(empty);

  const node = held.blockFor(empty, kids.length - 1);
  currentEditable = surfaceOf(node);
  held.setCurrent(node);

  const before = held.body().Childs.length;
  document.fire('keydown', { key: 'Backspace', preventDefault() {} });

  check('Backspace in an empty paragraph still removes it, guard and all',
        held.body().Childs.length === before - 1,
        `${before} -> ${held.body().Childs.length}`);

  if (held.body().Childs.includes(empty)) held.body().Childs.pop();
  currentEditable = null;
  held.setCurrent(null);
}

// --- and the two handles are not in the same place -------------------------
// A pair's own handle sits in the gutter, and so did the handle of its first cell,
// since that cell starts at the block's left edge. One lay exactly over the other,
// so the picture - the left-hand cell in the arrangement that was reported - could
// not be taken hold of at all. Read from the stylesheet, because that is where the
// collision was and nothing in the model can see it.
{
  const rule = sel => {
    const m = styles.match(new RegExp(sel.replace(/[.>]/g, c => '\\' + c)
                                      + '\\s*\\{([^}]*)\\}'));
    return m ? m[1] : '';
  };
  const leftOf = body => (body.match(/left:\s*(-?[\d.]+)px/) || [])[1];

  const blockLeft = leftOf(rule('.block > .handle'));
  const cellLeft = leftOf(rule('.cell > .handle'));

  check('a block and a cell put their handle in different places',
        blockLeft !== undefined && cellLeft !== undefined && blockLeft !== cellLeft,
        `block ${blockLeft}px, cell ${cellLeft}px`);

  // Over a photograph the gutter was never legible either, so the cell's handle sits
  // inside its own corner and carries a background of its own.
  check("and a cell's handle is drawn over what it sits on",
        /background:/.test(rule('.cell > .handle')));
}

// --- and a picture has to be movable ---------------------------------------
// It is the only block that is not editable, and guarding the whole key handler on
// isContentEditable made it the only block that could not be moved - right after a
// drag had been taken away from it.
// Driven, not read. The first version of this check searched the source for altKey
// near the keydown listener - which stayed true when the guard was put back, because
// the code was still there, merely unreachable. Looking for a thing is not the same
// as pressing the key.
const picked = new Node2('div');
picked.dataset.index = String(held.body().Childs.length - 1);
picked.contentEditable = undefined;              // a picture: the one uneditable block
held.setCurrent(picked);

const wasOrder = order();
document.fire('keydown', { altKey: true, key: 'ArrowUp', preventDefault() {} });
check('alt+arrow moves a picture too, not only a paragraph',
      order() !== wasOrder, `${wasOrder}  ->  ${order()}`);

// --- docking a picture beside a paragraph ----------------------------------
// Four drop zones rather than two: above and below reorder the body, left and right
// mean something else - the two blocks become one container laid out absolutely.
// Dock alone would not do it, since ArrangeChildren hands every child the same
// rectangle, so the coordinates are computed and this is what checks them.
{
  const kids = held.body().Childs;
  // The items themselves, not their places. A place only ever named a child of the
  // body, so half of a pair could not be named at all - which is why a pair could be
  // made and never undone.
  held.pairUp(kids.find(c => c.$type === 'ImageItem'),
              kids.find(c => c.$type === 'AdvancedTextItem'), 'left');

  const pair = held.body().Childs.find(c => c.$type === 'ContainerItem');
  check('a picture dropped beside a paragraph makes one container',
        !!pair && pair.Layout === 'Absolute' && pair.Childs.length === 2,
        pair ? pair.Childs.map(c => c.$type).join(' + ') : 'none');

  if (pair) {
    const pic = pair.Childs.find(c => c.$type === 'ImageItem');
    const txt = pair.Childs.find(c => c.$type === 'AdvancedTextItem');

    // The picture at a coordinate with a width; the paragraph starting after it and
    // stuck to the far edge. Overlapping is what Dock alone would have produced.
    check('the picture is placed at a coordinate, not merely docked',
          pic.Style.Location.LeftValue === 0
          && Number.isFinite(Number(pic.Style.Location.WidthValue)),
          `left ${pic.Style.Location.LeftValue}, width ${pic.Style.Location.WidthValue}`);

    check('and the paragraph starts past it and reaches the far edge',
          Number(txt.Style.Location.LeftValue) > Number(pic.Style.Location.WidthValue)
          && txt.Style.Location.Dock === 'Right',
          `left ${txt.Style.Location.LeftValue}, dock ${txt.Style.Location.Dock}`);

    check('the picture is capped at half the band, so the column stays readable',
          Number(pic.Style.Location.WidthValue) <= held.bandWidth() / 2 + 0.05,
          `${pic.Style.Location.WidthValue} of ${held.bandWidth()} mm`);

    // Only what this face can draw side by side, so it never makes a container it
    // would have to show as foreign in the same breath.
    check('and the pair is one the editor can draw', held.isPair(pair));
  }

  // Neither original is left behind in the body.
  const loose = held.body().Childs.filter(c => c.$type === 'ImageItem').length;
  check('the picture is no longer loose in the body', loose === 0, `${loose} left`);

  // --- and back out again --------------------------------------------------
  // The gesture was one-way: two blocks became one and nothing undid it. With only
  // a paragraph and a picture on the sheet that leaves a single block, and a block
  // cannot be dropped on itself - so the whole face reads as "drag and drop stopped
  // working" the moment a pair is made. It was reported in exactly those words.
  if (pair) {
    const before = held.body().Childs.length;
    const pic = pair.Childs.find(c => c.$type === 'ImageItem');

    const where = held.detach(pic);
    check('half a pair can be taken back out', where >= 0, `at ${where}`);

    const now = held.body().Childs;
    check('and the pair stops being one, the block left over taking its place',
          !now.includes(pair) && now.includes(pair.Childs[0]),
          now.map(c => c.$type).join(', '));

    check('so the sheet has as many blocks as before, not one fewer',
          now.length === before, `${before} -> ${now.length}`);

    // A block back in the flow is placed by the flow. Left behind, the coordinates
    // the pair pinned on it would put it at the same offset with nothing beside it.
    const back = pair.Childs[0];
    check('and what is left carries no coordinates from the pair',
          String(back.Style.Location.LeftValue) === 'NaN'
          && back.Style.Location.Dock === 'None',
          `left ${back.Style.Location.LeftValue}, dock ${back.Style.Location.Dock}`);

    now.splice(Math.min(where, now.length), 0, pic);
    held.pairUp(pic, back, 'left');
  }

  // --- and by the gesture, not only by the function it calls ---------------
  // detach is one level below what a person does. The arithmetic that goes wrong is
  // in the drop handler: it looks the target up again after the moving block has
  // gone, because taking half a pair out dissolves the very pair being dropped onto.
  // Dragged and dropped here, not called.
  {
    // The one holding this table, not merely the first pair on the sheet - earlier
  const made = held.body().Childs.find(c => held.isPair(c));
    const at = held.body().Childs.indexOf(made);
    const node = held.blockFor(made, at);
    const picture = made.Childs.find(c => c.$type === 'ImageItem');

    // The cell's own handle, which is what a person grabs.
    const cell = node.childNodes.find(c => c._item === picture);
    const grip = cell && cell.childNodes.find(c => c.classList?.contains('handle'));
    check('half a pair has a handle of its own', !!grip && grip.draggable === true);

    if (grip) {
      const before = held.body().Childs.length;
      grip.fire('dragstart', { dataTransfer: { setData() {} }, preventDefault() {} });
      node.fire('drop', { clientX: 50, clientY: 0,          // the top half: above
                          dataTransfer: {}, preventDefault() {} });

      const after = held.body().Childs;
      check('dragging half a pair out puts it back in the body',
            after.includes(picture), after.map(c => c.$type).join(', '));
      check('and the pair is gone, so the sheet gained a block rather than lost one',
            after.length === before + 1 && !after.some(c => held.isPair(c)),
            `${before} -> ${after.length}`);
      check('and it landed above what it came out of, which is where it was dropped',
            after.indexOf(picture) < after.findIndex(c => c.$type === 'AdvancedTextItem'),
            after.map(c => c.$type).join(', '));

      // --- and a side can be changed for the other side ------------------
      // A pair is named by the block a drop lands on, so half of one dropped on the
      // other side of its own pair named the pair - which is neither a picture nor a
      // paragraph, so pairUp fell through and did nothing. The only way to move a
      // picture from the right to the left was to take it out and put it back, which
      // is how it was reported. Dropped, not called.
      held.pairUp(picture, after.find(c => c.$type === 'AdvancedTextItem'), 'left');

      const made = held.body().Childs.find(c => held.isPair(c));
      const at = held.body().Childs.indexOf(made);
      const wasLeft = made.Childs[0].$type;

      const pairNode = held.blockFor(made, at);
      const half = pairNode.childNodes.find(c => c._item === picture);
      const hold = half && half.childNodes.find(c => c.classList?.contains('handle'));

      hold.fire('dragstart', { dataTransfer: { setData() {} }, preventDefault() {} });
      pairNode.fire('drop', { clientX: 95, clientY: 10,     // the outer fifth: to the right
                              dataTransfer: {}, preventDefault() {} });

      const swapped = held.body().Childs.find(c => held.isPair(c));
      check('half a pair can be dropped on the other side of its own pair',
            !!swapped && swapped.Childs[0].$type !== wasLeft,
            `${wasLeft} first before, ${swapped ? swapped.Childs[0].$type : 'no pair'} after`);

      check('and the sheet still holds one pair, not two blocks and a stray',
            held.body().Childs.filter(c => c.$type === 'ImageItem').length === 0,
            held.body().Childs.map(c => c.$type).join(', '));

      // A side zone is only lit where a side means something. pairUp draws exactly one
      // picture beside one paragraph and refuses the rest in silence, which is the
      // worst way to refuse - so the pointer falls back to above and below instead.
      const stranger = held.body().Childs.find(
        c => c.$type === 'AdvancedTextItem' && !swapped.Childs.includes(c));
      check('a side is offered where it means something',
            held.canPair(picture, swapped) === true);
      check('and never where the drop would be refused in silence',
            held.canPair(stranger, swapped) === false,
            stranger ? stranger.$type : 'none');

      // Put back what was taken apart: the checks after this one are about a pair,
      // and a check that quietly changes the shape everything downstream reads is a
      // second defect waiting for whoever writes the next one.
      check('and it goes back together, so the sheet is as it was found',
            held.body().Childs.some(c => held.isPair(c)));
    }
  }
}

// --- and where each half sits down the height of the pair ------------------
// Style.AlignVertical already existed, and ComputeVertical already read it - but
// only in an absolutely laid out container, which is what a pair is and what nothing
// else here is. So the buttons mean something inside a pair and nothing outside one,
// and that is the part worth holding: an enabled button that does nothing is §10.
{
  const pair = held.body().Childs.find(c => c.$type === 'ContainerItem');
  const txt = pair.Childs.find(c => c.$type === 'AdvancedTextItem');

  // Standing in the cell, not in the block: the container is what the flow holds,
  // the child is what has room to move inside it.
  const cell = new Node2('div');
  cell._item = txt;
  const holder = new Node2('div');
  holder.dataset.index = String(held.body().Childs.indexOf(pair));
  holder._item = pair;
  cell.parentNode = holder;
  held.setCurrent(cell);

  check('inside a pair, the vertical row means something',
        held.verticalOf(txt) !== null, held.verticalOf(txt));

  held.applyVertical('Center');
  check('and centring a half is recorded on it',
        txt.Style.AlignVertical === 'Center', txt.Style.AlignVertical);

  // Outside one there is nothing to sit inside: a block alone in the flow is as tall
  // as it is, so the row is dimmed rather than lying.
  const alone = new Node2('div');
  alone.dataset.index = '0';
  alone._item = held.body().Childs[0];
  held.setCurrent(alone);
  check('outside a pair it is dimmed rather than lying',
        held.verticalOf(held.body().Childs[0]) === null);
}

// --- a table, built with the rails ------------------------------------------
// The model was ready: a TableItem holds TableRow children, a row holds a cell per
// column, Columns is the list of widths. What was missing was a way to touch it
// without the designer - so what is checked is that the rails leave a table the
// engine can lay out, not that buttons exist.
{
  const table = held.newTable();
  held.setCurrent(null);
  held.insertAfterCurrent(table);

  check('a new table starts with a heading row and a row under it',
        table.Childs.length === 2 && table.Childs[0].IsHeader === true
        && table.Childs[1].IsHeader === false,
        table.Childs.map(r => (r.IsHeader ? 'head' : 'row')).join(' + '));

  check('and as many cells as it has columns',
        table.Childs.every(r => r.Childs.length === table.Columns.length),
        `${table.Columns.length} columns`);

  held.addColumn(table);
  check('adding a column adds one to every row',
        table.Columns.length === 3
        && table.Childs.every(r => r.Childs.length === 3),
        table.Childs.map(r => r.Childs.length).join('/'));

  held.addRow(table);
  check('and a new row arrives full width, never short',
        table.Childs.length === 3
        && table.Childs[2].Childs.length === table.Columns.length,
        `${table.Childs[2].Childs.length} of ${table.Columns.length}`);

  held.dropColumn(table, 1);
  check('dropping a column drops it from every row',
        table.Columns.length === 2
        && table.Childs.every(r => r.Childs.length === 2),
        table.Childs.map(r => r.Childs.length).join('/'));

  // A row handing back fewer cells than the table has columns is refused by a
  // conformance checker and unreadable aloud - CLAUDE.md §11 - so the one thing this
  // must never produce is a ragged table.
  const engineWouldRefuse = held.validate(held.report(), { sound: true, accessible: false })
    .filter(i => i.rule === 'sound.raggedTable');
  check('and never leaves a ragged row behind', engineWouldRefuse.length === 0,
        engineWouldRefuse.map(i => i.message).join(' | '));

  // Type into a cell through the cell's own handler, since a cell is an editable
  // surface like any other block - that is the whole reason a picture can go in one.
  const firstCell = table.Childs[0].Childs[0];
  const cellNode = new Node2('td');
  held.fillBlock(cellNode, firstCell);
  const cellSurface = surfaceOf(cellNode);
  cellSurface.appendChild(new Text2('Item'));
  cellSurface.fire('input');
  check('a cell is edited the way a paragraph is',
        firstCell.Runs[0].Text === 'Item', JSON.stringify(firstCell.Runs[0].Text));

  // --- and each button sits on what it takes away --------------------------
  // They were laid out as an evenly spaced row of 16px buttons in the corner, so only
  // the first one ever pointed at anything - two crosses and a plus bunched together,
  // with no telling which column any of them meant. And the rail sat at -20px, which
  // is over whatever block is above: in the report, the paragraph being read.
  {
    const node = held.blockFor(table, 0);
    const across = node.childNodes.find(c => c.classList?.contains('columns'));
    const down = node.childNodes.find(c => c.classList?.contains('rows'));

    check('a table has a rail for its columns and one for its rows', !!across && !!down);

    const lefts = across.childNodes.map(b => parseFloat(b.style.left));
    const tops = down.childNodes.map(b => parseFloat(b.style.top));

    // One button per column, plus the "+", and each over its own column: cells stand
    // 100 apart in the stub, so the crosses have to as well.
    check('a cross per column, and one more to add one',
          lefts.length === table.Columns.length + 1, `${lefts.length} buttons`);
    check('and each is over the column it removes, not beside the one before',
          lefts[0] === 42 && lefts[1] === 142,
          lefts.join(', '));
    check('while the plus sits past the last column, belonging to none of them',
          lefts[lefts.length - 1] > lefts[lefts.length - 2], lefts.join(', '));

    check('a cross per row, each on its own row',
          tops.length === table.Childs.length + 1 && tops[0] === 2 && tops[1] === 22,
          tops.join(', '));
    check('and its plus sits past the last row too, not back on top of one',
          tops[tops.length - 1] > tops[tops.length - 2], tops.join(', '));

    // And the whole thing keeps out of the block above, which is what put a row of
    // buttons across somebody's paragraph.
    // --- and a picture can go in a cell ------------------------------------
    // The model was ready for this and had been all along: a cell is one item of a
    // row, fillBlock already draws a picture, and the engine has never cared which
    // kind of item a cell holds. Only the gesture was missing, so a picture could sit
    // beside a table and never in it.
    {
      const shot = held.newItem('ImageItem');
      shot.Source = JSON.parse(JSON.stringify(picture.Source));
      shot.AltText = 'A picture in a cell';
      // Wider than any page, so the narrowing below is actually asked to do something.
      // At the 40 mm newItem hands out it was already under the column and the check
      // said yes without the code ever running.
      shot.Style.Location.WidthValue = 400;
      held.body().Childs.push(shot);

      const row = table.Childs[1];                       // not the heading row
      const empty = row.Childs[0];
      const cellNode = held.blockFor(table, 0)
        .querySelectorAll('td').find(td => td._item === empty);

      check('a cell is a drop target', !!cellNode);

      // Lit only where the drop is taken. A cell holds one item, so anywhere with
      // something in it the drop used to be refused, on the grounds that a cell holds
      // one item. That was the right refusal for the shape at the time and the wrong
      // shape: a cell holds one item, and an item can be a stack. So a cell with text
      // in it keeps the text and gains the picture under it.
      const typed = table.Childs[0].Childs[0];
      check('an empty cell takes a picture', held.canDropInCell(shot, empty) === true);
      check('and so does a cell with something already in it',
            held.canDropInCell(shot, typed) === true,
            JSON.stringify(typed.Runs.map(r => r.Text).join('')));

      const hold = held.blockFor(shot, held.body().Childs.length - 1)
        .childNodes.find(c => c.classList?.contains('handle'));
      hold.fire('dragstart', { dataTransfer: { setData() {} }, preventDefault() {} });
      cellNode.fire('drop', { dataTransfer: {}, preventDefault() {}, stopPropagation() {} });

      check('dragging a picture onto an empty cell puts it in the cell',
            row.Childs[0] === shot, row.Childs[0].$type);
      check('and takes it out of the body, so it is in one place only',
            !held.body().Childs.includes(shot),
            held.body().Childs.map(c => c.$type).join(', '));

      // A picture arrives capped at the body's band, which is right for the flow and
      // far too wide for a column: 160 mm of photograph in a 60 mm cell is a table
      // that overflows itself. Narrowed on the way in, both ways in.
      check('and it comes in at the width of the column, not of the page',
            Number(shot.Style.Location.WidthValue) <= held.bandWidth() / 2 + 0.01,
            `${shot.Style.Location.WidthValue} mm, band ${held.bandWidth()} mm`);

      // --- and the same by the toolbar, which is the other way in ------------
      // Choosing a picture while standing in an empty cell has to put it in that
      // cell. Left to itself, "after the current block" means after the whole table
      // for anything inside one, which is the one place it was not wanted.
      {
        const chosen = held.newItem('ImageItem');
        chosen.Source = JSON.parse(JSON.stringify(picture.Source));
        chosen.Style.Location.WidthValue = 400;        // wider than any page

        const target = row.Childs[1];
        const cellNode = held.blockFor(table, 0)
          .querySelectorAll('td').find(td => td._item === target);
        held.setCurrent(cellNode);

        held.insertAfterCurrent(chosen);
        check('a picture chosen from the toolbar lands in the cell, not after the table',
              row.Childs[1] === chosen && !held.body().Childs.includes(chosen),
              row.Childs[1].$type);
        check('and it is narrowed to the column on that way in too',
              Number(chosen.Style.Location.WidthValue) <= held.bandWidth() / 2 + 0.01,
              `${chosen.Style.Location.WidthValue} mm`);

        held.detach(chosen);
        held.setCurrent(null);
      }

      // And back out, or it is a one-way door again - the same fault as the pair.
      // What is left behind is an empty cell, never a hole: a row handing back fewer
      // cells than the table has columns is refused outright (CLAUDE.md §11).
      held.detach(shot);
      check('a picture can be taken back out of a cell',
            row.Childs[0] !== shot && row.Childs[0].$type === 'AdvancedTextItem',
            row.Childs[0].$type);
      check('and the row is as wide as the table, not one cell short',
            row.Childs.length === table.Columns.length,
            `${row.Childs.length} of ${table.Columns.length}`);
    }

    // --- and they follow the rows they point at ----------------------------
    // Placed once and never again, they stayed where the table was when it was drawn
    // - and a row grows the moment somebody types a second line in it. The crosses
    // then point at where the rows used to be, which is worse than pointing nowhere:
    // a button that means one row and looks like it means another.
    //
    // Typed into, not called: the input is raised on a cell three levels down and has
    // to reach the table, so this checks the wiring as much as the arithmetic.
    {
      const node = held.blockFor(table, 0);
      const down = node.childNodes.find(c => c.classList?.contains('rows'));
      const before = down.childNodes.map(b => b.style.top).join(',');

      // The second row grows: two lines instead of one, so everything under it moves.
      const rows = node.querySelectorAll('tr');
      rows[1]._rect = { left: 0, right: 300, width: 300, top: 20, bottom: 60, height: 40 };
      rows[2]._rect = { left: 0, right: 300, width: 300, top: 60, bottom: 80, height: 20 };

      const typed = node.querySelectorAll('td')
        .find(td => td.parentNode === rows[1]);
      surfaceOf(typed).fire('input');

      const after = down.childNodes.map(b => b.style.top).join(',');
      check('a rail follows a row that has just grown, without being asked again',
            after !== before, `${before}  ->  ${after}`);
      check('and the cross for the row under it moves down with it',
            parseFloat(down.childNodes[2].style.top) === 62,
            down.childNodes[2].style.top);
    }

    check('a table keeps room for its own rail rather than borrowing it from above',
          /\.block\.tabular\s*\{[^}]*padding-top/.test(styles)
          && node.classList.contains('tabular'));
  }
}

// --- every block keeps a handle, whatever it turned into --------------------
// Dropping a block to the side stopped dragging working, and neither half of the
// reason was visibly broken: a block used to be its own handle, so a paragraph was
// dragged by its contenteditable - where a browser would rather select text - and a
// pair was dragged by the picture inside one of its cells, where the gesture is
// ambiguous even to a person. The handle is one element outside anything editable,
// and this is the check that every block has exactly one.
{
  const handles = item => {
    const node = held.blockFor(item, 0);
    return node.childNodes.filter(c => c.classList && c.classList.contains('handle'));
  };

  for (const item of held.body().Childs) {
    const found = handles(item);
    check(`a ${item.$type} has one handle`, found.length === 1, found.length);
    check('and it is draggable', found.length === 1 && found[0].draggable === true);
  }

  // And nothing else starts a drag. A browser makes every picture draggable on its
  // own, and that drag carries the HTML rather than the block - so it lands where the
  // browser likes and the model hears nothing about it. With a handle on every block
  // there is no reason left to leave it on: two ways to start a drag, one of which
  // does something else, is worse than one.
  //
  // `=== false`, never `!== true`. Not setting it at all leaves it undefined, which
  // passes the second test and is exactly the state being guarded against.
  for (const item of held.body().Childs) {
    const picture = held.blockFor(item, 0).querySelector('img');
    if (!picture) continue;
    check(`a picture inside a ${item.$type} does not drag itself`,
          picture.draggable === false, String(picture.draggable));
  }

  // Including a pair, which is the shape that broke it.
  const pair = held.body().Childs.find(c => c.$type === 'ContainerItem');
  if (pair) {
    const found = handles(pair);
    check('a pair is dragged by its own handle, not by the picture in it',
          found.length === 1 && found[0].draggable === true
          && held.blockFor(pair, 0).querySelector('img')?.draggable === false,
          found.length + ' handle(s)');
  }
}

// --- a cell holds a stack, and a pair holds anything it can draw -----------
// Both were the same refusal wearing two coats: the face was narrower than the engine.
// A cell holds one *item*, and an item can be a container; and a table at a coordinate
// with a width lays out beside a paragraph perfectly well - measured before any of this
// was written, table 0..60 and paragraph 64..210, wrapping in its own column.
{
  const table = held.newTable();
  const row = table.Childs[1];
  const typed = row.Childs[0];
  typed.Runs = [{ Text: 'A caption', Font: null }];

  const picture = held.newItem('ImageItem');
  picture.Style.Location.WidthValue = 30;

  // --- into a cell that is not empty ---------------------------------------
  check('a cell with something in it takes a picture now',
        held.canDropInCell(picture, typed) === true);

  held.putInCell(row, 0, picture);
  const stacked = row.Childs[0];

  check('and what was there is kept, not replaced',
        stacked.$type === 'ContainerItem' && stacked.Childs.length === 2
        && stacked.Childs[0] === typed && stacked.Childs[1] === picture,
        stacked.$type + ' of ' + (stacked.Childs || []).map(c => c.$type).join(' + '));

  check('the stack flows rather than being pinned',
        stacked.Layout !== 'Absolute', stacked.Layout);

  // A third goes into the same stack rather than making a stack of stacks.
  const rule = held.newItem('LineItem');
  held.putInCell(row, 0, rule);
  check('a third block joins the same stack, not a stack of stacks',
        row.Childs[0] === stacked && stacked.Childs.length === 3,
        stacked.Childs.map(c => c.$type).join(' + '));

  // --- drawn, not merely recorded ------------------------------------------
  const node = held.blockFor(table, 0);
  const cellNode = node.querySelectorAll('td').find(td => td._item === stacked);
  check('the stack is drawn in the cell', !!cellNode && cellNode.classList.contains('stack'));

  const slots = cellNode ? cellNode.childNodes.filter(c => c.classList?.contains('slot')) : [];
  check('with one slot per block', slots.length === 3, `${slots.length} slots`);
  check('and each slot has a handle of its own, so nothing is stuck in a stack',
        slots.every(sl => sl.childNodes.some(c => c.classList?.contains('handle'))));

  // --- and out again -------------------------------------------------------
  held.body().Childs.push(table);
  held.detach(rule);
  check('a block can be taken back out of a stack',
        stacked.Childs.length === 2 && !stacked.Childs.includes(rule),
        stacked.Childs.map(c => c.$type).join(' + '));

  held.detach(picture);
  check('and a stack down to one stops being a stack',
        row.Childs[0] === typed, row.Childs[0].$type);
  check('while the row keeps as many cells as the table has columns',
        row.Childs.length === table.Columns.length,
        `${row.Childs.length} of ${table.Columns.length}`);

  held.body().Childs.pop();
}

// --- and a pair is decided by geometry, not by type ------------------------
{
  const band = held.bandWidth();

  const text = held.emptyParagraph();
  const other = held.emptyParagraph();
  const picture = held.newItem('ImageItem');
  picture.Style.Location.WidthValue = 40;

  const narrow = held.newTable();
  narrow.Columns.forEach(c => { c.Width = 30; });        // stated, so the check below can mean something
  const wide = held.newTable();
  wide.Columns.forEach(c => { c.Width = band * 0.9; }); // far too wide for a column

  check('a table may go beside a paragraph', held.canPair(narrow, text) === true);
  check('and a paragraph beside a paragraph', held.canPair(text, other) === true);
  check('but not a table with no room left to read beside it',
        held.canPair(wide, text) === false,
        `${wide.Columns.length} x ${wide.Columns[0].Width} mm of ${band} mm`);

  // A picture is scaled to the column it is given; a table is not, because its columns
  // are stated in millimetres and the engine does not stretch them - measured: a table
  // of 30 + 30 given a width of 85 renders 0..85 with its cells still ending at 60.
  const stated = narrow.Columns.reduce((sum, c) => sum + Number(c.Width || 0), 0) || null;
  held.body().Childs.push(narrow, text);
  held.pairUp(narrow, text, 'left');

  // The one holding this table, not merely the first pair on the sheet - earlier
  // checks left one there, and find() answered with that instead.
  const made = held.body().Childs.find(c => held.isPair(c) && c.Childs.includes(narrow));
  check('the two become one pair', !!made && made.Childs.length === 2,
        made ? made.Childs.map(c => c.$type).join(' + ') : 'none');

  if (made) {
    const [left, right] = made.Childs;
    check('the table is on the side it was dropped', left === narrow, left.$type);
    // Vacuous on the first writing: newTable's columns state no width, so `stated`
    // was null and the test below was true whatever the code did. The columns are
    // given one above, and this says so out loud rather than trusting it.
    check('the columns state a width at all, or the next check proves nothing',
          stated === 60, String(stated));
    check('and the table keeps it, never a frame squeezed around a narrower grid',
          Math.abs(Number(right.Style.Location.LeftValue) - (stated + 4)) < 0.6,
          `paragraph starts at ${right.Style.Location.LeftValue}, table states ${stated}`);
    check('while the paragraph starts past it and reaches the far edge',
          Number(right.Style.Location.LeftValue) > 0 && right.Style.Location.Dock === 'Right',
          `left ${right.Style.Location.LeftValue}, dock ${right.Style.Location.Dock}`);
  }

  // Put the sheet back the way it was found.
  const at = held.body().Childs.indexOf(made);
  if (at >= 0) held.body().Childs.splice(at, 1);
}

// Written only on the run the recipe at the top describes: no argument, which is the
// face that ships. Every face produces a document and they are not the same document -
// the full face starts with a letter's margins, the embedded one with two millimetres -
// so written by every run, the file published as evidence was whichever face happened
// to be checked last. The renders beside it had drifted the same way: the PDF and SVG
// came from one version of this file and the mail from another, older still.
if (!process.argv[2]) {
  const out = 'tests/output/editor/flow.json';
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, JSON.stringify(rep, null, 2));
  console.log(`  → ${out}`);
}

// A COUNT, because neither number was ever printed here.
//
// This harness ended on the path it had written and nothing else, so a run that quietly
// asked fewer questions was typographically identical to one that asked them all -
// which is the complaint this project makes everywhere about a check that stops
// checking. The sister harness had the same hole and the same fix.
//
// Not asserted against an expected number: a harness holding one is one addition away
// from failing for the wrong reason (section 16). What is wanted is that somebody
// diffing two runs can SEE the difference, which a printed total gives and a silent one
// cannot.
// --- what a face shows, and what it goes on doing anyway ---------------------
// The editor somebody embeds shows less than the editor somebody develops: no header,
// no Document or Typefaces panel, no findings on screen. That is the whole of change
// (A), and none of it is about the checks stopping.
//
// Asked by MOUNTING each shape rather than by reading the source. Every piece of markup
// is a literal in the file whichever face is being built, so a check that greps for
// `id="title"` passes on a face that never plants it - the fault this harness had for
// its whole life, in a fresh disguise.
{
  const laid = () => {
    // What actually reached the tree. The double keeps the markup as one text node, so
    // this reads the text - which is the honest source here: it is what was planted.
    const host = held.el('frisket-editor-root');
    return String(host && host.childNodes.map ? host.childNodes.map(n => n.textContent).join('') : '');
  };
  const has = id => new RegExp('id="' + id + '"').test(laid());

  // FIRST, what THIS page got - before anything below re-mounts and overwrites it.
  // The page declares its shape on the div it hands over, and that declaration is the
  // only difference between the face that ships and the face for working on the editor:
  // asked here rather than assumed, because with no attributes on the double both pages
  // used to fall back to the same shape and this run reported the same thing for either.
  {
    const tag = page.match(/<div\s+id="frisket-editor-root"([^>]*)>/);
    const attr = n => (tag && tag[1].match(new RegExp(n + '="([^"]*)"')) || [, null])[1];
    const wantChrome = attr('chrome') !== 'false';
    const wantPanel = attr('panel') !== 'false';

    check('the page got the face it asked for: chrome',
          has('new') === wantChrome && has('lang') === wantChrome,
          `asked ${wantChrome}, laid ${has('new')}`);

    check('and the panel it asked for',
          has('count') === wantPanel && has('title') === wantPanel,
          `asked ${wantPanel}, laid ${has('count')}`);

    // Where the file buttons went. "header" is a page's default because a page is the
    // whole application; "toolbar" is what a face with no header asks for, and the two
    // are never both - Open twice is worse than Open once in the wrong place.
    const files = attr('files') || (wantChrome ? 'header' : 'toolbar');
    check('and open and save where it put them', has('open') && has('save'),
          `${files}, ${has('file') ? 'with' : 'WITHOUT'} the file input`);
  }

  const FULL = { chrome: true, files: 'header', panel: true };
  const LEAN = { chrome: false, files: 'toolbar', panel: false };

  const seen = [];
  held.onChecks(found => seen.push(found));

  held.shown(FULL); held.mount();
  check('the full face lays the header, the panel and the findings',
        ['new', 'save', 'lang', 'theme', 'title', 'language', 'paper', 'fontSource',
         'targetTagged', 'count', 'issues'].every(has),
        ['new', 'save', 'lang', 'theme', 'title', 'language', 'paper', 'fontSource',
         'targetTagged', 'count', 'issues'].filter(id => !has(id)).join(', ') || 'all of them');

  held.shown(LEAN); held.mount();
  const gone = ['new', 'html', 'mail', 'lang', 'theme', 'title', 'language', 'paper',
                'fontSource', 'fontHint', 'targetTagged', 'targetInteractive',
                'count', 'issues'];
  check('the embedded face lays none of the machinery',
        !gone.some(has), gone.filter(has).join(', ') || `${gone.length} controls withheld`);

  check('and it is still an editor: the sheet and the toolbar are there',
        has('sheet') && has('toolbar'), laid().length + ' characters of markup');

  // Open and Save move rather than vanish - a page with no header still needs a way in
  // and out, and the file input travels with them or the button throws when pressed.
  check('with open and save in the toolbar, and the file input beside them',
        has('open') && has('save') && has('file'),
        ['open', 'save', 'file'].filter(id => !has(id)).join(', ') || 'all three');

  // THE HALF THAT MATTERS. Section 5c forbids an editor that quietly produces a
  // document `frisket check` would refuse. Hiding the panel is not that, as long as the
  // findings still reach whoever embedded this - so the seam is asked, not the screen.
  const before = seen.length;
  held.runChecks();
  check('and the checks still run with nowhere to paint them',
        seen.length > before && Array.isArray(seen[seen.length - 1]),
        `${seen.length - before} report(s), ${(seen[seen.length - 1] || []).length} finding(s)`);

  // The target is a state a host sets, not two checkboxes read off a page that has
  // none. An absent target is the worst answer available: the Capable profile gates on
  // the object being there rather than on its fields, so an empty one fires every rule
  // it has on every document.
  const t = held.target();
  check('and the render target survives a face with no checkboxes',
        !!t && t.Tagged === true && t.Interactive === true && t.Paged === true,
        JSON.stringify(t));

  // --- a selection belongs to the block it was made in --------------------------
  // Reported as the swatch flashing the colour of a run in the block you have just
  // left, before settling on the right answer a moment later. `cached` is a pair of
  // offsets into ONE block; markCurrent paints the toolbar from it on the very line
  // that changes which block is current, so for the width of one frame the toolbar
  // described the new block through the old block's numbers.
  //
  // Chrome showed it and Firefox did not, which is the ordinary difference between two
  // engines about when focus and selection settle - and exactly why it is worth
  // asserting rather than watching for.
  {
    const red = { R: 205, G: 24, B: 24, A: 255 };
    const coloured = held.newItem('AdvancedTextItem');
    coloured.Runs = [{ $type: 'TextRun', Text: 'red words here', Font: { Color: red } }];
    const plain = held.newItem('AdvancedTextItem');
    plain.Runs = [{ $type: 'TextRun', Text: 'plain words', Font: {} }];

    held.body().Childs = [coloured, plain];
    held.draw();

    const blockOf = i => held.el('sheet').querySelector(`.block[data-index="${i}"]`);

    held.markCurrent(blockOf(0));
    const first = blockOf(0).querySelector('.surface');
    selectionRange = { commonAncestorContainer: first, startContainer: first,
                       startOffset: 4, toString: () => '' };
    document.fire('selectionchange');
    check('the toolbar shows the colour of the block the caret is in',
          !!held.shownInk() && held.shownInk().R === 205, JSON.stringify(held.shownInk()));

    // The other block, and NOTHING else - no selectionchange yet, which is the frame
    // the report is about.
    held.markCurrent(blockOf(1));
    check('and moving to another block says nothing rather than the old answer',
          held.shownInk() === null && held.cached() === null,
          `ink ${JSON.stringify(held.shownInk())}, cached ${JSON.stringify(held.cached())}`);
  }

  // --- a paragraph inside a group, clicked --------------------------------------
  // Reported twice before it was understood, and both reports were this: click into
  // the text of a group and the two lists go blank while the swatch turns a colour
  // that is in a DIFFERENT block - then a second click on the same place puts it all
  // right.
  //
  // mousedown on a cell stops propagating; focusin did not. So the cell was marked,
  // correctly, and a moment later focusin bubbled past it to the group's own .block,
  // whose handler overwrote `current` with the ContainerItem. Everything reading
  // currentItem() then answered about a container where a paragraph had been clicked.
  // The second click worked because the block already had focus, so focusin never
  // fired.
  //
  // Driven as the browser drives it - mousedown, then focus moving - because the fault
  // is entirely in the order and the bubbling. Asserting on markCurrent alone would
  // have shown nothing wrong.
  {
    const red = { R: 205, G: 24, B: 24, A: 255 };

    const inside = held.newItem('AdvancedTextItem');
    inside.Runs = [{ $type: 'TextRun', Text: 'plain text', Font: {} }];

    // Two paragraphs rather than a picture: a pair only forms of children that can be
    // drawn, and an ImageItem with no bytes cannot. The report was about a picture, and
    // the mechanism is the cell rather than what is in it.
    const beside = held.newItem('AdvancedTextItem');
    beside.Runs = [{ $type: 'TextRun', Text: 'the other half', Font: {} }];

    const group = held.newItem('ContainerItem');
    group.Layout = 'Absolute';
    group.Childs = [inside, beside];

    const elsewhere = held.newItem('AdvancedTextItem');
    elsewhere.Runs = [{ $type: 'TextRun', Text: 'red text', Font: { Color: red } }];

    held.body().Childs = [elsewhere, group];
    held.draw();

    // A colour was chosen a moment ago, as it would have been.
    held.setInk(red);

    const cell = held.el('sheet').querySelector('.cell');
    const surface = cell && cell.querySelector('.surface');

    surface.fire('mousedown', {});
    surface.fire('focusin', {});          // the browser moving focus, a beat later

    check('clicking a paragraph inside a group selects that paragraph',
          held.currentItem() === inside,
          held.currentItem() ? held.currentItem().$type : 'nothing');

    selectionRange = { commonAncestorContainer: surface, startContainer: surface,
                       startOffset: 2, toString: () => '' };
    document.fire('selectionchange');

    check('and the swatch does not borrow a colour from another block',
          held.shownInk() === null, JSON.stringify(held.shownInk()));

    // And with nothing selected at all - a picture, a table, an empty sheet - there is
    // no text to have a colour, so the swatch has nothing to say. It said the last
    // colour chosen, which over a photograph is a statement about nothing.
    held.setInk(red);
    held.setCurrent(null);
    held.showActiveStyles();
    check('and says nothing where there is no text to have a colour',
          held.shownInk() === null, JSON.stringify(held.shownInk()));
  }

  // --- which families a face offers, and who decided ---------------------------
  // Asked of the function, not read off a rendered list: the theme guard taught that
  // one - three lists confronting each other could not see a guard at all, and
  // replacing it with a pass-through changed no count and no line of the run.
  //
  // A face with no font control does not read the preference stored by the face that
  // has one. Somebody set "from the server" on the full page, where they could see the
  // control; letting that decide inside a host application means an embedded editor
  // asking a preview server that is not there, failing, and showing a poorer list for
  // it - and no way to change its mind, because the control is gone. The theme trap,
  // one seam over.
  // --- which room a face is in, and who decided --------------------------------
  // Found in Chrome's inspector and it is not a Chrome fault. A host page painting the
  // editor in its own dark colours had `data-theme="light"` stamped on the tag, because
  // somebody had once picked Light on the standalone page and both faces share one
  // localStorage key. The host sets seven of the twenty-four palette names; the other
  // seventeen then came from the LIGHT theme inside the shadow root - so --panel was the
  // host's near-black and --control was white, and the size list came out white on a
  // dark toolbar.
  //
  // Third time this session that a preference stored by the face WITH a control decided
  // inside a face without one. Asked of the function, as the other two are.
  // --- and how much white the page starts with ---------------------------------
  // Twenty millimetres is a letter's margin and what somebody laying out a printed page
  // expects. Inside a host application it is twenty millimetres of that application's
  // screen given to a margin nobody there can change and few will print - so a face
  // with no control for it starts smaller. Third default of its kind, after the font
  // source and the room.
  held.shown(LEAN);
  check('a face with no margin control starts nearly at the edge',
        held.chooseMargins(null) === 2, held.chooseMargins(null) + 'mm');

  check('and a host that wants a printed page can still say so',
        held.chooseMargins('25') === 25, held.chooseMargins('25') + 'mm');

  check('and nonsense falls back rather than being believed',
        held.chooseMargins('lots') === 2 && held.chooseMargins('-5') === 2
        && held.chooseMargins('9000') === 2,
        [held.chooseMargins('lots'), held.chooseMargins('-5'),
         held.chooseMargins('9000')].join(', '));

  held.shown(FULL);
  check('while the face with the control keeps a letter margin',
        held.chooseMargins(null) === 20, held.chooseMargins(null) + 'mm');

  held.setTheme('light');                 // as if somebody had picked it on the full page

  held.shown(LEAN);
  check('a face with no theme picker leaves the room to the machine',
        held.chooseTheme(null) === 'auto', held.chooseTheme(null));

  check('and a host with an opinion can still name one',
        held.chooseTheme('graphite') === 'graphite', held.chooseTheme('graphite'));

  check('and a room that no longer exists falls back rather than being stamped',
        held.chooseTheme('slate') === 'auto', held.chooseTheme('slate'));

  held.shown(FULL);
  check('while the face that has the picker keeps what was stored',
        held.chooseTheme(null) === 'light', held.chooseTheme(null));

  held.setTheme('auto');

  held.setFontSource('library');          // as if the full page had stored it

  held.shown(LEAN);
  check('a face with no font control offers the web-safe families',
        held.chooseFontSource(null) === 'websafe', held.chooseFontSource(null));

  check('and a host that does have a font server can say so',
        held.chooseFontSource('library') === 'library', held.chooseFontSource('library'));

  check('and a name that is no longer a source falls back rather than passing through',
        held.chooseFontSource('from-the-moon') === 'websafe',
        held.chooseFontSource('from-the-moon'));

  held.shown(FULL);
  check('while the face that has the control keeps what was stored',
        held.chooseFontSource(null) === 'library', held.chooseFontSource(null));

  // And the consequence a person actually sees.
  held.setFontSource('websafe');
  held.shown(LEAN); held.mount();
  const offered = held.el('family').options.map(o => o.value).filter(Boolean);
  check('so the list it draws is those families and not the server\'s',
        offered.length > 0 && offered.every(f => held.webSafe().includes(f)),
        offered.join(', ') || 'nothing offered');

  // And what text naming no family is shown in. It was three things at once: the core
  // drew it in "sans-serif", this sheet in "Georgia, serif", and the PDF in the
  // engine's default. Asked of the sheet a person looks at, under each source. The
  // families below are stand-ins for what a server holds, not fonts this test relies on.
  const face = () => held.el('sheet').style.fontFamily;
  check('with the web\'s own families, unnamed text is in the stack a mail is sent with',
        face() === held.mailFace(), face());

  held.onChecks(null);
  held.setFontSource('library');
  held.shown(FULL); held.mount();
  check('with the server\'s families and no server, in a generic face, not a chosen one',
        face() === 'sans-serif', face());

  held.receiveFonts([
    { family: 'Other Face', subfamily: 'Regular', bold: false, italic: false, isDefault: false },
    { family: 'House Face', subfamily: 'Regular', bold: false, italic: false, isDefault: true },
    { family: 'House Face', subfamily: 'Bold', bold: true, italic: false, isDefault: true },
  ]);
  check('and once the server answers, in the family it gives text that names none',
        face() === "'House Face', sans-serif" && held.defaultFace() === face(), face());

  held.report().DefaultFont = { ...(held.report().DefaultFont || {}), Family: 'Other Face' };
  held.draw();
  check('while a document naming its own default is shown in that one, as it prints',
        face() === "'Other Face', sans-serif", face());
  delete held.report().DefaultFont.Family;

  check('and a face missing a cut borrows another rather than letting the browser invent one',
        /font-synthesis\s*:\s*none/.test(cssFor('#sheet')), cssFor('#sheet').trim().slice(0, 80));

  held.receiveFontsReset();
  held.shown(FULL); held.mount();
}

// --- the two controls in that row that were not stating anything ---------------
// Reported from the browser, in Firefox and in Edge alike, so it is ours: the family
// list comes up blank, and the colour swatch does not follow the caret.
//
// The second was written down as a decision - "not read off the selection: it is what
// was last chosen" - and it is the same decision already judged wrong for the size list
// fifteen lines away: B, I, U, S and both alignments all report what is there, so a
// control in that row reporting nothing is the odd one out.
{
  // A list with no options is not a list showing nothing. At mount the select has just
  // been planted and holds none, so selectedIndex is -1 - and the guard meant for a
  // mixed selection took that for one and blanked the list on a page that had just
  // finished loading.
  // Freshly mounted, because that is the moment being asked about. Written without
  // this it ran at the tail of a hundred and thirty checks that had each left a
  // selection behind, and reported on whatever the last one happened to do - a check
  // putting the page in a state it never reaches, which this file has now paid for
  // three times.
  // And with no selection made yet, as at a real load. Leaving behind the range some
  // earlier check made asks about a stale selection over a document that no longer
  // holds it - where blank IS the right answer, so the check would report on a third
  // state while claiming to ask about this one.
  held.setCached(null);
  held.mount();

  // A SELECT WITH NO GROUND OF ITS OWN IS PAINTED BY THE BROWSER.
  //
  // `background: none` reads as "let the toolbar show through" and means something
  // else to a <select>: with none declared the browser paints its native widget. The
  // only reason that never showed is that a background WAS declared on hover - so open
  // the list, the pointer moves onto the list, the hover falls away, and the closed box
  // comes back as a pale native control under a dark menu. Reported with a screenshot.
  //
  // Read from the stylesheet, because that is where it lives and nothing in the model
  // can see it - the same reason the handle-collision rules are checked that way.
  {
    // THE KIND IS IN THE HANDLE'S TOOLTIP, NOT ON THE PAPER.
    //
    // Three labels floated beside the text on hover - a block's kind, a cell's, a
    // slot's - printed into the page's own margin, where nothing else in this face
    // draws. A great deal of running commentary for something you want to know once,
    // about a block you cannot identify by looking. Asking is what pointing at the
    // handle is for.
    check('no kind is painted on the paper',
          !/attr\(data-kind\)/.test(styles),
          (styles.match(/attr\(data-kind\)/g) || []).length + ' label(s) left');

    {
      const para = held.newItem('AdvancedTextItem');
      para.Runs = [{ $type: 'TextRun', Text: 'some words', Font: {} }];
      const block = held.blockFor(para, 0);
      const grab = block.childNodes.find(n => n.classList
                                           && n.classList.contains('handle'));
      check('and the handle says what it will move',
            !!grab && /Advanced text/.test(grab.title || ''),
            grab ? JSON.stringify(grab.title) : 'no handle at all');
    }

    // AND IT REACHES ITS BLOCK. At left -24 and 18 wide it stopped at -6, leaving six
    // pixels of nothing between the two - and crossing that gap on the way to the
    // handle ends `.block:hover`, so it faded exactly as somebody reached for it.
    // Arithmetic rather than two remembered numbers, so moving the gutter cannot make
    // this check stale.
    {
      const grab = cssFor('.block > .handle');
      const left = Number((grab.match(/left:\s*(-?[\d.]+)px/) || [])[1]);
      const width = Number((grab.match(/width:\s*([\d.]+)px/) || [])[1]);
      check('the handle box reaches the block it belongs to',
            Number.isFinite(left) && Number.isFinite(width) && width >= -left,
            `left ${left}px, width ${width}px, so it stops at ${left + width}px`);

      // AND THE CHIP INSIDE IT IS SQUARE. Two different things, and they were one until
      // somebody looked: the padding that closes the gap above is inside the box, and a
      // background paints the box - so the wash under the pointer came out 24 by 20
      // where a cell's is 18 by 18. Arithmetic on the declarations rather than a pair of
      // remembered numbers, so moving the gutter cannot make this stale.
      const num = (css, prop, fallback) => {
        const m = css.match(new RegExp(prop + ':\\s*(-?[\\d.]+)px'));
        return m ? Number(m[1]) : fallback;
      };
      const border = num(grab, 'border', 0) || 1;
      const padRight = num(grab, 'padding-right', 0);
      const height = num(grab, 'height', NaN);
      const chipW = width - 2 * border - padRight;
      const chipH = height - 2 * border;

      check('and the chip it paints is square, like a cell handle',
            chipW === chipH, `${chipW} by ${chipH}`);

      check('and it paints the chip alone, not the padding that closes the gap',
            /background-clip:\s*content-box/.test(grab),
            (grab.match(/background-clip[^;]*/) || ['nothing said'])[0]);
    }

    // SQUARE BUTTONS STAY SQUARE.
    //
    // `width: 28px; height: 28px` is a claim, and a flex item makes no such claim by
    // default - it shrinks, and it shrinks in ONE direction. So every icon in a toolbar
    // too wide for its editor came out narrower than it is tall, with every height
    // still 28. Reported as "the buttons are not square", which is exactly what it was.
    //
    // The tell was already in the stylesheet: the colour chevron carried a min-width and
    // nothing else did - the same fault, patched in the one place somebody saw it.
    const toolbar = cssFor('#toolbar');
    const button = cssFor('#toolbar button');
    const list = cssFor('#toolbar select');

    check('a toolbar button does not shrink out of square',
          /flex:\s*none/.test(button) || /flex-shrink:\s*0/.test(button),
          (button.match(/flex[^;]*/) || ['nothing said about flex'])[0]);

    check('and the lists give way instead, which is what can afford to',
          /min-width:\s*0/.test(list),
          (list.match(/flex[^;]*/) || ['nothing said about flex'])[0]);

    check('and the row wraps rather than squeezing when even that is not enough',
          /flex-wrap:\s*wrap/.test(toolbar),
          (toolbar.match(/flex-wrap[^;]*/) || ['no wrap'])[0]);

    // A HANDLE LEFT SHOWING BY A BLOCK YOU ARE NO LONGER IN.
    //
    // A block's handle lives in the gutter, where leaving it showing for the current
    // block costs nothing. A cell's was moved into its own corner precisely because
    // the gutter is taken - so it sits OVER the text, and leaving it there for
    // whichever cell was last clicked puts a second handle on the page while you are
    // reading another block. Reported exactly that way, in both engines.
    check('a cell handle shows on hover, not for the cell last clicked',
          !/\.cell\.here\s*>\s*\.handle/.test(styles) && /\.cell:hover\s*>\s*\.handle/.test(styles),
          (/\.cell\.here\s*>\s*\.handle/.test(styles) ? 'still shown for the last clicked' : '')
          + (/\.cell:hover\s*>\s*\.handle/.test(styles) ? '' : ' and not shown on hover at all')
          || 'hover only');

    check('and a slot handle the same, for the same reason',
          !/\.slot\.here\s*>\s*\.handle/.test(styles) && /\.slot:hover\s*>\s*\.handle/.test(styles),
          (/\.slot\.here\s*>\s*\.handle/.test(styles) ? 'still shown for the last clicked' : '')
          + (/\.slot:hover\s*>\s*\.handle/.test(styles) ? '' : ' and not shown on hover at all')
          || 'hover only');
  }

  {
    const ground = cssFor('#toolbar select');
    const focused = cssFor('#toolbar select:focus');
    // The value read out and looked at, never a lookahead: `background:\s*(?!none)`
    // refuses nothing, because \s* backtracks to zero and the negation is then tested
    // against " none". It passed on `background: none` with the fault in the file.
    const declares = css => {
      const m = /background:\s*([^;]+)/.exec(css || '');
      return !!m && m[1].trim() !== 'none';
    };

    check('a select carries its own ground at rest', declares(ground),
          (ground || '').match(/background:[^;]*/) || 'no background at all');

    check('and while its list is open, which is when it stops being hovered',
          declares(focused),
          (focused || '').match(/background:[^;]*/) || 'no background at all');
  }

  check('the family list is not blank the moment it is built',
        held.el('family').selectedIndex >= 0,
        `selectedIndex ${held.el('family').selectedIndex}, `
        + `${held.el('family').options.length} option(s)`);

  const red = { R: 200, G: 0, B: 0, A: 255 };
  const runs = (text, font) => [{ $type: 'TextRun', Text: text, Font: font || {} }];

  const plain = held.newItem('AdvancedTextItem');
  plain.Runs = runs('aaaaaaa');
  const coloured = held.newItem('AdvancedTextItem');
  coloured.Runs = runs('bbbbbbb', { Color: red });

  held.body().Childs = [plain, coloured];
  held.draw();

  const caretIn = index => {
    const block = held.el('sheet').querySelector(`.block[data-index="${index}"]`);
    const surface = block && block.querySelector('.surface');
    held.markCurrent(block);
    selectionRange = { commonAncestorContainer: surface, startContainer: surface,
                       startOffset: 3, toString: () => '' };
    document.fire('selectionchange');
  };

  held.setInk({ R: 0, G: 0, B: 255, A: 255 });   // somebody picked blue a moment ago

  caretIn(1);
  const onRed = held.shownInk();
  check('the swatch says what the caret is standing in',
        !!onRed && onRed.R === 200 && onRed.G === 0 && onRed.B === 0,
        JSON.stringify(onRed));

  caretIn(0);
  const onPlain = held.shownInk();
  check('and says so when the text has none, rather than the last colour chosen',
        onPlain === null, JSON.stringify(onPlain));

  // The remembered colours are untouched by that: they are the menu's business, and
  // they are where "the same colour again" lives now.
  check('while the colours chosen before are still remembered',
        (held.report().Swatches || []).length > 0,
        JSON.stringify(held.report().Swatches));
}

// --- several editors on one page ---------------------------------------------------
// The face that ships is the one a host embeds, and a host may embed it more than once -
// a letter and its footnote, every item of a list. Until 18 October 2026 an editor was the
// file, one per page: a second <frisket-editor> took over the first one's root, document
// and selection, and the first went on showing a sheet whose edits landed in the second.
// Nothing failed. Asked through the element itself, which is how a host gets more than
// one, each in a root of its own as a shadow root gives it.
{
  // A shadow root, small: it answers for the ids planted under it and for nothing else.
  // Two editors each have a #sheet, and a lookup reaching across would make two editors
  // one - which is exactly the fault under test, so a double that did it could not see it.
  const within = (node, root) => {
    for (let n = node; n; n = n.parentNode) if (n === root) return true;
    return false;
  };
  class Root2 extends Node2 {
    constructor(host) { super('#shadow-root'); this.host = host; this._byId = {}; }
    getElementById(id) {
      const owner = [...planted].find(n => n._ids && n._ids.has(id) && within(n, this));
      if (!owner) return null;
      const node = (this._byId[id] ??= new Node2('div'));
      node.parentNode = owner;
      return node;
    }
    getSelection() { return window.getSelection(); }
  }

  global.HTMLElement = class extends Node2 {
    constructor() { super('frisket-editor'); }
    attachShadow() { return (this.shadowRoot = new Root2(this)); }
    dispatchEvent(e) { (this._listeners[e.type] || []).forEach(f => f(e)); }
  };
  global.CustomEvent = class { constructor(type, init) { this.type = type; this.detail = init && init.detail; } };
  const registry = {};
  global.customElements = { get: n => registry[n], define: (n, c) => { registry[n] = c; } };
  document.currentScript = { src: 'file:///frisket/apps/designer/frisket-editor-element.js' };

  // The element reads its own address with `new URL`, and this double's URL is only the
  // two blob helpers the editor uses - so a real one, for the length of the load.
  const pageURL = global.URL;
  global.URL = Object.assign(class extends require('url').URL {}, pageURL);
  eval(readText(path.join(DIR, 'frisket-editor-element.js')));
  global.URL = pageURL;
  const Editor = customElements.get('frisket-editor');

  // Mounting takes the focus only when asked. Counted on every node, since focusBlock
  // focuses whichever surface it finds.
  let focused = 0;
  const focusWas = Node2.prototype.focus;
  Node2.prototype.focus = function () { focused++; };

  document.title = 'The host page';
  const pageEditor = held;
  const pageTitle = pageEditor.report().Title;

  const made = [];
  for (const room of ['graphite', 'frost']) {
    held = {};               // each editor plants its own handles as it is made
    const tag = new Editor();
    tag.setAttribute('theme', room);
    const found = [];
    tag.addEventListener('checks', e => found.push(e.detail));
    tag.connectedCallback();
    made.push({ tag, held, found });
  }
  held = pageEditor;
  const [a, b] = made;
  const unasked = focused;

  check('two elements make two editors, each in a root of its own',
        a.held.uiRoot() === a.tag.shadowRoot && b.held.uiRoot() === b.tag.shadowRoot
          && a.held.el('sheet') !== b.held.el('sheet'),
        `${a.held.uiRoot() === b.held.uiRoot() ? 'one root' : 'two roots'}, `
          + `${a.held.el('sheet') === b.held.el('sheet') ? 'one sheet' : 'two sheets'}`);

  // A document each, through the property a host uses.
  const docFor = (ed, title, words, language) => {
    const r = JSON.parse(JSON.stringify(ed.tag.report));
    r.Title = title;
    if (language) r.Language = language; else delete r.Language;
    const para = ed.held.newItem('AdvancedTextItem');
    para.Runs = [{ Text: words, Font: null }];
    r.Sections[0].Childs.find(c => c.$type === 'Body').Childs = [para];
    return r;
  };
  a.tag.report = docFor(a, 'Alpha', 'Alpha writes here', 'en-GB');
  b.tag.report = docFor(b, 'Beta', 'Beta writes here', null);

  const sheetText = ed => textNodesIn(ed.held.el('sheet')).map(t => t.textContent).join('');
  check('each holds its own document',
        a.tag.report.Title === 'Alpha' && b.tag.report.Title === 'Beta',
        `${a.tag.report.Title} / ${b.tag.report.Title}`);
  check('and draws it into its own sheet, never the other one\'s',
        sheetText(a).includes('Alpha writes here') && !sheetText(a).includes('Beta')
          && sheetText(b).includes('Beta writes here') && !sheetText(b).includes('Alpha'),
        `${sheetText(a)} / ${sheetText(b)}`);

  // Typing into one, the way the page types: into its surface, then its input handler.
  const typingA = surfaceOf(a.held.el('sheet').childNodes[0]);
  typingA.appendChild(new Text2(' and goes on'));
  typingA.fire('input');
  check('typing into one reaches its document and no other',
        JSON.stringify(a.tag.report).includes('and goes on')
          && !JSON.stringify(b.tag.report).includes('and goes on')
          && !JSON.stringify(pageEditor.report()).includes('and goes on'),
        'reached: ' + ([['the first', a.tag.report], ['the second', b.tag.report], ['the page\'s', pageEditor.report()]]
          .filter(([, r]) => JSON.stringify(r).includes('and goes on')).map(([n]) => n).join(', ') || 'nothing'));

  const last = ed => ed.found[ed.found.length - 1] || [];
  check('each tells its own host what it found',
        !last(a).some(i => i.rule === 'ua.language') && last(b).some(i => i.rule === 'ua.language'),
        `${last(a).map(i => i.rule).join(',') || 'nothing'} / ${last(b).map(i => i.rule).join(',') || 'nothing'}`);

  check('each wears the room it was given',
        a.tag.getAttribute('data-theme') === 'graphite' && b.tag.getAttribute('data-theme') === 'frost',
        `${a.tag.getAttribute('data-theme')} / ${b.tag.getAttribute('data-theme')}`);

  check('and neither renamed the page it was put in', document.title === 'The host page', document.title);

  check('the page\'s own editor kept its document while two more were made',
        pageEditor.report().Title === pageTitle, pageEditor.report().Title);

  check('neither took the focus, not having been asked to', unasked === 0, `${unasked} focus calls`);
  held = {};
  const asking = new Editor();
  asking.setAttribute('autofocus', '');
  asking.connectedCallback();
  held = pageEditor;
  check('while one that says autofocus does', focused > unasked, `${focused - unasked} focus calls`);

  Node2.prototype.focus = focusWas;

  // Side by side is where the layout showed. A face with no panel kept the panel's 300
  // pixels on its right, and the editor stopped where its content did rather than filling
  // the element a host had sized - both unseen while an editor had the window to itself.
  const markupOf = ed => ed.tag.shadowRoot.getElementById('frisket-editor-root').innerHTML;
  const pageMarkup = document.getElementById('frisket-editor-root').innerHTML;
  check('an editor with no panel lays out one column, not the panel\'s two',
        markupOf(a).includes('<main class="alone">') && /grid-template-columns\s*:\s*1fr\s*;/.test(cssFor('main.alone')),
        (markupOf(a).match(/<main[^>]*>/) || ['no main'])[0] + ' / ' + (cssFor('main.alone').trim() || 'no rule'));
  check('while the face that has the panel keeps both',
        pageMarkup.includes('<main>') && pageMarkup.includes('<aside'),
        (pageMarkup.match(/<main[^>]*>/) || ['no main'])[0]);
  const hostRule = cssFor('#frisket-editor-root');
  const sheetsAlone = styles.replace(/\/\*[\s\S]*?\*\//g, ' ');
  check('and the editor fills the room it is given, in a page and in an element',
        /height\s*:\s*100%/.test(hostRule) && /display\s*:\s*flex/.test(hostRule)
          && /(^|\n)\s*:host\s*\{[^}]*display\s*:\s*block/.test(sheetsAlone),
        hostRule.trim() || 'no rule for the host div');

  // And the shape that keeps it so. The fault was state at the top of the file, one copy
  // per page; the checks above catch the report and the root being shared, and would not
  // notice the selection cache, the pending style or the drag in progress going back out
  // the same way. So the top level is asked directly: what may live there is what
  // belongs to the page - its language, and the server's fonts, asked once - and nothing
  // an editor changes as somebody types.
  const PAGE_STATE = ['lang0', 'serverFontsAsked', 'facesLoaded'];
  const editorSource = readText(path.join(DIR, 'frisket-editor.js'));
  const topLevel = [...editorSource.matchAll(/^(?:let|var)\s+(\w+)/gm)].map(m => m[1]);
  const strays = topLevel.filter(name => !PAGE_STATE.includes(name));
  check('nothing an editor changes lives at the top of the file, one copy for the page',
        strays.length === 0 && PAGE_STATE.every(name => topLevel.includes(name)),
        strays.length ? 'at the top: ' + strays.join(', ') : topLevel.join(', '));
}

// --- line endings ------------------------------------------------------------------
// Asked here rather than trusted: the fault was invisible in the tree the harness was
// written in, and comes back the day somebody adds one raw read.
{
  const crlf = path.join(require('os').tmpdir(), `frisket-crlf-${process.pid}.txt`);
  fs.writeFileSync(crlf, 'one\r\ntwo\rthree\n');
  const read = readText(crlf);
  fs.unlinkSync(crlf);
  check('a file in CRLF is read with plain line breaks',
        read === 'one\ntwo\nthree\n', JSON.stringify(read));

  // This file's own reads, all of them: exactly one goes to the disk directly, and it
  // is readText's own.
  const direct = (readText(__filename).match(/fs\.readFileSync\(/g) || []).length;
  check('every file this harness reads, it reads through readText',
        direct === 1, `${direct - 1} read directly`);
}

// --- the engine an editor is told of ------------------------------------------------
// Asked last, since they wait on answers. The address of the font server was one constant
// at the top of the editor, written for the preview server on this machine: a host could
// not name its own, and every element asked localhost whatever page it was in. Each editor
// is told its own now, through the host's fetch, and one told of none asks nobody.
//
// A run whose answers never come would hang, or - with nothing left to wait on - let Node
// leave with exit code 0 and no summary, which reads as a pass. So it fails after twenty
// seconds and says so; the timer is not unref()'d, which is what allowed the second.
setTimeout(() => {
  console.log(`  ✗ the asynchronous checks never finished, after ${checked} checks`);
  process.exit(1);
}, 20000);

(async () => {
  const later = () => new Promise(resolve => setTimeout(resolve, 0));
  const until = async test => { for (let i = 0; i < 50 && !test(); i++) await later(); return test(); };

  const Editor = customElements.get('frisket-editor');
  const LIST = [
    { family: 'House Face', subfamily: 'Regular', bold: false, italic: false, isDefault: true },
    { family: 'Other Face', subfamily: 'Regular', bold: false, italic: false, isDefault: false },
  ];
  const asked = [];
  const hostFetch = (url, init) => {
    asked.push(url);
    return Promise.resolve({ ok: true, json: async () => LIST });
  };

  // Nobody else may be asked while these run: the browser's own fetch is counted.
  const pageFetch = global.fetch;
  let strayed = 0;
  global.fetch = (...a) => { strayed++; return Promise.reject(new Error('not this one')); };

  const make = (attrs, props = {}) => {
    const kept = held;
    held = {};
    const tag = new Editor();
    for (const [name, value] of Object.entries(attrs)) tag.setAttribute(name, value);
    Object.assign(tag, props);
    tag.fetch = hostFetch;
    tag.connectedCallback();
    const made = { tag, held };
    held = kept;
    return made;
  };

  const told = make({ fonts: 'library', server: 'http://engine.test/' });
  await until(() => told.held.el('family').options.some(o => o.value === 'House Face'));
  const families = told.held.el('family').options.map(o => o.value).filter(Boolean);
  check('an editor asks the engine it is told, through the host\'s fetch',
        asked.includes('http://engine.test/fonts') && families.includes('House Face') && strayed === 0,
        `${asked.join(', ') || 'nothing asked'} -> ${families.join(', ') || 'no families'}`);

  const before = asked.length;
  const second = make({ fonts: 'library', server: 'http://engine.test' });
  await until(() => second.held.el('family').options.some(o => o.value === 'House Face'));
  check('and a second editor told the same engine does not ask it again',
        asked.length === before && second.held.el('family').options.some(o => o.value === 'House Face'),
        `${asked.length - before} more request(s)`);

  const other = make({ fonts: 'library', server: 'https://elsewhere.test' });
  await until(() => asked.length > before);
  check('while one told another engine asks that one',
        asked.includes('https://elsewhere.test/fonts'), asked.slice(before).join(', ') || 'nothing asked');

  const count = asked.length;
  // What it offers instead is the families the document already names - the lean face has
  // no hint to read, so the list itself is asked.
  const none = make({ fonts: 'library' });
  await later(); await later();
  const offered = none.held.el('family').options.map(o => o.value).filter(Boolean);
  check('and an element told of no engine asks nobody - not even this machine',
        asked.length === count && strayed === 0 && !offered.includes('House Face'),
        `${asked.length - count + strayed} request(s), offering ${offered.join(', ') || 'the document default alone'}`);

  global.fetch = pageFetch;

  // --- the host's merge fields ------------------------------------------------------
  // What the host will fill in when it prints - a name, an amount, a date, a signature -
  // offered by the host and inserted at the caret as the token the engine has always
  // bound. A picture is the one kind that is not a token: a block the data fills.
  const FIELDS = [
    { path: 'Destinataire.Nom', label: 'Nom du destinataire', type: 'text', sample: 'Maître Dupont' },
    { path: '{Dossier.Montant}', label: 'Montant', type: 'currency', sample: '12 480,00 EUR' },
    { path: 'Cabinet.Logo', label: 'Logo du cabinet', type: 'image' },
    { path: 'Client.Signature', label: 'Signature', type: 'image', sample: 'data:image/png;base64,iVBORw0KGgo=' },
    'Dossier.Numero',
    { path: '', label: 'no path, no field' },
  ];
  const press = node => node.onclick({ stopPropagation() {} });
  const entries = ed => ed.held.el('fieldMenu').childNodes.filter(n => n.tagName === 'BUTTON');
  const entry = (ed, label) => entries(ed).find(b => b.childNodes[0].textContent === label);
  const bodyOf = ed => ed.tag.report.Sections[0].Childs.find(c => c.$type === 'Body').Childs;
  const imagesIn = node => [
    ...(node.tagName === 'IMG' ? [node] : []),
    ...(node.childNodes || []).flatMap(imagesIn)];

  const plain = make({});
  check('with no fields offered there is no button to offer them',
        plain.held.el('fieldsTool').hidden === true, plain.held.el('fieldsTool').hidden ? 'hidden' : 'shown');

  const writer = make({}, { fields: FIELDS });
  press(writer.held.el('addField'));
  const menuText = textNodesIn(writer.held.el('fieldMenu')).map(n => n.textContent).join(' | ');
  check('the fields a host offers are on a menu, by kind, each a label over its token',
        writer.held.el('fieldsTool').hidden === false && writer.held.el('fieldMenu').hidden === false
          && entries(writer).length === 5 && menuText.includes('Nom du destinataire')
          && menuText.includes('{Dossier.Montant}') && !menuText.includes('{{')
          && menuText.includes('Amount') && menuText.includes('Maître Dupont'),
        menuText.slice(0, 160));

  // A paragraph with the caret at its end, as typing leaves it.
  const letter = JSON.parse(JSON.stringify(writer.tag.report));
  const para = writer.held.newItem('AdvancedTextItem');
  para.Runs = [{ Text: 'Bonjour ', Font: null }];
  letter.Sections[0].Childs.find(c => c.$type === 'Body').Childs = [para];
  writer.tag.report = letter;
  const block = writer.held.el('sheet').childNodes[0];
  writer.held.markCurrent(block);
  currentEditable = surfaceOf(block);
  press(entry(writer, 'Nom du destinataire'));
  currentEditable = null;
  const said = (bodyOf(writer)[0].Runs || []).map(r => r.Text).join('');
  check('a field goes in where the caret is, as its token, and the document has it at once',
        said === 'Bonjour {Destinataire.Nom}', JSON.stringify(said));

  // Nowhere to type: a paragraph of its own.
  writer.held.markCurrent(null);
  const blocks = bodyOf(writer).length;
  press(entry(writer, 'Dossier.Numero'));
  const added = bodyOf(writer)[bodyOf(writer).length - 1];
  check('with no paragraph under the caret, a field starts one',
        bodyOf(writer).length === blocks + 1 && added.$type === 'AdvancedTextItem'
          && added.Runs[0].Text === '{Dossier.Numero}',
        `${added.$type}: ${JSON.stringify(added.Runs || added.Text)}`);

  // A picture: a block the data fills, described, with nothing of its own to draw.
  // Looked for in the editor's own document, not in `report`: that hands out a copy, the
  // findings point at the live items, and a comparison between the two is never true - which
  // is how this check first passed with the picture's description taken out.
  press(entry(writer, 'Logo du cabinet'));
  const liveBody = writer.held.report().Sections[0].Childs.find(c => c.$type === 'Body').Childs;
  const logo = liveBody.find(c => c.$type === 'ImageItem'
                                  && (c.SourceField === 'Cabinet.Logo' || c.AltText === 'Logo du cabinet'));
  const findings = (writer.tag.issues || []).filter(i => i.item === logo).map(i => i.rule);
  check('a picture field is a picture the data fills, described, and clear of the checks',
        !!logo && logo.SourceField === 'Cabinet.Logo' && logo.AltText === 'Logo du cabinet'
          && !(logo.Source && logo.Source.Data) && findings.length === 0,
        logo ? `${logo.SourceField}, "${logo.AltText}", ${findings.join(', ') || 'no findings'}` : 'not inserted');

  const drawn = imagesIn(writer.held.el('sheet'));
  const frame = drawn.find(img => String(img.src).startsWith('data:image/svg+xml')
                                  && decodeURIComponent(img.src).includes('Cabinet.Logo'));
  press(entry(writer, 'Signature'));
  const sampled = imagesIn(writer.held.el('sheet')).find(img => img.src === FIELDS[3].sample);
  check('and it is drawn as a frame naming its path, or as the sample the host gave',
        !!frame && !!sampled, `${frame ? 'a frame' : 'no frame'}, ${sampled ? 'the sample' : 'no sample'}`);

  // A catalogue that comes later, and one handed over after connecting.
  const later_ = make({}, { fields: Promise.resolve(['Dossier.Ville']) });
  const before_ = later_.held.el('fieldsTool').hidden;
  await later(); await later();
  check('a catalogue that arrives later shows the button when it does',
        before_ === true && later_.held.el('fieldsTool').hidden === false,
        `${before_ ? 'hidden' : 'shown'} then ${later_.held.el('fieldsTool').hidden ? 'hidden' : 'shown'}`);
  plain.tag.fields = ['Dossier.Ville'];
  check('and so does one handed over after the editor is in the page',
        plain.held.el('fieldsTool').hidden === false, plain.held.el('fieldsTool').hidden ? 'hidden' : 'shown');

  console.log(failures ? `\n${checked} checks, ${failures} FAILED`
                       : `\n${checked} checks, all good`);
  process.exit(failures ? 1 : 0);
})();
