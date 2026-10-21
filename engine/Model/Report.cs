using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frisket
{
    /// <summary>
    /// The whole template, and the one file that comes out of it: the properties a reader
    /// and an archive see - title, author, language - the defaults every item inherits
    /// from, the mark and the back printed on every sheet, and the list of sections the
    /// content is laid out on.
    ///
    /// Everything here that is not about the file names the first section only. Dimension,
    /// Header, Body and Footer are conveniences over Sections[0], so once there is a second
    /// section the page size set through the report still reaches only the first, the new
    /// one keeps the sheet of nothing it was born with, and items added through report.Body
    /// go on landing where they always did.
    /// </summary>
    public class Report
    {

        /// <summary>
        /// Every item of every section, depth first. The sections themselves are not
        /// included, which keeps this meaning what it always meant for a report of one.
        /// </summary>
        [JsonIgnore]
        public IEnumerable<BaseItem> AllItems => Sections.SelectMany(Descendants);

        private static IEnumerable<BaseItem> Descendants(ContainerItem container)
        {
            foreach (BaseItem child in container.Childs)
            {
                yield return child;
                if (child is ContainerItem inner)
                {
                    foreach (BaseItem deep in Descendants(inner))
                        yield return deep;
                }
            }
        }

        /* header/footer specific
         * page number
         * total page
         * title
         *
         * */


        private Header? _header;
        private Footer? _footer;
        private Body? _body;

        /// <summary>The document's title: the PDF's /Title and dc:title, what a reader announces on opening it, and what {Title} prints. One per file, whatever the number of sections.</summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// The sheet the first section is laid out on.
        ///
        /// A convenience over <c>Sections[0].Dimension</c>, kept because a report of one
        /// section - which is nearly all of them - should not have to say "section"
        /// twice to set its page size. The size itself belongs to the section, since
        /// that is what varies between sections.
        /// </summary>
        [JsonIgnore]
        public DocumentDimension Dimension
        {
            get => Section.Dimension;
            set => Section.Dimension = value;
        }

        /// <summary>
        /// Reference text size, as a cap height in millimeters. Everything expressed in
        /// <see cref="FontSizeUnit.Rem"/> is a multiple of this, so changing it rescales
        /// the whole report at once.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public double RootFontSize { get; set; } = 3.0;

        /// <summary>
        /// The report's own text style. Anything a <see cref="TextItem"/> leaves
        /// undecided is taken from here, so a template names its typeface once and can be
        /// restyled by changing one line. What neither settles falls back to the built-in
        /// defaults documented on <see cref="TextStyle"/>.
        /// </summary>
        public TextStyle DefaultFont { get; set; } = new();

        /// <summary>
        /// Values for the custom tokens a text may use, such as <c>{CustomerName}</c>.
        /// </summary>
        public Dictionary<string, string> Fields { get; set; } = new();

        /// <summary>
        /// The language the document is written in, as a BCP 47 tag - "fr-FR", "en-GB".
        /// A reader announcing French text with an English voice is unintelligible, and
        /// this one line is what prevents it.
        /// </summary>
        public string? Language { get; set; }

        /// <summary>
        /// How heading numbers are spelled - "1.1.1", "A.a.1", "A) a. 1" - for the
        /// <c>{HeadingNumber}</c> and <c>{SectionHeadingNumber}</c> tokens. Null for
        /// none, which is the default: a document numbers its headings only if it says
        /// so. See <see cref="HeadingNumbers"/>.
        ///
        /// It belongs to the file rather than the section, the way <see cref="Title"/>
        /// does - numbering is a convention of the document, not of one page size. If a
        /// section ever needs its own, that is an override to add, and adding one later
        /// breaks nothing.
        /// </summary>
        public string? HeadingNumbering { get; set; }

        /// <summary>
        /// Who the document is from. Written into the file's properties and its XMP
        /// packet, which a reader shows and an archive indexes on.
        /// </summary>
        public string? Author { get; set; }

        /// <summary>What the document is about, in a line.</summary>
        public string? Subject { get; set; }

        /// <summary>
        /// Words an archive can search on, comma separated - an invoice number and a
        /// customer reference, typically. This is what makes a file findable years later
        /// without opening it.
        /// </summary>
        public string? Keywords { get; set; }

        /// <summary>
        /// What is printed on the back of a sheet - conditions of sale, most often. Null
        /// for a single-sided report. Back pages are neither numbered nor counted.
        /// </summary>
        public BackPage? BackPage { get; set; }

        /// <summary>
        /// A word written across every page - SPECIMEN on a preview, say. Null for none.
        /// It is drawn as page content rather than as a background, which is what makes
        /// it survive being printed from a browser.
        /// </summary>
        public Watermark? Watermark { get; set; }

        /// <summary>
        /// A picture drawn behind every page - letterhead, stationery, a tinted panel.
        /// Null for none. A sibling of <see cref="Watermark"/> rather than a mode of it,
        /// and the printed counterpart of <see cref="ProofBackground"/>, which is only ever
        /// shown to whoever is writing the template.
        /// </summary>
        public Backdrop? Backdrop { get; set; }

        /// <summary>
        /// The colour of the paper this is meant to be printed on - letterhead, coloured
        /// stock - so an editor can show the page against it while the template is being
        /// written. Null, and white, unless the template says otherwise.
        ///
        /// <b>Nothing ever prints it.</b> Not the PDF, not the SVG, not the flowing HTML:
        /// it is a statement about paper the engine will never supply, and a proof that
        /// drew it would be claiming the printer does. The SVG in particular is the
        /// engine's own evidence of what the PDF will contain, and the two are held to
        /// each other - a colour in one and not the other would be exactly the divergence
        /// that check exists to catch.
        ///
        /// So it is carried, not drawn. It lives here rather than in an editor's own
        /// settings because it belongs to the document: it survives being saved, and the
        /// two faces agree about it without either of them owning it. Test 72 renders a
        /// report with it set and without, and requires the bytes to be identical.
        /// </summary>
        public Color? ProofBackground { get; set; }

        /// <summary>
        /// The colours this document has used, most recent first - what an editor offers
        /// back in one click instead of making somebody find the same blue twice.
        ///
        /// <b>Nothing ever prints it</b>, exactly like <see cref="ProofBackground"/>: it
        /// is a list of choices, not a mark on a page. It lives on the report rather than
        /// in an editor's own settings because it belongs to the document - it survives
        /// being saved, travels with the file, and both faces see the same one without
        /// either of them owning it.
        ///
        /// A document that carries none is not a document with no colours: an editor can
        /// read what is actually on the page and offer that instead, which is what an
        /// imported template needs. Kept rather than derived so that a colour used once
        /// and then undone is still to hand - a history that forgets on undo is not one.
        /// </summary>
        public List<Color> Swatches { get; set; } = new();

        /// <summary>
        /// Timestamp behind <c>{Date}</c> and <c>{Time}</c>. Left null it is taken at
        /// resolve time; set it to make a layout reproducible.
        /// </summary>
        public DateTimeOffset? GeneratedAt { get; set; }


        // Header, body and footer are children of the section like anything else;
        // these are conveniences over the tree, not a second place to store it. Only the
        // section is serialized, so nothing can be written twice or drift apart.

        [JsonIgnore]
        public Header? Header { get => _header; set { Section.Remove(_header!); _header = value; if (_header != null) Section.Add(_header); } }

        [JsonIgnore]
        public Body? Body { get => _body; set { Section.Remove(_body!); _body = value; if (_body != null) Section.Add(_body); } }

        [JsonIgnore]
        public Footer? Footer { get => _footer; set { Section.Remove(_footer!); _footer = value; if (_footer != null) Section.Add(_footer); } }

        /// <summary>
        /// The sections, in order. One section is the ordinary case; several is what
        /// mixes page sizes in one file, and what assembles separate documents into one.
        ///
        /// A list rather than a chain from the first: there is no principal section
        /// with satellites hanging off it, only sections as equals.
        /// </summary>
        public List<Section> Sections { get; set; } = new();

        /// <summary>
        /// The first section. Everything written before sections existed says
        /// <c>report.Section</c>, <c>report.Body</c>, <c>report.Header</c>, and means
        /// the only one there was; this keeps all of that saying the same thing.
        /// </summary>
        [JsonIgnore]
        public Section Section
        {
            get
            {
                if (Sections.Count == 0)
                    Sections.Add(new Section(this));
                return Sections[0];
            }
            set
            {
                value.Owner = this;
                if (Sections.Count == 0) Sections.Add(value);
                else Sections[0] = value;
            }
        }

        /// <summary>
        /// Adds a section and returns it, so a caller can set its page size and fill it
        /// without first working out where in the list it landed.
        /// </summary>
        public Section AddSection(Section? section = null)
        {
            Section added = section ?? new Section(this);
            added.Owner = this;
            Sections.Add(added);
            return added;
        }

        public Report()
        {
            Sections.Add(new Section(this));
        }

        /// <summary>
        /// Lays the report out and returns the pages to draw.
        /// Pass the measurer of the backend you are about to draw with, so that layout
        /// and drawing agree on font metrics; the default is a fixed-pitch stand-in that
        /// is deterministic but matches no real font.
        /// </summary>
        public LayoutResult Resolve(ITextMeasurer? measurer = null,
                                   CancellationToken cancellation = default)
            => new LayoutResolver(this, measurer ?? FixedPitchTextMeasurer.Default)
                   { Cancellation = cancellation }.Resolve();

        /// <summary>Lays the report out using the backend's own metrics.</summary>
        public LayoutResult Resolve(IReportBackend backend,
                                   CancellationToken cancellation = default)
            => new LayoutResolver(this, backend.Measurer)
                   { Cancellation = cancellation }.Resolve();

        /// <summary>Serialises the definition. The computed layout is never part of it.</summary>
        public string ToJson()
            => JsonSerializer.Serialize(this, SourceGenerationContext.Default.Report);

        /// <summary>
        /// Reads a definition back. Parent and Owner are the tree's own back-references
        /// and are not serialized, so they are rebuilt here.
        /// </summary>
        public static Report FromJson(string json)
        {
            Report report = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.Report)
                            ?? throw new JsonException("The JSON did not contain a report.");
            report.Reattach();
            return report;
        }

        /// <summary>
        /// Reads a definition from disk and resolves every <see cref="SubTemplateItem"/>
        /// in it, each relative to the file that names it rather than to this one or to
        /// the working directory. <see cref="FromJson"/> alone cannot do this - it has no
        /// path to resolve a relative include against - which is why this exists as its
        /// own entry point rather than a variant reading the text first.
        /// </summary>
        public static Report FromFile(string path, ISubTemplates? source = null)
        {
            Report report = FromJson(File.ReadAllText(path));
            string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            SubTemplateExpander.Expand(report, directory, source);
            return report;
        }

        /// <summary>
        /// Splices in every sub-template that can be resolved without a file: one whose
        /// JSON it carries, and one named <c>st://</c> that <paramref name="source"/> can
        /// find. A sub-template naming a path is refused here, because there is no
        /// directory to resolve it against - that is what <see cref="FromFile"/> is for.
        ///
        /// This is the door a server and an editor come through. Both are handed JSON as
        /// text, with no file of their own, so until this existed neither could splice a
        /// sub-template at all and the designer's panel said so rather than letting the
        /// preview pretend (CLAUDE.md section 14).
        ///
        /// Separate from <see cref="FromJson"/>, which stays a parse and nothing else:
        /// expansion happens before binding and before layout, and a step that quietly
        /// did two things would make that order harder to see rather than easier.
        /// </summary>
        public void ExpandSubTemplates(ISubTemplates? source = null)
            => SubTemplateExpander.Expand(this, "", "", source);

        /// <summary>
        /// Restores every Parent and Owner from the shape of the tree. Idempotent, and
        /// worth calling after building a tree by any means other than Add.
        /// </summary>
        public void Reattach()
        {
            // Deserialising a file with no Sections array leaves the list empty rather
            // than wrong; one empty section is what an empty report is.
            if (Sections.Count == 0)
                Sections.Add(new Section(this));

            foreach (Section section in Sections)
            {
                section.Owner = this;
                Reattach(section);
            }

            // The band shortcuts are derived from the tree, never stored separately, and
            // they name the first section's bands - which is what they always named.
            _header = Section.Childs.OfType<Header>().FirstOrDefault();
            _body = Section.Childs.OfType<Body>().FirstOrDefault();
            _footer = Section.Childs.OfType<Footer>().FirstOrDefault();
        }

        private void Reattach(ContainerItem container)
        {
            foreach (BaseItem child in container.Childs)
            {
                child.Parent = container;
                child.Owner = this;
                if (child is ContainerItem inner)
                    Reattach(inner);
            }
        }
    }

    // Anything at its default is left out. An unset property means "inherit", and a
    // template that spells out every zero is both longer and less honest about what it
    // actually decides: a plain text item was writing thirty-five lines of style to say
    // nothing at all.
    //
    // This is only safe where a property's default is default(T). Where it is not - a
    // position that starts at NaN rather than 0, a table header that repeats unless told
    // otherwise - the property carries JsonIgnoreCondition.Never, because leaving it out
    // would read back as something else. Those are marked one by one; see Style.Location
    // and TableItem.RepeatHeader.
    [JsonSourceGenerationOptions(WriteIndented = true,
                                 NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                                 DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonSerializable(typeof(Report))]
    public partial class SourceGenerationContext : JsonSerializerContext
    {
    }
}
