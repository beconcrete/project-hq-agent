# Contracts Evaluation Set

Use this checklist after extraction or chat changes. The current fixtures live in `infra/test/`.

## Fixture Coverage

| Fixture | Expected family | Purpose |
|---|---|---|
| `nda-microsoft-sweden-be-concrete.pdf` | NDA | Counterparty lookup and confidentiality facts |
| `nda-aws-sweden-be-concrete.pdf` | NDA | Second NDA counterparty |
| `consulting-spotify-solutions-architect-bjorn-jan-feb-2026.pdf` | Consulting assignment | Easy assignment, expiry, person, hourly rate |
| `consulting-lovable-platform-architecture-bjorn-may-dec-2026.pdf` | Consulting assignment | More complex assignment and longer lifecycle |
| `inspirational-talk-nox-consulting-bjorn-2026.pdf` | One-time engagement | Fixed fee and one-time service |

## Expected Chat Questions

Run these from the Contracts page after uploading the fixtures:

- Which contracts expire next?
- What contracts do we have for Björn Eriksen?
- Do we have an NDA with Microsoft Sweden?
- Which agreements involve Spotify?
- Which contracts have renewal or notice deadlines?
- What is the hourly rate for Spotify?
- Which contracts have a one-time fee?

## Expected Scope Guard Behavior

These should be rejected with the standard contract-domain boundary message:

- How do I list files in a terminal?
- What car model should I buy?
- Give me dating advice.

## Failure Triage

| Symptom | Likely area |
|---|---|
| Contract never appears beyond `processing` | Queue/function ingestion |
| Wrong document type | MAF triage |
| Missing dates, people, counterparties, or payment facts | Extraction prompt or `ContractFactsExtractor` |
| Chat ignores a visible normalized fact | `ContractChatAgent` tool choice or `IContractIntelligence` |
| Chat invents a fact | Prompt/tool result grounding |
| Correct answer but no document link | Chat references or download URL endpoint |
