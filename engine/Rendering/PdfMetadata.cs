using System.Text.Json.Serialization;
using System.Globalization;
using System.Text;

namespace Frisket
{
    /// <summary>How strictly the PDF should conform.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PdfConformance>))]
    public enum PdfConformance
    {
        /// <summary>A plain PDF 1.7 file.</summary>
        None,

        /// <summary>
        /// PDF/A-3 level B: archivable, and the basis Factur-X builds on. Everything the
        /// file needs to render must travel inside it - fonts above all - and it carries
        /// a colour profile and XMP metadata saying so. Level B guarantees appearance;
        /// level A would also require a tagged structure tree.
        /// </summary>
        PdfA3b
    }

    /// <summary>A file carried inside the PDF, and what it is to the section.</summary>
    public sealed class PdfAttachment
    {
        public required string Name { get; init; }
        public required byte[] Data { get; init; }
        public string MediaType { get; init; } = "application/octet-stream";

        /// <summary>
        /// The AFRelationship: Data for a machine-readable version of the section,
        /// Source, Alternative, Supplement, or Unspecified.
        /// </summary>
        public string Relationship { get; init; } = "Unspecified";

        public string? Description { get; init; }
    }

    /// <summary>Builds the XMP packet a PDF/A file must carry.</summary>
    internal static class PdfMetadata
    {
        /// <summary>
        /// Factur-X declares itself through its own XMP schema, which PDF/A in turn
        /// requires to be described in the extension schema below - metadata about the
        /// metadata, but a conformance checker does look for it.
        /// </summary>
        public const string FacturXNamespace = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";

