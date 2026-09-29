#!/usr/bin/env python3
"""Sets two PDFs beside one another and says whether they are the same document.

    python tools/compare-pdf.py from-c.pdf from-cli.pdf

This exists because the check that matters for the C boundary is not a return code
but a document (CLAUDE.md 8) - and a plain `cmp` no longer answers it. The native
library links the system's zlib and the managed CLI carries its own, so the same
bytes come out compressed differently: identical content, different length. Read
with `cmp`, that looks exactly like a broken boundary.

So the streams are inflated before being compared, and the four things that are
allowed to differ - the timestamp, the file /ID, the stream lengths and the xref
offsets that follow from them - are neutralised rather than eyeballed.

Exit code 0 when the documents are the same, 1 when they are not.
"""
import re
import sys
import zlib


def streams(raw):
    """Every stream in the file, inflated where it was deflated."""
    out = []
    for m in re.finditer(rb"(\d+) 0 obj\r?\n(.*?)stream\r?\n", raw, re.S):
        start = m.end()
        end = raw.find(b"\nendstream", start)
        body = raw[start:end]
        if b"FlateDecode" in m.group(2):
            try:
                body = zlib.decompress(body)
            except zlib.error as why:
                body = b"<<undecodable: %s>>" % str(why).encode()
        out.append((int(m.group(1)), body))
    return out


def skeleton(raw):
    """The file with every stream and every incidental number taken out.

    What is left is the structure: the objects, their dictionaries and the way they
    point at one another - which is the half that a different compressor cannot
    change and a broken boundary cannot help changing.
    """
    raw = re.sub(rb"stream\r?\n.*?\nendstream", b"stream~endstream", raw, flags=re.S)
    raw = re.sub(rb"/Length \d+", b"/Length -", raw)
    raw = re.sub(rb"(/CreationDate|/ModDate) \(D:[^)]*\)", rb"\1 (D:-)", raw)
    raw = re.sub(rb"<[0-9A-Fa-f]{32}>", b"<ID>", raw)
    raw = re.sub(rb"\d{10} 00000 n", b"---------- 00000 n", raw)
    return re.sub(rb"startxref\s+\d+", b"startxref -", raw)


def main(left, right):
    a, b = open(left, "rb").read(), open(right, "rb").read()
    print(f"{left}: {len(a)} bytes")
    print(f"{right}: {len(b)} bytes")

    sa, sb = streams(a), streams(b)
    if len(sa) != len(sb):
        print(f"FAIL different number of streams: {len(sa)} and {len(sb)}")
        return 1

    wrong = [(n, len(x), len(y)) for (n, x), (_, y) in zip(sa, sb) if x != y]
    for number, one, other in wrong:
        print(f"FAIL object {number} differs once inflated: {one} and {other} bytes")
    print(f"{len(sa) - len(wrong)}/{len(sa)} streams identical once inflated")

    same = skeleton(a) == skeleton(b)
    print("structure identical once timestamp, /ID, lengths and xref are set aside:",
          "yes" if same else "no")

    if not same:
        ka, kb = skeleton(a), skeleton(b)
        at = next((i for i in range(min(len(ka), len(kb))) if ka[i] != kb[i]),
                  min(len(ka), len(kb)))
        print(f"  first difference at {at}")
        print("  left :", ka[max(0, at - 70):at + 70])
        print("  right:", kb[max(0, at - 70):at + 70])

    return 0 if same and not wrong else 1


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    sys.exit(main(sys.argv[1], sys.argv[2]))
