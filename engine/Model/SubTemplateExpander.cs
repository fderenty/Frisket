namespace Frisket
{
    /// <summary>
    /// Resolves every <see cref="SubTemplateItem"/> in a tree before anything else touches
    /// it - before binding, so a repeated container clones an already-composed subtree,
    /// and before layout, so continuation, margins and pagination need not know a nested
    /// section was ever there. By the time either runs, there is no splice point left,
    /// only an ordinary flattened tree.
    ///
    /// A sub-template names a file, and the name comes from the template - which is
    /// untrusted the moment a template is something a caller was handed rather than
    /// wrote. So the resolved path is held to a <c>root</c> the caller sets: an include
    /// may live at or below it, never above it and never on another drive. Without that,
    /// <c>Path.Combine(baseDirectory, "..\..\secret")</c> climbs out, and an absolute
    /// path makes <c>baseDirectory</c> vanish entirely - <c>Path.Combine</c> discards
    /// the left side when the right is rooted - so a template could name any file on the
    /// machine and have its contents composed into the document.
    /// </summary>
    internal static class SubTemplateExpander
    {
        /// <summary>
        /// A chain longer than this is refused. The cycle guard already stops a file
        /// including itself; this stops a thousand distinct files including one another
        /// in a line, which the cycle guard does not see and which would otherwise walk
        /// the stack down as far as the chain is long.
        /// </summary>
        private const int MaxDepth = 16;

        internal static void Expand(Report report, string baseDirectory,
                                    ISubTemplates? source = null)
            => Expand(report, baseDirectory, baseDirectory, source);

        /// <param name="root">
        /// The boundary a resolved include may not cross. The directory of the file
        /// being loaded is the safe default; a caller who means to share layout from a
        /// parent directory passes that parent instead, and thereby says so on purpose.
        /// </param>
        internal static void Expand(Report report, string baseDirectory, string root,
                                    ISubTemplates? source = null)
        {
            var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // A report with no file behind it has no root to be held to, and none of the
            // shapes it can use needs one: carried bytes resolve against nothing, and a
            // reference is the caller's to serve.
            string fullRoot = string.IsNullOrEmpty(root) ? "" : Path.GetFullPath(root);

            // Every section, and not only the first. This read `report.Section` - the
            // facade over Sections[0] - so a sub-template anywhere but in the opening
            // section was never expanded at all: no error, no splice, an empty container
            // where a block was meant to be. It went unseen because a report had one
            // section for most of the engine's life and no sample has ever put an
            // include in a second one. Appending makes several sections the ordinary
            // case, so it would have stopped being invisible today in any event.
            for (int i = 0; i < report.Sections.Count; i++)
            {
                var appended = new List<Section>();
                ExpandContainer(report.Sections[i], baseDirectory, fullRoot, chain, 0, source, appended);

                if (appended.Count == 0)
                    continue;

                // After the section that asked for them, and then skipped: they came out
                // of ExpandOne already expanded, and walking them again would expand
                // their own includes twice.
                report.Sections.InsertRange(i + 1, appended);
                i += appended.Count;
            }

            // The pieces arrived from another Report, so their Owner is that one and
            // their Parent is nothing. Every back-reference is rebuilt from the shape of
            // the tree, which is exactly what this exists for.
            report.Reattach();
        }

        /// <param name="appended">
        /// Where a piece brought across whole is put, for the caller to place in the run
        /// of sections. It travels down rather than being returned because the item can
        /// sit at any depth, and what it produces belongs at the top.
        /// </param>
        private static void ExpandContainer(ContainerItem container, string baseDirectory,
                                            string root, HashSet<string> chain, int depth,
                                            ISubTemplates? source, List<Section> appended)
        {
            foreach (BaseItem child in container.Childs.ToArray())
            {
                if (child is SubTemplateItem sub)
                {
                    ExpandOne(sub, baseDirectory, root, chain, depth, source, appended);
                    continue;   // its children come already expanded - see ExpandOne
                }

                if (child is ContainerItem inner)
                    ExpandContainer(inner, baseDirectory, root, chain, depth, source, appended);
            }
        }

        /// <summary>The scheme a reference the caller must serve begins with.</summary>
        internal const string ReferencePrefix = "st://";

        /// <summary>
        /// Loads what <paramref name="item"/> points at and gives it the referenced
        /// report's <see cref="Report.Body"/>, as children of its own - never the
        /// referenced report's header, footer, page size or margins, which is what
        /// makes this a splice into the surrounding flow rather than a page within
        /// a page.
        /// </summary>
        private static void ExpandOne(SubTemplateItem item, string baseDirectory, string root,
                                      HashSet<string> chain, int depth, ISubTemplates? source,
                                      List<Section> appended)
        {
            bool carried = item.TemplateData.Length > 0;

            // TemplateValidator's sound.emptySubTemplate already says so; there is
            // nothing to load and no path to resolve a cycle against.
            if (!carried && string.IsNullOrWhiteSpace(item.TemplatePath))
                return;

            string named = carried ? "(carried)" : item.TemplatePath;

            if (depth >= MaxDepth)
            {
                throw new InvalidOperationException(
                    $"Sub-templates nested deeper than {MaxDepth}, at '{named}'. "
                    + "This is a chain of includes too long to be anything but a mistake.");
            }

            // Which door, decided by what is filled in rather than by a mode anybody has
            // to set. Only the third touches a disk, which is why the traversal guard
            // lives inside it and nowhere else.
            string json;
            string key;
            string referencedDirectory = baseDirectory;

            if (carried)
            {
                // The template travels inside this one, so there is nothing to resolve,
                // no root to be held to, and no identity to key a cycle on - the bytes
                // are finite and MaxDepth still bounds how deep they can nest.
                json = System.Text.Encoding.UTF8.GetString(item.TemplateData);
                key = "";
            }
            else if (item.TemplatePath.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase))
            {
                string reference = item.TemplatePath[ReferencePrefix.Length..].Trim();

                // Refused rather than guessed. Path.Combine on "st://x" produces
                // something a file system will happily interpret, and a reference
                // silently becoming a path is the shape of fault section 8b is about.
                if (source is null)
                {
                    throw new InvalidOperationException(
                        $"Sub-template '{item.TemplatePath}' names a reference, and no source "
                        + "was given to resolve it. Pass an ISubTemplates to Report.FromFile "
                        + "or to ExpandSubTemplates, or carry the template in TemplateData.");
                }

                json = source.Find(reference)
                    ?? throw new FileNotFoundException(
                        $"Sub-template reference not found: '{item.TemplatePath}'.");

                key = ReferencePrefix + reference;
            }
            else
            {
                // No file behind this report, so no directory to resolve against and no
                // root to be held to. Refused rather than resolved: Path.Combine("", x)
                // is x, and GetFullPath would then measure it from the working
                // directory - which is a template choosing which files the engine reads,
                // the fault section 8b was written for. Nearly shipped it a second time.
                if (string.IsNullOrEmpty(root))
                {
                    throw new InvalidOperationException(
                        $"Sub-template '{item.TemplatePath}' names a path, and this report "
                        + "came from text rather than from a file - so there is no directory "
                        + "to resolve it against. Carry the template in TemplateData, or "
                        + "name it st:// and give a source.");
                }

                string path = Path.GetFullPath(Path.Combine(baseDirectory, item.TemplatePath));

                // The check is on the canonical path, after GetFullPath, or a '..' or a link
                // would slip under the comparison. The message names the path the author
                // wrote, never the one it resolved to: the resolved path describes the
                // machine's layout to whoever sent the template.
                if (!IsUnder(path, root))
                {
                    throw new InvalidOperationException(
                        $"Sub-template '{item.TemplatePath}' points outside the template's own "
                        + "directory. An include may sit beside the template or below it, "
                        + "never above it and never at an absolute path.");
                }

                if (!File.Exists(path))
                    throw new FileNotFoundException($"Sub-template not found: '{item.TemplatePath}'.");

                json = File.ReadAllText(path);
                key = path;

                // A sub-template may itself include further sub-templates, each resolved
                // relative to where *it* lives, not the file that first pulled it in - so
                // moving a sub-template does not silently break what it includes. The root
                // does not move with it: everything still has to land under the boundary
                // the caller set at the top.
                referencedDirectory = Path.GetDirectoryName(path) ?? baseDirectory;
            }

            if (key.Length > 0 && !chain.Add(key))
            {
                throw new InvalidOperationException(
                    $"Sub-template cycle: '{named}' includes itself, directly "
                    + "or through another sub-template.");
            }

            try
            {
                Report referenced = Report.FromJson(json);

                // The piece resolved whole first, its own sections included, so what is
                // taken below is finished. A cycle through an append is caught by the
                // same chain: this runs inside it.
                var deeper = new List<Section>();
                for (int i = 0; i < referenced.Sections.Count; i++)
                {
                    ExpandContainer(referenced.Sections[i], referencedDirectory, root,
                                    chain, depth + 1, source, deeper);
                    if (deeper.Count == 0)
                        continue;

                    referenced.Sections.InsertRange(i + 1, deeper);
                    i += deeper.Count;
                    deeper = new List<Section>();
                }

                if (item.Mode == SubTemplateMode.Append)
                {
                    AppendDocument(item, referenced, appended);
                    return;
                }

                if (referenced.Body != null)
                {
                    foreach (BaseItem child in referenced.Body.Childs)
                        item.Add(child.Clone());
                }
            }
            finally
            {
                // Spent once resolved: the same file included twice in different,
                // non-nested places is not a cycle, only the chain currently being
                // expanded is.
                if (key.Length > 0)
                    chain.Remove(key);
            }
        }

        /// <summary>
        /// Takes the piece whole: its sections, its page sizes, its headers and footers.
        ///
        /// What does NOT cross is everything a file has one of. Title, author, subject
        /// and keywords are the information dictionary and the XMP packet, of which
        /// there is exactly one per PDF - so a piece's are dropped and the host's stand.
        /// That is not a limitation to be lifted later: a dossier assembling three
        /// reports has one title, and it is the dossier's.
        ///
        /// The language is the exception, because PDF makes it one. /Lang belongs to the
        /// file AND may sit on any structure element, so a piece that declares its own
        /// is given it on the section it becomes, and a reader announces those pages in
        /// the language they are written in. Silently taking the host's would be the
        /// worst of the three available answers - a German appendix read aloud with
        /// English phonemes is not wrong in a way anybody can see.
        ///
        /// Only where the piece said something, and only where it differs: writing the
        /// host's own tag onto every appended section would fill the file with /Lang
        /// entries that say nothing, and hide the ones that do.
        /// </summary>
        private static void AppendDocument(SubTemplateItem item, Report referenced,
                                           List<Section> appended)
        {
            foreach (Section piece in referenced.Sections)
            {
                if (!string.IsNullOrWhiteSpace(referenced.Language)
                    && string.IsNullOrWhiteSpace(piece.Language))
                {
                    piece.Language = referenced.Language;
                }

                appended.Add(piece);
            }

            // The item leaves nothing behind. It is a container, so an empty one would
            // lay out as a band of no height and be tagged as a section holding nothing -
            // an element in the structure tree standing for a document that is now
            // somewhere else entirely.
            item.Parent?.Remove(item);
        }

        /// <summary>
        /// Whether a canonical path sits at or below a canonical root. Asked through
        /// <see cref="Path.GetRelativePath"/> rather than a string prefix, so that
        /// <c>/root-evil</c> is not mistaken for a child of <c>/root</c>, and a different
        /// drive - where the relative path comes back rooted - is refused outright.
        /// </summary>
        private static bool IsUnder(string path, string root)
        {
            string relative = Path.GetRelativePath(root, path);

            return !Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith("../", StringComparison.Ordinal);
        }
    }
}
