namespace Frisket
{
    /// <summary>
    /// Where a template named <c>st://something</c> comes from.
    ///
    /// Supplied by the caller, because the engine cannot know what a reference means: a
    /// row in a database, a key in a cache, a blob in an object store. What the engine
    /// does know is that it must not guess - a reference with no source is refused, and
    /// never quietly resolved against a directory.
    ///
    /// <b>Whoever implements this owns the safety of it.</b> The file path shape is held
    /// to a root the caller sets, because a path is a thing the engine can reason about
    /// (CLAUDE.md section 8b: a template chose which files the engine read, and an
    /// absolute path made the base directory disappear). A reference is not: only the
    /// implementer knows which references are theirs to serve, so only they can refuse
    /// the ones that are not.
    /// </summary>
    public interface ISubTemplates
    {
        /// <summary>
        /// The template's JSON for this reference, or null when there is none.
        ///
        /// <paramref name="reference"/> is what followed <c>st://</c>, trimmed - so a
        /// template saying <c>st://letterhead</c> asks for "letterhead".
        ///
        /// Returning null is an answer and produces a refusal naming the reference;
        /// throwing is also an answer, and the exception travels to the caller intact
        /// rather than being turned into a missing file.
        /// </summary>
        string? Find(string reference);
    }
}
