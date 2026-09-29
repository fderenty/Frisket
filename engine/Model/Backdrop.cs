using System.Text.Json.Serialization;

namespace Frisket
{
    /// <summary>Which rectangle the backdrop fills.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<BackdropArea>))]
    public enum BackdropArea
    {
        /// <summary>
        /// The whole sheet, edge to edge. What pre-printed stationery is: the paper the
        /// document would have been printed on, so it runs under the margins as well.
        /// </summary>
        Sheet,

        /// <summary>
        /// Only what the section's margins leave - the band a header starts at down to the
        /// band a footer ends at. A tint or a rule meant to sit behind the content rather
        /// than to stand in for the paper.
        /// </summary>
        Margins
    }

    /// <summary>
    /// A picture behind every page: letterhead, pre-printed stationery, a tinted panel.
    ///
    /// A sibling of <see cref="Watermark"/>, deliberately not a mode of it. A mark says
    /// something about the document's standing - DRAFT, COPY - and is drawn to be hard to
    /// remove; a backdrop is the paper. One type covering both would have to mean "a word
    /// or a picture, a warning or stationery", which is the vocabulary trap section 16 of
    /// CLAUDE.md records, and its properties would then be half-meaningless either way.
    ///
    /// Not to be confused with <see cref="Report.ProofBackground"/>, which is the colour of
    /// paper the engine will never supply and which nothing ever prints. This one is drawn.
    ///
    /// Always behind the content, which is what the word means. A picture over the content
    /// is a mark, and that is what <see cref="WatermarkLayer.InFront"/> is for.
    ///
    /// Drawn as an artifact, so assistive software skips it: PDF/UA asks that every piece
    /// of content be either tagged or set aside, and a letterhead read out before every
    /// page is the same nuisance as a watermark read out eighty-seven times.
    /// </summary>
    public class Backdrop
    {
        public Backdrop() { }

        public Backdrop(ImageSource source) => Source = source;

        /// <summary>The picture. Nothing is drawn while it carries no bytes.</summary>
        public ImageSource Source { get; set; } = new();

        /// <summary>Sheet by default, because stationery runs under the margins too.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<BackdropArea>))]
        public BackdropArea Area { get; set; } = BackdropArea.Sheet;

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
        /// The JSON face of <see cref="NaturalHeight"/>.
        /// </summary>
        [JsonPropertyName("NaturalHeight")]
        public double? NaturalHeightJson
        {
            get => double.IsNaN(NaturalHeight) ? null : NaturalHeight;
            set => NaturalHeight = value ?? double.NaN;
        }

        /// <summary>
        /// How the picture sits in its rectangle. <see cref="ImageScaling.Stretch"/> here
        /// rather than the type's own <see cref="ImageScaling.Uniform"/>: filling the page
        /// is the plain reading of a background, and stationery is drawn at the paper's
        /// proportions already.
        ///
        /// This property did not exist at first, on the reasoning that the engine decodes
        /// nothing and so cannot know a picture's proportions - which was true of the type
        /// as it stood and stopped being true the moment it could be told. It is told the
        /// same way an <see cref="ImageItem"/> is: whoever writes the template says the
        /// natural size, and an editor that can measure the file fills it in. Without a
        /// natural size <see cref="ImageScaling.Uniform"/> has nothing to preserve and
        /// falls back to filling the rectangle, which is the honest answer rather than a
        /// silent quarter of a page.
        ///
        /// There is still no "cover" - fill the rectangle and crop what overflows -
        /// because cropping needs the backend to clip and no backend can: an SVG page
        /// clips at its own edge and a PDF viewer clips at the MediaBox, both by accident,
        /// so a cropped sheet would come out right in each and a cropped margin box would
        /// spill in one and not the other. Two outputs that disagree is the thing section
        /// 2 exists to catch.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<ImageScaling>))]
        public ImageScaling Scaling { get; set; } = ImageScaling.Stretch;

        /// <summary>
        /// Where it sits across its rectangle when it does not fill it - which is only
        /// ever the case under <see cref="ImageScaling.Uniform"/> or
        /// <see cref="ImageScaling.None"/>. Centred by default: a picture smaller than
        /// the sheet belongs in the middle of it.
        ///
        /// <see cref="ItemAlign"/> rather than a placement enum of its own. Nine named
        /// positions would be one vocabulary more to keep in step with a model that
        /// already says Start, Center and End on both axes - and section 5d is about
        /// exactly the property invented beside the one that was already there.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<ItemAlign>))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public ItemAlign AlignHorizontal { get; set; } = ItemAlign.Center;

        /// <summary>Where it sits down its rectangle. Centred, for the same reason.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<ItemAlign>))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public ItemAlign AlignVertical { get; set; } = ItemAlign.Center;

        /// <summary>
        /// 0 to 1. Full strength by default, which is right for stationery meant to be
        /// printed on and wrong for a photograph. Fade it here rather than in an image
        /// editor: a PNG carrying an alpha channel is refused outright by the PDF backend,
        /// so an author has no other way to do it.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public double Opacity { get; set; } = 1;

        /// <summary>Pages that carry it. All of them, unless narrowed.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<PageScope>))]
        public PageScope PageScope { get; set; } = PageScope.All;

        public Backdrop Clone() => new()
        {
            Source = Source,
            Area = Area,
            Scaling = Scaling,
            AlignHorizontal = AlignHorizontal,
            AlignVertical = AlignVertical,
            NaturalWidth = NaturalWidth,
            NaturalHeight = NaturalHeight,
            Opacity = Opacity,
            PageScope = PageScope
        };
    }
}
