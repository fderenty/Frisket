namespace test
{
    /// <summary>
    /// The template as a file: what it writes down and reads back, the shape the designer
    /// saves, defaults that must survive being omitted, and the JSON schema and reference
    /// page generated off the model by reflection.
    ///
    /// Nothing in tests/ begins with "Template" except the generators themselves
    /// (TemplateSchema.cs, TemplateReference.cs), so that a reader hunting the schema is
    /// never offered the check and the subject under two names a letter apart.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>Builds the same invoice as test 17, so the round trip has something real to chew on.</summary>
        static Report BuildInvoice()
        {
            var r = new Report
            {
                Title = "Invoice F-2025-0142",
                Dimension = new() { Width = 210, Height = 297 },
                RootFontSize = 3.0,
                GeneratedAt = new DateTimeOffset(2025, 7, 24, 9, 30, 0, TimeSpan.Zero),
                Header = new(),
                Footer = new(),
                Body = new()
            };
            r.Fields["Customer"] = "ACME Industries";
            r.Section.Style.Padding = new() { Left = 15, Right = 15, Top = 12, Bottom = 12 };

            r.Header!.Style.Location.Height = (32, Units.Millimeter);
            r.Header.Add(new TextItem
            {
                Text = "{Title}",
                Font = new() { Family = InvoiceFont, Size = 2, SizeUnit = FontSizeUnit.Rem, Bold = true },
                Style = new() { Location = new() { Left = (0, Units.Millimeter), Top = (0, Units.Millimeter) } }
            });
            r.Header.Add(new LineItem
            {
                Pen = new() { Width = 0.4, DashStyle = DashStyle.Dash },
                Style = new() { Location = new() { Bottom = (2, Units.Millimeter), Dock = Dock.Left | Dock.Right } }
            });

            r.Footer!.Style.Location.Height = (14, Units.Millimeter);
            r.Footer.Add(new TextItem
            {
                Text = "Page {Page} / {Pages}",
                Font = new() { Family = InvoiceFont, Size = 0.85, SizeUnit = FontSizeUnit.Rem,
                               HorizontalAlignment = HorizontalAlignment.Right, Color = new(90, 90, 90) },
                Style = new() { Location = new() { Right = (0, Units.Millimeter), Bottom = (0, Units.Millimeter) } }
            });

            var table = new TableItem
            {
                Grid = new() { Width = 0.15, Color = new(180, 180, 180) },
                RepeatHeader = true
            };
            table.AddColumn(22);
            table.AddAutoColumn();
            table.AddColumn(18, Units.Percentage);

            var head = table.AddHeaderRow(Label("Ref", bold: true), Label("Description", bold: true),
                                          Label("Total HT", bold: true, align: HorizontalAlignment.Right));
            head.Style.Background = new() { Color = new(238, 238, 238) };
            head.Style.Padding = new() { Left = 1.5, Right = 1.5, Top = 1.5, Bottom = 1.5 };

            for (int i = 1; i <= 30; i++)
            {
                var row = table.AddRow(Label($"A-{i:000}"),
                                       Label($"Service no. {i}"),
                                       Label($"{(i * 19.9):0.00} €", align: HorizontalAlignment.Right));
                row.Style.Padding = new() { Left = 1.5, Right = 1.5, Top = 1.2, Bottom = 1.2 };
                row.Style.Splitting = Splitting.KeepTogether;
            }
            r.Body!.Add(table);

            var watermark = new TextItem
            {
                Text = "COPIE",
                Style = new() { PageScope = PageScope.Last, Visibility = Visibility.Invisible,
                                Location = new() { Top = (140, Units.Millimeter) } }
            };
            r.Section.Add(watermark);

            return r;
        }

        /// <summary>
        /// Every item the model can hold clones to its own type and keeps every property
        /// it carries.
        ///
        /// Asked of the assembly rather than of a list written here, because reading a
        /// dozen CopyTo methods and noticing which assignment is missing is what a person
        /// does badly and reflection does perfectly. Two faults were found this way in one
        /// afternoon and neither by reading: <c>FieldItem.CopyTo</c> copied eleven
        /// properties of twelve, and <c>BarcodeItem</c> overrode neither method, so
        /// cloning one gave back a plain <see cref="BaseItem"/> - an item of no kind,
        /// which draws as nothing.
        ///
        /// A clone is not a corner. <c>RepeatOver</c> clones the children of a repeating
        /// container once per record, and <c>SubTemplateExpander</c> clones every spliced
        /// child - so what a clone drops is dropped in every repeated copy and in none of
        /// the originals, which is why a template looks right and a document does not.
        ///
        /// A type added tomorrow joins this check by existing, which is the point.
        /// </summary>
        [Fact]
        public void Test80_EveryItemClonesToItselfWhole()
        {
            const string testName = "Test 80";
            Console.WriteLine("Test 80: every item clones to its own type, with everything it carried");
            Console.WriteLine();

            var kinds = typeof(BaseItem).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(BaseItem)) && !t.IsAbstract && t.IsPublic)
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.Name).ToList();

            Console.WriteLine($"    {kinds.Count} types d'element trouves dans le modele");
            AssertEqual(testName, "the model has items to check at all", true, kinds.Count > 8, 0);

            var wrongType = new List<string>();
            var dropped = new List<string>();

            foreach (Type kind in kinds)
            {
                var item = (BaseItem)Activator.CreateInstance(kind)!;

                // Every settable property given a value that is not its default, or a
                // clone that dropped it would land on the same value and say nothing.
                var carried = kind.GetProperties()
                    .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                    .Where(p => p.Name is not ("Parent" or "Owner" or "Childs"))
                    .ToList();

                foreach (var property in carried)
                {
                    object? unusual = Unusual(property.PropertyType, property.GetValue(item));
                    if (unusual != null)
                        property.SetValue(item, unusual);
                }

                // A type with no CreateEmpty but a CopyTo throws here rather than
                // returning the wrong thing - the cast inside CopyTo fails. Caught, so
                // that both shapes of the same fault are reported in the same words
                // instead of one of them arriving as a stack trace.
                BaseItem copy;
                try
                {
                    copy = item.Clone();
                }
                catch (InvalidCastException)
                {
                    wrongType.Add($"{kind.Name} -> threw, so CreateEmpty is missing");
                    continue;
                }

                if (copy.GetType() != kind)
                {
                    wrongType.Add($"{kind.Name} -> {copy.GetType().Name}");
                    continue;   // every property is lost with the type; one line says it
                }

                foreach (var property in carried)
                {
                    string mine = Describe(property.GetValue(item));
                    string theirs = Describe(property.GetValue(copy));
                    if (mine != theirs)
                        dropped.Add($"{kind.Name}.{property.Name}: {mine} -> {theirs}");
                }
            }

            foreach (string loss in wrongType)
                Console.WriteLine($"    type perdu : {loss}");
            foreach (string loss in dropped)
                Console.WriteLine($"    perdu      : {loss}");

            AssertEqual(testName, "a clone is of the type it was made from", 0, wrongType.Count, 0);
            AssertEqual(testName, "and keeps every property that type carries", 0, dropped.Count, 0);

            // Named as well, so a reader knows what this was written for rather than only
            // that it passes today.
            AssertEqual(testName, "a barcode included", true,
                new BarcodeItem { Value = "4006381333931" }.Clone() is BarcodeItem { Value: "4006381333931" }, 0);

            Console.WriteLine();
        }

        /// <summary>
        /// A value of this type that is not what the item was born with, so that losing
        /// it is visible. Null when nothing sensible can be made - the property is then
        /// left as it is and compared as it is, which proves less but never lies.
        /// </summary>
        private static object? Unusual(Type type, object? current)
        {
            Type bare = Nullable.GetUnderlyingType(type) ?? type;

            if (bare == typeof(string)) return "carried";
            if (bare == typeof(bool)) return !(current as bool? ?? false);
            if (bare == typeof(int)) return (current as int? ?? 0) + 7;
            if (bare == typeof(double)) return (current as double? ?? 0) + 7;
            if (bare == typeof(Color)) return new Color(11, 22, 33);
            if (bare.IsEnum)
            {
                foreach (object value in Enum.GetValues(bare))
                    if (!Equals(value, current))
                        return value;
            }
            return null;
        }


        [Fact]
        public void Test20_JsonRoundTrip()
        {
            var testName = "Test 20: JSON Round Trip";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            var measurer = new FontTextMeasurer(library);

            var original = BuildInvoice();
            string json = original.ToJson();

            var reloaded = Report.FromJson(json);

            // The definition survives.
            AssertEqual(testName, "title", original.Title, reloaded.Title, 0);
            AssertEqual(testName, "page size", original.Dimension.Width, reloaded.Dimension.Width, 0.001);
            AssertEqual(testName, "root font size", original.RootFontSize, reloaded.RootFontSize, 0.001);
            AssertEqual(testName, "custom field", "ACME Industries", reloaded.Fields["Customer"], 0);
            AssertEqual(testName, "timestamp", original.GeneratedAt!.Value, reloaded.GeneratedAt!.Value, 0);
            AssertEqual(testName, "section padding", 15, reloaded.Section.Style.Padding.Left, 0.001);

            // The tree survives, with its shape and its derived types.
            AssertEqual(testName, "same item count", original.AllItems.Count(), reloaded.AllItems.Count(), 0);
            AssertEqual(testName, "header rebuilt", true, reloaded.Header != null, 0);
            AssertEqual(testName, "children are not lost", 2, reloaded.Header!.Childs.Count, 0);

            var reloadedTable = reloaded.Body!.Childs.OfType<TableItem>().First();
            AssertEqual(testName, "table type preserved", 3, reloadedTable.Columns.Count, 0);
            AssertEqual(testName, "column unit preserved", Units.Percentage, reloadedTable.Columns[2].WidthUnit, 0);
            AssertEqual(testName, "rows preserved", 31, reloadedTable.Childs.Count, 0);
            AssertEqual(testName, "header row flag preserved", true, ((TableRow)reloadedTable.Childs[0]).IsHeader, 0);
            AssertEqual(testName, "grid pen preserved", 0.15, reloadedTable.Grid!.Width, 0.001);

            var reloadedRule = reloaded.Header.Childs.OfType<LineItem>().First();
            AssertEqual(testName, "dash style preserved", DashStyle.Dash, reloadedRule.Pen.DashStyle, 0);

            var reloadedMark = reloaded.Section.Childs.OfType<TextItem>().First(t => t.Text == "COPIE");
            AssertEqual(testName, "page scope preserved", PageScope.Last, reloadedMark.Style.PageScope, 0);
            AssertEqual(testName, "visibility preserved", Visibility.Invisible, reloadedMark.Style.Visibility, 0);

            // Parent and Owner are rebuilt, not serialized.
            AssertEqual(testName, "parents rebuilt", true,
                reloaded.AllItems.All(i => i.Parent != null && i.Owner == reloaded), 0);

            // The real proof: both lay out to exactly the same geometry.
            var a = original.Resolve(measurer);
            var b = reloaded.Resolve(measurer);

            AssertEqual(testName, "same page count", a.PageCount, b.PageCount, 0);

            int compared = 0;
            bool identical = a.PageCount == b.PageCount;
            for (int p = 0; identical && p < a.PageCount; p++)
            {
                var left = a.Pages[p].Items;
                var right = b.Pages[p].Items;
                if (left.Count != right.Count)
                {
                    identical = false;
                    break;
                }

                for (int i = 0; i < left.Count; i++)
                {
                    if (left[i].Source.GetType() != right[i].Source.GetType()
                        || Math.Abs(left[i].Frame.Left - right[i].Frame.Left) > 0.001
                        || Math.Abs(left[i].Frame.Top - right[i].Frame.Top) > 0.001
                        || Math.Abs(left[i].Frame.Right - right[i].Frame.Right) > 0.001
                        || Math.Abs(left[i].Frame.Bottom - right[i].Frame.Bottom) > 0.001
                        || left[i].Fragment != right[i].Fragment)
                    {
                        identical = false;
                        break;
                    }
                    compared++;
                }
            }

            AssertEqual(testName, $"every one of {compared} placements is identical", true, identical, 0);

            // And it round trips again, byte for byte.
            AssertEqual(testName, "re-serialising is byte for byte identical", true, reloaded.ToJson() == json, 0);

            var outputDir = OutputDirectory();
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "invoice-template.json"), json);
            Console.WriteLine($"  💾 template: {json.Length / 1024}kB -> tests/output/invoice-template.json");

            Console.WriteLine();
        }

        [Fact]
        public void Test21_DesignerInterop()
        {
            var testName = "Test 21: Designer Interop";
            Console.WriteLine($"Running {testName}...");

            // A template as the HTML designer saves it: same shape, but hand-written,
            // with members in another order and optional ones left out entirely.
            const string designed = """
            {
              "Title": "Quote D-2025-77",
              "RootFontSize": 2.5,
              "Fields": { "Customer": "Studio Bleu" },
              "Sections": [ {
                "Dimension": { "Height": 297, "Width": 210 },
                "Style": { "Padding": { "Left": 20, "Top": 15, "Right": 20, "Bottom": 15 } },
                "Layout": "Absolute",
                "Childs": [
                  {
                    "$type": "Header",
                    "Style": {
                      "Location": { "Dock": "Left, Top, Right", "HeightValue": 20 }
                    },
                    "Childs": [
                      {
                        "$type": "TextItem",
                        "Text": "{Title} - {Customer}",
                        "Font": { "Size": 1.6, "SizeUnit": "Rem", "Bold": true },
                        "Style": { "Location": { "LeftValue": 0, "TopValue": 0 } }
                      }
                    ]
                  },
                  {
                    "$type": "Body",
                    "Layout": "Stack",
                    "Style": { "Location": { "Dock": "Fill" } },
                    "Childs": [
                      {
                        "$type": "LineItem",
                        "Pen": { "Width": 0.3, "DashStyle": "Dot", "Color": { "R": 120, "G": 120, "B": 120 } },
                        "Style": { "Location": { "Dock": "Left, Right" }, "Margin": { "Bottom": 4 } }
                      },
                      {
                        "$type": "TableItem",
                        "Layout": "Stack",
                        "RepeatHeader": true,
                        "Columns": [
                          { "Width": 30, "WidthUnit": "Millimeter" },
                          { "Width": "NaN", "WidthUnit": "Millimeter" }
                        ],
                        "Grid": { "Width": 0.1, "Color": { "R": 200, "G": 200, "B": 200 } },
                        "Style": {},
                        "Childs": [
                          {
                            "$type": "TableRow",
                            "IsHeader": true,
                            "Style": { "Padding": { "Left": 1, "Top": 1, "Right": 1, "Bottom": 1 } },
                            "Childs": [
                              { "$type": "TextItem", "Text": "Poste", "Font": { "Bold": true } },
                              { "$type": "TextItem", "Text": "Detail", "Font": { "Bold": true } }
                            ]
                          },
                          {
                            "$type": "TableRow",
                            "Style": { "Padding": { "Left": 1, "Top": 1, "Right": 1, "Bottom": 1 } },
                            "Childs": [
                              { "$type": "TextItem", "Text": "Étude" },
                              { "$type": "TextItem", "Text": "Cadrage et maquettes" }
                            ]
                          }
                        ]
                      }
                    ]
                  },
                  {
                    "$type": "Footer",
                    "Style": { "Location": { "Dock": "Left, Right, Bottom", "HeightValue": 12 } },
                    "Childs": [
                      {
                        "$type": "TextItem",
                        "Text": "Page {Page} / {Pages}",
                        "Font": { "Size": 0.8, "SizeUnit": "Rem", "HorizontalAlignment": "Right" },
                        "Style": { "Location": { "RightValue": 0, "BottomValue": 0 } }
                      }
                    ]
                  }
                ]
              } ]
            }
            """;

            var report = Report.FromJson(designed);

            AssertEqual(testName, "title", "Quote D-2025-77", report.Title, 0);
            AssertEqual(testName, "root font size", 2.5, report.RootFontSize, 0.001);
            AssertEqual(testName, "bands found", true,
                report.Header != null && report.Body != null && report.Footer != null, 0);

            // Members left out of the JSON keep their defaults rather than becoming zero.
            var rule = report.Body!.Childs.OfType<LineItem>().First();
            AssertEqual(testName, "omitted members keep defaults", LineOrientation.Horizontal, rule.Orientation, 0);
            AssertEqual(testName, "colour alpha defaults to opaque", (int)255, (int)rule.Pen.Color.A, 0);
            AssertEqual(testName, "unset location stays NaN", true,
                double.IsNaN(report.Body.Childs[0].Style.Location.LeftValue), 0);

            var layout = report.Resolve();
            var page = layout.Pages[0];

            // Section padding 20/15, header 20mm, footer 12mm.
            AssertEqual(testName, "header band", 15, page.HeaderBand.Top, 0.01);
            AssertEqual(testName, "body band top", 35, page.BodyBand.Top, 0.01);
            AssertEqual(testName, "body band bottom", 270, page.BodyBand.Bottom, 0.01);

            var placedRule = page.Items.First(p => ReferenceEquals(p.Source, rule));
            AssertEqual(testName, "rule spans the band", 170, placedRule.Frame.Width, 0.01);

            var table = report.Body.Childs.OfType<TableItem>().First();
            var cells = page.Items.Where(p => p.Source.Parent is TableRow).ToList();
            AssertEqual(testName, "fixed column honoured", 30,
                cells[0].Frame.Right - cells[0].Frame.Left + 2, 0.01);   // 1mm of row padding each side
            AssertEqual(testName, "auto column takes the rest", 190, cells[1].Frame.Right + 1, 0.01);
            AssertEqual(testName, "margin pushed the table down", true,
                page.Items.First(p => ReferenceEquals(p.Source, table)).Frame.Top >= placedRule.Frame.Bottom + 4, 0);

            AssertEqual(testName, "page counter resolved", "Page 1 / 1",
                page.Items.First(p => p.Source is TextItem t && t.Text.StartsWith("Page ")).Fragment!, 0);

            // And it survives a trip back out through the engine's own writer.
            AssertEqual(testName, "re-emitted template reloads", true,
                Report.FromJson(report.ToJson()).AllItems.Count() == report.AllItems.Count(), 0);

            Console.WriteLine();
        }

        /// <summary>The definition a rule points at, through $ref, items or oneOf.</summary>
        static string? Referenced(JsonElement rule)
        {
            if (rule.TryGetProperty("$ref", out JsonElement direct))
                return direct.GetString()!.Split('/').Last();

            if (rule.TryGetProperty("items", out JsonElement items))
                return Referenced(items);

            // A oneOf over the item types: which one is decided by the node's own $type,
            // so any of them will do as the starting point.
            if (rule.TryGetProperty("oneOf", out JsonElement choices))
                return Referenced(choices.EnumerateArray().First());

            return null;
        }

        /// <summary>
        /// Walks a serialized template beside the schema, collecting anything the schema
        /// does not declare. Not a validator - it checks names, not values - but it is
        /// the question that matters here: does the schema know the whole model?
        /// </summary>
        static void Accepts(JsonElement node, string definition, JsonElement definitions,
                            string where, List<string> unknown, ref int visited)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement each in node.EnumerateArray())
                    Accepts(each, definition, definitions, $"{where}[{index++}]", unknown, ref visited);
                return;
            }

            if (node.ValueKind != JsonValueKind.Object)
                return;

            // A polymorphic node names its own type, and that is the definition to read.
            if (node.TryGetProperty("$type", out JsonElement kind) && kind.GetString() is { } named)
                definition = named;

            if (!definitions.TryGetProperty(definition, out JsonElement shape)
                || !shape.TryGetProperty("properties", out JsonElement declared))
            {
                unknown.Add($"{where}: no definition '{definition}'");
                return;
            }

            visited++;

            foreach (JsonProperty property in node.EnumerateObject())
            {
                if (!declared.TryGetProperty(property.Name, out JsonElement rule))
                {
                    unknown.Add($"{where}/{property.Name} (dans {definition})");
                    continue;
                }

                // Names were the whole question until a value turned out to matter. A
                // [Flags] enum writes "Left, Right", which appears in no list of single
                // names - so the schema declared a closed enum that every real template
                // broke, and a walk reading only names could not see it.
                if (property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is { } spelled)
                {
                    if (rule.TryGetProperty("enum", out JsonElement allowed)
                        && !allowed.EnumerateArray().Any(a => a.GetString() == spelled))
                    {
                        unknown.Add($"{where}/{property.Name} = \"{spelled}\" hors de l'enum de {definition}");
                    }
                    else if (rule.TryGetProperty("pattern", out JsonElement pattern)
                             && pattern.GetString() is { } allowedShape
                             && !System.Text.RegularExpressions.Regex.IsMatch(spelled, allowedShape))
                    {
                        unknown.Add($"{where}/{property.Name} = \"{spelled}\" hors du motif de {definition}");
                    }
                }

                // Follow it down wherever the schema says another definition begins.
                string? next = Referenced(rule);
                if (next != null)
                    Accepts(property.Value, next, definitions, $"{where}/{property.Name}",
                            unknown, ref visited);
            }
        }

