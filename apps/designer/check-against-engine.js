// Runs the designer's layout code headlessly and checks it against the engine.
//
// The engine writes the report it bound and the bands it computed for it; this
// feeds the same section through the designer's JavaScript echo and compares.
// Nothing is hardcoded, so the check follows the template rather than rotting
// behind it.
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const stubEl = () => {
  const kept = [];
  const self = new Proxy({
  children: kept,
  style: { setProperty() {}, removeProperty() {} },
  checked: false, value: '', innerHTML: '', textContent: '', files: [],
  appendChild: n => { kept.push(n); return n; },
  append: (...n) => kept.push(...n),
  classList: { add() {}, remove() {}, toggle() {}, contains: () => false },
  querySelector: () => null, querySelectorAll: () => [],
  getContext: () => ({ font: '', measureText: t => ({ width: String(t).length * 7 }) }),
  addEventListener: () => {}, click: () => {},
  setAttribute() {}, removeAttribute() {}, getAttribute: () => null
  }, { get: (t, k) => (k in t ? t[k] : undefined), set: (t, k, v) => (t[k] = v, true) });
  return self;
};

/// Every scrap of text under a stub element. What a panel put on the screen, which
/// is the only way to ask which fields it showed.
const wordsIn = node => {
  if (!node || typeof node !== 'object') return '';
  // innerHTML as well as textContent: the stub cannot parse markup, and the tree
  // writes its rows that way, so reading only textContent finds an empty string and
  // says the tree is empty.
  const own = [node.textContent, node.innerHTML]
    .filter(v => typeof v === 'string').join(' ');
  return own + ' ' + (node.children || []).map(wordsIn).join(' ');
};

global.document = {
  createElement: stubEl, getElementById: stubEl,
  // querySelector has to answer with an element, not null: the page reads back
  // panel widths off <main> as it starts, and a stub that returns nothing turns
  // a chrome detail into a failure of the layout check.
  querySelector: stubEl, querySelectorAll: () => [], body: stubEl(),
  // The page stamps data-theme on the root element as it starts.
  documentElement: stubEl(),
  addEventListener() {}
};
// The page asks the browser about the shape of the screen and can open a second
// window for the preview. Neither exists here, and neither is what is under test.
global.window = {
  matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
  addEventListener() {},
  open: () => null,
  getScreenDetails: undefined
};

// The zoom measures the room it has. Here there is none, and none is needed: what
// is under test is where things land in millimetres, not how large they are drawn.
global.getComputedStyle = () => ({
  paddingLeft: "0px", paddingRight: "0px", paddingTop: "0px", paddingBottom: "0px",
  // The detached preview reads the palette off the root element to carry it into a
  // window of its own. There is no second window here, and no palette to carry.
  getPropertyValue: () => ""
});

global.DOMPoint = class { constructor(x, y) { this.x = x; this.y = y; } };
global.alert = () => {};
global.confirm = () => false;
global.FileReader = class {};
global.Blob = class {};
global.URL = { createObjectURL: () => '', revokeObjectURL: () => {} };
global.Image = class {};
global.fetch = () => Promise.reject(new Error('no server in the harness'));

// A report holds a list of sections; everything here checks one, which is the first.
// Written once rather than at each site.
const firstDoc = r => r.Sections[0];

const [, , scriptPath, boundPath, bandsPath, templatePath, quotePath,
       faultyPath, findingsPath, flatPath, flatFindingsPath] = process.argv;

// Give it the page itself and it pulls the script out, which is one fewer step to
// get wrong and - the reason it matters - lets it see the stylesheet too, which a
// pre-extracted .js file never carried. A .js path still works, minus the palette
// audit, which then has nothing to read.
const source = fs.readFileSync(scriptPath, 'utf8');
const isPage = /\.html?$/i.test(scriptPath);

// The repository, found by walking up to the file that marks it - never by counting
// levels from the page. Counting worked for exactly as long as every face lived two
// deep; the day the built pages moved to release/ the count was one too many, and the
// harness died reading an engine source one directory above the repository. Same rule
// and same reason as RepositoryRoot() on the C# side (section 12).
const REPO = (() => {
  let at = path.resolve(path.dirname(scriptPath));
  for (;;) {
    if (fs.existsSync(path.join(at, 'frisket.slnx'))) return at;
    const up = path.dirname(at);
    if (up === at) throw new Error(`no frisket.slnx above ${scriptPath}`);
    at = up;
  }
})();

// Where the parts live, which is not where the page necessarily does. The sweep below
// audits the stylesheets that SHIP; a built page carries copies of the ones it links
// and sits in a directory with none of its own, so sweeping beside it would quietly
// audit less - the very hole this sweep was written to close (section 5b).
const PARTS = path.join(REPO, 'apps', 'designer');

// The page loads the core from a file of its own. Follow the src the way a browser
// would, in the order written, rather than naming the file here - the page is what
// decides what it is made of, and a harness that keeps its own list is one rename
// away from checking something the page no longer loads.
const linked = isPage
  ? [...source.matchAll(/<script src="([^"]+)"><\/script>/g)]
      .map(m => fs.readFileSync(path.join(path.dirname(scriptPath), m[1]), 'utf8'))
  : [];

const inline = isPage
  ? [...source.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1])
  : [source];

const script = [...linked, ...inline].join('\n');

// Scripts first, or the stylesheet the detached preview builds inside a JS string
// counts as one of the page's own.
//
// Every .css in the directory, and NOT only the ones this page links. Following the
// links audited whichever face the harness happened to be handed - the designer - and
// left frisket-editor.css unread, fifteen loose literals in the face that ships, while
// section 5b went on promising the check catches exactly that. A check that keeps its
// own list of what to check is one addition away from checking less than it says
// (section 16); finding them is the only version of this that stays true.
//
// And the pieces are a SET. A built face carries a copy of every part it links, so
// reading the parts and then the page gives each stylesheet twice - which is not merely
// noisy: "light sets the same names as dark" then compares dark with its own duplicate,
// and every count doubles. A check that reads its subject twice is section 16's fault in
// miniature.
//
// The page's OWN <style> blocks are not duplicates and must still be read, so this is a
// dedup and not a choice of source. The generator stamps each sheet it swallows with a
// "/* name.css */" line saying where it came from, which is exactly what stops the two
// copies comparing equal - so the key ignores it. Both faces report the same numbers
// again, and that agreement is itself worth something: it says the built page carries
// the same material and not a subset of it.
const bare = t => t.replace(/^\s*\/\*[^\n*]*\.css\s*\*\/\s*/, '').trim();

const styles = isPage
  ? [...new Map([
      ...fs.readdirSync(PARTS).filter(f => f.endsWith('.css'))
          .map(f => fs.readFileSync(path.join(PARTS, f), 'utf8')),
      ...[...source.replace(/<script>[\s\S]*?<\/script>/g, '')
                .matchAll(/<style>([\s\S]*?)<\/style>/g)].map(m => m[1]),
    ].map(t => [bare(t), t]).filter(([k]) => k)).values()].join('\n')
  : null;

const sandbox = {};
eval(script +
     '\nsandbox.layout = layout; sandbox.render = render; sandbox.blankReport = blankReport;' +
     '\nsandbox.moveChild = moveChild; sandbox.watermarkMarks = watermarkMarks; sandbox.backAsPage = backAsPage; sandbox.hydrate = hydrate;' +
     '\nsandbox.validate = validate; sandbox.roleOf = roleOf;' +
     '\nsandbox.STRINGS = STRINGS; sandbox.THEMES = THEMES; sandbox.themeOrAuto = themeOrAuto;' +
     '\nsandbox.backdropArea = backdropArea; sandbox.WATERMARK_INK = WATERMARK_INK;' +
     '\nsandbox.forEngine = forEngine; sandbox.expandCarried = expandCarried;' +
     '\nsandbox.ui = ui; sandbox.setData = setData; sandbox.resolvedImageSource = resolvedImageSource;' +
     '\nsandbox.drawProps = drawProps; sandbox.parentOf = parentOf; sandbox.doc0 = doc0;' +
     '\nsandbox.setSectionIndex = setSectionIndex; sandbox.reportNode = reportNode;' +
     '\nsandbox.setSelected = v => { selected = v; }; sandbox.getReport = () => report;' +
     '\nsandbox.setReport = v => { report = v; };');

// The alpha the engine defaults a watermark to, read from its own source. Typed here
// it would be a third copy of a number that already lives in two places, and the check
// below would then be comparing the harness with itself.
const engineWatermarkAlpha = (() => {
  const source = fs.readFileSync('engine/Model/Watermark.cs', 'utf8');
  const found = /Color = new Color\(128, 128, 128, (\d+)\)/.exec(source);
  return found ? Number(found[1]) / 255 : NaN;
})();

let failures = 0;
let checked = 0;
const check = (name, ok, detail = '') => {
  console.log(`  ${ok ? '✓' : '✗'} ${name}${detail ? ': ' + detail : ''}`);
  checked++; if (!ok) failures++;
};
const near = (a, b, tol = 0.01) => Math.abs(a - b) <= tol;

console.log('Designer against the engine');

const bound = JSON.parse(fs.readFileSync(boundPath, 'utf8'));
const bands = JSON.parse(fs.readFileSync(bandsPath, 'utf8'));
const out = sandbox.layout(bound);

check('the engine\'s own report lays out', out.placed.length > 0, out.placed.length + ' items');
check('page size read back', out.page.width === 210 && out.page.height === 297,
      `${out.page.width}x${out.page.height}`);

const kinds = new Set(out.placed.map(p => p.item.$type || 'Section'));
for (const kind of ['Header', 'Footer', 'Body', 'TableRow', 'TextItem', 'LineItem', 'ImageItem'])
  check(`${kind} understood`, kinds.has(kind));

// The bands are where the whole layout hangs from. The echo cannot match the engine
// to the millimetre - it has no font file, so it assumes a cap-height ratio and the
// browser's own metrics - but it must follow the same rules and land close. Anything
// further apart than a few millimetres means the two have actually diverged.
const CLOSE = 5;
const header = out.placed.find(p => p.item.$type === 'Header');
const footer = out.placed.find(p => p.item.$type === 'Footer');

check('header band follows the engine', near(header.frame.top, bands.header.top)
      && near(header.frame.bottom, bands.header.bottom, CLOSE),
      `${header.frame.top.toFixed(2)}..${header.frame.bottom.toFixed(2)} vs ` +
      `${bands.header.top.toFixed(2)}..${bands.header.bottom.toFixed(2)}`);

