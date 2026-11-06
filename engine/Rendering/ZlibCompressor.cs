namespace Frisket
{
    /// <summary>
    /// Compresses one stream of a PDF for /FlateDecode, in place of the framework's zlib: see
    /// <see cref="PdfBackend.Compressor"/>. What it hands back must be a complete zlib stream
    /// (RFC 1950) of <paramref name="data"/> - the two-byte header, the deflate data, and the
    /// Adler-32 of what was compressed - because that is what a reader inflates.
    ///
    /// It is how a smaller encoder reaches the file without the engine depending on one. Zopfli
    /// is the case it was made for, through the ZopfliSharp package, in one line:
    /// <code>pdf.Compressor = data => Zopfli.Compress(data, ZopfliFormat.Zlib);</code>
    /// <c>ZopfliFormat.Deflate</c> would leave the header and the checksum out, and is refused.
    ///
    /// Called once for each stream the file deflates - page contents, fonts, images, object
    /// streams and the cross-reference stream - and from several threads at once when
    /// <see cref="PdfBackend.CompressionThreads"/> allows it.
    /// </summary>
    /// <param name="data">The stream's bytes, as they will read once inflated.</param>
    public delegate byte[] ZlibCompressor(ReadOnlySpan<byte> data);
}
