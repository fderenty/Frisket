using System.Text.Json;
using System.Text.Json.Serialization;
using Frisket;

// -----------------------------------------------------------------------------
// The exact preview.
//
// The designer's own preview is an approximation: it measures with the browser's
// fonts and does not paginate. This serves the real engine over HTTP so the same
// page can ask for the truth - one engine, one set of metrics, real pagination -
// while staying a standalone file that works with no server at all.
//
// It also hands out the PDF, which is the same three calls with another backend.
// -----------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5080");

// The designer may well be opened straight off the disk, which sends "Origin: null".
builder.Services.AddCors(options => options.AddDefaultPolicy(
    policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

WebApplication app = builder.Build();
app.UseCors();

FontLibrary fonts = LoadFonts(app.Environment.ContentRootPath, app.Logger);
var measurer = new FontTextMeasurer(fonts);

// The faces themselves, so one address serves the whole thing.
//
// The directory, not one file. The designer used to be a single page and reading it
// off disk was enough; it now loads frisket-core.js, frisket-runs.js and
// frisket-chrome.css beside itself, and a server that answers only for the HTML hands
// the browser a page whose every request 404s. That is how splitting the core broke
// this without anybody noticing: the harness loads the files itself and a file:// URL
// finds them next door, so the one way of opening it that goes through here was the
// one way nobody tried.
string faces = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "designer"));

if (Directory.Exists(faces))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(faces),
        RequestPath = ""
    });
}

string Face(string name) => Path.Combine(faces, name);

app.MapGet("/", () => File.Exists(Face("frisket-designer.html"))
    ? Results.Content(File.ReadAllText(Face("frisket-designer.html")), "text/html; charset=utf-8")
    : Results.NotFound($"The designer was not found under {faces}."));

// The other face. Two editors over one core - one places things at coordinates, the
// other edits a flow - and a server that offered only the first would be deciding
// which of them counts.
app.MapGet("/editor", () => File.Exists(Face("frisket-editor.html"))
    ? Results.Content(File.ReadAllText(Face("frisket-editor.html")), "text/html; charset=utf-8")
    : Results.NotFound($"The editor was not found under {faces}."));

// IsDefault marks the family text that names none is drawn in - what the font directory
// declares - so a face can show such text as it will print rather than in whatever its
// browser falls back to.
app.MapGet("/fonts", () => Results.Ok(fonts.Fonts
    .Select(f => new
    {
        f.Family, f.Subfamily, f.Bold, f.Italic, f.CapHeight,
        IsDefault = string.Equals(f.Family, fonts.DefaultFamily, StringComparison.OrdinalIgnoreCase)
    })
    .OrderBy(f => f.Family).ThenBy(f => f.Subfamily)));

/// The file behind one cut, so a face draws with the outlines the engine measures and
/// embeds instead of with a namesake installed on the machine, or with nothing. Only an
/// exact cut is served. Where a family has no bold or no italic the browser then picks
/// another of its cuts, as FontLibrary.Resolve does - provided it is not allowed to
/// synthesise one, which is why the faces say font-synthesis: none. A slanted regular
/// on screen would be a promise the PDF does not keep.
app.MapGet("/fonts/file", (string family, bool bold, bool italic) =>
{
    FontFile? font = fonts.Fonts.FirstOrDefault(f =>
        string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase) && f.Bold == bold && f.Italic == italic);
    if (font == null)
        return Results.NotFound(new { error = $"No {(bold ? "bold " : "")}{(italic ? "italic " : "")}cut of '{family}'." });

    bool cff = font.Data.Length >= 4 && font.Data[0] == 'O' && font.Data[1] == 'T' && font.Data[2] == 'T' && font.Data[3] == 'O';
    return Results.File(font.Data, cff ? "font/otf" : "font/ttf");
});

/// Takes a font the person at the keyboard chose.
///
/// The library is one for the whole process, so a font sent here is there for everyone
/// until the server stops. That is the right shape for what this is - a demo beside
/// the designer, not a service - and the wrong shape for anything shared, which would
/// want a library per session.
///
/// Parsing is the check, and it is the only one: reading the header would prove the
/// first twelve bytes look like a font, while getting through Register proves the
/// tables are there, the cmap reads and the 'H' can be measured - which is what the
/// engine will need. Refused out loud, unlike a directory scan, which steps over what
/// is not a font because it was never asked about that file in particular.
///
/// Whether the font may lawfully be embedded is not asked. It is the sender's font and
/// the sender's decision; what this project owes is not to ship one it has no right to.
app.MapPost("/fonts", async (HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    byte[] bytes = buffer.ToArray();

    if (bytes.Length == 0)
        return Results.BadRequest(new { error = "No bytes were sent." });

    try
    {
        FontFile font = fonts.Register(bytes);
        app.Logger.LogInformation("Took {Family} {Subfamily} ({Bytes} bytes)",
                                  font.Family, font.Subfamily, bytes.Length);

        return Results.Ok(new
        {
            font.Family, font.Subfamily, font.Bold, font.Italic, font.CapHeight,
            Clashes = fonts.Clashes.Select(c => c.ToString()).ToArray()
        });
    }
    catch (InvalidDataException why)
    {
        return Results.BadRequest(new { error = why.Message });
    }
});

