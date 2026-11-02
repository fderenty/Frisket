using System.Text.Json.Serialization;

namespace Frisket
{
    /// <summary>
    /// What an element is, for a reader that cannot see it. This is the vocabulary of
    /// the PDF structure tree, cut down to what a report actually uses.
    ///
    /// It is declared rather than guessed. A heading inferred from being bold and large
    /// is right most of the time, and the times it is wrong nobody notices until someone
    /// depends on it - which is exactly the population this exists for.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PdfRole>))]
    public enum PdfRole
    {
        /// <summary>
        /// From the kind of element: a text is a paragraph, an image a figure, a table
        /// row a row. Not a guess about meaning - a direct mapping - so it is the right
        /// default and wrong only where the author knows better.
        /// </summary>
        Auto,

        /// <summary>A division of the section, holding headings and content.</summary>
        Section,

        Heading1,
        Heading2,
        Heading3,
        Heading4,
        Heading5,
        Heading6,

        Paragraph,

        /// <summary>A caption belonging to a table or a figure above or below it.</summary>
        Caption,

        List,
        ListItem,

        /// <summary>A table, holding rows.</summary>
        Table,

        /// <summary>A row, holding cells.</summary>
        TableRow,

        /// <summary>
        /// A heading cell. What makes a table readable out loud: a reader announces the
        /// column heading before each value, so "Total 19,90" instead of "19,90".
        /// </summary>
        TableHeader,

        /// <summary>An ordinary cell.</summary>
        TableCell,

        /// <summary>
        /// Something to be looked at rather than read - a logo, a barcode. Needs
        /// <see cref="BaseItem.AltText"/>, or it is announced as nothing at all.
        /// </summary>
        Figure,

        /// <summary>
        /// Something the reader fills in. A widget annotation has to sit inside one of
        /// these or assistive software cannot tell what the control is for.
        /// </summary>
        Form,

        /// <summary>
        /// Decoration: rules, shading, the watermark. Skipped entirely by assistive
        /// software, which is the point - a tiled SPECIMEN read out eighty-seven times
        /// is worse than no mark at all.
        /// </summary>
        Artifact
    }

    /// <summary>
    /// How a tagged PDF reads what a header or footer repeats from page to page. It is printed
    /// on every page whichever this says. What such a band shows on one page only is not
    /// repeating, so it is always read: an amount due on the last page cannot be silenced from
    /// here, only by a Role of Artifact on the item itself.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RepeatedReading>))]
    public enum RepeatedReading
    {
        /// <summary>
        /// Read where it first appears, and not again. A band says the same words on every
        /// page but for its page numbers, so nothing is lost, and a sentence running over a
        /// page is not broken by them. Someone arriving straight at a later page does not hear it.
        /// </summary>
        Once,

        /// <summary>
        /// Read on every page: a notice that has to be heard wherever the reader is. PDF/UA 7.8
        /// asks for running headers and footers to be artifacts, so an audit may object.
        /// </summary>
        EveryPage,

        /// <summary>
        /// Never read, as PDF/UA 7.8 asks of a running header or footer. Whatever it says has to
        /// be in the body as well, or it is on the paper and nowhere else.
        /// </summary>
        Never
    }

    /// <summary>
    /// What a placement of a header or footer is drawn as when it is not to be read: a pagination
    /// artifact, which is how PDF/UA 7.8 asks for one to be written, and of its own subtype.
    /// </summary>
    internal sealed class RunningMark : BaseItem
    {
        public static readonly RunningMark Header = new("Header");
        public static readonly RunningMark Footer = new("Footer");

        public string Subtype { get; }

        private RunningMark(string subtype)
        {
            Subtype = subtype;
            Role = PdfRole.Artifact;
        }
    }

    /// <summary>The tag each role writes into the structure tree.</summary>
    internal static class PdfRoles
    {
        public static string Tag(PdfRole role) => role switch
        {
            PdfRole.Section => "Sect",
            PdfRole.Heading1 => "H1",
            PdfRole.Heading2 => "H2",
            PdfRole.Heading3 => "H3",
            PdfRole.Heading4 => "H4",
            PdfRole.Heading5 => "H5",
            PdfRole.Heading6 => "H6",
            PdfRole.Paragraph => "P",
            PdfRole.Caption => "Caption",
            PdfRole.List => "L",
            PdfRole.ListItem => "LI",
            PdfRole.Figure => "Figure",
            PdfRole.Form => "Form",
            PdfRole.Table => "Table",
            PdfRole.TableRow => "TR",
            PdfRole.TableHeader => "TH",
            PdfRole.TableCell => "TD",
            _ => "P"
        };

        /// <summary>
        /// What an element is when it says nothing. A direct mapping from the kind of
        /// item, which is why Auto is honest rather than clever.
        /// </summary>
        public static PdfRole Resolve(BaseItem item) => item switch
        {
            _ when item.Role != PdfRole.Auto => item.Role,

            // A cell takes its role from the row it sits in, not from what it holds: the
            // same text is a heading in one row and a value in the next.
            _ when item.Parent is TableRow header && header.IsHeader => PdfRole.TableHeader,
            _ when item.Parent is TableRow => PdfRole.TableCell,

            TableItem => PdfRole.Table,
            TableRow => PdfRole.TableRow,
            LineItem => PdfRole.Artifact,
            // A copied page is a drawing and nothing else: no text of ours, no structure
            // of ours, nothing that can be read aloud. So it owes an alternative text
            // exactly as a picture does, which ua.altText then asks for.
            ImageItem or BarcodeItem or ImportedPageItem => PdfRole.Figure,

            // A signature to be given by hand writes no widget: there is no control for
            // a Form element to point at, and one that points at nothing is worse than
            // none. It is a ruled area to look at and write on - a figure - and it needs
            // AltText like any other.
            FieldItem { Kind: FieldKind.Signature, Signing: Signing.ByHand } => PdfRole.Figure,

            FieldItem => PdfRole.Form,
            TextItem or AdvancedTextItem => PdfRole.Paragraph,
            _ => PdfRole.Section
        };
    }
}
