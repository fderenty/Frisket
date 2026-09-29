using System.Text.Json.Serialization;

namespace Frisket
{
    /// <summary>Which sheets get a printed back.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<BackPageScope>))]
    public enum BackPageScope
    {
        /// <summary>
        /// Behind every sheet. This is what terms of sale need: a page of an invoice that
        /// travels on its own must carry them, and a page can always be separated from
        /// the rest.
        /// </summary>
        EveryPage,

        /// <summary>Behind the last sheet only.</summary>
        LastPage,

        /// <summary>Behind the first sheet only.</summary>
        FirstPage
    }

    /// <summary>
    /// What is printed on the back of a sheet - conditions of sale, most often.
    ///
    /// It holds ordinary items, so it can be typed text with <c>{tokens}</c>, an image of
    /// a page, or any mix. It is laid out on a full page of its own: no header, no footer
    /// and no page band, because a back is not part of the document's flow.
    ///
    /// Back pages are not numbered and not counted. An invoice reading "page 1 / 2" has
    /// two sides of content and four printed faces, and the reader counts what is written
    /// on the front. <c>{Page}</c> on a back therefore gives the number of the sheet it
    /// belongs to, which is what makes "conditions applying to page 2" possible.
    /// </summary>
    public class BackPage
    {
        /// <summary>The content itself. Laid out over the whole sheet.</summary>
        public ContainerItem Content { get; set; } = new();

        [JsonConverter(typeof(JsonStringEnumConverter<BackPageScope>))]
        public BackPageScope Scope { get; set; } = BackPageScope.EveryPage;

        /// <summary>
        /// Margins of the back sheet, independent of the document's: conditions of sale
        /// are usually set closer to the edge than an invoice, being smaller print.
        /// </summary>
        public EdgeSize Padding { get; set; } = new() { Left = 12, Top = 12, Right = 12, Bottom = 12 };

        public BackPage Clone() => new()
        {
            Content = Content,
            Scope = Scope,
            Padding = Padding.Clone()
        };
    }
}
