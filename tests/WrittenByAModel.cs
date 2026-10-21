using System.Text.Json.Nodes;

namespace test
{
    /// <summary>
    /// A template written the way a model writes one from the schema, and corrected the way a
    /// program corrects it: by following the pointers the checks hand back.
    ///
    /// The engine does not talk to a model - which one, with what key, is the host's - but it
    /// is what tells a model what went wrong, and that is only worth something if a program
    /// can act on it without a person reading the message. So this test is the loop: write,
    /// check, mend at the pointer, check again, compose.
    /// </summary>
    public partial class EngineTests
    {
        [Fact]
        public void Test97_AModelWritesATemplate()
        {
            const string testName = "Test 97";
            Console.WriteLine("Test 97: a template written by a model, mended by following the checks");
            Console.WriteLine();

            // What the host can fill in - the catalogue the content editor is handed as `fields`.
            const string catalogue = """
                [
                  { "path": "Destinataire.Nom", "label": "Nom du destinataire" },
                  "Devise",
                  { "path": "Cabinet.Logo", "type": "image" },
                  { "path": "Lignes", "type": "list", "label": "Lignes de la facture",
                    "fields": [ { "path": "Ref" }, { "path": "Montant", "type": "currency" } ] }
                ]
                """;

            // The mistakes a model makes, one of each: a property misspelt and another in the
            // wrong case (both ignored by the reader), no page size, no language, a picture
            // with nothing to say for it, a field name off by a letter, and a line naming a
            // field its list does not have. The header's {Devise} is at the root, which is
            // where a header - printed once - reads.
            const string written = """
                {
                  "Title": "Facture",
                  "Sections": [{
                    "Childs": [{ "$type": "Body", "Layout": "Stack", "Childs": [
                      { "$type": "TextItem", "Txt": "Facture n° 2026-0417" },
                      { "$type": "TextItem", "text": "Page {Page} sur {Pages}" },
                      { "$type": "TextItem", "Text": "À l'attention de {Destinataire.Nmo}" },
                      { "$type": "ImageItem", "SourceField": "Cabinet.Logo" },
                      { "$type": "TableItem", "RepeatOver": "Lignes", "Columns": [{}, {}], "Childs": [
                        { "$type": "TableRow", "IsHeader": true, "Childs": [
                          { "$type": "TextItem", "Text": "Référence" },
                          { "$type": "TextItem", "Text": "Montant ({Devise})" } ] },
                        { "$type": "TableRow", "Childs": [
                          { "$type": "TextItem", "Text": "{Ref}" },
                          { "$type": "TextItem", "Text": "{Prix} {Devise}" } ] } ] }
                    ] }]
                  }]
                }
                """;

            IReadOnlyList<TemplateIssue> Checked(string json)
            {
                var all = new List<TemplateIssue>(TemplateJson.Check(json));
                if (all.Any(i => i.Rule == "read.invalid")) return all;
                Report report = Report.FromJson(json);
                all.AddRange(TemplateValidator.Check(report));
                all.AddRange(TemplateFields.Check(report, catalogue));
                return all;
            }

            IReadOnlyList<TemplateIssue> first = Checked(written);

            // Left where somebody wiring a model can read them: what was written, what the
            // host can fill in, and what the checks said - the shape a program reads.
            string dir = OutputDirectory("model");
            // In LF whatever the checkout: a raw string takes the line endings of its source file
            // as git wrote it, and a sample that follows the checkout differs on every runner
            // (CLAUDE.md 6).
            File.WriteAllText(Path.Combine(dir, "written.template.json"), written.ReplaceLineEndings("\n"));
            File.WriteAllText(Path.Combine(dir, "fields.json"), catalogue.ReplaceLineEndings("\n"));
            File.WriteAllText(Path.Combine(dir, "findings.json"), TemplateIssue.ToJson(first));

            foreach (TemplateIssue issue in first)
                Console.WriteLine($"    {issue.Level,-7} {issue.Rule,-22} {issue.Pointer}");

            TemplateIssue? Find(string rule, string pointer)
                => first.FirstOrDefault(i => i.Rule == rule && i.Pointer == pointer);

            const string body = "/Sections/0/Childs/0/Childs";
            AssertEqual(testName, "a misspelt property, at its place, with the name meant", true,
                Find("read.unknownProperty", body + "/0/Txt")?.Message.Contains("\"Text\"") == true, 0);
            AssertEqual(testName, "one in the wrong case too - the reader is case-sensitive", true,
                Find("read.unknownProperty", body + "/1/text")?.Message.Contains("\"Text\"") == true, 0);
            AssertEqual(testName, "no page size, on the section", true, Find("sound.noPageSize", "/Sections/0") != null, 0);
            AssertEqual(testName, "no language, on the file", true, Find("ua.language", "/Language") != null, 0);
            AssertEqual(testName, "a picture with nothing to say for it", true, Find("ua.altText", body + "/3") != null, 0);
            AssertEqual(testName, "a field off by a letter, with the one meant", true,
                Find("fields.unknown", body + "/2/Text")?.Message.Contains("\"Destinataire.Nom\"") == true, 0);
            AssertEqual(testName, "a field a list entry does not hold, said to be in that list", true,
                Find("fields.unknown", body + "/4/Childs/1/Childs/1/Text")?.Message.Contains("Lignes de la facture") == true, 0);
            AssertEqual(testName, "and nothing said of what the data and the engine do answer",
                "", string.Join(", ", first.Where(i => i.Rule == "fields.unknown")
                                           .Select(i => i.Pointer)
                                           .Where(p => p != body + "/2/Text" && p != body + "/4/Childs/1/Childs/1/Text")), 0);

            // --- mended the way a program mends it: at the pointer, by the name suggested ----
            JsonNode tree = JsonNode.Parse(written)!;
            static JsonNode At(JsonNode root, IEnumerable<string> steps)
            {
                JsonNode at = root;
                foreach (string step in steps)
                    at = at is JsonArray list ? list[int.Parse(step)]! : at[step]!;
                return at;
            }
            static (JsonObject Owner, string Name) Parent(JsonNode root, string pointer)
            {
                string[] steps = pointer.Split('/')[1..];
                return ((JsonObject)At(root, steps[..^1]), steps[^1]);
            }
            static string? Meant(string message)
                => System.Text.RegularExpressions.Regex.Match(message, "Did you mean \"([^\"]+)\"") is { Success: true } m
                    ? m.Groups[1].Value : null;

            foreach (TemplateIssue issue in first)
            {
                switch (issue.Rule)
                {
                    case "read.unknownProperty":
                        var (owner, name) = Parent(tree, issue.Pointer!);
                        JsonNode? value = owner[name];
                        owner.Remove(name);
                        owner[Meant(issue.Message)!] = value;
                        break;
                    case "sound.noPageSize":
                        ((JsonObject)tree["Sections"]![0]!)["Dimension"] = new JsonObject { ["Width"] = 210, ["Height"] = 297 };
                        break;
                    case "ua.language":
                        tree["Language"] = "fr-FR";
                        break;
                    case "ua.altText":
                        ((JsonObject)At(tree, issue.Pointer!.Split('/')[1..]))["AltText"] = "Logo du cabinet";
                        break;
                    case "fields.unknown":
                        var (item, property) = Parent(tree, issue.Pointer!);
                        string text = item[property]!.GetValue<string>();
                        string wrong = System.Text.RegularExpressions.Regex.Match(issue.Message, "^\"([^\"]+)\"").Groups[1].Value;
                        item[property] = text.Replace("{" + wrong + "}", "{" + (Meant(issue.Message) ?? "Montant") + "}");
                        break;
                }
            }

            string mended = tree.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            File.WriteAllText(Path.Combine(dir, "mended.template.json"), mended.ReplaceLineEndings("\n"));
            IReadOnlyList<TemplateIssue> again = Checked(mended);
            AssertEqual(testName, "mended at the pointers, nothing is left to say",
                "", string.Join(", ", again.Select(i => i.Rule + " " + i.Pointer)), 0);

            // --- and the document it now makes ---------------------------------------------
            string data = new JsonObject
            {
                ["Destinataire"] = new JsonObject { ["Nom"] = "Maître Dupont" },
                ["Devise"] = "EUR",
                ["Lignes"] = new JsonArray(new JsonObject { ["Ref"] = "A-1", ["Montant"] = "1 200,00" }),
            }.ToJsonString();
            Report bound = ReportBinder.Bind(Report.FromJson(mended), data);
            LayoutResult layout = bound.Resolve(new FontTextMeasurer(new FontLibrary()));
            string printed = string.Join(" | ", layout.Pages.SelectMany(p => p.Items)
                .Select(i => i.Fragment ?? (i.Source as TextItem)?.Text ?? "").Where(t => t.Length > 0));
            Console.WriteLine($"    {printed}");
            AssertEqual(testName, "the words that were ignored are printed now", true,
                printed.Contains("Facture n° 2026-0417") && printed.Contains("Page 1 sur 1"), 0);
            AssertEqual(testName, "and the fields filled", true,
                printed.Contains("Maître Dupont") && printed.Contains("1 200,00 EUR") && printed.Contains("Montant (EUR)"), 0);

            // A header row is printed once, outside the list, so an entry's field named there
            // has nothing to answer it - while the same name one row down is answered per entry.
            Report headed = Report.FromJson("""
                { "Sections": [{ "Dimension": { "Width": 210, "Height": 297 },
                  "Childs": [{ "$type": "Body", "Childs": [
                    { "$type": "TableItem", "RepeatOver": "Lignes", "Columns": [{}], "Childs": [
                      { "$type": "TableRow", "IsHeader": true, "Childs": [{ "$type": "TextItem", "Text": "{Ref}" }] },
                      { "$type": "TableRow", "Childs": [{ "$type": "TextItem", "Text": "{Ref}" }] } ] } ] }] }] }
                """);
            AssertEqual(testName, "an entry's field named in the header is flagged there, and only there",
                "/Sections/0/Childs/0/Childs/0/Childs/0/Childs/0/Text",
                string.Join(", ", TemplateFields.Check(headed, catalogue).Select(i => i.Pointer)), 0);

            // --- and what the reader refuses outright, said where -----------------------------
            const string unknownKind = """
                { "Sections": [{ "Dimension": { "Width": 210, "Height": 297 },
                  "Childs": [{ "$type": "Body", "Childs": [{ "$type": "Paragraph", "Text": "x" }] }] }] }
                """;
            TemplateIssue? refused = TemplateJson.Check(unknownKind).FirstOrDefault(i => i.Rule == "read.invalid");
            Console.WriteLine($"    refused: {refused?.Pointer} {refused?.Message}");
            AssertEqual(testName, "a kind nobody knows is refused, and the refusal says where", true,
                refused?.Pointer?.StartsWith("/Sections/0/Childs/0/Childs/0") == true, 0);

            AssertEqual(testName, "and a text that is not JSON at all is refused, not thrown", true,
                TemplateJson.Check("{ \"Title\": ").Any(i => i.Rule == "read.invalid"), 0);
            Console.WriteLine();
        }

