using System.Text.Json.Serialization;

namespace Frisket
{
    /// <summary>
    /// Anything a template can put on a page. Every element is one of these, and what it
    /// declares here is what they all have in common whatever their kind: where the item
    /// sits and how it looks (Style), what it is for a reader who cannot see it (Role,
    /// AltText), whether it earns its place at all (ShowWhen, HideWhen), and where it lands
    /// in the outline (Bookmark).
    ///
    /// An item sits in exactly one container. Adding one that already has a Parent does
    /// nothing and returns false - a result nothing in this repository reads - so a rule
    /// put in the header and then in the footer appears in the header and is silently
    /// missing from the footer. Clone() is what makes the second one, which is why every
    /// place that moves an item somewhere else clones it first.
    /// </summary>
    [JsonDerivedType(typeof(BaseItem), typeDiscriminator: "BaseItem")]
    [JsonDerivedType(typeof(ContainerItem), typeDiscriminator: "ContainerItem")]
    [JsonDerivedType(typeof(TextItem), typeDiscriminator: "TextItem")]
    [JsonDerivedType(typeof(LineItem), typeDiscriminator: "LineItem")]
    [JsonDerivedType(typeof(ImageItem), typeDiscriminator: "ImageItem")]
    [JsonDerivedType(typeof(TableItem), typeDiscriminator: "TableItem")]
    [JsonDerivedType(typeof(TableRow), typeDiscriminator: "TableRow")]
    [JsonDerivedType(typeof(ImageGridItem), typeDiscriminator: "ImageGridItem")]
    [JsonDerivedType(typeof(SubTemplateItem), typeDiscriminator: "SubTemplateItem")]
    [JsonDerivedType(typeof(ImportedPageItem), typeDiscriminator: "ImportedPageItem")]
    [JsonDerivedType(typeof(AdvancedTextItem), typeDiscriminator: "AdvancedTextItem")]
    [JsonDerivedType(typeof(FieldItem), typeDiscriminator: "FieldItem")]
    [JsonDerivedType(typeof(BarcodeItem), typeDiscriminator: "BarcodeItem")]
    [JsonDerivedType(typeof(Header), typeDiscriminator: "Header")]
    [JsonDerivedType(typeof(Footer), typeDiscriminator: "Footer")]
    [JsonDerivedType(typeof(Body), typeDiscriminator: "Body")]
    [JsonDerivedType(typeof(Section), typeDiscriminator: "Section")]
    public class BaseItem
    {
        /// <summary>
        /// The report this item belongs to. A back-reference, so it is never serialized;
        /// <see cref="Report.Reattach()"/> rebuilds it from the shape of the tree.
        /// </summary>
        [JsonIgnore]
        public Report? Owner { get; internal set; }

        /// <summary>The container holding this item. A back-reference, never serialized.</summary>
        [JsonIgnore]
        public ContainerItem? Parent { get; internal set; }

        /// <summary>
        /// Puts an entry in the document's outline, pointing at wherever this item lands.
        /// Null for none.
        ///
        /// Tokens are filled from the data like any text, so a group heading reading
        /// <c>{Customer.Name}</c> gives one bookmark per customer - which is what makes a
        /// file holding two hundred invoices usable at all.
        /// </summary>
        public string? Bookmark { get; set; }

        /// <summary>
        /// How deep the entry sits: zero at the top, one nested under the last entry
        /// above it at zero, and so on. A level that skips a step is pulled back to one
        /// more than its predecessor, since an outline with a hole in it is not a tree.
        /// </summary>
        public int BookmarkLevel { get; set; }

        /// <summary>
        /// The language of this element and everything under it, as a BCP 47 tag
        /// ("de-DE", "fr"). Null - the usual case - means the file's, which is
        /// <see cref="Report.Language"/>.
        ///
        /// PDF/UA 7.2 asks that a passage in a language other than the document's be
        /// marked as such, because a reader announcing German words with English
        /// phonemes is not a cosmetic fault: it is unintelligible. It reaches the file
        /// as /Lang on this element's structure entry, which is where PDF puts it, and
        /// costs nothing when it says the same as the file.
        ///
        /// It exists because assembling documents made it unavoidable. A dossier built
        /// from self-contained pieces has one /Lang and the pieces may disagree, and
        /// there are only three things to do about that: lie, refuse, or say it where
        /// PDF already allows it to be said. See <see cref="SubTemplateMode.Append"/>.
        /// </summary>
        public string? Language { get; set; }

