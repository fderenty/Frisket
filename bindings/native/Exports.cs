using System.Runtime.InteropServices;
using System.Text.Json;
using Frisket;

namespace Frisket.Native
{
    /// <summary>
    /// The C face of the engine.
    ///
    /// Everything crosses as a null-terminated UTF-8 string, an opaque handle, or a
    /// status code - the three things every language with an FFI already knows how to
    /// spell. No structs are passed by value, because agreeing on a struct layout across
    /// C, Pascal, Python and whatever comes next is a decade of small mistakes; options
    /// travel as JSON instead, which the engine speaks anyway and which can gain a field
    /// without breaking a single caller.
    ///
    /// Every export catches. An exception reaching this boundary ends the process with
    /// nothing to read, so the rule is absolute rather than a matter of care.
    /// </summary>
    public static unsafe class Exports
    {
        // ------------------------------------------------------------------
        // What went wrong, and what this is
        // ------------------------------------------------------------------

        /// <summary>
        /// The last failure on this thread, or null. Valid until the next call on the
        /// same thread - copy it if you intend to keep it.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_last_error")]
        public static IntPtr LastError() => Interop.LastError();

        /// <summary>
        /// The version this library was built as - the release's tag, or 0.1.0-dev for a build
        /// that is not one - without the commit the build metadata appends.
        ///
        /// It answered a literal "1.0.0" until the first release was being prepared, which
        /// every build would have gone on saying; and it allocated a fresh copy on every call
        /// while frisket.h promised a static string nobody frees. Made once, kept.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_version")]
        public static IntPtr Version() => VersionText;

        private static readonly IntPtr VersionText = Interop.Utf8(
            (typeof(Exports).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion ?? "0.0.0").Split('+')[0]);

        // ------------------------------------------------------------------
        // Fonts
        // ------------------------------------------------------------------

        /// <summary>
        /// An empty font library. Left empty it draws with the typefaces bundled into this
        /// library - Mulish for text that names no family; the first face added makes them
        /// step aside, and the two are never mixed.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_fonts_create")]
        public static int FontsCreate(IntPtr* library) => Interop.Guarded(() =>
        {
            if (library == null)
                return Interop.Fail("fk_fonts_create: no output pointer");

            *library = Handles.Wrap(new FontLibrary());
            return Interop.Ok;
        });

        /// <summary>
        /// Registers every font file in a directory, and reports how many were taken.
        /// A count of zero is not an error here - the caller may be adding several
        /// directories - but rendering with an empty library is.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_fonts_add_directory")]
        public static int FontsAddDirectory(IntPtr library, IntPtr directory, int* count)
            => Interop.Guarded(() =>
        {
            if (Handles.Unwrap<FontLibrary>(library) is not { } fonts)
                return Interop.Fail("fk_fonts_add_directory: not a font library");

            if (Interop.Read(directory) is not { } path)
                return Interop.Fail("fk_fonts_add_directory: no directory");

            if (!Directory.Exists(path))
                return Interop.Fail($"fk_fonts_add_directory: no directory at '{path}'");

            int found = fonts.RegisterDirectory(path);
            if (count != null)
                *count = found;

            return Interop.Ok;
        });

        /// <summary>
        /// One font file, named on purpose - so a failure is reported rather than
        /// stepped over.
        ///
        /// <c>fk_fonts_add_directory</c> shrugs at a file that is not a font: it was
        /// never asked about that file in particular. This was asked about exactly one,
        /// and skipping it in silence would leave a caller looking at a list their font
        /// is not in, with nothing to say why.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_fonts_add_file")]
        public static int FontsAddFile(IntPtr library, IntPtr file) => Interop.Guarded(() =>
        {
            if (Handles.Unwrap<FontLibrary>(library) is not { } fonts)
                return Interop.Fail("fk_fonts_add_file: not a font library");

            if (Interop.Read(file) is not { } path)
                return Interop.Fail("fk_fonts_add_file: no path");

            if (!File.Exists(path))
                return Interop.Fail($"fk_fonts_add_file: no file at '{path}'");

            fonts.Register(path);
            return Interop.Ok;
        });

