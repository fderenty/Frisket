using System.IO.Compression;
using System.Text.RegularExpressions;
using ZopfliSharp;

namespace test
{
    /// <summary>
    /// How the streams are deflated: by a compressor the caller brings, checked before it goes
    /// in, on one thread or several, and by Zopfli - smaller, and the same document.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>
        /// Every stream object of a file, as it stands: its number, its dictionary and the bytes
        /// between <c>stream</c> and <c>endstream</c>, read by the length the dictionary gives.
        /// Streams are never packed into object streams, so the file itself holds them all; a
        /// dictionary is not let run past an <c>endobj</c>, or a plain object of the classic
        /// layout would swallow the stream after it.
        /// </summary>
        static List<(int Number, string Dictionary, byte[] Payload)> StreamsOf(byte[] pdf)
        {
            string raw = System.Text.Encoding.Latin1.GetString(pdf);
            var found = new List<(int, string, byte[])>();

            foreach (Match m in Regex.Matches(raw, @"(?<=\n)(\d+) 0 obj\n<< ((?:(?!endobj).)*?)/Length (\d+) >>\nstream\n",
                         RegexOptions.Singleline))
            {
                int length = int.Parse(m.Groups[3].Value);
                found.Add((int.Parse(m.Groups[1].Value), m.Groups[2].Value, pdf.AsSpan(m.Index + m.Length, length).ToArray()));
            }

            return found;
        }

