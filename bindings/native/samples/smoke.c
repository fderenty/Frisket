/*
 * Proves the boundary works from C, which is the only thing that proves it.
 *
 * Loads a template, renders it three ways, and writes the PDF out so it can be
 * compared with what the command line produces from the same inputs. A library that
 * loads and returns a status is not evidence; a document that matches is.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "../frisket.h"

static int failed = 0;

static void check(const char* what, int ok)
{
    printf("  %s %s\n", ok ? "ok  " : "FAIL", what);
    if (!ok) {
        const char* why = fk_last_error();
        if (why) printf("       %s\n", why);
        failed++;
    }
}

/* True when the text holds a byte no ASCII string could. */
static int HasHighByte(const char* text)
{
    const unsigned char* at = (const unsigned char*)text;
    for (; *at; ++at) {
        if (*at >= 0x80) return 1;
    }
    return 0;
}

static char* slurp(const char* path)
{
    FILE* f = fopen(path, "rb");
    long size;
    char* text;

    if (!f) return NULL;
    fseek(f, 0, SEEK_END);
    size = ftell(f);
    fseek(f, 0, SEEK_SET);

    text = (char*)malloc((size_t)size + 1);
    if (!text) { fclose(f); return NULL; }

    if (fread(text, 1, (size_t)size, f) != (size_t)size) { fclose(f); free(text); return NULL; }
    text[size] = 0;
    fclose(f);
    return text;
}

/* Like slurp, but keeps the length: a font is not a string and has 0 bytes in it. */
static unsigned char* slurpb(const char* path, long* size)
{
    FILE* f = fopen(path, "rb");
    unsigned char* bytes;

    if (!f) return NULL;
    fseek(f, 0, SEEK_END);
    *size = ftell(f);
    fseek(f, 0, SEEK_SET);

    bytes = (unsigned char*)malloc((size_t)*size);
    if (!bytes) { fclose(f); return NULL; }

    if (fread(bytes, 1, (size_t)*size, f) != (size_t)*size) { fclose(f); free(bytes); return NULL; }
    fclose(f);
    return bytes;
}

