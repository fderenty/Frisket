namespace test
{
    /// <summary>
    /// The quote fixture down its two paths - a fillable AcroForm, or flattened with the
    /// ticks and the finger-drawn signatures bound in - and the cryptographic signature
    /// field.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>A quote with the parts a customer is meant to fill in.</summary>
        static Report Quote()
        {
            var report = new Report
            {
                Title = "Quote Q-2025-0042",
                Dimension = new() { Width = 210, Height = 297 },
                RootFontSize = 2.2,
                GeneratedAt = new DateTimeOffset(2025, 10, 2, 9, 0, 0, TimeSpan.Zero),
                DefaultFont = new() { Family = DmsFont },
                Header = new() { Layout = LayoutMode.Stack },
                Body = new(),
                Footer = new() { Layout = LayoutMode.Stack }
            };
            report.Section.Style.Padding = new() { Left = 15, Top = 12, Right = 15, Bottom = 12 };

            report.Header!.Add(T("Quote {Number}", 1.6, bold: true));
            report.Header.Add(Rule(0.3));

            // --- what the customer chooses ------------------------------------
            var choices = new ContainerItem { Layout = LayoutMode.Stack };
            choices.Style.Margin = new() { Top = 3, Bottom = 3 };
            choices.Add(T("Services selected", 1.1, bold: true));

            foreach ((string name, string label, bool ticked) in new[]
            {
                ("opt.revision", "Full overhaul", true),
                ("opt.freins", "Remplacement plaquettes", false),
                ("opt.pneus", "Montage pneumatiques", false)
            })
            {
                var row = new ContainerItem { Layout = LayoutMode.Absolute };
                row.Style.Location.Height = (6, Units.Millimeter);
                row.Style.Location.Dock = Dock.Left | Dock.Right;

                var box = new FieldItem
                {
                    Name = name, Kind = FieldKind.Checkbox,
                    // The state comes from the data; the flag here is only the fallback
                    // used when the template is rendered blank.
                    Value = "{Choices." + char.ToUpperInvariant(name[4]) + name[5..] + "}"
                };
                box.Style.Location.Left = (0, Units.Millimeter);
                box.Style.Location.Top = (0, Units.Millimeter);
                box.Style.Location.Width = (4.5, Units.Millimeter);
                box.Style.Location.Height = (4.5, Units.Millimeter);
                row.Add(box);
                row.Add(At(7, 0, label));

                choices.Add(row);
            }
            report.Body!.Add(choices);

            // --- what the customer writes -------------------------------------
            var details = new ContainerItem { Layout = LayoutMode.Stack };
            details.Add(T("Your details", 1.1, bold: true));

            // The token is spelled out beside the field rather than cut out of its name
            // by index: the old form assumed the prefix was exactly seven characters, so
            // renaming a field silently produced a token pointing nowhere.
            foreach ((string name, string token, string label, bool multi) in new[]
            {
                ("customer.name",  "{Customer.Name}",  "Name", false),
                ("customer.phone", "{Customer.Phone}", "Telephone", false),
                ("customer.notes", "{Customer.Notes}", "Notes", true)
            })
            {
                var row = new ContainerItem { Layout = LayoutMode.Absolute };
                row.Style.Location.Dock = Dock.Left | Dock.Right;
                row.Style.Margin = new() { Top = 1.5 };
                row.Add(At(0, 1, label, 0.9));

                var input = new FieldItem
                {
                    Name = name,
                    Kind = FieldKind.Text,
                    Value = token,
                    Multiline = multi,
                    Tooltip = label,
                    MaxLength = multi ? 0 : 60
                };
                input.Style.Location.Left = (32, Units.Millimeter);
                input.Style.Location.Top = (0, Units.Millimeter);
                input.Style.Location.Dock = Dock.Right;
                row.Add(input);

                details.Add(row);
            }
            report.Body.Add(details);

            // --- and where it is signed ----------------------------------------
            var sign = new ContainerItem { Layout = LayoutMode.Absolute };
            sign.Style.Location.Dock = Dock.Left | Dock.Right;
            sign.Style.Margin = new() { Top = 8 };
            sign.Add(At(0, 0, "Agreed, on", 0.9));

            var date = new FieldItem { Name = "signature.date", Kind = FieldKind.Text, Tooltip = "jj/mm/aaaa" };
            date.Style.Location.Left = (30, Units.Millimeter);
            date.Style.Location.Top = (0, Units.Millimeter);
            date.Style.Location.Width = (35, Units.Millimeter);
            sign.Add(date);

            var scrawl = new FieldItem
            {
                Name = "signature.customer",
                Kind = FieldKind.Signature,
                Tooltip = "Customer's signature"
            };
            scrawl.Style.Location.Right = (0, Units.Millimeter);
            scrawl.Style.Location.Top = (0, Units.Millimeter);
            scrawl.Style.Location.Width = (70, Units.Millimeter);
            sign.Add(scrawl);

            report.Body.Add(sign);

            report.Footer!.Add(T("Page {Page} / {Pages}", 0.8, false, HorizontalAlignment.Right));
            return report;
        }

        /// <summary>Every field in a report, wherever it sits in the tree.</summary>
        static IEnumerable<FieldItem> Fields(Report report)
        {
            IEnumerable<BaseItem> Walk(IEnumerable<BaseItem>? items)
            {
                foreach (BaseItem item in items ?? Enumerable.Empty<BaseItem>())
                {
                    yield return item;
                    if (item is ContainerItem group)
                        foreach (BaseItem child in Walk(group.Items))
                            yield return child;
                }
            }

            var bands = new List<ContainerItem?> { report.Header, report.Body, report.Footer };
            return Walk(bands.Where(b => b != null).SelectMany(b => b!.Items)).OfType<FieldItem>();
        }

        /// <summary>The object numbers listed in the first array under the given key.</summary>
        static List<int> Refs(string pdf, string key)
        {
            var array = System.Text.RegularExpressions.Regex.Match(pdf, System.Text.RegularExpressions.Regex.Escape(key) + @"\s*\[([^\]]*)\]");
            return System.Text.RegularExpressions.Regex.Matches(array.Groups[1].Value, @"(\d+) 0 R")
                       .Select(m => int.Parse(m.Groups[1].Value)).ToList();
        }

        /// <summary>
        /// A document whose only content is fields still comes out as a document.
        ///
        /// The form's default appearance names a font, and the font it names is whichever
        /// one the file happens to have embedded - which is none, when nothing anywhere
        /// draws a glyph. A signature page with a rule, a box and no words is an entirely
        /// ordinary thing to ask for, and it is the shape nothing in this suite had.
        ///
        /// Raised by the signature review as "ToArray throws"; what it does is written
        /// down here either way, because the claim and the behaviour are both worth
        /// having in one place.
        /// </summary>
        [Fact]
        public void Test82_APageOfNothingButFields()
        {
            const string testName = "Test 82";
            Console.WriteLine("Test 82: a page whose only content is a place to sign");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            var report = new Report
            {
                Title = "Signature page", Language = "en-GB",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };

            // Not one glyph anywhere: no text item, no tooltip, no value.
            var field = new FieldItem
            {
                Name = "customer.sign", Kind = FieldKind.Signature,
                Signing = Signing.Digital, AltText = null
            };
            field.Style.Location.HeightValue = 25;
            field.Outline = new PenStyle { Width = 0.3 };
            report.Body!.Add(field);
            report.Reattach();

            var pdf = new PdfBackend(library)
            {
                Conformance = PdfConformance.PdfA3b, Language = "en-GB", Compress = false
            };
            ReportRenderer.Render(report, pdf);

            byte[] bytes;
            string thrown = "";
            try
            {
                bytes = pdf.ToArray();
            }
            catch (Exception e)
            {
                bytes = Array.Empty<byte>();
                thrown = e.GetType().Name + ": " + e.Message;
            }

            Console.WriteLine($"    {bytes.Length} octets" + (thrown.Length > 0 ? $" — {thrown}" : ""));
            AssertEqual(testName, "a document comes out at all", true, thrown.Length == 0, 0);
            AssertEqual(testName, "and it is a file", true, bytes.Length > 1000, 0);

            string written = System.Text.Encoding.Latin1.GetString(bytes);

            // The form is the whole document, so it has to be there. Without a font
            // embedded the AcroForm was skipped and the widgets were never reserved -
            // and a page then lists an annotation that was never written, which is
            // section 2's own example: "/Annots present, and the form worked in no
            // reader, because /Annots pointed at object 0".
            AssertEqual(testName, "the form is written", true, written.Contains("/AcroForm"), 0);
            AssertEqual(testName, "with a widget in it", true, written.Contains("/Subtype /Widget"), 0);

            AssertEqual(testName, "and no annotation points at object zero",
                false, System.Text.RegularExpressions.Regex.IsMatch(written, @"/Annots \[\s*0 0 R"), 0);

            // Every widget the pages list really is an object in the file.
            var listed = System.Text.RegularExpressions.Regex.Matches(written, @"/Annots \[([^\]]*)\]")
                .SelectMany(m => System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, @"(\d+) 0 R")
                                     .Select(r => int.Parse(r.Groups[1].Value)))
                .ToList();

            Console.WriteLine($"    annotations listees : {string.Join(", ", listed)}");
            AssertEqual(testName, "a page lists at least one", true, listed.Count > 0, 0);

            foreach (int number in listed)
            {
                // \\n, not Environment.NewLine. A PDF writes its own line
                // endings, and on Windows the framework constant is a carriage return
                // and a newline - so this looked for something the file never contains
                // and reported the object missing when it was there.
                AssertEqual(testName, $"object {number} exists",
                    true, written.Contains("\n" + number + " 0 obj"), 0);
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Two widgets never share a name, and a form with something to sign says so.
        ///
        /// Both halves are the same kind of fault as everything else in this area: the
        /// file is well formed, every reader opens it, and it does the wrong thing in
        /// silence.
        ///
        /// A name is not a label in a PDF - it is the identity of the field. Two widgets
        /// carrying one name are one field with two appearances, so typing in either
        /// fills both, and a form-processing back end receives one value where two were
        /// asked for.
        /// </summary>
        [Fact]
        public void Test84_NamesAreUniqueAndAFormToSignSaysSo()
        {
            const string testName = "Test 84";
            Console.WriteLine("Test 84: one name per widget, and /SigFlags on a form to be signed");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            static FieldItem Box(string name, FieldKind kind = FieldKind.Text,
                                 Signing signing = Signing.ByHand)
            {
                var field = new FieldItem
                {
                    Name = name, Kind = kind, Signing = signing,
                    Tooltip = $"Field {name}", AltText = $"Field {name}"
                };
                field.Style.Location.HeightValue = 8;
                return field;
            }

            string Written(params FieldItem[] fields)
            {
                var report = new Report
                {
                    Title = "Names", Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack }
                };
                report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };
                foreach (FieldItem field in fields)
                    report.Body!.Add(field);
                report.Reattach();

                var pdf = new PdfBackend(library) { Interactive = true, Compress = false };
                ReportRenderer.Render(report, pdf);
                return System.Text.Encoding.Latin1.GetString(pdf.ToArray());
            }

            // --- a name handed out is a name taken -----------------------------------
            // Two fields want one name, so the second is given an invented one. Before
            // 25 August 2026 that invented name was handed out without being recorded,
            // so a later field asking for it in so many words was given it too - and the
            // collision walked back in through the door the fix walked out of.
            //
            // The bait is every shape the invention might take rather than the one it
            // takes today: separator and counter are an implementation detail, and a
            // sample calibrated on the current derivation is a sample outside the window
            // of the fault. Written with only "a-2" for bait, this test passed with the
            // fault put straight back - the old code derived "a.1", which collides with
            // nothing here.
            // Declared in this order, and `asked` below repeats it: the check that each
            // name stayed on its own field can only be written against the order they
            // went in, since /T comes out in field order.
            string clashing = Written(Box("a"), Box("a"),
                                      Box("a-1"), Box("a-2"), Box("a.1"), Box("a.2"));

            var names = System.Text.RegularExpressions.Regex
                .Matches(clashing, @"/T \(([^)]*)\)")
                .Select(m => m.Groups[1].Value)
                .ToList();

            Console.WriteLine($"    noms ecrits : {string.Join(", ", names)}");
            AssertEqual(testName, "six fields, six widgets", 6, names.Count, 0);
            AssertEqual(testName, "and no two of them share a name",
                6, names.Distinct(StringComparer.Ordinal).Count(), 0);

            // The separator is not a period: a period separates a parent field from its
            // child, so "a.1" hangs the widget under a parent that is not there.
            AssertEqual(testName, "and none of the invented names claims a parent",
                false, names.Except(new[] { "a", "a-1", "a-2", "a.1", "a.2" })
                            .Any(n => n.Contains('.')), 0);

            // And every name an author wrote is still on THAT AUTHOR'S OWN field. Not
            // colliding is half the job: an invented name that a later field asks for by
            // name does not collide - the loop sees to that - it renames the author's
            // field instead, and a form back end then receives the value under a key that
            // is in no template.
            //
            // Order matters here, and is the whole point: "a-1" and "a-2" are declared
            // AFTER the duplicate that would otherwise be given one of them.
            //
            // Asked field by field, and that is not pedantry: written as "every one of
            // those strings appears somewhere", this passed with the guard removed. The
            // duplicate had taken "a-2" and the author's field had become "a-2-2", so all
            // six names were still distinct and all five strings were still present - the
            // assertion was true of a document where exactly the wrong field carried the
            // name. Section 2 in its own small way: what matters is that the name and the
            // field it was written for reach each other.
            string[] asked = { "a", "a", "a-1", "a-2", "a.1", "a.2" };
            var stolen = new List<string>();
            for (int i = 0; i < asked.Length && i < names.Count; i++)
            {
                // A name asked for twice cannot be kept twice; every other one must be.
                if (asked.Count(n => n == asked[i]) == 1 && names[i] != asked[i])
                    stolen.Add($"{asked[i]} became {names[i]}");
            }

            AssertEqual(testName, "and every name the author wrote is still on its own field",
                "", string.Join("; ", stolen), 0);

            // --- a form with something to sign says so -------------------------------
            // Without /SigFlags a reader shows the box and offers no way to sign it: the
            // same document, looking the same, doing nothing.
            string toSign = Written(Box("name"),
                                    Box("endorsement", FieldKind.Signature, Signing.Digital));
            string toFill = Written(Box("name"), Box("reference"));

            Console.WriteLine($"    /SigFlags : {CountOccurrences(toSign, "/SigFlags 1")} a signer, "
                            + $"{CountOccurrences(toFill, "/SigFlags")} sans signature");

            AssertEqual(testName, "a form holding a signature declares SignaturesExist",
                1, CountOccurrences(toSign, "/SigFlags 1"), 0);

            // The control, and it is the half that matters: a claim made on every form
            // is not a claim. Nothing here is signed and nothing can be, so the file
            // must say nothing rather than say it anyway.
            AssertEqual(testName, "and a form with nothing to sign claims nothing",
                0, CountOccurrences(toFill, "/SigFlags"), 0);

            // AppendOnly asserts that a full rewrite would invalidate a signature the
            // file holds. Every signature written here is empty, waiting for someone.
            AssertEqual(testName, "and it does not claim AppendOnly over an unsigned field",
                0, CountOccurrences(toSign, "/SigFlags 2") + CountOccurrences(toSign, "/SigFlags 3"), 0);

            Console.WriteLine();
        }

        /// <summary>
        /// A control used as a table cell has to be two things at once, and each of them
        /// alone breaks the other.
        ///
        /// Tagged as a cell - which is what it resolves to, since PdfRoles.Resolve answers
        /// TableCell for anything whose parent is a TableRow before it ever looks at
        /// FieldItem - its widget is reachable from nothing: PDF/UA 7.18.1 wants every
        /// annotation in the structure tree, 7.18.4 wants it nested in a Form, and the
        /// file offered neither. Tagged as a Form instead, veraPDF answers "TR element may
        /// contain only TH and TD elements", and the row is then one cell short of its
        /// header - a second rule broken by the first fix.
        ///
        /// Both were tried in that order on the conformance document, and this is what the
        /// validator asked for: one item, two elements. The cell keeps its place in the
        /// row and the Form under it takes the widget.
        ///
        /// Test 53 validates it from the outside, which is the check that counts - and it
        /// declines when veraPDF is not installed. This one asks the same question of the
        /// bytes, so the answer is not conditional on a machine.
        /// </summary>
        [Fact]
        public void Test86_AControlInATableCellIsReachable()
        {
            const string testName = "Test 86";
            Console.WriteLine("Test 86: a control used as a table cell");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            var report = new Report
            {
                Title = "Costs", Language = "en-GB",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };
            report.Body!.Add(new TextItem { Text = "Costs", Role = PdfRole.Heading1,
                                            Font = new() { Size = 4 } });

            var table = new TableItem { Grid = new PenStyle { Width = 0.15 } };
            table.Columns.Add(new TableColumn());
            table.Columns.Add(new TableColumn { Width = 40, WidthUnit = Units.Millimeter });

            var head = new TableRow { IsHeader = true };
            head.Add(new TextItem { Text = "Work", Font = new() { Bold = true } });
            head.Add(new TextItem { Text = "Cost", Font = new() { Bold = true } });
            table.Add(head);

            var row = new TableRow();
            row.Add(new TextItem { Text = "Remedial works" });

            var cost = new FieldItem
            {
                Name = "agreed_cost", Kind = FieldKind.Text,
                Tooltip = "The cost agreed", AltText = "Box for the agreed cost"
            };
            cost.Style.Location.HeightValue = 6;
            row.Add(cost);
            table.Add(row);
            report.Body!.Add(table);
            report.Reattach();

            var backend = new PdfBackend(library)
            {
                Conformance = PdfConformance.PdfA3b, Language = "en-GB",
                Tagged = true, Interactive = true, Compress = false
            };
            ReportRenderer.Render(report, backend);
            string pdf = System.Text.Encoding.Latin1.GetString(backend.ToArray());

            // --- the widget really was written, or nothing below proves anything ------
            var widget = System.Text.RegularExpressions.Regex.Match(
                pdf, @"(\d+) 0 obj\s*<< /Type /Annot /Subtype /Widget");
            AssertEqual(testName, "the cell is a control at all", true, widget.Success, 0);

            string number = widget.Groups[1].Value;
            AssertEqual(testName, "and the page names it under /Annots", true,
                System.Text.RegularExpressions.Regex.IsMatch(
                    pdf, @"/Annots \[[^\]]*\b" + number + @" 0 R"), 0);

            // --- every structure element, so the two ends can be joined ---------------
            var body = new Dictionary<string, string>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         pdf, @"(\d+) 0 obj\s*(<< /Type /StructElem .*?)endobj",
                         System.Text.RegularExpressions.RegexOptions.Singleline))
                body[m.Groups[1].Value] = m.Groups[2].Value;

            string Tag(string obj) =>
                System.Text.RegularExpressions.Regex.Match(body.GetValueOrDefault(obj, ""), @"/S /(\w+)")
                      .Groups[1].Value;

            // Which element points AT the widget. Not "a Form exists" and not "an OBJR
            // exists" - the two reaching each other is the whole question, and the form
            // that worked in no reader had both of those and /Annots pointing at object 0.
            string owner = body.FirstOrDefault(e =>
                System.Text.RegularExpressions.Regex.IsMatch(
                    e.Value, @"/K << /Type /OBJR /Obj " + number + @" 0 R >>")).Key ?? "";

            Console.WriteLine($"    widget {number} <- element {(owner.Length > 0 ? owner : "aucun")} "
                            + $"({(owner.Length > 0 ? Tag(owner) : "-")})");

            AssertEqual(testName, "an element in the tree points at that widget", true,
                owner.Length > 0, 0);
            AssertEqual(testName, "and it is a Form, which is where a widget belongs",
                "Form", Tag(owner), 0);

            // --- and the cell is still a cell -----------------------------------------
            string parent = System.Text.RegularExpressions.Regex
                .Match(body.GetValueOrDefault(owner, ""), @"/P (\d+) 0 R").Groups[1].Value;

            Console.WriteLine($"    et son parent est {parent} ({Tag(parent)})");
            AssertEqual(testName, "the Form sits inside the cell, not in place of it",
                "TD", Tag(parent), 0);

            // A row may hold nothing but TH and TD, and both rows must count the same.
            var rows = body.Where(e => Tag(e.Key) == "TR").ToList();
            AssertEqual(testName, "both rows are in the tree", 2, rows.Count, 0);

            var counts = rows.Select(r =>
                System.Text.RegularExpressions.Regex.Matches(
                    System.Text.RegularExpressions.Regex.Match(r.Value, @"/K \[([^\]]*)\]").Groups[1].Value,
                    @"(\d+) 0 R").Select(m => m.Groups[1].Value).ToList()).ToList();

            Console.WriteLine("    lignes : " + string.Join(" | ", counts.Select(c =>
                string.Join(",", c.Select(Tag)))));

            AssertEqual(testName, "and every one of their children is a cell", true,
                counts.SelectMany(c => c).All(o => Tag(o) is "TD" or "TH"), 0);
            AssertEqual(testName, "and the rows count the same", 1,
                counts.Select(c => c.Count).Distinct().Count(), 0);

            // --- and a row that splits still declares one control ---------------------
            // A row too tall for a page is drawn whole in the first part and "present but
            // empty in the rest", so it keeps its full count of cells on the continuation
            // page (section 11). That second placement is a rectangle of zero height, and
            // for anything drawn it is an empty cell - for a field it was a SECOND widget:
            // unfillable, invisible, listed in /AcroForm /Fields under an invented name
            // that is in no template, and handed to a form back end as a key nobody wrote.
            //
            // The row has to be taller than a page or this proves nothing: a row that fits
            // never splits, and one that never splits could not lay its cells twice.
            var tall = new Report
            {
                Title = "A row taller than a page", Language = "en-GB",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            tall.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };
            tall.Body!.Add(new TextItem { Text = "Schedule", Role = PdfRole.Heading1,
                                          Font = new() { Size = 4 } });

            var long_ = new TableItem { Grid = new PenStyle { Width = 0.15 } };
            long_.Columns.Add(new TableColumn());
            long_.Columns.Add(new TableColumn { Width = 40, WidthUnit = Units.Millimeter });

            var longRow = new TableRow();
            longRow.Add(new TextItem
            {
                // A long paragraph rather than explicit line breaks: it wraps to well
                // over a page, and there is no escape to get wrong on the way in.
                Text = string.Concat(Enumerable.Repeat(
                    "Remedial works as scheduled and agreed for this item, carried out "
                    + "under the terms set out above. ", 260))
            });

            var once = new FieldItem
            {
                Name = "agreed_cost", Kind = FieldKind.Text,
                Tooltip = "The cost agreed", AltText = "Box for the agreed cost"
            };
            once.Style.Location.HeightValue = 6;
            longRow.Add(once);
            long_.Add(longRow);
            tall.Body!.Add(long_);
            tall.Reattach();

            var second = new PdfBackend(library)
            {
                Conformance = PdfConformance.PdfA3b, Language = "en-GB",
                Tagged = true, Interactive = true, Compress = false
            };
            LayoutResult laid = tall.Resolve(new FontTextMeasurer(library));
            ReportRenderer.Render(tall, laid, second);
            string split = System.Text.Encoding.Latin1.GetString(second.ToArray());

            int halves = laid.Pages.Sum(p => p.Items.Count(i => i.Source == longRow));
            int widgets = CountOccurrences(split, "/Subtype /Widget");
            Console.WriteLine($"    la ligne est posee {halves} fois, sur {laid.Pages.Count} pages, "
                            + $"pour {widgets} controle(s)");

            // The control first: a row laid once could not produce a second widget, so an
            // assertion of "one widget" would be about a document that never had the fault.
            AssertEqual(testName, "the row really was laid more than once", true, halves > 1, 0);
            AssertEqual(testName, "and one declared control is one widget", 1, widgets, 0);

            Console.WriteLine();
        }

        /// <summary>
        /// Everything that has to be true of a document before a signing platform can do
        /// anything with it - and everything that has to still be true of the one it
        /// hands back.
        ///
        /// One list, asked twice, and that is the whole design. A platform is sent a PDF
        /// and returns a PDF; the return leg is a network call this project does not
        /// make, but the QUESTION asked of the file that comes back is exactly the
        /// question asked of the file that went out, plus "is it still valid". Written as
        /// two lists they would drift, and the drift would be invisible: a document that
        /// went out fit to sign and came back missing its attachment looks, on screen,
        /// identical.
        ///
        /// The risk this exists for is not ours and cannot be reasoned about. A signature
        /// applied as an incremental update preserves the file underneath it; a platform
        /// that rewrites the document instead can drop the PDF/A claim, the Factur-X
        /// attachment and the whole structure tree while producing a perfectly valid
        /// signed PDF. Which of the two a given provider does is a fact about that
        /// provider, discovered by sending it a file and reading what comes back.
        /// </summary>
        [Fact]
        public void Test89_FitToHandToASigningPlatform()
        {
            const string testName = "Test 89";
            Console.WriteLine("Test 89: fit to hand to a signing platform");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            string examples = Path.Combine(RepositoryRoot(), "examples");
            Report report = Report.FromFile(Showcase.WriteFiles(examples));
            report = ReportBinder.Bind(report,
                File.ReadAllText(Path.Combine(examples, Showcase.DataFile)));

            string Written(bool interactive)
            {
                var pdf = new PdfBackend(library)
                {
                    Conformance = PdfConformance.PdfA3b, Tagged = true,
                    Interactive = interactive, Compress = false
                };

                // The half of the list below that is about what must SURVIVE can only be
                // asked of a document that carries it in the first place.
                pdf.AttachFacturX(FacturXXml());

                ReportRenderer.Render(report, pdf);
                return System.Text.Encoding.Latin1.GetString(pdf.ToArray());
            }

            string outbound = Written(interactive: true);

            List<string> faults = NotFitToSign(outbound);
            foreach (string fault in faults)
                Console.WriteLine($"    ✗ {fault}");
            Console.WriteLine($"    {(faults.Count == 0 ? "rien ne l'empeche" : $"{faults.Count} obstacle(s)")}");

            AssertEqual(testName, "the document we would send is one a platform can act on",
                "", string.Join("; ", faults), 0);

            // --- and the control, which is the only reason the list above means anything -
            // Flattened, the same template draws a picture of a place to sign. Nothing on
            // the page differs, no reader complains, and a platform finds no field to
            // fill. It is the one fault in this whole area that is invisible end to end,
            // which is why `frisket check` has a rule for it and why it is checked here
            // rather than trusted.
            List<string> flattened = NotFitToSign(Written(interactive: false));
            Console.WriteLine($"    aplati : {flattened.Count} obstacle(s) - "
                            + string.Join("; ", flattened.Take(2)));

            AssertEqual(testName, "and a flattened one is not", true, flattened.Count > 0, 0);
            AssertEqual(testName, "for want of a signature field to fill", true,
                flattened.Any(f => f.Contains("signature field")), 0);

            // --- and the list rehearsed on bytes it did not make -----------------------
            // This list exists to judge a file somebody else produced, so "it says nothing
            // about a good document" is half a check. Every complaint in it is fired here
            // by spoiling the real document in the one way that complaint is about.
            //
            // Written without this, two of the three sabotages went green: the showcase
            // has no two fields sharing a name and no signature missing from /AcroForm,
            // so those two branches were true for want of matter. A sample outside the
            // window of the fault describes a document that never had the problem.
            string signatureName = System.Text.RegularExpressions.Regex
                .Match(outbound, @"/T \(([^)]*)\)[^>]*?/FT /Sig").Groups[1].Value;
            if (signatureName.Length == 0)
            {
                // The pattern above reads across a widget's whole dictionary, so it will
                // stop working the day the key order changes. Better to say so than to
                // silently rehearse nothing.
                signatureName = System.Text.RegularExpressions.Regex
                    .Matches(outbound, @"(\d+) 0 obj\s*(<< /Type /Annot.*?)endobj",
                             System.Text.RegularExpressions.RegexOptions.Singleline)
                    .Where(m => m.Groups[2].Value.Contains("/FT /Sig"))
                    .Select(m => System.Text.RegularExpressions.Regex
                        .Match(m.Groups[2].Value, @"/T \(([^)]*)\)").Groups[1].Value)
                    .FirstOrDefault("");
            }

            AssertEqual(testName, "the rehearsal below has a signature to spoil",
                true, signatureName.Length > 0, 0);

            string firstName = System.Text.RegularExpressions.Regex
                .Match(outbound, @"/T \(([^)]*)\)").Groups[1].Value;

            (string what, string spoiled, string expect)[] rehearsal =
            {
                ("no declaration", outbound.Replace("/SigFlags 1", ""), "declare"),
                ("a name used twice",
                 outbound.Replace($"/T ({signatureName})", $"/T ({firstName})"), "one name"),
                ("the signature in no /AcroForm /Fields",
                 System.Text.RegularExpressions.Regex.Replace(
                     outbound, @"(/AcroForm << /Fields \[)[^\]]*", "$1"), "/AcroForm /Fields"),
                ("no attachment", outbound.Replace("/AFRelationship", "/Was"), "attachment"),
                ("no structure tree", outbound.Replace("/StructTreeRoot", "/Was"), "structure tree"),
                ("no language", outbound.Replace("/Lang ", "/Was "), "language")
            };

            var mute = new List<string>();
            foreach ((string what, string spoiled, string expect) in rehearsal)
            {
                List<string> said = NotFitToSign(spoiled);
                Console.WriteLine($"    {what,-38} -> {(said.Count == 0 ? "rien" : said[0])}");
                if (!said.Any(f => f.Contains(expect, StringComparison.Ordinal)))
                    mute.Add($"{what} passed unremarked");
            }

            AssertEqual(testName, "every complaint in the list can be made",
                "", string.Join("; ", mute), 0);

            Console.WriteLine();
        }

        /// <summary>
        /// What would stop a signing platform, or stop us accepting what it returns.
        /// Empty means fit.
        ///
        /// Two halves, and they are asked together on purpose. The first is what a
        /// platform NEEDS - a signature field it can find, name and fill. The second is
        /// what we need to still be there afterwards: the conformance claim, the
        /// attachment, the structure tree. Those are not the platform's concern and it
        /// has no reason to preserve them.
        ///
        /// Written to take bytes rather than a Report, because the file that comes back
        /// is bytes and nothing else - there is no model behind it to ask.
        /// </summary>
        private static List<string> NotFitToSign(string pdf)
        {
            var faults = new List<string>();

            string R(string pattern) =>
                System.Text.RegularExpressions.Regex.Match(pdf, pattern).Value;

            // --- what the platform needs ---------------------------------------------
            // The whole object, then asked what it is. Written as one pattern reaching
            // for /FT /Sig it found nothing: a widget carries /MK << ... >> before it,
            // and any "not a bracket" run stops dead at that. The regex was wrong and
            // the file was right, which took reading the bytes to tell apart.
            var signatures = System.Text.RegularExpressions.Regex
                .Matches(pdf, @"(\d+) 0 obj\s*(<< /Type /Annot /Subtype /Widget.*?)endobj",
                         System.Text.RegularExpressions.RegexOptions.Singleline)
                .Where(m => m.Groups[2].Value.Contains("/FT /Sig"))
                .Select(m => m.Groups[1].Value).ToList();

            if (signatures.Count == 0)
                faults.Add("no signature field to fill");

            if (!pdf.Contains("/SigFlags 1"))
            {
                // Without it a reader shows the box and offers no way to sign: the same
                // document, looking the same, doing nothing.
                faults.Add("the form does not declare that it holds a signature");
            }

            string fields = R(@"/AcroForm << /Fields \[[^\]]*\]");
            foreach (string number in signatures)
            {
                // Named, or a platform cannot say which one to sign - and two widgets
                // sharing a name are one field with two appearances.
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        pdf, @"\b" + number + @" 0 obj\s*<<[^>]*?/T \("))
                    faults.Add($"signature field {number} has no name to target");

                if (!System.Text.RegularExpressions.Regex.IsMatch(fields, @"\b" + number + @" 0 R"))
                    faults.Add($"signature field {number} is in no /AcroForm /Fields");

                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        pdf, @"/Annots \[[^\]]*\b" + number + @" 0 R"))
                    faults.Add($"signature field {number} is on no page");
            }

            var names = System.Text.RegularExpressions.Regex
                .Matches(pdf, @"/T \(([^)]*)\)").Select(m => m.Groups[1].Value).ToList();
            if (names.Count != names.Distinct(StringComparer.Ordinal).Count())
                faults.Add("two fields answer to one name");

            // --- what has to survive the round trip ------------------------------------
            if (!pdf.Contains("pdfaid:part") || !pdf.Contains("pdfaid:conformance"))
                faults.Add("the PDF/A claim is gone");

            if (!pdf.Contains("/AFRelationship") || !pdf.Contains("factur-x.xml"))
                faults.Add("the Factur-X attachment is gone");

            if (!pdf.Contains("/StructTreeRoot"))
                faults.Add("the structure tree is gone");

            if (!pdf.Contains("/Lang "))
                faults.Add("the document no longer says what language it is in");

            return faults;
        }

        [Fact]
        public void Test30_FormFields()
        {
            var testName = "Test 30: Form Fields";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());

            // Nothing filled in yet, so an unanswered token blanks rather than showing.
            Report quote = ReportBinder.Bind(Quote(), """{ "Number": "D-2025-0042" }""",
                                             new BindingOptions { MissingField = MissingField.Blank });

            // --- the fields lay out like anything else --------------------------
            LayoutResult layout = quote.Resolve(new FontTextMeasurer(library));
            var placed = layout.Pages[0].Items.Where(p => p.Source is FieldItem).ToList();

            AssertEqual(testName, "every field placed", 8, placed.Count, 0);
            AssertEqual(testName, "all on the page", true,
                placed.All(p => p.Frame.Top > 0 && p.Frame.Bottom < 297), 0);
            AssertEqual(testName, "a checkbox is square", true,
                placed.Where(p => ((FieldItem)p.Source).Kind == FieldKind.Checkbox)
                      .All(p => Math.Abs(p.Frame.Width - p.Frame.Height) < 0.01), 0);
            AssertEqual(testName, "a text field sized itself a line tall", true,
                placed.First(p => ((FieldItem)p.Source).Name == "customer.name").Frame.Height is > 3 and < 8, 0);
            AssertEqual(testName, "a multiline one is taller", true,
                placed.First(p => ((FieldItem)p.Source).Name == "customer.notes").Frame.Height
                > placed.First(p => ((FieldItem)p.Source).Name == "customer.phone").Frame.Height * 2, 0);

            // --- the PDF carries a real form -------------------------------------
            var pdf = new PdfBackend(library);
            ReportRenderer.Render(quote, pdf);
            string text = System.Text.Encoding.Latin1.GetString(pdf.ToArray());

            AssertEqual(testName, "there is a form", true, text.Contains("/AcroForm"), 0);
            AssertEqual(testName, "one widget per fillable field", 7, CountOccurrences(text, "/Subtype /Widget"), 0);
            AssertEqual(testName, "checkboxes are buttons", 3, CountOccurrences(text, "/FT /Btn"), 0);
            AssertEqual(testName, "text fields are text", 4, CountOccurrences(text, "/FT /Tx"), 0);
            // Signed by hand, so no widget at all: written as one, a reader that supports
            // signing offers a certificate dialog in place of the ruled box.
            AssertEqual(testName, "a hand-signed area is not a form field", 0, CountOccurrences(text, "/FT /Sig"), 0);

            // The cryptographic kind is still available, per field: a quote signed by the
            // customer with a pen and a copy countersigned by someone who holds a
            // certificate are the same section asking two different things.
            Report signed = Quote();
            foreach (FieldItem area in Fields(signed).Where(f => f.Kind == FieldKind.Signature))
                area.Signing = Signing.Digital;

            var digital = new PdfBackend(library);
            ReportRenderer.Render(signed, digital);
            string cryptographic = System.Text.Encoding.Latin1.GetString(digital.ToArray());

            AssertEqual(testName, "asked for, it is a real signature field", 1,
                CountOccurrences(cryptographic, "/FT /Sig"), 0);
            AssertEqual(testName, "and it joins the form", 8,
                CountOccurrences(cryptographic, "/Subtype /Widget"), 0);
            AssertEqual(testName, "the choice survives the round trip", "Digital",
                Report.FromJson(signed.ToJson()) is Report back
                    ? Fields(back).First(f => f.Kind == FieldKind.Signature).Signing.ToString()
                    : "?", 0);
            AssertEqual(testName, "the page lists its widgets", true, text.Contains("/Annots ["), 0);

            // And the references resolve. They used to say "0 0 R" eight times over: the
            // page was written before the widgets had object numbers. Every assertion
            // above still passed, because each looked for bytes rather than for whether
            // one part of the file could reach another - which is all a reader does.
            var annots = Refs(text, "/Annots");
            var declared = Refs(text, "/Fields");
            var widgets = System.Text.RegularExpressions.Regex
                .Matches(text, @"(\d+) 0 obj\s*<< /Type /Annot")
                .Select(m => int.Parse(m.Groups[1].Value)).OrderBy(n => n).ToList();

            AssertEqual(testName, "no widget reference points at nothing", 0, annots.Count(n => n == 0), 0);
            AssertEqual(testName, "the page reaches every widget", true, annots.OrderBy(n => n).SequenceEqual(widgets), 0);
            AssertEqual(testName, "and the form declares the same ones", true,
                declared.OrderBy(n => n).SequenceEqual(widgets), 0);
            AssertEqual(testName, "fields are named", true, text.Contains("(customer.name)") && text.Contains("(opt.revision)"), 0);
            AssertEqual(testName, "a tooltip travels", true, text.Contains("(jj/mm/aaaa)"), 0);
            // Nothing was filled in, so every box comes out clear - and a token the data
            // never answered must not read as a tick.
            AssertEqual(testName, "every box starts clear", 3, CountOccurrences(text, "/V /Off"), 0);
            AssertEqual(testName, "none starts ticked", false, text.Contains("/V /Yes"), 0);
            AssertEqual(testName, "multiline is flagged", true, text.Contains("/Ff 4096"), 0);
            AssertEqual(testName, "a length limit travels", true, text.Contains("/MaxLen 60"), 0);
            AssertEqual(testName, "widgets print", true, text.Contains("/F 4"), 0);

            // Readers that take a field over and draw their own control - Edge, Chrome -
            // read its border from these two and from nothing else. A widget describing
            // neither renders as a plain opaque box, hiding even what the page drew under
            // it, which is how two sections differing by eight rectangles came out
            // looking identical.
            AssertEqual(testName, "every widget states its border colour", 7, CountOccurrences(text, "/MK <<"), 0);
            AssertEqual(testName, "and its border width", 7, CountOccurrences(text, "/BS <<"), 0);

            // Appearance streams are drawn rather than left to the reader: PDF/A forbids
            // NeedAppearances, and a field only some viewers can draw is not a section.
            AssertEqual(testName, "appearances are supplied", true, text.Contains("/AP <<"), 0);
            AssertEqual(testName, "and never deferred to the reader", false, text.Contains("NeedAppearances"), 0);
            AssertEqual(testName, "a checkbox has both states", true, text.Contains("/Yes ") && text.Contains("/Off "), 0);

            // --- the form survives losing its annotations -------------------------
            // A widget draws itself and nothing else, so a rasteriser or a third-party
            // flattener that ignores annotations used to leave a page with no trace that
            // anything was ever fillable. The same boxes go on the page underneath.
            var backed = new PdfBackend(library) { Compress = false, FieldBackdrop = true };
            ReportRenderer.Render(quote, backed);
            string withBackdrop = System.Text.Encoding.Latin1.GetString(backed.ToArray());

            var bare = new PdfBackend(library) { Compress = false };
            ReportRenderer.Render(quote, bare);
            string withoutBackdrop = System.Text.Encoding.Latin1.GetString(bare.ToArray());

            AssertEqual(testName, "the page draws each field's box as well",
                7, CountOccurrences(withBackdrop, " re\nS") - CountOccurrences(withoutBackdrop, " re\nS"), 0);
            AssertEqual(testName, "and the widgets are still there", 7,
                CountOccurrences(withBackdrop, "/Subtype /Widget"), 0);
            AssertEqual(testName, "and it is off unless asked for", 7,
                CountOccurrences(withoutBackdrop, "/Subtype /Widget"), 0);

            // --- flattening keeps the state, drops the form ----------------------
            // Uncompressed, so the drawing instructions can be read back here.
            var flat = new PdfBackend(library) { Interactive = false, Compress = false };
            ReportRenderer.Render(quote, flat);
            string flattened = System.Text.Encoding.Latin1.GetString(flat.ToArray());

            AssertEqual(testName, "flattened, there is no form", false, flattened.Contains("/AcroForm"), 0);
            AssertEqual(testName, "and no widgets", 0, CountOccurrences(flattened, "/Subtype /Widget"), 0);
            AssertEqual(testName, "but the boxes are still drawn", true, flattened.Contains(" re\nS"), 0);

            // --- and it still archives -------------------------------------------
            var archivable = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b };
            ReportRenderer.Render(quote, archivable);
            byte[] bytes = archivable.ToArray();
            string archived = System.Text.Encoding.Latin1.GetString(bytes);

            AssertEqual(testName, "an archivable form is still a form", true,
                archived.Contains("/AcroForm") && archived.Contains("<pdfaid:part>3</pdfaid:part>"), 0);
            AssertEqual(testName, "with its own appearances", true, archived.Contains("/AP <<"), 0);

            string dir = OutputDirectory("quote");
            Directory.CreateDirectory(dir);
            WriteSamplePdf(Path.Combine(dir, "quote.pdf"), bytes);

            // And the same section with the boxes drawn on the page as well, for a
            // pipeline that rasterises without carrying annotations over. In a reader it
            // is the uglier of the two - the reader's own chrome sits beside ours - which
            // is why it is not the default.
            var backdrop = new PdfBackend(library)
            {
                Conformance = PdfConformance.PdfA3b,
                FieldBackdrop = true
            };
            ReportRenderer.Render(quote, backdrop);
            WriteSamplePdf(Path.Combine(dir, "quote-with-background.pdf"), backdrop.ToArray());

            // So the designer's echo can be checked against a template holding fields.
            File.WriteAllText(Path.Combine(dir, "quote.template.json"), Quote().ToJson());

            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(quote, svg);
            svg.SaveTo(dir, "quote");

            Console.WriteLine($"  ☑  8 champs -> {Path.Combine(dir, "quote.pdf")}");
            Console.WriteLine($"     and with a field background -> {Path.Combine(dir, "quote-with-background.pdf")}");
            Console.WriteLine();
        }

        /// <summary>
        /// Stands in for a signature drawn on a tablet: a canvas exports a data: URI, and
        /// it is filled with an opaque colour first so the PNG carries no alpha channel.
        /// </summary>
        static string SignatureDataUri()
        {
            byte[] png = PngEncoder.Rgb(300, 100, (x, y) =>
            {
                // A scrawl: two strokes that cross, thick enough to read at 40mm wide.
                double a = 50 + 34 * Math.Sin(x / 22.0);
                double b = 50 - 26 * Math.Sin(x / 13.0 + 1.1);
                bool ink = (Math.Abs(y - a) < 3.5 && x > 20 && x < 250)
                        || (Math.Abs(y - b) < 2.5 && x > 60 && x < 210);
                return ink ? ((byte)20, (byte)30, (byte)90) : ((byte)255, (byte)255, (byte)255);
            });

            return "data:image/png;base64," + Convert.ToBase64String(png);
        }

        /// <summary>The quote again, but every value now comes from the data.</summary>
        static Report BoundQuote(bool signed)
        {
            Report template = Quote();

            // The signature areas become images once there is something to put in them.
            var body = template.Body!;
            var strip = new ContainerItem { Layout = LayoutMode.Absolute };
            strip.Style.Location.Dock = Dock.Left | Dock.Right;
            strip.Style.Location.Height = (26, Units.Millimeter);
            strip.Style.Margin = new() { Top = 6 };

            foreach ((string who, string field, double left) in new[]
            {
                ("Customer's signature", "Signatures.Customer", 0.0),
                ("Engineer's signature", "Signatures.Repairer", 100.0)
            })
            {
                strip.Add(At(left, 0, who, 0.85));

                var drawn = new ImageItem
                {
                    SourceField = field,
                    NaturalWidth = 60,
                    NaturalHeight = 20,
                    Scaling = ImageScaling.Uniform
                };
                drawn.Style.Location.Left = (left, Units.Millimeter);
                drawn.Style.Location.Top = (4, Units.Millimeter);
                drawn.Style.Location.Width = (60, Units.Millimeter);
                strip.Add(drawn);
            }

            body.Add(strip);

            string data = signed
                ? $$"""
                  {
                    "Number": "D-2025-0042",
                    "Choices": { "Revision": "Yes", "Brakes": "Off", "Tyres": "Yes" },
                    "Customer": { "Name": "J. Smith", "Phone": "020 0000 0000", "Notes": "Mornings preferred" },
                    "Signatures": { "Customer": "{{SignatureDataUri()}}", "Repairer": "{{SignatureDataUri()}}" }
                  }
                  """
                : """{ "Number": "D-2025-0042" }""";

            return ReportBinder.Bind(template, data, new BindingOptions { MissingField = MissingField.Blank });
        }

        /// <summary>
        /// Every property of a field survives being cloned, which is not a corner: a
        /// container with RepeatOver clones its children once per record, so anything a
        /// clone drops is dropped in every repeated copy and in none of the originals.
        ///
        /// Written because Signing was dropped, and because reading eleven assignments
        /// and noticing the twelfth is missing is exactly what a person does badly and a
        /// reflection walk does perfectly. So the list is not typed here: it is asked of
        /// the type, and a property added tomorrow joins this check by existing.
        /// </summary>
        [Fact]
        public void Test77_ACloneKeepsEveryPropertyOfItsField()
        {
            const string testName = "Test 77";
            Console.WriteLine("Test 77: a cloned field keeps every property it had");
            Console.WriteLine();

            // Set to something that is not the default, or a clone that dropped it would
            // land on the same value and the check would be about nothing.
            var field = new FieldItem
            {
                Name = "customer.signature",
                Kind = FieldKind.Signature,
                Value = "already there",
                Tooltip = "sign here",
                ReadOnly = true,
                Required = true,
                Multiline = true,
                MaxLength = 42,
                Signing = Signing.Digital,
                AltText = "the customer's signature"
            };
            field.Font.Bold = true;
            field.Outline = new PenStyle { Width = 0.8 };
            field.Fill = new BrushStyle { Color = new Color(200, 210, 220) };

            var clone = (FieldItem)field.Clone();

            // Asked of the type rather than listed, so tomorrow's property is checked by
            // having been declared. Only what a field carries itself - a clone is a new
            // object in a new place, so Parent and Owner are meant to differ.
            var carried = typeof(FieldItem).GetProperties()
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                .Where(p => p.Name is not ("Parent" or "Owner"))
                .ToList();

            Console.WriteLine($"    {carried.Count} proprietes lues sur le type");
            AssertEqual(testName, "the type has properties to check at all",
                true, carried.Count > 10, 0);

            var dropped = new List<string>();
            foreach (var property in carried)
            {
                string mine = Describe(property.GetValue(field));
                string theirs = Describe(property.GetValue(clone));
                if (mine != theirs)
                    dropped.Add($"{property.Name}: {mine} -> {theirs}");
            }

            foreach (string loss in dropped)
                Console.WriteLine($"    perdu : {loss}");

            AssertEqual(testName, "and a clone keeps every one of them", 0, dropped.Count, 0);

            // The one that was missing, named as well, so a reader of this test knows
            // what it was written for rather than only that it passes.
            AssertEqual(testName, "including how it expects to be signed",
                Signing.Digital, clone.Signing, 0);

            Console.WriteLine();
        }


        [Fact]
        public void Test31_SignedQuote()
        {
            var testName = "Test 31: The Signed Quote";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());

            // --- the paper path: fields to fill in by hand -----------------------
            Report blank = BoundQuote(signed: false);
            var paper = new PdfBackend(library);
            ReportRenderer.Render(blank, paper);
            string fillable = System.Text.Encoding.Latin1.GetString(paper.ToArray());

            AssertEqual(testName, "the printable one is a form", true, fillable.Contains("/AcroForm"), 0);
            AssertEqual(testName, "with its boxes unticked", true, fillable.Contains("/V /Off"), 0);
            AssertEqual(testName, "and no signature on it", 0,
                CountOccurrences(fillable, "/Subtype /Image"), 0);

            // --- the web path: the same template, now carrying what was captured ---
            Report signed = BoundQuote(signed: true);
            var record = new PdfBackend(library) { Interactive = false, Conformance = PdfConformance.PdfA3b };
            ReportRenderer.Render(signed, record);
            byte[] bytes = record.ToArray();
            string archived = System.Text.Encoding.Latin1.GetString(bytes);

            AssertEqual(testName, "the record is not a form", false, archived.Contains("/AcroForm"), 0);
            AssertEqual(testName, "nor carries widgets", 0, CountOccurrences(archived, "/Subtype /Widget"), 0);
            AssertEqual(testName, "it is archivable", true, archived.Contains("<pdfaid:part>3</pdfaid:part>"), 0);

            // Both signatures travelled as images, straight through with no decoding.
            //
            // Counted as DRAWINGS and not as objects. These two are byte-identical -
            // SignatureDataUri() is deterministic and both fields get the same canvas -
            // so an image cache keyed on content legitimately embeds them once and draws
            // them twice. Counting objects said 2 only while the cache was keyed on the
            // field path, which is the very fault that made sixty records share one
            // picture: the number was right for the wrong reason.
            var readable = new PdfBackend(library) { Interactive = false, Compress = false };
            ReportRenderer.Render(BoundQuote(signed: true), readable);
            string drawn = System.Text.Encoding.Latin1.GetString(readable.ToArray());

            AssertEqual(testName, "both signatures are drawn on it", 2,
                System.Text.RegularExpressions.Regex.Matches(drawn, @"/Im\d+ Do").Count, 0);
            AssertEqual(testName, "from the one picture they both are", 1,
                CountOccurrences(archived, "/Subtype /Image"), 0);
            AssertEqual(testName, "the canvas PNG passed through untouched", true,
                archived.Contains("/Predictor 15"), 0);

            // The ticks and the typed answers came from the data, not the template.
            var fields = signed.AllItems.OfType<FieldItem>().ToList();
            AssertEqual(testName, "a tick came from the data", true,
                fields.First(f => f.Name == "opt.revision").IsChecked, 0);
            AssertEqual(testName, "an untick too", false,
                fields.First(f => f.Name == "opt.freins").IsChecked, 0);
            AssertEqual(testName, "so did the typed name", "J. Smith",
                fields.First(f => f.Name == "customer.name").Value!, 0);

            // An unsigned quote simply has no signature: the image bows out rather than
            // drawing an empty box where a name should be.
            var missing = blank.AllItems.OfType<ImageItem>().Where(i => i.SourceField != null).ToList();
            AssertEqual(testName, "no data, no signature drawn", 2,
                missing.Count(i => i.Style.Visibility == Visibility.Gone), 0);

            // --- and the two agree about everything else --------------------------
            LayoutResult a = blank.Resolve(new FontTextMeasurer(library));
            LayoutResult b = signed.Resolve(new FontTextMeasurer(library));
            AssertEqual(testName, "same section either way", a.PageCount, b.PageCount, 0);

            string dir = OutputDirectory("quote");
            Directory.CreateDirectory(dir);
            WriteSamplePdf(Path.Combine(dir, "quote-signed.pdf"), bytes);

            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(signed, svg);
            svg.SaveTo(dir, "quote-signed");

            Console.WriteLine($"  ✍  {Path.Combine(dir, "quote-signed.pdf")}");
            Console.WriteLine();
        }
    }
}
