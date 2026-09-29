namespace test
{
    /// <summary>
    /// Where an item lands inside the box it was given: coordinates and units, constraints,
    /// visibility, margin and padding, a rule spanning its band and a picture keeping its
    /// proportions, alignment when nothing else has spoken, and which containers flow rather
    /// than stack.
    /// </summary>
    public partial class EngineTests
    {
        [Fact]
        public void Test1_AbsolutePositioning()
        {
            var testName = "Test 1: Absolute Positioning (Millimeters)";
            Console.WriteLine($"Running {testName}...");

            var r = new Report
            {
                Title = testName,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Absolute }   // this test is about absolute coordinates
            };

            r.Body.Add(new TextItem
            {
                Text = "Absolute Item",
                Style = new()
                {
                    Location = new()
                    {
                        Left = (10, Units.Millimeter),
                        Top = (20, Units.Millimeter),
                        Width = (50, Units.Millimeter),
                        Height = (30, Units.Millimeter)
                    }
                }
            });

            r.Resolve();
            SaveReport(r, "test1_output.json");

            var item = r.Body.Childs.First();
            AssertEqual(testName, "Left", 10, item.Style.RenderedFrame.Left, 0.1);
            AssertEqual(testName, "Top", 20, item.Style.RenderedFrame.Top, 0.1);
            AssertEqual(testName, "Right", 60, item.Style.RenderedFrame.Right, 0.1);
            AssertEqual(testName, "Bottom", 50, item.Style.RenderedFrame.Bottom, 0.1);
            
            Console.WriteLine();
        }

        [Fact]
        public void Test2_PercentagePositioning()
        {
            var testName = "Test 2: Percentage Positioning";
            Console.WriteLine($"Running {testName}...");

            var r = new Report
            {
                Title = testName,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Absolute }   // this test is about absolute coordinates
            };

            r.Body.Add(new TextItem
            {
                Text = "Hello Percentage",
                Style = new()
                {
                    Location = new()
                    {
                        Left = (5, Units.Percentage),
                        Top = (15, Units.Millimeter),
                        Width = (40, Units.Millimeter),
                        Height = (30, Units.Millimeter)
                    }
                }
            });

            r.Resolve();
            SaveReport(r, "test2_output.json");

            var item = r.Body.Childs.First();
            // Body width = 210, so 5% = 10.5
            AssertEqual(testName, "Left (5% of 210)", 10.5, item.Style.RenderedFrame.Left, 0.1);
            AssertEqual(testName, "Top", 15, item.Style.RenderedFrame.Top, 0.1);
            AssertEqual(testName, "Width", 40, item.Style.RenderedFrame.Width, 0.1);
            AssertEqual(testName, "Height", 30, item.Style.RenderedFrame.Height, 0.1);
            
            Console.WriteLine();
        }

        [Fact]
        public void Test6_MixedUnits()
        {
            var testName = "Test 6: Mixed Units (MM + Percentage)";
            Console.WriteLine($"Running {testName}...");

            var r = new Report
            {
                Title = testName,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Absolute }   // this test is about absolute coordinates
            };

            r.Body.Add(new TextItem
            {
                Text = "Mixed Units",
                Style = new()
                {
                    Location = new()
                    {
                        Left = (10, Units.Percentage),      // 21mm (10% of 210)
                        Top = (15, Units.Millimeter),        // 15mm
                        Width = (50, Units.Percentage),      // 105mm (50% of 210)
                        Height = (20, Units.Millimeter)      // 20mm
                    }
                }
            });

            r.Resolve();
            SaveReport(r, "test6_output.json");

            var item = r.Body.Childs.First();
            AssertEqual(testName, "Left (10% of 210)", 21, item.Style.RenderedFrame.Left, 0.1);
            AssertEqual(testName, "Width (50% of 210)", 105, item.Style.RenderedFrame.Width, 0.1);
            AssertEqual(testName, "Top (15mm)", 15, item.Style.RenderedFrame.Top, 0.1);
            AssertEqual(testName, "Height (20mm)", 20, item.Style.RenderedFrame.Height, 0.1);
            
            Console.WriteLine();
        }

        [Fact]
        public void Test4_Constraints()
        {
            var testName = "Test 4: Constraints";
            Console.WriteLine($"Running {testName}...");

            var r = new Report
            {
                Title = testName,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new()
            };

            r.Body.Add(new TextItem
            {
                Text = "Constrained Item",
                Style = new()
                {
                    Location = new()
                    {
                        Left = (10, Units.Millimeter),
                        Top = (10, Units.Millimeter),
                        Width = (150, Units.Millimeter),
                        Height = (80, Units.Millimeter)
                    },
                    Constraints = new()
                    {
                        MaxWidth = 100,
                        MaxHeight = 50
                    }
                }
            });

            r.Resolve();
            SaveReport(r, "test4_output.json");

            var item = r.Body.Childs.First();
            AssertEqual(testName, "Width clamped to MaxWidth", 100, item.Style.RenderedFrame.Width, 0.1);
            AssertEqual(testName, "Height clamped to MaxHeight", 50, item.Style.RenderedFrame.Height, 0.1);
            
            Console.WriteLine();
        }

        [Fact]
        public void Test5_Visibility()
        {
            var testName = "Test 5: Visibility States";
            Console.WriteLine($"Running {testName}...");

            var r = new Report
            {
                Title = testName,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new()
            };

            r.Body.Add(new TextItem
            {
                Text = "Visible Item",
                Style = new()
                {
                    Visibility = Visibility.Visible,
                    Location = new() { Left = (10, Units.Millimeter), Top = (10, Units.Millimeter) }
                }
            });

            r.Body.Add(new TextItem
            {
                Text = "Gone Item",
                Style = new()
                {
                    Visibility = Visibility.Gone,
                    Location = new() { Left = (10, Units.Millimeter), Top = (30, Units.Millimeter) }
                }
            });

            r.Body.Add(new TextItem
            {
                Text = "Invisible Item",
                Style = new()
                {
                    Visibility = Visibility.Invisible,
                    Location = new() { Left = (10, Units.Millimeter), Top = (50, Units.Millimeter) }
                }
            });

            r.Resolve();
            SaveReport(r, "test5_output.json");

            var items = r.Body.Childs.ToList();
            AssertEqual(testName, "Visible item visibility", Visibility.Visible, items[0].Style.RenderedFrame.Visibility, 0);
            AssertEqual(testName, "Gone item visibility", Visibility.Gone, items[1].Style.RenderedFrame.Visibility, 0);
            AssertEqual(testName, "Invisible item visibility", Visibility.Invisible, items[2].Style.RenderedFrame.Visibility, 0);
            
            // Gone items should not have calculated positions
            AssertEqual(testName, "Gone item position not calculated", double.IsNaN(items[1].Style.RenderedFrame.Left), true, 0);
            
            Console.WriteLine();
        }

        [Fact]
        public void Test18_MarginAndPadding()
        {
            var testName = "Test 18: Margin and Padding";
            Console.WriteLine($"Running {testName}...");

            // --- margin, in absolute mode -----------------------------------
            var r = BandedReport(testName);
            r.Body!.Layout = LayoutMode.Absolute;

            var offset = new TextItem
            {
                Text = "M",
                Style = new()
                {
                    Margin = new() { Left = 5, Top = 3, Right = 8 },
                    Location = new() { Left = (0, Units.Millimeter), Top = (0, Units.Millimeter),
                                       Dock = Dock.Right }
                }
            };
            r.Body.Add(offset);

            var layout = r.Resolve();
            var frame = layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, offset)).Frame;

            // The band is 0..210 wide and starts at y=20; the margin shrinks it on each side.
            AssertEqual(testName, "margin shifts the left edge", 5, frame.Left, 0.01);
            AssertEqual(testName, "margin shifts the top edge", 23, frame.Top, 0.01);
            AssertEqual(testName, "margin pulls the right anchor in", 202, frame.Right, 0.01);

            // --- padding grows an auto-sized box ----------------------------
            var r2 = BandedReport(testName);
            r2.Body!.Layout = LayoutMode.Absolute;

            var padded = new TextItem
            {
                Text = "AB",
                Style = new()
                {
                    Padding = new() { Left = 2, Top = 2, Right = 2, Bottom = 2 },
                    Location = new() { Left = (10, Units.Millimeter), Top = (10, Units.Millimeter) }
                }
            };
            r2.Body.Add(padded);

            var backend = new SvgBackend();
            var layout2 = ReportRenderer.Render(r2, backend);
            var box = layout2.Pages[0].Items.First(p => ReferenceEquals(p.Source, padded)).Frame;

            // 1 rem = 3mm caps -> em = 3/0.7mm; the stand-in measurer gives each glyph
            // half an em, so "AB" is one em wide. A line is 1.5 cap heights tall: the
            // leading follows the size that was asked for, not the em and not the font.
            double em = 3.0 / 0.70;
            double line = 3.0 * 1.5;
            AssertEqual(testName, "padding widens the box", em + 4, box.Width, 0.01);
            AssertEqual(testName, "padding heightens the box", line + 4, box.Height, 0.01);

            // ...and the text is drawn inside that padding, not on the frame edge.
            AssertEqual(testName, "text drawn inside the padding", true,
                backend.Pages[0].Contains("<text x=\"12\""), 0);

            // --- row padding insets cells on all four sides -----------------
            var r3 = BandedReport(testName);
            var table = new TableItem();
            table.AddColumn(40);
            table.AddColumn(40);
            var row = table.AddRow(Cell("A"), Cell("B"));
            row.Style.Padding = new() { Left = 3, Top = 1, Right = 3, Bottom = 1 };
            r3.Body!.Add(table);

            var layout3 = r3.Resolve();
            var items = layout3.Pages[0].Items;
            var rowFrame = items.First(p => ReferenceEquals(p.Source, row)).Frame;
            var cellA = items.First(p => ReferenceEquals(p.Source, row.Childs[0])).Frame;
            var cellB = items.First(p => ReferenceEquals(p.Source, row.Childs[1])).Frame;

            AssertEqual(testName, "cell inset from the column edge", 3, cellA.Left - rowFrame.Left, 0.01);
            AssertEqual(testName, "cell inset from the next column", 37, cellA.Right - rowFrame.Left, 0.01);
            AssertEqual(testName, "second cell follows the same rule", 43, cellB.Left - rowFrame.Left, 0.01);
            AssertEqual(testName, "cell inset vertically", 1, cellA.Top - rowFrame.Top, 0.01);
            AssertEqual(testName, "row height carries its padding", line + 2, rowFrame.Height, 0.01);

            Console.WriteLine();
        }

        [Fact]
        public void Test14_LinesAndImages()
        {
            var testName = "Test 14: Lines and Images";
            Console.WriteLine($"Running {testName}...");

            var r = BandedReport(testName);

            var rule = new LineItem { Pen = new() { Width = 0.5 } };
            var logo = new ImageItem
            {
                NaturalWidth = 40,
                NaturalHeight = 20,
                Scaling = ImageScaling.Uniform,
                Style = new() { Location = new() { Width = (60, Units.Millimeter) } }
            };
            r.Body!.Add(rule);
            r.Body.Add(logo);

            var layout = r.Resolve();
            var placedRule = layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, rule));
            var placedLogo = layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, logo));

            AssertEqual(testName, "rule spans the band", 210, placedRule.Frame.Width, 0.1);
            AssertEqual(testName, "rule is as thick as its pen", 0.5, placedRule.Frame.Height, 0.01);
            AssertEqual(testName, "rule sits at band top", 20, placedRule.Frame.Top, 0.1);

            // 60mm wide at a 2:1 natural ratio is 30mm tall, stacked under the rule.
            AssertEqual(testName, "image keeps its aspect ratio", 30, placedLogo.Frame.Height, 0.1);
            AssertEqual(testName, "image follows the rule", 20.5, placedLogo.Frame.Top, 0.1);

            Console.WriteLine();
        }

        [Fact]
        public void Test43_AbsoluteSurvivesTheFile()
        {
            var testName = "Test 43: A form pinned to coordinates, saved and read back";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // A body laid out to fixed marks: a form does not flow, and this is the one
            // shape the whole engine is otherwise built to avoid.
            var report = new Report
            {
                Title = "Formulaire",
                Language = "en-GB",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Absolute }
            };

            var pinned = new TextItem { Text = "bottom right", Font = new() { Size = 1 } };
            pinned.Style.Location.Left = (150, Units.Millimeter);
            pinned.Style.Location.Top = (200, Units.Millimeter);
            report.Body!.Add(pinned);

            // Wide enough to need three lines, and told so.
            var wrapped = new TextItem
            {
                Text = "a text long enough that it has to wrap if the width "
                     + "it is given is genuinely honoured by the engine",
                Font = new() { Size = 1 }
            };
            wrapped.Style.Location.Left = (10, Units.Millimeter);
            wrapped.Style.Location.Top = (20, Units.Millimeter);
            wrapped.Style.Location.Width = (60, Units.Millimeter);
            report.Body!.Add(wrapped);

            report.Reattach();

            // The template is round tripped through JSON on every bind, so this is not a
            // theoretical journey: it is what happens to every section produced.
            Report reloaded = Report.FromJson(report.ToJson());

            AssertEqual(testName, "the body is still laid out absolutely",
                LayoutMode.Absolute, reloaded.Body!.Layout, 0);

            var backend = new SvgBackend(new FontTextMeasurer(library));
            ReportRenderer.Render(reloaded, backend);
            string svg = backend.Pages[0];

            // Stacked, the pinned text would sit at the top of the band; the coordinate
            // is the whole point of the mode.
            var placed = System.Text.RegularExpressions.Regex.Matches(
                svg, @"<text x=""([\d.]+)"" y=""([\d.]+)""");

            bool atItsMark = placed.Any(m => Math.Abs(double.Parse(m.Groups[1].Value,
                                 System.Globalization.CultureInfo.InvariantCulture) - 150) < 0.5);
            AssertEqual(testName, "and the pinned text is at its mark", true, atItsMark, 0);

            // A width means a width. Measured for three lines and drawn as one was the
            // shape of the fault: the box was right and the ink went across the page.
            int lines = System.Text.RegularExpressions.Regex.Matches(
                svg, @"<text x=""10""").Count;
            Console.WriteLine($"    the 60 mm text fits on {lines} lines");
            AssertEqual(testName, "a stated width breaks the text", true, lines >= 3, 0);

            AssertEqual(testName, "and none of them is the whole thing", false,
                svg.Contains("genuinely honoured by the engine</text>")
                && svg.Contains("a text long enough that it has to wrap"), 0);

            // A width the engine worked out for itself is not a width to break to: the
            // two numbers are equal to the last bit and the last word falls off.
            var measured = new TextItem { Text = "14,252,123.30 EUR", Font = new() { Size = 1.35, Bold = true } };
            measured.Style.Location.Right = (0, Units.Millimeter);
            measured.Style.Location.Top = (250, Units.Millimeter);
            reloaded.Body!.Add(measured);
            reloaded.Reattach();

            var again = new SvgBackend(new FontTextMeasurer(library));
            ReportRenderer.Render(reloaded, again);

            AssertEqual(testName, "a self-measured text keeps its last word", true,
                again.Pages[0].Contains("14,252,123.30 EUR"), 0);

            Console.WriteLine();
        }

