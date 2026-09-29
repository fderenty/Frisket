using System.Text.Json.Serialization;

namespace Frisket
{
    /// <summary>What a reader is asked to do with a field.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<FieldKind>))]
    public enum FieldKind
    {
        /// <summary>A line, or a block, of typed text.</summary>
        Text,

        /// <summary>A box that is ticked or not.</summary>
        Checkbox,

        /// <summary>
        /// An area set aside for a signature. It is a real signature field, so it can be
        /// signed in a reader later, and it is a plain empty box on paper - which is all
        /// a quote needs to be signed by hand. Cryptographic signing is another matter
        /// entirely and none of this engine's business.
        /// </summary>
        Signature
    }

    /// <summary>What a signature area expects of whoever signs it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Signing>))]
    public enum Signing
    {
        /// <summary>
        /// A ruled box, signed with a pen once printed or with the reader's own drawing
        /// tool. This is what a quote or a delivery note asks for, and it asks nothing of
        /// the person signing.
        /// </summary>
        ByHand,

        /// <summary>
        /// A cryptographic signature field. A reader that supports it offers to sign with
        /// a certificate, and refuses without one - so it only suits sections where the
        /// signatory is known to hold one.
        /// </summary>
        Digital
    }

    /// <summary>
    /// A field the reader can fill in: the interactive half of a quote or an order form.
    ///
    /// It lays out like any other item - it has a frame, it flows, it fits in a table
    /// cell. What changes is what the backend does with it: the PDF backend writes a form
    /// widget, the SVG backend draws what it would look like.
    ///
    /// The value written here is the field's starting state, and the one drawn when the
    /// section is flattened.
    /// </summary>
    public class FieldItem : BaseItem
    {
        /// <summary>
        /// Name the value is stored and read back under. It must be unique in the
        /// section; a field stamped out by a repeat gets an index appended so that a
        /// prototype can be repeated without every copy answering to the same name.
        /// </summary>
        public string Name { get; set; } = "";

        [JsonConverter(typeof(JsonStringEnumConverter<FieldKind>))]
        public FieldKind Kind { get; set; } = FieldKind.Text;

        /// <summary>
        /// Starting value. For a checkbox, anything but null, empty and "false" counts as
        /// ticked.
        /// </summary>
        public string? Value { get; set; }

        /// <summary>Shown by a reader when the pointer rests on the field.</summary>
        public string? Tooltip { get; set; }

        public bool ReadOnly { get; set; }
        public bool Required { get; set; }

        /// <summary>Text fields only: accept more than one line.</summary>
        public bool Multiline { get; set; }

        /// <summary>Text fields only: zero for no limit.</summary>
        public int MaxLength { get; set; }

        /// <summary>Signature fields only: how the section expects to be signed.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<Signing>))]
        public Signing Signing { get; set; } = Signing.ByHand;

        /// <summary>How the value is drawn. Inherits from the section like any text.</summary>
        public TextStyle Font { get; set; } = new();

        /// <summary>
        /// Outline of the field itself. Null draws nothing, which is what you want for a
        /// field sitting on a printed rule.
        /// </summary>
        public PenStyle? Outline { get; set; } = new() { Width = 0.2, Color = new(90, 90, 90) };

        /// <summary>Fill behind the field, to make it visible on a busy page.</summary>
        public BrushStyle? Fill { get; set; }

        /// <summary>
        /// True when <see cref="Value"/> reads as ticked. A token the data never answered
        /// is not a tick: left as it is, "{Choices.Revision}" is a non-empty string and
        /// would otherwise tick every box on an unfilled form.
        /// </summary>
        [JsonIgnore]
        public bool IsChecked => !string.IsNullOrEmpty(Value)
                                 && !(Value!.StartsWith('{') && Value.EndsWith('}'))
                                 && !string.Equals(Value, "false", StringComparison.OrdinalIgnoreCase)
                                 && !string.Equals(Value, "Off", StringComparison.OrdinalIgnoreCase)
                                 && Value != "0";

        protected override BaseItem CreateEmpty() => new FieldItem();

        protected override void CopyTo(BaseItem target)
        {
            var copy = (FieldItem)target;
            copy.Name = Name;
            copy.Kind = Kind;
            copy.Value = Value;
            copy.Tooltip = Tooltip;
            copy.ReadOnly = ReadOnly;
            copy.Required = Required;
            copy.Multiline = Multiline;
            copy.MaxLength = MaxLength;

            // Missed when Signing was added, and the only one of the eleven that was.
            // A clone is not a rare path: RepeatOver clones the children of a repeating
            // container, so a signature field in a run of them silently became ByHand -
            // a digital signature turning into a place to sign, which is section 5's
            // "the only class of fault where the document looks exactly right".
            copy.Signing = Signing;

            copy.Font = Font.Clone();
            copy.Outline = Outline?.Clone();
            copy.Fill = Fill?.Clone();
        }
    }
}
