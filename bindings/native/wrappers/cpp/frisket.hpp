// Frisket for C++ - a header, and nothing to build.
//
// The C interface underneath is complete and perfectly usable on its own; what this
// adds is that you cannot forget to give anything back. Every handle lives in an
// object that frees it when the object goes out of scope, whether that happens at the
// end of the block or because an exception is unwinding through it. Failures arrive
// as exceptions carrying what the library said, so a return value never goes
// unchecked either.
//
// Handles are moveable and not copyable, which is the truth about them: there is one
// underlying resource, and two owners of it would free it twice.
//
// C++17. Include frisket.h beside it and link the import library.

#ifndef FRISKET_HPP
#define FRISKET_HPP

#include <cstdint>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

#include "../../frisket.h"

namespace frisket {

/// What the library refused, and why. The message is copied out at once: the C side
/// keeps it only until the next call on this thread.
class error : public std::runtime_error {
public:
    explicit error(const char* what)
        : std::runtime_error(what && *what ? what : "frisket failed") {}
};

namespace detail {

inline void check(fk_status status) {
    if (status != FK_OK)
        throw error(fk_last_error());
}

/// Owns one handle of type T and frees it with F. The single place any of this is
/// written, so the three kinds below are three lines each.
template <typename Free>
class owned {
public:
    owned() = default;
    explicit owned(void* handle) : handle_(handle) {}

    owned(const owned&) = delete;
    owned& operator=(const owned&) = delete;

    owned(owned&& other) noexcept : handle_(std::exchange(other.handle_, nullptr)) {}

    owned& operator=(owned&& other) noexcept {
        if (this != &other) {
            reset();
            handle_ = std::exchange(other.handle_, nullptr);
        }
        return *this;
    }

    ~owned() { reset(); }

    void* get() const noexcept { return handle_; }
    explicit operator bool() const noexcept { return handle_ != nullptr; }

private:
    void reset() noexcept {
        if (handle_) Free{}(std::exchange(handle_, nullptr));
    }

    void* handle_ = nullptr;
};

struct free_fonts    { void operator()(void* h) const noexcept { fk_fonts_free(h); } };
struct free_template { void operator()(void* h) const noexcept { fk_template_free(h); } };
struct free_buffer   { void operator()(void* h) const noexcept { fk_buffer_free(h); } };

/// A result, copied out and the library's own copy released. Nothing the library owns
/// outlives the call that produced it, which is the rule that makes the rest simple.
inline std::vector<std::uint8_t> take_bytes(void* handle) {
    detail::owned<free_buffer> held{handle};

    const std::uint8_t* data = fk_buffer_data(handle);
    const std::int32_t size = fk_buffer_size(handle);
    return data && size > 0 ? std::vector<std::uint8_t>(data, data + size)
                            : std::vector<std::uint8_t>{};
}

inline std::string take_text(void* handle) {
    detail::owned<free_buffer> held{handle};

    const std::uint8_t* data = fk_buffer_data(handle);
    const std::int32_t size = fk_buffer_size(handle);
    return data && size > 0 ? std::string(reinterpret_cast<const char*>(data), size)
                            : std::string{};
}

}  // namespace detail

/// What one document should look like.
///
/// Built into JSON rather than passed as a struct: the boundary takes it that way on
/// purpose, so a field added to the library does not break anything compiled against
/// this header. Writing the JSON here is six lines and no dependency.
struct options {
    enum class format { pdf, html, svg };

    format shape = format::pdf;
    bool archivable_pdf_a3b = false;
    bool tagged = false;
    bool subset_fonts = true;
    bool interactive_fields = true;
    bool bare_html = false;
    // Invariant to match the engine's own default; a narrow-space thousands
    // separator is a French convention, not a neutral one.
    bool french_numbers = false;

