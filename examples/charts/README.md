# Drawing a chart for a document

Frisket does not draw charts. The application that has the numbers draws one as an SVG file and
hands it over, and Frisket writes it into the PDF as vector marks and real text: sharp at any
size, light, and with labels that can be selected and searched.

[attendance.svg](attendance.svg) is a complete example: two curves, two vertical scales, a grid, a
clipped plot area, turned axis titles and a legend.

## Handing it over

A chart is a picture, so it goes in an `ImageItem`. The template stays the same from one document
to the next and the data brings the chart of the day, as base64 or as a `data:` URI:

```json
{ "$type": "ImageItem", "SourceField": "Charts.Attendance",
  "AltText": "Visitors rose from 300 in January to 1,500 in July, then fell back to 750." }
```

```json
{ "Charts": { "Attendance": "data:image/svg+xml;base64,PD94bWwg..." } }
```

- **Size.** The width comes from the column or from the item's own `Location`. The height follows
  from the drawing's `viewBox`, so `NaturalWidth` and `NaturalHeight` are only needed to choose other
  proportions. `Scaling` and `Style.AlignHorizontal` behave as for any picture.
- **`AltText` is what a screen reader reads.** The drawing is one figure: its labels are real text
  for selecting and searching, but a reader announces the alternative text, so that is where the
  message of the chart belongs.
- **Labels.** `font-size` is what SVG says it is, the em in the drawing's own units, not the cap
  height a size means elsewhere in a template. They scale with the drawing: a `viewBox` 800 wide
  placed 170 mm wide draws 14 units as about 3 mm of em.
- **Fonts.** The first name in `font-family` is the one asked for, and the library must hold it. A
  generic name (`sans-serif`, `serif`, `monospace`...) or no `font-family` at all means the document's
  own: the template's `DefaultFont`, then the library's default. A family the library does not hold
  is refused, as it is anywhere else in a template: another face would change every width.
- **Mail.** A mail reader does not draw SVG and Frisket cannot make a bitmap of one, so
  `--format mail` writes the alternative text in its place. The rule `can.drawingInFlow` says so
  beforehand. If the reader should see the chart, send a PNG in that document's data.

## Checking what you generate

Everything outside the lists below is refused by name, never skipped. A chart with a piece missing
and nobody told is worse than one that does not print. Ask from your own tests:

```csharp
IReadOnlyList<TemplateIssue> found = SvgDrawing.Check(svg, fonts);   // empty: it will draw
```

The same words come back from the PDF and from the exact preview if the file gets that far.

Without C#, or to ask of a whole document, hand over the template and the data together. Each
refused chart comes back with a JSON Pointer to the `ImageItem` that names it:

```bash
frisket check -t report.json -d case-417.json --json
```

```csharp
IReadOnlyList<TemplateIssue> found = TemplateData.Check(template, dataJson);
```

`POST /check` on the preview server takes the same as a `data` field.

## What is taken

Elements:

| element | what it is |
|---|---|
| `svg` | the root, which must say its size: a `viewBox`, or a `width` and `height` in plain lengths |
| `g` | a group, passing its properties down |
| `path` | moves, lines, cubics, quadratics and arcs, absolute and relative: `M L H V C S Q T A Z` |
| `rect` | with rounded corners through `rx` and `ry` |
| `circle`, `ellipse`, `line`, `polyline`, `polygon` | the basic shapes |
| `text`, `tspan` | labels, with `x`, `y` and one `dx` and `dy`, which may be in `em` |
| `clipPath` | one shape, usually in `defs`, named by `clip-path="url(#id)"` |
| `defs` | holds clipPaths and nothing else |
| `title`, `desc`, `metadata` | not drawn, and not looked into |

Properties, as attributes or in a `style` attribute, where `style` wins:

| property | values |
|---|---|
| `fill`, `stroke` | `none`, `transparent`, `currentColor`, `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, `rgb()`, `rgba()`, a CSS colour name |
| `color` | the same colours, for `currentColor` to read |
| `fill-opacity`, `stroke-opacity`, `opacity` | a number or a percentage |
| `fill-rule`, `clip-rule` | `nonzero`, `evenodd` |
| `stroke-width`, `stroke-dashoffset` | a length |
| `stroke-dasharray` | `none`, or plain lengths |
| `stroke-linecap` | `butt`, `round`, `square` |
| `stroke-linejoin` | `miter`, `round`, `bevel` |
| `stroke-miterlimit` | a number of one or more |
| `clip-path` | `none`, `url(#id)` |
| `font-family`, `font-size` | see above; a size is a length |
| `font-weight` | `normal`, `bold`, or a number, where 600 and up is bold |
| `font-style` | `normal`, `italic`, `oblique` |
| `text-anchor` | `start`, `middle`, `end` |
| `display` | `inline`, `block`, `none` |
| `visibility` | `visible`, `hidden`, `collapse` |

The attribute `transform` takes `matrix`, `translate`, `scale`, `rotate`, `skewX` and `skewY`, on
anything but a `tspan`. A length is a number, or a number in `px`, `pt`, `pc`, `mm`, `cm` or `in`.
`xml:space="preserve"` keeps the spaces of a text and is passed down.

These change nothing in the picture and are let through: `id`, `class`, `lang`, `tabindex`, `role`,
`focusable`, `xmlns`, `shape-rendering`, `text-rendering`, `color-rendering`, `image-rendering`,
`color-interpolation`, `pointer-events`, `cursor`, `font-kerning`, `font-variant-ligatures`,
anything beginning `aria-` or `data-`, and any attribute in a namespace of its own, as an editor
writes. On the root, `width`, `height`, `x`, `y`, `preserveAspectRatio`, `version` and `baseProfile`
are read for the size at most.

## What is not, and what to write instead

| refused | instead |
|---|---|
| `dominant-baseline`, `alignment-baseline` | `dy="0.35em"` centres a label on its position |
| a `<style>` sheet, and so any rule for `class` | the properties on the elements |
| `<use>`, `<symbol>`, `<marker>` | the shape written out |
| gradients and patterns | a plain colour |
| `<image>` | an `ImageItem` beside the drawing |
| a stroke on a text | `stroke="none"` on it or on its group |
| `<script>`, `<foreignObject>`, event attributes | nothing: a document is not a page |
| `%` and `em` in a length, but for `dx` and `dy` | a plain length in the drawing's units |

PNG and JPEG need none of this: a PNG may have an alpha channel, which the PDF keeps as a soft mask,
and a palette, interlacing or 16 bits to a channel are refused with a word on what to save instead.

A `DOCTYPE` naming a DTD is passed over, and nothing is ever fetched. One with an internal subset is
refused, as is any entity other than the five XML defines and numbered characters.

An `opacity` on a group is multiplied down to what it holds, in the PDF and in the proof alike. That
differs from a browser only where the group's own children overlap.
