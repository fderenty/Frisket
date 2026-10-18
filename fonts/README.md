# Frisket.Fonts

The typefaces [Frisket](https://www.nuget.org/packages/Frisket) draws with when it is given
none. Frisket depends on this package, so there is nothing to install: a `FontLibrary`
nobody fills uses these, and a first render needs no font configured.

| Family | Cuts |
|---|---|
| Mulish - the default, for text that names no family | Regular, Italic, Bold, Bold Italic |
| Elms Sans | Regular, Italic, Bold, Bold Italic |
| Noto Sans Display | Regular, Italic, Bold, Bold Italic |
| Glametrix | Regular, Bold, Light |

Register any font of your own and these step aside entirely - the two are never mixed, so
a family missing from your fonts is refused rather than found here.

`BundledFonts` lists what the assembly carries (`Files`, `Licences`, `DefaultFamily`) and
hands out a file's bytes (`Open`).

## Licence

Every family here is under the **SIL Open Font License 1.1**, which is the licence of this
package. Each family's licence text and copyright notice travel with it, inside the assembly
and under `licenses/<family>/OFL.txt` in the package. The OFL allows embedding the fonts,
whole or subset, in the documents you produce (OFL FAQ 1.12); it does not allow selling them
on their own. The files are unmodified.

Frisket itself is MIT: [github.com/fderenty/Frisket](https://github.com/fderenty/Frisket).
