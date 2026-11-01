using System.Text.Json;

namespace Frisket
{
    /// <summary>
    /// What a template cannot show about itself until it is filled: the pictures the data brings.
    ///
    /// <see cref="TemplateValidator"/> reads the template, where a chart that comes through
    /// <see cref="ImageItem.SourceField"/> is only a name. Such a picture is refused as the
    /// document is drawn, in a rendering that has already started. This asks beforehand, of the
    /// document as it will be, without drawing it.
    ///
    /// The rules are the validator's own, since the fault is the same one wherever the bytes came
    /// from, and the pointer is into the template: the place that names the field, which is where
    /// a program can look, and not into a filled copy whose rows have been multiplied. A list of
    /// a thousand entries with the same fault says so once.
    /// </summary>
    public static class TemplateData
    {
        /// <param name="template">The template, as it was read and before it is bound.</param>
        /// <param name="dataJson">The data that will fill it.</param>
        /// <param name="target">What is about to be produced, as for the validator: a mail cannot show a drawing.</param>
        /// <param name="options">How it will be bound, if not in the default way.</param>
        public static IReadOnlyList<TemplateIssue> Check(Report template, string dataJson,
                                                         RenderTarget? target = null, BindingOptions? options = null)
        {
            if (template is null) throw new ArgumentNullException(nameof(template));
            if (dataJson is null) throw new ArgumentNullException(nameof(dataJson));

            var found = new List<TemplateIssue>();
            Report bound;

            try
            {
                bound = ReportBinder.Bind(template, dataJson, options);
            }
            catch (Exception e) when (e is JsonException or MissingDataException or InvalidOperationException)
            {
                found.Add(new TemplateIssue("data.invalid", IssueLevel.Error,
                    "This data cannot fill the template: " + e.Message, null, "Data"));
                return found;
            }

            // Field by field: the bound copies are clones, and what they share with the template
            // is the name they were filled from.
            var named = new Dictionary<string, ImageItem>();
            foreach (ImageItem written in template.AllItems.OfType<ImageItem>())
            {
                if (!string.IsNullOrWhiteSpace(written.SourceField))
                    named.TryAdd(written.SourceField.Trim(), written);
            }

            var said = new HashSet<(string Field, string Rule, string Message)>();

            void Say(string field, string rule, IssueLevel level, string message)
            {
                if (!said.Add((field, rule, message)))
                    return;

                ImageItem? written = named.GetValueOrDefault(field);
                found.Add(new TemplateIssue(rule, level, $"The picture at \"{field}\" in the data: {message}",
                                            written, "Data › " + field)
                {
                    Pointer = written == null ? null : TemplateValidator.PointerOf(template, written)
                });
            }

            foreach (ImageItem image in bound.AllItems.OfType<ImageItem>())
            {
                if (string.IsNullOrWhiteSpace(image.SourceField))
                    continue;

                string field = image.SourceField.Trim();

                if (Pictures.Refusal(image.Source) is { } refused)
                    Say(field, "sound.unreadablePicture", IssueLevel.Error, refused);

                if (target is { Paged: false } && Pictures.LooksLikeDrawing(image.Source.Data))
                    Say(field, "can.drawingInFlow", IssueLevel.Warning, TemplateValidator.DrawingInFlow);
            }

            return found;
        }
    }
}