    std::string to_json() const {
        // A value outside the enum is refused rather than allowed to fall through to
        // the last branch of a chain of ternaries. It did fall through, to "svg", and
        // a caller asking for something impossible got a perfectly good SVG - which
        // is precisely the kind of quiet substitution the rest of this library exists
        // to avoid.
        const char* name = nullptr;
        switch (shape) {
            case format::pdf:  name = "pdf";  break;
            case format::html: name = "html"; break;
            case format::svg:  name = "svg";  break;
        }
        if (!name) throw error("unknown format");

        auto flag = [](bool value) { return value ? "true" : "false"; };

        std::string json = std::string("{\"format\":\"") + name + '"';
        if (archivable_pdf_a3b) json += ",\"conformance\":\"pdfa3b\"";

        json += std::string(",\"tagged\":") + flag(tagged)
              + ",\"subset\":" + flag(subset_fonts)
              + ",\"interactive\":" + flag(interactive_fields)
              + ",\"bare\":" + flag(bare_html)
              + ",\"numbers\":\"" + (french_numbers ? "french" : "invariant") + "\"}";
        return json;
    }
};

/// The faces a document may name. Nothing renders until it holds some.
class fonts {
public:
    fonts() {
        void* handle = nullptr;
        detail::check(fk_fonts_create(&handle));
        handle_ = detail::owned<detail::free_fonts>{handle};
    }

    /// Registers every font file in a directory; returns how many were taken.
    std::int32_t add_directory(const std::string& directory) {
        std::int32_t count = 0;
        detail::check(fk_fonts_add_directory(handle_.get(), directory.c_str(), &count));
        return count;
    }

    /// One font file, named on purpose. Unlike add_directory, a file that is not a
    /// font throws: this was asked about that file and no other.
    void add_file(const std::string& file) {
        detail::check(fk_fonts_add_file(handle_.get(), file.c_str()));
    }

    /// One font from its bytes, for a caller with no file. The bytes are copied.
    void add_memory(const void* data, std::size_t length) {
        detail::check(fk_fonts_add_memory(handle_.get(),
                                          static_cast<const std::uint8_t*>(data),
                                          static_cast<std::int32_t>(length)));
    }

    void add_memory(const std::vector<std::uint8_t>& data) {
        add_memory(data.data(), data.size());
    }

    /// The family for text that names none - overriding the one add_directory took from
    /// the directory's default-family.txt, or its first family. A family a template does
    /// name and the library does not hold is an error, never a fall back to this one.
    void set_default_family(const std::string& family) {
        detail::check(fk_fonts_set_default(handle_.get(), family.c_str()));
    }

    /// What the library holds, as JSON.
    std::string list() const {
        void* out = nullptr;
        detail::check(fk_fonts_list(handle_.get(), &out));
        return detail::take_text(out);
    }

    void* handle() const noexcept { return handle_.get(); }

private:
    detail::owned<detail::free_fonts> handle_;
};

/// A template: the document without the data in it.
class document {
public:
    static document from_file(const std::string& path) {
        void* handle = nullptr;
        detail::check(fk_template_load(path.c_str(), &handle));
        return document{handle};
    }

    static document from_json(const std::string& json) {
        void* handle = nullptr;
        detail::check(fk_template_parse(json.c_str(), &handle));
        return document{handle};
    }

    /// What is wrong with it, as a JSON array. "[]" means nothing to report.
    std::string check(bool accessible = true) const {
        void* out = nullptr;
        detail::check(fk_template_check(handle_.get(), accessible ? 1 : 0, &out));
        return detail::take_text(out);
    }

    /// One document, as bytes. `data` may be empty, in which case the template is
    /// rendered as it stands - which is how a template is looked at while it is
    /// being drawn.
    std::vector<std::uint8_t> render(const fonts& faces,
                                     const std::string& data = {},
                                     const options& how = {}) const {
        return detail::take_bytes(produce(faces, data, how));
    }

    /// The same, when what comes back is text: HTML, or the SVG envelope.
    std::string render_text(const fonts& faces,
                            const std::string& data = {},
                            const options& how = {}) const {
        return detail::take_text(produce(faces, data, how));
    }

private:
    explicit document(void* handle) : handle_(handle) {}

    void* produce(const fonts& faces, const std::string& data, const options& how) const {
        const std::string json = how.to_json();
        void* out = nullptr;

        // Empty and absent are not the same on the other side: one is a document
        // with no data, the other a request to render the template bare.
        detail::check(fk_render(handle_.get(), faces.handle(),
                                data.empty() ? nullptr : data.c_str(),
                                json.c_str(), nullptr, &out));
        return out;
    }

    detail::owned<detail::free_template> handle_;
};

inline std::string version() {
    const char* text = fk_version();
    return text ? text : std::string{};
}

}  // namespace frisket

#endif  // FRISKET_HPP
