using System.Text.Json.Serialization;

namespace Frisket
{
    /// <summary>Which symbology to draw.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Symbology>))]
    public enum Symbology
    {
        /// <summary>Two dimensional, holds anything, and survives damage. The default.</summary>
        Qr,

        /// <summary>The general-purpose linear barcode a handheld scanner expects.</summary>
        Code128
    }

    /// <summary>
    /// A barcode. Its value goes through the same token and data binding as any text, so
    /// a template can carry <c>{InvoiceNumber}</c> and the data decides what is encoded.
    ///
    /// It is drawn as a grid of rectangles rather than as an image: it stays sharp at any
    /// size, costs a few hundred bytes instead of a bitmap, and needs nothing of a backend
    /// beyond the rectangle it already draws.
    /// </summary>
    public class BarcodeItem : BaseItem
    {
        /// <summary>What to encode. Tokens are substituted before it is read.</summary>
        public string Value { get; set; } = "";

        /// <summary>Which code: Qr or Code128.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<Symbology>))]
        public Symbology Symbology { get; set; } = Symbology.Qr;

        /// <summary>QR only: how much damage the code can take and still read.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<QrErrorCorrection>))]
        public QrErrorCorrection Correction { get; set; } = QrErrorCorrection.Medium;

        /// <summary>The bars themselves. Black unless a section wants otherwise.</summary>
        public Color Ink { get; set; } = Color.Black;

        /// <summary>
        /// The margin of clear space around the code, in modules. Zero takes what the
        /// symbology asks for, which is not the same number: four for QR, ten each side
        /// for Code 128.
        ///
        /// A code set too close to a rule or to the edge of the page is a code a scanner
        /// refuses, and the fault looks like the printer's rather than the document's.
        /// </summary>
        public int QuietZone { get; set; }

        /// <summary>The margin actually used, once a zero has been read as "the standard's".</summary>
        [JsonIgnore]
        public int QuietZoneOrDefault => QuietZone > 0 ? QuietZone : Symbology == Symbology.Qr ? 4 : 10;

        /// <summary>
        /// Code 128 only: print the value underneath. Worth keeping - it is the eye's
        /// fallback exactly when a scanner will not read, which is when someone has to
        /// type the number in.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]   // default is not default(T)
        public bool ShowText { get; set; } = true;

        /// <summary>How the value is printed under a linear barcode.</summary>
        public TextStyle Font { get; set; } = new() { Size = 0.7 };

        // Neither of these existed, so Clone() handed back a plain BaseItem: not a
        // barcode with its properties reset, but an item of no kind at all, which the
        // renderer draws as nothing. Reached by RepeatOver, which clones the children of
        // a repeating container once per record - a run of twenty-five codes came out as
        // twenty-five empty rectangles' worth of nothing, against twenty-five drawn for
        // the identical barcode outside a repetition.
        //
        // Found by a reflection walk over the model rather than by reading: the same
        // pass that caught Signing missing from FieldItem.CopyTo, and the reason test 80
        // now asks the question of every type instead of one at a time.
        protected override BaseItem CreateEmpty() => new BarcodeItem();

        protected override void CopyTo(BaseItem target)
        {
            base.CopyTo(target);
            var copy = (BarcodeItem)target;
            copy.Value = Value;
            copy.Symbology = Symbology;
            copy.Correction = Correction;
            copy.Ink = Ink;
            copy.QuietZone = QuietZone;
            copy.ShowText = ShowText;
            copy.Font = Font.Clone();
        }
    }
}