        public static string Build(string title, string? author, DateTimeOffset created,
                                   string producer, PdfConformance conformance,
                                   string? facturXFileName, string? facturXProfile,
                                   string? subject = null, string? keywords = null,
                                   bool tagged = false)
        {
            string when = created.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
            var sb = new StringBuilder(4096);

            sb.Append("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n")
              .Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n")
              .Append(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");

            sb.Append("  <rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n")
              .Append("   <dc:format>application/pdf</dc:format>\n")
              .Append("   <dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">")
              .Append(Escape(title)).Append("</rdf:li></rdf:Alt></dc:title>\n");
            if (!string.IsNullOrEmpty(author))
                sb.Append("   <dc:creator><rdf:Seq><rdf:li>").Append(Escape(author!))
                  .Append("</rdf:li></rdf:Seq></dc:creator>\n");

            // Whatever the info dictionary says, this has to say too: a validator reads
            // both and rejects a file whose two accounts of itself disagree.
            if (!string.IsNullOrEmpty(subject))
                sb.Append("   <dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">")
                  .Append(Escape(subject!)).Append("</rdf:li></rdf:Alt></dc:description>\n");

            sb.Append("  </rdf:Description>\n");

            if (tagged)
            {
                // Without this a checker has nothing to test against: a file can be
                // perfectly accessible and still never claim to be.
                sb.Append("  <rdf:Description rdf:about=\"\" ")
                  .Append("xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\">\n")
                  .Append("   <pdfuaid:part>1</pdfuaid:part>\n")
                  .Append("  </rdf:Description>\n");

            }

            sb.Append("  <rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">\n")
              .Append("   <xmp:CreatorTool>").Append(Escape(producer)).Append("</xmp:CreatorTool>\n")
              .Append("   <xmp:CreateDate>").Append(when).Append("</xmp:CreateDate>\n")
              .Append("   <xmp:ModifyDate>").Append(when).Append("</xmp:ModifyDate>\n")
              .Append("  </rdf:Description>\n");

            sb.Append("  <rdf:Description rdf:about=\"\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">\n")
              .Append("   <pdf:Producer>").Append(Escape(producer)).Append("</pdf:Producer>\n");

            // Keywords belongs to the pdf schema, so it goes in the block that declares
            // the prefix. Written into the dc block instead, the prefix was unbound and
            // the whole packet stopped parsing - and a checker that cannot read the
            // packet cannot read the conformance claim either, so the file was taken for
            // PDF/A-1 and failed on everything PDF/A-3 exists to permit.
            if (!string.IsNullOrEmpty(keywords))
                sb.Append("   <pdf:Keywords>").Append(Escape(keywords!)).Append("</pdf:Keywords>\n");

            sb.Append("  </rdf:Description>\n");

            if (conformance == PdfConformance.PdfA3b)
            {
                sb.Append("  <rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">\n")
                  .Append("   <pdfaid:part>3</pdfaid:part>\n")
                  .Append("   <pdfaid:conformance>B</pdfaid:conformance>\n")
                  .Append("  </rdf:Description>\n");
            }

            if (facturXFileName != null)
            {
                sb.Append("  <rdf:Description rdf:about=\"\" xmlns:fx=\"").Append(FacturXNamespace).Append("\">\n")
                  .Append("   <fx:DocumentType>INVOICE</fx:DocumentType>\n")
                  .Append("   <fx:DocumentFileName>").Append(Escape(facturXFileName)).Append("</fx:DocumentFileName>\n")
                  .Append("   <fx:Version>1.0</fx:Version>\n")
                  .Append("   <fx:ConformanceLevel>").Append(Escape(facturXProfile ?? "EN 16931"))
                  .Append("</fx:ConformanceLevel>\n")
                  .Append("  </rdf:Description>\n");

            }

            sb.Append(ExtensionSchemas(facturXFileName != null,
                                       tagged && conformance != PdfConformance.None));

            sb.Append(" </rdf:RDF>\n</x:xmpmeta>\n");

            // The trailing padding is what lets a tool rewrite the packet in place.
            for (int i = 0; i < 20; i++)
                sb.Append("                                                                                                  \n");
            sb.Append("<?xpacket end=\"w\"?>");

            return sb.ToString();
        }

        /// <summary>
        /// Describes the schemas this file borrows that PDF/A does not know on its own.
        ///
        /// One block, one bag, however many schemas: pdfaExtension:schemas is a property
        /// like any other and may appear once. Written twice - once for Factur-X and once
        /// for PDF/UA - neither claim survives, and the two standards look incompatible
        /// when the fault is a duplicated property.
        /// </summary>
        private static string ExtensionSchemas(bool facturX, bool tagged)
        {
            if (!facturX && !tagged)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append("  <rdf:Description rdf:about=\"\" xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\"")
              .Append(" xmlns:pdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\"")
              .Append(" xmlns:pdfaProperty=\"http://www.aiim.org/pdfa/ns/property#\">\n")
              .Append("   <pdfaExtension:schemas><rdf:Bag>\n");

            if (tagged)
            {
                sb.Append("    <rdf:li rdf:parseType=\"Resource\">\n")
                  .Append("     <pdfaSchema:schema>PDF/UA identification schema</pdfaSchema:schema>\n")
                  .Append("     <pdfaSchema:namespaceURI>http://www.aiim.org/pdfua/ns/id/</pdfaSchema:namespaceURI>\n")
                  .Append("     <pdfaSchema:prefix>pdfuaid</pdfaSchema:prefix>\n")
                  .Append("     <pdfaSchema:property><rdf:Seq>\n")
                  .Append("      <rdf:li rdf:parseType=\"Resource\">\n")
                  .Append("       <pdfaProperty:name>part</pdfaProperty:name>\n")
                  .Append("       <pdfaProperty:valueType>Integer</pdfaProperty:valueType>\n")
                  .Append("       <pdfaProperty:category>internal</pdfaProperty:category>\n")
                  .Append("       <pdfaProperty:description>PDF/UA version identifier</pdfaProperty:description>\n")
                  .Append("      </rdf:li>\n")
                  .Append("     </rdf:Seq></pdfaSchema:property>\n")
                  .Append("    </rdf:li>\n");
            }

            if (!facturX)
            {
                return sb.Append("   </rdf:Bag></pdfaExtension:schemas>\n")
                         .Append("  </rdf:Description>\n").ToString();
            }

            sb.Append("    <rdf:li rdf:parseType=\"Resource\">\n")
              .Append("    <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>\n")
              .Append("    <pdfaSchema:namespaceURI>").Append(FacturXNamespace).Append("</pdfaSchema:namespaceURI>\n")
              .Append("    <pdfaSchema:prefix>fx</pdfaSchema:prefix>\n")
              .Append("    <pdfaSchema:property><rdf:Seq>\n");

            (string Name, string Description)[] properties =
            {
                ("DocumentFileName", "The name of the embedded XML section"),
                ("DocumentType", "The type of the hybrid section"),
                ("Version", "The version of the standard"),
                ("ConformanceLevel", "The conformance level of the embedded XML")
            };

            foreach ((string name, string description) in properties)
            {
                sb.Append("     <rdf:li rdf:parseType=\"Resource\">\n")
                  .Append("      <pdfaProperty:name>").Append(name).Append("</pdfaProperty:name>\n")
                  .Append("      <pdfaProperty:valueType>Text</pdfaProperty:valueType>\n")
                  .Append("      <pdfaProperty:category>external</pdfaProperty:category>\n")
                  .Append("      <pdfaProperty:description>").Append(description).Append("</pdfaProperty:description>\n")
                  .Append("     </rdf:li>\n");
            }

            sb.Append("     </rdf:Seq></pdfaSchema:property>\n")
              .Append("    </rdf:li>\n")
              .Append("   </rdf:Bag></pdfaExtension:schemas>\n")
              .Append("  </rdf:Description>\n");
            return sb.ToString();
        }

        private static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