app.MapPost("/preview", (PreviewRequest request) =>
{
    try
    {
        Report report = Build(request);
        var backend = new SvgBackend(measurer) { ShowBands = request.ShowBands };
        LayoutResult layout = ReportRenderer.Render(report, backend);

        return Results.Ok(new
        {
            pageCount = layout.PageCount,
            pages = backend.Pages
        });
    }
    catch (Exception e)
    {
        // The designer shows this verbatim: a template that will not lay out is
        // exactly what the author needs told, in the words the engine used.
        return Results.BadRequest(new { error = e.Message });
    }
});

// The whole report as one HTML file, fonts and all - what `frisket render --format
// html` writes, over HTTP. The engine has been able to do this since the beginning;
// nothing here published it, so no face could offer it.
//
// Text/html rather than JSON: it is meant to be opened in a tab and looked at, and a
// string of HTML inside a JSON envelope only has to be unwrapped again.
app.MapPost("/html", (PreviewRequest request) =>
{
    try
    {
        Report report = Build(request);
        // EmbeddedFonts, or the browser draws with whatever it happens to have while
        // the layout was computed with the real metrics - a preview that drifts from the
        // truth is worse than none, since it looks like the truth.
        var backend = new SvgBackend(measurer)
        {
            ShowBands = request.ShowBands,
            EmbeddedFonts = fonts
        };
        ReportRenderer.Render(report, backend);
        return Results.Content(backend.ToHtml(), "text/html; charset=utf-8");
    }
    catch (Exception e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

// The report as a message: HTML that reflows, for an email rather than a page.
//
// Deliberately beside /html rather than an option on it. They are two different
// documents - one is the pages themselves drawn as SVG, the other has no pages at all -
// and a flag would invite the reading that one is a variant of the other.
app.MapPost("/mail", (PreviewRequest request) =>
{
    try
    {
        return Results.Content(FlowingHtml.Write(Build(request)), "text/html; charset=utf-8");
    }
    catch (Exception e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

app.MapPost("/pdf", (PreviewRequest request) =>
{
    try
    {
        Report report = Build(request);
        var pdf = new PdfBackend(fonts);

        if (request.Archivable)
            pdf.Conformance = PdfConformance.PdfA3b;

        ReportRenderer.Render(report, pdf);
        return Results.File(pdf.ToArray(), "application/pdf", "report.pdf");
    }
    catch (Exception e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

app.Logger.LogInformation("Frisket preview on http://localhost:5080 - {Count} fonts", fonts.Fonts.Count());
app.Run();

// -----------------------------------------------------------------------------

static Report Build(PreviewRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Template))
        throw new ArgumentException("No template was sent.");

    Report template = Report.FromJson(request.Template);

    if (string.IsNullOrWhiteSpace(request.Data))
        return template;

    var options = new BindingOptions();
    if (string.Equals(request.Numbers, "french", StringComparison.OrdinalIgnoreCase))
        options.Numbers = NumberFormats.French;

    return ReportBinder.Bind(template, request.Data!, options);
}

/// <summary>Loads every font shipped with the repository, looking upwards for the folder.</summary>
static FontLibrary LoadFonts(string from, ILogger logger)
{
    var library = new FontLibrary();

    for (DirectoryInfo? at = new(from); at != null; at = at.Parent)
    {
        string candidate = Path.Combine(at.FullName, "engine", "Fonts");
        if (!Directory.Exists(candidate))
            continue;

        logger.LogInformation("Fonts from {Path}: {Count}", candidate, library.RegisterDirectory(candidate));
        return library;
    }

    logger.LogWarning("No engine/Fonts folder found above {From}; text will not measure.", from);
    return library;
}

/// <param name="Template">The template, as the designer saves it.</param>
/// <param name="Data">The data section, or null to render the template as it stands.</param>
/// <param name="ShowBands">Tint the header, body and footer bands.</param>
/// <param name="Numbers">"french" for narrow-space grouping, anything else for invariant.</param>
/// <param name="Archivable">Ask the PDF for PDF/A-3 conformance.</param>
internal sealed record PreviewRequest(
    string Template,
    string? Data = null,
    bool ShowBands = false,
    string? Numbers = null,
    bool Archivable = false);

[JsonSerializable(typeof(PreviewRequest))]
internal partial class ServerJson : JsonSerializerContext;