        /// <summary>
        /// `frisket check --json`, started as a program starts it and read as a program reads
        /// it: bytes off standard output, parsed as JSON.
        ///
        /// Its first version wrote through Console, which on Windows goes by the console's code
        /// page - redirected or not - so the "›" of every place came out as ">", and an accented
        /// list name would have come out mangled: harmless on a screen, wrong in what a model is
        /// handed back. Nothing in the suite started the CLI, so nothing could have seen it.
        /// </summary>
        [SkippableFact]
        public void Test98_CheckAnswersAProgram()
        {
            const string testName = "Test 98";
            Console.WriteLine("Test 98: frisket check --json, read as a program reads it");

            string configuration = AppContext.BaseDirectory.Contains(
                Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar) ? "Debug" : "Release";
            string cli = Path.Combine(RepositoryRoot(), "apps", "cli", "bin", configuration, "net10.0", "Frisket.Cli.dll");
            Skip.If(!File.Exists(cli), $"the CLI is not built at {cli}.");

            string dir = Path.Combine(OutputDirectory("model"), "cli");
            Directory.CreateDirectory(dir);
            string template = Path.Combine(dir, "template.json");
            string fields = Path.Combine(dir, "fields.json");
            File.WriteAllText(template, """
                { "Title": "Relevé", "Language": "fr-FR",
                  "Sections": [{ "Childs": [{ "$type": "Body", "Layout": "Stack", "Childs": [
                    { "$type": "TableItem", "RepeatOver": "Désignations", "Columns": [{}], "Childs": [
                      { "$type": "TableRow", "IsHeader": true, "Childs": [{ "$type": "TextItem", "Text": "Libellé" }] },
                      { "$type": "TableRow", "Childs": [{ "$type": "TextItem", "Txt": "{Libelle}", "Text": "{Libellé}" }] } ] } ] }] }] }
                """);
            File.WriteAllText(fields, """
                [ { "path": "Désignations", "type": "list", "label": "Désignations du relevé",
                    "fields": [ { "path": "Libelle" } ] } ]
                """);

            var run = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
                {
                    ArgumentList = { cli, "check", "-t", template, "--fields", fields, "--json" },
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };
            run.Start();
            using var bytes = new MemoryStream();
            run.StandardOutput.BaseStream.CopyTo(bytes);
            run.WaitForExit();
            string said = System.Text.Encoding.UTF8.GetString(bytes.ToArray());
            Console.WriteLine(said);

            JsonArray findings = JsonNode.Parse(said)!.AsArray();
            JsonNode? Of(string rule) => findings.FirstOrDefault(f => (string?)f!["rule"] == rule);

            AssertEqual(testName, "what it writes is JSON, one object per finding", true, findings.Count > 0, 0);
            AssertEqual(testName, "a place is written with its own separator, not one the console chose",
                "TextItem \u203A Txt", (string?)Of("read.unknownProperty")?["where"] ?? "", 0);
            AssertEqual(testName, "an accented name arrives as it was written", true,
                ((string?)Of("fields.unknown")?["message"])?.Contains("Désignations du relevé") == true, 0);
            AssertEqual(testName, "with the pointer a program acts on", "/Sections/0/Childs/0/Childs/0/Childs/1/Childs/0/Text",
                (string?)Of("fields.unknown")?["pointer"] ?? "", 0);
            AssertEqual(testName, "and an error is still an exit code a build reads", 3, run.ExitCode, 0);
            Console.WriteLine();
        }
    }
}
