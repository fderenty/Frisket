"""Frisket from Python, over the flat C interface.

Loads the shared library by name and calls it - no build step, no extension module.
It is also the cheapest check that the boundary is callable by *late* binding, which
is what every other wrapper does: Pascal's `external`, Java's FFM, Node's ffi. If
this works, the shape is right for all of them.
"""
import ctypes
import json
import os
import sys


class FrisketError(RuntimeError):
    pass


def _library_name():
    if sys.platform == "win32":
        return "frisket.dll"
    if sys.platform == "darwin":
        return "libfrisket.dylib"
    return "libfrisket.so"


class _Api:
    """The entry points, with their argument shapes declared once."""

    def __init__(self, path=None):
        self.lib = ctypes.CDLL(path or _library_name())
        c = ctypes.c_void_p
        p = ctypes.c_char_p
        i = ctypes.c_int32

        def declare(name, restype, argtypes):
            fn = getattr(self.lib, name)
            fn.restype = restype
            fn.argtypes = argtypes
            return fn

        self.last_error = declare("fk_last_error", p, [])
        self.version = declare("fk_version", p, [])

        self.fonts_create = declare("fk_fonts_create", i, [ctypes.POINTER(c)])
        self.fonts_add = declare("fk_fonts_add_directory", i, [c, p, ctypes.POINTER(i)])
        self.fonts_add_file = declare("fk_fonts_add_file", i, [c, p])
        self.fonts_add_memory = declare("fk_fonts_add_memory", i, [c, ctypes.c_char_p, i])
        self.fonts_default = declare("fk_fonts_set_default", i, [c, p])
        self.fonts_list = declare("fk_fonts_list", i, [c, ctypes.POINTER(c)])
        self.fonts_free = declare("fk_fonts_free", None, [c])

        self.template_parse = declare("fk_template_parse", i, [p, ctypes.POINTER(c)])
        self.template_load = declare("fk_template_load", i, [p, ctypes.POINTER(c)])
        self.template_check = declare("fk_template_check", i, [c, i, ctypes.POINTER(c)])
        self.template_free = declare("fk_template_free", None, [c])

        self.render = declare("fk_render", i, [c, c, p, p, c, ctypes.POINTER(c)])
        self.token_create = declare("fk_token_create", i, [ctypes.POINTER(c)])
        self.token_cancel = declare("fk_cancel", i, [c])
        self.token_free = declare("fk_token_free", None, [c])

        self.buffer_data = declare("fk_buffer_data", ctypes.POINTER(ctypes.c_uint8), [c])
        self.buffer_size = declare("fk_buffer_size", i, [c])
        self.buffer_free = declare("fk_buffer_free", None, [c])

    def check(self, status):
        if status != 0:
            message = self.last_error()
            raise FrisketError(message.decode("utf-8") if message else "unknown failure")

    def take(self, handle):
        """Copies a buffer out and frees it. Nothing of ours outlives the call."""
        size = self.buffer_size(handle)
        data = ctypes.string_at(self.buffer_data(handle), size)
        self.buffer_free(handle)
        return data


def _utf8(value):
    return None if value is None else value.encode("utf-8")


class Fonts:
    def __init__(self, api, directories=(), default=None):
        self._api = api
        self._handle = ctypes.c_void_p()
        api.check(api.fonts_create(ctypes.byref(self._handle)))

        for directory in directories:
            self.add(directory)
        if default:
            api.check(api.fonts_default(self._handle, _utf8(default)))

    def add(self, directory):
        count = ctypes.c_int32()
        self._api.check(self._api.fonts_add(self._handle, _utf8(directory), ctypes.byref(count)))
        return count.value

    def add_file(self, path):
        """One font file, named on purpose. Unlike add(), a file that is not a font
        raises: this was asked about that file and no other."""
        self._api.check(self._api.fonts_add_file(self._handle, _utf8(path)))

    def add_bytes(self, data):
        """One font from its bytes - posted over HTTP, read from a database, picked in
        a browser. Parsing is the check; getting back without raising means the tables
        are there, the cmap reads and the 'H' can be measured."""
        self._api.check(self._api.fonts_add_memory(self._handle, bytes(data), len(data)))

    def list(self):
        out = ctypes.c_void_p()
        self._api.check(self._api.fonts_list(self._handle, ctypes.byref(out)))
        return json.loads(self._api.take(out))

    @property
    def handle(self):
        return self._handle

    def close(self):
        if self._handle:
            self._api.fonts_free(self._handle)
            self._handle = ctypes.c_void_p()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()


class Template:
    def __init__(self, api, path=None, json_text=None):
        self._api = api
        self._handle = ctypes.c_void_p()

        if path is not None:
            api.check(api.template_load(_utf8(path), ctypes.byref(self._handle)))
        elif json_text is not None:
            api.check(api.template_parse(_utf8(json_text), ctypes.byref(self._handle)))
        else:
            raise ValueError("a template needs a path or some JSON")

    def check(self, accessible=True):
        out = ctypes.c_void_p()
        self._api.check(self._api.template_check(self._handle, 1 if accessible else 0,
                                                 ctypes.byref(out)))
        return json.loads(self._api.take(out))

    def render(self, fonts, data=None, token=None, **options):
        """One section. `data` is a dict or a JSON string; options are the C ones.

        `token` is a Token another thread may cancel; the render then raises rather
        than handing back half a document."""
        if isinstance(data, (dict, list)):
            data = json.dumps(data, ensure_ascii=False)

        out = ctypes.c_void_p()
        self._api.check(self._api.render(self._handle, fonts.handle, _utf8(data),
                                         _utf8(json.dumps(options) if options else None),
                                         token.handle if token else None,
                                         ctypes.byref(out)))
        produced = self._api.take(out)

        # PDF is bytes; the other two are text, and SVG is text that happens to be an
        # envelope. Decoding here saves every caller the same two lines.
        if options.get("format", "pdf") == "pdf":
            return produced
        text = produced.decode("utf-8")
        return json.loads(text) if options.get("format") == "svg" else text

    def close(self):
        if self._handle:
            self._api.template_free(self._handle)
            self._handle = ctypes.c_void_p()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()


class Token:
    """Something one thread hands to a render and another trips.

    Its own object rather than a flag on the template, because it belongs to one
    operation: hung on a template, cancelling one render would cancel the next."""

    def __init__(self, api):
        self._api = api
        self._handle = ctypes.c_void_p()
        api.check(api.token_create(ctypes.byref(self._handle)))

    def cancel(self):
        self._api.check(self._api.token_cancel(self._handle))

    @property
    def handle(self):
        return self._handle

    def close(self):
        if self._handle:
            self._api.token_free(self._handle)
            self._handle = None

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()


class Frisket:
    """The library itself. One per process is plenty."""

    def __init__(self, path=None):
        self._api = _Api(path)

    @property
    def version(self):
        return self._api.version().decode("utf-8")

    def fonts(self, directories=(), default=None):
        return Fonts(self._api, directories, default)

    def template(self, path=None, json_text=None):
        return Template(self._api, path, json_text)

    def token(self):
        return Token(self._api)