        /// <summary>
        /// What this element is, for a reader that cannot see it. Declared rather than
        /// guessed; see <see cref="PdfRole"/>.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<PdfRole>))]
        public PdfRole Role { get; set; } = PdfRole.Auto;

        // Where this item sits in the outline, level by level, once HeadingNumbers.Apply
        // has walked the tree. Fields rather than properties, and internal, because they
        // are worked out from the document and never authored - the serializer only sees
        // public properties, so there is nothing here for a template to contradict.
        internal int[]? HeadingCounters;
        internal int[]? SectionHeadingCounters;

        /// <summary>
        /// What to say in place of something that cannot be read - a logo, a barcode, a
        /// chart. Without it such an element is announced as nothing at all, which is
        /// worse than the picture being missing: the reader is not told anything is there.
        /// </summary>
        public string? AltText { get; set; }

        /// <summary>
        /// A path in the data. The item is dropped - <see cref="Visibility.Gone"/>, so it
        /// takes no room and nothing beneath it moves - unless the value at that path is
        /// worth showing: present, and not an empty string, not zero, not false.
        ///
        /// Wanted the first time a form was reproduced faithfully. A box that only a
        /// credit note fills, a label for a number that is not always there, a line under
        /// an address line that may not exist: without this the choice is a rule drawn
        /// through empty space or a template per case.
        ///
        /// Names the path plainly - <c>Client.Vat</c> - and braces are tolerated, since
        /// that is how the same path is written everywhere else on the item.
        /// </summary>
        public string? ShowWhen { get; set; }

        /// <summary>
        /// The other way round: dropped when the value <em>is</em> there. Both may be
        /// given, and either one hiding the item is enough.
        /// </summary>
        public string? HideWhen { get; set; }

        /// <summary>Where the item sits and how much room it takes: position and size, margin and padding, size bounds, visibility, page breaks and alignment.</summary>
        public Style Style { get; set; } = new();



        public BaseItem() { }

        /// <summary>
        /// A deep copy, detached from any parent. This is what lets a prototype row be
        /// stamped out once per record without the copies sharing anything.
        /// </summary>
        public BaseItem Clone()
        {
            BaseItem copy = CreateEmpty();
            copy.Style = Style.Clone();
            copy.Bookmark = Bookmark;
            copy.BookmarkLevel = BookmarkLevel;
            copy.Role = Role;
            copy.AltText = AltText;
            copy.Language = Language;
            copy.ShowWhen = ShowWhen;
            copy.HideWhen = HideWhen;
            CopyTo(copy);
            return copy;
        }

        protected virtual BaseItem CreateEmpty() => new();

        protected virtual void CopyTo(BaseItem target) { }
    }

    /// <summary>How a container positions its children.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<LayoutMode>))]
    public enum LayoutMode
    {
        /// <summary>Each child is positioned on its own, by anchors and coordinates.</summary>
        Absolute,

        /// <summary>
        /// Children are stacked vertically in declaration order, separated by their
        /// margins. This is the only mode that can paginate: absolute coordinates give
        /// no rule for deciding what moves to the next page.
        /// </summary>
        Stack
    }

    /// <summary>
    /// A group of items, placed either at coordinates of their own or stacked one under the
    /// next. Everything in the model that holds children is one of these - the bands, a
    /// table and its rows, an image grid, a section.
    ///
    /// An absolute container hands every child the same box rather than taking room away as
    /// it goes, and a child that states no position of its own fills all of it: two that
    /// say nothing land exactly on top of one another. Nor does it ever continue onto the
    /// next page - it is placed whole, and runs off the sheet if it must. Which containers
    /// avoid that by flowing is measured in test 70, not remembered here.
    /// </summary>
    public class ContainerItem : BaseItem
    {
        private readonly List<BaseItem> _childs = new();

        /// <summary>
        /// Always written, never inferred from its absence. The property starts at
        /// <see cref="LayoutMode.Absolute"/>, and the four containers that have to flow set
        /// themselves to <see cref="LayoutMode.Stack"/> in their constructors:
        /// <see cref="Body"/>, <see cref="TableItem"/>, <see cref="ImageGridItem"/> and
        /// <see cref="SubTemplateItem"/>. A <see cref="Header"/>, a <see cref="Footer"/>,
        /// a <see cref="Section"/> and a plain group keep the default.
        ///
        /// That matters because an absolute container hands every child the same box
        /// rather than taking room away as it goes, and a child that states no position of
        /// its own fills all of it - so two children of a band that says nothing land
        /// exactly on top of one another.
        ///
        /// This text has now been wrong twice. It said "Body, Header and Footer" for eight
        /// months, which was wrong about Header and Footer; the correction said "Body
        /// alone, and the other two have no constructor", which was wrong about all four -
        /// Header and Footer do have constructors, at the bottom of this file, setting Dock
        /// and not Layout. The second reading looked at a five-line window and generalised.
        /// A window is not a class.
        ///
        /// Left to the default rule the enum's zero would be dropped on the way out and
        /// a body laid out to fixed coordinates would come back stacked. Binding round
        /// trips the template through JSON, so this turned a whole section into a
        /// column of items with no error anywhere.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<LayoutMode>))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public LayoutMode Layout { get; set; } = LayoutMode.Absolute;