        /// <summary>A zlib stream that stores rather than compresses: valid, and unlike anything zlib's best would write.</summary>
        static byte[] Stored(ReadOnlySpan<byte> data)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.NoCompression, leaveOpen: true))
                zlib.Write(data);
            return output.ToArray();
        }

        /// <summary>The showcase as test 53 validates it: bound, tagged, archivable, with its Factur-X invoice.</summary>
        static byte[] ShowcasePdf(FontLibrary library, Action<PdfBackend> configure)
        {
            string examples = Path.Combine(RepositoryRoot(), "examples");
            Report report = ReportBinder.Bind(Report.FromFile(Showcase.WriteFiles(examples)),
                File.ReadAllText(Path.Combine(examples, Showcase.DataFile)));

            var backend = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Tagged = true };
            configure(backend);
            backend.AttachFacturX(FacturXXml());
            ReportRenderer.Render(report, backend);
            return backend.ToArray();
        }

        static FontLibrary RepositoryFonts()
        {
            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            return library;
        }

        /// <summary>
        /// PdfBackend.Compressor deflates every stream the file deflates, and the file is the one
        /// it would have been: the point of extension Zopfli comes in by, asked without Zopfli.
        ///
        /// The compressor here stores rather than compresses, so its bytes cannot be taken for the
        /// ones zlib would have written in its place, and it keeps what it handed back: each
        /// /FlateDecode stream in the file must be one of them, and there must be as many as it
        /// was asked for. On the showcase, because it carries every kind of stream there is to
        /// miss - the kinds are asked to be there, or the count would agree for want of matter:
        /// an opaque PNG, whose data used to cross as it came and so never met a compressor, the
        /// soft mask of a PNG with alpha, the fonts, the attachment, the object streams and the
        /// cross-reference stream. Both layouts, since the classic one deflates fewer of them.
        /// </summary>
        [Fact]
        public void Test123_ACompressorBroughtByTheCallerDeflatesEveryStream()
        {
            const string testName = "Test 123";
            Console.WriteLine("Test 123: a compressor brought by the caller deflates every stream");

            FontLibrary library = RepositoryFonts();

            foreach (bool packed in new[] { true, false })
            {
                string layout = packed ? "compressed" : "classic";
                var made = new List<byte[]>();
                byte[] file = ShowcasePdf(library, b =>
                {
                    b.Compress = packed;
                    b.Compressor = data =>
                    {
                        byte[] stored = Stored(data);
                        lock (made) made.Add(stored);
                        return stored;
                    };
                });

                var deflated = StreamsOf(file).Where(s => s.Dictionary.Contains("/FlateDecode")).ToList();
                var mine = deflated.Where(s => made.Any(m => m.AsSpan().SequenceEqual(s.Payload))).ToList();
                Console.WriteLine($"    {layout}: {made.Count} streams handed to the compressor, {deflated.Count} deflated in the file");

                AssertEqual(testName, $"{layout}: as many streams deflated as the compressor was asked for",
                    made.Count, deflated.Count, 0);
                AssertEqual(testName, $"{layout}: every one of them is what it handed back",
                    deflated.Count, mine.Count, 0);

                bool Has(Func<string, bool> kind) => mine.Any(s => kind(s.Dictionary));
                string? mask = Regex.Match(string.Join("\n", deflated.Select(s => s.Dictionary)), @"/SMask (\d+) 0 R").Groups[1].Value;

                AssertEqual(testName, $"{layout}: an opaque PNG's rows among them, behind their predictor", true,
                    Has(d => d.Contains("/Predictor 15")), 0);
                AssertEqual(testName, $"{layout}: a soft mask among them", true,
                    mask is { Length: > 0 } && mine.Any(s => s.Number == int.Parse(mask)), 0);
                AssertEqual(testName, $"{layout}: the Factur-X attachment among them", true,
                    Has(d => d.Contains("/Type /EmbeddedFile")), 0);

                if (packed)
                {
                    AssertEqual(testName, "compressed: a font program among them", true,
                        Has(d => d.Contains("/Length1") || d.Contains("/Subtype /OpenType")), 0);
                    AssertEqual(testName, "compressed: an object stream among them", true, Has(d => d.Contains("/Type /ObjStm")), 0);
                    AssertEqual(testName, "compressed: the cross-reference stream among them", true, Has(d => d.Contains("/Type /XRef")), 0);
                }
            }

            // On several threads the file is the same, with the caller's compressor and without.
            byte[] Twice(ZlibCompressor? compressor, int threads)
                => ShowcasePdf(library, b => { b.Compressor = compressor; b.CompressionThreads = threads; });

            AssertEqual(testName, "zlib on every thread writes what zlib on one writes", true,
                Twice(null, -1).AsSpan().SequenceEqual(Twice(null, 1)), 0);
            AssertEqual(testName, "and so does the caller's compressor", true,
                Twice(d => Stored(d), 4).AsSpan().SequenceEqual(Twice(d => Stored(d), 1)), 0);

            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfBackend(library) { CompressionThreads = 0 });
            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfBackend(library) { CompressionThreads = -2 });
            Console.WriteLine();
        }

        /// <summary>
        /// What a compressor hands back is inflated and compared before it goes in, and refused
        /// in words that say what is wrong with it. A reader handed a stream that is not zlib
        /// draws a blank page or drops a font and says nothing; the plausible mistake is raw
        /// deflate, asked for because the filter is called FlateDecode.
        ///
        /// One answer spoiled in each of the ways the check tells apart, so each branch of it is
        /// seen saying no: the header, the preset dictionary, the checksum, and a body that
        /// inflates to something else behind a checksum that is right. And on several threads,
        /// what one stream threw comes out as itself, a refusal or a cancellation, never wrapped.
        /// </summary>
        [Fact]
        public void Test124_ACompressorThatDoesNotWriteZlibIsRefused()
        {
            const string testName = "Test 124";
            Console.WriteLine("Test 124: a compressor whose answer is not zlib is refused, naming the fault");

            FontLibrary library = RepositoryFonts();

            byte[] Draw(ZlibCompressor compressor, int threads = 1)
            {
                var report = new Report { Title = "Refused", Dimension = new() { Width = 210, Height = 297 }, Body = new() };
                report.Body!.Add(T(string.Join(" ", Enumerable.Repeat("Every stream goes through the compressor.", 12))));
                var pdf = new PdfBackend(library) { Compressor = compressor, CompressionThreads = threads };
                ReportRenderer.Render(report, pdf);
                return pdf.ToArray();
            }

            static byte[] Raw(ReadOnlySpan<byte> data)
            {
                using var output = new MemoryStream();
                using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                    deflate.Write(data);
                return output.ToArray();
            }

            static byte[] Reversed(ReadOnlySpan<byte> data)
            {
                byte[] copy = data.ToArray();
                Array.Reverse(copy);
                return copy;
            }

            static byte[] SameAdler(ReadOnlySpan<byte> data)
            {
                byte[] copy = data.ToArray();
                int i = Enumerable.Range(0, copy.Length - 3).First(k => copy[k] < 255 && copy[k + 1] > 0
                                                                         && copy[k + 2] > 0 && copy[k + 3] < 255);
                copy[i]++;
                copy[i + 1]--;
                copy[i + 2]--;
                copy[i + 3]++;
                return copy;
            }

            var spoiled = new (string Fault, ZlibCompressor Compressor, string Said)[]
            {
                ("raw deflate", d => Raw(d), "does not start with a zlib header"),
                ("nothing", d => null!, "too short"),
                ("a preset dictionary", d => { byte[] z = PdfWriter.Deflate(d.ToArray()); z[0] = 0x78; z[1] = 0xBB; return z; },
                    "preset dictionary"),
                ("another stream's data", d => PdfWriter.Deflate(d[1..].ToArray()), "its checksum is not that of the data"),
                ("another body under the right checksum", d =>
                {
                    byte[] other = PdfWriter.Deflate(Reversed(d));
                    byte[] right = PdfWriter.Deflate(d.ToArray());
                    right[^4..].CopyTo(other.AsSpan(other.Length - 4));
                    return other;
                }, "it does not inflate"),
                // The framework's inflater checks the Adler-32 too, so reaching the comparison
                // takes other data with the same checksum - which Adler-32 makes easy: one up
                // and its neighbour one down leaves the sum alone, and the same again further on,
                // the other way round, puts back what the first pair moved in the running total.
                ("other data with the same checksum", d => PdfWriter.Deflate(SameAdler(d)), "it inflates to something else"),
            };

            foreach (var (fault, compressor, said) in spoiled)
            {
                var refused = Assert.Throws<InvalidOperationException>(() => Draw(compressor));
                Console.WriteLine($"    {fault}: {refused.Message[..Math.Min(140, refused.Message.Length)]}...");
                AssertEqual(testName, $"{fault}: refused, saying why", true, refused.Message.Contains(said), 0);
            }

            var wrapped = Record.Exception(() => Draw(d => Raw(d), threads: -1));
            AssertEqual(testName, "on several threads, the refusal comes out as itself", "InvalidOperationException",
                wrapped?.GetType().Name ?? "nothing", 0);
            AssertEqual(testName, "and says the same", true, wrapped?.Message.Contains("ZopfliFormat.Zlib") == true, 0);

            var cancelled = Record.Exception(() => Draw(d => throw new OperationCanceledException(), threads: -1));
            AssertEqual(testName, "a cancellation stays a cancellation", true, cancelled is OperationCanceledException, 0);

            AssertEqual(testName, "and a compressor that writes zlib is taken", true,
                Draw(d => Stored(d)).Length > 0, 0);
            Console.WriteLine();
        }

        /// <summary>
        /// The showcase deflated by Zopfli, once per run and shared by the two tests that ask
        /// about it: produced here rather than read off the disk, for test 53's reason - a file
        /// lying about from an earlier run passes whatever the code now does.
        /// </summary>
        static readonly Lazy<(byte[] Zlib, byte[] Zopfli, double Seconds)> ZopfliShowcase = new(() =>
        {
            FontLibrary library = RepositoryFonts();
            byte[] zlib = ShowcasePdf(library, _ => { });

            var spread = new ZopfliOptions { MaxDegreeOfParallelism = -1 };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            byte[] zopfli = ShowcasePdf(library, b =>
            {
                b.Compressor = data => Zopfli.Compress(data, ZopfliFormat.Zlib, spread);
                b.CompressionThreads = -1;
            });

            return (zlib, zopfli, clock.Elapsed.TotalSeconds);
        });

        /// <summary>
        /// Zopfli makes the same document, smaller.
        ///
        /// The same: every stream of the Zopfli file inflates to what the stream of the same
        /// number inflates to in the zlib one, under the same dictionary but for its /Length -
        /// which is what tools/compare-pdf.py asks of two files, said here in the suite. The
        /// cross-reference stream's offsets are the one thing allowed to move, since every
        /// length before them did. Smaller: on the showcase, which carries fonts, pictures, a
        /// form, a structure tree and an attachment, and so says what a real document gains.
        /// </summary>
        [Fact]
        public void Test125_ZopfliMakesTheSameDocumentSmaller()
        {
            const string testName = "Test 125";
            Console.WriteLine("Test 125: Zopfli makes the same document, smaller");

            (byte[] zlib, byte[] zopfli, double seconds) = ZopfliShowcase.Value;
            double saved = 100.0 * (zlib.Length - zopfli.Length) / zlib.Length;
            Console.WriteLine($"    zlib {zlib.Length} bytes, Zopfli {zopfli.Length} bytes ({saved:0.0}% smaller), {seconds:0.0} s");

            AssertEqual(testName, "the Zopfli file is smaller", true, zopfli.Length < zlib.Length, 0);

            static string Shape(string dictionary) => Regex.Replace(dictionary, @"/W \[\d+ \d+ \d+\] ", "");

            var left = StreamsOf(zlib).ToDictionary(s => s.Number);
            var right = StreamsOf(zopfli).ToDictionary(s => s.Number);
            AssertEqual(testName, "the same stream objects, by number", string.Join(",", left.Keys.Order()),
                string.Join(",", right.Keys.Order()), 0);

            int compared = 0;
            foreach (var (number, one) in left)
            {
                var other = right[number];
                AssertEqual(testName, $"object {number}: the same dictionary", Shape(one.Dictionary), Shape(other.Dictionary), 0);
                if (one.Dictionary.Contains("/Type /XRef"))
                    continue;

                bool flate = one.Dictionary.Contains("/FlateDecode");
                byte[] a = flate ? Inflated(one.Payload) : one.Payload;
                byte[] b = flate ? Inflated(other.Payload) : other.Payload;
                AssertEqual(testName, $"object {number}: the same bytes once inflated", true, a.AsSpan().SequenceEqual(b), 0);
                compared++;
            }

            AssertEqual(testName, "every stream but the cross-reference compared", left.Count - 1, compared, 0);

            WriteSamplePdf(Path.Combine(OutputDirectory("conformance"), "zopfli.pdf"), zopfli);
            Console.WriteLine();
        }

        /// <summary>
        /// The Zopfli document passes veraPDF on both profiles, as test 53's does with zlib. A
        /// deflate stream is a deflate stream to a validator, so this is not where Zopfli could
        /// fail; it is where a compressor that wrote a broken stream, or a predictor left with no
        /// filter in front of it, would. Skipped where veraPDF is not installed, as test 53 is.
        /// </summary>
        [SkippableTheory]
        [InlineData("3b")]
        [InlineData("ua1")]
        public void Test126_TheZopfliDocumentValidates(string flavour)
        {
            var testName = $"Test 126: the Zopfli document, veraPDF {flavour}";
            Console.WriteLine($"Running {testName}...");

            string? verapdf = FindVeraPdf();
            Skip.If(verapdf == null,
                "veraPDF was not found. Set FRISKET_VERAPDF to its launcher, or put it on PATH, "
                + "to have this run. See CLAUDE.md §4.");

            // Off the tree: the document is test 125's sample, and a copy left behind by a run
            // that stopped half way would be a file nobody committed.
            DirectoryInfo scratch = Directory.CreateTempSubdirectory("frisket-zopfli-");
            string pdf = Path.Combine(scratch.FullName, $"zopfli-{flavour}.pdf");
            File.WriteAllBytes(pdf, ZopfliShowcase.Value.Zopfli);

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(verapdf!)
                {
                    Arguments = $"--format text --flavour {flavour} \"{pdf}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(180_000);
            scratch.Delete(recursive: true);

            Console.WriteLine($"    {output.Trim()}");
            AssertEqual(testName, $"the document is valid {flavour}", true, output.Contains("PASS", StringComparison.Ordinal), 0);
        }

        /// <summary>
        /// `frisket render --zopfli`, started as a program starts it: a smaller PDF than without,
        /// and the two ways to ask for nothing refused before anything is written - passes for an
        /// encoder nobody asked for, and a number of passes that is not one.
        /// </summary>
        [SkippableFact]
        public void Test127_TheCommandLineCompressesWithZopfliWhenAsked()
        {
            const string testName = "Test 127";
            Console.WriteLine("Test 127: frisket render --zopfli");

            string configuration = AppContext.BaseDirectory.Contains(
                Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar) ? "Debug" : "Release";
            string cli = Path.Combine(RepositoryRoot(), "apps", "cli", "bin", configuration, "net10.0", "Frisket.Cli.dll");
            Skip.If(!File.Exists(cli), $"the CLI is not built at {cli}.");

            string dir = Directory.CreateTempSubdirectory("frisket-zopfli-cli-").FullName;
            string template = Path.Combine(RepositoryRoot(), "tests", "output", "acme", "acme.template.json");
            string data = Path.Combine(RepositoryRoot(), "tests", "output", "acme", "acme.data.json");

            (int Code, string Said) Run(params string[] extra)
            {
                var run = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    },
                };
                foreach (string arg in new[] { cli, "render", "-t", template, "-d", data, "--format", "pdf" }.Concat(extra))
                    run.StartInfo.ArgumentList.Add(arg);
                run.Start();
                string said = run.StandardError.ReadToEnd();
                run.StandardOutput.ReadToEnd();
                run.WaitForExit();
                return (run.ExitCode, said);
            }

            var plain = Run("-o", Path.Combine(dir, "zlib"));
            var zopfli = Run("-o", Path.Combine(dir, "zopfli"), "--zopfli");
            long zlibBytes = new FileInfo(Path.Combine(dir, "zlib", "acme.data.pdf")).Length;
            long zopfliBytes = new FileInfo(Path.Combine(dir, "zopfli", "acme.data.pdf")).Length;
            Console.WriteLine($"    the invoice: {zlibBytes} bytes with zlib, {zopfliBytes} with --zopfli");

            AssertEqual(testName, "both rendered", "0 0", $"{plain.Code} {zopfli.Code}", 0);
            AssertEqual(testName, "--zopfli writes a smaller file", true, zopfliBytes < zlibBytes, 0);

            var alone = Run("-o", Path.Combine(dir, "refused"), "--zopfli-iterations", "5");
            AssertEqual(testName, "passes with no --zopfli are refused", 2, alone.Code, 0);
            AssertEqual(testName, "saying what they go with", true, alone.Said.Contains("goes with --zopfli"), 0);

            var none = Run("-o", Path.Combine(dir, "refused"), "--zopfli", "--zopfli-iterations", "0");
            AssertEqual(testName, "no passes at all is refused", 2, none.Code, 0);
            AssertEqual(testName, "saying what is wanted", true, none.Said.Contains("at least 1"), 0);
            AssertEqual(testName, "and nothing was written", false, Directory.Exists(Path.Combine(dir, "refused")), 0);

            Directory.Delete(dir, recursive: true);
            Console.WriteLine();
        }
    }
}
