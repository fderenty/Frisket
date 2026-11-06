# Third-party notices

Frisket's own code is under the MIT licence in [LICENSE](LICENSE). What follows is not: the
typefaces, under the SIL Open Font License, and ZopfliSharp, under the Apache License 2.0,
which the command line, the native library and the preview server carry and the engine does
not.

## Typefaces

The typefaces under [`engine/Fonts`](engine/Fonts) are **not** covered by that licence.
Each one carries its own licence text and copyright notice in its own folder, and those
must travel with it.

```
  SIL Open Font License 1.1        (see OFL.txt in each folder)
    Elms Sans          Copyright 2025, The Elms Sans Project Authors
    Glametrix          Copyright (c) 2012-2013, gluk (www.glukfonts.pl)
                       Reserved Font Name: Glametrix
                       licence text at misc/OFL_License-61e0.txt
    Mulish             Copyright 2016, The Mulish Project Authors
    Noto Sans Display  Copyright 2015, Google Inc.
                       Noto is a trademark of Google Inc.
```

All of these permit redistribution, and all permit embedding a font in a document, which
is what this engine does when it subsets a face into a PDF. None permits selling the fonts
on their own.

### In the `Frisket.Fonts` package

[`fonts/Frisket.Fonts.csproj`](fonts/Frisket.Fonts.csproj) embeds four of those families in
an assembly, and the `Frisket` package depends on it, so anything built on the engine
carries them. The package is licensed `OFL-1.1` as a whole, and holds each family's licence
text under `licenses/<family>/OFL.txt` as well as inside the assembly, as the OFL asks of
anything that bundles a font with software (OFL FAQ 1.20). The software around them stays
MIT (FAQ 1.3).

```
  Mulish              Regular, Italic, Bold, Bold Italic      the default
  Elms Sans           Regular, Italic, Bold, Bold Italic      four of its eighteen cuts
  Noto Sans Display   Regular, Italic, Bold, Bold Italic
  Glametrix           Regular, Bold, Light
```

The files are not modified: test 94 compares every embedded face, byte for byte, with the
one in `engine/Fonts`. A document made with them carries a subset of the face, which the
OFL allows without the licence text travelling into the document (FAQ 1.12).

The test suite copies Mulish into `tests/output/fonts-clash`, to prove that two files
claiming one name are reported rather than one hiding the other. It copies Mulish's
`OFL.txt` beside it, so that copy travels with its licence like the original.

A font that cannot produce its own licence text does not go into `engine/Fonts`. A label on
a download page is not a licence: one font already said "freeware" there while its own
licence file forbade redistribution in as many words.

## ZopfliSharp

The command line, the native library and the preview server can deflate a PDF with Zopfli,
through [ZopfliSharp](https://github.com/fderenty/ZopfliSharp), which each of them carries
compiled in: the command line and the native library are linked ahead of time, so it is inside
the one binary of each release archive. Its version is named once, in
[`ZopfliSharp.props`](ZopfliSharp.props). It is under the **Apache License 2.0**, not MIT.

The engine does not carry it, and neither do the `Frisket`, `Frisket.Fonts` and
`Frisket.Import` packages: the engine takes an encoder through `PdfBackend.Compressor` and
names none, so a program built on it carries ZopfliSharp only if it references it itself.

Apache-2.0 asks two things of anything that redistributes the work (section 4): a copy of
the licence, and the content of its NOTICE file. Both travel beside each binary, in
`licenses/zopflisharp/LICENSE` and `licenses/zopflisharp/NOTICE`, copied out of the package
by [`ZopfliSharp.props`](ZopfliSharp.props), and the NOTICE reads:

```
ZopfliSharp
Copyright 2026 Frédéric Derenty

This product is a translation to C# of Zopfli, the compression library by Google:
https://github.com/google/zopfli

  Zopfli Compression Algorithm
  Copyright 2011-2016 Google Inc. All Rights Reserved.
  Licensed under the Apache License, Version 2.0.

  Zopfli was created by Lode Vandevenne and Jyrki Alakuijala, based on an
  algorithm by Jyrki Alakuijala. Contributors to the original: Mark Adler,
  Jyrki Alakuijala, Frédéric Kayser, Jeffrey Lim, Daniel Reed, Huzaifa
  Sidhpurwala, Péter Szabó, Lode Vandevenne, Derek Buitenhuis.

Changes from the original: the C sources under src/zopfli were translated to
C#, keeping their structure so that the output stays byte-for-byte identical.
Each translated file names the C file it comes from. Spreading one compression
over several threads is an addition of this version.
```

The licence itself: <https://www.apache.org/licenses/LICENSE-2.0>.