        /// <summary>
        /// How many columns the children flow into. One by default.
        ///
        /// Named ColumnCount rather than Columns because a table already has Columns,
        /// meaning its list of column widths - and a table is a container. Two different
        /// things under one name on the same inheritance chain is a fault waiting for
        /// somebody to write the wrong one; the compiler said so and the name changed.
        ///
        /// Only meaningful in <see cref="LayoutMode.Stack"/>: absolute children are
        /// placed by their own coordinates, and there is nothing for a column to decide.
        ///
        /// They fill in order rather than balancing - a page of terms and conditions
        /// wants its first column full before the second begins. What the last column
        /// cannot take goes to the next page, as it always did.
        /// </summary>
        public int ColumnCount { get; set; } = 1;

        /// <summary>Millimetres between one column and the next.</summary>
        public double ColumnGap { get; set; } = 6;

        /// <summary>
        /// Whether the last page of a column flow is evened out. On by default.
        ///
        /// Columns fill in order, so the page where the text runs out has its first
        /// column full and its last nearly empty, which looks like a mistake. Balancing
        /// finds the shallowest height that still holds everything and uses that, so
        /// the columns end together.
        ///
        /// It costs a handful of extra passes over that page alone - pages that
        /// overflow have no spare room and are left as they are. Turn it off for a
        /// column of items that must start at the very top whatever happens.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool BalanceColumns { get; set; } = true;

        /// <summary>
        /// Name of the collection in the data this container repeats over, such as
        /// "Lines". The binder stamps its children out once per record; until it runs the
        /// container is an ordinary one holding the prototype.
        ///
        /// In a <see cref="TableItem"/> the header rows stay put and the remaining rows
        /// are the prototypes, cycled - two of them give banded rows for free. In any
        /// other container the whole set of children is one prototype block.
        /// </summary>
        public string? RepeatOver { get; set; }

        /// <summary>Shown in place of the children when the collection turns out empty.</summary>
        public string? EmptyText { get; set; }

        /// <summary>
        /// Groups the collection by the value of this field before repeating, when the
        /// data arrives flat. Each pass then sees a synthetic record carrying
        /// <c>Key</c>, <c>Count</c>, a <c>Sum</c> of every numeric field - the subtotals -
        /// and the rows themselves under <see cref="GroupItems"/>, ready for a nested
        /// repeat. Leave it null when the data is already grouped.
        /// </summary>
        public string? GroupBy { get; set; }

        /// <summary>Name the grouped rows are published under. Defaults to "Items".</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public string GroupItems { get; set; } = "Items";

        /// <summary>What this holds, in order. In a stacked container it is the order things come down the page; in an absolute one, the order they are drawn, each over the last. A table holds its rows here, and a row its cells, one per column.</summary>
        [JsonIgnore]
        public IReadOnlyList<BaseItem> Childs => _childs;

        /// <summary>
        /// The children, as the serializer sees them. Assigning re-attaches every item
        /// through <see cref="Add"/>, which is what restores Parent and Owner after a
        /// round trip - they are not serialized, being the tree's own back-references.
        /// </summary>
        [JsonPropertyName("Childs")]
        public List<BaseItem> Items
        {
            get => _childs;
            set
            {
                foreach (BaseItem child in _childs.ToArray())
                    Remove(child);

                if (value == null)
                    return;

                foreach (BaseItem child in value)
                    Add(child);
            }
        }

        /// <summary>
        /// Attaches an item. A subtree can be built standalone and added afterwards:
        /// ownership is propagated down when it joins a report.
        /// </summary>
        public bool Add(BaseItem item)
        {
            if (item is null || item.Parent != null || ReferenceEquals(item, this))
                return false;

            item.Parent = this;
            SetOwner(item, Owner);
            _childs.Add(item);
            return true;
        }

        public void Remove(BaseItem item)
        {
            if (item is null || !_childs.Remove(item))
                return;

            item.Parent = null;
            SetOwner(item, null);
        }

