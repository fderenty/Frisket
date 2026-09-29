# The template schema

Two faces of one model, both generated from it:

| file | for |
|---|---|
| `frisket-template.schema.json` | an editor, and anything that checks a file as it is typed |
| `frisket-template.reference.html` | a person, and anything asked to write a template |

The page answers what the schema structurally cannot. A token lives *inside* a string,
so the schema sees `"Text": { "type": "string" }` and stops there - no reader could
discover `{Page}` or `{HeadingNumber}` from it. And the schema says `Kind: "Signature"`
is legal without saying what a signature field produces or loses. Open the page for
what a thing does; point an editor at the schema for whether a file is shaped right.


`frisket-template.schema.json` — the shape of a template, as a JSON Schema
(draft 2020-12).

**Generated, never written.** It is produced from the model by reflection, in
`tests/TemplateSchema.cs`, and rewritten by test 47. A schema typed out beside the model
would be a sixth place to keep in step — see the table at the top of `CLAUDE.md` — and
it would drift within the week, silently, because nothing would compare the two.

## Using it

Point a template at it and an editor completes and checks as you type:

```json
{
  "$schema": "schema/frisket-template.schema.json",
  "Title": "Invoice {InvoiceNumber}",
  "Sections": [ { "Childs": [ ... ] } ]
}
```

This example said `"Section": { … }` until 24 August 2026. `Report.Section` is
`[JsonIgnore]` — it names the first of the list and is never written — so the key does
not exist in a template at all, and an editor pointed at the schema would have refused
it. A reader copying it got a document with no section and no complaint. The keys here
are checked against the schema by test 47 now, which is the only reason to trust them.

VS Code does this out of the box. For a model asked to produce a template, the schema
is the answer to "what may go here": every element type, every enumeration and its
exact spellings, and the discriminator that tells one kind from another.

## What it carries

| | |
|---|---|
| 33 definitions | every type reachable from `Report` |
| `$type` | pinned per kind with `const`, so a `oneOf` resolves to one branch |
| enumerations | as their names, which is what the file holds |
| serialization names | `LeftValue`, not the C# `Left` behind it |
| `additionalProperties: false` | a misspelt property is a mistake, not a silent no-op |
| numbers | `number` **or** the string `"NaN"`, which is how the model says undecided |

Nothing is `required`: a template writes only what it decides, so every property may
be absent.

## What it does not carry, and cannot

A schema describes shape. It cannot say that a figure needs alternative text, that a
table's rows must agree in width, or that columns asked of a container which places
its children by coordinates will do nothing at all. Those are
`TemplateValidator`'s — in the engine, in the designer, and in `frisket check` —
and the schema's own `description` says so rather than looking complete.

**A template this schema accepts may still have things to report.**

## Keeping it honest

Test 47 does three things beyond generating it:

- it checks the newest properties are in it — today `ColumnCount`, `ColumnGap`,
  `BalanceColumns`, `ShowWhen` — which is the plainest evidence it was read off the
  model and not off a memory of it;
- it walks a **real bound template** — 881 objects — and fails if any property is one
  the schema does not declare;
- it fails if the committed file is not what the model now produces.