/// <summary>
        /// Which containers flow, measured rather than remembered.
        ///
        /// ContainerItem.Layout starts at Absolute and the containers that have to
        /// paginate set themselves to Stack in their constructors. Which ones those are
        /// has been written down wrongly twice: "Body, Header and Footer" for eight
        /// months, then "Body alone, and the other two have no constructor" - the second
        /// arrived while correcting the first, from a five-line window of a file.
        ///
        /// Both survived because a sentence naming classes is a claim nothing can
        /// contradict. This constructs every container in the assembly and reads the
        /// answer off the object, so the next time the sentence is wrong the suite says
        /// so instead of a reader six months later.
        /// </summary>
        [Fact]
        public void Test70_WhichContainersFlow()
        {
            const string testName = "Test 70";
            Console.WriteLine("Test 70: which containers flow");
            Console.WriteLine();

            var containers = typeof(ContainerItem).Assembly.GetTypes()
                .Where(t => t.IsPublic && !t.IsAbstract && typeof(ContainerItem).IsAssignableFrom(t))
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToList();

            var flows = new List<string>();
            var pinned = new List<string>();

            foreach (Type type in containers)
            {
                var made = (ContainerItem)Activator.CreateInstance(type)!;
                (made.Layout == LayoutMode.Stack ? flows : pinned).Add(type.Name);
                Console.WriteLine($"    {type.Name,-18} {made.Layout}");
            }

            AssertEqual(testName, "every container was constructed", true, containers.Count >= 8, containers.Count);

            // Named here because this is the sentence that keeps going wrong. Adding a
            // container that flows should fail this and send someone to the two places
            // that describe it - ContainerItem.Layout's own summary, and CLAUDE.md.
            AssertEqual(testName, "the containers that flow are the four that must",
                "Body, ImageGridItem, SubTemplateItem, TableItem",
                string.Join(", ", flows.OrderBy(n => n, StringComparer.Ordinal)), 0);

            AssertEqual(testName, "and a band, a section and a plain group do not", true,
                pinned.Contains("Header") && pinned.Contains("Footer")
                && pinned.Contains("Section") && pinned.Contains("ContainerItem"), 0);

            // What that costs, measured on the shape the comment warns about: two
            // children of an absolute band, neither stating a position, land on exactly
            // the same rectangle.
            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            var report = new Report
            {
                Title = "t", Language = "en-GB",
                Dimension = new() { Width = 210, Height = 297 },
                Header = new Header()
            };
            report.Header!.Style.Location.HeightValue = 40;
            report.Header!.Add(new TextItem { Text = "FIRST", Font = new() { Size = 3 } });
            report.Header!.Add(new TextItem { Text = "SECOND", Font = new() { Size = 3 } });
            report.Body = new Body();
            report.Reattach();

            var placed = report.Resolve(new FontTextMeasurer(library)).Pages[0].Items
                .Where(i => i.Source is TextItem t && (t.Text == "FIRST" || t.Text == "SECOND"))
                .Select(i => (((TextItem)i.Source!).Text, i.Frame.Top, i.Frame.Left))
                .ToList();

            foreach ((string text, double top, double left) in placed)
                Console.WriteLine($"    {text,-8} at {left:F2}, {top:F2}");

            AssertEqual(testName, "both children of the absolute band were placed", 2, placed.Count, 0);
            AssertEqual(testName, "and they land on the same spot, which is the whole warning",
                true, placed.Count == 2
                      && Math.Abs(placed[0].Top - placed[1].Top) < 0.01
                      && Math.Abs(placed[0].Left - placed[1].Left) < 0.01, 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test32_ItemAlignment()
        {
            var testName = "Test 32: Aligning the Item Itself";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            var measurer = new FontTextMeasurer(library);

            // A band 180mm wide, holding 60mm-wide items placed three ways.
            var report = new Report
            {
                Dimension = new() { Width = 210, Height = 297 },
                DefaultFont = new() { Family = DmsFont },
                Body = new() { Layout = LayoutMode.Absolute }
            };
            report.Section.Style.Padding = new() { Left = 15, Right = 15, Top = 20, Bottom = 20 };

            ImageItem Block(ItemAlign across, ItemAlign down)
            {
                var block = new ImageItem { NaturalWidth = 60, NaturalHeight = 20 };
                block.Style.Location.Width = (60, Units.Millimeter);
                block.Style.Location.Height = (20, Units.Millimeter);
                block.Style.AlignHorizontal = across;
                block.Style.AlignVertical = down;
                report.Body!.Add(block);
                return block;
            }

            ImageItem left = Block(ItemAlign.Start, ItemAlign.Start);
            ImageItem middle = Block(ItemAlign.Center, ItemAlign.Center);
            ImageItem right = Block(ItemAlign.End, ItemAlign.End);
            ImageItem plain = Block(ItemAlign.None, ItemAlign.None);

            // Dock and coordinates always win: alignment only speaks when nothing else has.
            ImageItem pinned = Block(ItemAlign.Center, ItemAlign.Center);
            pinned.Style.Location.Left = (5, Units.Millimeter);
            pinned.Style.Location.Top = (7, Units.Millimeter);

            LayoutResult layout = report.Resolve(measurer);
            Rect Frame(BaseItem item) => layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, item)).Frame;

            // The body band runs 15..195 across and 20..277 down.
            AssertEqual(testName, "Start hugs the left", 15, Frame(left).Left, 0.01);
            AssertEqual(testName, "Center is centred", 15 + (180 - 60) / 2, Frame(middle).Left, 0.01);
            AssertEqual(testName, "End hugs the right", 195 - 60, Frame(right).Left, 0.01);
            AssertEqual(testName, "None is left, as it always was", 15, Frame(plain).Left, 0.01);

            AssertEqual(testName, "Start hugs the top", 20, Frame(left).Top, 0.01);
            AssertEqual(testName, "Center is centred down too", 20 + (257 - 20) / 2.0, Frame(middle).Top, 0.01);
            AssertEqual(testName, "End hugs the bottom", 277 - 20, Frame(right).Top, 0.01);

            AssertEqual(testName, "a coordinate beats the alignment", 20, Frame(pinned).Left, 0.01);
            AssertEqual(testName, "on both axes", 27, Frame(pinned).Top, 0.01);

            // An anchor beats it too, and stretching still stretches.
            var stretched = new ImageItem { NaturalWidth = 60, NaturalHeight = 20 };
            stretched.Style.Location.Height = (20, Units.Millimeter);
            stretched.Style.Location.Dock = Dock.Left | Dock.Right;
            stretched.Style.AlignHorizontal = ItemAlign.Center;
            report.Body!.Add(stretched);

            LayoutResult again = report.Resolve(measurer);
            AssertEqual(testName, "an anchor beats it as well", 180,
                again.Pages[0].Items.First(p => ReferenceEquals(p.Source, stretched)).Frame.Width, 0.01);

            // --- and it works in the flow, which is where a logo usually sits ------
            var flowing = new Report
            {
                Dimension = new() { Width = 210, Height = 297 },
                DefaultFont = new() { Family = DmsFont },
                Body = new()
            };
            var centred = new TextItem { Text = "Centred", Font = new() { Size = 1.5 } };
            centred.Style.Location.Width = (40, Units.Millimeter);
            centred.Style.AlignHorizontal = ItemAlign.Center;
            flowing.Body!.Add(centred);

            Rect inFlow = flowing.Resolve(measurer).Pages[0].Items
                .First(p => ReferenceEquals(p.Source, centred)).Frame;
            AssertEqual(testName, "centred in a flow too", (210 - 40) / 2, inFlow.Left, 0.01);

            // --- it survives the round trip ----------------------------------------
            Report reloaded = Report.FromJson(report.ToJson());
            AssertEqual(testName, "the alignment is written down", ItemAlign.Center,
                reloaded.Body!.Childs[1].Style.AlignHorizontal, 0);

            Console.WriteLine();
        }

