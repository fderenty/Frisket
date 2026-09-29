namespace test
{
    /// <summary>
    /// The published shared libraries, read as files rather than run.
    ///
    /// Section 8 is emphatic that a library which loads and returns FK_OK proves
    /// nothing, and this proves less than that: it never loads anything. What it settles
    /// is narrower and was worth having anyway - that each published binary is an image
    /// of the architecture its folder claims, and that it offers every entry point
    /// <c>frisket.h</c> declares.
    ///
    /// It exists because win-arm64 was published on 20 August 2026 and five entry points
    /// were added on the 21st. The ARM64 library sat there for five days exporting
    /// fifteen of the twenty, and nothing could see it: there is no ARM machine here, the
    /// suite never opened the folder, and "the build said succeeded" is all anyone had.
    /// A caller asking for fk_cancel would have got null, and a wrapper that binds at
    /// load time would have failed outright.
    ///
    /// Skipped rather than failed when a binary is absent: a fresh clone publishes
    /// nothing, and a silent green on a check with no subject is exactly the vacuous
    /// assertion this project complains about everywhere else.
    /// </summary>
    public partial class EngineTests
    {
        [SkippableFact]
        public void Test75_PublishedLibrariesExportWhatTheHeaderDeclares()
        {
            const string testName = "Test 75";
            Console.WriteLine("Test 75: every published library exports what frisket.h declares");
            Console.WriteLine();

            string root = Path.Combine(RepositoryRoot(), "bindings", "native");
            string header = Path.Combine(root, "frisket.h");
            Skip.If(!File.Exists(header), "bindings/native/frisket.h is not there.");

            // Every fk_ name the boundary declares, read from the header rather than
            // listed here: a list kept beside the subject is one addition away from
            // checking less than it says.
            var declared = System.Text.RegularExpressions.Regex
                .Matches(File.ReadAllText(header), @"\bfk_[a-z_]+(?=\s*\()")
                .Select(m => m.Value).Distinct().OrderBy(n => n).ToList();

            Console.WriteLine($"    frisket.h declare {declared.Count} points d'entree");
            AssertEqual(testName, "the header declares entry points at all",
                true, declared.Count > 10, 0);

            // Every publish folder, and what is in it - rather than only the ones that
            // happen to hold what this test can read. A folder that exists and is empty
            // reads as "published" to anybody browsing, and is not: linux-x64 is built
            // inside WSL and its library never lands in this tree.
            string bin = Path.Combine(root, "bin");
            var folders = Directory.Exists(bin)
                ? Directory.GetDirectories(bin)
                    .Where(d => Directory.Exists(Path.Combine(d, "publish")))
                    .OrderBy(d => d).ToList()
                : new List<string>();

            var published = new List<(string Rid, string Dll)>();
            var unread = new List<string>();
            foreach (string folder in folders)
            {
                string rid = Path.GetFileName(folder);
                var libraries = Directory.GetFiles(Path.Combine(folder, "publish"))
                    .Where(f => f.EndsWith(".dll") || f.EndsWith(".so") || f.EndsWith(".dylib"))
                    .ToList();

                if (libraries.Count == 0)
                {
                    Console.WriteLine($"    {rid,-12} rien de publie ici");
                    continue;
                }

                foreach (string library in libraries)
                {
                    // A stated limit rather than a silent pass: this reads PE images, so
                    // an ELF or a Mach-O is named and left alone. The day one lands here
                    // it says so out loud instead of being counted as checked.
                    if (!library.EndsWith(".dll"))
                    {
                        Console.WriteLine($"    {rid,-12} {Path.GetFileName(library)} - pas un PE, non lu ici");
                        unread.Add($"{rid}/{Path.GetFileName(library)}");
                        continue;
                    }
                    published.Add((rid, library));
                }
            }

            // Two different reasons to skip, said as two. It used to answer "run build.sh to
            // have this run" whatever it found - and on Linux build.sh publishes an ELF, which
            // this test does not read, so following the advice changed nothing. Found the
            // first time the suite ran on Linux with a library published beside it.
            Skip.If(published.Count == 0 && unread.Count > 0,
                "Only libraries this test cannot read were published (" + string.Join(", ", unread)
                + "). It reads PE export tables; an ELF or a Mach-O is not checked against the "
                + "header here, which is a limit of this test and not a pass.");
            Skip.If(published.Count == 0,
                "No PE library has been published. Run bindings/native/build.ps1 on Windows to "
                + "have this run - and see CLAUDE.md \u00a78 for why the script rather than the "
                + "bare dotnet publish.");

            foreach (var (rid, dll) in published)
            {
                byte[] image = File.ReadAllBytes(dll);
                string machine = MachineOf(image);
                var exports = ExportsOf(image).OrderBy(n => n).ToList();

                Console.WriteLine($"    {rid,-12} {machine,-6} {exports.Count} exports");

                // The folder names the architecture; the file has to agree. A win-arm64
                // folder holding an x64 image is a build that quietly ignored its RID.
                string wanted = rid.EndsWith("-arm64") ? "ARM64"
                              : rid.EndsWith("-x64") ? "x64" : machine;
                AssertEqual(testName, $"{rid} is an image of the architecture it claims",
                    wanted, machine, 0);

                var missing = declared.Where(n => !exports.Contains(n)).ToList();
                if (missing.Count > 0)
                    Console.WriteLine($"      manquants : {string.Join(", ", missing)}");

                AssertEqual(testName, $"{rid} exports every declared entry point",
                    0, missing.Count, 0);
            }

            // And they offer the same surface as each other, which is the half that
            // catches one target left behind: a name added to the header and to one
            // build is caught by the loop above, but a name dropped from the header and
            // still exported by one image only shows here.
            if (published.Count > 1)
            {
                var surfaces = published
                    .Select(p => string.Join(",", ExportsOf(File.ReadAllBytes(p.Dll))
                        .Where(n => n.StartsWith("fk_")).OrderBy(n => n)))
                    .Distinct().ToList();

                AssertEqual(testName, "and every target offers the same surface",
                    1, surfaces.Count, 0);
            }

            Console.WriteLine();
            Console.WriteLine("    (read as files, never run: there is no ARM machine here, and");
            Console.WriteLine("     section 8 says what running one is worth that this is not)");
            Console.WriteLine();
        }

        /// <summary>The COFF machine of a PE image, as a name this file can compare.</summary>
        private static string MachineOf(byte[] image)
        {
            if (image.Length < 0x40 || image[0] != 'M' || image[1] != 'Z')
                return "not a PE";

            int pe = BitConverter.ToInt32(image, 0x3C);
            if (pe + 6 > image.Length || BitConverter.ToInt32(image, pe) != 0x00004550)
                return "no PE signature";

            return BitConverter.ToUInt16(image, pe + 4) switch
            {
                0x8664 => "x64",
                0xAA64 => "ARM64",
                0x014C => "x86",
                var other => "0x" + other.ToString("X4")
            };
        }

        /// <summary>
        /// The names a PE image exports.
        ///
        /// Written out rather than taken from a package: the engine has no dependencies
        /// and a test project that needs one to read a header would be a strange way to
        /// keep that promise. Fifty lines, and the format has not changed since 1993.
        /// </summary>
        private static List<string> ExportsOf(byte[] image)
        {
            var names = new List<string>();
            if (MachineOf(image).StartsWith("no") || MachineOf(image).StartsWith("not"))
                return names;

            int pe = BitConverter.ToInt32(image, 0x3C);
            int sections = BitConverter.ToUInt16(image, pe + 6);
            int optionalSize = BitConverter.ToUInt16(image, pe + 20);
            int optional = pe + 24;

            // PE32+ carries eight more bytes of addresses before the data directories.
            bool plus = BitConverter.ToUInt16(image, optional) == 0x20B;
            int directories = optional + (plus ? 112 : 96);
            int exportRva = BitConverter.ToInt32(image, directories);
            if (exportRva == 0)
                return names;

            // The section table, to turn a virtual address into an offset in the file.
            var map = new List<(int Rva, int Size, int Raw)>();
            for (int i = 0; i < sections; i++)
            {
                int at = optional + optionalSize + i * 40;
                int vsize = BitConverter.ToInt32(image, at + 8);
                int vaddr = BitConverter.ToInt32(image, at + 12);
                int rsize = BitConverter.ToInt32(image, at + 16);
                int raw = BitConverter.ToInt32(image, at + 20);
                map.Add((vaddr, Math.Max(vsize, rsize), raw));
            }

            int Offset(int rva)
            {
                foreach (var (start, size, raw) in map)
                    if (rva >= start && rva < start + size)
                        return raw + (rva - start);
                return -1;
            }

            int table = Offset(exportRva);
            if (table < 0)
                return names;

            int count = BitConverter.ToInt32(image, table + 24);
            int namesRva = BitConverter.ToInt32(image, table + 32);
            int pointers = Offset(namesRva);
            if (pointers < 0)
                return names;

            for (int i = 0; i < count; i++)
            {
                int at = Offset(BitConverter.ToInt32(image, pointers + i * 4));
                if (at < 0)
                    continue;

                int end = at;
                while (end < image.Length && image[end] != 0)
                    end++;

                names.Add(System.Text.Encoding.ASCII.GetString(image, at, end - at));
            }

            return names;
        }
    }
}
