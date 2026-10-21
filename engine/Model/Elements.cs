using System.Text.Json.Serialization;

namespace Frisket
{
    [JsonConverter(typeof(JsonStringEnumConverter<LineOrientation>))]
    public enum LineOrientation
    {
        /// <summary>Left to right, halfway down the frame. The separator of every invoice.</summary>
        Horizontal,

        /// <summary>Top to bottom, halfway across the frame.</summary>
        Vertical,

        /// <summary>Top-left to bottom-right.</summary>
        DiagonalDown,

        /// <summary>Bottom-left to top-right.</summary>
        DiagonalUp
    }

    /// <summary>A straight rule. With no explicit height it is as thick as its pen.</summary>
    public class LineItem : BaseItem
    {
        /// <summary>Horizontal, Vertical, or one of the two diagonals of the frame (DiagonalDown, DiagonalUp).</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<LineOrientation>))]
        public LineOrientation Orientation { get; set; } = LineOrientation.Horizontal;

        /// <summary>How the line is drawn: colour, thickness and dash.</summary>
        public PenStyle Pen { get; set; } = new();

        protected override BaseItem CreateEmpty() => new LineItem();

        protected override void CopyTo(BaseItem target)
        {
            var copy = (LineItem)target;
            copy.Orientation = Orientation;
            copy.Pen = Pen.Clone();
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter<ImageScaling>))]
    public enum ImageScaling
    {
        /// <summary>Fill the frame, ignoring the aspect ratio.</summary>
        Stretch,

        /// <summary>Fit inside the frame, keeping the aspect ratio.</summary>
        Uniform,

        /// <summary>Draw at natural size, top-left of the frame.</summary>
        None
    }

    /// <summary>
    /// A picture. The engine decodes nothing - that would mean a graphics dependency -
    /// so the natural size is declared by the author, in millimeters. Giving only one of
    /// the two lets the other follow from <see cref="NaturalWidth"/>/<see cref="NaturalHeight"/>.
    /// </summary>
    public class ImageItem : BaseItem
    {
        /// <summary>The picture carried in the template. Empty when it comes from the data instead - see SourceField.</summary>
        public ImageSource Source { get; set; } = new();

        /// <summary>Natural width in millimeters, NaN when unknown.</summary>
        [JsonIgnore]
        public double NaturalWidth { get; set; } = double.NaN;

        /// <summary>
        /// The JSON face of <see cref="NaturalWidth"/>. NaN is this engine's "not set", and a
        /// template says that best by not mentioning the property at all - so it maps to
        /// null, which the serializer then leaves out.
        /// </summary>
        [JsonPropertyName("NaturalWidth")]
        public double? NaturalWidthJson
        {
            get => double.IsNaN(NaturalWidth) ? null : NaturalWidth;
            set => NaturalWidth = value ?? double.NaN;
        }

        /// <summary>Natural height in millimeters, NaN when unknown.</summary>
        [JsonIgnore]
        public double NaturalHeight { get; set; } = double.NaN;

        /// <summary>
        /// The JSON face of <see cref="NaturalHeight"/>. NaN is this engine's "not set", and a
        /// template says that best by not mentioning the property at all - so it maps to
        /// null, which the serializer then leaves out.
        /// </summary>
        [JsonPropertyName("NaturalHeight")]
        public double? NaturalHeightJson
        {
            get => double.IsNaN(NaturalHeight) ? null : NaturalHeight;
            set => NaturalHeight = value ?? double.NaN;
        }

        /// <summary>How the picture fills its frame: Uniform keeps its proportions, Stretch fills the frame, None draws it at its natural size.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<ImageScaling>))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public ImageScaling Scaling { get; set; } = ImageScaling.Uniform;

        /// <summary>
        /// Path in the data the bytes come from, as base64 or a data: URI. This is how a
        /// signature drawn on a tablet reaches the page: the browser posts the canvas,
        /// and the template says where to look for it rather than carrying it.
        /// </summary>
        public string? SourceField { get; set; }

        /// <summary>Width over height, NaN when either natural dimension is missing.</summary>
        [JsonIgnore]
        public double AspectRatio => NaturalWidth > 0 && NaturalHeight > 0
            ? NaturalWidth / NaturalHeight
            : double.NaN;

        protected override BaseItem CreateEmpty() => new ImageItem();

        protected override void CopyTo(BaseItem target)
        {
            var copy = (ImageItem)target;
            copy.NaturalWidth = NaturalWidth;
            copy.NaturalHeight = NaturalHeight;
            copy.Scaling = Scaling;
            copy.SourceField = SourceField;
            // The bytes are shared on purpose: a logo repeated on every row should not
            // be copied once per row.
            copy.Source = Source;
        }
    }

    /// <summary>
    /// A contact sheet: its children - ordinarily <see cref="ImageItem"/>, one per
    /// picture - are laid out <see cref="GridColumns"/> to a row, each cell the same
    /// width and <see cref="RowHeight"/> tall, the picture fitted inside proportionally
    /// (<see cref="ImageScaling.Uniform"/>, already the type's default). A row is never
    /// split - a picture cannot continue itself onto the next page the way a paragraph
    /// or a table row can - so a row that does not fit whole moves there entire.
    ///
    /// Populating it from data needs nothing new: <see cref="ContainerItem.RepeatOver"/>
    /// already stamps a single prototype child out once per record, and the binder
    /// already resolves an <see cref="ImageItem.SourceField"/> per copy - the same two
    /// mechanisms a repeating table row already relies on.
    /// </summary>
    public class ImageGridItem : ContainerItem
    {
        /// <summary>How many pictures sit side by side before a new row starts. One is
        /// a valid choice - a single column of full-width photos.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public int GridColumns { get; set; } = 3;

        /// <summary>Height of one row, in millimetres.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public double RowHeight { get; set; } = 40;

        /// <summary>
        /// Millimetres between one picture and its neighbours, across and down. Zero -
        /// pictures touching - is a real choice, not "not set", so it is written out
        /// like the other two rather than read back as 4.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public double Gap { get; set; } = 4;

        public ImageGridItem()
        {
            Layout = LayoutMode.Stack;   // this is what paginates it, like Body and TableItem
        }

        protected override BaseItem CreateEmpty() => new ImageGridItem();

        protected override void CopyTo(BaseItem target)
        {
            base.CopyTo(target);
            var copy = (ImageGridItem)target;
            copy.GridColumns = GridColumns;
            copy.RowHeight = RowHeight;
            copy.Gap = Gap;
        }
    }

    /// <summary>
    /// A splice point: another template's file, inserted as if its <see cref="Body"/>
    /// had been written here. Composing one document out of several smaller files
    /// rather than one large one is the whole point, so only the body transplants -
    /// a sub-template's own header and footer, if it has any, stay unused, and its own
    /// page margins are never consulted; whatever holds this item governs the space.
    ///
    /// Resolved once, before binding: <see cref="Report.FromFile"/> replaces every
    /// <see cref="SubTemplateItem"/> in the tree with the referenced body's children,
    /// recursively, so a sub-template may itself include further sub-templates. A
    /// cycle - a file that includes itself, directly or through others - is refused
    /// rather than looped forever.
    ///
    /// Behaves as an ordinary container once resolved: <see cref="BaseItem.ShowWhen"/>
    /// and <see cref="BaseItem.HideWhen"/> on this item decide whether the included
    /// content appears at all, exactly as they would on any other element - inserting
    /// a sub-template conditionally needs nothing further.
    /// </summary>
    /// <summary>
    /// What a sub-template brings across: a block, or a document.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SubTemplateMode>))]
    public enum SubTemplateMode
    {
        /// <summary>
        /// The other template's <see cref="Report.Body"/>, as children of this item, so
        /// it merges into the surrounding flow. Its header, footer, page size and
        /// margins are dropped - there is nowhere to put them - which is the right
        /// answer for a reused block and the wrong one for a whole piece.
        /// </summary>
        Splice,

        /// <summary>
        /// The other template's sections, whole, placed after the section that carries
        /// this item. Its own page size, header, footer and margins come with it, which
        /// is what makes a dossier out of self-contained pieces.
        ///
        /// Two things follow from a document not being a flow item. The item's POSITION
        /// in the flow decides nothing - only which section it sits in, and therefore
        /// where in the run of sections the piece lands; anything declared after it
        /// stays in the host's section, and so comes out before the piece. And what a
        /// file says about itself - title, author, subject, keywords - has one value
        /// for the whole file, so the pieces' are dropped and the host's stand.
        ///
        /// The exception is the language, which PDF lets an element carry: a piece
        /// declaring one is given it on the section it becomes, rather than being read
        /// aloud in the host's. See <see cref="BaseItem.Language"/>.
        /// </summary>
        Append
    }

    /// <summary>
    /// A page of another PDF, drawn as part of this document.
    ///
    /// The use it was asked for is a back page of terms somebody else drew, but nothing
    /// here is about back pages: it lays out like a picture, in a band or a cell or at a
    /// coordinate, and several may sit on one sheet.
    ///
    /// <para><b>The engine does not read it.</b> Parsing another producer's PDF is
    /// consuming bytes somebody else chose, and that lives in <c>Frisket.Import</c>, a
    /// separate assembly this one never references. A template names the page;
    /// <see cref="ImportedPages.Resolve"/> is where a caller hands over something
    /// able to read it, and until then the item knows only where to look.</para>
    ///
    /// <para><b>And it puts a conformance claim in question</b>, which is reported through
    /// <c>LayoutResult.Issues</c> and refused by nothing. The page may carry
    /// a font that is not embedded or transparency a profile forbids, and the engine
    /// cannot vouch for bytes it did not write - so it states the fact and leaves the
    /// choice with whoever asked for the claim. A report being assembled to be read has
    /// no need of PDF/A at all, and for that use none of this matters.</para>
    /// </summary>
    public class ImportedPageItem : BaseItem
    {
        /// <summary>
        /// The other PDF, carried here rather than pointed at - so a template can be one
        /// file, and can be handed to something with no file system at all.
        ///
        /// Bytes, and therefore base64 in the file. Which carries this type's warning,
        /// the same one <see cref="ImageSource.Data"/> has: it only looks like a base64
        /// string when a template is read as JSON, and putting it in a string gives
        /// "System.Byte[]", which travels perfectly well and draws nothing.
        /// </summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Where the other PDF is, resolved relative to the template that names it -
        /// never the working directory. Empty when the file travels in
        /// <see cref="Data"/>.
        /// </summary>
        public string SourcePath { get; set; } = "";

        /// <summary>
        /// Which page, counting from <b>one</b>.
        ///
        /// From one because a template is written by a person, and the third page of a
        /// document is the third page of it. The reader underneath counts from zero, as
        /// an index should, and the translation happens once at the seam.
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// How it fills the space it is given. <see cref="ImageScaling.Uniform"/> by
        /// default, which keeps the page's own proportions - the same rule and the same
        /// code as a picture, since it is the same question.
        /// </summary>
        public ImageScaling Scaling { get; set; } = ImageScaling.Uniform;

        /// <summary>
        /// What a reader should be told is here, since a copied page is a drawing to
        /// everything downstream: no text of ours, no structure of ours, and nothing that
        /// can be read aloud. PDF/UA asks a figure for one.
        /// </summary>
        public string? Alt
        {
            get => AltText;
            set => AltText = value;
        }

        /// <summary>
        /// The page once somebody able to read it has been handed over. Never serialised:
        /// it is not part of the template, it is what a reader made of it.
        /// </summary>
        [JsonIgnore]
        public IImportedPage? Resolved { get; internal set; }

        protected override BaseItem CreateEmpty() => new ImportedPageItem();

        protected override void CopyTo(BaseItem target)
        {
            base.CopyTo(target);
            var copy = (ImportedPageItem)target;
            copy.Data = Data;
            copy.SourcePath = SourcePath;
            copy.Page = Page;
            copy.Scaling = Scaling;

            // Carried across a clone on purpose: a repeated container clones its children
            // after the pages have been resolved, and reading the same file once per
            // record would be the difference between a report and an afternoon.
            copy.Resolved = Resolved;
        }
    }

    /// <summary>Another template brought into this one - its body spliced in where this item sits, or its sections appended after this one. Named by a path, by st://reference for the host to supply, or carried whole in TemplateData.</summary>
    public class SubTemplateItem : ContainerItem
    {
        /// <summary>
        /// Whether this brings across a block or a document. See
        /// <see cref="SubTemplateMode"/>; <see cref="SubTemplateMode.Splice"/> is what
        /// this element has always done and stays the default.
        /// </summary>
        public SubTemplateMode Mode { get; set; } = SubTemplateMode.Splice;

        /// <summary>
        /// Where the other template comes from. Three shapes, and which one is used is
        /// decided by what is filled in rather than by a mode anybody has to set:
        ///
        /// <list type="bullet">
        /// <item><b>A path</b> - resolved relative to the file that contains this item,
        /// not the working directory and not the file at the top of the chain, so a
        /// sub-template can be moved without updating whoever includes it. Held to a
        /// root the caller sets; see <see cref="SubTemplateExpander"/>.</item>
        /// <item><b><c>st://</c> and a reference</b> - a name only the caller can turn
        /// into a template, which is what a template kept in a database is. Nothing is
        /// read from disk, and the reference never reaches a file system: a caller with
        /// no source for it gets a refusal rather than a file called "st:".</item>
        /// <item><b>Nothing at all</b>, with <see cref="TemplateData"/> filled - the
        /// template travels inside this one.</item>
        /// </list>
        ///
        /// The last two exist because a file is exactly what a server and an editor do
        /// not have: both are handed JSON as text, with no directory to resolve anything
        /// relative against, so until now neither could splice a sub-template at all.
        /// </summary>
        public string TemplatePath { get; set; } = "";

        /// <summary>
        /// The other template's JSON, carried here rather than pointed at - so a
        /// template with sub-templates can be one file, and can be handed to something
        /// that has no file system at all.
        ///
        /// Bytes, and therefore base64 in the file, the same shape as
        /// <see cref="ImageSource.Data"/>. Which carries that type's warning: this is a
        /// <c>byte[]</c> and only looks like a base64 string when a template is read as
        /// JSON. Putting it in a string gives "System.Byte[]", which is valid enough to
        /// travel and draws nothing - see CLAUDE.md section 5a-bis, where that cost an
        /// afternoon on an image.
        /// </summary>
        public byte[] TemplateData { get; set; } = Array.Empty<byte>();

        public SubTemplateItem()
        {
            Layout = LayoutMode.Stack;   // flows like Body once its children are spliced in
        }

        protected override BaseItem CreateEmpty() => new SubTemplateItem();

        protected override void CopyTo(BaseItem target)
        {
            base.CopyTo(target);
            var copy = (SubTemplateItem)target;
            copy.Mode = Mode;
            copy.TemplatePath = TemplatePath;
            copy.TemplateData = TemplateData;
        }
    }

    /// <summary>
    /// One column of a table. A width in millimeters or in percent of the table is
    /// honoured as given; the columns left on <see cref="Auto"/> share what remains.
    /// </summary>
    public class TableColumn
    {
        /// <summary>The column's width, in WidthUnit. Unset, the column shares with the others what the fixed ones leave.</summary>
        [JsonIgnore]
        public double Width { get; set; } = double.NaN;

        /// <summary>
        /// The JSON face of <see cref="Width"/>. NaN is this engine's "not set", and a
        /// template says that best by not mentioning the property at all - so it maps to
        /// null, which the serializer then leaves out.
        /// </summary>
        [JsonPropertyName("Width")]
        public double? WidthJson
        {
            get => double.IsNaN(Width) ? null : Width;
            set => Width = value ?? double.NaN;
        }

        /// <summary>Millimeter, or Percentage of the table's width.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<Units>))]
        public Units WidthUnit { get; set; } = Units.Millimeter;

        /// <summary>
        /// Size the column to its widest cell instead of sharing the leftover space.
        /// Only meaningful when no explicit width is given.
        /// </summary>
        public bool Fit { get; set; }

        /// <summary>Floor for a fitted column, in millimeters. Zero means none.</summary>
        public double MinWidth { get; set; }

        /// <summary>Ceiling for a fitted column, in millimeters. Zero means none.</summary>
        public double MaxWidth { get; set; }

        /// <summary>True when no explicit width was given.</summary>
        [JsonIgnore]
        public bool Auto => double.IsNaN(Width);

        public TableColumn() { }

        public TableColumn(double width, Units unit = Units.Millimeter)
        {
            Width = width;
            WidthUnit = unit;
        }
    }

    /// <summary>
    /// One row. Its children are the cells, one per column, in order; a missing cell
    /// simply leaves that column empty on that row.
    /// </summary>
    public class TableRow : ContainerItem
    {
        /// <summary>Header rows are repeated at the top of every page the table spans.</summary>
        public bool IsHeader { get; set; }

        public TableRow() { }

        public TableRow(bool isHeader) => IsHeader = isHeader;

        protected override BaseItem CreateEmpty() => new TableRow();

        protected override void CopyTo(BaseItem target)
        {
            base.CopyTo(target);
            ((TableRow)target).IsHeader = IsHeader;
        }
    }

    /// <summary>
    /// A table. Its children are <see cref="TableRow"/> items, and it splits between
    /// rows when it does not fit: a row is never cut in half.
    /// </summary>
    public class TableItem : ContainerItem
    {
        /// <summary>The columns, left to right. Every row carries one cell per column.</summary>
        public List<TableColumn> Columns { get; set; } = new();

        /// <summary>Repeat the header rows at the top of each page the table continues on.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public bool RepeatHeader { get; set; } = true;

        /// <summary>
        /// Pen for the cell grid. Carried for the backend, which strokes the placed cell
        /// rectangles; the resolver only computes geometry.
        /// </summary>
        public PenStyle? Grid { get; set; }

        public TableItem()
        {
            Layout = LayoutMode.Stack;
        }

        public TableItem AddColumn(double width, Units unit = Units.Millimeter)
        {
            Columns.Add(new TableColumn(width, unit));
            return this;
        }

        /// <summary>Adds an auto-width column, sharing whatever the fixed columns leave.</summary>
        public TableItem AddAutoColumn()
        {
            Columns.Add(new TableColumn());
            return this;
        }

        /// <summary>
        /// Adds a column as wide as its widest cell, optionally held between bounds.
        /// What is left over still goes to the plain auto columns.
        /// </summary>
        public TableItem AddFittedColumn(double minWidth = 0, double maxWidth = 0)
        {
            Columns.Add(new TableColumn { Fit = true, MinWidth = minWidth, MaxWidth = maxWidth });
            return this;
        }

        public TableRow AddRow(params BaseItem[] cells) => AddRow(false, cells);

        public TableRow AddHeaderRow(params BaseItem[] cells) => AddRow(true, cells);

        protected override BaseItem CreateEmpty() => new TableItem();

        protected override void CopyTo(BaseItem target)
        {
            base.CopyTo(target);
            var copy = (TableItem)target;
            copy.RepeatHeader = RepeatHeader;
            copy.Grid = Grid?.Clone();
            copy.Columns.Clear();
            foreach (TableColumn column in Columns)
            {
                copy.Columns.Add(new TableColumn(column.Width, column.WidthUnit)
                {
                    Fit = column.Fit, MinWidth = column.MinWidth, MaxWidth = column.MaxWidth
                });
            }
        }

        private TableRow AddRow(bool header, params BaseItem[] cells)
        {
            var row = new TableRow(header);
            foreach (BaseItem cell in cells)
                row.Add(cell);
            Add(row);
            return row;
        }
    }
}