        /// <summary>
        /// One font from its bytes, for a caller that has no file - a font posted over
        /// HTTP, held in a database, picked in a browser.
        ///
        /// Parsing is the check, and there is no cheaper one worth having. Reading the
        /// header would prove the first twelve bytes look like a font; getting through
        /// this proves the tables are there, the cmap reads and the 'H' can be measured,
        /// which is what the engine will actually need. A second, weaker reader run
        /// first could only agree with this one or contradict it - and the contradiction
        /// is the interesting case, since it would be a font declared good and then
        /// refused, or refused and then perfectly usable.
        ///
        /// Whether the font may lawfully be embedded is not asked. It is the caller's
        /// font and the caller's decision; what this project owes is not to *ship* one
        /// it has no right to (CLAUDE.md 3), which is a different promise.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_fonts_add_memory")]
        public static int FontsAddMemory(IntPtr library, byte* data, int length)
            => Interop.Guarded(() =>
        {
            if (Handles.Unwrap<FontLibrary>(library) is not { } fonts)
                return Interop.Fail("fk_fonts_add_memory: not a font library");

            if (data == null || length <= 0)
                return Interop.Fail("fk_fonts_add_memory: no bytes");

            var bytes = new byte[length];
            new ReadOnlySpan<byte>(data, length).CopyTo(bytes);

            fonts.Register(bytes);
            return Interop.Ok;
        });

        /// <summary>
        /// A token one thread hands to a render and another thread trips.
        ///
        /// Its own handle rather than a flag on the template or the font library, and
        /// deliberately: a token belongs to one operation. Hung on a template, cancelling
        /// one render would cancel the next render that happened to use it - which is a
        /// fault nobody would look for, since the second call did nothing wrong.
        ///
        /// This is what makes cancellation possible at all with a synchronous boundary:
        /// fk_render does not return until it is finished, so the word has to come from
        /// somewhere else, and a handle is somewhere else.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_token_create")]
        public static int TokenCreate(IntPtr* result) => Interop.Guarded(() =>
        {
            if (result == null)
                return Interop.Fail("fk_token_create: no output pointer");

            *result = Handles.Wrap(new CancellationTokenSource());
            return Interop.Ok;
        });

        /// <summary>
        /// Asks whatever is using this token to stop. Safe from any thread, and safe to
        /// call twice - the second time is not an error, it is a caller who does not
        /// know the first one landed.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_cancel")]
        public static int Cancel(IntPtr token) => Interop.Guarded(() =>
        {
            if (Handles.Unwrap<CancellationTokenSource>(token) is not { } source)
                return Interop.Fail("fk_cancel: not a token");

            if (!source.IsCancellationRequested)
                source.Cancel();

            return Interop.Ok;
        });

        [UnmanagedCallersOnly(EntryPoint = "fk_token_free")]
        public static void TokenFree(IntPtr token)
        {
            if (Handles.Unwrap<CancellationTokenSource>(token) is { } source)
                source.Dispose();

            Handles.Release(token);
        }

        /// <summary>
        /// What to use for a text that names no family at all.
        ///
        /// Not a substitute for a family a template *does* name and the library does
        /// not hold: that is <see cref="FontNotFoundException"/>, deliberately, since
        /// laying the report out against another face would move every line break.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_fonts_set_default")]
        public static int FontsSetDefault(IntPtr library, IntPtr family) => Interop.Guarded(() =>
        {
            if (Handles.Unwrap<FontLibrary>(library) is not { } fonts)
                return Interop.Fail("fk_fonts_set_default: not a font library");

            fonts.DefaultFamily = Interop.Read(family);
            return Interop.Ok;
        });