        protected override BaseItem CreateEmpty() => new ContainerItem();

        protected override void CopyTo(BaseItem target)
        {
            var copy = (ContainerItem)target;
            copy.Layout = Layout;
            copy.ColumnCount = ColumnCount;
            copy.ColumnGap = ColumnGap;
            copy.BalanceColumns = BalanceColumns;
            copy.RepeatOver = RepeatOver;
            copy.EmptyText = EmptyText;
            copy.GroupBy = GroupBy;
            copy.GroupItems = GroupItems;
            foreach (BaseItem child in _childs)
                copy.Add(child.Clone());
        }

        internal static void SetOwner(BaseItem item, Report? owner)
        {
            item.Owner = owner;
            if (item is ContainerItem container)
            {
                foreach (BaseItem child in container.Childs)
                    SetOwner(child, owner);
            }
        }

    }

    /// <summary>
    /// A piece of text in one style - a label, a paragraph, a cell of a table.
    ///
    /// Where it breaks into lines is settled at layout and never at drawing: the backends
    /// split on newlines and wrap nothing. A stacked flow hands the text its column and it
    /// wraps to that; pinned to coordinates it wraps only to a width the author stated - a
    /// Width, or both edges - and a text given a left mark and nothing else is measured at
    /// infinite width and drawn as one line, across whatever sits beside it and off the
    /// sheet, unclipped and unremarked.
    /// </summary>
    public class TextItem : BaseItem
    {
        /// <summary>What is printed. Tokens in braces are filled in: {Page}, {Pages} and {Title} by the engine, a path such as {Client.Name} from the data. {{ and }} print a brace, and a line break in the string breaks the line.</summary>
        public string Text { get; set; } = "";

        /// <summary>Appearance of the text, and what the resolver measures it with.</summary>
        public TextStyle Font { get; set; } = new();

        public TextItem() { }

        protected override BaseItem CreateEmpty() => new TextItem();

        protected override void CopyTo(BaseItem target)
        {
            var copy = (TextItem)target;
            copy.Text = Text;
            copy.Font = Font.Clone();
        }
    }

    /// <summary>
    /// One piece of an <see cref="AdvancedTextItem"/>, in the item's own base style
    /// except where <see cref="Font"/> says otherwise. Null there means every property
    /// is inherited - the common case, a run of plain text between two styled words.
    /// </summary>
    public class TextRun
    {
        /// <summary>This run's words. Tokens in braces are filled in as in a TextItem, and a line break in the string breaks the line.</summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Overrides on top of the item's own <see cref="AdvancedTextItem.Font"/>, which
        /// is itself a normal cascade onto <see cref="Report.DefaultFont"/> - the same
        /// three-level resolution a plain <see cref="TextItem"/> already goes through,
        /// with one more level on top. A run does not set alignment, wrapping or line
        /// spacing: those describe the paragraph, not a piece of it.
        /// </summary>
        public TextStyle? Font { get; set; }
    }

    /// <summary>
    /// A paragraph of runs, each with its own style - what a plain <see cref="TextItem"/>
    /// cannot say, since it carries exactly one <see cref="TextStyle"/> for its whole
    /// string. Wrapping and pagination both work at the level of a whole *line*, built
    /// from pieces of one or more runs, not at the level of a run - see
    /// <see cref="LayoutResolver.FlowAdvancedText"/>.
    /// </summary>
    public class AdvancedTextItem : BaseItem
    {
        /// <summary>The paragraph's pieces, in reading order, each with its own style over the paragraph's.</summary>
        public List<TextRun> Runs { get; set; } = new();

        /// <summary>
        /// The paragraph's own style: family, size, alignment, line spacing. A run
        /// overrides pieces of this the way this overrides <see cref="Report.DefaultFont"/>.
        /// </summary>
        public TextStyle Font { get; set; } = new();

        protected override BaseItem CreateEmpty() => new AdvancedTextItem();

        protected override void CopyTo(BaseItem target)
        {
            var copy = (AdvancedTextItem)target;
            copy.Font = Font.Clone();
            copy.Runs = Runs.Select(r => new TextRun { Text = r.Text, Font = r.Font?.Clone() }).ToList();
        }
    }

    /// <summary>
    /// The band across the top of every page of its section. It sizes itself from what it
    /// holds, and the body band starts where it ends.
    ///
    /// It does not stack. Its constructor sets Dock and never touches Layout, so a header
    /// left as it was built composes in Absolute, where a child that states no coordinates
    /// is handed the whole band. Two lines of an address then print one over the other, in
    /// a band as tall as one of them, and nothing anywhere says so.
    /// </summary>
    public class Header : ContainerItem
    {
        protected override BaseItem CreateEmpty() => new Header();

