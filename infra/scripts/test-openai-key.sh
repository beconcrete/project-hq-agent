#!/usr/bin/env bash
# Replicates the contract extraction pipeline locally against nda.pdf.
# Steps: extract PDF text (Python) → triage → extract fields
#
# Run: bash infra/scripts/test-openai-key.sh

set -euo pipefail

API_KEY="${OPENAI_API_KEY:-}"
if [ -z "$API_KEY" ]; then
  echo "Error: OPENAI_API_KEY environment variable is not set." >&2
  exit 1
fi
PDF_PATH="$(dirname "$0")/../test/nda.pdf"

PDF_B64=$(base64 -i "$PDF_PATH")

echo "=== Step 1: Extract PDF text via OpenAI file content ==="
STEP1_RAW=$(curl -s https://api.openai.com/v1/chat/completions \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d "$(jq -n --arg pdf "$PDF_B64" '{
    model: "gpt-4.1-mini",
    max_tokens: 8192,
    messages: [{
      role: "user",
      content: [
        { type: "file", file: { filename: "document.pdf", file_data: ("data:application/pdf;base64," + $pdf) } },
        { type: "text", text: "Extract all text content from this document verbatim, preserving structure. No commentary." }
      ]
    }]
  }')")

echo "Raw response:"
echo "$STEP1_RAW" | jq '{error: .error, chars: (.choices[0].message.content | length), preview: .choices[0].message.content[:200]}'

EXTRACTED_TEXT=$(echo "$STEP1_RAW" | jq -r '.choices[0].message.content // ""')
echo "Extracted ${#EXTRACTED_TEXT} characters."
echo ""

TMPDIR=$(mktemp -d)
echo "$EXTRACTED_TEXT" > "$TMPDIR/text.txt"

echo "=== Step 2: Triage ==="
TRIAGE_PAYLOAD=$(jq -n --rawfile text "$TMPDIR/text.txt" '{
  model: "gpt-4.1-mini",
  max_tokens: 256,
  messages: [
    {
      role: "system",
      content: "You are a contract classification specialist. Identify the type of legal document with a free-text description (e.g. \"Non-Disclosure Agreement\", \"Software Licence\"). Set confidence from 0.0 to 1.0. Output ONLY JSON: {\"documentType\": \"...\", \"confidence\": 0.0}"
    },
    { role: "user", content: $text }
  ]
}')

TRIAGE_RAW=$(curl -s https://api.openai.com/v1/chat/completions \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d "$TRIAGE_PAYLOAD")

echo "Raw triage response: $TRIAGE_RAW" | head -c 500
TRIAGE_RESULT=$(echo "$TRIAGE_RAW" | jq -r '.choices[0].message.content')
echo ""
echo "Triage result:"
echo "$TRIAGE_RESULT" | jq .
echo "$TRIAGE_RESULT" > "$TMPDIR/triage.txt"
echo ""

echo "=== Step 3: Extract fields ==="
EXTRACTION_PAYLOAD=$(jq -n --rawfile text "$TMPDIR/text.txt" --rawfile triage "$TMPDIR/triage.txt" '{
  model: "gpt-4.1",
  max_tokens: 2048,
  messages: [
    {
      role: "system",
      content: "You are a contract analyst. Extract the fields relevant for this document type. Output ONLY this JSON, no markdown:\n{\n  \"documentType\": \"<from triage>\",\n  \"triageConfidence\": <from triage>,\n  \"extractedFields\": { <key-value fields> },\n  \"extractionConfidence\": <0.0-1.0>,\n  \"modelUsed\": \"gpt-4.1\",\n  \"pendingReview\": false\n}"
    },
    { role: "user", content: ("Triage: " + $triage + "\nDocument:\n" + $text) }
  ]
}')

EXTRACTION_RAW=$(curl -s https://api.openai.com/v1/chat/completions \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d "$EXTRACTION_PAYLOAD")

echo "Extraction result:"
echo "$EXTRACTION_RAW" | jq -r '.choices[0].message.content'

rm -rf "$TMPDIR"
