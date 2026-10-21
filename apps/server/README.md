# The preview server

Serves the real engine so the designer can ask it for the truth, and hands out the
PDF. It is not required to design a template - the designer works on its own.

## Running it

From the repository root:

```
dotnet run --project apps/server
Two faces are served, and everything they load beside them:

| address | what it is |
|---|---|
| `/` | the designer - a template at coordinates |
| `/editor` | the flow editor - a document typed in the flow |
| `/preview` `/pdf` | the pages, laid out by the engine itself |
| `/html` | those pages as one file, SVG with the fonts embedded |
| `/mail` | the same report as a message: HTML that reflows, no pages |
| `/fonts` | what the engine can actually embed, which is what a face may offer |

The whole directory is served, not one file. The designer was a single page once and
reading it off disk was enough; it now loads `frisket-core.js`, `frisket-runs.js` and
`frisket-chrome.css` beside itself, and a server answering only for the HTML hands the
browser a page whose every other request is a 404.```

It prints where it is listening, and that line is the one to trust:

```
Now listening on: http://localhost:5080
```

Then open **http://localhost:5080** - the server hands out the designer itself, so
one address serves the lot.

Pressing F5 in Visual Studio does the same thing: `Properties/launchSettings.json`
pins the same port, so the IDE and the command line cannot disagree.

## If it does not answer

The port is **5080**, not the 5000 ASP.NET usually defaults to. Check the line the
server printed rather than assuming.

**The firewall is not the culprit.** Windows never filters loopback traffic, so
nothing on `localhost` is ever blocked. A firewall prompt only appears if you change
the address to `0.0.0.0` or a machine name to reach the server from another device.

What it usually is:

- **Something else already on 5080.** The server then fails to start and says so.
  `Get-NetTCPConnection -State Listen | Where-Object LocalPort -eq 5080` names the
  process. A previous run left behind is the common case.
- **Run from the wrong folder.** `--project server` is relative to where you are.
- **A stale build.** `dotnet build` first if the engine changed.

## What it exposes

| | |
|---|---|
| `GET /` | the designer |
| `GET /fonts` | what the font library holds |
| `POST /preview` | template + data → the SVG pages, laid out for real |
| `POST /pdf` | the same, through the PDF backend |
| `POST /check` | template (+ the host's `fields`) → the findings, each with a JSON Pointer - for a model writing templates, see `examples/ai/README.md` |

```json
{
  "template": "<the template JSON>",
  "data": "<the data JSON, or null>",
  "showBands": false,
  "numbers": "french",
  "archivable": false
}
```

A template that will not lay out comes back as a 400 carrying the engine's own
words. That is what the designer shows.

CORS is wide open on purpose: the designer is often opened straight off the disk,
which sends `Origin: null`. Tighten it before putting this anywhere but a
workstation - it is a design tool, not a public endpoint.
