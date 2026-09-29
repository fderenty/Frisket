using System.Text.Json.Serialization;

namespace Frisket
{
    [JsonConverter(typeof(JsonStringEnumConverter<HorizontalAlignment>))]
    public enum HorizontalAlignment { Left, Center, Right, Justify }

    [JsonConverter(typeof(JsonStringEnumConverter<VerticalAlignment>))]
    public enum VerticalAlignment { Top, Middle, Bottom }

    [JsonConverter(typeof(JsonStringEnumConverter<DashStyle>))]
    public enum DashStyle { Solid, Dash, Dot, DashDot }

    /// <summary>
    /// How <see cref="TextStyle.Size"/> is expressed.
    ///
    /// The point is the typographic em size, and the ratio between the em and the
    /// height a reader actually perceives varies a lot from one typeface to the next.
    /// Sizing in millimeters removes that variance: it pins the cap height, so 3 mm
    /// looks like 3 mm whatever the font.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<FontSizeUnit>))]
    public enum FontSizeUnit
    {
        /// <summary>Typographic points, em size. What backends ultimately want.</summary>
        Point,

        /// <summary>Millimeters of cap height - the height of a capital W.</summary>
        Millimeter,

        /// <summary>Multiple of <see cref="Report.RootFontSize"/>. Never circular.</summary>
        Rem,

        /// <summary>
        /// Percentage of the height of the item's own frame, as cap height. Only usable
        /// when that height is fixed independently of the text; the resolver falls back
        /// to <see cref="Rem"/> otherwise, since a frame sized from text that is sized
        /// from the frame has no solution.
        /// </summary>
        PercentOfFrame
    }

    /// <summary>
    /// Text appearance. <see cref="Size"/> is read through <see cref="SizeUnit"/>;
    /// the resolver converts it to points before anything measures or draws it.
    /// </summary>
    public sealed class TextStyle
    {
        // Every property is optional. Null means "not decided here", and the value comes
        // from Report.DefaultFont instead - so a template names its typeface once and can
        // be restyled by changing one line. Whatever neither settles falls back to the
        // built-in default named on each property.

        /// <summary>Typeface name; unset, then empty, resolves to the measurer's default family.</summary>
        public string? Family { get; set; }

        /// <summary>Size expressed in <see cref="SizeUnit"/>. Defaults to 1.</summary>
        public double? Size { get; set; }

        /// <summary>Defaults to <see cref="FontSizeUnit.Rem"/>.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<FontSizeUnit>))]
        public FontSizeUnit? SizeUnit { get; set; }

        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public bool? Underline { get; set; }
        public bool? Strikethrough { get; set; }

        /// <summary>Defaults to black.</summary>
        public Color? Color { get; set; }

        /// <summary>Defaults to <see cref="HorizontalAlignment.Left"/>.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<HorizontalAlignment>))]
        public HorizontalAlignment? HorizontalAlignment { get; set; }

        /// <summary>Defaults to <see cref="VerticalAlignment.Top"/>.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<VerticalAlignment>))]
        public VerticalAlignment? VerticalAlignment { get; set; }

        /// <summary>Defaults to true.</summary>
        public bool? Wrap { get; set; }

        /// <summary>
        /// Distance from one baseline to the next, as a multiple of the text's own size -
        /// that is, of its cap height, the thing a size in millimetres actually pins.
        /// Defaults to 1.5.
        ///
        /// Deliberately not a multiple of the em, and deliberately not the font's own
        /// line height: both vary by half from one typeface to the next, so a table row
        /// would change height simply because the section was restyled. Here the
        /// section decides the leading and every font obeys. A face with unusually long
        /// ascenders - a script, say - needs this raised, and the page shows it at once.
        /// </summary>
        public double? LineSpacing { get; set; }

        /// <summary>
        /// A copy of this style with anything left undecided taken from
        /// <paramref name="parent"/>. One level is enough: a text over the document's
        /// default. Applied by the resolver, so nothing downstream ever sees a gap.
        /// </summary>
        public TextStyle Over(TextStyle? parent)
        {
            if (parent == null)
                return this;

            TextStyle merged = Clone();
            merged.Family ??= parent.Family;
            merged.Size ??= parent.Size;
            merged.SizeUnit ??= parent.SizeUnit;
            merged.Bold ??= parent.Bold;
            merged.Italic ??= parent.Italic;
            merged.Underline ??= parent.Underline;
            merged.Strikethrough ??= parent.Strikethrough;
            merged.Color ??= parent.Color;
            merged.HorizontalAlignment ??= parent.HorizontalAlignment;
            merged.VerticalAlignment ??= parent.VerticalAlignment;
            merged.Wrap ??= parent.Wrap;
            merged.LineSpacing ??= parent.LineSpacing;
            return merged;
        }

        public const double PointsToMillimeters = 25.4 / 72.0;

        public TextStyle Clone() => (TextStyle)MemberwiseClone();

        /// <summary>
        /// The same style with an explicit point size. Measurers and backends only ever
        /// receive styles in this form.
        /// </summary>
        public TextStyle WithPointSize(double points)
        {
            TextStyle copy = Clone();
            copy.Size = points;
            copy.SizeUnit = FontSizeUnit.Point;
            return copy;
        }

        /// <summary>
        /// Em size in millimeters. Only meaningful once <see cref="SizeUnit"/> is
        /// <see cref="FontSizeUnit.Point"/>, which the resolver guarantees.
        /// </summary>
        [JsonIgnore]
        public double EmSizeInMillimeters => (Size ?? 1) * PointsToMillimeters;
    }

    public sealed class PenStyle
    {
        public PenStyle Clone() => (PenStyle)MemberwiseClone();

        public Color Color { get; set; } = Color.Black;

        /// <summary>Line thickness in millimeters.</summary>
        public double Width { get; set; } = 0.2;

        [JsonConverter(typeof(JsonStringEnumConverter<DashStyle>))]
        public DashStyle DashStyle { get; set; } = DashStyle.Solid;
    }

    public sealed class BrushStyle
    {
        public BrushStyle Clone() => (BrushStyle)MemberwiseClone();

        public Color Color { get; set; } = Color.White;
    }

    /// <summary>Raw image bytes; decoding is the backend's responsibility.</summary>
    public sealed class ImageSource
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string? SourcePath { get; set; }

        /// <summary>MIME type of <see cref="Data"/>, needed by backends that embed it.</summary>
        public string MediaType { get; set; } = "image/png";
    }
}
