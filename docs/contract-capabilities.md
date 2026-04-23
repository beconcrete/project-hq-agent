# Contract Capabilities

This document describes the Contracts domain boundary as it exists today and the shape future agents should depend on.

## Durable Fact Model

The `Contracts` table stores one row per uploaded contract. Raw extraction remains available in `Fields`, while common query facts are promoted to columns so chat, lifecycle views, and future agents can answer without scraping model JSON.

| Area | Fields |
|---|---|
| Identity | `PartitionKey` correlation ID, `BlobPath`, `FileName`, `UserId`, `UploadedAt`, `DocumentType` |
| Confidence | `TriageConfidence`, `ExtractionConfidence`, `ModelUsed`, `Status`, `ReviewState` |
| Dates | `EffectiveDate`, `ExpiryDate`, `NoticePeriodDays`, `NoticeDeadline`, `AutoRenewal` |
| Parties and people | `PrimaryCounterparty`, `CounterpartyNames`, `PeopleMentioned`, `CustomerName` |
| Assignment | `AssignmentStartDate`, `AssignmentEndDate` |
| Commercials | `PaymentAmount`, `PaymentCurrency`, `PaymentUnit`, `PaymentType`, `PaymentTerms` |
| Quality | `RiskFlags`, `MissingFields`, `ReviewedAt`, `ReviewedBy`, `ReviewNote` |
| Relationships | `RelationshipType`, `DuplicateOfCorrelationId`, `SupersedesCorrelationId`, `RelatedContractIds`, `RelationshipReasons`, `RelationshipCandidates` |
| Soft delete | `DeletedAt`, `DeletedBy`, `DeleteReason` |

The supported first-pass contract families are NDA, consulting assignment, software/licence, service/customer agreement, and one-time engagement. Family-specific details that are not used for portfolio queries stay in `Fields`.

## Review States

`Status` controls coarse UI behavior:

| Status | Meaning |
|---|---|
| `processing` | Uploaded and queued, not extracted yet |
| `completed` | Extracted and usable |
| `pending_review` | Extracted but core facts are missing, ambiguous, or low confidence |
| `deleted` | Soft-deleted after admin review; hidden from default list and chat |
| `failed` | Ingestion failed |

`ReviewState` is the human review layer:

| ReviewState | Meaning |
|---|---|
| `approved_by_extraction` | Model extraction was confident enough to use without manual review |
| `pending_review` | Admin should inspect the row before relying on it |
| `approved` | Admin approved the extracted facts |
| `rejected` | Admin rejected and soft-deleted the upload |
| `duplicate_deleted` | Admin marked the upload as a duplicate and soft-deleted it |
| `failed` | No usable extraction exists |

Approving, rejecting, or classifying a relationship stores `ReviewedAt`, `ReviewedBy`, and optional `ReviewNote`. Raw extraction is not overwritten by review actions. Reject/delete and duplicate/delete are soft deletes so the audit trail remains available.

## Duplicate And Update Detection

After extraction, a new contract is compared with existing active contracts. The first version is deterministic and uses normalized facts:

| RelationshipType | Meaning |
|---|---|
| `duplicate` | Same family, same or similar parties/people, and same dates; likely the same contract uploaded again |
| `replacement` | Same customer/consultant with overlapping dates and changed material facts such as payment or terms |
| `extension` | Same customer/consultant with a later non-overlapping period |
| `new` | No meaningful relationship found |
| `unknown` | Similarity exists, but not enough to confidently classify it |

The UI shows relationship candidates with reasons. Admin actions decide what to do:

- **Approve new** keeps the upload as a standalone active contract.
- **Mark extension** keeps the upload active and records it as related to the selected contract.
- **Mark replacement** keeps the upload active and records which contract it supersedes.
- **Duplicate, delete** records the duplicate relationship and soft-deletes the new upload.
- **Reject, delete** soft-deletes a bad or unusable upload.

For v1, state-changing actions happen in the UI. Chat may mention review or relationship status when it affects an answer, but it does not make delete/replacement decisions.

## Capability Boundary

`IContractIntelligence` is the internal boundary for contract-domain facts. Future agents should call this interface instead of table storage, blob storage, or `ContractChatAgent`.

Deterministic methods:

| Method | Use |
|---|---|
| `ListContractsAsync` | Portfolio overview with normalized facts |
| `GetContractAsync` | Detail for a known correlation ID |
| `GetContractDocumentTextAsync` | Document fallback when extracted facts are insufficient |
| `FindExpiringAsync` | Expiry queries by date range and optional type |
| `FindRenewalWindowsAsync` | Notice/renewal/action deadline queries |
| `FindByPersonAsync` | Employee, consultant, signatory, or contact impact |
| `FindByCounterpartyAsync` | Customer, supplier, vendor, or party lookup |

Conversational method:

| Method | Use |
|---|---|
| `AnswerAsync` | A lightweight contract-domain answer wrapper. Use deterministic methods first when another agent already knows what it needs. |

`ContractChatAgent` is a frontend conversation layer over this boundary. It may use model tool-calling and document fallback, but that should not become the integration contract for future Sales or HR agents.

## Cross-Domain Invocation Pattern

For the next domains, use domain capabilities directly in-process when the caller is hosted inside `hq-agent-function-app`. Do not expose internal agent endpoints to the browser.

Minimum request context:

| Field | Purpose |
|---|---|
| `Caller.UserId` | Ownership and audit trail |
| `Caller.IsAdmin` | Visibility boundary |
| `CorrelationId` | Traceability across agents and logs |
| `SourceAgent` | Example: `SalesForecastAgent` |
| `QuestionIntent` | Why the other domain is being called |

Minimum response behavior:

- Return normalized facts and source references, not prose only.
- Preserve document correlation IDs when a contract influenced the answer.
- Log the calling agent, target capability, and correlation ID.
- Avoid letting future domains depend on `ContractExtractionEntity` internals.

Example future Sales call:

```csharp
var expiringAssignments = await contracts.FindExpiringAsync(
    caller,
    from: DateOnly.FromDateTime(DateTime.UtcNow),
    to: DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
    contractType: "consulting",
    ct);
```
