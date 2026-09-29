namespace test
{
    /// <summary>
    /// The binder's contract: tokens and their formats, scope resolved outward and falling
    /// back to the root, show and hide against the data, mail merge as two modes of one
    /// template, the reserved set, and headings numbering themselves before anything is
    /// measured.
    /// </summary>
    public partial class EngineTests
    {
        [Fact]
        public void Test12_Fields()
        {
            var testName = "Test 12: Fields";
            Console.WriteLine($"Running {testName}...");

            var r = BandedReport(testName);
            r.Title = "Invoice 2025-07";
            r.GeneratedAt = new DateTimeOffset(2025, 7, 24, 9, 30, 0, TimeSpan.Zero);
            r.Fields["Customer"] = "ACME";

            var stamp = new TextItem { Text = "{Title} - {Customer} - {Date} {Time}" };
            r.Header!.Add(stamp);

            var counter = new TextItem { Text = "Page {Page} / {Pages}" };
            r.Footer!.Add(counter);

            var escaped = new TextItem { Text = "{{Page}} stays literal, {Unknown} shows up" };
            r.Footer.Add(escaped);

            for (int i = 0; i < 5; i++)
                r.Body!.Add(Block($"Block {i}", 100));

            var layout = r.Resolve();
            AssertEqual(testName, "page count", 3, layout.PageCount, 0);

            string TextOn(Page p, BaseItem src) => p.Items.First(x => ReferenceEquals(x.Source, src)).Fragment!;

            AssertEqual(testName, "header tokens", "Invoice 2025-07 - ACME - 2025-07-24 09:30", TextOn(layout.Pages[0], stamp), 0);
            AssertEqual(testName, "page 1 counter", "Page 1 / 3", TextOn(layout.Pages[0], counter), 0);
            AssertEqual(testName, "page 2 counter", "Page 2 / 3", TextOn(layout.Pages[1], counter), 0);
            AssertEqual(testName, "page 3 counter", "Page 3 / 3", TextOn(layout.Pages[2], counter), 0);
            AssertEqual(testName, "braces escape, unknown token kept",
                "{Page} stays literal, {Unknown} shows up", TextOn(layout.Pages[0], escaped), 0);

            // A format on the token.
            counter.Text = "{Page:000}/{Pages:000}";
            var formatted = r.Resolve();
            AssertEqual(testName, "formatted counter", "002/003", TextOn(formatted.Pages[1], counter), 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test13_PageCountWidth()
        {
            var testName = "Test 13: Page Count Width";
            Console.WriteLine($"Running {testName}...");

            // 12 pages, so {Pages} needs two digits. The placeholder must have been
            // widened before measuring, otherwise the text would be measured one digit
            // short and could overflow its frame once the real count is written in.
            var r = BandedReport(testName);
            // An explicit Left keeps the item auto-width instead of spanning the band.
            var counter = new TextItem
            {
                Text = "{Pages}",
                Style = new() { Location = new() { Left = (10, Units.Millimeter) } }
            };
            r.Footer!.Add(counter);
            for (int i = 0; i < 12; i++)
                r.Body!.Add(Block($"Block {i}", 250));

            var layout = r.Resolve();
            AssertEqual(testName, "page count", 12, layout.PageCount, 0);

            var placed = layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, counter));
            AssertEqual(testName, "counter reads the real total", "12", placed.Fragment!, 0);

            // Width was reserved for two digits, so the frame fits the final text exactly.
            var measured = FixedPitchTextMeasurer.Default.Measure("12", placed.Font!, double.PositiveInfinity);
            AssertEqual(testName, "frame width matches final text", measured.Width, placed.Frame.Width, 0.01);

            Console.WriteLine();
        }

        /// <summary>A one-cell table repeating over "Lines", for the awkward cases.</summary>
        static Report Small(string cell, bool empties = true)
        {
            var report = new Report { Dimension = new() { Width = 210, Height = 297 }, Body = new() };
            var table = new TableItem { RepeatOver = "Lines" };
            if (empties)
                table.EmptyText = "vide";
            table.AddAutoColumn();
            table.AddRow(new TextItem { Text = cell });
            report.Body!.Add(table);
            return report;
        }

        static TableRow OnlyRow(Report report)
            => LinesTable(report).Childs.OfType<TableRow>().Last();

        static string CellText(TableRow row) => row.Childs.OfType<TextItem>().First().Text;

        [Fact]
        public void Test26_Binding()
        {
            var testName = "Test 26: Template and Data";
            Console.WriteLine($"Running {testName}...");

            // --- the template is never touched ---------------------------------
            Report template = Small("{Ref}");
            string before = template.ToJson();
            Report bound = ReportBinder.Bind(template, """{ "Lines": [ {"Ref":"a"}, {"Ref":"b"}, {"Ref":"c"} ] }""");
            AssertEqual(testName, "the template comes back unchanged", before, template.ToJson(), 0);
            AssertEqual(testName, "template holds one prototype row", 1,
                LinesTable(template).Childs.Count, 0);

            // --- the prototype is stamped out once per record --------------------
            TableItem table = LinesTable(bound);
            AssertEqual(testName, "one row per record", 3, table.Childs.Count, 0);
            AssertEqual(testName, "the repetition is spent", true, table.RepeatOver == null, 0);

            var rows = table.Childs.Cast<TableRow>().ToList();
            AssertEqual(testName, "first row read its record", "a", CellText(rows[0]), 0);
            AssertEqual(testName, "third row read its own", "c", CellText(rows[2]), 0);

            // Copies share nothing: changing one must not reach the others.
            ((TextItem)rows[0].Childs[0]).Text = "touched";
            AssertEqual(testName, "rows are independent copies", "c", CellText(rows[2]), 0);

            // --- values are typed, presentation belongs to the template ----------
            const string numbers = """{ "Lines": [ { "Qty": 1037.5 } ] }""";
            AssertEqual(testName, "number formatted by the token", "1,037.500",
                CellText(OnlyRow(ReportBinder.Bind(Small("{Qty:#,##0.000}"), numbers))), 0);
            AssertEqual(testName, "same value, another format", "1037,50",
                CellText(OnlyRow(ReportBinder.Bind(Small("{Qty:0.00}"), numbers,
                    new BindingOptions { Numbers = NumberFormats.French }))), 0);
            AssertEqual(testName, "French grouping uses a narrow no-break space", "1 037,50",
                CellText(OnlyRow(ReportBinder.Bind(Small("{Qty:#,##0.00}"), numbers,
                    new BindingOptions { Numbers = NumberFormats.French }))), 0);
            AssertEqual(testName, "a date is formatted by the token", "27/10/2010",
                CellText(OnlyRow(ReportBinder.Bind(Small("{On:dd/MM/yyyy}"),
                    """{ "Lines": [ { "On": "2010-10-27" } ] }"""))), 0);

            // --- scope: a record's own field wins over the document's -------------
            AssertEqual(testName, "a record's field wins over the document's", "record",
                CellText(OnlyRow(ReportBinder.Bind(Small("{Label}"),
                    """{ "Label": "section", "Lines": [ { "Label": "record" } ] }"""))), 0);
            AssertEqual(testName, "and falls back to it when absent", "section",
                CellText(OnlyRow(ReportBinder.Bind(Small("{Label}"),
                    """{ "Label": "section", "Lines": [ { } ] }"""))), 0);

            // --- an empty collection ----------------------------------------------
            Report none = ReportBinder.Bind(Small("{Ref}"), """{ "Lines": [] }""");
            AssertEqual(testName, "an empty collection says so", "vide",
                CellText(OnlyRow(none)), 0);
            AssertEqual(testName, "without a message the table disappears", Visibility.Gone,
                LinesTable(ReportBinder.Bind(Small("{Ref}", empties: false), """{ "Lines": [] }""")).Style.Visibility, 0);

            // --- what to do about a token the data cannot answer -------------------
            const string one = """{ "Lines": [ {} ] }""";
            AssertEqual(testName, "unknown token stays visible by default", "{Nope}",
                CellText(OnlyRow(ReportBinder.Bind(Small("{Nope}"), one))), 0);
            AssertEqual(testName, "or is blanked on request", "",
                CellText(OnlyRow(ReportBinder.Bind(Small("{Nope}"), one,
                    new BindingOptions { MissingField = MissingField.Blank }))), 0);

            bool refused = false;
            try
            {
                ReportBinder.Bind(Small("{Nope}"), one, new BindingOptions { MissingField = MissingField.Fail });
            }
            catch (MissingDataException e)
            {
                refused = e.Field == "Nope";
            }
            AssertEqual(testName, "or refused outright", true, refused, 0);

            // --- and on the real template ------------------------------------------
            Report invoice = BoundAcmeInvoice(3);
            AssertEqual(testName, "the title is bound", "Invoice No. 33250200001", invoice.Title, 0);
            AssertEqual(testName, "nested path resolved", true,
                invoice.AllItems.OfType<TextItem>().Any(t => t.Text == "ACME Logistics"), 0);
            AssertEqual(testName, "{Page} survives binding", true,
                invoice.AllItems.OfType<TextItem>().Any(t => t.Text.Contains("{Page}")), 0);
            AssertEqual(testName, "a bound report survives JSON",
                invoice.AllItems.Count(), Report.FromJson(invoice.ToJson()).AllItems.Count(), 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test44_ShownOnlyWhenThereIsSomething()
        {
            var testName = "Test 44: Elements that appear only when the data has something";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // A form's problem, and the reason this exists: a box only one kind of
            // section fills, a label for a number that is not always there. Without a
            // condition the choice is a rule drawn through empty space, or one template
            // per case.
            Report Template()
            {
                var report = new Report
                {
                    Title = "Conditions",
                    Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack }
                };

                void Line(string text, string? show = null, string? hide = null)
                {
                    var item = new TextItem { Text = text, Font = new() { Size = 1 } };
                    item.ShowWhen = show;
                    item.HideWhen = hide;
                    report.Body!.Add(item);
                }

                // English, because the report above says en-GB. It said so already and
                // half these lines were French - the ACME invoice's fault one storey
                // down, and the reason its numbers used to be French here too.
                Line("always here");
                Line("credit note: {Reference}", show: "Reference");
                Line("VAT: {Vat}", show: "Vat");
                Line("discount of {Discount}", show: "Discount");
                Line("settled", show: "Paid");
                Line("reminder", hide: "Paid");
                Line("lines present", show: "Lines");
                Line("in braces", show: "{Reference}");

                report.Reattach();
                return report;
            }

            string Render(string json)
            {
                // Neutral, which is what BindingOptions does when nobody says otherwise.
                // This test is about ShowWhen and HideWhen; it asked for French numbers
                // for no reason its subject can give, and section 2b is about exactly
                // that - a preference set somewhere is still a preference, and the
                // question to ask is what the engine does when nobody tells it anything.
                //
                // NumberFormats.French keeps the two assertions written for it, on the
                // narrow no-break space and the decimal comma, forty lines above.
                Report bound = ReportBinder.Bind(Template(), json);
                var backend = new SvgBackend(new FontTextMeasurer(library));
                ReportRenderer.Render(bound, backend);
                return backend.Pages[0];
            }

            // Nothing worth showing: absent, null, blank, zero, false, and an empty list.
            string bare = Render("""
            {
              "Reference": "",
              "Vat": null,
              "Discount": 0,
              "Paid": false,
              "Lines": []
            }
            """);

            AssertEqual(testName, "what is unconditional stays", true, bare.Contains("always here"), 0);
            AssertEqual(testName, "an empty string is nothing", false, bare.Contains("credit note:"), 0);
            AssertEqual(testName, "a null is nothing", false, bare.Contains("VAT:"), 0);
            AssertEqual(testName, "a zero is nothing", false, bare.Contains("discount of"), 0);
            AssertEqual(testName, "a false is nothing", false, bare.Contains("settled"), 0);
            AssertEqual(testName, "an empty list is nothing", false, bare.Contains("lines present"), 0);
            AssertEqual(testName, "and the braces are tolerated", false, bare.Contains("in braces"), 0);

            // HideWhen is the other way round, and false does not count as something.
            AssertEqual(testName, "the reminder shows while nothing is paid", true,
                bare.Contains("reminder"), 0);

            string full = Render("""
            {
              "Reference": "550502012",
              "Vat": "FR72844787861",
              "Discount": 12.5,
              "Paid": true,
              "Lines": [ { "Label": "one line" } ]
            }
            """);

            AssertEqual(testName, "a value brings its line back", true, full.Contains("550502012"), 0);
            AssertEqual(testName, "so does a number that is not zero", true, full.Contains("discount of 12.5"), 0);
            AssertEqual(testName, "and a true", true, full.Contains("settled"), 0);
            AssertEqual(testName, "a filled list counts", true, full.Contains("lines present"), 0);
            AssertEqual(testName, "the braces resolve the same path", true, full.Contains("in braces"), 0);
            AssertEqual(testName, "and the reminder is dropped once paid", false, full.Contains("reminder"), 0);

            // Gone, not merely invisible: the lines below have to close the gap, or a
            // hidden label leaves a hole where a form expects none.
            var lines = System.Text.RegularExpressions.Regex.Matches(bare, @"<text [^>]*y=""([\d.]+)""");
            var tops = lines.Select(m => double.Parse(m.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture)).OrderBy(v => v).ToList();

            Console.WriteLine($"    {tops.Count} lignes visibles, de {tops.First():F1} à {tops.Last():F1} mm");
            AssertEqual(testName, "only the two survivors are drawn", 2, tops.Count, 0);
            AssertEqual(testName, "and they follow each other", true, tops[1] - tops[0] < 6, 0);

            Console.WriteLine();
        }

        /// <summary>
        /// One template, two kinds of mail merge, chosen at the command line.
        ///
        /// The record is not lifted out and made the root. The named array is cut down
        /// to the one element and the rest of the file is left alone, so a template
        /// written to repeat over recipients gives every letter in one document without
        /// splitting, and one document per letter with it. Two modes, one template - and
        /// two templates kept in step is the fault this project keeps paying for.
        ///
        /// It costs the engine nothing, which is the point: RepeatOver already repeats,
        /// PageBreak already breaks, and a token already resolves through the record
        /// before falling back to the file's root. This is the CLI choosing what to hand
        /// over, not the engine learning anything.
        ///
        /// Checked here rather than by running the CLI, since what matters is the shape
        /// of what comes out; the refusals below are the CLI's own and are exercised by
        /// hand in the same shell as the rest of it.
        /// </summary>
        [Fact]
        public void Test62_MailMergeIsTwoModesOfOneTemplate()
        {
            const string testName = "Test 62";
            Console.WriteLine("Test 62: the same template, whole or split");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            static Report Template()
            {
                var report = new Report { Title = "merge", Language = "fr-FR", Body = new Body() };
                report.Section.Dimension = new DocumentDimension { Width = 210, Height = 297 };

                var run = new ContainerItem { Layout = LayoutMode.Stack, RepeatOver = "Recipients" };
                var letter = new ContainerItem { Layout = LayoutMode.Stack };
                letter.Style.PageBreak = PageBreak.Before;

                // {Company} is never in a record: it falls back to the file's root, which
                // is the rule a table row already uses to read a currency off its invoice.
                letter.Add(new TextItem { Text = "{Company}" });
                letter.Add(new TextItem { Text = "Cher {Name}," });

                run.Add(letter);
                report.Body!.Add(run);
                return report;
            }

            const string whole = """
                { "Company": "ACME SA",
                  "Recipients": [ { "Name": "Ada" }, { "Name": "Grace" }, { "Name": "Alan" } ] }
                """;

            static List<string> Lines(LayoutResult layout)
            {
                var found = new List<string>();
                foreach (Page page in layout.Pages)
                    foreach (PlacedItem placed in page.Items)
                        if (placed.Fragment is { Length: > 0 } line)
                            found.Add(line);
                return found;
            }

            // --- whole: every letter in one document --------------------------------
            LayoutResult all = ReportBinder.Bind(Template(), whole, new BindingOptions())
                                           .Resolve(measurer);

            Console.WriteLine($"    whole            -> {all.PageCount} pages");
            AssertEqual(testName, "one page per recipient", 3, all.PageCount, 0);
            AssertEqual(testName, "and every one of them is in it", true,
                Lines(all).Contains("Cher Ada,") && Lines(all).Contains("Cher Alan,"), 0);

            // --- split: the array cut to one, everything else left alone ------------
            // The shape the CLI hands over, written out here so the test is about the
            // shape rather than about the CLI's argument parsing.
            var made = new List<LayoutResult>();
            foreach (string name in new[] { "Ada", "Grace", "Alan" })
            {
                string one = $$"""
                    { "Company": "ACME SA", "Recipients": [ { "Name": "{{name}}" } ] }
                    """;
                made.Add(ReportBinder.Bind(Template(), one, new BindingOptions()).Resolve(measurer));
            }

            Console.WriteLine($"    split            -> {made.Count} documents of "
                              + string.Join("/", made.Select(m => m.PageCount)) + " page(s)");

            AssertEqual(testName, "one document per recipient", 3, made.Count, 0);
            AssertEqual(testName, "each of a single page", true, made.All(m => m.PageCount == 1), 0);

            // The template did not change between the two, which is the whole claim.
            AssertEqual(testName, "the second document is Grace's alone", true,
                Lines(made[1]).Contains("Cher Grace,") && !Lines(made[1]).Contains("Cher Ada,"), 0);

            // --- and the shared half still resolves, in both modes -------------------
            // Free, and worth an assertion precisely because it is free: the fallback to
            // the root is what makes cutting the array enough. Lift the record out to be
            // the root instead and this line is what breaks.
            AssertEqual(testName, "what is shared reaches a split document too", true,
                Lines(made[0]).Contains("ACME SA"), 0);
            AssertEqual(testName, "and reaches a whole one", true,
                Lines(all).Count(l => l == "ACME SA") == 3, 0);

            Console.WriteLine();
        }

/// <summary>
        /// Every token the engine declares is one the data may not answer.
        ///
        /// BindingOptions.Reserved decides which tokens the binder leaves alone for a
        /// later pass. Anything not in it, the data answers - so a data file carrying a
        /// field called HeadingNumber replaces the system token, silently and completely.
        ///
        /// It held five bare string literals sitting beside the constants that spell the
        /// same words, and it had fallen four behind: SectionPage and SectionPages since
        /// sections were added, HeadingNumber and SectionHeadingNumber the day they were.
        /// Nothing compared the two lists because there was nothing to compare - the set
        /// was a copy of the tokens, not a reading of them.
        ///
        /// This reflects over the token constants rather than listing them here, or it
        /// would be the seventh place.
        /// </summary>
        [Fact]
        public void Test68_ReservedTokens()
        {
            const string testName = "Test 68";
            Console.WriteLine("Test 68: the data cannot answer a system token");
            Console.WriteLine();

            static IEnumerable<string> Declared(Type type)
                => type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                       .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                       .Select(f => (string)f.GetRawConstantValue()!);

            var declared = Declared(typeof(ReportTokens))
                .Concat(Declared(typeof(HeadingNumbers)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var reserved = new BindingOptions().Reserved;

            Console.WriteLine($"    {declared.Count} jetons declares, {reserved.Count} reserves");
            foreach (string token in declared.OrderBy(t => t, StringComparer.Ordinal))
                Console.WriteLine($"      {token,-22} {(reserved.Contains(token) ? "reserve" : "LA DONNEE Y REPOND")}");

            string[] unguarded = declared.Where(t => !reserved.Contains(t))
                                         .OrderBy(t => t, StringComparer.Ordinal).ToArray();
            AssertEqual(testName, $"every declared token is reserved ({string.Join(", ", unguarded)})",
                0, unguarded.Length, 0);

            string[] stray = reserved.Where(t => !declared.Contains(t))
                                     .OrderBy(t => t, StringComparer.Ordinal).ToArray();
            AssertEqual(testName, $"and nothing is reserved that is not a token ({string.Join(", ", stray)})",
                0, stray.Length, 0);

            // --- and what being reserved is worth ------------------------------------
            // The data is given a field for every system token. Bound, not one of them
            // may have been taken: a template asking for the page it is on must not get
            // whatever the data happened to call that.
            var report = new Report
            {
                Title = "Bulletin", Language = "en-GB", HeadingNumbering = "1.1",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            foreach (string token in declared.OrderBy(t => t, StringComparer.Ordinal))
                report.Body!.Add(new TextItem { Text = $"{token}=[{{{token}}}]", Font = new() { Size = 3 } });
            report.Reattach();

            string json = "{" + string.Join(",", declared.Select(t => $"\"{t}\":\"SEIZED\"")) + "}";
            Report bound = ReportBinder.Bind(report, json);

            string[] seized = bound.AllItems.OfType<TextItem>()
                .Where(t => t.Text.Contains("SEIZED", StringComparison.Ordinal))
                .Select(t => t.Text).ToArray();

            Console.WriteLine($"    donnees hostiles : {seized.Length} jeton(s) pris");
            AssertEqual(testName, $"the data answers none of them ({string.Join(", ", seized)})",
                0, seized.Length, 0);

            // The control case, without which the one above passes for lack of matter: an
            // ordinary field IS answered, so the binder was working at all.
            var plain = new Report
            {
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            plain.Body!.Add(new TextItem { Text = "[{Customer}]" });
            plain.Reattach();

            string filled = ReportBinder.Bind(plain, """{"Customer":"SEIZED"}""")
                .AllItems.OfType<TextItem>().First().Text;
            Console.WriteLine($"    temoin : {filled}");
            AssertEqual(testName, "while an ordinary field is answered", "[SEIZED]", filled, 0);

            Console.WriteLine();
        }

        /// <summary>
        /// Puts a heading, a rule across the page and a line of "what to look for" at
        /// the top of a stacked body.
        ///
        /// These samples exist to be looked at - the third of the three ways a rendering
        /// is checked, and the only one no assertion replaces. Opened in a reader they
        /// were handsome pages that said nothing about themselves: a contact sheet, a
        /// watermark and a split row look like pictures, a tint and a table unless
        /// something says what they are and what would be wrong.
        ///
        /// It never stands in for a check. Saying "sixty photographs below" puts none
        /// there; the assertions still count what was placed. A sentence that asserts is
        /// CLAUDE.md §2 written in English.
        /// </summary>
/// <summary>
        /// Headings number themselves, and the number survives being laid out twice.
        ///
        /// The number lives in the text - {HeadingNumber}. Introduction - rather than
        /// being painted to the left of the paragraph, which is the whole difference
        /// from a word processor's linked lists: the author picks the position and types
        /// the closing punctuation, so the format never has to describe it.
        ///
        /// The case that decides the design is the balanced column. Balancing searches
        /// eight column heights, placing and rewinding each time, and KeepWithNext
        /// rewinds pages as well - so a counter advanced while items are placed would
        /// count the same heading again for every trial. Here the walk runs once before
        /// any measurement and stores the answer on the item, which makes filling a token
        /// a pure function of it. That is what the third block below measures, and it is
        /// the only one that would fail on the obvious implementation.
        /// </summary>
        [Fact]
        public void Test66_HeadingNumbers()
        {
            const string testName = "Test 66";
            Console.WriteLine("Test 66: headings number themselves");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            static TextItem Head(int level, string text, string token = "{HeadingNumber}")
            {
                var item = new TextItem
                {
                    Text = token + " " + text,
                    Font = new TextStyle { Size = 3 },
                    Role = (PdfRole)((int)PdfRole.Heading1 + level - 1)
                };
                return item;
            }

            List<string> Rendered(Report report)
            {
                var svg = new SvgBackend(measurer);
                ReportRenderer.Render(report, svg);
                return System.Text.RegularExpressions.Regex
                    .Matches(string.Join("\n", svg.Pages), @"<text [^>]*>([^<]*)</text>")
                    .Select(m => m.Groups[1].Value)
                    .ToList();
            }

            static Report Blank(string? numbering)
            {
                var report = new Report
                {
                    Title = "t", Language = "en-GB", HeadingNumbering = numbering,
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack }
                };
                report.Section.Style.Padding = new() { Left = 15, Top = 15, Right = 15, Bottom = 15 };
                return report;
            }

            // --- document order, and the level from the role -------------------------
            Report plain = Blank("1.1.1");
            plain.Body!.Add(Head(1, "Alpha"));
            plain.Body!.Add(Head(2, "Beta"));
            plain.Body!.Add(Head(2, "Gamma"));
            plain.Body!.Add(Head(3, "Delta"));
            plain.Body!.Add(Head(1, "Epsilon"));
            plain.Reattach();

            List<string> numbered = Rendered(plain);
            Console.WriteLine("    " + string.Join("  |  ", numbered));

            AssertEqual(testName, "the first heading opens at one", "1 Alpha", numbered[0], 0);
            AssertEqual(testName, "a level below it carries both", "1.1 Beta", numbered[1], 0);
            AssertEqual(testName, "and its sibling advances only itself", "1.2 Gamma", numbered[2], 0);
            AssertEqual(testName, "a third level hangs off the second", "1.2.1 Delta", numbered[3], 0);

            // The deeper counters reset, or the next chapter would read 2.2.
            AssertEqual(testName, "a new chapter resets what is under it", "2 Epsilon", numbered[4], 0);

            // --- a level asked for by name -------------------------------------------
            Report stated = Blank("1.1.1");
            stated.Body!.Add(Head(1, "Alpha"));
            stated.Body!.Add(Head(2, "Beta", "{HeadingNumber[1]}"));
            stated.Reattach();

            List<string> forced = Rendered(stated);
            Console.WriteLine("    " + string.Join("  |  ", forced));
            AssertEqual(testName, "a stated level truncates to it", "1 Beta", forced[1], 0);

            // --- the balanced column, which is the whole reason for the design -------
            // Twenty short articles in two balanced columns: the engine places this
            // heading, rewinds it and places it again eight times over while it searches
            // for the shortest column that still holds everything.
            Report balanced = Blank("1.1.1");
            balanced.Body!.ColumnCount = 2;
            balanced.Body!.ColumnGap = 8;
            balanced.Body!.BalanceColumns = true;
            balanced.Body!.Add(Head(1, "Terms"));
            for (int i = 1; i <= 20; i++)
                balanced.Body!.Add(new TextItem
                {
                    Text = $"Clause {i}. " + string.Join(" ", Enumerable.Repeat(
                        "The supplier undertakes to carry out the work to the standards of the art", 3)) + ".",
                    Font = new() { Size = 9, SizeUnit = FontSizeUnit.Point, LineSpacing = 1.4 }
                });
            balanced.Reattach();

            string heading = Rendered(balanced).First(t => t.EndsWith("Terms", StringComparison.Ordinal));
            Console.WriteLine($"    apres huit essais de colonne : {heading}");
            AssertEqual(testName, "eight column trials still leave one number", "1 Terms", heading, 0);

            // --- hidden headings take no number --------------------------------------
            Report hidden = Blank("1.1.1");
            hidden.Body!.Add(Head(1, "Alpha"));
            TextItem gone = Head(1, "Beta");
            gone.Style.Visibility = Visibility.Gone;
            hidden.Body!.Add(gone);
            hidden.Body!.Add(Head(1, "Gamma"));
            hidden.Reattach();

            List<string> survivors = Rendered(hidden);
            Console.WriteLine("    " + string.Join("  |  ", survivors));
            AssertEqual(testName, "a heading the data hid is not in the document", 2, survivors.Count, 0);
            AssertEqual(testName, "so the one after it takes the number it did not", "2 Gamma", survivors[1], 0);

            // --- two counters, exactly as the page tokens have two -------------------
            Report split = Blank("1.1.1");
            split.Body!.Add(new TextItem
            {
                Text = "{HeadingNumber} file, {SectionHeadingNumber} section",
                Font = new TextStyle { Size = 3 },
                Role = PdfRole.Heading1
            });

            Section second = split.AddSection();
            second.Dimension = new DocumentDimension { Width = 210, Height = 297 };
            var body2 = new Body { Layout = LayoutMode.Stack };
            body2.Add(new TextItem
            {
                Text = "{HeadingNumber} file, {SectionHeadingNumber} section",
                Font = new TextStyle { Size = 3 },
                Role = PdfRole.Heading1
            });
            second.Add(body2);
            split.Reattach();

            List<string> both = Rendered(split);
            Console.WriteLine("    " + string.Join("  |  ", both));
            AssertEqual(testName, "in the first section the two counters agree",
                "1 file, 1 section", both[0], 0);
            AssertEqual(testName, "in the second the file keeps counting and the section restarts",
                "2 file, 1 section", both[1], 0);

            // --- the alphabets --------------------------------------------------------
            string[] shapes = { "I.1", "i.1", "A.1", "a.1" };
            foreach (string shape in shapes)
            {
                Report roman = Blank(shape);
                for (int i = 1; i <= 4; i++)
                    roman.Body!.Add(Head(1, "H"));
                roman.Reattach();
                Console.WriteLine($"    {shape,-6} -> {string.Join(" ", Rendered(roman))}");
            }

            Report romans = Blank("I.1");
            for (int i = 1; i <= 4; i++)
                romans.Body!.Add(Head(1, "H"));
            romans.Reattach();
            AssertEqual(testName, "roman counts in roman", "IV H", Rendered(romans)[3], 0);

            Report lower = Blank("i.1");
            for (int i = 1; i <= 4; i++)
                lower.Body!.Add(Head(1, "H"));
            lower.Reattach();
            AssertEqual(testName, "and lower case in lower case", "iv H", Rendered(lower)[3], 0);

            // Past Z, which is where a contract with twenty-seven clauses arrives and a
            // roman numeral past MMMCMXCIX never does.
            Report letters = Blank("A.1");
            for (int i = 1; i <= 27; i++)
                letters.Body!.Add(Head(1, "H"));
            letters.Reattach();
            List<string> spelled = Rendered(letters);
            Console.WriteLine($"    A.1 au 26e et 27e : {spelled[25]} / {spelled[26]}");
            AssertEqual(testName, "the alphabet ends at Z", "Z H", spelled[25], 0);
            AssertEqual(testName, "and carries on the way a spreadsheet does", "AA H", spelled[26], 0);

            // --- what the separator is, and what it is not ---------------------------
            Report spaced = Blank("A) a. 1");
            spaced.Body!.Add(Head(1, "One"));
            spaced.Body!.Add(Head(2, "Two"));
            spaced.Body!.Add(Head(3, "Three"));
            spaced.Reattach();
            List<string> apart = Rendered(spaced);
            Console.WriteLine("    " + string.Join("  |  ", apart));

            // The closing bracket belongs to the level that follows, so level one is bare
            // and the author types whatever comes after it.
            AssertEqual(testName, "a separator sits between two levels, never after the last",
                "A One", apart[0], 0);
            AssertEqual(testName, "and it is whatever was written between the symbols",
                "A) a Two", apart[1], 0);
            AssertEqual(testName, "at every depth", "A) a. 1 Three", apart[2], 0);

            // --- no format, no invention ---------------------------------------------
            // Written out so the designer's echo can be held to the very same numbers.
            // An echo that draws "1." where the engine draws "1.1." is the preview lying
            // about the page, and nothing but a confrontation finds it.
            // The order matters more than the length. A second chapter followed by a
            // second-level heading is the only shape where the reset shows on the page:
            // without it Epsilon reads "B) b" instead of "B) a". The first version of
            // this sample went A B C B A, where every displayed number happens to be the
            // same either way - so removing the reset from the echo left the harness
            // green, which is how a sample outside its window looks from the inside.
            Report shared = Blank("A) a. 1");
            shared.Body!.Add(Head(1, "Alpha"));
            shared.Body!.Add(Head(2, "Beta"));
            shared.Body!.Add(Head(3, "Gamma"));
            shared.Body!.Add(Head(1, "Delta"));
            shared.Body!.Add(Head(2, "Epsilon"));
            shared.Body!.Add(Head(2, "Zeta", "{SectionHeadingNumber}"));
            shared.Reattach();

            string headingsDir = OutputDirectory("headings");
            File.WriteAllText(Path.Combine(headingsDir, "numbered.template.json"),
                System.Text.Json.JsonSerializer.Serialize(shared, SourceGenerationContext.Default.Report));
            File.WriteAllText(Path.Combine(headingsDir, "numbered.expected.json"),
                "[" + string.Join(",", Rendered(shared)
                    .Select(t => System.Text.Json.JsonSerializer.Serialize(t))) + "]");
            Console.WriteLine($"  # {Path.Combine(headingsDir, "numbered.expected.json")}");

            Report unnumbered = Blank(null);
            unnumbered.Body!.Add(Head(1, "Alpha"));
            unnumbered.Reattach();
            Console.WriteLine("    " + string.Join("  |  ", Rendered(unnumbered)));
            AssertEqual(testName, "with no format the token stays visible rather than vanishing",
                "{HeadingNumber} Alpha", Rendered(unnumbered)[0], 0);

            Console.WriteLine();
        }
    }
}
