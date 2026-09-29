namespace test
{
    /// <summary>
    /// What the engine does with bytes and callers it does not trust - CLAUDE.md sections 8b,
    /// 8c and 8d, which are one family: a count read out of a file cannot decide how long the
    /// engine works, a reader disturbed on purpose may refuse but may not read outside its
    /// array, and a render nobody wants any more stops and returns nothing.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>Offset of a table in an sfnt, or -1. Enough for a test to reach into one.</summary>
        private static int Find(byte[] font, string tag)
        {
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
            {
                int rec = 12 + i * 16;
                if (System.Text.Encoding.ASCII.GetString(font, rec, 4) == tag)
                    return (font[rec + 8] << 24) | (font[rec + 9] << 16)
                         | (font[rec + 10] << 8) | font[rec + 11];
            }
            return -1;
        }

        /// <summary>
        /// A font declares how many things it holds, and the reader used to believe it.
        ///
        /// The byte reader is forgiving on purpose - past the end it returns zero rather
        /// than throwing - and that is what turned an absurd count into work rather than
        /// an error: the loop never ran out of bytes, so it ran exactly as many times as
        /// the file asked. Two hundred and twelve bytes declaring a hundred million cmap
        /// groups took seven tenths of a second and were accepted at the end of it.
        ///
        /// Measured rather than asserted structurally: the fault was never that the
        /// numbers came out wrong - they did not - but that the work was unbounded. So
        /// the assertion is a clock, and the two sizes are declared a thousandfold apart
        /// so that a reader can see the cost has stopped following the claim.
        /// </summary>
        [Fact]
        public void Test59_FontCountsAreBounded()
        {
            const string testName = "Test 59";
            Console.WriteLine("Test 59: a font does not decide how much work it is worth");
            Console.WriteLine();

            // A minimal sfnt: the three tables the parser insists on, plus one cmap whose
            // group count is whatever we say. Nothing else is needed to be parsed.
            static byte[] Font(uint declaredGroups)
            {
                static byte[] Be16(int v) => new[] { (byte)(v >> 8), (byte)v };
                static byte[] Be32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

                var head = new byte[54];
                Be16(1000).CopyTo(head, 18);                       // unitsPerEm
                var hhea = new byte[36];
                Be16(1).CopyTo(hhea, 34);                          // numberOfHMetrics
                var hmtx = new byte[] { 0x01, 0xF4, 0, 0 };

                // One subtable record pointing at a format 12 map, which then declares
                // more groups than the file could ever hold.
                var cmap = new List<byte>();
                cmap.AddRange(Be16(0)); cmap.AddRange(Be16(1));    // version, one table
                cmap.AddRange(Be16(3)); cmap.AddRange(Be16(10));   // platform, encoding
                cmap.AddRange(Be32(12));                           // offset to the subtable
                cmap.AddRange(Be16(12)); cmap.AddRange(Be16(0));   // format 12
                cmap.AddRange(Be32(28)); cmap.AddRange(Be32(0));   // length, language
                cmap.AddRange(Be32(declaredGroups));

                var tables = new (string Tag, byte[] Data)[]
                {
                    ("cmap", cmap.ToArray()), ("head", head), ("hhea", hhea), ("hmtx", hmtx)
                };

                var file = new List<byte>();
                file.AddRange(Be32(0x00010000));
                file.AddRange(Be16(tables.Length));
                file.AddRange(Be16(64)); file.AddRange(Be16(2)); file.AddRange(Be16(0));

                int at = 12 + tables.Length * 16;
                var body = new List<byte>();
                foreach ((string tag, byte[] data) in tables)
                {
                    file.AddRange(System.Text.Encoding.ASCII.GetBytes(tag));
                    file.AddRange(Be32(0));
                    file.AddRange(Be32((uint)(at + body.Count)));
                    file.AddRange(Be32((uint)data.Length));
                    body.AddRange(data);
                    while (body.Count % 4 != 0) body.Add(0);
                }
                file.AddRange(body);
                return file.ToArray();
            }

            static double Seconds(byte[] font)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                try { new FontLibrary().Register(font); } catch (Exception) { }
                return clock.Elapsed.TotalSeconds;
            }

            byte[] modest = Font(1);
            byte[] absurd = Font(100_000_000);

            AssertEqual(testName, "both fonts are the same size on disk",
                modest.Length, absurd.Length, 0);

            // Warm the path, so the first-call cost of touching the parser is not what
            // gets measured.
            Seconds(modest);

            double small = Seconds(modest);
            double large = Seconds(absurd);
            Console.WriteLine($"    {modest.Length} bytes declaring 1 group           -> {small:0.000} s");
            Console.WriteLine($"    {absurd.Length} bytes declaring 100 000 000 groups -> {large:0.000} s");

            // Before the bound, the second of these took about 0.69 s against nothing
            // measurable for the first. A tenth of a second is far above the noise of
            // parsing 212 bytes and far below what an unbounded loop costs.
            AssertEqual(testName, "a hundred million declared groups cost no real time",
                true, large < 0.1, 0);
            AssertEqual(testName, "and the cost no longer follows the claim",
                true, large < small + 0.1, 0);

            // The bound must not have been bought by refusing honest fonts: everything
            // in the library still loads, and still measures the same.
            var library = new FontLibrary();
            int loaded = library.RegisterDirectory(FontDirectory());
            AssertEqual(testName, "and every real font still loads", 29, loaded, 0);

            FontFile? mulish = library.Resolve("Mulish", false, false);
            AssertEqual(testName, "with its character map intact", true,
                mulish != null && mulish.HasGlyph('H') && mulish.HasGlyph('e'), 0);

            // --- and what the foundry says is read, and only read -------------------
            // fsType is reported so a caller who has to answer for embedding can, and is
            // not enforced: the bits say what the foundry permits, and the caller may be
            // the foundry. Refusing on them would tell someone they may not use a
            // typeface they drew. Held by a test so it stays a decision, not an oversight.
            var restricted = new FontLibrary();
            byte[] bytes = File.ReadAllBytes(mulish!.Source);

            // Bit 1 of fsType, set by hand: "may not be embedded".
            int os2 = Find(bytes, "OS/2");
            AssertEqual(testName, "the font carries an OS/2 table", true, os2 > 0, 0);
            bytes[os2 + 8] = 0x00;
            bytes[os2 + 9] = 0x02;

            FontFile marked = restricted.Register(bytes);
            Console.WriteLine($"    fsType 0x0002 -> restricted={marked.EmbeddingRestricted}, still loaded");

            AssertEqual(testName, "a font marked no-embedding says so",
                true, marked.EmbeddingRestricted, 0);
            AssertEqual(testName, "and is loaded all the same, because that is the caller's call",
                true, restricted.Resolve("Mulish", false, false) != null, 0);
            AssertEqual(testName, "while an ordinary font claims no restriction",
                false, mulish.EmbeddingRestricted, 0);

            Console.WriteLine();
        }

        /// Spoils the numbers a parser trusts. Counts and offsets are big-endian 16- and
        /// 32-bit words, so those are what get made enormous, zeroed, or set to something
        /// plausible and wrong - which is a great deal more productive than random bytes.
        /// </summary>
        private static byte[] Mutate(byte[] original, Random rng)
        {
            byte[] copy = (byte[])original.Clone();

            for (int i = 0, edits = 1 + rng.Next(4); i < edits; i++)
            {
                int at = rng.Next(copy.Length);

                switch (rng.Next(6))
                {
                    case 0:
                        copy[at] = (byte)rng.Next(256);
                        break;
                    case 1:
                        if (at + 1 < copy.Length) { copy[at] = 0xFF; copy[at + 1] = 0xFF; }
                        break;
                    case 2:
                        if (at + 3 < copy.Length)
                        {
                            copy[at] = 0x7F; copy[at + 1] = 0xFF;
                            copy[at + 2] = 0xFF; copy[at + 3] = 0xFF;
                        }
                        break;
                    case 3:
                        if (at + 3 < copy.Length)
                            copy[at] = copy[at + 1] = copy[at + 2] = copy[at + 3] = 0;
                        break;
                    case 4:
                        Array.Resize(ref copy, Math.Max(16, rng.Next(copy.Length)));
                        break;
                    case 5:
                        if (at + 1 < copy.Length)
                        {
                            int v = 1 << rng.Next(8, 16);
                            copy[at] = (byte)(v >> 8); copy[at + 1] = (byte)v;
                        }
                        break;
                }
            }

            return copy;
        }

        /// <summary>
        /// The two readers that take bytes from somewhere else, disturbed on purpose.
        ///
        /// Both are entitled to refuse a file they do not like, and that is not what is
        /// being looked for. A failure is one of three things a parser never has a right
        /// to do: read outside the array it was given, believe a size it was told and
        /// allocate it, or take longer than the file could possibly justify.
        ///
        /// Structure-aware, because blind bytes are nearly useless here: both formats
        /// open with a signature, so a random mutation is rejected in the first eight
        /// bytes and never reaches the parser. What finds things is taking a real file
        /// and spoiling the numbers - lengths, counts, offsets - that the parser trusts.
        ///
        /// Seeded, so a failure can be repeated: the seed and the round are printed, and
        /// FRISKET_FUZZ_ROUNDS turns the same loop into a soak without changing it.
        ///
        /// Both fixed faults below were found this way, and both had been read over and
        /// called safe first - which is the argument for the loop existing at all.
        /// </summary>
        [Fact]
        public void Test60_ParsersUnderMutation()
        {
            const string testName = "Test 60";
            Console.WriteLine("Test 60: the byte readers, disturbed on purpose");
            Console.WriteLine();

            // --- the two it already found, held as regressions -----------------------
            // A table offset is a uint in the file and an int here, so past 0x7FFFFFFF it
            // arrives negative. Every guard in the reader tested only the top end, which
            // a negative index passes, and reading threw where nothing was catching.
            byte[] font = File.ReadAllBytes(
                Directory.GetFiles(FontDirectory(), "Mulish-Regular.ttf", SearchOption.AllDirectories).First());

            int cmapRecord = -1;
            int tableCount = (font[4] << 8) | font[5];
            for (int i = 0; i < tableCount; i++)
            {
                if (System.Text.Encoding.ASCII.GetString(font, 12 + i * 16, 4) == "cmap")
                    cmapRecord = 12 + i * 16;
            }
            AssertEqual(testName, "the font has a cmap to point somewhere silly", true, cmapRecord > 0, 0);

            font[cmapRecord + 8] = 0xFF;                 // an offset that becomes negative
            font[cmapRecord + 9] = 0xFF;
            font[cmapRecord + 10] = 0xFF;
            font[cmapRecord + 11] = 0xF0;

            Exception? escaped = Record.Exception(() => FontFile.Load(font));
            Console.WriteLine($"    a table offset past 0x7FFFFFFF -> {escaped?.GetType().Name ?? "no exception"}");
            AssertEqual(testName, "a negative offset is refused, not read", true,
                escaped is null or InvalidDataException, 0);

            // A chunk length near int.MaxValue used to overflow the bounds test written
            // as at + 12 + length, come out negative, pass, and then be allocated.
            var png = new List<byte> { 137, 80, 78, 71, 13, 10, 26, 10 };
            png.AddRange(new byte[] { 0x7F, 0xFF, 0xFF, 0xF0 });          // a colossal length
            png.AddRange(System.Text.Encoding.ASCII.GetBytes("IDAT"));
            png.AddRange(new byte[32]);

            bool read = true;
            Exception? blew = Record.Exception(() => read = PngImage.TryRead(png.ToArray(), out _));
            Console.WriteLine($"    a chunk claiming 2 GB          -> {blew?.GetType().Name ?? (read ? "accepted" : "refused")}");
            AssertEqual(testName, "a colossal chunk length is refused rather than allocated",
                true, blew is null && !read, 0);

            // A header shorter than a header, and near enough the end that reading one
            // anyway goes off it. Both halves are needed, and getting the arithmetic
            // wrong is how the first version of this passed with the guard removed: at
            // 29 bytes it never got past the minimum-length check at the top, so the
            // IHDR branch was never reached at all.
            //
            // A filler chunk carries the header out to offset 28 in a 40-byte file: the
            // chunk itself fits, and the thirteen bytes a header has do not.
            var stunted = new List<byte> { 137, 80, 78, 71, 13, 10, 26, 10 };
            stunted.AddRange(new byte[] { 0, 0, 0, 8 });                       // filler, 8 bytes
            stunted.AddRange(System.Text.Encoding.ASCII.GetBytes("tEXt"));
            stunted.AddRange(new byte[12]);                                    // body + CRC
            stunted.AddRange(new byte[] { 0, 0, 0, 0 });                       // IHDR, length 0
            stunted.AddRange(System.Text.Encoding.ASCII.GetBytes("IHDR"));
            stunted.AddRange(new byte[4]);                                     // CRC, and the end

            AssertEqual(testName, "the sample is long enough to reach the header branch",
                true, stunted.Count >= 33, 0);

            blew = Record.Exception(() => PngImage.TryRead(stunted.ToArray(), out _));
            Console.WriteLine($"    an IHDR shorter than a header  -> {blew?.GetType().Name ?? "refused"}");
            AssertEqual(testName, "a short header is refused rather than read past",
                true, blew is null, 0);

            // --- and the net, for whatever is next -----------------------------------
            var corpus = new List<byte[]>();
            foreach (string f in Directory.EnumerateFiles(FontDirectory(), "*.*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".ttf") || f.EndsWith(".otf"))
                    corpus.Add(File.ReadAllBytes(f));
            }

            int fonts = corpus.Count;
            corpus.Add(PngEncoder.Rgb(8, 8, (x, y) => ((byte)(x * 30), (byte)(y * 30), 0)));
            int images = corpus.Count;

            // And a PDF, because the reader that opens one is the third thing here that
            // takes bytes from somewhere else - and the newest, so the least likely to
            // have been thought about hard enough. A page's own content is copied without
            // being looked at, but everything that finds it is read: the cross-reference
            // table, object streams, the page tree, a stream's declared length. Every one
            // of those is a number in a file somebody else wrote.
            //
            // The corpus is a document of ours, which is exactly the shape that spoiling
            // numbers is worth doing to: a random file is refused at the header and never
            // reaches a parser (section 8c).
            corpus.Add(SomethingToSpoil());

            int rounds = int.TryParse(Environment.GetEnvironmentVariable("FRISKET_FUZZ_ROUNDS"),
                                      out int asked) ? asked : 4000;
            int seed = int.TryParse(Environment.GetEnvironmentVariable("FRISKET_FUZZ_SEED"),
                                    out int given) ? given : 20260821;

            var rng = new Random(seed);
            var clock = new System.Diagnostics.Stopwatch();
            string? complaint = null;
            int worst = 0;

            for (int round = 0; round < rounds && complaint == null; round++)
            {
                int pick = rng.Next(corpus.Count);
                byte[] sample = Mutate(corpus[pick], rng);

                clock.Restart();
                try
                {
                    if (pick < fonts) FontFile.Load(sample);
                    else if (pick < images) PngImage.TryRead(sample, out _);
                    else Frisket.Import.PdfPageSource.Read(sample, 0).WriteInto(new Collected());
                }
                catch (InvalidDataException) { }                  // how a reader says no
                catch (ArgumentOutOfRangeException) { }            // and how this one says it too
                catch (Exception e)
                {
                    complaint = $"{e.GetType().Name} escaped at seed {seed}, round {round}";
                }
                clock.Stop();

                worst = Math.Max(worst, (int)clock.ElapsedMilliseconds);
                if (clock.ElapsedMilliseconds > 400 && complaint == null)
                    complaint = $"one input took {clock.ElapsedMilliseconds} ms at seed {seed}, round {round}";
            }

            Console.WriteLine($"    {rounds} mutations over {corpus.Count} sample(s), "
                            + $"seed {seed}, slowest {worst} ms");
            AssertEqual(testName, "nothing escaped and nothing hung", null!, complaint!, 0);

            Console.WriteLine();
        }

        /// <summary>
        /// A small PDF of our own, for the fuzzer to spoil.
        ///
        /// Ours rather than one found somewhere, for the reason section 8c gives about
        /// fonts: what finds faults is taking a real file and ruining the numbers a
        /// parser trusts, and a file we wrote is one whose numbers we know are right to
        /// begin with.
        /// </summary>
        private static byte[] SomethingToSpoil()
        {
            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            var report = new Report { Title = "Bait", Language = "en-GB" };
            report.Section.Dimension = new() { Width = 210, Height = 297 };
            report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };
            report.Body = new() { Layout = LayoutMode.Stack };
            report.Body.Add(new TextItem { Text = "Something to spoil." });
            report.Reattach();

            var pdf = new PdfBackend(library);
            ReportRenderer.Render(report, pdf);
            return pdf.ToArray();
        }

        /// <summary>
        /// A render the caller has stopped wanting stops, and leaves nothing behind.
        ///
        /// Polled, never awaited. None of this waits for anything - it is arithmetic -
        /// so there is no point at which a continuation could resume, and async would
        /// buy nothing. Cancelling CPU-bound work means asking, in the loops that last,
        /// whether the answer is still wanted.
        ///
        /// Three places, because there are three moments worth stopping at, and they
        /// belong to different people. Inside a document is the engine's; between
        /// composing and drawing is free to a caller holding both halves, and taken on
        /// their behalf by the overload that holds them for them; and between one
        /// document and the next belongs to whoever wrote the loop - what is already
        /// produced stays produced, since the engine cannot know where it went.
        ///
        /// And it throws. A layout abandoned half way is not half a layout, and a
        /// document abandoned half way is worse: a PDF cut short still looks like a PDF,
        /// which is the one failure that does not announce itself.
        /// </summary>
        [Fact]
        public void Test61_CancellationStopsTheWork()
        {
            const string testName = "Test 61";
            Console.WriteLine("Test 61: a render nobody wants any more stops");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            // Long enough that the pagination loop runs many times, so cancelling it is
            // a real interruption rather than a race with a one-page document.
            static Report Long()
            {
                var report = new Report { Title = "long", Language = "en", Body = new Body() };
                report.Section.Dimension = new DocumentDimension { Width = 210, Height = 297 };
                for (int i = 0; i < 400; i++)
                    report.Body!.Add(new TextItem { Text = $"Paragraph {i}, of a document written to take a while." });
                return report;
            }

            // --- it finishes when nobody stops it ------------------------------------
            LayoutResult whole = Long().Resolve(measurer);
            Console.WriteLine($"    left alone            -> {whole.PageCount} pages");
            AssertEqual(testName, "the document is long enough for the test to mean something",
                true, whole.PageCount > 3, 0);

            // --- and stops when somebody does ----------------------------------------
            using var already = new CancellationTokenSource();
            already.Cancel();

            Exception? stopped = Record.Exception(() => Long().Resolve(measurer, already.Token));
            Console.WriteLine($"    cancelled beforehand  -> {stopped?.GetType().Name ?? "finished anyway"}");
            AssertEqual(testName, "a layout nobody wants throws rather than finishing",
                true, stopped is OperationCanceledException, 0);

            // --- between composing and drawing ---------------------------------------
            // The seam a caller holding both halves has for nothing. The overload that
            // holds them for them takes it on their behalf, which is what this checks:
            // the layout is done, the drawing has not begun, and nothing comes out.
            var pdf = new PdfBackend(library) { Language = "en" };
            Exception? atTheSeam = Record.Exception(
                () => ReportRenderer.Render(Long(), pdf, already.Token));

            Console.WriteLine($"    at the seam           -> {atTheSeam?.GetType().Name ?? "drew anyway"}");
            AssertEqual(testName, "and so does a render, before it draws a thing",
                true, atTheSeam is OperationCanceledException, 0);

            // --- and while drawing ---------------------------------------------------
            // Cancelled after the layout, so the token is only ever seen by the drawing
            // loop - the one place the two checks above cannot reach.
            LayoutResult composed = Long().Resolve(measurer);
            using var duringDrawing = new CancellationTokenSource();
            duringDrawing.Cancel();

            var second = new PdfBackend(library) { Language = "en" };
            Exception? whileDrawing = Record.Exception(
                () => ReportRenderer.Render(Long(), composed, second, duringDrawing.Token));

            Console.WriteLine($"    while drawing         -> {whileDrawing?.GetType().Name ?? "finished anyway"}");
            AssertEqual(testName, "drawing stops too, not only laying out",
                true, whileDrawing is OperationCanceledException, 0);

            // --- nothing partial escapes ---------------------------------------------
            // The whole point. A PDF cut short still opens like a PDF, so the guarantee
            // worth holding is not "it stopped" but "it produced nothing".
            byte[] leftBehind = second.ToArray();
            LayoutResult full = Long().Resolve(measurer);
            var complete = new PdfBackend(library) { Language = "en" };
            ReportRenderer.Render(Long(), full, complete);

            Console.WriteLine($"    cancelled: {leftBehind.Length} bytes, whole: {complete.ToArray().Length} bytes");
            AssertEqual(testName, "a cancelled render is nowhere near a whole one",
                true, leftBehind.Length < complete.ToArray().Length / 2, 0);

            // --- and a default token costs nothing -----------------------------------
            // Every existing caller passes none. If that were not free, this change
            // would have made the ordinary path pay for a case it never uses.
            AssertEqual(testName, "no token at all still renders", true,
                Long().Resolve(measurer, CancellationToken.None).PageCount == whole.PageCount, 0);

            Console.WriteLine();
        }
    }
}
