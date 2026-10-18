#!/usr/bin/env bash
# The designer's echo against the engine, and the content editor, over every face - the
# parts as a developer opens them and the single files under release/ as they ship.
# Each harness reads what the suite wrote into tests/output, so this runs after it.
set -euo pipefail

o=tests/output
fixtures=(
  "$o/acme/acme.bound.json" "$o/acme/acme.bands.json" "$o/acme/acme.template.json"
  "$o/quote/quote.template.json"
  "$o/validator/faulty.template.json" "$o/validator/faulty.issues.json"
  "$o/validator/flattened.template.json" "$o/validator/flattened.issues.json"
)

for page in apps/designer/frisket-designer.html release/frisket-designer.standalone.html; do
  echo "::group::designer against the engine: $page"
  node apps/designer/check-against-engine.js "$page" "${fixtures[@]}"
  echo "::endgroup::"
done

for page in frisket-editor.html frisket-editor-full.html release/frisket-editor.standalone.html; do
  echo "::group::content editor: $page"
  node apps/designer/check-editor.js "$page"
  echo "::endgroup::"
done