        /// <summary>
        /// What the library holds, as JSON: family, weight, slant, and the cap height the
        /// engine measured from the outline rather than read from the font's own tables.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_fonts_list")]
        public static int FontsList(IntPtr library, IntPtr* result) => Interop.Guarded(() =>
        {
            if (Handles.Unwrap<FontLibrary>(library) is not { } fonts)
                return Interop.Fail("fk_fonts_list: not a font library");

            if (result == null)
                return Interop.Fail("fk_fonts_list: no output pointer");

            var json = new System.Text.StringBuilder("[");
            bool first = true;

            foreach (FontFile font in fonts.Fonts)
            {
                if (!first) json.Append(',');
                first = false;

                json.Append("{\"family\":").Append(JsonSerializer.Serialize(font.Family,
                        SourceGenerationContext.Default.String))
                    .Append(",\"bold\":").Append(font.Bold ? "true" : "false")
                    .Append(",\"italic\":").Append(font.Italic ? "true" : "false")
                    .Append(",\"capHeight\":")
                    .Append(font.CapHeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(",\"capHeightIsEstimated\":")
                    .Append(font.CapHeightIsEstimated ? "true" : "false")
                    // Reported, never enforced: whose font it is and what its licence
                    // permits is the caller's to answer, not this library's to guess.
                    .Append(",\"embeddingRights\":").Append(font.EmbeddingRights)
                    .Append(",\"embeddingRestricted\":")
                    .Append(font.EmbeddingRestricted ? "true" : "false").Append('}');
            }

            *result = Handles.Wrap(new Buffer(json.Append(']').ToString()));
            return Interop.Ok;
        });

        [UnmanagedCallersOnly(EntryPoint = "fk_fonts_free")]
        public static void FontsFree(IntPtr library) => Handles.Release(library);

        // ------------------------------------------------------------------
        // Templates
        // ------------------------------------------------------------------

        /// <summary>
        /// Reads a template from JSON text. A <see cref="SubTemplateItem"/> in it is left
        /// unresolved: resolving one means reading another file relative to wherever this
        /// text came from, and a bare string carries no such place. Load from a path with
        /// <see cref="TemplateLoad"/> when the template may include others.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_template_parse")]
        public static int TemplateParse(IntPtr json, IntPtr* template) => Interop.Guarded(() =>
        {
            if (template == null)
                return Interop.Fail("fk_template_parse: no output pointer");

            if (Interop.Read(json) is not { Length: > 0 } text)
                return Interop.Fail("fk_template_parse: no JSON");

            *template = Handles.Wrap(Report.FromJson(text));
            return Interop.Ok;
        });

        /// <summary>
        /// The same, from a file, so a caller need not read it themselves - and, unlike
        /// <see cref="TemplateParse"/>, resolving every <see cref="SubTemplateItem"/> the
        /// template contains, each relative to where it is.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_template_load")]
        public static int TemplateLoad(IntPtr path, IntPtr* template) => Interop.Guarded(() =>
        {
            if (template == null)
                return Interop.Fail("fk_template_load: no output pointer");

            if (Interop.Read(path) is not { Length: > 0 } file)
                return Interop.Fail("fk_template_load: no path");

            if (!File.Exists(file))
                return Interop.Fail($"fk_template_load: no template at '{file}'");

            *template = Handles.Wrap(Report.FromFile(file));
            return Interop.Ok;
        });

        /// <summary>
        /// What is wrong with the template, as a JSON array. Empty means nothing to
        /// report. Same rules the designer shows and an outside validator would reach.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_template_check")]
        public static int TemplateCheck(IntPtr template, int accessible, IntPtr* result)
            => Interop.Guarded(() =>
        {
            if (Handles.Unwrap<Report>(template) is not { } report)
                return Interop.Fail("fk_template_check: not a template");

            if (result == null)
                return Interop.Fail("fk_template_check: no output pointer");

            IReadOnlyList<TemplateIssue> issues = TemplateValidator.Check(report,
                accessible != 0 ? TemplateProfile.All : TemplateProfile.Sound);

            var json = new System.Text.StringBuilder("[");
            for (int i = 0; i < issues.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"rule\":").Append(Text(issues[i].Rule))
                    .Append(",\"level\":").Append(Text(issues[i].Level.ToString().ToLowerInvariant()))
                    .Append(",\"where\":").Append(Text(issues[i].Where))
                    .Append(",\"message\":").Append(Text(issues[i].Message)).Append('}');
            }

            *result = Handles.Wrap(new Buffer(json.Append(']').ToString()));
            return Interop.Ok;
        });

        [UnmanagedCallersOnly(EntryPoint = "fk_template_free")]
        public static void TemplateFree(IntPtr template) => Handles.Release(template);

        // ------------------------------------------------------------------
        // The one that matters
        // ------------------------------------------------------------------

