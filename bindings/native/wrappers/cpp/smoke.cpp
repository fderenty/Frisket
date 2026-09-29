// The C++ side of the boundary, exercised rather than described.
//
// Takes the same four arguments as the C and Pascal samples and writes the same PDF,
// so the three can be compared byte for byte. That comparison is the point: the
// Pascal wrapper passed every check it made while quietly corrupting accented data,
// and only the size of the resulting file gave it away.
//
// It also checks the thing this header exists for - that handles are given back on
// their own, including when an exception is unwinding through the scope that holds
// them.

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>
#include <iostream>
#include <sstream>
#include <string>

#include "frisket.hpp"

namespace {

int failed = 0;

void check(const char* what, bool ok) {
    std::cout << (ok ? "  ok   " : "  FAIL ") << what << '\n';
    if (!ok) ++failed;
}

std::string slurp(const std::string& path) {
    // Read as bytes. Anything that decodes the file into some local code page is how
    // accented data gets spoiled before the library ever sees it.
    std::ifstream input(path, std::ios::binary);
    std::ostringstream all;
    all << input.rdbuf();
    return all.str();
}

}  // namespace

int main(int argc, char** argv) {
    if (argc < 5) {
        std::cout << "usage: smoke <font-dir> <template.json> <data.json> <out.pdf>\n";
        return 2;
    }

    const std::string font_dir = argv[1];
    const std::string template_path = argv[2];
    const std::string data_path = argv[3];
    const std::string pdf_path = argv[4];

    try {
        std::cout << "frisket " << frisket::version() << '\n';

        frisket::fonts faces;
        const std::int32_t taken = faces.add_directory(font_dir);
        check("fonts are found", taken > 0);
        std::cout << "       " << taken << " faces\n";

        // A font the caller chose. The difference from add_directory is that a
        // failure comes back: a scan was never asked about any file in particular,
        // and a font somebody named is a font somebody wants.
        if (argc > 5) {
            frisket::fonts picked;
            picked.add_file(argv[5]);
            check("one font file, named on purpose", picked.list().rfind('[', 0) == 0);

            std::ifstream file(argv[5], std::ios::binary);
            // The extra parentheses are not decoration. Without them this line is a
            // function declaration - C++'s most vexing parse - and the wrapper compiles
            // perfectly well while the program using it does not. Only building the
            // smoke found it, which is why the smoke exists.
            const std::vector<std::uint8_t> bytes(
                (std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
            check("its bytes were read", !bytes.empty());
            picked.add_memory(bytes);

            bool refused = false;
            try {
                const std::vector<std::uint8_t> nonsense{'n', 'o', 't', ' ', 'a', ' ', 'f', 'o', 'n', 't'};
                picked.add_memory(nonsense);
            } catch (const std::exception&) {
                refused = true;
            }
            check("what is not a font is refused, not stepped over", refused);
        }

        faces.set_default_family("Josefin Sans");
        check("the library lists itself", faces.list().rfind('[', 0) == 0);

        const frisket::document doc = frisket::document::from_file(template_path);
        check("the template loads", true);

        // Reported, not required: what the validator finds belongs to the template
        // that was passed in, and a sample must work with whichever one that is.
        const std::string findings = doc.check(true);
        check("the validator answers", !findings.empty() && findings.front() == '[');
        std::cout << "       check -> " << findings << '\n';

        const std::string data = slurp(data_path);
        check("the data file is readable", data.size() > 100);

        // The crossing that matters. A wrapper that decodes the file into some local
        // code page loses every character outside it, and nothing fails: the document
        // comes out, slightly wrong, and only the bytes give it away.
        const bool high = std::any_of(data.begin(), data.end(),
            [](char c) { return static_cast<unsigned char>(c) >= 0x80; });
        check("and non-ASCII data survives being read", high);

        const std::vector<std::uint8_t> pdf = doc.render(faces, data);
        check("a PDF comes out", pdf.size() > 1000);
        check("and it is a PDF", pdf.size() > 5 && std::memcmp(pdf.data(), "%PDF-", 5) == 0);

        std::ofstream out(pdf_path, std::ios::binary);
        out.write(reinterpret_cast<const char*>(pdf.data()),
                  static_cast<std::streamsize>(pdf.size()));
        out.close();
        std::cout << "       " << pdf.size() << " bytes -> " << pdf_path << '\n';

        frisket::options html;
        html.shape = frisket::options::format::html;
        const std::string page = doc.render_text(faces, data, html);
        check("HTML comes out", page.find("<svg") != std::string::npos);

        frisket::options svg;
        svg.shape = frisket::options::format::svg;
        const std::string envelope = doc.render_text(faces, data, svg);
        check("SVG comes out as an envelope",
              envelope.find("\"pages\":[") != std::string::npos
              && envelope.find("\"pageWidth\":210") != std::string::npos);

        frisket::options archival;
        archival.archivable_pdf_a3b = true;
        archival.tagged = true;
        check("and so does an archivable one",
              doc.render(faces, data, archival).size() > pdf.size());

        // A refusal has to arrive as an exception carrying the reason, or a caller
        // has no way to tell a bad option from a blank page.
        bool refused = false;
        try {
            frisket::options wrong;
            wrong.shape = static_cast<frisket::options::format>(99);
            doc.render(faces, data, wrong);
        } catch (const frisket::error& e) {
            refused = true;
            std::cout << "       refused -> " << e.what() << '\n';
        }
        check("an impossible format is refused", refused);

        // What the header is for: the handle inside this scope is freed on the way
        // out, and the way out is an exception. Nothing here can leak it.
        bool unwound = false;
        try {
            frisket::fonts scoped;
            scoped.add_directory(font_dir);
            throw std::runtime_error("on purpose");
        } catch (const std::runtime_error&) {
            unwound = true;
        }
        check("a handle is given back even when an exception unwinds", unwound);

        // Moving is the truth about a handle: one resource, one owner.
        frisket::fonts moved = std::move(faces);
        check("a moved library still works", moved.list().rfind('[', 0) == 0);

    } catch (const std::exception& e) {
        std::cout << "  FAIL unexpected: " << e.what() << '\n';
        ++failed;
    }

    if (failed) {
        std::cout << '\n' << failed << " FAILED\n";
        return 1;
    }

    std::cout << "\nall good\n";
    return 0;
}
