using Frisket;

namespace test
{
    /// <summary>
    /// The section Frisket is validated against, and the one a visitor opens first.
    ///
    /// It is a report rather than an invoice on purpose. An invoice pulls a reader
    /// towards one trade and one country - and the engine's own vocabulary followed it
    /// for a while, down to French numbers as a hidden default. What is demonstrated
    /// here is the layout engine: headings and flowing prose, a table long enough to
    /// break across pages, pictures, an appendix spliced in from another file, codes,
    /// a signature block, and a back page. The subject is invented and the prose is
    /// lorem ipsum, so nothing in it can be mistaken for a real customer's paperwork.
    ///
    /// Built here in C#, then written out as JSON and read back through
    /// <see cref="Report.FromFile"/> before anything is rendered. That round trip is
    /// not ceremony: it is the only check that every element kind survives being
    /// written and re-read, and it is the path the CLI and the native library take,
    /// unlike a report handed straight to the renderer.
    /// </summary>
    internal static class Showcase
    {
        /// <summary>
        /// Writes the three files that make up the example, into <c>examples/</c> at the
        /// repository root. They are committed: the point is a template someone can open
        /// in the designer or hand to the CLI without building anything.
        /// </summary>
        internal static string WriteFiles(string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, AppendixFile), Appendix().ToJson());
            File.WriteAllText(Path.Combine(directory, TemplateFile), Build().ToJson());
            File.WriteAllText(Path.Combine(directory, DataFile), DataJson());
            return Path.Combine(directory, TemplateFile);
        }

        internal const string TemplateFile = "showcase.json";
        internal const string DataFile = "showcase.data.json";
        internal const string AppendixFile = "showcase-appendix.json";

        // ------------------------------------------------------------------
        // The appendix, which the report splices in from its own file
        // ------------------------------------------------------------------

        internal static Report Appendix()
        {
            var report = new Report { Body = new Body { Layout = LayoutMode.Stack } };

            // No letter of its own. This file is spliced into the report before
            // anything is laid out, so its heading joins the parent's count and is
            // numbered by it - which is the whole reason expansion happens that early.
            report.Body!.Add(Heading("Method", 1.25));
            report.Body!.Add(Prose(
                "Nam libero tempore, cum soluta nobis est eligendi optio cumque nihil "
                + "impedit quo minus id quod maxime placeat facere possimus, omnis "
                + "voluptas assumenda est, omnis dolor repellendus. Temporibus autem "
                + "quibusdam et aut officiis debitis aut rerum necessitatibus saepe."));
            report.Body!.Add(Prose(
                "Ut enim ad minima veniam, quis nostrum exercitationem ullam corporis "
                + "suscipit laboriosam, nisi ut aliquid ex ea commodi consequatur."));

            return report;
        }

        // ------------------------------------------------------------------
        // The report itself, with its tokens still unresolved
        // ------------------------------------------------------------------

        internal static Report Build()
        {
            var report = new Report
            {
                Title = "ACME Field Operations Report {ReportNumber}",

                // There is no default page size, and a template that omits this lays out
                // on a sheet of nothing: the pages come out 0 by 0, every column is too
                // narrow to divide, and the flow places one item per page and drops the
                // rest - silently, which is how an afternoon goes.
                Dimension = new DocumentDimension { Width = 210, Height = 297 },

                Language = "en-GB",

                // Two levels, figures at both. The sections are the H1s and what sits
                // under them is H2, so this spells "5" and "5.1" - and the dot after
                // either is typed in the heading's own text, never here.
                HeadingNumbering = "1.1",

                Author = "ACME Corporation",
                Subject = "Field operations report {ReportNumber}, {Period}",
                Keywords = "report, operations, {ReportNumber}",
                RootFontSize = 3,

                // The one place a face is named, and it is the suite's own constant:
                // a literal here would break the day the library is triaged again.
                DefaultFont = new TextStyle { Family = EngineTests.DmsFont },

                // Fixed, so the same template and data give the same bytes twice.
                GeneratedAt = new DateTimeOffset(2025, 8, 1, 9, 0, 0, TimeSpan.Zero),
            };

            report.Section.Style.Padding = new EdgeSize { Left = 12, Top = 10, Right = 12, Bottom = 8 };

            report.Header = BuildHeader();
            report.Footer = BuildFooter();
            report.Body = BuildBody();
            report.BackPage = new BackPage { Content = BuildNotes() };

            // Marked, in front, translucent. The trailing space is deliberate: tiled
            // without one, the last letter of a copy sits against the first of the next
            // and the two read as a word nobody wrote.
            report.Watermark = new Watermark("DRAFT ");
            report.Watermark.Font.Size = 6;
            report.Watermark.Spacing = 1;

            return report;
        }

        // ------------------------------------------------------------------

        private static Header BuildHeader()
        {
            var header = new Header { Layout = LayoutMode.Stack };
            header.Style.Margin.Bottom = 3;

            // Named in the outline, so a file holding a batch of these can be navigated
            // and each entry says which report it opens.
            header.Bookmark = "Report {ReportNumber}";

            var band = new ContainerItem { Layout = LayoutMode.Absolute };
            band.Style.Location.Dock = Dock.Left | Dock.Right;
            band.Style.Location.HeightValue = 16;

            var logo = new ImageItem
            {
                Source = new ImageSource
                {
                    Data = Mark(256),
                    MediaType = "image/png",
                    SourcePath = "acme-mark.png"
                },
                AltText = "ACME Corporation mark"
            };
            logo.Style.Location.Left = (0, Units.Millimeter);
            logo.Style.Location.Top = (0, Units.Millimeter);
            logo.Style.Location.Width = (15, Units.Millimeter);
            logo.Style.Location.Height = (15, Units.Millimeter);
            band.Add(logo);

            var name = new TextItem
            {
                Text = "ACME Corporation",
                Font = new TextStyle { Size = 1.5, Bold = true }
            };
            name.Style.Location.Left = (19, Units.Millimeter);
            name.Style.Location.Top = (1, Units.Millimeter);
            band.Add(name);

            var kind = new TextItem
            {
                Text = "Field Operations Report",
                Font = new TextStyle { Size = 1, Color = new Color(90, 90, 90) }
            };
            kind.Style.Location.Left = (19, Units.Millimeter);
            kind.Style.Location.Top = (7, Units.Millimeter);
            band.Add(kind);

            var reference = new TextItem
            {
                Text = "{ReportNumber}\n{Period}",
                Font = new TextStyle { Size = 0.9, HorizontalAlignment = HorizontalAlignment.Right }
            };
            reference.Style.Location.Dock = Dock.Right;
            reference.Style.Location.Top = (1, Units.Millimeter);
            reference.Style.Location.WidthValue = 50;
            band.Add(reference);

            header.Add(band);
            header.Add(new LineItem { Pen = new PenStyle { Width = 0.3, Color = new Color(40, 40, 40) } });
            return header;
        }

        private static Footer BuildFooter()
        {
            var footer = new Footer { Layout = LayoutMode.Stack };
            footer.Style.Margin.Top = 3;

            footer.Add(new LineItem { Pen = new PenStyle { Width = 0.2, Color = new Color(150, 150, 150) } });

            var row = new ContainerItem { Layout = LayoutMode.Absolute };
            row.Style.Location.Dock = Dock.Left | Dock.Right;
            row.Style.Location.HeightValue = 6;
            row.Style.Margin.Top = 1;

            var notice = new TextItem
            {
                Text = "Confidential — circulated for review only",
                Font = new TextStyle { Size = 0.75, Color = new Color(110, 110, 110) }
            };
            notice.Style.Location.Left = (0, Units.Millimeter);
            row.Add(notice);

            var page = new TextItem
            {
                Text = "Page {Page} of {Pages}",
                Font = new TextStyle { Size = 0.75, HorizontalAlignment = HorizontalAlignment.Right }
            };
            page.Style.Location.Dock = Dock.Right;
            page.Style.Location.WidthValue = 40;
            row.Add(page);

            footer.Add(row);
            return footer;
        }

        // ------------------------------------------------------------------

        private static Body BuildBody()
        {
            var body = new Body { Layout = LayoutMode.Stack };

            var title = new TextItem
            {
                Text = "Annual survey of field installations",
                Font = new TextStyle { Size = 2.2, Bold = true },

                // Not a heading. A document's title is not a section of the document,
                // and treating it as one put every numbered section a level down - "1.1
                // Summary" under a title nobody numbers. The sections are the H1s now,
                // so PDF/UA 7.4.2 is satisfied by the run starting where it should, and
                // a reader still learns the title: it is /Title in the file, which is
                // what is announced when the document opens.
                Role = PdfRole.Auto
            };
            title.Style.Margin.Top = 4;
            body.Add(title);

            var standfirst = new TextItem
            {
                Text = "Prepared for {Client} · {Period}",
                Font = new TextStyle { Size = 1, Color = new Color(90, 90, 90) }
            };
            standfirst.Style.Margin.Bottom = 3;
            body.Add(standfirst);

            // --- 1. Summary, and the mixed-style paragraph ---------------------
            Demonstrates(body, "Summary", "Summary",
                "Justified text, and one paragraph carrying several styles at once. In the "
                + "note below, bold, struck through, italic and a colour change happen "
                + "inside a sentence rather than between sentences - which no run of "
                + "separate text items can do. Look for straight right-hand edges on the "
                + "two paragraphs above it, with the last line of each left short.");
            body.Add(Prose(
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod "
                + "tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim "
                + "veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea "
                + "commodo consequat. Duis aute irure dolor in reprehenderit in voluptate "
                + "velit esse cillum dolore eu fugiat nulla pariatur."));
            body.Add(Prose(
                "Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium "
                + "doloremque laudantium, totam rem aperiam, eaque ipsa quae ab illo "
                + "inventore veritatis et quasi architecto beatae vitae dicta sunt explicabo. "
                + "Nemo enim ipsam voluptatem quia voluptas sit aspernatur aut odit."));

            // One paragraph carrying several styles at once, which no run of plain text
            // items can do: the styles change inside a sentence, not between them.
            var mixed = new AdvancedTextItem();
            mixed.Font.Size = 1;
            mixed.Runs.Add(new TextRun { Text = "Note for {Client}: " });
            mixed.Runs.Add(new TextRun { Text = "sixteen sites", Font = new TextStyle { Bold = true } });
            mixed.Runs.Add(new TextRun { Text = " were surveyed, " });
            mixed.Runs.Add(new TextRun
            {
                Text = "three were unreachable",
                Font = new TextStyle { Strikethrough = true, Color = new Color(192, 57, 43) }
            });
            mixed.Runs.Add(new TextRun
            {
                Text = " two were unreachable",
                Font = new TextStyle { Italic = true }
            });
            mixed.Runs.Add(new TextRun { Text = ", and the remainder reported nominal." });
            mixed.Style.Margin.Top = 3;
            body.Add(mixed);

            // --- 2. Measurements: the table that runs past a page --------------
            Demonstrates(body, "Measurements", "Measurements",
                "A table longer than a page, with one row taller than a page on its own. "
                + "Look for the column headings repeating at the top of each page, and - "
                + "the part worth checking - for the split row showing as many cells on "
                + "the second page as the table has columns. A half-row with fewer cells "
                + "is refused by a conformance checker and read aloud as nonsense.");
            body.Add(BuildTable());

            // --- 3. Photographs -----------------------------------------------
            Demonstrates(body, "Site photographs", "Site photographs",
                "A contact sheet filled from the data: one picture is described in the "
                + "template and repeated per record. Look for whole rows moving to the "
                + "next page rather than a row of pictures being sliced, and for each "
                + "picture keeping its proportions inside its cell.");

            var gallery = new ImageGridItem { GridColumns = 3, RowHeight = 24, Gap = 3, RepeatOver = "Photos" };
            gallery.Style.Margin.Top = 2;
            gallery.Add(new ImageItem
            {
                SourceField = "Image",
                NaturalWidth = 40,
                NaturalHeight = 30,
                AltText = "Photograph of site {Site}"
            });
            body.Add(gallery);

            // --- 4. Appendix, spliced in from its own file ---------------------
            Demonstrates(body, "Appendix", "Appendix",
                "Everything below this line comes from a second template file, spliced in "
                + "before the layout ever ran. Look for it flowing on from this page "
                + "rather than starting one of its own: a sub-template is part of the "
                + "surrounding flow, and brings none of its own margins or page size.");

            // The heading above already put "Appendix" in the outline; a second
            // bookmark on the item itself listed it twice, which reads as two
            // appendices to anyone using the outline to navigate.
            var appendix = new SubTemplateItem { TemplatePath = AppendixFile };
            appendix.Style.Margin.Top = 4;
            body.Add(appendix);

            // --- 5. References: both symbologies, bound from the data ----------
            // The codes go inside the block, so they cannot arrive on a page of their
            // own with nothing saying what they are - which is how this was reported.
            ContainerItem codesHolder = BuildCodes();

            Demonstrates(body, "References", "References",
                "Two barcodes, drawn by the engine rather than embedded as pictures, and "
                + "both carrying values from the data. Look for the two titled below this "
                + "one - numbered from it, so they read 5.1 and 5.2 without either number "
                + "being typed anywhere - and for each carrying alternative text, without "
                + "which a barcode is a figure with nothing to say.",
                codesHolder);

            return BuildSignOffTail(body);
        }

        private static ContainerItem BuildCodes()
        {
            var codes = new ContainerItem { Layout = LayoutMode.Absolute };
            codes.Style.Location.Dock = Dock.Left | Dock.Right;
            codes.Style.Location.HeightValue = 42;
            codes.Style.Margin.Top = 2;

            // A title over each, so the page says which symbology is which instead of
            // leaving a reader to tell them apart by their shape. They are level two -
            // under the section - and number themselves from it.
            TextItem qrTitle = Heading("QR code", 1.0);
            qrTitle.Style.Location.Left = (0, Units.Millimeter);
            qrTitle.Style.Location.Top = (0, Units.Millimeter);
            qrTitle.Style.Location.Width = (36, Units.Millimeter);
            qrTitle.Style.Margin.Top = 0;
            codes.Add(qrTitle);

            TextItem codeTitle = Heading("Code 128", 1.0);
            codeTitle.Style.Location.Left = (40, Units.Millimeter);
            codeTitle.Style.Location.Top = (0, Units.Millimeter);
            codeTitle.Style.Location.Width = (60, Units.Millimeter);
            codeTitle.Style.Margin.Top = 0;
            codes.Add(codeTitle);

            var qr = new BarcodeItem
            {
                Value = "https://example.invalid/r/{ReportNumber}",
                AltText = "QR code linking to report {ReportNumber} online"
            };
            qr.Style.Location.Left = (0, Units.Millimeter);
            qr.Style.Location.Top = (6, Units.Millimeter);
            qr.Style.Location.Width = (32, Units.Millimeter);
            qr.Style.Location.Height = (32, Units.Millimeter);
            codes.Add(qr);

            var code128 = new BarcodeItem
            {
                Symbology = Symbology.Code128,
                Value = "{ReportNumber}",
                AltText = "Code 128 of the report number {ReportNumber}"
            };
            code128.Style.Location.Left = (40, Units.Millimeter);
            code128.Style.Location.Top = (10, Units.Millimeter);
            code128.Style.Location.Width = (60, Units.Millimeter);
            code128.Style.Location.Height = (20, Units.Millimeter);
            codes.Add(code128);
            return codes;
        }

        private static Body BuildSignOffTail(Body body)
        {
            // --- 6. sign-off ---------------------------------------------------
            Demonstrates(body, "Sign-off", "Sign-off",
                "Fillable form fields: a tick box, two text boxes, a place to sign, and "
                + "one more inside a table. Try typing in them - if they are flat, the "
                + "document was rendered with the form flattened and the boxes are only a "
                + "picture of somewhere to write. A signature is the case worth watching, "
                + "because flattened it looks exactly the same and nothing on the page "
                + "says so. The cell in the cost table is here because a field that is a "
                + "table cell is tagged as a cell, not as a control, and its widget was "
                + "reachable from nothing in the structure tree - which no reader shows "
                + "you and only a validator refuses.");
            body.Add(BuildSignOff());
            body.Add(BuildCosts());

            // --- 7. what the page cannot show ----------------------------------
            // Three of the things this document exists to prove leave no mark on it.
            // Nothing on screen is wrong, and nothing on screen is evidence either, so
            // the only honest thing a caption can do is say where else to look.
            Demonstrates(body, "What is here but not visible", "Not visible",
                "Three things this file carries that no page can show you. A Factur-X "
                + "invoice is attached - open the attachments pane to find factur-x.xml. "
                + "The whole document is tagged, so a reader can announce headings, table "
                + "cells and alternative text - the structure tree is where to check. And "
                + "it claims PDF/A-3B and PDF/UA-1, which only a validator can confirm; a "
                + "file that claims nothing is judged as PDF/A-1b instead, which forbids "
                + "the transparency used by the watermark on every page.");

            var seeAlso = new TextItem
            {
                Text = "The watermark behind this text is that transparency, drawn under "
                     + "the content rather than over it. The back of each sheet carries the "
                     + "terms, which is why the page count is higher than the sheet count "
                     + "printed in the footer.",
                Font = new TextStyle { Size = 0.95, Italic = true, Color = new Color(85, 85, 85) }
            };
            seeAlso.Style.Margin.Top = 1;
            body.Add(seeAlso);

            return body;
        }

        private static TableItem BuildTable()
        {
            var table = new TableItem
            {
                RepeatOver = "Readings",
                RepeatHeader = true,
                Grid = new PenStyle { Width = 0.15, Color = new Color(170, 170, 170) }
            };
            table.Style.Margin.Top = 2;

            table.Columns.Add(new TableColumn { Width = 26, WidthUnit = Units.Millimeter });
            table.Columns.Add(new TableColumn());                 // Auto: takes what is left
            table.Columns.Add(new TableColumn { Width = 24, WidthUnit = Units.Millimeter });
            table.Columns.Add(new TableColumn { Width = 22, WidthUnit = Units.Millimeter });

            var head = new TableRow { IsHeader = true };
            head.Style.Padding = new() { Left = 1.5, Top = 1, Right = 1.5, Bottom = 1 };
            head.Add(HeadCell("Site"));
            head.Add(HeadCell("Observation"));
            head.Add(HeadCell("Reading"));
            head.Add(HeadCell("Status"));
            table.Add(head);

            var row = new TableRow();
            row.Style.Padding = new() { Left = 1.5, Top = 1, Right = 1.5, Bottom = 1 };
            row.Add(BodyCell("{Site}"));
            row.Add(BodyCell("{Observation}"));
            row.Add(BodyCell("{Reading}", HorizontalAlignment.Right));
            row.Add(BodyCell("{Status}", HorizontalAlignment.Right));
            table.Add(row);

            table.Bookmark = "Readings";
            table.BookmarkLevel = 1;
            return table;
        }

        /// <summary>
        /// A form carrying all three kinds a reader can meet: a box to tick, a line to
        /// fill in, and a place to sign by hand. One kind alone would leave the other
        /// two unvalidated in the one file that gets validated.
        ///
        /// And one of them inside a table, since 25 August 2026. A field used as a cell
        /// takes its role from the row it sits in - PdfRoles.Resolve answers TableCell
        /// for anything whose parent is a TableRow, before it ever looks at FieldItem -
        /// so its widget was written, listed in /Annots and in /AcroForm /Fields, and
        /// pointed at by nothing in the structure tree. PDF/UA 7.18.1 wants every
        /// annotation in the tree and 7.18.4 wants a widget nested in a Form; this file
        /// carried neither, and passed both profiles, because the shape was not in it.
        ///
        /// That is the whole argument for one document that carries everything: the
        /// validator can only refuse what it is shown.
        /// </summary>
        private static ContainerItem BuildSignOff()
        {
            var block = new ContainerItem { Layout = LayoutMode.Absolute };
            block.Style.Location.Dock = Dock.Left | Dock.Right;
            block.Style.Location.HeightValue = 46;
            block.Style.Margin.Top = 6;

            var tickLabel = new TextItem
            {
                Text = "Findings reviewed and accepted",
                Font = new TextStyle { Size = 0.9 }
            };
            tickLabel.Style.Location.Left = (8, Units.Millimeter);
            tickLabel.Style.Location.Top = (0.5, Units.Millimeter);
            block.Add(tickLabel);

            // Every field owes a description, and not only the handwritten signature
            // below. Left fillable it is a Form and a reader announces the tooltip; drawn
            // rather than fillable it is a Figure, because a Form element with no widget
            // under it is refused - and a Figure with nothing to say for it is refused
            // too. The same document, produced two ways, and PDF/UA asks something of it
            // both times.
            //
            // veraPDF found this the day the flattened output started being validated at
            // all: three checks of ISO 14289-1 clause 7.3, one per field here.
            var tick = new FieldItem
            {
                Name = "accepted",
                Kind = FieldKind.Checkbox,
                Tooltip = "Tick to accept the findings",
                AltText = "Checkbox: the findings are accepted"
            };
            tick.Style.Location.Left = (0, Units.Millimeter);
            tick.Style.Location.Top = (0, Units.Millimeter);
            tick.Style.Location.Width = (5, Units.Millimeter);
            tick.Style.Location.Height = (5, Units.Millimeter);
            block.Add(tick);

            var refLabel = new TextItem
            {
                Text = "Reviewer reference",
                Font = new TextStyle { Size = 0.85, Color = new Color(90, 90, 90) }
            };
            refLabel.Style.Location.Left = (0, Units.Millimeter);
            refLabel.Style.Location.Top = (8, Units.Millimeter);
            block.Add(refLabel);

            var reference = new FieldItem
            {
                Name = "reviewer_reference",
                Kind = FieldKind.Text,
                MaxLength = 32,
                Tooltip = "Reviewer's own reference for this report",
                AltText = "Box for the reviewer's own reference"
            };
            reference.Style.Location.Left = (0, Units.Millimeter);
            reference.Style.Location.Top = (12, Units.Millimeter);
            reference.Style.Location.Width = (75, Units.Millimeter);
            reference.Style.Location.Height = (7, Units.Millimeter);
            block.Add(reference);

            // One signed by hand and one signed digitally: the two are different widgets
            // in the file, and a section carrying only one leaves the other unvalidated.
            AddSignature(block, "Prepared by", "prepared_by", 0, 22, Signing.ByHand);
            AddSignature(block, "Approved by", "approved_by", 95, 22, Signing.Digital);
            return block;
        }

        /// <summary>
        /// A short table with a fillable cell in it, which is the shape that had no
        /// example anywhere.
        ///
        /// A field used as a cell takes its role from the row it sits in: PdfRoles.Resolve
        /// answers TableCell for anything whose parent is a TableRow, before it ever looks
        /// at FieldItem. So the widget was written, listed in /Annots and in /AcroForm
        /// /Fields, and pointed at by nothing in the structure tree - PDF/UA 7.18.1 wants
        /// every annotation in the tree, 7.18.4 wants a widget nested in a Form, and this
        /// file satisfied neither while passing both profiles, because the shape was not
        /// in it.
        ///
        /// The engine plainly meant fields in tables to work - LayoutResolver.IntrinsicWidth
        /// carries a case of its own for sizing a fitted column around one - so this is not
        /// an exotic arrangement being propped up. It is one that was supported everywhere
        /// except in the file that proves the output.
        /// </summary>
        private static TableItem BuildCosts()
        {
            var table = new TableItem
            {
                Grid = new PenStyle { Width = 0.15, Color = new Color(170, 170, 170) }
            };
            table.Style.Margin.Top = 4;

            table.Columns.Add(new TableColumn());                 // Auto: takes what is left
            table.Columns.Add(new TableColumn { Width = 34, WidthUnit = Units.Millimeter });

            var head = new TableRow { IsHeader = true };
            head.Style.Padding = new() { Left = 1.5, Top = 1, Right = 1.5, Bottom = 1 };
            head.Add(HeadCell("Work carried out"));
            head.Add(HeadCell("Agreed cost"));
            table.Add(head);

            var row = new TableRow();
            row.Style.Padding = new() { Left = 1.5, Top = 1, Right = 1.5, Bottom = 1 };
            row.Add(BodyCell("Remedial works, as scheduled above"));

            var cost = new FieldItem
            {
                Name = "agreed_cost",
                Kind = FieldKind.Text,
                MaxLength = 12,
                Tooltip = "The cost agreed for the remedial works",
                AltText = "Box for the agreed cost"
            };
            cost.Style.Location.HeightValue = 6;
            row.Add(cost);
            table.Add(row);

            return table;
        }

        private static void AddSignature(ContainerItem block, string label, string field,
                                         double left, double top, Signing signing)
        {
            var caption = new TextItem
            {
                Text = label,
                Font = new TextStyle { Size = 0.85, Color = new Color(90, 90, 90) }
            };
            caption.Style.Location.Left = (left, Units.Millimeter);
            caption.Style.Location.Top = (top, Units.Millimeter);
            block.Add(caption);

            var box = new FieldItem
            {
                Name = field,
                Kind = FieldKind.Signature,
                Signing = signing,
                Tooltip = signing == Signing.Digital
                    ? $"{label}, signed digitally"
                    : $"{label}, signed by hand",

                // A signature is tagged as a Figure, so it owes a description exactly
                // as a picture does - PDF/UA 7.3 refuses a figure that has none, and a
                // reader would otherwise announce an empty box.
                //
                // Both ways of signing, since 25 August 2026. This said Digital ? null,
                // on the reasoning that a digital one is a Form and a Form needs no
                // alternative text - true of interactive output and untrue of flattened,
                // where no widget is written and the element becomes a Figure like every
                // other. The reason was right and the scope was wrong.
                AltText = $"Box reserved for the {label.ToLowerInvariant()} signature"
            };
            box.Style.Location.Left = (left, Units.Millimeter);
            box.Style.Location.Top = (top + 5, Units.Millimeter);
            box.Style.Location.Width = (75, Units.Millimeter);
            box.Style.Location.Height = (18, Units.Millimeter);
            block.Add(box);
        }

        /// <summary>
        /// The back of every sheet: notes set in two columns that end level, which is
        /// the only place column balancing shows.
        /// </summary>
        private static ContainerItem BuildNotes()
        {
            var notes = new ContainerItem
            {
                Layout = LayoutMode.Stack,
                ColumnCount = 2,
                ColumnGap = 6,
                BalanceColumns = true
            };

            notes.Add(new TextItem
            {
                Text = "NOTES AND METHOD",
                Font = new TextStyle { Size = 1.1, Bold = true }
            });

            for (int note = 1; note <= 14; note++)
            {
                var clause = new TextItem
                {
                    Text = $"Note {note} — These notes apply to page " + "{Page} of {Pages} of the "
                         + "report and are taken to have been read. At vero eos et accusamus et "
                         + "iusto odio dignissimos ducimus qui blanditiis praesentium voluptatum "
                         + "deleniti atque corrupti quos dolores et quas molestias excepturi.",
                    Font = new TextStyle { Size = 0.75, HorizontalAlignment = HorizontalAlignment.Justify }
                };
                clause.Style.Margin.Top = 2;
                notes.Add(clause);
            }

            return notes;
        }

        // ------------------------------------------------------------------

        private static TextItem Section(string text, string bookmark)
        {
            var heading = new TextItem
            {
                Text = text,
                Font = new TextStyle { Size = 1.35, Bold = true },
                Role = PdfRole.Heading1
            };
            heading.Style.Margin.Top = 5;
            heading.Style.Margin.Bottom = 1;
            heading.Bookmark = bookmark;
            heading.BookmarkLevel = 1;
            return heading;
        }

        /// <summary>
        /// A heading, a rule across the page, and a line saying what to look at.
        ///
        /// This document exists to be looked at - it is the third of the three ways a
        /// rendering is checked, and the only one no assertion replaces. Opened in a
        /// reader it was a handsome report and said nothing about itself: a split table
        /// row, a translucent watermark and a signature look like a table, a tint and a
        /// box unless somebody says what they are and what would be wrong.
        ///
        /// The rule is drawn rather than typed, so it is the width of the page whatever
        /// the page turns out to be.
        ///
        /// What the caption must never do is stand in for a check. Saying "two barcodes
        /// below" does not put two barcodes in the file; test 40 still asks the tree
        /// before a byte is written. A sentence that asserts is section 2's "an
        /// assertion that looks for bytes proves nothing", written in English.
        /// </summary>
        private static void Demonstrates(Body body, string title,
                                         string bookmark, string lookFor,
                                         params BaseItem[] keepWith)
        {
            // Each piece asks not to be left at the foot of a page, so the three of them
            // and whatever follows arrive together - a heading stranded above the page
            // break being the oldest fault in typesetting, and one this document
            // produced: the barcodes turned up on a page of their own while
            // "5. References" and its caption stayed behind.
            //
            // They were briefly wrapped in a KeepTogether container instead, which works
            // for short content and never could for a table longer than a page. This
            // asks the right question - stay with what follows - so the size of what
            // follows stops mattering.
            TextItem heading = Section("{HeadingNumber}. " + title, bookmark);
            heading.Style.KeepWithNext = true;
            body.Add(heading);

            var rule = new LineItem
            {
                Pen = new PenStyle { Width = 0.2, Color = new Color(150, 150, 150) }
            };
            rule.Style.Location.Dock = Dock.Left | Dock.Right;
            rule.Style.Margin.Bottom = 1.5;
            rule.Style.KeepWithNext = true;
            body.Add(rule);

            var caption = new TextItem
            {
                Text = lookFor,
                Font = new TextStyle { Size = 0.95, Italic = true, Color = new Color(85, 85, 85) }
            };
            caption.Style.Margin.Bottom = 2;
            caption.Style.KeepWithNext = true;
            body.Add(caption);

            foreach (BaseItem item in keepWith)
                body.Add(item);
        }

        /// <summary>A heading under a section: level two, and numbered as one.</summary>
        private static TextItem Heading(string text, double size)
        {
            var heading = new TextItem
            {
                Text = "{HeadingNumber}. " + text,
                Font = new TextStyle { Size = size, Bold = true },
                Role = PdfRole.Heading2
            };
            heading.Style.Margin.Top = 4;
            heading.Style.Margin.Bottom = 1;
            return heading;
        }

        private static TextItem Prose(string text)
        {
            var item = new TextItem
            {
                Text = text,
                Font = new TextStyle { Size = 1, HorizontalAlignment = HorizontalAlignment.Justify }
            };
            item.Style.Margin.Top = 2;
            return item;
        }

        private static TextItem HeadCell(string text) => new()
        {
            Text = text,
            Font = new TextStyle { Size = 0.9, Bold = true }
        };

        private static TextItem BodyCell(string text,
                                         HorizontalAlignment align = HorizontalAlignment.Left) => new()
        {
            Text = text,
            Font = new TextStyle { Size = 0.9, HorizontalAlignment = align }
        };

        // ------------------------------------------------------------------
        // The data the template is filled from
        // ------------------------------------------------------------------

        /// <summary>
        /// Enough rows that the table runs past the foot of a page and has to continue,
        /// and one deliberately long observation so a single row is itself split - which
        /// is the case that once dropped the short cells of the second half.
        /// </summary>
        internal static string DataJson(int rows = 26)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"ReportNumber\": \"FR-2025-0481\",\n");
            sb.Append("  \"Period\": \"January to June 2025\",\n");
            sb.Append("  \"Client\": \"ACME Corporation\",\n");
            sb.Append("  \"Readings\": [\n");

            string[] status = { "Nominal", "Nominal", "Watch", "Nominal", "Fault" };
            for (int i = 0; i < rows; i++)
            {
                // One row carries a paragraph rather than a phrase, so that row alone is
                // taller than what a page has left and must break across the boundary.
                // Long enough that it cannot fit on any page whole - which is the whole
                // reason it is here. A row that happens to fit proves nothing about
                // splitting, and this one once landed in a table where it never split.
                string observation = i == 7
                    ? "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum "
                      + "dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non "
                      + "proident, sunt in culpa qui officia deserunt mollit anim id est laborum. "
                      + string.Join(" ", Enumerable.Repeat(
                            "Sed ut perspiciatis unde omnis iste natus error sit voluptatem "
                            + "accusantium doloremque laudantium, totam rem aperiam.", 40))
                    : "Consectetur adipiscing elit, sed do eiusmod tempor incididunt.";

                sb.Append("    { \"Site\": \"Site ").Append((char)('A' + i % 26)).Append('-')
                  .Append((i + 101).ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append("\", \"Observation\": \"").Append(observation)
                  .Append("\", \"Reading\": \"")
                  .Append((12.5 + i * 1.37).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(" mV\", \"Status\": \"").Append(status[i % status.Length]).Append("\" }");
                if (i < rows - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("  ],\n");
            sb.Append("  \"Photos\": [\n");
            for (int i = 0; i < 6; i++)
            {
                sb.Append("    { \"Site\": \"Site ").Append((char)('A' + i))
                  .Append("\", \"Image\": \"")
                  .Append(Convert.ToBase64String(Photograph(i)))
                  .Append("\" }");
                if (i < 5) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Pictures, drawn rather than shipped: a repository that carries a
        // photograph carries whatever is in it, and an invented mark cannot be
        // mistaken for a real company's.
        // ------------------------------------------------------------------

        /// <summary>An invented corporate mark: concentric rings, flat colours.</summary>
        internal static byte[] Mark(int size)
        {
            double centre = size / 2.0;
            return PngEncoder.Rgb(size, size, (x, y) =>
            {
                double dx = x - centre, dy = y - centre;
                double r = Math.Sqrt(dx * dx + dy * dy) / (size * 0.47);
                if (r > 1) return ((byte)255, (byte)255, (byte)255);
                if (r > 0.72) return ((byte)196, (byte)62, (byte)48);
                if (r > 0.46) return ((byte)255, (byte)255, (byte)255);
                return ((byte)32, (byte)44, (byte)62);
            });
        }

        /// <summary>
        /// A stand-in photograph: a soft gradient with a horizon, different per index so
        /// the contact sheet does not read as the same picture repeated.
        /// </summary>
        internal static byte[] Photograph(int index, int width = 160, int height = 120)
        {
            double hue = (index * 47 % 360) / 360.0;
            return PngEncoder.Rgb(width, height, (x, y) =>
            {
                double v = (double)y / height;
                bool sky = v < 0.55;
                double shade = sky ? 0.35 + v * 0.9 : 1.15 - v * 0.55;

                (double r, double g, double b) = sky
                    ? (0.35 + hue * 0.3, 0.55 + hue * 0.2, 0.85)
                    : (0.45 + hue * 0.35, 0.42 + hue * 0.2, 0.30);

                byte Clamp(double c) => (byte)Math.Clamp(c * shade * 255, 0, 255);
                return (Clamp(r), Clamp(g), Clamp(b));
            });
        }
    }
}