int main(int argc, char** argv)
{
    fk_fonts fonts = NULL;
    fk_template tmpl = NULL;
    fk_buffer out = NULL;
    int32_t taken = 0;
    char* data;
    const char* font_dir;
    const char* template_path;
    const char* data_path;
    const char* pdf_path;

    if (argc < 5) {
        printf("usage: smoke <font-dir> <template.json> <data.json> <out.pdf> [font-file]\n");
        return 2;
    }

    font_dir = argv[1];
    template_path = argv[2];
    data_path = argv[3];
    pdf_path = argv[4];

    printf("frisket %s\n", fk_version());

    check("a font library is made", fk_fonts_create(&fonts) == FK_OK);
    check("fonts are found", fk_fonts_add_directory(fonts, font_dir, &taken) == FK_OK && taken > 0);
    printf("       %d faces\n", taken);
    check("a default family is accepted", fk_fonts_set_default(fonts, "Mulish") == FK_OK);

    check("the library lists itself", fk_fonts_list(fonts, &out) == FK_OK);
    check("and the list is JSON", out && fk_buffer_size(out) > 2
                                  && fk_buffer_data(out)[0] == '[');
    fk_buffer_free(out);
    out = NULL;

    check("the template loads", fk_template_load(template_path, &tmpl) == FK_OK);

    /* Reported, not required. What the validator finds belongs to the template
       that was passed in, and a sample must work with whichever one that is. */
    check("the validator answers", fk_template_check(tmpl, 1, &out) == FK_OK
                                   && fk_buffer_data(out)[0] == '[');
    printf("       check -> %s\n", (const char*)fk_buffer_data(out));
    fk_buffer_free(out);
    out = NULL;

    data = slurp(data_path);
    check("the data file is readable", data != NULL);

    /* The crossing that matters. A wrapper that decodes the file into some local
       code page loses every character outside it, and nothing fails: the document
       comes out, slightly wrong, and only the bytes give it away. */
    check("and non-ASCII data survives being read", HasHighByte(data));

    /* --- the PDF ------------------------------------------------------- */
    check("a PDF comes out", fk_render(tmpl, fonts, data, "{\"format\":\"pdf\"}", NULL, &out) == FK_OK);
    check("and it is a PDF", out && fk_buffer_size(out) > 1000
                             && memcmp(fk_buffer_data(out), "%PDF-", 5) == 0);

    if (out) {
        FILE* f = fopen(pdf_path, "wb");
        if (f) {
            fwrite(fk_buffer_data(out), 1, (size_t)fk_buffer_size(out), f);
            fclose(f);
            printf("       %d bytes -> %s\n", fk_buffer_size(out), pdf_path);
        }
        fk_buffer_free(out);
        out = NULL;
    }

    /* --- the HTML, as a C string --------------------------------------- */
    check("HTML comes out", fk_render(tmpl, fonts, data, "{\"format\":\"html\"}", NULL, &out) == FK_OK);
    check("and reads as a C string", out
          && strstr((const char*)fk_buffer_data(out), "<svg") != NULL);
    fk_buffer_free(out);
    out = NULL;

    /* --- the SVG envelope ---------------------------------------------- */
    check("SVG comes out", fk_render(tmpl, fonts, data, "{\"format\":\"svg\"}", NULL, &out) == FK_OK);
    check("as an envelope with its pages", out
          && strstr((const char*)fk_buffer_data(out), "\"pages\":[") != NULL
          && strstr((const char*)fk_buffer_data(out), "\"pageWidth\":210") != NULL);
    fk_buffer_free(out);
    out = NULL;

    /* --- and the refusals, which matter as much ------------------------ */
    check("a misspelt option is refused",
          fk_render(tmpl, fonts, data, "{\"fromat\":\"pdf\"}", NULL, &out) != FK_OK);
    check("with something to read about it",
          fk_last_error() != NULL && strstr(fk_last_error(), "fromat") != NULL);

    check("a null template is refused", fk_render(NULL, fonts, data, NULL, NULL, &out) != FK_OK);
    check("an unknown format is refused",
          fk_render(tmpl, fonts, data, "{\"format\":\"ps\"}", NULL, &out) != FK_OK);

    /* ---- a font the caller chose, rather than a directory swept -------------
       Three doors, and the difference is the point. A scan steps over what is not
       a font because it was never asked about that file in particular; a font
       named on purpose has to be complained about, or somebody is left looking at
       a list theirs is not in with nothing to say why. Parsing is the check. */
    if (argc > 5) {
        long font_size = 0;
        unsigned char* font_bytes;
        fk_fonts picked = NULL;

        check("a second library is made", fk_fonts_create(&picked) == FK_OK);
        check("one font file, named on purpose",
              fk_fonts_add_file(picked, argv[5]) == FK_OK);

        font_bytes = slurpb(argv[5], &font_size);
        check("and the same font as bytes", font_bytes != NULL
              && fk_fonts_add_memory(picked, font_bytes, (int32_t)font_size) == FK_OK);
        free(font_bytes);

        /* What is not a font comes back as a failure, never as silence. The
           template is to hand and is certainly not one. */
        check("what is not a font is refused, not stepped over",
              fk_fonts_add_memory(picked, (const unsigned char*)data,
                                  (int32_t)strlen(data)) != FK_OK);
        check("and it says what it looked at", fk_last_error() != NULL);
        check("no bytes at all is refused too",
              fk_fonts_add_memory(picked, NULL, 0) != FK_OK);

        fk_fonts_free(picked);
    }

    /* ---- a render nobody wants any more -------------------------------------
       Tripped before the render rather than during it: a second thread would
       prove more, and needs a threading library this sample deliberately does
       without. What it does prove is the part that has no other witness - that a
       cancellation comes back as FK_CANCELLED and not as FK_FAILED. A caller
       retries a failure and does not retry a cancellation; folded together, the
       first timeout on a busy server becomes a storm. */
    {
        fk_token token = NULL;
        fk_buffer stopped = NULL;

        check("a token is made", fk_token_create(&token) == FK_OK);
        check("and tripping it is not an error", fk_cancel(token) == FK_OK);
        check("tripping it twice is not either", fk_cancel(token) == FK_OK);

        check("a cancelled render says so in its own words",
              fk_render(tmpl, fonts, data, "{\"format\":\"pdf\"}", token, &stopped) == FK_CANCELLED);
        check("and hands back nothing at all", stopped == NULL);

        fk_token_free(token);

        /* And a render given no token behaves exactly as it always did, which is
           what every existing caller passes. */
        check("no token means no cancellation",
              fk_render(tmpl, fonts, data, "{\"format\":\"pdf\"}", NULL, &out) == FK_OK);
        fk_buffer_free(out);
        out = NULL;
    }

    /* ---- a handle that was never one ----------------------------------------
       Guarded turns exceptions into codes, but a pointer that is not a handle is
       not an exception: GCHandle.FromIntPtr on it used to tear the process down
       where no catch could reach. If this program is still running when it prints,
       the boundary recognised the value instead of following it. */
    {
        fk_fonts bogus = (fk_fonts)(size_t)0xDEADBEEFu;
        check("a handle that is not one comes back as an error",
              fk_fonts_add_directory(bogus, font_dir, NULL) != FK_OK);
        check("and so does a null one",
              fk_fonts_add_directory(NULL, font_dir, NULL) != FK_OK);
        check("while freeing one twice is survivable", 1);
        fk_fonts_free(bogus);
    }

    free(data);
    fk_template_free(tmpl);
    fk_fonts_free(fonts);

    printf(failed ? "\n%d FAILED\n" : "\nall good\n", failed);
    return failed ? 1 : 0;
}
