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

        /// <summary>
        /// An ordinary cell. An item in a row given another role - a heading, a nested table,
        /// decoration - is still the row's cell, and that role goes under it: a row holds
        /// nothing but cells, and one a cell short is refused.
        /// </summary>
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
        ///
        /// It covers what it holds: on a group or a table, everything inside is set aside
        /// with it, whatever it says of itself. A form field inside is still read, since a
        /// fillable control has to be reachable.
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
            // Decoration covers what it holds. Until 9 November 2026 it covered the frame of
            // the item marked and nothing else: a table set aside went on writing its rows as
            // rows with no table over them, which PDF/UA 7.2 refuses, and a group set aside
            // had every word in it read. Whatever an item inside says of itself, it is set
            // aside with the rest; a control is the one exception, and the backend makes it,
            // since a widget must sit in a Form (7.18.4).
            _ when SetAside(item.Parent) => PdfRole.Artifact,

            _ when item.Role != PdfRole.Auto => item.Role,

            // A table is a table wherever it stands. In a cell it also gets the cell the row
            // needs (CellOf), rather than being taken for that cell and leaving its own rows
            // under a TD.
            TableItem => PdfRole.Table,

            // A cell takes its role from the row it sits in, not from what it holds: the
            // same text is a heading in one row and a value in the next.
            _ when item.Parent is TableRow header && header.IsHeader => PdfRole.TableHeader,
            _ when item.Parent is TableRow => PdfRole.TableCell,

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

        /// <summary>
        /// The cell a row needs around an item whose role is <paramref name="role"/>: TH or
        /// TD, or null when the item is not in a row or its row is set aside.
        ///
        /// Equal to the role when the item is a cell and nothing more. Different when it is
        /// something else as well - a heading, a nested table, a control, decoration - which
        /// then sits under the cell rather than in its place: a row may hold nothing but TH
        /// and TD, and a row one cell short is refused as surely (ISO 14289-1 7.2). The
        /// control was the first case, and had this for itself alone (CLAUDE.md 4b).
        /// </summary>
        public static PdfRole? CellOf(BaseItem item, PdfRole role)
        {
            if (item.Parent is not TableRow row || SetAside(row))
                return null;

            if (role is PdfRole.TableCell or PdfRole.TableHeader)
                return role;

            return row.IsHeader ? PdfRole.TableHeader : PdfRole.TableCell;
        }

        /// <summary>
        /// Whether a table's roles and a table's shape disagree here, which no reader can
        /// follow and PDF/UA refuses: a table holds rows, a row holds cells, a cell sits in a
        /// row. Set aside is never a disagreement, since nothing of it is read.
        /// </summary>
        public static bool BreaksTable(BaseItem item, PdfRole role) => role switch
        {
            PdfRole.Artifact => false,
            _ when item is TableItem => role != PdfRole.Table,
            _ when item is TableRow => role != PdfRole.TableRow,
            PdfRole.Table or PdfRole.TableRow => true,
            PdfRole.TableCell or PdfRole.TableHeader => item.Parent is not TableRow,
            _ => false
        };

        /// <summary>Whether a container, or anything above it, is set aside as decoration.</summary>
        private static bool SetAside(ContainerItem? container)
        {
            for (ContainerItem? at = container; at != null; at = at.Parent)
            {
                if (at.Role == PdfRole.Artifact)
                    return true;
            }
            return false;
        }
    }
}
