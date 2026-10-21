# Letting a model write templates

Frisket does not call a model. Which one, with what key, how many attempts and who approves
the result are the host's decisions. What Frisket provides is a format a model can write and
a set of checks a program can act on: the model writes a template, the checks say what is
wrong and exactly where, the model corrects it, and the engine's preview shows the result.

`tests/WrittenByAModel.cs` (test 97) runs that loop end to end. It writes a template with
the mistakes a model typically makes, applies the corrections by following the pointers the
checks return, checks again and composes the document. Its material is left in
`tests/output/model/`:

| file | what it is |
|---|---|
| `written.template.json` | the template as written, with its mistakes |
| `fields.json` | the host's catalogue of fields |
| `findings.json` | what the checks returned, in the shape a program reads |
| `mended.template.json` | the template once corrected at the pointers |

## What to give the model

1. **The schema**, `examples/schema/frisket-template.schema.json`. It covers every type
   and property, each with a description taken from the engine's own documentation
   comments, and it declares `additionalProperties: false`, so a misspelt property is
   rejected rather than skipped.
2. **The host's catalogue of fields**: what the data will hold when the document is
   printed. This is the same array the content editor is given as `fields`:

   ```json
   [ { "path": "Destinataire.Nom", "label": "Nom du destinataire" },
     { "path": "Cabinet.Logo", "type": "image" },
     { "path": "Lignes", "type": "list", "fields": [ { "path": "Ref" }, { "path": "Montant" } ] } ]
   ```

   Values arrive already formatted by the host. A template prints `{Montant}` exactly as the
   data spells it, so it never needs a number format.
3. **A few templates that work.** `tests/output/model/mended.template.json` is short, and
   `examples/showcase.json` uses every kind of element.
4. **What the schema cannot say**, in a sentence or two:
   - every section needs a `Dimension` in millimetres (210 by 297 for A4); nothing supplies one;
   - the report needs a `Title` and a `Language`, and every picture an `AltText`;
   - text goes in a `Body` whose `Layout` is `Stack`;
   - a token is a path in braces, such as `{Destinataire.Nom}`;
   - a table repeats over a list with `RepeatOver`: a header row with `IsHeader: true`,
     then one row naming the fields of an entry.

## The loop

Ask for a template, then check it. The checks are available in three ways, and all three
return the same thing:

- **HTTP**: `POST /check` with `{ "template": "...", "fields": [...] }`. `apps/server/Program.cs`
  shows the route, and a host's own server follows it with its own `ISubTemplates`.
- **Command line**: `frisket check -t template.json --fields fields.json --json`.
- **C#**: `TemplateJson.Check(text)`, then `TemplateValidator.Check(report)` and
  `TemplateFields.Check(report, catalogue)`, and `TemplateIssue.ToJson(...)` to write the
  result out.

Each check returns an array of findings:

```json
{
  "rule": "read.unknownProperty",
  "level": "warning",
  "message": "\"Txt\" is not something a TextItem carries, so it is ignored ... Did you mean \"Text\"?",
  "where": "TextItem › Txt",
  "pointer": "/Sections/0/Childs/0/Childs/0/Txt"
}
```

`pointer` is a JSON Pointer (RFC 6901) to the place to change. Give the findings back to the
model and ask it to change only what each pointer names. A model that rewrites the whole
template tends to fix one thing and break another. Repeat until there are no errors left;
warnings are for a person to judge. Then preview the template with `POST /preview` and show
it to someone before it is used.

The rules come in four families:

| prefix | read from | example |
|---|---|---|
| `read.` | the template's text | a property the model does not know; a text that cannot be read |
| `sound.` | the template | a section with no page size; a table whose rows disagree |
| `ua.` `can.` | the template, for a reader and for the output aimed at | a picture with no alternative text |
| `fields.` | the template, against the catalogue | `{Destinataire.Nmo}` where `Destinataire.Nom` was meant |

`read.` and `fields.` exist only on the engine's side. The template designer and the content
editor read an object rather than text, and only the host knows its catalogue. The other
three families are also checked in the designer, by an echo of the validator held to the
engine's findings.