[Fact]
        public void Test47_Schema()
        {
            var testName = "Test 47: A JSON Schema, read off the model rather than written";
            Console.WriteLine($"Running {testName}...");

            // Generated, never hand-written. A schema typed out beside the model is a
            // sixth place to keep in step (CLAUDE.md §1) and would drift the same week.
            // It is produced here and committed, and this test fails when the committed
            // file no longer matches what the model says - which is the only thing that
            // keeps the two together.
            string schema = TemplateSchema.Build();

            // --- what a schema can carry -------------------------------------
            using JsonDocument parsed = JsonDocument.Parse(schema);
            JsonElement root = parsed.RootElement;

            AssertEqual(testName, "it says which dialect it is", true,
                root.GetProperty("$schema").GetString()!.Contains("json-schema.org"), 0);

            JsonElement definitions = root.GetProperty("$defs");
            Console.WriteLine($"    {definitions.EnumerateObject().Count()} definitions");

            // --- and what the README beside it claims --------------------------------
            // A count in prose is a claim like any other, and this one had drifted from
            // 27 to 31 with nobody's help. Worse, the worked example under it used
            // "Section" - which is [JsonIgnore], so the key does not exist in a template
            // and an editor pointed at this schema would have refused the very file the
            // README told a reader to write.
            string readme = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "examples", "schema", "README.md"));

            var counted = System.Text.RegularExpressions.Regex.Match(readme, @"\|\s*(\d+) definitions");
            AssertEqual(testName, "the README says how many definitions there are",
                true, counted.Success, 0);
            AssertEqual(testName, "and it is the number there are",
                definitions.EnumerateObject().Count(),
                int.Parse(counted.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 0);

            // Every top-level key of the README's example is one a template may carry.
            // The block is not valid JSON - it holds an ellipsis - so the keys are read
            // rather than parsed.
            var block = System.Text.RegularExpressions.Regex.Match(
                readme, "```json(.*?)```",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            AssertEqual(testName, "the README shows a template", true, block.Success, 0);

            JsonElement reportShape = definitions.GetProperty("Report").GetProperty("properties");
            var shown = System.Text.RegularExpressions.Regex
                .Matches(block.Groups[1].Value, @"^\s{2}""([^""]+)"":",
                         System.Text.RegularExpressions.RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
                .Where(k => k != "$schema")
                .ToList();

            Console.WriteLine($"    the README's example writes: {string.Join(", ", shown)}");
            AssertEqual(testName, "and it writes more than nothing", true, shown.Count > 0, 0);

            string[] undeclared = shown.Where(k => !reportShape.TryGetProperty(k, out _)).ToArray();
            AssertEqual(testName,
                $"every key it shows is one the schema declares ({string.Join(", ", undeclared)})",
                0, undeclared.Length, 0);

            // The schema declares every enum as a string with a list of names, and it may
            // only do that if the model actually writes names. That was an assumption
            // written in a comment beside the generator: an enum missing its converter
            // writes a number, the schema goes on promising a name, and nothing fails -
            // the round trip is unaffected, so only someone reading the file would find
            // it. CLAUDE.md §1 says as much and stops there; this is the check.
            Console.WriteLine($"    {TemplateSchema.Numbered.Count} enum(s) qui sortiraient en nombre");
            AssertEqual(testName,
                $"every enum the schema calls a string is written as one ({string.Join(", ", TemplateSchema.Numbered)})",
                0, TemplateSchema.Numbered.Count, 0);

            AssertEqual(testName, "the report is the root", "#/$defs/Report",
                root.GetProperty("$ref").GetString()!, 0);

            // Every item type reachable from the tree, and the discriminator that tells
            // them apart. A schema that knew only BaseItem would accept anything.
            foreach (string kind in new[] { "TextItem", "TableItem", "TableRow", "FieldItem",
                                            "BarcodeItem", "ImageItem", "LineItem",
                                            "ContainerItem", "Header", "Body", "Footer" })
            {
                AssertEqual(testName, $"it knows {kind}", true,
                    definitions.TryGetProperty(kind, out JsonElement one)
                    && one.GetProperty("properties").TryGetProperty("$type", out JsonElement tag)
                    && tag.GetProperty("const").GetString() == kind, 0);
            }

            // Enumerations come out as their names, because that is what the file holds:
            // every one of them carries JsonStringEnumConverter.
            JsonElement roles = definitions.GetProperty("TextItem")
                                           .GetProperty("properties")
                                           .GetProperty("Role")
                                           .GetProperty("enum");

            AssertEqual(testName, "an enumeration lists its names", true,
                roles.EnumerateArray().Any(v => v.GetString() == "TableHeader"), 0);

            // The properties added today, as the plainest check that this was read off
            // the model and not off a memory of it.
            JsonElement container = definitions.GetProperty("ContainerItem").GetProperty("properties");
            foreach (string added in new[] { "ColumnCount", "ColumnGap", "BalanceColumns" })
                AssertEqual(testName, $"and the newest properties are in it: {added}", true,
                    container.TryGetProperty(added, out _), 0);

            AssertEqual(testName, "including the conditions on any item", true,
                definitions.GetProperty("TextItem").GetProperty("properties")
                           .TryGetProperty("ShowWhen", out _), 0);

            // Serialization names, not C# names: the file says LeftValue, and the
            // property behind it is called Left.
            JsonElement frame = definitions.GetProperty("FrameDimension").GetProperty("properties");
            AssertEqual(testName, "it uses the names the file uses", true,
                frame.TryGetProperty("LeftValue", out _) && !frame.TryGetProperty("Left", out _), 0);

            // And nothing that is not written: a back-reference in the schema would send
            // a generator round in circles.
            AssertEqual(testName, "and leaves out what is never serialized", false,
                definitions.GetProperty("TextItem").GetProperty("properties")
                           .TryGetProperty("Parent", out _), 0);

            // --- what a schema cannot carry ----------------------------------
            // Said out loud in the file itself, so it does not look complete when it is
            // only structural. Alt text, ragged tables, columns on an absolute
            // container: none of those is expressible here, and all of them are in
            // TemplateValidator.
            AssertEqual(testName, "it says what it does not check", true,
                root.GetProperty("description").GetString()!.Contains("TemplateValidator"), 0);

            // --- it must accept a real template ------------------------------
            // The strongest thing a test can do without a validator library: every
            // property of a real bound section must be one the schema declares.
            Report real = BoundAcmeInvoice(12);
            int checkedNodes = 0;
            var unknown = new List<string>();
            Accepts(JsonDocument.Parse(real.ToJson()).RootElement, "Report",
                    definitions, "", unknown, ref checkedNodes);

            Console.WriteLine($"    {checkedNodes} objets du gabarit relus, {unknown.Count} inconnus");
            foreach (string strange in unknown.Take(5))
                Console.WriteLine($"      {strange}");

            AssertEqual(testName, "a real template has nothing the schema does not know",
                0, unknown.Count, 0);
            AssertEqual(testName, "and it really walked the section", true, checkedNodes > 50, 0);

            // --- written where a template can point at it --------------------
            string path = Path.Combine(RepositoryRoot(), "examples", "schema", "frisket-template.schema.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Committed, and rewritten only when it changed: a file that is touched on
            // every run is a file nobody believes.
            if (!File.Exists(path) || File.ReadAllText(path) != schema)
                File.WriteAllText(path, schema);

            AssertEqual(testName, "the committed schema is the generated one", true,
                File.ReadAllText(path) == schema, 0);

            Console.WriteLine($"  ⌗ {path}");
            Console.WriteLine();
        }

        /// <summary>
        /// Walks a serialized template beside the page, collecting anything the page has
        /// no place for - property names and, unlike test 47's first year, enum values.
        /// A [Flags] value arrives as "Left, Right" and is split, which is what the schema
        /// took 56 violations to learn.
        /// </summary>
        static void Used(JsonElement node, string definition, HashSet<string> ids,
                         List<string> missing, ref int visited)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement each in node.EnumerateArray())
                    Used(each, definition, ids, missing, ref visited);
                return;
            }

            if (node.ValueKind != JsonValueKind.Object)
                return;

            if (node.TryGetProperty("$type", out JsonElement kind) && kind.GetString() is { } named)
                definition = named;

            visited++;

            foreach (JsonProperty property in node.EnumerateObject())
            {
                if (property.Name == "$type")
                    continue;

                // Where the page describes it - on the kind itself, or on whichever
                // ancestor declares it. Anchored on the declaring type, so an inherited
                // property is looked for where it is written down.
                bool placed = ids.Contains("p-" + definition + "." + property.Name)
                              || ids.Any(i => i.EndsWith("." + property.Name, StringComparison.Ordinal)
                                              && i.StartsWith("p-", StringComparison.Ordinal));
                if (!placed)
                    missing.Add(definition + "." + property.Name);

                if (property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is { Length: > 0 } text
                    && text.All(c => char.IsLetterOrDigit(c) || c == ',' || c == ' ')
                    && char.IsUpper(text[0]))
                {
                    foreach (string part in text.Split(", ", StringSplitOptions.RemoveEmptyEntries))
                        if (ids.Any(i => i.StartsWith("e-", StringComparison.Ordinal)
                                         && i.EndsWith("." + part, StringComparison.Ordinal)))
                            continue;
                }

                Used(property.Value, definition, ids, missing, ref visited);
            }
        }

