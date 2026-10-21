namespace test
{
    /// <summary>
    /// What `frisket check` and `fk_check` answer: the validator's three profiles, the faulty
    /// and mended fixtures the designer's echo is confronted with, and the rules that must
    /// fall silent - which is the half that makes the other half worth anything.
    ///
    /// Named for the question rather than for the class, so that it does not read as a twin
    /// of engine/Model/TemplateValidator.cs in a directory listing or a fuzzy file open.
    /// </summary>
    public partial class EngineTests
    {
        [Fact]
        public void Test42_TemplateValidator()
        {
            var testName = "Test 42: Faults found in the template, not in the PDF";
            Console.WriteLine($"Running {testName}...");

            // A template with one of each fault. Every one of these is something an
            // outside tool reports much later: veraPDF refuses the ragged table, a screen
            // reader falls silent on the logo, a reader announces a nameless control.
            var report = new Report
            {
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };

            var logo = new ImageItem();                       // no alternative text
            report.Body!.Add(logo);

            var code = new BarcodeItem { Value = "" };        // nothing to encode
            report.Body!.Add(code);

            var first = new FieldItem { Name = "signature", Kind = FieldKind.Text };
            var second = new FieldItem { Name = "signature", Kind = FieldKind.Text };
            report.Body!.Add(first);
            report.Body!.Add(second);                          // same name as the first

            // Six columns, and a row that brings two cells - the very shape veraPDF
            // caught in our own sample.
            // Columns asked for where nothing flows: the container places its children
            // by coordinates, so the setting does nothing and says nothing.
            var pinned = new ContainerItem { Layout = LayoutMode.Absolute, ColumnCount = 2 };
            pinned.Add(new TextItem { Text = "placed by hand" });
            report.Body!.Add(pinned);

            var table = new TableItem();
            for (int i = 0; i < 6; i++) table.AddAutoColumn();
            var short_ = new TableRow();
            short_.Add(new TextItem { Text = "A" });
            short_.Add(new TextItem { Text = "B" });
            table.Add(short_);
            report.Body!.Add(table);

            var grid = new ImageGridItem { GridColumns = 0 };  // fewer than one column
            report.Body!.Add(grid);

            var sub = new SubTemplateItem();                    // no file named
            report.Body!.Add(sub);

            var advanced = new AdvancedTextItem();              // no run with anything in it
            advanced.Runs.Add(new TextRun { Text = "" });
            report.Body!.Add(advanced);


            // A page break one level out from where it was meant: a repeating run
            // wrapping a single block, with the break on the run. It breaks once,
            // before the whole run, which is legal and is what somebody surprised by
            // RepeatOver rarely wants.
            var run = new ContainerItem { Layout = LayoutMode.Stack, RepeatOver = "Recipients" };
            run.Style.PageBreak = PageBreak.Before;
            var letter = new ContainerItem { Layout = LayoutMode.Stack };
            letter.Add(new TextItem { Text = "Cher {Name}," });
            run.Add(letter);
            report.Body!.Add(run);

            // A section inside a section. Nothing in the type system stops it, and it
            // means nothing - a page size inside a flow already laid out on a page.
            //
            // It is here because the rule existed in the engine alone for eight months:
            // no call site in the designer, no label in either language. Neither fixture
            // built the shape, and the harness compared the faults that fired rather than
            // the rules that exist, so nothing could see it.
            var nested = new Section();
            report.Body!.Add(nested);

            // A heading number asked for on a report that never says how one is written,
            // so the token would go out on the page exactly as typed.
            var unspelled = new TextItem { Text = "{HeadingNumber}. Introduction", Role = PdfRole.Heading1 };
            report.Body!.Add(unspelled);

            // The same token on something that is not a heading and names no level of its
            // own: there is nowhere for the number to be.
            var loose = new TextItem { Text = "See {HeadingNumber} below" };
            report.Body!.Add(loose);

            // Justified text carrying a hard break of its own. The break is stretched to
            // the margin like a wrapped line, which is not what a break means - and the
            // engine cannot tell the two apart once the lines are assembled, which is why
            // this is a rule rather than a fix.
            //
            // It is here because the rule had no assertion anywhere: it lived in both
            // validators and in both languages, and no fixture ever built the shape, so
            // nothing could have noticed it going wrong. The same gap sound.nestedSection
            // sat in for eight months, twenty lines above.
            var broken = new TextItem { Text = "First paragraph.\nSecond one." };
            broken.Font.HorizontalAlignment = HorizontalAlignment.Justify;
            report.Body!.Add(broken);

            // A role naming a control, on a paragraph. An authored role is taken before
            // anything else is asked, so it walks straight past the guard PdfRoles.Resolve
            // has for exactly this - and the renderer then writes a figure, which owes an
            // alternative text nothing asked the author for.
            //
            // Here rather than thirty lines down, where it went first: everything below
            // the check at the foot of this block is the MENDING phase, so a fault
            // declared there is added after the fixture has been questioned. It read as
            // the rule not firing.
            var notAControl = new TextItem { Text = "Sign here", Role = PdfRole.Form };
            report.Body!.Add(notAControl);

            // A second section with no page size, which nothing supplies: laid out on a sheet
            // of nothing. A person met it only by writing a template by hand; a model writing
            // one from the schema meets it the first time it leaves the property out.
            var sizeless = new Section();
            report.Sections.Add(sizeless);

            report.Reattach();

            // Written out so the designer's echo can be held to the same findings on
            // the very same template: two validators that disagree are worse than one,
            // because the designer is then reassuring somebody about a refused section.
            string dir = OutputDirectory("validator");
            File.WriteAllText(Path.Combine(dir, "faulty.template.json"),
                System.Text.Json.JsonSerializer.Serialize(report, SourceGenerationContext.Default.Report));

            var issues = TemplateValidator.Check(report);

            File.WriteAllText(Path.Combine(dir, "faulty.issues.json"),
                "[" + string.Join(",", issues.Select(i =>
                    $$"""{"rule":"{{i.Rule}}","level":"{{i.Level.ToString().ToLowerInvariant()}}"}"""))
                + "]");

            foreach (TemplateIssue issue in issues)
                Console.WriteLine($"    {issue.Level,-7} {issue.Rule,-20} {issue.Where}");

            bool Has(string rule) => issues.Any(i => i.Rule == rule);

            AssertEqual(testName, "a break one level out from where it was meant is noticed",
                true, Has("sound.repeatPageBreak"), 0);

            AssertEqual(testName, "the missing language is seen", true, Has("ua.language"), 0);
            AssertEqual(testName, "and the missing title", true, Has("ua.title"), 0);
            AssertEqual(testName, "a picture with nothing to say for it", true, Has("ua.altText"), 0);
            AssertEqual(testName, "a control with no name", true, Has("ua.fieldName"), 0);
            AssertEqual(testName, "a barcode with nothing in it", true, Has("sound.emptyBarcode"), 0);
            AssertEqual(testName, "two fields answering to one name", true, Has("sound.duplicateField"), 0);
            AssertEqual(testName, "a table whose rows disagree", true, Has("sound.raggedTable"), 0);
            AssertEqual(testName, "a table with no heading row", true, Has("ua.tableHeader"), 0);
            AssertEqual(testName, "columns that will do nothing", true, Has("sound.columnsIgnored"), 0);
            AssertEqual(testName, "a hard break inside justified text",
                true, Has("sound.justifiedBreak"), 0);
            AssertEqual(testName, "an image with nothing to draw", true, Has("sound.emptyImage"), 0);
            AssertEqual(testName, "a grid asking for fewer than one column", true, Has("sound.emptyImageGrid"), 0);
            AssertEqual(testName, "a sub-template naming no file", true, Has("sound.emptySubTemplate"), 0);
            AssertEqual(testName, "an advanced text with nothing in any run", true, Has("sound.emptyAdvancedText"), 0);
            AssertEqual(testName, "a section dropped inside another", true, Has("sound.nestedSection"), 0);
            AssertEqual(testName, "a heading number with no format to write it in",
                true, Has("sound.headingNumberNoFormat"), 0);
            AssertEqual(testName, "and one on something that is not a heading",
                true, Has("sound.headingNumberOutsideHeading"), 0);
            AssertEqual(testName, "a role naming a control on something that is not one",
                true, Has("ua.formRole"), 0);
            AssertEqual(testName, "a section with no page size", true,
                issues.Any(i => i.Rule == "sound.noPageSize" && ReferenceEquals(i.Item, sizeless)), 0);

            // It points at the element, not merely at the fault: this is what lets the
            // designer light up the offending node instead of printing a list.
            TemplateIssue ragged = issues.First(i => i.Rule == "sound.raggedTable");
            AssertEqual(testName, "and says which row", true, ReferenceEquals(short_, ragged.Item), 0);

            AssertEqual(testName, "the picture is named", true,
                ReferenceEquals(logo, issues.First(i => i.Rule == "ua.altText").Item), 0);

            // The depth rule, apart from the rest because it needs a format that is right
            // for one heading and short for the next - which is not a state the fixture
            // above can be in and be mended at the same time.
            var deep = new Report
            {
                Title = "t", Language = "en-GB", HeadingNumbering = "1.1",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            deep.Body!.Add(new TextItem { Text = "{HeadingNumber} Deep", Role = PdfRole.Heading3 });
            deep.Reattach();

            AssertEqual(testName, "a heading deeper than the format spells out", true,
                TemplateValidator.Check(deep, TemplateProfile.Sound)
                    .Any(i => i.Rule == "sound.headingNumberDepth"), 0);

            deep.HeadingNumbering = "1.1.1";
            AssertEqual(testName, "and it falls silent once the format reaches it", false,
                TemplateValidator.Check(deep, TemplateProfile.Sound)
                    .Any(i => i.Rule == "sound.headingNumberDepth"), 0);

            // The profile is honoured: asking only what a reader needs must not report a
            // duplicate field name, and asking only for soundness must not demand a title.
            var uaOnly = TemplateValidator.Check(report, TemplateProfile.Accessible);
            AssertEqual(testName, "accessibility alone keeps quiet about duplicates", false,
                uaOnly.Any(i => i.Rule == "sound.duplicateField"), 0);

            var soundOnly = TemplateValidator.Check(report, TemplateProfile.Sound);
            AssertEqual(testName, "and soundness alone does not ask for a title", false,
                soundOnly.Any(i => i.Rule.StartsWith("ua.")), 0);
            AssertEqual(testName, "yet still refuses the ragged row", true,
                soundOnly.Any(i => i.Rule == "sound.raggedTable"), 0);

            // Now mend every fault, one by one, and watch the list empty. A check that
            // never falls silent proves nothing at all.
            report.Title = "Bulletin";
            report.Language = "en-GB";
            logo.AltText = "Logo ACME";
            logo.SourceField = "Logo";       // was empty: neither a picture nor a path to one
            code.Value = "ACME-001";

            // A barcode is shown, not read: filling it in leaves it as silent as the logo
            // was. The rule catching that here is the rule doing its job.
            code.AltText = "Code-barres du dossier ACME-001";
            second.Name = "paraphe";
            first.Tooltip = "Customer's signature";
            second.Tooltip = "Customer initials";
            short_.Add(new TextItem { Text = "C" });
            short_.Add(new TextItem { Text = "D" });
            short_.Add(new TextItem { Text = "E" });
            short_.Add(new TextItem { Text = "F" });

            pinned.Layout = LayoutMode.Stack;
            grid.GridColumns = 3;
            sub.TemplatePath = "fragment.json";
            advanced.Runs[0].Text = "Repaired text.";

            // Two levels declared, and both headings sit inside them - so the depth rule
            // has nothing to say either. It is exercised on its own below, where a third
            // level is reached and the format still stops at two.
            report.HeadingNumbering = "1.1";
            loose.Text = "See {HeadingNumber[1]} below";
            report.Body!.Remove(nested);
            sizeless.Dimension = new() { Width = 210, Height = 297 };

            var header = new TableRow(true);
            for (int i = 0; i < 6; i++) header.Add(new TextItem { Text = $"Col {i + 1}" });
            table.Add(header);
            report.Reattach();

            // One more control, added mended rather than broken: it is faultless, and the
            // point of the profile below is what a faultless template still loses.
            var approval = new FieldItem
            {
                Name = "approval",
                Kind = FieldKind.Signature,
                Signing = Signing.Digital,
                Tooltip = "Sign here",
                AltText = "Where the customer signs"   // a signature is a figure until it is named
            };
            report.Body!.Add(approval);

            // And one signed with a pen, which is the case the flattening rules must let
            // through: it writes no widget in either output, so it is drawn identically
            // both ways and loses nothing by being flattened. Faultless, and here so that
            // the harness confronts the two sides on a silence as well as on a finding -
            // a rule that fires on everything is confronted by nothing.
            var countersign = new FieldItem
            {
                Name = "countersign",
                Kind = FieldKind.Signature,
                Signing = Signing.ByHand,
                Tooltip = "Countersign here",
                AltText = "Where the surveyor countersigns"
            };
            report.Body!.Add(countersign);

            // A group that places its children at coordinates. Faultless for a page -
            // that is what absolute layout is for - and with no form at all where there
            // are no pages, which is the whole point of the profile below.
            // Two groups, and the fixture needs both or it cannot tell the two
            // validators apart: one whose pieces overlap - nothing said where they are,
            // so both take the whole band - and one that is a genuine row of cells.
            var overlapping = new ContainerItem { Layout = LayoutMode.Absolute };
            overlapping.Add(new TextItem { Text = "over" });
            overlapping.Add(new TextItem { Text = "under" });
            report.Body!.Add(overlapping);

            // The break moved one level in, onto the block being repeated - which is
            // where "one page per record" has to be stated. Mended in place, like every
            // other fault here: a rule still firing once the fault is gone is a rule
            // that teaches people to stop reading the panel.
            run.Style.PageBreak = PageBreak.None;
            letter.Style.PageBreak = PageBreak.Before;

            // One paragraph per text, which is what the rule asks for. Mended in place
            // rather than by dropping the item, so the shape that fired it is still in
            // the fixture and the silence below is about the rule and not about an
            // absence - section 5, and section 8f for why it has to fall silent at all.
            // Mended in place like every other fault here: the paragraph stays, and only
            // the role it could never carry goes. Deleting the item would leave the rule
            // silent about an absence rather than about a fault put right.
            notAControl.Role = PdfRole.Auto;

            broken.Text = "First paragraph.";
            var secondParagraph = new TextItem { Text = "Second one." };
            secondParagraph.Font.HorizontalAlignment = HorizontalAlignment.Justify;
            report.Body!.Add(secondParagraph);

            var inARow = new ContainerItem { Layout = LayoutMode.Absolute };
            var rowLeft = new TextItem { Text = "left column" };
            rowLeft.Style.Location.LeftValue = 0;
            rowLeft.Style.Location.WidthValue = 80;
            var rowRight = new TextItem { Text = "right column" };
            rowRight.Style.Location.LeftValue = 84;
            rowRight.Style.Location.Dock = Dock.Right;
            inARow.Add(rowLeft);
            inARow.Add(rowRight);
            report.Body!.Add(inARow);

            report.Reattach();

            var mended = TemplateValidator.Check(report);
            foreach (TemplateIssue issue in mended)
                Console.WriteLine($"    reste: {issue.Rule} {issue.Where}");

            AssertEqual(testName, "nothing left to say", 0, mended.Count, 0);

            // The third profile reads the same mended template and finds three faults in
            // it - not because the template is wrong, but because the output being aimed
            // at cannot carry what it asks for. This is the only class of fault where the
            // document still looks exactly right, which is what makes it worth a rule.
            var flat = TemplateValidator.Check(report, TemplateProfile.All,
                                               new RenderTarget(Tagged: false, Interactive: false,
                                                                Paged: false));
            foreach (TemplateIssue issue in flat)
                Console.WriteLine($"    {issue.Level,-7} {issue.Rule,-24} {issue.Message}");

            AssertEqual(testName, "roles and a language that reach nothing", true,
                flat.Any(i => i.Rule == "can.untagged"), 0);
            AssertEqual(testName, "fields drawn rather than fillable", true,
                flat.Any(i => i.Rule == "can.flattenedFields"), 0);
            AssertEqual(testName, "and a signature nobody can sign", true,
                flat.Any(i => i.Rule == "can.flattenedSignature"), 0);
            AssertEqual(testName, "and a group with no page to be placed on", true,
                flat.Any(i => i.Rule == "can.absoluteInFlow"), 0);

            // Silent about the output when there is none named: with nothing to compare
            // the template against, warning about a loss that is not happening is worse
            // than saying nothing.
            AssertEqual(testName, "and silent when no output is named", 0,
                TemplateValidator.Check(report, TemplateProfile.All, null)
                                 .Count(i => i.Rule.StartsWith("can.")), 0);

            // Written out for the same reason as the faulty pair above: the designer's
            // echo reads this template and must find these three, or it clears a document
            // that quietly loses its form on the way out.
            File.WriteAllText(Path.Combine(dir, "flattened.template.json"),
                System.Text.Json.JsonSerializer.Serialize(report, SourceGenerationContext.Default.Report));

            // One case per question asked, and each carries the target it was asked
            // under. The harness used to hold a copy of the target in JavaScript - so
            // changing it here would have left the two comparing different questions
            // while agreeing perfectly about the answers, which is section 16 exactly.
            //
            // It is also what makes the second case free. can.flattenedFieldAltText
            // needs Tagged, and the first target has everything off, so the rule could
            // not be reached at all: a rule written on both sides by hand and confronted
            // nowhere is the one most likely to drift.
            var asked = new[]
            {
                (Target: new RenderTarget(Tagged: false, Interactive: false, Paged: false),
                 Found: flat),
                (Target: new RenderTarget(Tagged: true, Interactive: false, Paged: true),
                 Found: TemplateValidator.Check(report, TemplateProfile.All,
                            new RenderTarget(Tagged: true, Interactive: false, Paged: true)))
            };

            File.WriteAllText(Path.Combine(dir, "flattened.issues.json"),
                "[" + string.Join(",", asked.Select(a =>
                    $$"""{"target":{"Tagged":{{a.Target.Tagged.ToString().ToLowerInvariant()}},"Interactive":{{a.Target.Interactive.ToString().ToLowerInvariant()}},"Paged":{{a.Target.Paged.ToString().ToLowerInvariant()}}},"issues":["""
                    + string.Join(",", a.Found.Select(i =>
                        $$"""{"rule":"{{i.Rule}}","level":"{{i.Level.ToString().ToLowerInvariant()}}"}"""))
                    + "]}"))
                + "]");

            // And the section that veraPDF passes must pass here too, or the two
            // disagree about what conformance means.
            Report clean = BoundAcmeInvoice(20);
            clean.Language = "en-GB";
            var real = TemplateValidator.Check(clean, TemplateProfile.Sound);
            foreach (TemplateIssue issue in real)
                Console.WriteLine($"    facture: {issue.Rule} {issue.Where}");

            AssertEqual(testName, "the real invoice is sound", 0, real.Count, 0);

            Console.WriteLine();
        }

        /// <summary>
        /// The third profile: not "is this template wrong" but "can the output being
        /// aimed at carry what it asks for".
        ///
        /// Every rule here is about a template that is perfectly sound and quietly
        /// loses something on the way out. That is the hard kind to notice, because
        /// the document still looks right - a flattened form is drawn exactly as a
        /// filled one, and an untagged file is indistinguishable on paper from a
        /// tagged one.
        ///
        /// Silent when the target can carry everything, which is the half that makes
        /// the other half worth anything.
        /// </summary>
        /// <summary>
        /// A field that will be drawn rather than left fillable is a figure in the
        /// structure tree, and the panel says so before a byte is written.
        ///
        /// PDF/UA 7.18.4 asks a Form element for exactly one child identifying its
        /// widget. Flattened output writes no widget at all, so the element had nothing
        /// under it and veraPDF refused the file - while the XMP went on claiming
        /// PDF/UA-1, and the 3b profile passed, so nothing on this side noticed.
        ///
        /// The rule chosen is the one this engine already had for a handwritten
        /// signature (section 4: "a handwritten signature is a figure, so it must carry
        /// an AltText like an image, or PDF/UA 7.3 refuses the file"), applied wherever
        /// the widget fails to arrive rather than only where it was first noticed.
        ///
        /// Both halves in one test on purpose: a validator rule is worth having only if
        /// it predicts what the renderer really does, and the two are written in
        /// different files by different hands.
        /// </summary>
        [Fact]
        public void Test83_AFieldThatWillBeDrawnIsAFigure()
        {
            const string testName = "Test 83";
            Console.WriteLine("Test 83: a flattened field is a figure, and the panel says so first");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            static Report Sheet(string? altText)
            {
                var report = new Report
                {
                    Title = "Flattened", Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack }
                };
                report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };
                report.Body!.Add(new TextItem { Text = "Please confirm", Role = PdfRole.Heading1,
                                                Font = new() { Size = 4 } });

                var field = new FieldItem
                {
                    Name = "customer.name", Kind = FieldKind.Text,
                    Value = "J. Smith", AltText = altText
                };
                field.Style.Location.HeightValue = 8;
                report.Body!.Add(field);
                report.Reattach();
                return report;
            }

            // --- what the panel says, before anything is rendered --------------------
            static int Says(Report report, bool tagged, bool interactive) =>
                TemplateValidator.Check(report, TemplateProfile.Capable,
                    new RenderTarget { Tagged = tagged, Interactive = interactive })
                    .Count(i => i.Rule == "can.flattenedFieldAltText");

            Console.WriteLine($"    balise + aplati, sans texte alternatif : {Says(Sheet(null), true, false)}");
            AssertEqual(testName, "tagged and flattened with nothing to say for it is a fault",
                1, Says(Sheet(null), true, false), 0);

            // Three silences, and they are the half that makes the speech worth anything.
            AssertEqual(testName, "with an alternative text it is silent",
                0, Says(Sheet("The customer's name"), true, false), 0);
            AssertEqual(testName, "left fillable it is silent",
                0, Says(Sheet(null), true, true), 0);
            AssertEqual(testName, "and untagged it is silent, there being no tree to be wrong about",
                0, Says(Sheet(null), false, false), 0);

            // --- and what the renderer really does -----------------------------------
            string Written(Report report, bool interactive)
            {
                var pdf = new PdfBackend(library)
                {
                    Conformance = PdfConformance.PdfA3b, Language = "en-GB",
                    Tagged = true, Interactive = interactive, Compress = false
                };
                ReportRenderer.Render(report, pdf);
                return System.Text.Encoding.Latin1.GetString(pdf.ToArray());
            }

            string flattened = Written(Sheet("The customer's name"), interactive: false);
            string fillable = Written(Sheet("The customer's name"), interactive: true);

            int formsFlat = CountOccurrences(flattened, "/S /Form");
            int formsLive = CountOccurrences(fillable, "/S /Form");
            Console.WriteLine($"    /S /Form : {formsFlat} aplati, {formsLive} remplissable");

            // The control first: interactive output really does write a Form, or the
            // assertion below would pass on an engine that had simply stopped writing
            // them at all.
            AssertEqual(testName, "left fillable, it is a form", true, formsLive > 0, 0);
            AssertEqual(testName, "flattened, it is not",       0, formsFlat, 0);

            // What used to sit here - "no structure element says /K -1" - is gone. It
            // could not fail: the emitter now writes no /K at all where there is no mark
            // to name, so the string is unwritable and the assertion was an executable
            // comment. The question it was reaching for is a real one, and test 85 asks
            // it properly: every entry in the page's map is an element that owns a mark.

            // What it BECAME, which is the claim of the fix and was asserted nowhere:
            // "no Form" is also true of an engine that stopped tagging the field at all,
            // and the /Alt below is written whatever the element turns out to be.
            AssertEqual(testName, "flattened, it is a figure", true,
                System.Text.RegularExpressions.Regex.IsMatch(
                    flattened, @"/S /Figure[^>]*/Alt \(The customer's name\)")
                || System.Text.RegularExpressions.Regex.IsMatch(
                    flattened, @"/Alt \(The customer's name\)[^>]*/S /Figure"), 0);

            // The figure it became carries what the panel demanded.
            AssertEqual(testName, "the figure says what it is",
                true, flattened.Contains("/Alt (The customer's name)"), 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test54_CapableProfile()
        {
            var testName = "Test 54: the Capable profile";
            Console.WriteLine($"Running {testName}...");

            Report quote = Quote();

            // --- the richest output: nothing to say ---------------------------------
            var rich = TemplateValidator.Check(quote, TemplateProfile.Capable,
                new RenderTarget(Tagged: true, Interactive: true));
            Console.WriteLine($"    capable target: {rich.Count} issue(s) "
                            + $"{string.Join(", ", rich.Select(i => i.Rule))}");
            AssertEqual(testName, "a capable target raises nothing", 0, rich.Count, 0);

            // --- and with no target at all, still nothing ---------------------------
            var untold = TemplateValidator.Check(quote, TemplateProfile.Capable);
            AssertEqual(testName, "and neither does saying nothing about the target", 0,
                untold.Count, 0);

            // --- flattened: the fields are drawn, the signature is a picture ---------
            var flat = TemplateValidator.Check(quote, TemplateProfile.Capable,
                new RenderTarget(Tagged: true, Interactive: false));
            foreach (TemplateIssue issue in flat)
                Console.WriteLine($"    {issue.Rule}/{issue.Level}: {issue.Message[..Math.Min(60, issue.Message.Length)]}…");

            AssertEqual(testName, "flattening is reported", true,
                flat.Any(i => i.Rule == "can.flattenedFields"), 0);

            // The quote's own signature is given with a pen: it writes no widget in
            // either output, so flattening takes nothing from it and the rule must stay
            // quiet. That silence is the half worth testing - the rule fired on every
            // signature until 25 August 2026, which is a rule that accuses a template
            // that is right, and section 15 says what that costs.
            AssertEqual(testName, "a signature given with a pen loses nothing by being drawn",
                false, flat.Any(i => i.Rule == "can.flattenedSignature"), 0);

            // A digital one does: interactive, a reader offers to sign it; flattened, it
            // is a picture of a place to sign, and nothing on the page says so.
            // Two of them, because one proves nothing about "one finding per signature":
            // a rule reporting only the first produces exactly one finding on a template
            // holding one, and that is how the rule stood until 25 August 2026 - and how
            // the sabotage that removed the by-hand exclusion from the designer's echo
            // went green, both sides reporting one whatever they held.
            var signed = Quote();
            foreach (string who in new[] { "endorsement", "counter_endorsement" })
            {
                var digital = new FieldItem
                {
                    Name = who, Kind = FieldKind.Signature, Signing = Signing.Digital,
                    Tooltip = "Endorse", AltText = $"Where {who.Replace('_', ' ')} is signed"
                };
                digital.Style.Location.HeightValue = 20;
                signed.Body!.Add(digital);
            }
            signed.Reattach();

            var lost = TemplateValidator.Check(signed, TemplateProfile.Capable,
                new RenderTarget(Tagged: true, Interactive: false));

            List<TemplateIssue> cannotSign =
                lost.Where(i => i.Rule == "can.flattenedSignature").ToList();

            AssertEqual(testName, "and a flattened signature is an error, not a warning", true,
                cannotSign.All(i => i.Level == IssueLevel.Error) && cannotSign.Count > 0, 0);

            // One per signature. Each is its own place to go and put right - globally by
            // leaving the fields fillable, or here by signing this one with a pen - and a
            // finding standing for the rest hides the others.
            AssertEqual(testName, "one finding per signature that lost something",
                2, cannotSign.Count, 0);

            // Naming them is what proves the rule discriminates rather than reporting the
            // signatures it meets: the pen-signed one is declared first in the body and is
            // in neither finding.
            AssertEqual(testName, "and it names the ones that lost something",
                "counter_endorsement, endorsement",
                string.Join(", ", cannotSign.Select(i => (i.Item as FieldItem)?.Name ?? "(none)")
                                            .OrderBy(n => n, StringComparer.Ordinal)), 0);

            // --- untagged: the accessibility work is dropped -------------------------
            // The showcase rather than the quote: the quote declares no language, no
            // role and no alternative text, so it has nothing to lose by being untagged
            // and the rule is right to stay quiet about it.
            Report described = Showcase.Build();
            var untagged = TemplateValidator.Check(described, TemplateProfile.Capable,
                new RenderTarget(Tagged: false, Interactive: true));
            AssertEqual(testName, "an untagged target drops what was declared for a reader", true,
                untagged.Any(i => i.Rule == "can.untagged"), 0);

            // A template that declares nothing for a reader has nothing to lose, so the
            // rule has to stay quiet - otherwise it fires on every plain document and
            // gets ignored, which is worse than not existing.
            var plain = new Report { Title = "plain", Body = new Body() };
            plain.Body!.Add(new TextItem { Text = "nothing declared here" });
            var plainUntagged = TemplateValidator.Check(plain, TemplateProfile.Capable,
                new RenderTarget(Tagged: false, Interactive: true));
            AssertEqual(testName, "but says nothing about a document that declared nothing",
                0, plainUntagged.Count, 0);

            // --- and the profile is opt-in ------------------------------------------
            var soundOnly = TemplateValidator.Check(quote, TemplateProfile.Sound,
                new RenderTarget(Tagged: false, Interactive: false));
            AssertEqual(testName, "Sound alone never reads the target", 0,
                soundOnly.Count(i => i.Rule.StartsWith("can.")), 0);

            Console.WriteLine();
        }

        /// <summary>
        /// A page break on a repeating container, and the three shapes it must not
        /// complain about.
        ///
        /// This nearly did not get written. All four placements are honoured exactly -
        /// measured: on the run it breaks once, on the repeated block once per record,
        /// on both it does both - so this is not a legal value doing nothing, which is
        /// what section 10 is about. The engine is right; the author is usually
        /// surprised, because RepeatOver clones what is *inside* a container and never
        /// the container.
        ///
        /// So the rule is narrowed to the one shape where the break plainly had
        /// somewhere better to be: a run wrapping a single block that carries none of
        /// its own. Where there is no inner container there is nowhere else to put it;
        /// where the inner one already breaks, the author has understood the
        /// difference. Both stay quiet.
        ///
        /// The silence is the half worth testing. A rule that fires on a template that
        /// is right teaches people to ignore the panel - and this panel is the only
        /// thing between a simplified editor and a document veraPDF will refuse.
        /// </summary>
        [Fact]
        public void Test63_ARepeatedBreakIsWhereItWasMeant()
        {
            const string testName = "Test 63";
            Console.WriteLine("Test 63: where a page break belongs in a repeated run");
            Console.WriteLine();

            static Report Build(bool onRun, bool onBlock, bool wrapped)
            {
                var report = new Report { Title = "t", Language = "en", Body = new Body() };
                report.Section.Dimension = new DocumentDimension { Width = 210, Height = 297 };

                var run = new ContainerItem { Layout = LayoutMode.Stack, RepeatOver = "Recipients" };
                if (onRun) run.Style.PageBreak = PageBreak.Before;

                if (wrapped)
                {
                    var block = new ContainerItem { Layout = LayoutMode.Stack };
                    if (onBlock) block.Style.PageBreak = PageBreak.Before;
                    block.Add(new TextItem { Text = "Cher {Name}," });
                    run.Add(block);
                }
                else
                {
                    // Nothing to move the break onto: the run's children are the letter.
                    run.Add(new TextItem { Text = "Cher {Name}," });
                    run.Add(new TextItem { Text = "Solde : {Balance}" });
                }

                report.Body!.Add(run);
                return report;
            }

            static int Fired(Report report) => TemplateValidator
                .Check(report, TemplateProfile.Sound)
                .Count(i => i.Rule == "sound.repeatPageBreak");

            // --- the one shape it is for ---------------------------------------------
            int misplaced = Fired(Build(onRun: true, onBlock: false, wrapped: true));
            Console.WriteLine($"    break on the run, a block inside   -> {misplaced} warning(s)");
            AssertEqual(testName, "a break on the run, with a block that could carry it",
                1, misplaced, 0);

            // --- and the three it is not ---------------------------------------------
            int noBreak = Fired(Build(onRun: false, onBlock: true, wrapped: true));
            Console.WriteLine($"    break on the block                 -> {noBreak} warning(s)");
            AssertEqual(testName, "a break already on the repeated block is right",
                0, noBreak, 0);

            int both = Fired(Build(onRun: true, onBlock: true, wrapped: true));
            Console.WriteLine($"    break on both                      -> {both} warning(s)");
            AssertEqual(testName, "and somebody who wrote both has understood the difference",
                0, both, 0);

            int nowhere = Fired(Build(onRun: true, onBlock: false, wrapped: false));
            Console.WriteLine($"    break on the run, nothing inside   -> {nowhere} warning(s)");
            AssertEqual(testName, "with no inner container there is nowhere else to put it",
                0, nowhere, 0);

            // --- and it says nothing about a run that never breaks --------------------
            int quiet = Fired(Build(onRun: false, onBlock: false, wrapped: true));
            AssertEqual(testName, "a run with no break at all is not the rule's business",
                0, quiet, 0);

            // --- the engine still honours all four, which is why this is only a hint --
            // If any of these changed, the rule would be describing something the engine
            // no longer does - worse than no rule, since it teaches the panel is wrong.
            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            const string data = """{ "Recipients": [ { "Name": "Ada" }, { "Name": "Grace" } ] }""";

            int OnRun = ReportBinder.Bind(Build(true, false, true), data, new BindingOptions())
                                    .Resolve(measurer).PageCount;
            int OnBlock = ReportBinder.Bind(Build(false, true, true), data, new BindingOptions())
                                       .Resolve(measurer).PageCount;

            Console.WriteLine($"    pages: on the run {OnRun}, on the block {OnBlock}");
            AssertEqual(testName, "a break on the run is one break, not none", 1, OnRun, 0);
            AssertEqual(testName, "and a break on the block is one per record", 2, OnBlock, 0);

            Console.WriteLine();
        }
    }
}
