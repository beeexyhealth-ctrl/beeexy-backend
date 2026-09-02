# Phase 10 AI operations and security boundary

Phase 10 exposes informational AI Conversations, Temporary Documents, and Second Opinions. AI
output is supplemental and non-diagnostic. It never controls deterministic Pre-Triage urgency or
assessment, and it is not automatically promoted into Clinical History or FHIR.

## Public API surface

Every operation requires a bearer token:

| Method | Route | Successful status |
| --- | --- | --- |
| `POST` | `/api/v1/ai/conversations` | `201` |
| `GET` | `/api/v1/ai/conversations` | `200` |
| `GET` | `/api/v1/ai/conversations/{id}` | `200` |
| `POST` | `/api/v1/ai/conversations/{id}/messages` | `202` |
| `DELETE` | `/api/v1/ai/conversations/{id}` | `204` |
| `POST` | `/api/v1/ai/documents` | `201` |
| `DELETE` | `/api/v1/ai/documents/{id}` | `204` |
| `POST` | `/api/v1/ai/second-opinions` | `202` |
| `GET` | `/api/v1/ai/second-opinions/{id}` | `200` |
| `POST` | `/api/v1/ai/second-opinions/{id}/regenerate` | `202` |

Conversation and document access is account-owned. Patient-associated Conversations and all
Second Opinions additionally require current patient authority. Missing and unauthorized resource
lookups use the same concealed `404` behavior. Concurrent execution for the same Conversation or
Second Opinion is rejected with `409` before a provider call.

Conversation deletion is logical: it removes the Conversation from normal AI History and ordinary
detail access while retaining the internal execution/audit representation. Temporary Document
deletion is physical and keeps only lifecycle metadata.

## Provider and safety behavior

Configure the existing provider through `ClinicalAi__*` settings. Keep `ClinicalAi__ApiKey` in the
deployment secret store. With absent or invalid provider credentials, Beeexy selects the safe
unavailable behavior; structured deterministic Pre-Triage remains independent.

Each accepted Conversation message, initial Second Opinion, or regeneration makes exactly one
provider call. There is no hidden retry, summarization call, fallback provider, consensus, or
multi-model execution. Provider failures become sanitized status categories and generic safe user
responses.

Provider output must pass the versioned structural contract and Beeexy safety policy before it can
become a displayable immutable snapshot or assistant message. Rejected raw output is restricted
audit data: do not expose it through ordinary APIs, logs, operational dashboards, or support
exports. Regeneration reuses the immutable original input and appends a new snapshot; it does not
read current patient state or restore an expired source document.

## Temporary Documents

The only accepted formats are strict UTF-8 TXT and PDF with embedded extractable text. The exact
maximum is 25 MiB (`26,214,400` bytes). OCR, scanned/image-only PDFs, malformed or mismatched media,
binary TXT, and content that fails the file-safety boundary are rejected.

`AiDocuments__PrivateStorageRoot` may select an absolute private directory. If omitted, the runtime
uses `private-ai-documents` below the application base directory. The directory must not be served
by a web server, mounted into frontend/static content, or readable by unrelated operating-system
identities. Blob keys and filesystem paths are internal and must never be used as authorization.

Expiry is fixed at upload time plus exactly 24 hours. Reads, analysis, result lifetime, and
regeneration do not extend it. The hosted worker runs on startup, uses the durable database expiry
index, pages through every item due at the frozen cutoff, and retries failed or already-missing
artifacts safely. A source blob may disappear earlier on ephemeral storage; immutable approved
results and regeneration remain usable from the frozen normalized input.

Operational telemetry may include execution/result identifiers, configured provider/model,
prompt/safety versions, status, latency, sanitized failure category, safety display eligibility,
cleanup counts, and exception categories. It must not include user/document text, prompts, provider
requests/responses, rejected output, credentials, tokens, private paths, or stack traces containing
health information.

## Deployment smoke checks

After applying migrations and starting the service:

1. Confirm `/health/live` and `/health/ready` return success.
2. Confirm each route above returns `401` without a bearer token.
3. Inspect OpenAPI in a non-production environment and verify the ten operations have bearer
   security and expose no storage/audit/provider-payload fields.
4. With test credentials, exercise one safe Conversation message and Second Opinion; verify one
   provider request per accepted execution and only approved/fixed Beeexy content in responses.
5. Upload a harmless TXT, verify its metadata expiry is upload time plus 24 hours, delete it, and
   confirm repeated owner deletion is idempotent while a foreign account receives concealed `404`.
6. Verify cleanup success/failure telemetry contains counts or sanitized categories only. Do not
   use real patient data for deployment smoke checks.

These controls and tests are application acceptance evidence, not a penetration test, legal review,
formal healthcare-compliance assessment, or production certification.