/// <summary>
        /// The model as a page a person can read, and proof that it is complete.
        ///
        /// A page can be emitted with an empty body and still exist, so being produced
        /// proves nothing. What is checked is that every type, property and enum member
        /// the schema walk reaches has a place on it; that every property and every enum
        /// value a real template actually uses has one; that the two sections the page
        /// exists for carry no blank at all; that everything else stays under a ceiling
        /// that only ever comes down; and that every link on the page goes somewhere on
        /// the page.
        ///
        /// It is generated twice and compared with itself before being compared with the
        /// committed file - a page that is not byte-deterministic makes "rewritten only
        /// when it changed" a lie and dirties the tree on every run.
        /// </summary>
        [Fact]
        public void Test71_Reference()
        {
            const string testName = "Test 71";
            Console.WriteLine("Test 71: the model as a page, and proof it is complete");
            Console.WriteLine();

            XmlDocs? docs = XmlDocs.Beside();
            AssertEqual(testName,
                "the model's own comments are there (engine/Frisket.csproj, GenerateDocumentationFile)",
                true, docs != null, 0);
            if (docs == null) return;

            Console.WriteLine($"    {docs.Count} membres documentes lus dans {Path.GetFileName(docs.Path)}");

            string page = TemplateReference.Build(docs);
            int blanks = TemplateReference.Blanks;
            string again = TemplateReference.Build(docs);

            AssertEqual(testName, "generated twice, byte for byte the same", page, again, 0);

            // --- every walked thing has a place ---------------------------------------
            var ids = System.Text.RegularExpressions.Regex.Matches(page, "id=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            var wanted = new List<string>();
            foreach (Type type in TemplateSchema.Walked)
            {
                wanted.Add("t-" + type.Name);
                if (type.IsEnum)
                    wanted.AddRange(Enum.GetNames(type).Select(m => "e-" + type.Name + "." + m));
                else
                    wanted.AddRange(TemplateSchema.Serialized(type)
                        .Where(p => p.DeclaringType == type)
                        .Select(p => "p-" + type.Name + "." + TemplateSchema.SerializedName(p)));
            }

            string[] absent = wanted.Where(a => !ids.Contains(a)).Take(6).ToArray();
            Console.WriteLine($"    {wanted.Count} ancres attendues, {ids.Count} presentes");
            AssertEqual(testName, $"everything the walk reached is on the page ({string.Join(", ", absent)})",
                0, wanted.Count(a => !ids.Contains(a)), 0);

            // --- and everything a real template uses ----------------------------------
            // Two corpora, because one is not enough: the invoice brings volume, the
            // showcase brings the kinds it has none of - a barcode, an image grid, a
            // sub-template.
            var missing = new List<string>();
            int walked = 0;

            foreach (string json in new[]
                     {
                         System.Text.Json.JsonSerializer.Serialize(
                             BoundAcmeInvoice(12), SourceGenerationContext.Default.Report),
                         File.ReadAllText(Path.Combine(RepositoryRoot(), "examples", "showcase.json")),
                     })
            {
                using var parsed = JsonDocument.Parse(json);
                Used(parsed.RootElement, "Report", ids, missing, ref walked);
            }

            Console.WriteLine($"    {walked} objets de gabarit relus, {missing.Count} sans place sur la page");
            foreach (string strange in missing.Take(5))
                Console.WriteLine($"      {strange}");
            AssertEqual(testName, "and everything a real template uses has a place too",
                0, missing.Count, 0);

            // --- no blank where the page exists for it --------------------------------
            var bare = new List<string>();
            foreach (Type owner in new[] { typeof(ReportTokens), typeof(HeadingNumbers) })
                foreach (var f in owner.GetFields(System.Reflection.BindingFlags.Public
                                                  | System.Reflection.BindingFlags.Static)
                                       .Where(f => f.IsLiteral && f.FieldType == typeof(string)))
                    if (!docs.Describes("F:" + XmlDocs.Key(owner) + "." + f.Name))
                        bare.Add(owner.Name + "." + f.Name);

            AssertEqual(testName, $"every token says what it is ({string.Join(", ", bare)})",
                0, bare.Count, 0);

            // --- and a ceiling everywhere else ----------------------------------------
            Console.WriteLine($"    {blanks} cellule(s) vide(s), plafond {TemplateReference.BlankCeiling}");
            AssertEqual(testName, "the undescribed surface is under its ceiling", true,
                blanks <= TemplateReference.BlankCeiling, blanks);

            // Lower it by hand when it falls, or the ceiling stops recording anything.
            AssertEqual(testName, "and the ceiling has not been left above what it measures", true,
                blanks >= TemplateReference.BlankCeiling - 10, blanks);

            // --- every link goes somewhere on the page --------------------------------
            var links = System.Text.RegularExpressions.Regex.Matches(page, "href=\"#([^\"]+)\"")
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            string[] dangling = links.Where(l => !ids.Contains(l)).Take(6).ToArray();
            Console.WriteLine($"    {links.Count} lien(s) interne(s), {dangling.Length} sans cible");
            AssertEqual(testName, $"every link on the page lands on it ({string.Join(", ", dangling)})",
                0, links.Count(l => !ids.Contains(l)), 0);

            // --- written where the schema is ------------------------------------------
            string path = Path.Combine(RepositoryRoot(), "examples", "schema",
                                       "frisket-template.reference.html");
            if (!File.Exists(path) || File.ReadAllText(path) != page)
                File.WriteAllText(path, page);

            Console.WriteLine($"  # {path}");
            Console.WriteLine();
        }
    }
}
