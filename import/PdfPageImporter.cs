namespace Frisket.Import
{
    /// <summary>
    /// What the engine asks when a template names a page of another PDF.
    ///
    /// Four lines, and that is the point: the interface lives in the engine so the engine
    /// can call it, and everything that reads bytes somebody else chose lives here, so a
    /// document that imports nothing never carries a line of it.
    /// </summary>
    public sealed class PdfPageImporter : IPageImporter
    {
        /// <inheritdoc />
        public IImportedPage Read(byte[] pdf, int index) => PdfPageSource.Read(pdf, index);
    }
}
