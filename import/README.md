# Frisket.Import

Reads one page out of an existing PDF so that [Frisket](https://www.nuget.org/packages/Frisket)
can place it in a document it is writing - a back page of terms drawn by somebody else, a
certificate, a fixed appendix. It lays out like a picture: in a band, in a table cell, at a
coordinate, several to a sheet.

In a template the page is an `ImportedPageItem`:

```json
{ "$type": "ImportedPageItem", "SourcePath": "terms.pdf", "Page": 2,
  "AltText": "The terms, page 2",
  "Style": { "Location": { "WidthValue": 150, "WidthUnit": "Millimeter" } } }
```

and the pages are read with this package's reader, on the template, before it meets the
data - so a page named inside a repeated block is read once, not once per record:

```csharp
using Frisket;
using Frisket.Import;

Report template = Report.FromFile("templates/letter.json");
ImportedPages.Resolve(template, new PdfPageImporter(), baseDirectory: "templates");
Report report = ReportBinder.Bind(template, dataJson);
```

A path may not leave `baseDirectory` (or the `root` you pass): the template chooses the
file, and a template is untrusted the moment somebody else wrote it.

**A copy, not a rendering.** Nothing here interprets a content operator or knows what a font
is: the page's marks and the objects they reach travel as they are, renumbered into the new
file, so pages full of JPEG, JBIG2 or CCITT images import without a line of code for any of
them. Encrypted files are refused rather than half-read.

**An import puts a conformance claim in question.** Frisket cannot vouch for bytes it did
not write - the page may draw a font that is not embedded. It says so in
`LayoutResult.Issues` and refuses nothing: whether the document was meant to be PDF/A is
yours to know.

Frisket depends on nothing here; this package depends on Frisket. MIT licence -
[github.com/fderenty/Frisket](https://github.com/fderenty/Frisket).
