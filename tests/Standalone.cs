namespace test
{
    public partial class EngineTests
    {
        /// <summary>
        /// One file, openable from a memory stick.
        ///
        /// The content editor is a thing that ships, and it ships as five files that have
        /// to sit beside one another. That is fine for a repository and wrong for
        /// everything else: a server needs a route for each of them (which cost this
        /// project a dead page for a week - CLAUDE.md section 6), a mail attachment has
        /// nowhere to put the other four, and "open this in a browser" stops being true
        /// the moment one of them is missing.
        ///
        /// So the parts stay the source and a single file is <b>generated</b> from them.
        /// Generated, and not written twice: two copies of an editor is section 1's whole
        /// subject, and the second one is always the one nobody updates.
        ///
        /// <para>The page decides what it is made of. Its links and its scripts are
        /// followed <b>in the order written</b> rather than from a list kept here - a
        /// generator with its own list is one rename away from building something the
        /// page no longer is.</para>
        ///
        /// <para>They are written to <c>release/</c> at the top of the repository, and
        /// not beside the parts. That is the whole distinction this directory carries:
        /// what is <b>built</b> lives under <c>bin/</c> and is not tracked, what is
        /// <b>shipped</b> lives here and is - the same line the schema and the documents
        /// under <c>tests/output/</c> already sit on. It also means somebody looking for
        /// what to download has one place to look rather than a directory of sources
        /// with two answers hidden in it.</para>
        ///
        /// <para>Both faces, and symmetrically, though only the editor is held to release
        /// quality today. Which of them is presented as a product is a matter for the
        /// README and for wherever it is published, and it should stay one: the day the
        /// designer joins it, nothing here has to move.</para>
        /// </summary>
        [Fact]
        public void Test93_EachFaceBuildsIntoOneFile()
        {
            const string testName = "Test 93";
            Console.WriteLine("Test 93: each face builds into one file");
            Console.WriteLine();

            string designer = Path.Combine(RepositoryRoot(), "apps", "designer");
            string release = Path.Combine(RepositoryRoot(), "release");
            Directory.CreateDirectory(release);

            foreach (string face in new[] { "frisket-editor.html", "frisket-designer.html" })
            {
                string source = File.ReadAllText(Path.Combine(designer, face));
                var parts = Parts(source).ToList();

                string built = Standalone(designer, source);
                // The suffix stays, in a directory that already says "release", because
                // the repository's own rule is that a file name should not shadow another
                // (section 6): frisket-editor.html in two directories is two things a
                // fuzzy file open cannot tell apart, and the one somebody downloads is
                // the one that had better say what it is.
                string into = face.Replace(".html", ".standalone.html");
                File.WriteAllText(Path.Combine(release, into), built);

                Console.WriteLine($"    {face,-24} {parts.Count} part(s) -> release/{into} "
                                + $"({built.Length / 1024} kB)");

                // Nothing left pointing outside, or it is not standalone - it is the same
                // page with most of its pieces glued on.
                var outside = System.Text.RegularExpressions.Regex
                    .Matches(built, @"(?:src|href)=""([^""]+)""")
                    .Select(m => m.Groups[1].Value)
                    .Where(u => !u.StartsWith("http", StringComparison.Ordinal)
                             && !u.StartsWith("data:", StringComparison.Ordinal)
                             && !u.StartsWith('#'))
                    .ToList();

                AssertEqual(testName, $"{face}: nothing is left to fetch",
                    "", string.Join(", ", outside), 0);

                // Every part, whole. A generator that inlined three of four files would
                // produce a page that opens and does nothing, which looks like a broken
                // editor rather than a broken build.
                var lost = parts
                    .Where(p => !built.Contains(Marker(File.ReadAllText(Path.Combine(designer, p))),
                                                StringComparison.Ordinal))
                    .ToList();

                AssertEqual(testName, $"{face}: every part came across",
                    "", string.Join(", ", lost), 0);

                // The trap this kind of build always has: a script inlined into a page
                // ends at the first "</script>" in it, wherever that is - inside a string,
                // inside a comment, inside a regular expression. The browser does not
                // parse JavaScript to find the end of a script element; it looks for that
                // sequence of characters. So it is broken up on the way in, and this is
                // what says the escaping happened.
                foreach (System.Text.RegularExpressions.Match block in
                         System.Text.RegularExpressions.Regex.Matches(
                             built, @"<script>([\s\S]*?)</script>"))
                {
                    AssertEqual(testName, $"{face}: no script ends before it should",
                        false, block.Groups[1].Value.Contains("</script", StringComparison.OrdinalIgnoreCase), 0);
                }

                AssertEqual(testName, $"{face}: and it says it was generated",
                    true, built.Contains("generated", StringComparison.OrdinalIgnoreCase), 0);
            }

            // --- and the escaping, given something to escape --------------------------
            // No part carries "</script>" today, so the assertion above is true of every
            // face for want of matter - taking the escaping out left it green. That is
            // section 10c's lesson about a sample outside the window of the fault, and
            // the answer is the same: build the window.
            //
            // Not a hypothetical shape either. Any script that writes markup, matches on
            // a tag, or explains this very problem in a comment will carry the sequence,
            // and the page it is folded into ends there - silently, because a browser
            // looks for those characters and does not read the JavaScript around them.
            string scratch = OutputDirectory("standalone");
            File.WriteAllText(Path.Combine(scratch, "bait.js"),
                "const closing = \"</script>\";   // the sequence, in a string\n");
            File.WriteAllText(Path.Combine(scratch, "bait.html"),
                "<!doctype html>\n<html><head></head><body>\n"
                + "<script src=\"bait.js\"></script>\n</body></html>\n");

            string baited = Standalone(scratch, File.ReadAllText(Path.Combine(scratch, "bait.html")));

            var kept = System.Text.RegularExpressions.Regex.Match(baited, @"<script>([\s\S]*?)</script>");
            Console.WriteLine($"    appat : le bloc fait {kept.Groups[1].Value.Length} octets, "
                            + $"{(kept.Groups[1].Value.Contains("closing") ? "la ligne y est" : "LA LIGNE MANQUE")}");

            // The block must run to the end of the part, not stop inside it.
            AssertEqual(testName, "a script carrying the closing sequence is not cut short",
                true, kept.Groups[1].Value.Contains("in a string", StringComparison.Ordinal), 0);

            AssertEqual(testName, "and the sequence itself left the page",
                false, kept.Groups[1].Value.Contains("</script", StringComparison.OrdinalIgnoreCase), 0);

            // And it is still the same string once a browser has read it: the escape is
            // JavaScript's, so "<\/script>" and "</script>" are one value. An escaping
            // that changed what the code says would be a different kind of broken.
            AssertEqual(testName, "and says the same thing after the escape", true,
                kept.Groups[1].Value.Contains("<\\/script>", StringComparison.Ordinal), 0);

            Console.WriteLine();
        }

        /// <summary>The stylesheets and scripts a page names, in the order it names them.</summary>
        private static IEnumerable<string> Parts(string page) =>
            System.Text.RegularExpressions.Regex
                .Matches(page, @"<link[^>]+href=""([^""]+\.css)""|<script src=""([^""]+\.js)"">")
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

        /// <summary>
        /// A part's first line and its last, which is enough to say it came across whole
        /// without asking a test to hold a copy of every file it checks.
        /// </summary>
        private static string Marker(string part)
        {
            string[] lines = part.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0].TrimEnd('\r') : part;
        }

        /// <summary>
        /// The page with everything it names folded into it.
        ///
        /// In place, so the order is the page's own: a stylesheet that came second still
        /// comes second, and a script that had to run after another still does. Rebuilt
        /// from a list rather than substituted, the order would be this method's opinion.
        /// </summary>
        private static string Standalone(string directory, string page)
        {
            const string banner =
                "<!-- Generated from the parts beside it, by test 93. Do not edit: what you\n"
              + "     change here is overwritten the next time the suite runs, and the page\n"
              + "     everybody else uses is the split one. -->\n";

            string built = System.Text.RegularExpressions.Regex.Replace(
                page, @"<link[^>]+href=""([^""]+\.css)""[^>]*>|<script src=""([^""]+\.js)""></script>",
                match =>
                {
                    bool style = match.Groups[1].Success;
                    string name = style ? match.Groups[1].Value : match.Groups[2].Value;
                    string body = File.ReadAllText(Path.Combine(directory, name));

                    return style
                        ? $"<style>\n/* {name} */\n{body}\n</style>"
                        // Broken up because a browser looks for the characters and not for
                        // the end of a statement: the sequence inside a string would end
                        // the element there, and everything after it would be page text.
                        : $"<script>\n/* {name} */\n{body.Replace("</script", "<\\/script")}\n</script>";
                });

            int head = built.IndexOf("<head>", StringComparison.Ordinal);
            return head < 0 ? banner + built
                 : built[..(head + "<head>".Length)] + "\n" + banner + built[(head + "<head>".Length)..];
        }
    }
}