check('footer band follows the engine', near(footer.frame.bottom, bands.footer.bottom)
      && near(footer.frame.top, bands.footer.top, CLOSE),
      `${footer.frame.top.toFixed(2)}..${footer.frame.bottom.toFixed(2)} vs ` +
      `${bands.footer.top.toFixed(2)}..${bands.footer.bottom.toFixed(2)}`);

check('the body gets what is left', near(out.band.top, header.frame.bottom)
      && near(out.band.bottom, footer.frame.top),
      `${out.band.top.toFixed(2)}..${out.band.bottom.toFixed(2)}`);

// Columns: the echo must reach the same edges as the engine's own arithmetic.
const rows = out.placed.filter(p => p.item.$type === 'TableRow' && p.gridEdges);
check('table columns computed', rows.length > 0, rows.length + ' rows with a grid');
if (rows.length) {
  const e = rows[0].gridEdges;
  check('columns span the printable width', near(e[0], 9) && near(e[e.length - 1], 201),
        e.map(v => v.toFixed(1)).join(' / '));
  check('columns are contiguous and increasing',
        e.every((v, i) => i === 0 || v >= e[i - 1] - 0.001));
}

check('nothing runs off the sheet',
      out.placed.every(p => !Number.isFinite(p.frame.bottom) || p.frame.bottom <= 297.01));

const svg = sandbox.render(bound, null);
check('renders SVG', svg.startsWith('<svg') && svg.includes('</svg>'), svg.length + ' chars');
// Read off the section rather than written down here: which words a bound
// section carries is the engine's business, and naming one made this fail the
// day the samples were translated rather than the day binding broke.
const texts = [];
(function walk(node) {
  if (!node || typeof node !== 'object') return;
  if (typeof node.Text === 'string' && node.Text.length > 6 && !node.Text.includes('{'))
    texts.push(node.Text);
  for (const key of Object.keys(node)) walk(node[key]);
})(bound);

const onPage = texts.filter(t => svg.includes(t));
check('the bound values are on the page',
      texts.length > 5 && onPage.length > 0,
      `${onPage.length} of ${texts.length} resolved texts found`);
check('no token left behind', !svg.includes('{Customer.'));

// The unbound template must still load: that is what the designer opens.
const template = JSON.parse(fs.readFileSync(templatePath, 'utf8'));
check('the template itself also lays out', sandbox.layout(template).placed.length > 0);
check('a blank template lays out', sandbox.layout(sandbox.blankReport()).placed.length > 0);

// Fields, if a template holding them was given. The echo has to understand these
// too, or the designer shows a blank where a form is going to be.
if (quotePath) {
  const quote = JSON.parse(fs.readFileSync(quotePath, 'utf8'));
  const form = sandbox.layout(quote);
  const fields = form.placed.filter(p => p.item.$type === 'FieldItem');

  check('fields understood', fields.length > 0, fields.length + ' placed');
  check('each has a box', fields.every(f => Number.isFinite(f.frame.top) && f.frame.height > 0));
  check('a checkbox comes out square', fields
        .filter(f => f.item.Kind === 'Checkbox')
        .every(f => Math.abs(f.frame.width - f.frame.height) < 0.01));

  const one = fields.find(f => f.item.Kind === 'Text' && !f.item.Multiline);
  const many = fields.find(f => f.item.Multiline);

  // Not "pass if we cannot find them". A template stopped writing Kind once Text
  // became the default it omits, and this check went on reporting success while
  // finding nothing to compare - which is how a check earns its keep or does not.
  check('both kinds of text field are there', !!one && !!many,
        (one ? '' : 'aucun simple ') + (many ? '' : 'aucun multiligne'));
  check('a multiline field is taller', !!one && !!many && many.frame.height > one.frame.height * 2,
        one && many ? `${one.frame.height.toFixed(1)} vs ${many.frame.height.toFixed(1)}` : 'n/a');

  const drawn = sandbox.render(quote, null);
  check('the form is drawn, not skipped', drawn.includes('<rect') && drawn.length > 1500,
        drawn.length + ' chars');
}

// --- reordering ---------------------------------------------------------------
// Order is not decoration: a band laid out in Stack flows its children in this
// order. Moving a row down the tree moves a block down the page, so the index
// arithmetic is layout, and the off-by-one it invites only shows downwards.
{
  const kids = names => ({ n: 'P', Childs: names.split(' ').map(n => ({ n, Childs: [] })) });
  const shown = p => p.Childs.map(c => c.n).join(' ');
  const moved = (from, which, index, into) => {
    const p = kids(from);
    sandbox.moveChild(p, p.Childs[which], into || p, index);
    return into ? shown(into) : shown(p);
  };

  check('down: a lands under c', moved('a b c d', 0, 3) === 'b c a d', moved('a b c d', 0, 3));
  check('down: a lands last', moved('a b c d', 0, 4) === 'b c d a', moved('a b c d', 0, 4));
  check('up: d lands before b', moved('a b c d', 3, 1) === 'a d b c', moved('a b c d', 3, 1));
  check('up: c lands first', moved('a b c d', 2, 0) === 'c a b d', moved('a b c d', 2, 0));
  check('onto itself changes nothing', moved('a b c d', 1, 2) === 'a b c d', moved('a b c d', 1, 2));

  const into = kids('x y');
  check('across parents, no adjustment', moved('a b c d', 0, 1, into) === 'x a y', shown(into));
}

