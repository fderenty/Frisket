using System.Text.Json.Serialization;
namespace Frisket
{
    /// <summary>How much a finding matters.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<IssueLevel>))]
    public enum IssueLevel
    {
        /// <summary>The section will be refused, or read wrongly. Fix before shipping.</summary>
        Error,

        /// <summary>Legal, and probably not what was meant.</summary>
        Warning
    }

    /// <summary>What a template is being checked against.</summary>
    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter<TemplateProfile>))]
    public enum TemplateProfile
    {
        /// <summary>Things wrong whatever the document is for.</summary>
        Sound = 1,

        /// <summary>What PDF/UA needs, and a screen reader with it.</summary>
        Accessible = 2,

        /// <summary>
        /// Whether the output being aimed at can carry what the template asks for.
        ///
        /// The other two read the template alone; this one needs to be told what is
        /// about to be produced - <see cref="RenderTarget"/> - because the same
        /// template is faultless for one output and half-lost in another. A form is a
        /// form until it is flattened; a role and an alternative text reach the file
        /// only if it is tagged.
        ///
        /// Silent unless a target is given, since with none there is nothing to
        /// compare the template against.
        /// </summary>
        Capable = 4,

        All = Sound | Accessible | Capable
    }

    /// <summary>
    /// What is about to be produced, so <see cref="TemplateProfile.Capable"/> can say
    /// whether the template survives it.
    ///
    /// Defaults are the richest output - tagged, interactive - so a caller that says
    /// nothing is told nothing rather than warned about a loss that is not happening.
    /// </summary>
    /// <param name="Tagged">Whether the structure a reader needs is written.</param>
    /// <param name="Interactive">Whether form fields stay fillable rather than drawn.</param>
    /// <param name="Paged">
    /// Whether the output has pages at all. A PDF does; a message does not - it reflows
    /// to whatever width the reader's phone happens to be. Anything the template placed
    /// at coordinates has no form there, which is a loss nothing else can see.
    /// </param>
    public sealed record RenderTarget(bool Tagged = true, bool Interactive = true,
                                      bool Paged = true);

    /// <summary>One thing worth saying about a template.</summary>
    /// <param name="Rule">Short identifier, stable enough to filter or suppress on.</param>
    /// <param name="Level">Whether it will break, or merely disappoint.</param>
    /// <param name="Message">What is wrong, in a sentence someone can act on.</param>
    /// <param name="Item">The element concerned. Null when it is the section itself.</param>
    /// <param name="Where">Readable path to the element, for a list that is not on screen.</param>
    public sealed record TemplateIssue(string Rule, IssueLevel Level, string Message,
                                       BaseItem? Item, string Where);

    /// <summary>
    /// Reads a template and says what will go wrong, before any section is produced.
    ///
    /// Everything here is something an outside tool would eventually report - veraPDF, a
    /// screen reader, a scanner - but only after a PDF exists, by which time the person
    /// who could fix it has moved on. A missing alternative text is a five-second edit in
    /// the designer and a conformance failure three steps later.
    ///
    /// It reads the template alone. Anything that depends on the data - whether a token
    /// resolves, how tall a repeated row grows - belongs to binding, not here.
    /// </summary>
    public static class TemplateValidator
    {
        public static IReadOnlyList<TemplateIssue> Check(Report report,
                                                         TemplateProfile profile = TemplateProfile.All)
            => Check(report, profile, null);

        /// <summary>
        /// The same, told what is about to be produced. Only
        /// <see cref="TemplateProfile.Capable"/> reads <paramref name="target"/>; the
        /// other profiles answer from the template alone, as they always have.
        /// </summary>
        public static IReadOnlyList<TemplateIssue> Check(Report report,
                                                        TemplateProfile profile,
                                                        RenderTarget? target)
        {
            if (report is null) throw new ArgumentNullException(nameof(report));

            var found = new List<TemplateIssue>();

            if (profile.HasFlag(TemplateProfile.Capable) && target != null)
                CheckAgainstTarget(report, target, found);

            if (profile.HasFlag(TemplateProfile.Accessible))
                CheckDocument(report, found);

            // Every section, named so a fault in the third one does not read as a fault
            // in the first.
            for (int i = 0; i < report.Sections.Count; i++)
                Walk(report.Sections[i], report.Sections.Count == 1 ? "Section" : $"Section {i + 1}",
                     report, profile, found);

            if (report.BackPage is { } back)
                Walk(back.Content, "Verso", report, profile, found);

            return found;
        }

        /// <summary>
        /// What the chosen output cannot carry. Every rule here is about a template that
        /// is perfectly sound and simply loses something on the way out - which is
        /// exactly the kind of loss nobody notices, because the document still looks
        /// right.
        /// </summary>
        private static void CheckAgainstTarget(Report report, RenderTarget target,
                                               List<TemplateIssue> found)
        {
            List<BaseItem> items = report.AllItems.ToList();

            // Roles, alternative texts and a declared language are written into the
            // structure tree. Untagged, none of them reaches the file: the care taken
            // over the template is simply absent from what is produced.
            if (!target.Tagged)
            {
                bool described = !string.IsNullOrWhiteSpace(report.Language)
                              || items.Any(i => i.Role != PdfRole.Auto
                                             || !string.IsNullOrWhiteSpace(i.AltText));
                if (described)
                {
                    found.Add(new TemplateIssue("can.untagged", IssueLevel.Warning,
                        "This template declares roles, alternative texts or a language, and "
                        + "none of it reaches an untagged file. Render with Tagged, or stop "
                        + "maintaining what is being dropped.", null, "Report"));
                }
            }

            // Placed at coordinates, going somewhere with no coordinates. The children
            // can only be written one under the next, in the order they were declared,
            // which is certainly not the arrangement that was drawn. Reported before a
            // byte is written, because afterwards it is too late to be told.
            if (!target.Paged)
            {
                // A plain group, and nothing that merely inherits from one. A TableRow is
                // a ContainerItem laid out absolutely too, and its cells are placed by
                // the table rather than by an author - reporting every row of every table
                // would bury the one group this is about. The designer's echo matches on
                // the JSON discriminant and so was exact from the start; this is the
                // harness catching the two disagreeing.
                // Not "was it placed at coordinates" but "do its pieces sit beside one
                // another". A picture beside a paragraph has a perfectly good flowing
                // form - a row of cells, the oldest idiom there is - and warning about
                // that would be a rule people learn to ignore. What has no flowing form
                // is an arrangement whose pieces overlap, since a cell cannot be half
                // over its neighbour.
                //
                // FlowingHtml.SideBySide answers it, so the warning and the output cannot
                // disagree about which groups survive.
                double band = report.Sections.Count > 0
                    ? Math.Max(1, report.Sections[0].Dimension.Width)
                    : 210;

                List<ContainerItem> pinned = items
                    .Where(i => i.GetType() == typeof(ContainerItem))
                    .Cast<ContainerItem>()
                    .Where(c => c.Layout == LayoutMode.Absolute && c.Childs.Count > 1)
                    .Where(c => FlowingHtml.SideBySide(c, band) == null)
                    .ToList();

                // One per group, not one carrying a count. Each is a different place to
                // go and put right, so each names its own element - and a finding that
                // says "n groups" cannot be told apart from one saying a different n,
                // which is how the harness came to pass over an echo that had stopped
                // testing anything at all.
                foreach (ContainerItem group in pinned)
                {
                    found.Add(new TemplateIssue("can.absoluteInFlow", IssueLevel.Warning,
                        "This group's children overlap one another across the band, and the "
                        + "output being aimed at has no pages to place them on. They will be "
                        + "written one under the next instead.", group, "Report"));
                }
            }

            if (!target.Interactive)
            {
                // A flattened field is drawn exactly as it would be filled - the values
                // are identical - so nothing looks wrong. Only the filling in is gone.
                // A signature to be given by hand is not a widget in either output: it is
                // drawn both ways, so flattening takes nothing from it. Every rule below
                // is about what the flattening costs, so none of them is about this one -
                // and its being a figure that owes an alternative text is ua.altText's
                // business, which asks it whatever the output.
                //
                // Section 15 again, and it is the same rule as the demotion in the
                // backend seen from the other side: what decides is whether a widget was
                // ever coming, not what kind of field it is.
                List<FieldItem> losing = items.OfType<FieldItem>()
                    .Where(f => !(f.Kind == FieldKind.Signature && f.Signing == Signing.ByHand))
                    .ToList();

                // The count is of what loses something, not of what exists. Counting every
                // field told an author whose only control is a pen-signed signature that
                // "1 field will be drawn rather than left fillable" - about a field drawn
                // identically either way, three lines under a comment saying none of these
                // rules is about it.
                if (losing.Count > 0)
                {
                    found.Add(new TemplateIssue("can.flattenedFields", IssueLevel.Warning,
                        $"{losing.Count} field(s) will be drawn rather than left fillable. "
                        + "The page looks the same either way, which is what makes this easy "
                        + "to ship by mistake.", losing[0], "Report"));
                }

                // And a field that will be drawn is a figure in the structure tree,
                // because a Form element must point at a widget and no widget is coming.
                // PDF/UA 7.3 then wants something to say about it - so a field with no
                // AltText, flattened AND tagged, is a file veraPDF refuses.
                //
                // Both conditions, or this fires on every flattened field in an untagged
                // render, where there is no structure tree to be wrong about. Section 15:
                // a rule that accuses a template that is right teaches people to ignore
                // the panel, and this panel is the only thing between a simplified editor
                // and a document veraPDF refuses.
                if (target.Tagged)
                {
                    foreach (FieldItem mute in losing.Where(f => string.IsNullOrWhiteSpace(f.AltText)))
                    {
                        found.Add(new TemplateIssue("can.flattenedFieldAltText", IssueLevel.Error,
                            "This field will be drawn rather than left fillable, so it is a "
                            + "figure in the structure tree - and a figure with nothing to "
                            + "say for it is refused by PDF/UA. Give it an alternative text, "
                            + "or leave the fields fillable.", mute, "Report"));
                    }
                }

                // Worse for a signature: what is produced is a picture of a place to
                // sign, which cannot be signed.
                //
                // One per signature, and not one standing for the rest. Section 5a-bis
                // gives the test - is each a different place to go and put right? - and
                // here it is: the remedy can be global (leave the fields fillable) or it
                // can be this one signature becoming a by-hand one, which survives being
                // drawn. A finding reporting only the first hides the others.
                //
                // It is also what makes the by-hand exclusion above observable at all.
                // Reporting only the first, the rule produced one finding whether or not
                // the pen-signed one was excluded, so the harness could not tell the two
                // sides apart - and the sabotage that took the exclusion out of the
                // designer's echo went green.
                foreach (FieldItem signature in losing.Where(f => f.Kind == FieldKind.Signature))
                {
                    found.Add(new TemplateIssue("can.flattenedSignature", IssueLevel.Error,
                        "A signature field flattened is a picture of a place to sign. Nobody "
                        + "can sign it, and nothing says so.", signature, "Report"));
                }
            }
        }

        private static void CheckDocument(Report report, List<TemplateIssue> found)
        {
            if (string.IsNullOrWhiteSpace(report.Language))
            {
                found.Add(new TemplateIssue("ua.language", IssueLevel.Error,
                    "The section does not say what language it is written in, so a reader "
                    + "announces it with whatever voice it happens to have. Set Language to a "
                    + "tag such as fr-FR.", null, "Section"));
            }

            if (string.IsNullOrWhiteSpace(report.Title))
            {
                found.Add(new TemplateIssue("ua.title", IssueLevel.Error,
                    "The section has no title, which is what a reader announces when the file "
                    + "opens and what an archive indexes it under.", null, "Section"));
            }
        }

        private static void Walk(ContainerItem container, string where, Report report,
                                 TemplateProfile profile, List<TemplateIssue> found)
        {
            if (container is TableItem table)
                CheckTable(table, where, profile, found);

            var names = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (BaseItem child in container.Childs)
            {
                string here = where + " › " + Name(child);

                CheckItem(child, here, profile, found, names, report.HeadingNumbering);

                if (child is ContainerItem inner)
                    Walk(inner, here, report, profile, found);
            }
        }

        private static void CheckItem(BaseItem item, string where, TemplateProfile profile,
                                      List<TemplateIssue> found, Dictionary<string, int> names,
                                      string? numbering = null)
        {
            PdfRole role = PdfRoles.Resolve(item);

            // A heading number asked for and not answerable. All three leave the token
            // visible on the page rather than resolving to something wrong, which is loud
            // - but loud at the wrong moment, once the document is out.
            if (item is TextItem { Text: not null } counted && HeadingNumbers.Mentions(counted.Text))
            {
                int declared = HeadingNumbers.Depth(numbering);
                int level = HeadingNumbers.LevelOf(item);

                if (declared == 0)
                {
                    found.Add(new TemplateIssue("sound.headingNumberNoFormat", IssueLevel.Warning,
                        "This text asks for a heading number, and the report does not say how "
                        + "one is written. Set HeadingNumbering - \"1.1.1\", \"A.a.1\" - or drop "
                        + "the token; until then it stays on the page as it was typed.",
                        item, where));
                }

                if (level == 0 && !MentionsLevel(counted.Text))
                {
                    found.Add(new TemplateIssue("sound.headingNumberOutsideHeading", IssueLevel.Warning,
                        "This is not a heading, so there is no level for the number to be. "
                        + "Give it a heading role, or name the level in the token itself - "
                        + "{HeadingNumber[1]} for the chapter this sits under.", item, where));
                }

                if (declared > 0 && level > declared)
                {
                    found.Add(new TemplateIssue("sound.headingNumberDepth", IssueLevel.Warning,
                        "This heading is deeper than the numbering format spells out, so the "
                        + "last symbol of the format is repeated for it. Add a level to "
                        + "HeadingNumbering, or the two deepest levels will look alike.",
                        item, where));
                }
            }

            if (profile.HasFlag(TemplateProfile.Accessible))
            {
                // A figure with nothing to say in its place is announced as nothing at
                // all: the person is not even told something is there.
                if (role == PdfRole.Figure && string.IsNullOrWhiteSpace(item.AltText))
                {
                    found.Add(new TemplateIssue("ua.altText", IssueLevel.Error,
                        "This is shown rather than read, and has no alternative text. Without one "
                        + "a screen reader announces nothing at all.", item, where));
                }

                // A role naming a control, on something that will never be one. An
                // authored role is taken before anything else is asked, so this walks
                // straight past the guard PdfRoles.Resolve has for exactly this - and the
                // renderer then does the only sane thing left and writes a Figure, which
                // owes an alternative text that nothing asked the author for.
                //
                // Found by reviewing the demotion rather than by writing it: the backend
                // named three ways to open a Form no widget comes for, and this was the
                // third, handled in the file and unmentioned in the panel.
                if (role == PdfRole.Form && item is not FieldItem)
                {
                    found.Add(new TemplateIssue("ua.formRole", IssueLevel.Error,
                        "This is not a field, so the role Form names a control that will "
                        + "never be written. It is drawn instead, and a drawing needs an "
                        + "alternative text - set the role to Figure and give it one, or "
                        + "leave the role to be worked out.", item, where));
                }

                // The tooltip is a field's accessible name; PDF/UA requires one.
                if (item is FieldItem field && string.IsNullOrWhiteSpace(field.Tooltip)
                    && role == PdfRole.Form)
                {
                    found.Add(new TemplateIssue("ua.fieldName", IssueLevel.Error,
                        "This field has no tooltip, so a reader announces a control with no idea "
                        + "what it is for.", item, where));
                }
            }

            if (!profile.HasFlag(TemplateProfile.Sound))
                return;

            // Columns divide a flow. An absolutely placed child carries its own
            // coordinates, so there is nothing for a column to decide; a table or an
            // image grid has its own dispatch that never consults ColumnCount either,
            // whatever Layout says - both leave the setting doing nothing at all,
            // silently, which is the part worth reporting.
            if (item is ContainerItem { ColumnCount: > 1 } columned
                && (columned.Layout == LayoutMode.Absolute || item is TableItem or ImageGridItem))
            {
                found.Add(new TemplateIssue("sound.columnsIgnored", IssueLevel.Warning,
                    "This asks for columns, but they will never appear here: an absolutely "
                    + "placed container has no flow to divide, and a table or an image grid "
                    + "already divides its rows on its own terms. Set it to a plain stacking "
                    + "container, or drop the columns.", item, where));
            }

            // A hard break inside justified text is stretched to the margin like a
            // wrapped line, which is not what a break means.
            if (item is TextItem { Text: not null } justified
                && justified.Font.HorizontalAlignment == HorizontalAlignment.Justify
                && justified.Text.Contains('\n'))
            {
                found.Add(new TemplateIssue("sound.justifiedBreak", IssueLevel.Warning,
                    "This text is justified and holds a line break of its own. A break is "
                    + "stretched to the margin like a wrapped line, which is not what it "
                    + "means - put each paragraph in its own text.", item, where));
            }

            if (item is BarcodeItem code && string.IsNullOrWhiteSpace(code.Value))
            {
                found.Add(new TemplateIssue("sound.emptyBarcode", IssueLevel.Error,
                    "This barcode has nothing to encode.", item, where));
            }

            // Neither a picture baked into the template nor a path to one in the data:
            // both backends draw nothing at all for an empty Source, silently.
            if (item is ImageItem picture && picture.Source.Data.Length == 0
                && string.IsNullOrWhiteSpace(picture.SourceField))
            {
                found.Add(new TemplateIssue("sound.emptyImage", IssueLevel.Error,
                    "This image has neither a picture of its own nor a path to one in the "
                    + "data, so nothing will ever be drawn here.", item, where));
            }

            // The same fault as an empty image, in a new element: neither a PDF of its
            // own nor a path to one, so nothing will ever be drawn here.
            if (item is ImportedPageItem blank && blank.Data.Length == 0
                && string.IsNullOrWhiteSpace(blank.SourcePath))
            {
                found.Add(new TemplateIssue("sound.emptyImportedPage", IssueLevel.Error,
                    "This imported page has neither a PDF of its own nor a path to one, so "
                    + "nothing will ever be drawn here.", item, where));
            }

            // Pages are counted from one, because a template is written by a person.
            // Zero and below are not a smaller number, they are a different question -
            // and the reader would be handed a negative index it can only refuse.
            if (item is ImportedPageItem { Page: < 1 })
            {
                found.Add(new TemplateIssue("sound.importedPageNumber", IssueLevel.Error,
                    "Pages are counted from one. This asks for a page before the first.",
                    item, where));
            }

            // Fewer than one column is not a smaller grid, it is a broken one - the
            // resolver falls back to one column rather than dividing by zero, but that
            // is not what the number on screen promised.
            if (item is ImageGridItem { GridColumns: < 1 })
            {
                found.Add(new TemplateIssue("sound.emptyImageGrid", IssueLevel.Error,
                    "This image grid asks for fewer than one column, which draws as a "
                    + "single column rather than what was set here.", item, where));
            }

            // A section is an item like any other - that is what lets a report hold a
            // list of them - so nothing in the type system stops one being dropped into
            // a body. It means nothing: a page size inside a flow already laid out on a
            // page. The model can express it and the renderer cannot honour it, which is
            // exactly the shape of fault this validator exists for.
            if (item is Section)
            {
                found.Add(new TemplateIssue("sound.nestedSection", IssueLevel.Error,
                    "A section cannot sit inside another. Sections are a list on the "
                    + "report, never nested - a page size means nothing inside a page.",
                    item, where));
            }

            // Nothing to splice in: the engine has no path to resolve, so the item
            // stays empty forever, silently.
            // Three ways to say where the template comes from, so three ways not to:
            // no path, no reference, and no bytes carried.
            if (item is SubTemplateItem sub
                && string.IsNullOrWhiteSpace(sub.TemplatePath) && sub.TemplateData.Length == 0)
            {
                found.Add(new TemplateIssue("sound.emptySubTemplate", IssueLevel.Error,
                    "This sub-template names nothing and carries nothing, so nothing will "
                    + "ever be inserted here. Give it a path, a st:// reference, or the "
                    + "other template's own JSON.", item, where));
            }

            // A page break on a repeating container breaks once, before the whole run -
            // which is a perfectly ordinary thing to want, so this is not about a value
            // that does nothing. Measured, all four placements are honoured exactly:
            // on the run it breaks once, on the repeated block it breaks per record, on
            // both it does both.
            //
            // What earns a warning is narrower. RepeatOver clones a container's
            // *children*, never the container, so "one page per record" has to be stated
            // one level in - and a run wrapping a single block is the shape where the
            // break was most likely meant for that block. Where there is no inner
            // container there is nowhere else to put it, and where the inner one already
            // has its own break the author has plainly understood the difference: both
            // stay quiet, or this would fire on templates that are right.
            if (item is ContainerItem repeating
                && !string.IsNullOrWhiteSpace(repeating.RepeatOver)
                && repeating.Style.PageBreak is PageBreak.Before or PageBreak.BeforeAndAfter
                && repeating.Childs.Count == 1
                && repeating.Childs[0] is ContainerItem repeated
                && repeated.Style.PageBreak is PageBreak.None)
            {
                found.Add(new TemplateIssue("sound.repeatPageBreak", IssueLevel.Warning,
                    "This breaks once, before the whole repeated run. Repeating clones "
                    + "what is inside the container, so for one page per record the break "
                    + "belongs on the block being repeated.",
                    item, where));
            }

            if (item is AdvancedTextItem advanced)
            {
                // A blank cell is a blank cell, not a fault. Everywhere else an empty
                // paragraph takes room and draws nothing, which is worth an error; in a
                // table it is a column with nothing to say in this row, and §11 requires
                // it to be there rather than left out. Reporting it would put four errors
                // on the panel the moment somebody inserted a table, which is how a panel
                // teaches people to stop reading it.
                if (advanced.Parent is not TableRow
                    && advanced.Runs.All(r => string.IsNullOrEmpty(r.Text)))
                {
                    found.Add(new TemplateIssue("sound.emptyAdvancedText", IssueLevel.Error,
                        "This advanced text has no runs with anything in them, so nothing "
                        + "will ever be drawn here.", item, where));
                }

                // Justify used to be reported here, because the resolver placed each
                // run's own box and never stretched the gaps between them. It stretches
                // them now - PlaceAdvancedLines sets a justified line word by word - so
                // there is nothing left to warn about and the rule is gone rather than
                // kept as a reassuring no-op. A rule that no longer describes the engine
                // is worse than no rule: it teaches people to ignore the panel.

                // {Pages} in a run was reported here too, because only a plain TextItem's
                // fragment was revisited once the total was known and a run printed the
                // placeholder - "page 1 of 0". Each piece of a run carries the token beside
                // it now (BreakAdvancedText, 20 October 2026), so the rule went the way
                // Justify's did.
            }

            // Two fields answering to one name are one field to a reader: filling either
            // fills both, and only one value survives.
            if (item is FieldItem named && !string.IsNullOrWhiteSpace(named.Name))
            {
                if (names.TryGetValue(named.Name, out _))
                {
                    found.Add(new TemplateIssue("sound.duplicateField", IssueLevel.Warning,
                        $"Another field beside this one is also called '{named.Name}'. Filling one "
                        + "fills the other, and only one value is kept.", item, where));
                }

                names[named.Name] = 1;
            }
        }

        /// <summary>True if any heading token in this text names its own level.</summary>
        private static bool MentionsLevel(string text)
            => text.Contains("{" + HeadingNumbers.Token + "[", StringComparison.OrdinalIgnoreCase)
               || text.Contains("{" + HeadingNumbers.SectionToken + "[", StringComparison.OrdinalIgnoreCase);

        private static void CheckTable(TableItem table, string where, TemplateProfile profile,
                                       List<TemplateIssue> found)
        {
            var rows = table.Childs.OfType<TableRow>()
                            .Where(r => r.Style.Visibility != Visibility.Gone).ToList();

            if (rows.Count == 0)
                return;

            if (profile.HasFlag(TemplateProfile.Accessible) && !rows.Any(r => r.IsHeader))
            {
                found.Add(new TemplateIssue("ua.tableHeader", IssueLevel.Warning,
                    "This table has no header row, so a reader announces values with nothing to "
                    + "say what they are.", table, where));
            }

            if (!profile.HasFlag(TemplateProfile.Sound))
                return;

            // Rows of different widths cannot be navigated out loud, and a conformance
            // checker refuses them outright. Cheap to see here, expensive to find later:
            // this is exactly what veraPDF caught in our own sample.
            //
            // The declared columns are the authority when there are any; without them the
            // widest row is the best guess at what the table was meant to be.
            int width = table.Columns.Count > 0 ? table.Columns.Count : rows.Max(r => r.Childs.Count);

            foreach (TableRow row in rows.Where(r => r.Childs.Count != width))
            {
                found.Add(new TemplateIssue("sound.raggedTable", IssueLevel.Error,
                    $"This row has {row.Childs.Count} cell(s) where the table has {width} column(s). "
                    + "Rows of unequal width cannot be read out in order, and a conformance "
                    + "checker refuses them.", row, where + " › " + Name(row)));
            }
        }

        private static string Name(BaseItem item) => item switch
        {
            TextItem text when !string.IsNullOrWhiteSpace(text.Text)
                => Short(text.Text),
            FieldItem field when !string.IsNullOrWhiteSpace(field.Name) => field.Name,
            TableRow { IsHeader: true } => "header row",
            TableRow => "row",
            _ => item.GetType().Name
        };

        private static string Short(string text)
        {
            string clean = text.Replace('\n', ' ').Trim();
            return clean.Length <= 24 ? clean : clean[..24] + "…";
        }
    }
}
