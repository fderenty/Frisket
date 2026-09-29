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
            TextItem => PdfRole.Paragraph,
            _ => PdfRole.Section
        };
    }
}