// --- watermark ----------------------------------------------------------------
// The echo lays the same grid as the engine. It cannot match copy for copy - it has
// no font file and guesses the word's run - but it must cover the sheet the same
// way, because a preview showing a clear corner says something false about the
// section about to be sent.
{
  const page = { width: 210, height: 297 };

  // The same mark the engine's own test uses. Left without a Font the echo falls back
  // to one rem, and the grid comes out five times denser than the engine's - which
  // would have compared two different sections and called them alike.
  const MARK = {
    Text: 'SPECIMEN', Mode: 'Tiled', Angle: 45, Spacing: 0,
    Font: { Size: 8, SizeUnit: 'Millimeter', Bold: true }
  };
  const marked = Object.assign(sandbox.blankReport(),
    { Watermark: MARK });
  const tiles = sandbox.watermarkMarks(marked, page);

  // The engine puts down 87 for this mark on A4. The echo guesses the word's run
  // from a ratio instead of a font file, so it cannot match exactly - but an order
  // of magnitude apart means the two are laying different grids.
  check('the echo tiles the sheet', tiles.length > 40 && tiles.length < 200,
        tiles.length + ' copies, moteur 87');

  const run = 'SPECIMEN'.length * (tiles[0] ? tiles[0].cap : 8) * 0.62;
  const rad = Math.PI / 4;
  let worst = 0;
  for (let x = 0; x <= 210; x += 10) {
    for (let y = 0; y <= 297; y += 10) {
      let near = Infinity;
      for (const m of tiles) {
        const dx = run * Math.cos(rad), dy = -run * Math.sin(rad);
        const t = Math.max(0, Math.min(1, ((x - m.x) * dx + (y - m.y) * dy) / (dx * dx + dy * dy)));
        near = Math.min(near, Math.hypot(x - (m.x + t * dx), y - (m.y + t * dy)));
      }
      worst = Math.max(worst, near);
    }
  }
  check('and leaves no corner clear', worst < 20, worst.toFixed(1) + ' mm');

  const once = Object.assign(sandbox.blankReport(),
    { Watermark: Object.assign({}, MARK, { Mode: 'Single' }) });
  check('single mode places one copy', sandbox.watermarkMarks(once, page).length === 1);

  // How heavy the mark is, which the echo carries as its own literal and the engine as
  // a default on Watermark.Font. Two copies of one number, and nothing was holding them
  // together: lightening the engine's alone would have left the preview drawing a
  // heavier DRAFT than the printer, which is the echo lying about the page.
  //
  // Read out of the engine's own template rather than typed here, or this becomes a
  // third copy of the same number.
  // The two rule sets, compared as SETS rather than as tallies.
  //
  // Everything below this file already compared was the faults that FIRED on one
  // fixture. A rule present in one validator and absent from the other, on a shape the
  // fixture never builds, is invisible to that - and one had been: sound.nestedSection
  // lived in the engine alone from the day it was written, with no call site here and no
  // label in either language, so frisket check refused a template this page called sound.
  //
  // Read out of the two sources rather than listed here, the way the watermark's alpha is
  // read out of Watermark.cs: a third list would be a third thing to keep in step.
  {
    const engineSource = fs.readFileSync(
      path.join(REPO, 'engine', 'Model', 'TemplateValidator.cs'), 'utf8');

    const ids = (text, pattern) =>
      new Set([...text.matchAll(pattern)].map(m => m[1]));

    const inEngine = ids(engineSource, /new TemplateIssue\("([a-z]+\.[A-Za-z]+)"/g);
    const inEcho = ids(script, /\badd\("([a-z]+\.[A-Za-z]+)"/g);
    const labelled = ids(script, /"issue\.([a-z]+\.[A-Za-z]+)":/g);

    const missing = (a, b) => [...a].filter(x => !b.has(x)).sort();

    check('every rule the engine can raise, this page can raise too',
          missing(inEngine, inEcho).length === 0,
          `engine has ${inEngine.size}, here ${inEcho.size}; only in the engine: ${missing(inEngine, inEcho).join(', ') || 'none'}`);

    check('and nothing here that the engine does not know',
          missing(inEcho, inEngine).length === 0,
          `only here: ${missing(inEcho, inEngine).join(', ') || 'none'}`);

    // A rule with no label prints its own key at a person, which is worse than silence
    // because it looks like a message.
    check('every rule raised here has something to say',
          missing(inEcho, labelled).length === 0,
          `unlabelled: ${missing(inEcho, labelled).join(', ') || 'none'}`);

    // And in EVERY language it speaks, asked of the dictionaries themselves.
    //
    // Two reasons it cannot be asked any other way. The sweep above collects
    // "issue.x" into a Set, so English and French collapse to one entry and a rule
    // labelled in one of them alone passes - which is how the sabotage that deleted an
    // English wording went green while French carried it. And t() falls back to
    // English when a language has no entry, which is right on screen and makes the
    // question unanswerable through that door: a missing French wording comes back as
    // perfectly good English.
    //
    // The languages are found rather than named, so a third one is held to this the day
    // it arrives instead of quietly not being.
    const languages = Object.keys(sandbox.STRINGS);
    check('the core declares the languages it speaks', languages.length >= 2,
          languages.join(', '));

    const mute = [];
    for (const code of languages)
      for (const rule of [...inEcho].sort())
        if (typeof sandbox.STRINGS[code]['issue.' + rule] !== 'string')
          mute.push(`${rule} (${code})`);

    check('and every rule has something to say in each of them', mute.length === 0,
          mute.join(', ') || `${inEcho.size} rule(s) x ${languages.length} language(s)`);
  }

  // Alignment, against the engine's own frames.
  //
  // Compared on the ANCHORED quantity, never on a raw edge: this echo has no font file,
  // so it measures a word to a different width, and a centred item's left edge is
  // av.left + (av.width - width) / 2 - which moves with that measurement. What does not
  // move is the edge the alignment anchors to. Start owns its left, End owns its right,
  // Center owns the midpoint, and all three are the same in both whatever the text
  // measures.
  //
  // The block this guards did not exist here until 24 August 2026: Style.AlignVertical
  // reached the engine, the panel and the flow editor and never this file.
  {
    const dir = path.join(path.dirname(templatePath), '..', 'align');
    const template = JSON.parse(fs.readFileSync(path.join(dir, 'aligned.template.json'), 'utf8'));
    const wanted = JSON.parse(fs.readFileSync(path.join(dir, 'aligned.frames.json'), 'utf8'));

    const laid = sandbox.layout(template);
    const mine = {};
    for (const p of laid.placed)
      if (p.item && p.item.$type === 'TextItem' && p.item.Text)
        mine[p.item.Text] = p.frame;

    // What each name's alignment anchors to, and therefore what must match.
    const anchor = {
      START:  f => [f.left, f.top],
      END:    f => [f.right, f.bottom],
      CENTRE: f => [(f.left + f.right) / 2, (f.top + f.bottom) / 2],
      PINNED: f => [f.left, f.top],
    };

    for (const name of Object.keys(anchor)) {
      const here = mine[name], there = wanted[name];
      if (!here || !there) {
        check(`${name} is placed here too`, false, here ? 'engine lost it' : 'echo lost it');
        continue;
      }
      const [ax, ay] = anchor[name](here);
      const [bx, by] = anchor[name](there);
      check(`${name} anchors where the engine anchors it`,
            near(ax, bx, 0.05) && near(ay, by, 0.05),
            `here ${ax.toFixed(2)},${ay.toFixed(2)} - engine ${bx.toFixed(2)},${by.toFixed(2)}`);
    }

    // And the three that align must be in three different places, or the four checks
    // above would all pass on an echo that aligns nothing.
    check('and the three alignments land three different ways',
          mine.START && mine.CENTRE && mine.END
          && mine.START.left < mine.CENTRE.left && mine.CENTRE.left < mine.END.left
          && mine.START.top < mine.CENTRE.top && mine.CENTRE.top < mine.END.top,
          mine.START ? `${mine.START.left.toFixed(1)} / ${mine.CENTRE.left.toFixed(1)} / ${mine.END.left.toFixed(1)}` : 'missing');
  }

  // Heading numbers, against the engine's own answer rather than against a list typed
  // here - which would be the harness agreeing with itself. Test 66 writes both files.
  //
  // Found beside the acme template rather than passed in: every output lives under one
  // tree, and two more positional arguments is two more things for a command in prose
  // to get wrong.
  {
    const dir = path.join(path.dirname(templatePath), '..', 'headings');
    const numbered = JSON.parse(fs.readFileSync(path.join(dir, 'numbered.template.json'), 'utf8'));
    const wanted = JSON.parse(fs.readFileSync(path.join(dir, 'numbered.expected.json'), 'utf8'));

    const drawn = [...sandbox.render(numbered, null, 0)
      .matchAll(/<text [^>]*>([^<]*)<\/text>/g)].map(m => m[1]);

    check('the echo numbers headings exactly as the engine does',
          drawn.length === wanted.length && drawn.every((t, i) => t === wanted[i]),
          `echo ${JSON.stringify(drawn)}, engine ${JSON.stringify(wanted)}`);
  }

  {
    const rendered = sandbox.render(Object.assign(sandbox.blankReport(), { Watermark: MARK }), null, 0);
    const shown = [...rendered.matchAll(/fill-opacity="([0-9.]+)"/g)].map(m => Number(m[1]));
    const faintest = Math.min(...shown);

    check('the echo draws the watermark at the weight the engine defaults to',
          Math.abs(faintest - engineWatermarkAlpha) < 0.01,
          `echo ${faintest}, engine ${engineWatermarkAlpha}`);

  // The line above holds the echo's FALLBACK - the branch taken only when a mark
  // carries no colour of its own. Every mark the panel makes carries one, so the
  // branch that always runs was never in the comparison, and it had drifted: the
  // engine lowered the ink from 77 to 51 after measuring it too dark over a
  // paragraph, and the panel went on stamping 77. Both halves are held now.
  check('the shared ink is the engine\'s',
        Math.abs(sandbox.WATERMARK_INK.A / 255 - engineWatermarkAlpha) < 0.001,
        `${sandbox.WATERMARK_INK.A} vs ${Math.round(engineWatermarkAlpha * 255)}`);

  check('and the panel takes it rather than typing it again',
        !/Color:\s*\{\s*R:\s*128[^}]*A:\s*\d+/.test(source),
        'a number typed in the panel is a copy nothing holds');

  // --- the picture behind the page ------------------------------------------
  // Section 1 once more, and the half a harness exists for: the model, the panel,
  // the echo and this file all have to say one thing about one type.
  const backdropSource = fs.readFileSync('engine/Model/Backdrop.cs', 'utf8');
  const areaBlock = /enum BackdropArea\s*\{([\s\S]*?)^    \}/m.exec(backdropSource);
  const areas = areaBlock
    ? areaBlock[1].split('\n').map(l => l.trim())
        .filter(l => l && !l.startsWith('/')).map(l => l.replace(/,$/, ''))
    : [];

  check('the engine names the areas a backdrop can cover', areas.length === 2, areas.join(', '));

  const offered = [...source.matchAll(/<option value="(\w+)" data-i18n="BackdropArea\.(\w+)"/g)];
  check('the panel offers exactly those, in order',
        offered.length === areas.length
        && offered.every((m, i) => m[1] === areas[i] && m[2] === areas[i]),
        offered.map(m => m[1]).join(', '));

  for (const area of areas)
    check(`and both languages word ${area}`,
          (script.match(new RegExp(`"BackdropArea\\.${area}":`, 'g')) || []).length === 2);

  // The fitting modes, and the alignment the engine gives a backdrop - both read out
  // of the sources rather than typed here, or this would be comparing the harness with
  // itself. The engine fits a backdrop through an ephemeral ImageItem whose style says
  // Center; the echo has to encode the same choice in its preserveAspectRatio, and
  // xMinYMin against xMidYMin is a picture in the wrong half of the page.
  const scalings = [...backdropSource.matchAll(/ImageScaling\.(\w+)/g)].map(m => m[1]);
  const offeredFits = [...source.matchAll(/<option value="(\w+)" data-i18n="ImageScaling\.(\w+)"/g)];
  check('the panel offers the fitting modes the engine has',
        offeredFits.length === 3 && offeredFits.every(m => m[1] === m[2]),
        offeredFits.map(m => m[1]).join(', '));
  check('and the type names the one it defaults to', scalings.includes('Stretch'),
        scalings.join(', '));

  for (const mode of ['Stretch', 'Uniform', 'None'])
    check(`and both languages word ${mode}`,
          (script.match(new RegExp(`"ImageScaling\.${mode}":`, 'g')) || []).length === 2);

  // The defaults, read off the model: a picture smaller than the sheet is centred on
  // both axes, and the echo has to encode the same two choices in its
  // preserveAspectRatio. xMinYMin against xMidYMid is a picture in the wrong corner.
  const defaultAcross = /AlignHorizontal { get; set; } = ItemAlign\.(\w+)/.exec(backdropSource);
  const defaultDown = /AlignVertical { get; set; } = ItemAlign\.(\w+)/.exec(backdropSource);
  const acrossOf = { Start: 'xMin', Center: 'xMid', End: 'xMax' };
  const downOf = { Start: 'YMin', Center: 'YMid', End: 'YMax' };
  const engineAlign = defaultAcross;
  const drawnFit = report => {
    const svg = sandbox.render(report, null, 0);
    return (/<image[^>]*preserveAspectRatio="([^"]+)"/.exec(svg) || [])[1];
  };

  // Geometry, against the bands the engine itself reported for this invoice. A margin
  // backdrop starts where the HEADER band starts, not where the body does - 8 mm here
  // against 92.65, which is the entire distance between reading the right band and the
  // wrong one, and invisible on any template without a header.
  const papered = JSON.parse(JSON.stringify(bound));
  papered.Backdrop = { Source: { MediaType: 'image/png', Data: 'QkFDS0Ryb3A=' },
                       Area: 'Margins', Opacity: 1, PageScope: 'All' };

  const paperedLaid = sandbox.layout(papered, 0);
  const inside = sandbox.backdropArea(papered, paperedLaid.page, paperedLaid.placed,
                                      paperedLaid.band, paperedLaid.section);

  check('a margin backdrop starts at the engine\'s header band',
        near(inside.y, bands.header.top),
        `${inside.y.toFixed(2)} vs ${bands.header.top.toFixed(2)}`);
  check('and stops at the engine\'s footer band',
        near(inside.y + inside.h, bands.footer.bottom),
        `${(inside.y + inside.h).toFixed(2)} vs ${bands.footer.bottom.toFixed(2)}`);

  papered.Backdrop.Area = 'Sheet';
  const whole = sandbox.backdropArea(papered, paperedLaid.page, paperedLaid.placed,
                                     paperedLaid.band, paperedLaid.section);
  check('and the whole sheet is the whole sheet',
        whole.x === 0 && whole.y === 0
        && near(whole.w, paperedLaid.page.width) && near(whole.h, paperedLaid.page.height),
        JSON.stringify(whole));

  // Behind, which is what the word means: before the text, and before a watermark
  // that is itself behind.
  // Found by its own payload, not by the first <image> on the page: the invoice
  // carries a logo, so "an image comes before the text" was true with the backdrop
  // drawn on top of everything, and the sabotage that moved it passed.
  const papercut = sandbox.render(papered, null, 0);
  const at = papercut.indexOf('base64,QkFDS0Ryb3A');
  check('the backdrop is drawn before anything else',
        at > 0 && at < papercut.indexOf('<text'),
        `backdrop at ${at}, first text at ${papercut.indexOf('<text')}`);

  // Faded on the page, not only in the file - the fault this project keeps finding is
  // a number that is right somewhere nothing draws from.
  papered.Backdrop.Scaling = 'Stretch';
  check('a stretched backdrop fills its rectangle in the drawing',
        drawnFit(papered) === 'none', drawnFit(papered));

  papered.Backdrop.Scaling = 'Uniform';
  delete papered.Backdrop.AlignHorizontal;
  delete papered.Backdrop.AlignVertical;
  check('and one keeping its proportions falls where the model says it falls',
        drawnFit(papered) === `${acrossOf[defaultAcross && defaultAcross[1]]}`
                            + `${downOf[defaultDown && defaultDown[1]]} meet`,
        `${drawnFit(papered)} against ${defaultAcross && defaultAcross[1]}/`
        + `${defaultDown && defaultDown[1]}`);

  // And a placement that was asked for reaches the drawing. Nine positions out of two
  // axes, so the corners are what prove both are read: the top-left and the
  // bottom-right cannot both be right by accident.
  papered.Backdrop.AlignHorizontal = 'Start';
  papered.Backdrop.AlignVertical = 'Start';
  check('a backdrop placed top-left is drawn top-left',
        drawnFit(papered) === 'xMinYMin meet', drawnFit(papered));

  papered.Backdrop.AlignHorizontal = 'End';
  papered.Backdrop.AlignVertical = 'End';
  check('and one placed bottom-right is drawn bottom-right',
        drawnFit(papered) === 'xMaxYMax meet', drawnFit(papered));

  const offeredPlaces = [...source.matchAll(/<option value="(\w+)" data-i18n="ItemAlign\.(\w+)"/g)];
  check('the panel offers three positions on each axis',
        offeredPlaces.length === 6, offeredPlaces.map(m => m[1]).join(', '));

  papered.Backdrop.Scaling = 'Stretch';
  delete papered.Backdrop.AlignHorizontal;
  delete papered.Backdrop.AlignVertical;

  papered.Backdrop.Opacity = 0.25;
  check('an opacity reaches the drawing',
        /<image[^>]*opacity="0.25"/.test(sandbox.render(papered, null, 0)));

  papered.Backdrop.Opacity = 1;
  check('and full strength writes no attribute',
        !/<image[^>]*opacity=/.test(sandbox.render(papered, null, 0)));

  papered.Backdrop.Opacity = 0;
  // The payload is nonsense on purpose, and distinctive on purpose: every PNG in
  // base64 begins iVBORw0KGgo, so a marker cut from the front of one matches the
  // invoice's own logo and the assertion passes on somebody else's picture.
  check('a backdrop turned off draws nothing',
        !/base64,QkFDS0Ryb3A/.test(sandbox.render(papered, null, 0)));
  }

}

// --- back page ----------------------------------------------------------------
// The designer previews the back on its own tab, which means laying it out as a
// page rather than as an item of the front. Its content has to reach the sheet's
// edges minus the back's own padding - a back set with the document's margins
// would look right in the designer and print differently.
{
  const clause = { $type: 'TextItem', Text: 'Article 1 - conditions', Style: {}, Font: {} };
  const marked = Object.assign(sandbox.blankReport(), {
    BackPage: {
      Content: { $type: 'ContainerItem', Layout: 'Stack', Style: {}, Childs: [clause] },
      Scope: 'EveryPage',
      Padding: { Left: 12, Top: 12, Right: 12, Bottom: 12 }
    }
  });

  const out = sandbox.layout(sandbox.backAsPage(marked));
  const placed = out.placed.filter(p => p.item.$type === 'TextItem');

  check('the back lays out on its own', placed.length === 1, placed.length + ' items');
  check('inside the back\'s own margins', placed.length > 0
        && placed[0].frame.left >= 11.9 && placed[0].frame.left <= 12.6,
        placed.length ? placed[0].frame.left.toFixed(2) + ' mm du bord' : 'n/a');
  check('and the sheet is a full page', out.page.width === 210 && out.page.height === 297);
}

// --- a sized item keeps its size ----------------------------------------------
// hydrate used to swap in a merged copy of Style on every layout, which left the
// properties panel writing into an orphaned object: the field showed one value and
// the section kept another, and a barcode collapsed to a flat line.
{
  const doc = sandbox.blankReport();
  const body = firstDoc(doc).Childs.find(c => c.$type === 'Body');
  const code = {
    $type: 'BarcodeItem', Value: 'X', Symbology: 'Code128',
    Style: { Location: { LeftValue: 5, TopValue: 5, WidthValue: 60, HeightValue: 20 } }
  };
  body.Layout = 'Absolute';
  body.Childs.push(code);

  const seen = sandbox.layout(doc).placed.find(p => p.item === code);
  check('a sized barcode gets its size', !!seen && Math.abs(seen.frame.height - 20) < 0.01,
        seen ? seen.frame.height.toFixed(2) + ' mm' : 'absent');

  // The objects the editor holds must survive a layout, or the next edit is lost.
  const held = code.Style.Location;
  sandbox.layout(doc);
  check('and the style object is the same one', code.Style.Location === held);

  held.HeightValue = 35;
  const after = sandbox.layout(doc).placed.find(p => p.item === code);
  check('so an edit through it lands', Math.abs(after.frame.height - 35) < 0.01,
        after.frame.height.toFixed(2) + ' mm');
}

// --- opening a template -------------------------------------------------------
// A template writes only what it decides, so one that was saved may carry no
// Location, no Margin and no Font at all. The properties panel reads those the
// moment a document is opened - before any layout ran - and reaching into what is
// not there threw on the very first template anyone tried to open.
{
  const saved = JSON.parse(fs.readFileSync(templatePath, 'utf8'));
  firstDoc(saved).$type = 'Section';
  sandbox.hydrate(firstDoc(saved));

  const doc = firstDoc(saved);
  check('an opened template has a Location', !!doc.Style && !!doc.Style.Location);
  check('with the edges the panel reads',
        doc.Style.Location.LeftValue !== undefined && doc.Style.Margin.Left !== undefined);

  const header = doc.Childs.find(c => c.$type === 'Header') || { Childs: [] };
  const text = header.Childs.find(c => c.$type === 'TextItem');
  check('and its text items have a Font', !!text && text.Font !== undefined);
}

// --- columns in the echo ------------------------------------------------------
// The preview has to divide the page the same way the engine does, or a template
// laid out in two columns is designed against a picture of one.
//
// Blocks of a stated height rather than paragraphs: the echo has no font file and
// measures text through a stub here, so text would test the stub. What is under
// test is where the columns are and when one hands over to the next.
{
  const doc = sandbox.blankReport();
  const body = firstDoc(doc).Childs.find(c => c.$type === 'Body');
  body.Childs.length = 0;
  body.Layout = 'Stack';
  body.ColumnCount = 2;
  body.ColumnGap = 8;

  for (let i = 0; i < 30; i++) {
    body.Childs.push({
      $type: 'ContainerItem', Layout: 'Absolute', Childs: [],
      Style: { Location: { HeightValue: 20 }, Margin: { Bottom: 2 } }
    });
  }

  const out = sandbox.layout(doc);
  const blocks = out.placed.filter(p => p.item.$type === 'ContainerItem');
  const lefts = [...new Set(blocks.map(p => Math.round(p.frame.left)))].sort((a, b) => a - b);

  check('the echo divides the page in two', lefts.length === 2, 'x = ' + lefts.join(', '));

  if (lefts.length === 2) {
    // The margins and the gap decide the second column outright, so this is
    // arithmetic rather than a tolerance: the two either agree or they do not.
    const pad = firstDoc(doc).Style.Padding;
    const width = (out.page.width - pad.Left - pad.Right - 8) / 2;
    check('and the second starts a column-width and a gap along',
          Math.abs(lefts[1] - (lefts[0] + width + 8)) <= 0.5,
          lefts[1].toFixed(1) + ' vs ' + (lefts[0] + width + 8).toFixed(1));

    const first = blocks.filter(p => Math.round(p.frame.left) === lefts[0]);
    const second = blocks.filter(p => Math.round(p.frame.left) === lefts[1]);

    check('filling the first column before starting the second',
          second.length > 0 && Math.min(...second.map(p => p.frame.top))
                             < Math.max(...first.map(p => p.frame.bottom)),
          first.length + ' then ' + second.length);

    // Nothing may hang below the sheet: that is the whole difference between
    // columns and a narrower single column.
    check('and nothing runs off the page',
          Math.max(...blocks.map(p => p.frame.bottom)) <= out.page.height + 0.5,
          Math.max(...blocks.map(p => p.frame.bottom)).toFixed(1) + ' mm');
  }

  // Balancing, on a load that fits: the columns should end together rather than the
  // first being full and the last nearly empty. Ten blocks over two columns.
  body.Childs.length = 10;
  const even = sandbox.layout(doc).placed.filter(p => p.item.$type === 'ContainerItem');
  const feet = {};
  even.forEach(p => {
    const x = Math.round(p.frame.left);
    feet[x] = Math.max(feet[x] || 0, p.frame.bottom);
  });

  const bottoms = Object.values(feet);
  check('balanced columns end together', bottoms.length === 2
        && Math.abs(bottoms[0] - bottoms[1]) <= 25,
        bottoms.map(v => v.toFixed(0)).join(' / ') + ' mm');

  body.BalanceColumns = false;
  const lop = sandbox.layout(doc).placed.filter(p => p.item.$type === 'ContainerItem');
  const first = Math.max(...lop.filter(p => Math.round(p.frame.left) === lefts[0])
                             .map(p => p.frame.bottom));
  check('and without it the first column runs on', first > Math.max(...bottoms),
        first.toFixed(0) + ' mm against ' + Math.max(...bottoms).toFixed(0));
}

// --- an image bound to the data -----------------------------------------------
// What ReportBinder.BindImage does on the engine side: a bare base64 string or a
// data: URI, both accepted, and nothing drawn when the path leads nowhere - the
// same rule that leaves an unsigned quote with no signature rather than an empty
// box. Exercised through the very data pane a person would type into, not through
// an exported helper standing in for it.
{
  const doc = sandbox.blankReport();
  const body = firstDoc(doc).Childs.find(c => c.$type === 'Body');
  const img = { $type: 'ImageItem', SourceField: 'Sig', NaturalWidth: 40, NaturalHeight: 20,
                Style: { Location: { LeftValue: 0, TopValue: 0 } } };
  body.Childs.push(img);
  sandbox.hydrate(firstDoc(doc));

  // No data loaded at all: drawn anyway, like any other conditional element - the
  // designer cannot know what a real section would do, so it shows something
  // rather than guessing it away.
  sandbox.setData('');
  check('an unbound image is drawn with no data loaded',
        sandbox.layout(doc).placed.some(p => p.item === img));

  // Data loaded, and something at the path: drawn, and the data: wrapper is
  // stripped the way the binder strips it, leaving the same payload the engine
  // would embed.
  sandbox.setData(JSON.stringify({ Sig: 'data:image/png;base64,QUJD' }));
  check('a bound image with data is drawn',
        sandbox.layout(doc).placed.some(p => p.item === img));

  const resolved = sandbox.resolvedImageSource(img);
  check('the data: wrapper is stripped like the engine strips it',
        !!resolved && resolved.data === 'QUJD' && resolved.mediaType === 'image/png');

  // Data loaded, nothing at the path: gone, not an empty box - matching the
  // engine's Visibility.Gone for an unsigned quote.
  sandbox.setData(JSON.stringify({ Sig: '' }));
  check('an image whose data resolves to nothing is not drawn',
        !sandbox.layout(doc).placed.some(p => p.item === img));

  // Reset, so nothing checked after this block sees stale data.
  sandbox.setData('');
}

// --- the core stands on its own ---------------------------------------------
// Everything above runs the page with a stubbed document, which proves the two
// halves still agree - not that they are actually separable. This runs the core in
// a context with no document, no window and no stub of either: if it lays out,
// draws and validates there, the seam is real rather than tidy.
//
// It is the check that decides whether a second face is cheap or a second copy of
// the engine's echo. That is worth a hard failure rather than a comment.
// Whether the core is separable is a question a SINGLE-FILE build cannot answer, and
// that is not a shortcoming of the build - it is what merging means. The core and the
// page are one script there, and the page uses `document` because it is the page.
//
// So it is said rather than skipped. Written with the block simply gated on `linked`, a
// standalone face ran four fewer checks and reported no failure: 164 against 162, and
// the one block that proves the core touches no DOM quietly absent. Section 16 - a
// harness handed a different shape must check the same things or state plainly that it
// cannot. Made to run against the merged parts instead, it failed honestly and for the
// wrong reason, which is a third way of being unhelpful.
if (!linked.length) {
  console.log('  -  the core is separable: not asked, this face is one file - '
            + 'the core and the page are the same script here');
}

if (linked.length) {
  const context = { console: { log() {}, warn() {}, error() {} } };
  const held = {};
  vm.createContext(context);

  try {
    const handles = `
      held.layout = layout; held.render = render; held.validate = validate;
      held.setTextRuler = setTextRuler; held.blankReport = blankReport;
      held.setLang = setLang; held.setData = setData;
      held.breakLines = breakLines; held.breakAdvancedRuns = breakAdvancedRuns;
      held.textWidth = textWidth; held.BREAKS_AT = BREAKS_AT;
      held.setDefaultFace = setDefaultFace; held.defaultFace = defaultFace;
      held.MAIL_FACE = MAIL_FACE; held.fontsFromServer = fontsFromServer;
    `;
    vm.runInContext(linked.join('\n') + handles, Object.assign(context, { held }));

    check('the core loads with no document and no window',
          typeof held.layout === 'function');

    // Its one declared dependency, handed in rather than reached for.
    held.setTextRuler((text) => String(text).length * 7);

    const alone = held.blankReport();
    alone.Title = 'On its own';
    alone.Language = 'en-GB';

    const laid = held.layout(alone, 0);
    check('and lays a section out there', laid.placed.length > 0,
          `${laid.placed.length} items on ${laid.page.width}x${laid.page.height}`);
    check('and draws it', held.render(alone, null, 0).startsWith('<svg'));
    check('and validates it', Array.isArray(held.validate(alone)));

    // The wording travels with the core, so an issue reads the same either side.
    held.setLang('fr');
    const flat = held.validate(alone, { sound: true, accessible: true, capable: true,
                                        target: { Tagged: false, Interactive: false } });
    check('and words its findings in either language',
          flat.length > 0 && !flat[0].message.startsWith('issue.'),
          flat.map(i => i.rule).join(', '));
    held.setLang('en');

    // Where a line may break, asked of the engine's own list rather than a copy of it:
    // each space LineBreaks.Glue names must hold an amount together in both of the
    // echo's breakers. The plain space has to break inside the same amount at the same
    // width first - without that the window proves nothing (section 10b).
    const glueSource = fs.readFileSync(path.join(REPO, 'engine', 'Text', 'LineBreaks.cs'), 'utf8');
    const glueLine = (glueSource.match(/const string Glue = "([^"]*)"/) || [])[1] || '';
    const glue = [...glueLine.matchAll(/\\u([0-9A-Fa-f]{4})/g)]
      .map(m => String.fromCharCode(parseInt(m[1], 16)));
    check('the engine names the spaces a line may not break at', glue.length > 0, glueLine);

    const wrapFont = {}, wrapReport = held.blankReport();
    const lead = 'Le total est de 12';
    const amount = sep => lead + sep + '480,00 EUR';
    const wrapWidth = (held.textWidth(lead, wrapFont, wrapReport)
                  + held.textWidth(lead + ' 480,00', wrapFont, wrapReport)) / 2;
    const cut = lines => lines.some(l => /12$/.test(l));
    const runLines = text => held.breakAdvancedRuns({ Runs: [{ Text: text }], Font: {} }, wrapWidth, wrapReport)
      .map(line => line.pieces.map(p => p.text).join('').replace(/\s+$/, ''));

    const plainLines = held.breakLines(amount(' '), wrapFont, wrapReport, wrapWidth);
    check('at that width a plain space breaks inside the amount, in both breakers',
          cut(plainLines) && cut(runLines(amount(' '))),
          plainLines.join(' | ') + '  /  ' + runLines(amount(' ')).join(' | '));
    for (const g of glue) {
      const name = 'U+' + g.charCodeAt(0).toString(16).toUpperCase().padStart(4, '0');
      const plain = held.breakLines(amount(g), wrapFont, wrapReport, wrapWidth);
      const runs = runLines(amount(g));
      check(`${name} holds it together, in both breakers`,
            !cut(plain) && !cut(runs) && plain.some(l => l.includes('12' + g + '480'))
              && runs.some(l => l.includes('12' + g + '480')),
            plain.join(' | ') + '  /  ' + runs.join(' | '));
    }

    // And the whole rule, character by character, against where the engine really
    // breaks - written by test 19. The checks above ask only about the spaces the engine
    // names, so a space it stopped naming would go on being held here in silence.
    const breaksFile = path.join(REPO, 'tests', 'output', 'text', 'line-breaks.json');
    if (!fs.existsSync(breaksFile)) {
      check('the echo breaks lines exactly where the engine does', false,
            'no ' + path.relative(REPO, breaksFile) + ' - run dotnet test first');
    } else {
      const engineBreaks = new Set();
      for (const [from, to] of JSON.parse(fs.readFileSync(breaksFile, 'utf8')).breaks)
        for (let cp = from; cp <= to; cp++) engineBreaks.add(cp);
      const disagree = [];
      for (let cp = 0; cp <= 0xFFFF; cp++)
        if (held.BREAKS_AT.test(String.fromCharCode(cp)) !== engineBreaks.has(cp))
          disagree.push('U+' + cp.toString(16).toUpperCase().padStart(4, '0')
                        + (engineBreaks.has(cp) ? ' (the engine breaks)' : ' (the echo breaks)'));
      check('the echo breaks lines exactly where the engine does, over all 65,536',
            disagree.length === 0 && engineBreaks.size > 0,
            disagree.length ? disagree.slice(0, 6).join(', ') : engineBreaks.size + ' characters break');
    }

    // Text that names no family. The stack a mail reader is handed is the engine's,
    // read out of FlowingHtml rather than trusted; and the face the page declares is
    // what both the drawing and the measuring use - a page that measured in one face
    // and drew in another would wrap every line wrongly.
    const flowing = fs.readFileSync(path.join(REPO, 'engine', 'Html', 'FlowingHtml.cs'), 'utf8');
    const engineMailFace = (flowing.match(/IsNullOrWhiteSpace\(family\)\s*\?\s*"([^"]+)"/) || [])[1];
    check('the face mail text names no family in is the engine\'s, not a copy of it',
          engineMailFace && engineMailFace === held.MAIL_FACE,
          `engine ${engineMailFace || 'not found'} / echo ${held.MAIL_FACE}`);

    const measured = [];
    held.setTextRuler((text, font) => { measured.push(font); return String(text).length * 7; });
    held.setDefaultFace("'Probe Face', sans-serif");
    const unnamed = held.blankReport();
    unnamed.Language = 'en-GB';
    const unnamedBody = unnamed.Sections[0].Childs.find(c => c.$type === 'Body');
    unnamedBody.Childs.push({ $type: 'TextItem', Text: 'Nothing names a family here' });
    unnamed.Watermark = { Text: 'NOR HERE' };
    const drawn = held.render(unnamed, null, 0);
    // Two writers of text, each asked: the paragraph and the watermark. Asked as one,
    // either could fall back to sans-serif while the other carried the check.
    const probed = `font-family="'Probe Face', sans-serif"`;
    const textTag = (drawn.match(/<text[^>]*>Nothing names a family here/) || [''])[0];
    const markTag = (drawn.match(/<text[^>]*>NOR HERE/) || [''])[0];
    check('text naming no family is drawn in the face the page declares',
          textTag.includes(probed), (textTag.match(/font-family="[^"]*"/) || ['no text drawn'])[0]);
    check('and so is a watermark naming none',
          markTag.includes(probed), (markTag.match(/font-family="[^"]*"/) || ['no watermark drawn'])[0]);
    check('and measured in it too',
          measured.some(f => f.endsWith("'Probe Face', sans-serif")),
          measured.slice(0, 2).join(' | ') || 'nothing measured');
    held.setDefaultFace(null);
    held.setTextRuler((text) => String(text).length * 7);

    // And the server's list read the way the server writes it. The route and its three
    // parameters are read out of the server, so a rename on either side fails here
    // rather than in a browser that quietly draws with a fallback.
    const server = fs.readFileSync(path.join(REPO, 'apps', 'server', 'Program.cs'), 'utf8');
    const route = server.match(/MapGet\("(\/fonts\/file)",\s*\(string (\w+), bool (\w+), bool (\w+)\)/);
    const marks = /IsDefault\s*=/.test(server);
    const got = held.fontsFromServer([
      { family: 'House Face', subfamily: 'Regular', bold: false, italic: false, isDefault: true },
      { family: 'House Face', subfamily: 'Bold Italic', bold: true, italic: true, isDefault: true },
      { family: 'Other Face', subfamily: 'Regular', bold: false, italic: false, isDefault: false },
    ], 'http://server');
    const boldItalic = got.faces.find(f => f.weight === '700' && f.style === 'italic');
    check('the server marks its default family, and the page reads which one it is',
          marks && got.defaultFamily === 'House Face', `${marks ? 'marked' : 'not marked'}, read ${got.defaultFamily}`);
    check('and asks for each cut at the address the server hands it out from',
          route && boldItalic && boldItalic.url ===
            `http://server${route[1]}?${route[2]}=House%20Face&${route[3]}=true&${route[4]}=true`,
          boldItalic ? boldItalic.url : 'no bold italic face');
  } catch (e) {
    check('the core loads with no document and no window', false, e.message);
  }
}

// --- a sub-template the template carries -------------------------------------
// The engine splices three shapes and this page can only splice one of them - the
// carried bytes - because a path needs a file it does not have and a st:// reference
// needs a program it is not. The panel says which is which; what this checks is that
// the one it CAN do, it does, and that the file keeps the bytes rather than the
// expansion.
{
  const inner = {
    Sections: [{ Childs: [{ $type: 'Body', Childs: [
      { $type: 'TextItem', Text: 'spliced into the preview' }
    ] }] }]
  };

  const holder = sandbox.blankReport();
  const body = holder.Sections[0].Childs.find(c => c.$type === 'Body');
  body.Childs.push({
    $type: 'SubTemplateItem', Layout: 'Stack', TemplatePath: '', Childs: [],
    TemplateData: Buffer.from(JSON.stringify(inner), 'utf8').toString('base64')
  });

  const drawn = sandbox.render(holder, null, 0);
  check('the preview splices a sub-template the template carries',
        drawn.includes('spliced into the preview'),
        drawn.includes('spliced into the preview') ? 'the words are on the page'
        : drawn.includes('<text') ? 'something was drawn, but not that' : 'nothing drawn at all');

  // Twice, because layout runs on every keystroke: appending rather than replacing
  // would put the other template in again on each one.
  sandbox.render(holder, null, 0);
  const twice = sandbox.render(holder, null, 0);
  const times = (twice.match(/spliced into the preview/g) || []).length;
  check('and once however many times it is drawn', times === 1, `${times} copies`);

  // The file keeps the bytes and not the expansion, or editing the other template
  // would leave this one showing, confidently, what it used to say.
  const slot = sandbox.forEngine(holder).Sections[0].Childs
    .find(c => c.$type === 'Body').Childs.find(c => c.$type === 'SubTemplateItem');
  check('what is saved carries the bytes',
        (slot.TemplateData || '').length > 0, String((slot.TemplateData || '').length));
  check('and not the children they expand to',
        (slot.Childs || []).length === 0, `${(slot.Childs || []).length} children`);

  // A path is NOT spliced here, and that is the line the engine draws too.
  const pathed = sandbox.blankReport();
  pathed.Sections[0].Childs.find(c => c.$type === 'Body').Childs.push({
    $type: 'SubTemplateItem', Layout: 'Stack', TemplatePath: 'fragment.json', Childs: []
  });
  check('a sub-template naming a path stays empty here',
        !sandbox.render(pathed, null, 0).includes('spliced'),
        'the page has no file to resolve it against');

  // And the same bytes in the other mode draw nothing at all, which is not a gap in
  // this preview but the mode's own meaning: Append brings a DOCUMENT, whose sections
  // land after this one, and this canvas shows one section. Drawing its body into the
  // flow would show a page that will never exist.
  //
  // Confronted rather than merely asserted: the engine removes the item and puts the
  // piece in Report.Sections, so the two agree that nothing of it belongs in this
  // section's flow. The check that matters is the pair - splice draws it, append does
  // not - because "append draws nothing" is also true of a preview that has quietly
  // stopped splicing anything at all.
  const joined = sandbox.blankReport();
  joined.Sections[0].Childs.find(c => c.$type === 'Body').Childs.push({
    $type: 'SubTemplateItem', Layout: 'Stack', Mode: 'Append', TemplatePath: '', Childs: [],
    TemplateData: Buffer.from(JSON.stringify(inner), 'utf8').toString('base64')
  });

  const asDocument = sandbox.render(joined, null, 0);
  check('the same bytes appended draw nothing in this section',
        !asDocument.includes('spliced into the preview'),
        'a document is not a block, and this canvas shows one section');

  // The pair, stated: one mode draws it and the other does not.
  check('and the two modes really differ',
        drawn.includes('spliced into the preview')
        && !asDocument.includes('spliced into the preview'),
        'splice draws it, append does not');

  // What is saved still carries the bytes: an appended piece is resolved by whoever
  // has a file system, exactly as a spliced one is.
  const kept = sandbox.forEngine(joined).Sections[0].Childs
    .find(c => c.$type === 'Body').Childs.find(c => c.$type === 'SubTemplateItem');
  check('and an appended sub-template is saved with its bytes and its mode',
        (kept.TemplateData || '').length > 0 && kept.Mode === 'Append',
        `${(kept.TemplateData || '').length} bytes, mode ${kept.Mode}`);
}

// --- every colour has a name, and the paper keeps its own --------------------
// Two families, and the line between them is the whole point. The chrome follows
// the theme - and one day the theme of whatever page embeds this - so each of its
// colours is a custom property a host can set. The paper does not follow anything:
// the canvas shows a printed page, and a mark that changed with the interface would
// make this tool disagree with the printer.
//
// Neither survives being a discipline. Before this check there were 40 distinct
// literals, one grey doing two jobs (#14161a was both a panel and the desk) and two
// greys doing one (#8b939e beside #8b949e, a digit apart, nobody's decision). So the
// rule is mechanical: a hex or rgba() lives in the palette or in ON_PAPER, nowhere
// else. Adding a control with a colour of its own now fails here rather than in six
// months, half-themed.
if (styles !== null) {
  const coloured = line => /#[0-9a-fA-F]{3,8}|rgba?\(/.test(line);

  const lines = text => text.split(/\r?\n/).map(l => l.trim());

  // In the stylesheet a colour may only appear where a custom property is declared.
  const strayCss = lines(styles).filter(l => coloured(l) && !l.startsWith('--'));
  check('no colour in the stylesheet outside the palette', strayCss.length === 0,
        strayCss.slice(0, 3).join(' | ')
        || `${lines(styles).filter(coloured).length} declarations`);

  // And a name used is a name declared - drawn round the paper family, which is the
  // one where a typo is invisible: a var(--on-paper-...) that resolves to nothing
  // inherits instead, and inheriting on white paper looks very nearly right. The rule
  // stops at this family on purpose, because --sheet-pad and --sheet-width are set
  // from JS and carry their own fallback: widening it to every custom property would
  // mean exempting those, and an exemption is where the next hole goes.
  const paperDeclared = new Set([...styles.matchAll(/(--on-paper[a-z-]*)\s*:/g)].map(m => m[1]));
  const paperUsed = new Set([...styles.matchAll(/var\((--on-paper[a-z-]*)/g)].map(m => m[1]));
  const undeclared = [...paperUsed].filter(n => !paperDeclared.has(n));
  check('every paper colour a sheet uses is one the palette declares',
        undeclared.length === 0,
        undeclared.join(', ') || `${paperUsed.size} used, ${paperDeclared.size} declared`);

  // In the script, only inside ON_PAPER.
  const block = script.match(/const ON_PAPER = \{[\s\S]*?^\};/m);
  check('the script names its paper colours in one place', !!block);
  const strayJs = lines(script.replace(block ? block[0] : '', ''))
    .filter(l => /#[0-9a-fA-F]{3,8}/.test(l));
  check('and writes none anywhere else', strayJs.length === 0,
        strayJs.slice(0, 3).join(' | '));

  // Every theme must set the same names, or switching leaves a colour behind at
  // whatever the last one made it - the failure that looks like a rendering bug.
  //
  // Every theme, found rather than named: the audit compared dark against light while
  // a third had been added beside them, and passed. A check that keeps its own list of
  // what to check is one addition away from checking less than it says.
  //
  // A rule of its own, not one name in a group. The paper is declared for every theme
  // at once, so a selector list ending in [data-theme="frost"] read as a fourth theme
  // that sets one property and nothing else - and the audit duly reported the other
  // twenty-four missing from it. Anchored to the start of a rule.
  // The comma is what tells them apart: the paper and the corner radius are declared
  // for every theme at once, and that selector list ends in [data-theme="frost"], so
  // it read as a fourth theme setting two properties and nothing else - the audit duly
  // reported the other twenty-four missing from it. A theme's own rule is the one whose
  // selector does not continue a list.
  // A theme's rule now names :host beside :root - the palette has to reach a shadow
  // root, where :root matches nothing - so the selector may carry one sibling before
  // its brace. What the check MEANS is untouched: it still finds a theme by its own
  // rule and still refuses a continuation line. Sabotaged after the change, because a
  // check edited in the same pass as its subject stops checking anything (section 16).
  const themes = [...styles.matchAll(
      /(?<!,)\n[ \t]*:root\[data-theme="([a-z]+)"\](?:[ \t]*,[ \t]*:host\([^)]*\))?[ \t]*\{([\s\S]*?)\n[ \t]*\}/g)]
    .map(m => ({ name: m[1], body: m[2] }));

  check('the themes are found rather than named', themes.length >= 2,
        themes.map(t => t.name).join(', '));

  const namesIn = body => new Set([...body.matchAll(/(--[a-z-]+)\s*:/g)].map(m => m[1]));

  if (themes.length >= 2) {
    const first = namesIn(themes[0].body);
    for (const theme of themes.slice(1)) {
      const mine = namesIn(theme.body);
      const only = (a, b) => [...a].filter(x => !b.has(x));
      check(`${theme.name} sets the same names as ${themes[0].name}`,
            only(first, mine).length === 0 && only(mine, first).length === 0,
            [...only(first, mine).map(x => x + ` missing from ${theme.name}`),
             ...only(mine, first).map(x => x + ` only in ${theme.name}`)].join(', ')
            || `${mine.size} each`);
    }
  }

  // THE LIST OF ROOMS, THE STYLESHEET AND THE TWO DICTIONARIES.
  //
  // Three hand-kept lists that nothing confronted: the options a face offers, the
  // `[data-theme]` rules that paint them, and the wordings that name them. Every
  // disagreement was silent, and the worst is the commonest - an option whose rule has
  // gone stamps an attribute nothing matches, so the page falls through to the base
  // palette and the picker says one room while showing another.
  //
  // The options are no longer a list at all: both faces build their select from THEMES,
  // which is why only three things meet here rather than five. That is the fix; this is
  // what says the fix holds.
  //
  // `auto` is exempt from the stylesheet half BY VALUE, not by position: it is the
  // machine's own answer and deliberately has no rule of its own. Exempting "the first
  // option" instead would quietly excuse whatever happened to be first.
  {
    const offered = (sandbox.THEMES || []).map(t => t.name);
    check('the rooms are a list the core keeps', offered.length >= 2, offered.join(', '));

    const painted = new Set(themes.map(t => t.name));
    const needsRule = offered.filter(n => n !== 'auto');

    const unpainted = needsRule.filter(n => !painted.has(n));
    check('every room offered has a rule to paint it', unpainted.length === 0,
          unpainted.map(n => n + ' is offered and painted nowhere').join(', ')
          || needsRule.join(', '));

    // And the other way round, which is the half a cull gets wrong: a rule left behind
    // for a room nobody can pick is dead weight that reads as a living theme.
    const unreachable = [...painted].filter(n => !offered.includes(n));
    check('and every rule paints a room somebody can pick', unreachable.length === 0,
          unreachable.map(n => n + ' is painted and offered nowhere').join(', ')
          || `${painted.size} rules`);

    // Both languages, one at a time. Counting the keys into a set would let a room
    // worded in either language alone pass, because the two collapse to one entry -
    // section 5's own lesson, and the reason t() falling back to English makes a
    // missing French wording unaskable through the interface.
    for (const lang of Object.keys(sandbox.STRINGS)) {
      const missing = offered.filter(n => !(`theme.${n}` in sandbox.STRINGS[lang]));
      check(`and ${lang} words every one of them`, missing.length === 0,
            missing.map(n => 'theme.' + n).join(', ') || `${offered.length} rooms`);
    }

    // And a name that is no longer a room. This one is not about a list at all - it is
    // about somebody who chose a room before it was taken away. The preference lives
    // under one localStorage key shared by both faces and used to be handed straight to
    // setAttribute: after a cull that is an attribute nothing matches, so the base
    // palette shows in silence, and the picker is left blank because assigning a value
    // with no matching option leaves a real select at selectedIndex -1. The dead name
    // then sat in the variable for the rest of the session.
    //
    // Asked OF THE FUNCTION rather than read off the source. The first writing of these
    // checks confronted three lists and left this uncovered: replacing the guard with
    // `name => name || "auto"` changed no count and no line of the run.
    const stale = sandbox.themeOrAuto;
    check('a stored room that no longer exists falls back to auto',
          stale('slate') === 'auto' && stale('') === 'auto' && stale(null) === 'auto',
          `slate -> ${stale('slate')}, empty -> ${stale('')}, null -> ${stale(null)}`);

    check('and one that does exist is kept unchanged',
          offered.every(n => stale(n) === n), offered.join(', '));
  }

  // THE DEFAULT ROOM IS ONE OF THE NAMED ONES.
  //
  // Everything above finds a theme by its `[data-theme="..."]` selector, so the unnamed
  // `:root, :host` block is matched by none of it - and that block is what a face falls
  // back to, which is to say what `auto` shows on a dark machine. It declares the same
  // twenty-four names and was compared with nothing.
  //
  // It got away with it for as long as it was the byte-for-byte twin of the explicit
  // `dark`, which IS audited: the thing covering it was another block, not a check. The
  // day that twin is deleted - or the day somebody edits one of the two and not the
  // other - the default room drifts in silence, and it is the room most people are in.
  //
  // Told no name. It asks that the default be one of the rooms the selector can reach,
  // and prints which, so a cull cannot make it stale and a mistyped digit matches
  // nothing at all.
  //
  // Found by indentation: this file writes top-level rules at two spaces and the
  // prefers-color-scheme twin at four, so `\n  :root, :host {` is the base and nothing
  // else. The --chrome-ink block wears the same selector and is excluded by carrying no
  // color-scheme; the paper block by its selector list running on past `:host`.
  const bases = [...styles.matchAll(/\n  :root,\s*:host\s*\{([\s\S]*?)\n  \}/g)]
    .map(m => m[1]).filter(b => /color-scheme/.test(b));

  check('the default room is one block, found not named', bases.length === 1,
        `${bases.length} candidate(s)`);

  if (bases.length === 1 && themes.length >= 1) {
    const pairsOf = body => new Map([...body.matchAll(/(--[a-z-]+)\s*:\s*([^;]+);/g)]
                                    .map(m => [m[1], m[2].trim().replace(/\s+/g, ' ')]));
    const base = pairsOf(bases[0]);

    // The names first, against the same reference every other theme is held to. A name
    // missing HERE is the worst of the family: every named theme goes on agreeing with
    // every other one, and the block they all fall back to is the one short.
    const want = namesIn(themes[0].body), got = new Set(base.keys());
    const missing = [...want].filter(n => !got.has(n));
    const extra = [...got].filter(n => !want.has(n));
    check('the default room declares the same names as a named one',
          missing.length === 0 && extra.length === 0,
          [...missing.map(n => n + ' missing'), ...extra.map(n => n + ' only in the default')]
            .join(', ') || `${got.size} names`);

    // And the values, against every named theme in turn: exactly one must match whole.
    // Anything else means the default is a room nobody designed - which is precisely
    // what it would become if a transplant dropped or mistyped one value.
    const same = themes.filter(t => {
      const p = pairsOf(t.body);
      return p.size === base.size && [...p.keys()].every(k => p.get(k) === base.get(k));
    });

    check('and its values are exactly one of them', same.length === 1,
          same.length ? same.map(t => t.name).join(', ')
                      : 'matches none: ' + themes.map(t => {
                          const p = pairsOf(t.body);
                          const d = [...base.keys()].filter(k => p.get(k) !== base.get(k));
                          return `${t.name} differs on ${d.length}`;
                        }).join(', '));
  }

  // The light values are written twice - once for the system preference, once for the
  // explicit choice, because a preference has to be able to disagree with the machine.
  // Two copies drift, so they are compared name for name and value for value, or
  // picking "Light" on a dark machine gives a third theme nobody designed.
  const media = styles.match(/@media \(prefers-color-scheme: light\)\s*\{\s*:root(?:\s*,\s*:host)?\s*\{([\s\S]*?)\n    \}/);
  const light = themes.find(t => t.name === 'light');
  check('the system-light values exist', !!media);
  if (media && light) {
    const pairs = body => new Map([...body.matchAll(/(--[a-z-]+)\s*:\s*([^;]+);/g)]
                                  .map(m => [m[1], m[2].trim().replace(/\s+/g, ' ')]));
    const m = pairs(media[1]), e = pairs(light.body);
    const differ = [...e.keys()].filter(k => m.get(k) !== e.get(k));
    check('and match the explicit light theme exactly', differ.length === 0,
          differ.map(k => `${k}: ${m.get(k) ?? 'absent'} vs ${e.get(k)}`).slice(0, 3).join(' | ')
          || `${e.size} names`);
  }

  // The paper is one value in both, which is what "does not follow the theme" means
  // once it is a rule rather than a comment.
  const papers = [...styles.matchAll(/--paper\s*:\s*([^;]+);/g)].map(m => m[1].trim());
  check('the paper is one colour in every theme',
        papers.length > 0 && new Set(papers).size === 1, papers.join(', '));
}

// --- the preview shows the section it was asked for --------------------------
// It composed Sections[0] whatever was open, so opening the second section drew the
// first one's header on the second one's page - and the tree beside it listed the
// second one's children. Each half looked right; only the two together were wrong,
// which is why it survived being looked at.
{
  const two = {
    Title: 'Two sections', RootFontSize: 3, DefaultFont: {},
    Sections: [
      { $type: 'Section', Layout: 'Absolute', Dimension: { Width: 210, Height: 297 }, Childs: [
        { $type: 'Header', Layout: 'Stack', Style: { Location: { Height: 20 } }, Childs: [
          { $type: 'TextItem', Text: 'first', Style: { Location: {} } }] }] },
      { $type: 'Section', Layout: 'Absolute', Dimension: { Width: 297, Height: 210 }, Childs: [
        { $type: 'Body', Layout: 'Stack', Style: { Location: {} }, Childs: [
          { $type: 'TextItem', Text: 'second', Style: { Location: {} } }] }] }
    ]
  };

  const textsOf = r => r.placed.filter(p => p.item.$type === 'TextItem').map(p => p.item.Text);

  const first = sandbox.layout(two, 0);
  check('the first section is laid out on its own sheet',
        near(first.page.width, 210) && near(first.page.height, 297),
        `${first.page.width}x${first.page.height}`);
  check('and holds its own text', textsOf(first).join() === 'first', textsOf(first).join());

  const second = sandbox.layout(two, 1);
  check('the second is laid out on the sheet it declares',
        near(second.page.width, 297) && near(second.page.height, 210),
        `${second.page.width}x${second.page.height}`);
  check('and holds its own text, not the first section\'s',
        textsOf(second).join() === 'second', textsOf(second).join());

  // The bands are drawn from what layout returned rather than from what the tree has
  // selected, so the green header band cannot belong to another section.
  check('and names the section it composed', second.section === two.Sections[1],
        second.section === two.Sections[0] ? 'the first one' : 'ok');

  // Asked for nothing, it composes the first - which is what every caller meant back
  // when a report had one section, the harness included.
  check('with no section named it is the first',
        textsOf(sandbox.layout(two)).join() === 'first');

  // A size on every section, not only on the first: one that lays out on nothing puts
  // one item per page and drops the rest, silently.
  const sized = { Sections: [{ $type: 'Section', Childs: [] }, { $type: 'Section', Childs: [] }] };
  sandbox.layout(sized, 1);
  check('and every section is given a sheet', sized.Sections[1].Dimension
        && sized.Sections[1].Dimension.Width > 0,
        JSON.stringify(sized.Sections[1].Dimension));
}

// --- the two validators must agree ------------------------------------------
// The engine wrote a template built to be wrong, and what it found in it. The
// echo reads the very same file: same faults, same count, same severities, or
// the designer clears a section that will be refused three steps later.
if (faultyPath && findingsPath) {
  const faulty = JSON.parse(fs.readFileSync(faultyPath, 'utf8'));
  firstDoc(faulty).$type = 'Section';
  sandbox.hydrate(firstDoc(faulty));

  const expected = JSON.parse(fs.readFileSync(findingsPath, 'utf8'));
  const mine = sandbox.validate(faulty);

  const tally = list => {
    const counts = {};
    for (const i of list) {
      const key = i.rule + '/' + i.level;
      counts[key] = (counts[key] || 0) + 1;
    }
    return counts;
  };
  const theirs = tally(expected), ours = tally(mine);
  const rules = new Set([...Object.keys(theirs), ...Object.keys(ours)]);

  check('the same number of faults', mine.length === expected.length,
        `${mine.length} here, ${expected.length} in the engine`);

  for (const rule of [...rules].sort())
    check(`${rule} agrees`, theirs[rule] === ours[rule],
          `${ours[rule] || 0} here, ${theirs[rule] || 0} in the engine`);

  // Each finding about an element must carry it: one that points nowhere cannot
  // light up a node, and the panel would list a fault nobody can reach.
  const aboutItems = mine.filter(i => i.rule !== 'ua.title' && i.rule !== 'ua.language');
  check('and each one names its element', aboutItems.every(i => i.item),
        aboutItems.filter(i => i.item).length + ' of ' + aboutItems.length);
}

// --- and they must agree about the output, not only about the template -------
// The same confrontation for the third profile, on a template that is faultless:
// everything found here is something the chosen output drops. The echo has to be
// told what is being produced, exactly as the engine is, or it stays silent - and a
// silent check that looks like agreement is the worst outcome of the three.
if (flatPath && flatFindingsPath) {
  const flat = JSON.parse(fs.readFileSync(flatPath, 'utf8'));
  firstDoc(flat).$type = 'Section';
  sandbox.hydrate(firstDoc(flat));

  // One case per target, and the target comes out of the file rather than being
  // written again here. A copy of it in this harness would let the engine change the
  // question while the two went on agreeing about the answers - section 16, and the
  // reason the file carries the question as well as the answer.
  const cases = JSON.parse(fs.readFileSync(flatFindingsPath, 'utf8'));

  const tally = list => {
    const counts = {};
    for (const i of list) counts[i.rule + '/' + i.level] = (counts[i.rule + '/' + i.level] || 0) + 1;
    return counts;
  };

  // Every case's findings, not the last one's. Written with a single `mine` that
  // each turn overwrote, the wording check below silently narrowed to whichever
  // target happened to be last - so can.untagged, which only the first case reaches,
  // stopped being checked at all the moment a second case was added.
  const mine = [];
  for (const { target, issues: expected } of cases) {
    const how = [target.Tagged ? 'tagged' : 'untagged',
                 target.Interactive ? 'fillable' : 'flattened'].join(' ');
    const here = sandbox.validate(flat, { sound: true, accessible: true, capable: true, target });
    mine.push(...here);

    const theirs = tally(expected), ours = tally(here);

    check(`${how}: the same number of losses`, here.length === expected.length,
          `${here.length} here, ${expected.length} in the engine`);

    for (const rule of [...new Set([...Object.keys(theirs), ...Object.keys(ours)])].sort())
      check(`${how}: ${rule} agrees`, theirs[rule] === ours[rule],
            `${ours[rule] || 0} here, ${theirs[rule] || 0} in the engine`);

  }

  // A file with no case in it would pass every loop above without running one.
  check('and there was something to confront', cases.length > 1, cases.length + ' target(s)');

  // Told nothing about the output, both say nothing: the rules are about a loss, and
  // with no output named there is none to speak of.
  const quiet = sandbox.validate(flat, { sound: true, accessible: true, capable: true });
  check('and both keep quiet with no output named',
        !quiet.some(i => i.rule.startsWith('can.')), quiet.length + ' other finding(s)');

  // --- and the second section is read exactly like the first --------------------
  // The designer hydrates the section being edited; validate() walks them all. So in
  // any section but the open one every default was missing, and a default missing is
  // not the same as a default: `f.Signing === "ByHand"` is false for a signature whose
  // Signing was never written, and ByHand is the zero of that enum, so the engine
  // never writes it.
  //
  // What that produced is the worst shape a panel can take: two invented Errors
  // against a field that loses nothing, and silence about the one real PDF/UA fault
  // that same field had. The template it accuses is right.
  //
  // Asked by doubling the file rather than by carrying a two-section fixture: what has
  // to hold is that a section is read the same wherever it sits, and a copy of a
  // section says that most sharply - every count must exactly double, and a rule that
  // reads a default off an item nobody hydrated will not.
  {
    const asked = { sound: true, accessible: true, capable: true,
                    target: cases[cases.length - 1].target };

    const fresh = () => {
      const copy = JSON.parse(fs.readFileSync(flatPath, 'utf8'));
      copy.Sections[0].$type = 'Section';
      return copy;
    };

    // The page's own sequence, and it is the whole point: refresh() hydrates the
    // section being EDITED and then validates the report. Hydrating neither would be
    // symmetric, and symmetric is exactly what the fault is not - written that way
    // first, this check stayed green with the fix taken out, both sections being read
    // wrongly in the same way.
    const one = fresh();
    sandbox.hydrate(one.Sections[0]);

    const two = fresh();
    two.Sections.push(JSON.parse(JSON.stringify(two.Sections[0])));
    sandbox.hydrate(two.Sections[0]);

    const once = tally(sandbox.validate(one, asked));
    const doubled = tally(sandbox.validate(two, asked));

    // Two shapes are right and everything else is the fault. A rule about an item
    // fires once per item, so it doubles; one about the document - the output is
    // flattened, the file is untagged - fires once for the report however many
    // sections it has, so it stays put. Which is which is not written down here: a
    // harness keeping its own list of that is one rule away from checking less than
    // it says, and the two legal answers can be stated without the list.
    const off = [...new Set([...Object.keys(once), ...Object.keys(doubled)])]
      .filter(rule => (doubled[rule] || 0) !== (once[rule] || 0)
                   && (doubled[rule] || 0) !== (once[rule] || 0) * 2)
      .map(rule => `${rule}: ${doubled[rule] || 0} for two sections, ${once[rule] || 0} for one`);

    // And at least one that really did double, or every rule here is about the
    // document and the second section was never read at all.
    const perItem = Object.keys(once).filter(rule => (doubled[rule] || 0) === once[rule] * 2);

    check('a doubled file has findings that double', perItem.length > 0, perItem.join(', '));
    check('and the second section is read exactly like the first', off.length === 0,
          off.join('; ') || 'every count either doubled or is about the file');
  }

  // Every message resolves; a rule added on one side without its wording shows the
  // reader its own key.
  check('every loss is worded, in every case asked',
        mine.every(i => i.message && !i.message.startsWith('issue.')),
        [...new Set(mine.map(i => i.rule))].join(', '));
}

// --------------------------------------------- the tree roots at the report
// The report's fields used to sit under the section's heading, so changing the title
// while a section was selected looked as though it should change only that section.
// The behaviour was right and the panel was not: Report.Title is the PDF's /Title, and
// there is one per file.
//
// The move is structural rather than a better heading. A properties panel shows what
// is selected, and an honest label over somebody else's fields only names the category
// error. So the report is a node of its own, and its fields are reached by choosing it.
{
  const rep = sandbox.blankReport();
  rep.Title = 'A title on the file';
  rep.Sections.push({ $type: 'Section', Style: {}, Layout: 'Absolute', Childs: [],
                      Dimension: { Width: 297, Height: 210 } });
  sandbox.setReport(rep);

  const words = who => {
    sandbox.setSelected(who);
    sandbox.ui.props.children.length = 0;
    sandbox.drawProps();
    return wordsIn(sandbox.ui.props);
  };

  const onReport = words(rep);
  const onSection = words(sandbox.doc0());

  check('the report has a panel of its own', /title/i.test(onReport), onReport.slice(0, 50));
  check('and it is where the file-wide fields live',
        /author/i.test(onReport) && /language/i.test(onReport));

  // The half that was actually wrong: a section showing the title is what made
  // changing it look local.
  check('a section no longer shows the file-wide fields',
        !/author/i.test(onSection) && !/language/i.test(onSection),
        onSection.slice(0, 60));
  check('while it keeps what is genuinely its own', /page/i.test(onSection));

  // A report is not an item on a page: no position, no role, nothing to delete.
  check('and the report offers no position or role',
        !/role/i.test(onReport) && !/left/i.test(onReport));

  // Sections are drawn in the tree now, which would make them draggable and
  // deletable if the existing guards did not already answer. Asked, not assumed.
  check('a section has no parent to be moved out of',
        rep.Sections.every(sec => sandbox.parentOf(sec) === null));

  sandbox.setSectionIndex(1);
  check('choosing a section is how one is opened', sandbox.doc0() === rep.Sections[1],
        rep.Sections.length + ' sections');
  sandbox.setSectionIndex(0);

  const tree = sandbox.reportNode();
  check('and the tree shows the report with a child per section',
        wordsIn(tree).includes('A title on the file'), wordsIn(tree).slice(0, 60));
}

// A COUNT, because neither number was ever printed.
//
// This harness said `all good` and nothing else, so a run that quietly asked four fewer
// questions was typographically identical to one that asked them - which is the whole
// complaint this project makes about a check that stops checking. It is not
// hypothetical here: the palette audit compares every theme against the first it finds,
// so culling the themes drops the emitted checks from six to two, and without a total
// that reads exactly like six passes.
//
// The number is not asserted against anything. A harness holding an expected count is
// one addition away from failing for the wrong reason (section 16); what is wanted is
// that a reader diffing two runs can SEE the difference, which a printed total gives
// and a silent one cannot.
console.log(failures ? `\n${checked} checks, ${failures} FAILED`
                     : `\n${checked} checks, all good`);
process.exit(failures ? 1 : 0);
