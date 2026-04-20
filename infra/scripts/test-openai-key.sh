#!/usr/bin/env bash
# Quick smoke test — verifies the OpenAI API key works with a minimal request.
# Run locally: bash infra/scripts/test-openai-key.sh

API_KEY="<your-openai-api-key>"

echo "Calling OpenAI chat completions..."

curl -s https://api.openai.com/v1/chat/completions \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gpt-4.1-mini",
    "max_tokens": 10,
    "messages": [{"role": "user", "content": "Say hello"}]
  }' | jq '{status: .error.code, message: .error.message, reply: .choices[0].message.content}'