/// <summary>
        /// An item that measured its own size sits where its alignment says.
        ///
        /// Style.AlignHorizontal and Style.AlignVertical move an item inside the room it
        /// was given, but only on an axis nothing pinned - a coordinate, an edge it is
        /// stuck to, or a stated size has already decided, and an alignment must not
        /// argue with any of them.
        ///
        /// Written out for the designer's echo, which had neither: the property reached
        /// the engine, the panel and the flow editor and never frisket-core.js, so the
        /// preview put a centred item against the top-left of its band while the engine
        /// centred it. The fifth column of section 1's table, in the wild.
        /// </summary>
        [Fact]
        public void Test69_AlignedInsideItsBand()
        {
            const string testName = "Test 69";
            Console.WriteLine("Test 69: an item sits where its alignment says");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            static TextItem Placed(string text, ItemAlign across, ItemAlign down)
            {
                var item = new TextItem { Text = text, Font = new TextStyle { Size = 3 } };
                item.Style.AlignHorizontal = across;
                item.Style.AlignVertical = down;
                return item;
            }

            var report = new Report
            {
                Title = "Aligned", Language = "en-GB",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Absolute }
            };
            report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };

            report.Body!.Add(Placed("START", ItemAlign.Start, ItemAlign.Start));
            report.Body!.Add(Placed("CENTRE", ItemAlign.Center, ItemAlign.Center));
            report.Body!.Add(Placed("END", ItemAlign.End, ItemAlign.End));

            // A fourth that must NOT move: it states both edges, so nothing is left to
            // align. Without it the check cannot tell "the alignment fired" from "the
            // echo moves everything".
            var pinned = Placed("PINNED", ItemAlign.Center, ItemAlign.Center);
            pinned.Style.Location.Left = (0, Units.Millimeter);
            pinned.Style.Location.Top = (0, Units.Millimeter);
            report.Body!.Add(pinned);

            report.Reattach();

            LayoutResult layout = report.Resolve(measurer);
            var frames = layout.Pages[0].Items
                .Where(i => i.Source is TextItem { Text.Length: > 0 })
                .ToDictionary(i => ((TextItem)i.Source!).Text,
                              i => (i.Frame.Left, i.Frame.Top, i.Frame.Right, i.Frame.Bottom));

            foreach ((string name, var f) in frames.OrderBy(k => k.Key, StringComparer.Ordinal))
                Console.WriteLine($"    {name,-8} x {f.Left,7:F2}..{f.Right,7:F2}   y {f.Top,7:F2}..{f.Bottom,7:F2}");

            AssertEqual(testName, "all four were placed", 4, frames.Count, 0);

            // The three that align must land in three different places on both axes, or
            // the sample proves nothing about alignment.
            AssertEqual(testName, "Start is left of Centre, which is left of End", true,
                frames["START"].Left < frames["CENTRE"].Left
                && frames["CENTRE"].Left < frames["END"].Left, 0);
            AssertEqual(testName, "and Start is above Centre, which is above End", true,
                frames["START"].Top < frames["CENTRE"].Top
                && frames["CENTRE"].Top < frames["END"].Top, 0);

            // A stated coordinate wins over an alignment.
            AssertEqual(testName, "a pinned item is not moved by its alignment",
                frames["START"].Left, frames["PINNED"].Left, 0.01);

            // Written out so the echo can be held to the same four frames.
            string dir = OutputDirectory("align");
            File.WriteAllText(Path.Combine(dir, "aligned.template.json"),
                System.Text.Json.JsonSerializer.Serialize(report, SourceGenerationContext.Default.Report));
            File.WriteAllText(Path.Combine(dir, "aligned.frames.json"),
                "{" + string.Join(",", frames.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k =>
                    $"\"{k.Key}\":{{\"left\":{k.Value.Left.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},"
                    + $"\"top\":{k.Value.Top.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},"
                    + $"\"right\":{k.Value.Right.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},"
                    + $"\"bottom\":{k.Value.Bottom.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}}}"))
                + "}");

            Console.WriteLine($"  # {Path.Combine(dir, "aligned.frames.json")}");
            Console.WriteLine();
        }
    }
}
