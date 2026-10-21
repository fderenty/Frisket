# Frisket

A document engine for .NET 10 with no third-party dependencies. A template is a JSON file,
the data is another, and the engine lays the two out into a PDF or an SVG proof - writing its
own PDF, measuring its own type and encoding its own barcodes.

```csharp
using Frisket;

Report template = Report.FromFile("invoice.json");
Report report = ReportBinder.Bind(template, File.ReadAllText("invoice.data.json"));

var pdf = new PdfBackend(new FontLibrary());
ReportRenderer.Render(report, pdf);
File.WriteAllBytes("invoice.pdf", pdf.ToArray());
```

Given the same template, data and date, the output is the same file byte for byte.

## What it does

- **Layout** - flowing or pinned to coordinates, headers and footers, tables whose rows
  split across pages, columns that balance, justified text, sections with a page size of
  their own.
- **PDF** - PDF/A-3B and PDF/UA-1 (`Conformance = PdfConformance.PdfA3b`, `Tagged = true`),
  both validated with veraPDF; Factur-X attachments; fillable forms, and signature fields
  a signing platform can sign.
- **Type** - cap heights measured from the glyph outline rather than taken from the font's
  own claims, so 3 mm capitals are 3 mm in every typeface; fonts are subset into the file.
- **Codes** - QR and Code 128, generated here.
- **Checks** - `TemplateValidator.Check(report)` says what will go wrong before any
  document exists: a figure with no alternative text, two fields sharing a name, a table
  row one cell short. Told what is about to be produced, it also says what that output
  will lose - a form flattened into a picture of one, a role dropped from untagged output.

## Templates written by a program

A template can come from a program or a language model as well as from a person. The JSON
Schema in the repository describes every type and property, and the checks say what is wrong
and where, so a program can correct the template and check it again:

```csharp
var issues = new List<TemplateIssue>(TemplateJson.Check(json));  // names the reader skips
if (!issues.Any(i => i.Rule == "read.invalid"))
{
    Report template = Report.FromJson(json);
    issues.AddRange(TemplateValidator.Check(template));
    issues.AddRange(TemplateFields.Check(template, catalogueJson)); // tokens the data lacks
}
string findings = TemplateIssue.ToJson(issues);   // each with a JSON Pointer into the template
```

`TemplateJson.Check` names a property the engine does not read, which would otherwise be
skipped in silence, along with the name probably meant. `TemplateFields.Check` holds every
token to the host's catalogue of fields, the array the content editor is handed.
[examples/ai](https://github.com/fderenty/Frisket/tree/main/examples/ai) is the guide for
wiring a model to these checks.

## Fonts

A `FontLibrary` nobody fills draws with the typefaces in
[Frisket.Fonts](https://www.nuget.org/packages/Frisket.Fonts), a dependency of this package:
Mulish for text that names no family, plus Elms Sans, Noto Sans Display and Glametrix.
Register a font of your own - `Register`, `RegisterDirectory` - and only yours are used. A
family a template names and the library does not hold is refused, never substituted:
another face would re-wrap every line.

## More

The repository holds the template designer, the content editor, a command-line tool, a C
interface with C++, Pascal and Python wrappers, and the reasons behind each decision:
[github.com/fderenty/Frisket](https://github.com/fderenty/Frisket).

MIT licence. The bundled typefaces are under the SIL Open Font License 1.1.