        public Header() 
        {
            Style.Location.Dock = Dock.Top | Dock.Left | Dock.Right;
        }
    }

    /// <summary>
    /// The band across the bottom of every page of its section - page numbers, a notice,
    /// the totals. It sizes itself from what it holds, and the body band ends where it
    /// begins.
    ///
    /// Like Header, it does not stack: its constructor sets Dock and never Layout, so a
    /// footer left as it was built hands every child that states no coordinates the whole
    /// band. A notice and a page number written as two children come out on one line, over
    /// one another, and nothing anywhere says so.
    /// </summary>
    public class Footer : ContainerItem
    {
        protected override BaseItem CreateEmpty() => new Footer();

        public Footer() 
        {
            Style.Location.Dock = Dock.Bottom | Dock.Left | Dock.Right;
        }
    }

    /// <summary>
    /// The band between the header and the footer, and the part of a section that flows
    /// from one page to the next.
    ///
    /// Flowing is Layout being Stack, which Body sets for itself in its constructor. Set it
    /// to Absolute and the section comes out one page however much is in it: the children
    /// are arranged at their coordinates on that single page, whatever falls below the band
    /// is drawn off the sheet, and no further page is opened and no issue raised.
    /// </summary>
    public class Body : ContainerItem
    {
        protected override BaseItem CreateEmpty() => new Body();

        public Body()
        {
            Style.Location.Dock = Dock.Fill;
            Layout = LayoutMode.Stack;   // the body is what flows from page to page
        }
    }

    /// <summary>
    /// One section of a report: a page setup and the bands laid out on it.
    ///
    /// A report holds a *list* of these, in order, which is what lets one file mix an
    /// A4 portrait body with an A3 landscape appendix - and what lets separate
    /// sections be assembled into one. PDF works the same way: the page size is
    /// <c>/MediaBox</c> on each page object, not a property of the file.
    ///
    /// **Nothing flows from one document into the next.** Content that does not fit
    /// adds pages within its own section; it never spills into the following one.
    /// That is a deliberate limit rather than an omission: it makes the one genuinely
    /// hard case - a table splitting between two page sizes and changing width
    /// mid-continuation - inexpressible rather than merely discouraged.
    ///
    /// A `Section` inside a `Body` means nothing and cannot be drawn. The type system
    /// permits it because a document is an item like any other; the validator is what
    /// forbids it.
    /// </summary>
    public class Section : ContainerItem
    {
        /// <summary>Used by the serializer; the owner is restored by <see cref="Report.Reattach()"/>.</summary>
        public Section() { }

        public Section(Report owner) => Owner = owner;

        /// <summary>
        /// The sheet this section is laid out on. There is no default size, and nothing
        /// supplies one: a section that omits this lays out on a sheet of nothing - pages
        /// come out 0 by 0, every column is too narrow to divide, and the flow puts one
        /// item on each page and drops the rest, silently.
        ///
        /// The three lines this replaces ended "which is why Report gives a new one A4
        /// rather than leaving it at zero". No such assignment has ever existed - Width
        /// and Height are plain doubles - so the sentence contradicted its own first
        /// clause, and nothing could tell it so until the documentation file was turned
        /// on and a generated page was going to publish it.
        /// </summary>
        public DocumentDimension Dimension { get; set; } = new DocumentDimension();

        /// <summary>
        /// How many pages this section is *meant* to take, or null for no expectation.
        ///
        /// Purely declarative: exceeding it is reported through
        /// <see cref="LayoutResult.Issues"/> and nothing is refused. Whether to go on
        /// and produce the section belongs to whoever asked for it, which is why the
        /// engine states the fact rather than acting on it - lay out first, decide
        /// after, which <see cref="Report.Resolve(ITextMeasurer, CancellationToken)"/> has always allowed.
        ///
        /// Not to be confused with <c>LayoutResolver.MaxPages</c>, which is a runaway
        /// guard against a layout that never converges and says so in those words.
        /// </summary>
        public int? PageLimit { get; set; }

        protected override BaseItem CreateEmpty() => new Section();

        protected override void CopyTo(BaseItem target)
        {
            base.CopyTo(target);
            var copy = (Section)target;
            copy.Dimension = new DocumentDimension
            {
                Width = Dimension.Width,
                Height = Dimension.Height
            };
            copy.PageLimit = PageLimit;
        }
    }
}
