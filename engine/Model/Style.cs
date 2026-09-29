using System.Text.Json.Serialization;

namespace Frisket
{
    /// <summary>
    /// The edges an item is stuck to. This is docking, in the sense of Delphi's Align or
    /// WinForms' Dock, and not the distance-keeping their "anchors" mean: a parent in a
    /// report is never resized, so "keep 10mm from the right edge" and "stick to the
    /// right edge" cannot be told apart when the edge never moves.
    ///
    /// Stuck to one edge, an item sits against it. Stuck to both edges of an axis, it
    /// stretches across. Stuck to none, its size and <see cref="Style.AlignHorizontal"/>
    /// decide where it lands.
    /// </summary>
    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter<Dock>))]
    public enum Dock
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,

        /// <summary>All four: the item takes everything it was given.</summary>
        Fill = Left | Top | Right | Bottom
    }

    [JsonConverter(typeof(JsonStringEnumConverter<Visibility>))]
    public enum Visibility { Visible, Invisible, Gone }

    [JsonConverter(typeof(JsonStringEnumConverter<Units>))]
    public enum Units { Millimeter, Percentage,  }

    /// <summary>
    /// Where an item actually landed once the layout has run: four edges as absolute
    /// millimetres across and down the sheet - not the insets the same four names mean on
    /// FrameDimension - with Width and Height derived from them, the visibility it was laid
    /// out with, and NaN wherever the resolver settled nothing.
    ///
    /// It holds one placement, and an item can be placed many times. Each placement
    /// overwrites it edge by edge, so a paragraph split over three pages, a header row
    /// repeated on every continuation and a band re-arranged for each sheet all read back
    /// as wherever they landed last. Nothing is drawn from it either - a backend is given
    /// the flat list of placed items - so it answers where something ended up only for
    /// something that ended up in one place.
    /// </summary>
    public class ReadOnlyFrameDimension
    {
        double _Left = double.NaN;
        double _Top = double.NaN;
        double _Right = double.NaN;
        double _Bottom = double.NaN;

        public double Left { get { return _Left; } internal set { _Left = value; LeftCalculated = true; } }
        public double Top { get { return _Top; } internal set { _Top = value; TopCalculated = true; } }
        public double Right { get { return _Right; } internal set { _Right = value; RightCalculated = true; } }
        public double Bottom { get { return _Bottom; } internal set { _Bottom = value; BottomCalculated = true; } }

        public double Width => (WidthCalculated == true) ? Right - Left : double.NaN;
        public double Height => (HeightCalculated == true) ? Bottom - Top : double.NaN;

        public Visibility Visibility { get; internal set; }

        internal bool LeftCalculated;
        internal bool TopCalculated;
        internal bool RightCalculated;
        internal bool BottomCalculated;
        internal bool WidthCalculated => (LeftCalculated == true && RightCalculated == true);
        internal bool HeightCalculated => (TopCalculated == true && BottomCalculated == true);

        public ReadOnlyFrameDimension()
        {
            ClearCalculated();
        }

        public void ClearCalculated()
        {
            LeftCalculated = false;
            TopCalculated = false;
            RightCalculated = false;
            BottomCalculated = false;
        }

        /// <summary>
        /// Clears the flags *and* the stale coordinates, so an edge that the resolver
        /// leaves undetermined reads back as NaN instead of the previous run's value.
        /// </summary>
        internal void Reset()
        {
            _Left = double.NaN;
            _Top = double.NaN;
            _Right = double.NaN;
            _Bottom = double.NaN;
            ClearCalculated();
        }

    }

    /// <summary>
    /// The size of a sheet, in millimetres: a width and a height, with neither axis
    /// privileged - landscape is nothing more than the larger number written first.
    ///
    /// Both start at zero and nothing supplies a page size - not the engine, not the CLI,
    /// not the native library. A section that leaves them there is laid out on a sheet of
    /// nothing: /MediaBox [0 0 0 0], every column too narrow to divide, one item forced
    /// onto each page. The designer writes A4 into any section that arrives without one,
    /// which is why this is only ever met by a template built by hand.
    /// </summary>
    public class DocumentDimension
    {
        public double Width { get; set; }
        public double Height { get; set; }

    }

    /// <summary>
    /// Where an item sits in the space its container gave it: Left and Top measured in from
    /// the near edge, Right and Bottom in from the far one, a width and a height beside
    /// them, each in millimetres or as a percentage of that same space, and each of the six
    /// unset - NaN, never zero - until the template states it.
    ///
    /// In a stacked container, which is what a body is by default, Top and Bottom decide
    /// nothing. The vertical position there is the cursor: Left, Right, Width and Height
    /// are all honoured, the other two belong to the absolute path alone, and Margin.Top is
    /// what moves an item down. The coordinates stay in the file, the page ignores them,
    /// and nothing says so.
    /// </summary>
    public class FrameDimension
    {

        [JsonIgnore]
        public double LeftValue { get; set; } = double.NaN;

        /// <summary>
        /// The JSON face of <see cref="LeftValue"/>. NaN is this engine's "not set", and a
        /// template says that best by not mentioning the property at all - so it maps to
        /// null, which the serializer then leaves out.
        /// </summary>
        [JsonPropertyName("LeftValue")]
        public double? LeftJson
        {
            get => double.IsNaN(LeftValue) ? null : LeftValue;
            set => LeftValue = value ?? double.NaN;
        }

        [JsonConverter(typeof(JsonStringEnumConverter<Units>))]
        public Units LeftUnit { get; set; } = Units.Millimeter;

        [JsonIgnore]
        public double RightValue { get; set; } = double.NaN;

        /// <summary>
        /// The JSON face of <see cref="RightValue"/>. NaN is this engine's "not set", and a
        /// template says that best by not mentioning the property at all - so it maps to
        /// null, which the serializer then leaves out.
        /// </summary>
        [JsonPropertyName("RightValue")]
        public double? RightJson
        {
            get => double.IsNaN(RightValue) ? null : RightValue;
            set => RightValue = value ?? double.NaN;
        }

        [JsonConverter(typeof(JsonStringEnumConverter<Units>))]
        public Units RightUnit { get; set; } = Units.Millimeter;

        [JsonIgnore]
        public double WidthValue { get; set; } = double.NaN;

        /// <summary>
        /// The JSON face of <see cref="WidthValue"/>. NaN is this engine's "not set", and a
        /// template says that best by not mentioning the property at all - so it maps to
        /// null, which the serializer then leaves out.
        /// </summary>
        [JsonPropertyName("WidthValue")]
        public double? WidthJson
        {
            get => double.IsNaN(WidthValue) ? null : WidthValue;
            set => WidthValue = value ?? double.NaN;
        }

        [JsonConverter(typeof(JsonStringEnumConverter<Units>))]
        public Units WidthUnit { get; set; } = Units.Millimeter;


        [JsonIgnore]
        public double TopValue { get; set; } = double.NaN;

        /// <summary>
        /// The JSON face of <see cref="TopValue"/>. NaN is this engine's "not set", and a
        /// template says that best by not mentioning the property at all - so it maps to
        /// null, which the serializer then leaves out.
        /// </summary>
        [JsonPropertyName("TopValue")]
        public double? TopJson
        {
            get => double.IsNaN(TopValue) ? null : TopValue;
            set => TopValue = value ?? double.NaN;
        }

        [JsonConverter(typeof(JsonStringEnumConverter<Units>))]
        public Units TopUnit { get; set; } = Units.Millimeter;

        [JsonIgnore]
        public double BottomValue { get; set; } = double.NaN;

        /// <summary>
        /// The JSON face of <see cref="BottomValue"/>. NaN is this engine's "not set", and a
        /// template says that best by not mentioning the property at all - so it maps to
        /// null, which the serializer then leaves out.
        /// </summary>
        [JsonPropertyName("BottomValue")]
        public double? BottomJson
        {
            get => double.IsNaN(BottomValue) ? null : BottomValue;
            set => BottomValue = value ?? double.NaN;
        }

        [JsonConverter(typeof(JsonStringEnumConverter<Units>))]
        public Units BottomUnit { get; set; } = Units.Millimeter;

        [JsonIgnore]
        public double HeightValue { get; set; } = double.NaN;

        /// <summary>
        /// The JSON face of <see cref="HeightValue"/>. NaN is this engine's "not set", and a
        /// template says that best by not mentioning the property at all - so it maps to
        /// null, which the serializer then leaves out.
        /// </summary>
        [JsonPropertyName("HeightValue")]
        public double? HeightJson
        {
            get => double.IsNaN(HeightValue) ? null : HeightValue;
            set => HeightValue = value ?? double.NaN;
        }

        [JsonConverter(typeof(JsonStringEnumConverter<Units>))]
        public Units HeightUnit { get; set; } = Units.Millimeter;



        public bool AutoWidth { get; set; } = false;
        public bool AutoHeight { get; set; } = false;

        [JsonIgnore]
        public (double, Units) Left { get { return (LeftValue, LeftUnit); } set { LeftValue = value.Item1; LeftUnit = value.Item2; } }

        [JsonIgnore]
        public (double, Units) Right { get { return (RightValue, RightUnit); } set { RightValue = value.Item1;RightUnit = value.Item2; } }

        [JsonIgnore]
        public (double, Units) Width { get { return (WidthValue, WidthUnit); } set { WidthValue = value.Item1; WidthUnit = value.Item2; } }


        [JsonIgnore]
        public (double, Units) Top { get { return (TopValue, TopUnit); } set { TopValue = value.Item1; TopUnit = value.Item2; } }

        [JsonIgnore]
        public (double, Units) Bottom { get { return (BottomValue, BottomUnit); } set { BottomValue = value.Item1; BottomUnit = value.Item2; } }

        [JsonIgnore]
        public (double, Units) Height { get { return (HeightValue, HeightUnit); } set { HeightValue = value.Item1; HeightUnit = value.Item2; } }

        [JsonConverter(typeof(JsonStringEnumConverter<Dock>))]
        public Dock Dock { get; set; } = Dock.None;

        /// <summary>
        /// Nothing positioned, nothing sized, nothing docked. Units are not consulted:
        /// a unit without a value beside it decides nothing.
        /// </summary>
        [JsonIgnore]
        public bool IsEmpty =>
            double.IsNaN(LeftValue) && double.IsNaN(RightValue) && double.IsNaN(WidthValue) &&
            double.IsNaN(TopValue) && double.IsNaN(BottomValue) && double.IsNaN(HeightValue) &&
            !AutoWidth && !AutoHeight && Dock == Dock.None;

        public FrameDimension Clone()
        {
            return new()
            {
                Left = Left,
                Right = Right,
                Width = Width,
                Top = Top,
                Bottom = Bottom,
                Height = Height,
                AutoWidth = AutoWidth,
                AutoHeight = AutoHeight,
                Dock = Dock,
            };
        }
    }

    /// <summary>
    /// Four distances in millimetres, one per side, serving both as the margin outside an
    /// item frame and as the padding inside it.
    ///
    /// Which of the two a band reads is settled rather than a matter of taste: the section
    /// and the body are handed their rectangle, and the resolver deflates it by their
    /// Padding without ever looking at their Margin. Page margins are a section padding;
    /// written as a margin, the same four numbers do nothing at all.
    /// </summary>
    public class EdgeSize
    {
        public EdgeSize Clone() => (EdgeSize)MemberwiseClone();

        public double Left { get; set; }
        public double Top { get; set; }
        public double Right { get; set; }
        public double Bottom { get; set; }

        /// <summary>Nothing set on any side, so nothing worth writing down.</summary>
        [JsonIgnore]
        public bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
    }

    /// <summary>
    /// Lower and upper bounds in millimetres on the size the resolver arrives at. Zero on a
    /// side means no bound there, so a Constraints nobody has touched changes nothing.
    ///
    /// A clamp keeps the edge the item is stuck to and moves the opposite one - and in the
    /// flow an item that states no coordinates is stuck to all four. A MaxWidth of 40 in a
    /// band of 210 therefore moves the left edge and the item lands against the right
    /// margin; a MinHeight grows the box upward, over what is already on the page. The flow
    /// cursor hears about neither, so nothing below it moves to match.
    /// </summary>
    public class Constraints
    {
        public Constraints Clone() => (Constraints)MemberwiseClone();

        public double MinWidth { get; set; }
        public double MinHeight { get; set; }
        public double MaxWidth { get; set; }
        public double MaxHeight { get; set; }

        /// <summary>No bound on any side.</summary>
        [JsonIgnore]
        public bool IsEmpty => MinWidth == 0 && MinHeight == 0 && MaxWidth == 0 && MaxHeight == 0;
    }

    /// <summary>
    /// Where an item sits in the space it was given, as opposed to where its text sits
    /// inside the item. It only has a say when the item has a size of its own and was
    /// not already pinned on that axis: an anchor or a coordinate always wins.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ItemAlign>))]
    public enum ItemAlign
    {
        /// <summary>Wherever the anchors and coordinates put it.</summary>
        None,

        /// <summary>Against the left, or the top.</summary>
        Start,

        /// <summary>Halfway.</summary>
        Center,

        /// <summary>Against the right, or the bottom.</summary>
        End
    }

    /// <summary>
    /// The box every item carries: where it sits and how big it may be, the space around it
    /// and inside it, whether it shows, what it does when the page runs out beneath it, and
    /// the rule and fill drawn on its frame.
    ///
    /// What is left unsaid is not left alone. An item with no coordinate, no width and no
    /// alignment is anchored to both edges of its band before layout begins, and comes out
    /// the full width of the column - so a one-word label given a Background paints a
    /// stripe right across the page. The item really is that wide, and nothing says so.
    /// </summary>
    public class Style
    {
        internal FrameDimension Location4Calc { get; set; } = new();

        // These four always exist at runtime, so code can read Style.Margin.Left without
        // a null check. In a template they are the opposite: an empty one says nothing,
        // and four empty objects on every item was a third of the file. They map to null
        // when empty, and the serializer drops them.

        [JsonIgnore]
        public FrameDimension Location { get; set; } = new();

        [JsonPropertyName("Location")]
        public FrameDimension? LocationJson
        {
            get => Location.IsEmpty ? null : Location;
            set => Location = value ?? new();
        }

        [JsonIgnore]
        public EdgeSize Margin { get; set; } = new();

        [JsonPropertyName("Margin")]
        public EdgeSize? MarginJson
        {
            get => Margin.IsEmpty ? null : Margin;
            set => Margin = value ?? new();
        }

        [JsonIgnore]
        public EdgeSize Padding { get; set; } = new();

        [JsonPropertyName("Padding")]
        public EdgeSize? PaddingJson
        {
            get => Padding.IsEmpty ? null : Padding;
            set => Padding = value ?? new();
        }

        [JsonIgnore]
        public Constraints Constraints { get; set; } = new();

        [JsonPropertyName("Constraints")]
        public Constraints? ConstraintsJson
        {
            get => Constraints.IsEmpty ? null : Constraints;
            set => Constraints = value ?? new();
        }

        [JsonIgnore]
        public ReadOnlyFrameDimension RenderedFrame { get; set; } = new();

        [JsonConverter(typeof(JsonStringEnumConverter<Visibility>))]
        public Visibility Visibility { get; set; } = Visibility.Visible;

        /// <summary>
        /// Author-requested page break around this item.
        /// Declared, but inert until the pagination pass exists.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<PageBreak>))]
        public PageBreak PageBreak { get; set; } = PageBreak.None;

        /// <summary>What to do when the item does not fit in the space left on the page.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<Splitting>))]
        public Splitting Splitting { get; set; } = Splitting.Auto;

        /// <summary>
        /// Do not let the page end immediately after this item.
        /// </summary>
        /// <remarks>
        /// The cure for a heading stranded at the foot of a page while what it announces
        /// begins on the next - the oldest fault in typesetting, and one this engine
        /// produced in its own conformance document.
        ///
        /// Its own property rather than a third <see cref="Splitting"/> value, because
        /// the two are orthogonal and an enum would force a choice between them. "Do not
        /// split me" and "do not leave me at the bottom" are different requests, and a
        /// block of heading, rule and caption wants both. An enum growing by combinations
        /// is the start of a matrix nobody keeps up to date.
        ///
        /// It is a request, not a guarantee. Nothing is pushed back past the first item
        /// on a page: moving what is already at the top achieves nothing, and without
        /// that floor a run of items all asking for it would push each other forever.
        /// </remarks>
        public bool KeepWithNext { get; set; }

        /// <summary>
        /// Which pages this item shows on. Declared, but inert until the pagination
        /// pass exists; <see cref="PageScope.Range"/> reads <see cref="PageRange"/>.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<PageScope>))]
        public PageScope PageScope { get; set; } = PageScope.All;

        /// <summary>Explicit page numbers, 1-based, used when <see cref="PageScope"/> is Range.</summary>
        public List<int>? PageRange { get; set; }

        /// <summary>
        /// Where the item sits across the space it was given. Answers the question text
        /// alignment cannot: how to centre an image, a table or a field. It applies only
        /// when the item has a width of its own and neither an anchor nor a coordinate
        /// has already placed it.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<ItemAlign>))]
        public ItemAlign AlignHorizontal { get; set; } = ItemAlign.None;

        /// <summary>
        /// Where the item sits down the space it was given. Only meaningful inside an
        /// absolutely arranged container: in a flow the vertical position is the cursor.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<ItemAlign>))]
        public ItemAlign AlignVertical { get; set; } = ItemAlign.None;

        /// <summary>
        /// Re-emit this item at the top of the next page when the container holding it
        /// runs over. It is what a table's repeating header does, made available to any
        /// stacked container - so a group header stays above its rows when the group
        /// spills onto the following page.
        /// </summary>
        public bool RepeatOnBreak { get; set; }

        /// <summary>Outline stroked around the frame. Carried for the backend; the resolver ignores it.</summary>
        public PenStyle? Border { get; set; }

        /// <summary>Fill behind the frame. Carried for the backend; the resolver ignores it.</summary>
        public BrushStyle? Background { get; set; }

        /// <summary>A deep copy. The rendered frame is left out: it is a result, not a setting.</summary>
        public Style Clone() => new()
        {
            Location = Location.Clone(),
            Margin = Margin.Clone(),
            Padding = Padding.Clone(),
            Constraints = Constraints.Clone(),
            Visibility = Visibility,
            AlignHorizontal = AlignHorizontal,
            AlignVertical = AlignVertical,
            RepeatOnBreak = RepeatOnBreak,
            PageBreak = PageBreak,
            Splitting = Splitting,
            KeepWithNext = KeepWithNext,
            PageScope = PageScope,
            PageRange = PageRange == null ? null : new List<int>(PageRange),
            Border = Border?.Clone(),
            Background = Background?.Clone()
        };

        /// <summary>
        /// Fills in what the author left unsaid.
        ///
        /// <paramref name="canSizeToContent"/> tells it the item has content to measure.
        /// Without it, a band anchored to one edge and given no height stretches to the
        /// opposite edge - which is right for a plain frame and wrong for a header or a
        /// footer, which should hug what they hold.
        ///
        /// This text sat above Clone() until the documentation file was turned on, which
        /// is the only thing that has ever read it: Clone() carried two summaries and a
        /// paramref for an argument it does not take, and the method the prose was
        /// written for had none at all.
        /// </summary>
        public void SetDefault4Calc(bool canSizeToContent = false)
        {
            Location4Calc = Location.Clone();

            // Horizontal dimension logic
            bool hasLeftValue = !double.IsNaN(Location4Calc.LeftValue);
            bool hasRightValue = !double.IsNaN(Location4Calc.RightValue);
            bool hasWidthValue = !double.IsNaN(Location4Calc.WidthValue);
            bool hasLeft = (Location4Calc.Dock & Dock.Left) != 0;
            bool hasRight = (Location4Calc.Dock & Dock.Right) != 0;

            if (!hasLeftValue && !hasRightValue && !hasWidthValue)
            {
                // No explicit horizontal values
                if (!hasLeft && !hasRight)
                {
                    // No anchors either: default to auto-width filling from left to right.
                    //
                    // Unless an alignment was asked for, which is itself a statement that
                    // the item does not fill: there is nothing to centre in a box the item
                    // already spans. Stretching first and aligning afterwards is how
                    // AlignHorizontal came to be a legal value that changed nothing.
                    Location4Calc.AutoWidth = true;
                    if (AlignHorizontal == ItemAlign.None)
                        Location4Calc.Dock |= Dock.Left | Dock.Right;
                    else
                        Location4Calc.Dock |= Dock.Left;
                }
                else if (hasLeft && !hasRight)
                {
                    // Only left anchor: fill to right
                    Location4Calc.Dock |= Dock.Right;
                }
                else if (hasRight && !hasLeft)
                {
                    // Only right anchor: fill from left
                    Location4Calc.Dock |= Dock.Left;
                }
                // If both anchors already set, keep as is
            }
            else if (hasRightValue && !hasLeftValue && !hasWidthValue)
            { 
                // Only right value specified: position from right edge
                if (hasRight && Location4Calc.AutoWidth)
                {
                    Location4Calc.Dock &= ~Dock.Right;
                }
            }

            // Vertical dimension logic
            bool hasTopValue = !double.IsNaN(Location4Calc.TopValue);
            bool hasBottomValue = !double.IsNaN(Location4Calc.BottomValue);
            bool hasHeightValue = !double.IsNaN(Location4Calc.HeightValue);
            bool hasTop = (Location4Calc.Dock & Dock.Top) != 0;
            bool hasBottom = (Location4Calc.Dock & Dock.Bottom) != 0;

            if (!hasTopValue && !hasBottomValue && !hasHeightValue)
            {
                // No explicit vertical values
                Location4Calc.AutoHeight = true;

                if (!hasTop && !hasBottom)
                {
                    // No anchors either: fill from top to bottom, unless there is content
                    // whose height can answer the question - or an alignment, which says
                    // the item is meant to sit somewhere in the space rather than be it.
                    if (!canSizeToContent && AlignVertical == ItemAlign.None)
                        Location4Calc.Dock |= Dock.Top | Dock.Bottom;
                    else
                        Location4Calc.Dock |= Dock.Top;
                }
                else if (hasTop && !hasBottom)
                {
                    if (!canSizeToContent)
                        Location4Calc.Dock |= Dock.Bottom;   // stretch to the bottom
                }
                else if (hasBottom && !hasTop)
                {
                    if (!canSizeToContent)
                        Location4Calc.Dock |= Dock.Top;       // stretch from the top
                }
                // If both anchors already set, keep as is
            }
            else if (hasBottomValue && !hasTopValue && !hasHeightValue)
            { 
                // Only bottom value specified: position from bottom edge
                if (hasBottom && Location4Calc.AutoHeight)
                {
                    Location4Calc.Dock &= ~Dock.Bottom;
                }
            }
        }
    }
}
