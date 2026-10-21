/*
 * Frisket - a report engine behind a flat C interface.
 *
 * Everything crosses as a null-terminated UTF-8 string, an opaque handle, or a status
 * code. No struct is passed by value and no callback is taken, because those are the
 * two things that make a foreign-function binding hard to write and easy to get subtly
 * wrong. Options travel as JSON, which the engine speaks anyway and which can gain a
 * field without breaking a caller compiled last year.
 *
 * Errors: every function returning fk_status leaves a message behind on failure. Fetch
 * it with fk_last_error() before calling anything else on the same thread.
 *
 * Ownership: whatever the library hands back through an out parameter is yours to free,
 * with the matching er_*_free. Nothing else is.
 *
 * Threads: handles are not synchronised. Use one set per thread, or lock around them.
 * The error message is per thread already.
 */

#ifndef FRISKET_H
#define FRISKET_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#  define FK_CALL __cdecl
#else
#  define FK_CALL
#endif

typedef int32_t fk_status;

#define FK_OK        0
#define FK_ARGUMENT  1   /* something you passed was missing or wrong */
#define FK_FAILED    2   /* the engine refused; fk_last_error says why */
#define FK_CANCELLED 3   /* you asked it to stop, and it did - not a failure */

/* Opaque. Never dereference one. */
typedef void* fk_fonts;
typedef void* fk_template;
typedef void* fk_buffer;
typedef void* fk_token;

/* ---------------------------------------------------------------- diagnostics */

/* The last failure on this thread, UTF-8, or NULL. Valid until the next call. */
const char* FK_CALL fk_last_error(void);

/* The library's own version - "1.2.3", or "1.2.3-dev" for a build that is not a
   release. Yours to free? No - it is a static string. */
const char* FK_CALL fk_version(void);

/* --------------------------------------------------------------------- fonts */

/* An empty library. Left empty, it draws with the typefaces bundled into this
   library - Mulish for text that names no family. The first face added makes them
   step aside entirely; the two are never mixed. */
fk_status FK_CALL fk_fonts_create(fk_fonts* out);

/* Registers every font file in a directory. `count` may be NULL.
   A file that is not a font is stepped over: this was not asked about it. */
fk_status FK_CALL fk_fonts_add_directory(fk_fonts fonts, const char* directory, int32_t* count);

/* One font file, named on purpose - so a failure is returned rather than stepped
   over, which is the whole difference from the call above. */
fk_status FK_CALL fk_fonts_add_file(fk_fonts fonts, const char* file);

/* One font from its bytes, for a caller with no file: posted over HTTP, held in a
   database, picked in a browser. The bytes are copied, so they may be freed on return.

   Parsing is the check. Reading the header would only prove the first twelve bytes
   look like a font; getting FK_OK back here means the tables are there, the cmap
   reads, and the 'H' can be measured - which is what the engine will need.

   Whether the font may lawfully be embedded is not asked: it is your font and your
   decision. */
fk_status FK_CALL fk_fonts_add_memory(fk_fonts fonts, const uint8_t* data, int32_t length);

/* The family for text that names none. fk_fonts_add_directory sets it from the
   directory's default-family.txt when there is one, and otherwise to the first family it
   registers; this overrides either. A family a template does name and the library does
   not hold is an error, never a fall back to this one: another face would move every
   line break. */
fk_status FK_CALL fk_fonts_set_default(fk_fonts fonts, const char* family);

/* ------------------------------------------------------------- cancellation */

/* A token one thread gives to a render and another trips.

   Its own handle rather than a flag on the template or the font library: a token
   belongs to one operation, and hung on a template, cancelling one render would
   cancel the next render that happened to use it.

   This is what makes cancellation possible across a synchronous boundary at all.
   fk_render does not return until it is done, so the word has to come from
   somewhere else - fk_cancel, from another thread, at any time.

   A cancelled render produces nothing and returns FK_CANCELLED. It is not
   FK_FAILED, and the difference is worth honouring: a caller retries a failure and
   does not retry a cancellation. */
fk_status FK_CALL fk_token_create(fk_token* out);
fk_status FK_CALL fk_cancel(fk_token token);
void      FK_CALL fk_token_free(fk_token token);

/* What the library holds, as a JSON array. Free with fk_buffer_free. */
fk_status FK_CALL fk_fonts_list(fk_fonts fonts, fk_buffer* out);

void FK_CALL fk_fonts_free(fk_fonts fonts);

/* ----------------------------------------------------------------- templates */

fk_status FK_CALL fk_template_parse(const char* json, fk_template* out);
fk_status FK_CALL fk_template_load(const char* path, fk_template* out);

/*
 * What is wrong with the template, as a JSON array of
 * { rule, level, where, message }. Empty means nothing to report. Pass accessible
 * non-zero to include what a screen reader needs on top of plain soundness.
 */
fk_status FK_CALL fk_template_check(fk_template tmpl, int32_t accessible, fk_buffer* out);

void FK_CALL fk_template_free(fk_template tmpl);

/* -------------------------------------------------------------------- render */

/*
 * Produces one document.
 *
 *   data_json     the record to fill the template with, or NULL to render it bare
 *   options_json  a JSON object, or NULL for the defaults:
 *
 *     { "format":      "pdf" | "html" | "svg",     default "pdf"
 *       "conformance": "none" | "pdfa3b",          default "none"
 *       "tagged":      true | false,               default false
 *       "subset":      true | false,               default true
 *       "interactive": true | false,               default true
 *       "bare":        true | false,               default false   (html)
 *       "numbers":     "french" | "invariant" }    default "invariant"
 *
 * The result is bytes for a PDF, UTF-8 text for HTML, and for SVG a JSON envelope -
 * { pageWidth, pageHeight, fontCss, pages: [...] } - since a set of pages is not one
 * file. Every result carries a trailing zero as well as a size, so a text one can be
 * used as a C string without copying it.
 *
 * An unknown option is refused rather than ignored.
 */
fk_status FK_CALL fk_render(fk_template tmpl, fk_fonts fonts,
                            const char* data_json, const char* options_json,
                            fk_token token, fk_buffer* out);

/* ------------------------------------------------------------------- buffers */

const uint8_t* FK_CALL fk_buffer_data(fk_buffer buffer);
int32_t        FK_CALL fk_buffer_size(fk_buffer buffer);
void           FK_CALL fk_buffer_free(fk_buffer buffer);

#ifdef __cplusplus
}
#endif

#endif /* FRISKET_H */