        /// <summary>
        /// Produces one document. <paramref name="dataJson"/> may be null, in which case
        /// the template is rendered as it stands - which is how a template is looked at
        /// while it is being drawn.
        ///
        /// The result is bytes for a PDF, UTF-8 text for HTML, and a JSON envelope for
        /// SVG - pages, font CSS and sheet size - since a set of pages is not one file.
        /// Every result is null-terminated as well as sized, so a text one can be used as
        /// a C string without copying.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_render")]
        public static int Render(IntPtr template, IntPtr fonts, IntPtr dataJson,
                                 IntPtr optionsJson, IntPtr token, IntPtr* result)
            => Interop.Guarded(() =>
        {
            if (Handles.Unwrap<Report>(template) is not { } loaded)
                return Interop.Fail("fk_render: not a template");

            // NULL is the ordinary case and means "this cannot be cancelled". A handle
            // that is not a token is a mistake, though, and says so rather than being
            // quietly treated as none - the caller believes they can stop this.
            CancellationToken cancellation = CancellationToken.None;
            if (token != IntPtr.Zero)
            {
                if (Handles.Unwrap<CancellationTokenSource>(token) is not { } source)
                    return Interop.Fail("fk_render: not a cancellation token");
                cancellation = source.Token;
            }

            if (Handles.Unwrap<FontLibrary>(fonts) is not { } library)
                return Interop.Fail("fk_render: not a font library");

            if (result == null)
                return Interop.Fail("fk_render: no output pointer");

            RenderOptions options = RenderOptions.Parse(Interop.Read(optionsJson));

            // Bound or bare, the renderer is handed a section, never the template
            // itself: binding works on a copy so a template can be filled many times.
            Report report = Interop.Read(dataJson) is { Length: > 0 } data
                ? ReportBinder.Bind(loaded, data, new BindingOptions { Numbers = options.Numbers })
                : loaded;

            Buffer produced = options.Format switch
            {
                "pdf" => new Buffer(Pdf(report, library, options, cancellation)),
                "html" => new Buffer(Html(report, library, options, cancellation)),
                "svg" => new Buffer(Svg(report, library, cancellation)),
                var other => throw new ArgumentException(
                    $"unknown format '{other}'. Try pdf, html or svg.")
            };

            *result = Handles.Wrap(produced);
            return Interop.Ok;
        });

        private static byte[] Pdf(Report report, FontLibrary library, RenderOptions options,
                                  CancellationToken cancellation)
        {
            var pdf = new PdfBackend(library)
            {
                Conformance = options.Conformance,
                Tagged = options.Tagged,
                Subset = options.Subset,
                Interactive = options.Interactive,
                Language = report.Language
            };

            ReportRenderer.Render(report, pdf, cancellation);
            return pdf.ToArray();
        }

        private static string Html(Report report, FontLibrary library, RenderOptions options,
                                   CancellationToken cancellation)
        {
            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(report, svg, cancellation);
            return svg.ToHtml(options.Bare ? SvgBackend.HtmlChrome.Bare : SvgBackend.HtmlChrome.Preview);
        }

        private static string Svg(Report report, FontLibrary library,
                                  CancellationToken cancellation)
        {
            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(report, svg, cancellation);
            SvgBackend.SvgFragment fragment = svg.ToFragment();

            var json = new System.Text.StringBuilder("{\"pageWidth\":")
                .Append(fragment.PageWidth.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(",\"pageHeight\":")
                .Append(fragment.PageHeight.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(",\"fontCss\":").Append(Text(fragment.FontCss))
                .Append(",\"pages\":[");

            for (int i = 0; i < fragment.Pages.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(Text(fragment.Pages[i]));
            }

            return json.Append("]}").ToString();
        }

        // ------------------------------------------------------------------
        // Buffers
        // ------------------------------------------------------------------

        /// <summary>The bytes. Valid until the buffer is freed.</summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_buffer_data")]
        public static IntPtr BufferData(IntPtr buffer)
            => Handles.Unwrap<Buffer>(buffer) is { } held ? held.Data : IntPtr.Zero;

        /// <summary>How many, not counting the terminator that is there anyway.</summary>
        [UnmanagedCallersOnly(EntryPoint = "fk_buffer_size")]
        public static int BufferSize(IntPtr buffer)
            => Handles.Unwrap<Buffer>(buffer) is { } held ? held.Size : 0;

        [UnmanagedCallersOnly(EntryPoint = "fk_buffer_free")]
        public static void BufferFree(IntPtr buffer) => Handles.Release(buffer);

        // ------------------------------------------------------------------

        private static string Text(string? value)
            => JsonSerializer.Serialize(value ?? string.Empty, SourceGenerationContext.Default.String);
    }
}
