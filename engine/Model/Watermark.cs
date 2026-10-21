using System.Text.Json.Serialization;

namespace Frisket
{
    /// <summary>Whether the mark is written once across the page, or repeated over it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<WatermarkMode>))]
    public enum WatermarkMode
    {
        /// <summary>
        /// Repeated in a grid at a small size. Harder to remove from a screenshot than a
        /// single word, because every copy has to go and the text underneath has to be
        /// reconstructed in each place - which is the point of marking a preview at all.
        /// </summary>
        Tiled,

        /// <summary>
        /// Once, large, across the middle. Reads better, and takes one rectangle of paint
        /// to erase; suitable when the mark is a courtesy rather than a safeguard.
        /// </summary>
        Single
    }

    /// <summary>Whether the mark sits under the content or over it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<WatermarkLayer>))]
    public enum WatermarkLayer
    {
        /// <summary>
        /// Under the content. Anything the page fills - a black title bar, a shaded table
        /// header - paints over the mark and clears it exactly there. Only worth choosing
        /// when the section has no filled areas, or when the mark is a courtesy.
        /// </summary>
        Behind,

        /// <summary>
        /// Over the content, translucent. The default: the mark is then on every part of
        /// the page, including the parts worth tampering with.
        /// </summary>
        InFront
    }

    /// <summary>
    /// A word written across every page - SPECIMEN, BROUILLON, COPIE.
    ///
    /// It is drawn as ordinary page content, not as a background. That distinction is
    /// the whole point: a browser drops background graphics when printing unless the
    /// reader goes and turns them back on, so a mark drawn as a background is exactly
    /// absent from the one artefact that matters - the printout someone might pass off
    /// as an original.
    ///
    /// The engine draws it and takes no view on whether a given section needs one.
    /// </summary>
    public class Watermark
    {
        public Watermark() { }

        public Watermark(string text) => Text = text;

        /// <summary>
        /// DRAFT rather than SPECIMEN. Both are marks, and the repository's samples had
        /// said DRAFT since the day the watermark folder was made to name its modes
        /// rather than its words - while this line went on proposing the other one, so a
        /// mark made in the designer never matched a mark made anywhere else.
        ///
        /// <b>The trailing space is deliberate.</b> Tiling steps along the diagonal by
        /// the measured width of the text, so a space at the end widens the step and
        /// opens the grid: measured on the twelve-line invoice, 97 copies at 13.4 mm
        /// worst gap without it, 91 at 15.3 mm with. <see cref="Spacing"/> is the
        /// property that says this on purpose and in millimetres; the space is only
        /// what the default asks for, and any explicit spacing overrides it.
        ///
        /// It is also invisible in every reader except a file, which is why the tests
        /// state it once in a constant rather than typing it into an assertion.
        /// </summary>
        public string Text { get; set; } = "DRAFT ";

        /// <summary>Tiled across the whole sheet, or written once (Single).</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<WatermarkMode>))]
        public WatermarkMode Mode { get; set; } = WatermarkMode.Tiled;

        /// <summary>
        /// In front by default. Behind, the mark is at the mercy of whatever the content
        /// paints over it: a filled title bar or a shaded table header erases it exactly
        /// there, which is the failure every report library has - and it clears the areas
        /// worth tampering with. In front it is translucent instead, so both stay legible.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<WatermarkLayer>))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public WatermarkLayer Layer { get; set; } = WatermarkLayer.InFront;

        /// <summary>
        /// Degrees anticlockwise from horizontal. 45 by default: a diagonal crosses both
        /// the lines of text and the rules of a table, so no part of the page is left with
        /// a clean horizontal band to work in.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public double Angle { get; set; } = 45;

        /// <summary>
        /// How the word is drawn. Its size is a cap height in millimetres like any other
        /// text, so the mark scales with the section rather than with the paper.
        ///
        /// Mid grey at thirty percent on purpose: it shifts white paper and a black title
        /// bar by the same amount, so the mark reads over both. A light grey looks better
        /// on white and vanishes on anything dark - which is where it is needed.
        /// </summary>
        public TextStyle Font { get; set; } = new()
        {
            Size = 8,
            SizeUnit = FontSizeUnit.Millimeter,
            Bold = true,
            // 20% rather than 30%. A watermark has to be unmistakable, not to compete
            // with the text: at 77 of 255 the DRAFT ran dark enough over a paragraph to
            // make it tiresome to read, which is the one thing a mark drawn *behind* the
            // content must not do. Reported from looking at the conformance document,
            // where the words being obscured were the ones explaining what to look at.
            Color = new Color(128, 128, 128, 51)
        };

        /// <summary>
        /// Millimetres between one copy and the next, along the diagonal and across it.
        /// Ignored in <see cref="WatermarkMode.Single"/>. Zero means "from the text": a
        /// gap of half the word's own length, which stays right whatever the size.
        /// </summary>
        public double Spacing { get; set; }

        /// <summary>Pages that carry the mark. All of them, unless narrowed.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<PageScope>))]
        public PageScope PageScope { get; set; } = PageScope.All;

        public Watermark Clone() => new()
        {
            Text = Text,
            Mode = Mode,
            Layer = Layer,
            Angle = Angle,
            Font = Font.Clone(),
            Spacing = Spacing,
            PageScope = PageScope
        };
    }
}
