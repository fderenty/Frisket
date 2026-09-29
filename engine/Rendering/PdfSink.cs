namespace Frisket
{
    /// <summary>
    /// The narrow view of the file being written that something outside the engine needs
    /// in order to put objects into it.
    ///
    /// It exists so that importing a page from another PDF can be built as a separate
    /// assembly that this one never references. The reasoning is the same as
    /// <c>ISignatureProvider</c>'s and it is not tidiness: the threat model is inverted.
    /// Everything else here produces bytes we chose; a reader consumes bytes somebody
    /// else chose, and giving <c>Frisket</c> that attack surface for one feature would be
    /// paying for a parser in every document that never imports anything.
    ///
    /// Reserve first and fill afterwards, always. An object graph copied out of another
    /// file has references pointing in every direction, so nothing can be written until
    /// every number it might name is known.
    /// </summary>
    public interface IPdfSink
    {
        /// <summary>Claims an object number to be filled in later.</summary>
        int Reserve();

        /// <summary>Writes a plain object's body - everything between <c>obj</c> and <c>endobj</c>.</summary>
        void Set(int number, string body);

        /// <summary>
        /// Writes a stream object. <paramref name="dictionary"/> is the inside of the
        /// dictionary and must not carry <c>/Length</c>, which is computed here, nor
        /// <c>/Filter</c> when <paramref name="compress"/> is asked for.
        /// </summary>
        void SetStream(int number, string dictionary, byte[] data, bool compress = false);
    }

    /// <summary>
    /// One page of another PDF, ready to be copied into the file being written.
    ///
    /// It is a <b>copy, not a rendering</b>, and that single decision is what separates
    /// this from "a PDF reader". Nothing here interprets a content operator, or knows
    /// what a font or a colour space is: the page's marking instructions and the objects
    /// they reach travel across exactly as they were, renumbered and no more. So the
    /// work is the file's structure - the cross-reference table, object streams, the
    /// page tree - and stops well short of understanding what the page says.
    ///
    /// <para><b>An imported page puts a conformance claim in question.</b> The engine
    /// cannot vouch for bytes it did not write: the page may carry a font that is not
    /// embedded, transparency a profile forbids, or a colour space with no ICC profile
    /// behind it. That is reported through <c>LayoutResult.Issues</c> and
    /// refused by nothing, which is the same decision as reading a font's <c>fsType</c>
    /// and never enforcing it - the caller may be the only one who knows, so the engine
    /// states the fact and leaves the choice where it belongs. A report being assembled
    /// to be read has no need of PDF/A at all, and for that use none of this
    /// matters.</para>
    /// </summary>
    public interface IImportedPage
    {
        /// <summary>The page's width in millimetres, from its media box.</summary>
        double Width { get; }

        /// <summary>The page's height in millimetres, from its media box.</summary>
        double Height { get; }

        /// <summary>
        /// Copies everything the page needs into <paramref name="sink"/> and returns the
        /// object number of a Form XObject that draws it.
        ///
        /// The form's own coordinate space is the page's, so a caller places it with a
        /// matrix rather than by asking for a size - which is what lets one imported page
        /// be laid on a sheet of a different shape without anything being stretched by
        /// accident.
        /// </summary>
        int WriteInto(IPdfSink sink);
    }
}
