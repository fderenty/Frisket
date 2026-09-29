namespace Frisket
{
    /// <summary>
    /// Something able to read a page out of another PDF.
    ///
    /// Handed in rather than reached for, and the reason is the same one
    /// <c>ISignatureProvider</c> has: parsing another producer's file is consuming bytes
    /// somebody else chose, and <c>Frisket</c> should not carry that for the sake of
    /// documents that import nothing. <c>Frisket.Import</c> fills this in.
    /// </summary>
    public interface IPageImporter
    {
        /// <summary>
        /// The page at <paramref name="index"/>, counting from zero.
        ///
        /// From zero here and from one in <see cref="ImportedPageItem.Page"/>: an index is
        /// an index, and a template is written by a person for whom the third page of a
        /// document is the third page of it. The translation happens once, at this seam.
        /// </summary>
        IImportedPage Read(byte[] pdf, int index);
    }

    /// <summary>
    /// Finds the page every <see cref="ImportedPageItem"/> names, before anything measures
    /// or draws.
    ///
    /// Before, and not at render time, for the reason section 14 gives about
    /// sub-templates: one choice answers several questions at once. A page whose size is
    /// known before layout can be fitted and flowed like a picture; a repeated container
    /// clones children that already carry what was read, so the same file is opened once
    /// rather than once per record; and the fact that the document now contains somebody
    /// else's bytes can be stated in <c>LayoutResult.Issues</c>, which is
    /// composed and not drawn.
    /// </summary>
    public static class ImportedPages
    {
        /// <summary>
        /// Reads every page this report names.
        ///
        /// <paramref name="root"/> is the boundary a path may not cross, exactly as for a
        /// sub-template and for the same reason: the path comes from the template, and a
        /// template is untrusted the moment it is something a caller was handed rather
        /// than wrote. Empty means no path may be resolved at all - which is right for a
        /// server handed JSON as text, and is a refusal rather than a resolution against
        /// the working directory.
        /// </summary>
        public static void Resolve(Report report, IPageImporter reader,
                                   string baseDirectory = "", string? root = null)
        {
            ArgumentNullException.ThrowIfNull(report);
            ArgumentNullException.ThrowIfNull(reader);

            string boundary = string.IsNullOrEmpty(root) ? baseDirectory : root;
            string fullRoot = string.IsNullOrEmpty(boundary) ? "" : Path.GetFullPath(boundary);

            // Read once per file and page, not once per item: a template that puts the
            // same terms on the back of every section names it several times, and a
            // repeated container names it once per record.
            var already = new Dictionary<(string, int), IImportedPage>();

            foreach (BaseItem item in report.AllItems)
            {
                if (item is not ImportedPageItem page || page.Resolved != null)
                    continue;

                int index = Math.Max(0, page.Page - 1);
                byte[] bytes = Bytes(page, baseDirectory, fullRoot, out string key);

                if (bytes.Length == 0)
                    continue;                       // sound.emptyImportedPage says so

                if (already.TryGetValue((key, index), out IImportedPage? known))
                {
                    page.Resolved = known;
                    continue;
                }

                IImportedPage read = reader.Read(bytes, index);
                already[(key, index)] = read;
                page.Resolved = read;
            }
        }

        private static byte[] Bytes(ImportedPageItem page, string baseDirectory,
                                    string root, out string key)
        {
            if (page.Data.Length > 0)
            {
                // Keyed on the bytes rather than on the item, so the same file carried
                // twice is read once. Cheap, since this is a length and a hash of what is
                // already in memory.
                key = "data:" + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(page.Data));
                return page.Data;
            }

            if (string.IsNullOrWhiteSpace(page.SourcePath))
            {
                key = "";
                return Array.Empty<byte>();
            }

            if (string.IsNullOrEmpty(root))
            {
                throw new InvalidOperationException(
                    $"The imported page '{page.SourcePath}' names a file, and this report has no "
                    + "directory to resolve it against. Load the template with Report.FromFile, "
                    + "or carry the PDF in Data.");
            }

            string resolved = Path.GetFullPath(Path.Combine(baseDirectory, page.SourcePath));

            // The canonical path, held to the root through GetRelativePath rather than a
            // string prefix - "root-evil" is not a child of "root", and an absolute path
            // makes Path.Combine discard the left side entirely, which is the whole of
            // section 8b's first fault.
            string relative = Path.GetRelativePath(root, resolved);
            bool inside = !Path.IsPathRooted(relative)
                          && relative != ".."
                          && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                          && !relative.StartsWith("../", StringComparison.Ordinal);

            if (!inside)
            {
                // Named as the author wrote it, never as it resolved: the resolved path
                // describes the machine to whoever sent the template.
                throw new InvalidOperationException(
                    $"The imported page '{page.SourcePath}' resolves outside the folder it is "
                    + "allowed to read from.");
            }

            if (!File.Exists(resolved))
                throw new FileNotFoundException($"No PDF at '{page.SourcePath}'.");

            key = "file:" + resolved;
            return File.ReadAllBytes(resolved);
        }
    }
}
