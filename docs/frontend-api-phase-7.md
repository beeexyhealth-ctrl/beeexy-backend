# Beeexy Phase 7 — Frontend API integration

## 1. Purpose and source of truth

This guide is the frontend contract for the completed Phase 7 clinic and doctor
directory. Phase 7 provides anonymous reads over a product-approved synthetic
demo directory, exact stored-value filters, and deterministic demo doctor
matching. Phase 8 has not started.

The current endpoint handlers, HTTP DTOs, application use cases, public-query
boundary, repositories, cursor codecs, matching engine, OpenAPI document, and
Phase 7.3–7.7 tests are the source of truth for statements labeled **backend
contract**. Suggestions labeled **frontend recommendation** are safe UX and
client-integration guidance, not server requirements.

The frontend consumes the returned projections, ordering, explanations, and
opaque cursors. It must not reproduce publication rules, credential visibility,
hard filtering, score calculation, factor evaluation, ranking, or cursor
encoding.

Phase 7 exposes exactly these four operations:

| Method | Route | Purpose | Authentication | Declared responses |
|---|---|---|---|---|
| `GET` | `/api/v1/clinics` | List and filter public clinics | Anonymous | `200`, `422`, `500` |
| `GET` | `/api/v1/clinics/{id}` | Read one public clinic and eligible locations | Anonymous | `200`, `404`, `500` |
| `GET` | `/api/v1/doctors` | List, filter, and deterministically rank public doctors | Anonymous | `200`, `422`, `500` |
| `GET` | `/api/v1/doctors/{id}` | Read one non-personalized public doctor profile | Anonymous | `200`, `404`, `500` |

OpenAPI currently contains 36 paths. It contains these two clinic paths and two
doctor paths, with no public matching endpoint, rule selector, import/admin
endpoint, or Phase 8 endpoint.

There is no frontend source tree in this repository. Names such as
`listClinics` and `searchDoctors` in this guide are recommendations to adapt to
the consuming frontend's established API layer. Do not add Axios, React Query,
SWR, or another state/HTTP library solely for Phase 7.

## 2. Public and anonymous access

### Backend contract

All four operations use anonymous access. They do not require login, an active
patient, pre-triage, Clinical History, or FHIR context. An invalid/malformed
Bearer token does not change the approved public response: Phase 7.7 tests send
an invalid Bearer token to all four routes and receive `200` with the same
public-safe projection.

No `Authorization` header is required. A shared client may still attach its
ordinary session header, but the frontend must not gate these routes behind
authentication or treat an authentication failure elsewhere as a prerequisite
for directory access.

Useful request headers are:

```http
Accept: application/json
X-Correlation-ID: <privacy-safe-client-correlation-id>
```

Neither is required by the endpoint handlers. The global pipeline returns
`X-Correlation-ID`; a valid caller-supplied value is 1–64 characters using
letters, digits, `-`, `_`, `.`, or `:`. Do not put a token, patient identifier,
symptom, or other clinical content in it.

### Frontend recommendation

Reuse the existing Beeexy API client and configured API origin. Use its normal
Problem Details parsing, correlation-ID handling, and `AbortSignal` support.
Directory screens should work before login and should cancel obsolete requests
when filters or routes change.

## 3. Shared query, normalization, and response conventions

### Query serialization

Omit optional parameters that have no value. Do not serialize `undefined`,
`null`, or an empty string as a query value. Every supported query parameter
may occur at most once. Repeated parameters return `422`; an unknown query name
also returns `422` on list/search operations.

Use `URLSearchParams` or the consuming application's existing equivalent so
spaces and Unicode are encoded correctly. Query names are case-sensitive API
contract names.

### Normalization and exact matching

| Value class | Backend normalization and validation | Match behavior |
|---|---|---|
| Codes (`code`, `specialtyCode`, `languageCode`, `insurancePlanCode`) | Trim leading/trailing whitespace; non-empty; maximum 100 characters; no whitespace anywhere after trimming | Exact, ordinal, case-sensitive stored-code equality |
| Location parts (`locality`, `administrativeArea`, `country`) | Trim leading/trailing whitespace; non-empty; maximum 100 characters | Exact, case-sensitive stored-text equality |
| `pageSize` | Optional integer; default `20`; accepted range `1` through `100` inclusive | Controls items per page only |
| `cursor` | Optional opaque backend token; nonblank canonical Base64URL; maximum encoded length 4096 | Must belong to the normalized filters and current visible boundary |

The backend does not lowercase, case-fold, fuzzy-match, Unicode-normalize, or
semantically expand filter values. For example,
`code=DEMO-CLINIC-AURORA` is a valid value but does not match the stored
lowercase code, so it returns a successful empty page. A valid unknown Unicode
code likewise returns an empty page. Blank values and codes containing spaces
are malformed and return `422`.

### JSON and optionality

- JSON property names are camelCase.
- Identifiers are UUID strings.
- Collections are JSON arrays and are empty when no eligible values exist.
- Page `nextCursor` is a string when another page exists and `null` at the end.
- An affiliation `location` is an object or `null`.
- Neutral doctor search items omit the `match` property; they do not send
  `"match": null`.
- Doctor detail never has a `match` property.
- Public DTOs do not expose publication flags, credential states/evidence,
  matching package/hash/formula metadata, import metadata, or database fields.

## 4. Shared pagination rules

Both list operations use backend-owned keyset cursors and one-item lookahead.
The cursor is an implementation token, not frontend state to inspect.

Frontend rules:

1. Treat `nextCursor` as opaque.
2. Never decode, alter, synthesize, persist as business data, or display it.
3. Send it back only in the `cursor` parameter for the same operation and the
   same normalized filters.
4. `pageSize` may change without changing the filter identity, but keeping it
   stable produces the clearest UX.
5. When any filter changes, discard accumulated results and the old cursor,
   then request the first page without `cursor`.
6. Stop when `nextCursor === null`.
7. Handle a stale, hidden-boundary, mismatched, tampered, or incompatible cursor
   as `422`; restart only after intentionally discarding the old traversal.

Clinic and neutral doctor traversal use UUID ascending order. Matching-active
doctor traversal uses score descending, then canonical doctor UUID text
ascending. The frontend must append pages in response order without sorting
them again.

## 5. `GET /api/v1/clinics`

### Purpose and request

**Backend contract:** Lists published clinics. The list projection is sufficient
for clinic cards and deliberately excludes locations and all internal state.

```http
GET /api/v1/clinics?code=demo-clinic-aurora&locality=Demo%20Central&administrativeArea=Synthetic%20Demo%20Region&country=Synthetic%20Demo%20Country&pageSize=20
Accept: application/json
```

Exact query parameters:

| Parameter | Type | Required | Semantics |
|---|---|---:|---|
| `cursor` | string | No | Opaque clinic cursor bound to the normalized clinic filters and a still-public boundary clinic |
| `pageSize` | integer | No | Default `20`; minimum `1`; maximum `100` |
| `code` | string | No | Exact clinic code after trimming |
| `locality` | string | No | Exact locality on an eligible published location |
| `administrativeArea` | string | No | Exact administrative area on an eligible published location |
| `country` | string | No | Exact country on an eligible published location |

All supplied filters use AND semantics. If two or three location fields are
supplied, one eligible location must satisfy all of them; the backend does not
combine fields from different clinic locations.

Ordering is published clinic UUID ascending. A valid unknown filter returns
`200` with `items: []` and `nextCursor: null`.

### `200 OK` response

```json
{
  "items": [
    {
      "clinicId": "71020000-0000-4000-8000-000000000001",
      "code": "demo-clinic-aurora",
      "name": "Synthetic Demo Clinic Aurora"
    }
  ],
  "nextCursor": null
}
```

| Field | Type | Nullable | Meaning |
|---|---|---:|---|
| `items` | `ClinicSummary[]` | No | Public clinic summaries in backend order |
| `items[].clinicId` | UUID string | No | Clinic identifier used by the detail route |
| `items[].code` | string | No | Exact stored synthetic clinic code |
| `items[].name` | string | No | Exact stored synthetic clinic name |
| `nextCursor` | string or `null` | Yes | Opaque next-page token; `null` means end of results |

### Errors

- Invalid page size: `422`, `clinic_directory.page_size_invalid`.
- Malformed, repeated, blank, or overlong filter: `422`,
  `clinic_directory.filter_invalid`.
- Unsupported query name: `422`, `clinic_directory.filter_unsupported`.
- Malformed, tampered, filter-mismatched, noncanonical, unknown-version, stale,
  or hidden-boundary cursor: `422`, `clinic_directory.cursor_invalid`.
- Unexpected server failure: safe `500` Problem Details.

No results is a successful `200`, not an error.

## 6. Clinic contracts

```ts
export interface ClinicQuery {
  cursor?: string;
  pageSize?: number;
  code?: string;
  locality?: string;
  administrativeArea?: string;
  country?: string;
}

export interface ClinicSummary {
  clinicId: string;
  code: string;
  name: string;
}

export interface ClinicPage {
  items: ClinicSummary[];
  nextCursor: string | null;
}

export interface ClinicLocation {
  locationId: string;
  name: string;
  locality: string;
  administrativeArea: string;
  country: string;
  timeZone: string;
}

export interface ClinicDetail {
  clinicId: string;
  code: string;
  name: string;
  locations: ClinicLocation[];
}
```

`timeZone` is a stored IANA timezone identifier such as `America/Lima`, not a
UTC offset. The list response does not contain `locations`; use clinic detail
only when the user opens a clinic rather than fetching every detail from a
list.

## 7. `GET /api/v1/clinics/{id}`

### Request

`id` is a clinic UUID. Send the ordinary hyphenated UUID returned as
`clinicId` by the list endpoint.

```http
GET /api/v1/clinics/71020000-0000-4000-8000-000000000001
Accept: application/json
```

The operation defines no query parameters.

### `200 OK` response

```json
{
  "clinicId": "71020000-0000-4000-8000-000000000001",
  "code": "demo-clinic-aurora",
  "name": "Synthetic Demo Clinic Aurora",
  "locations": [
    {
      "locationId": "71020000-0000-4100-8000-000000000011",
      "name": "Synthetic Aurora Central Location",
      "locality": "Demo Central",
      "administrativeArea": "Synthetic Demo Region",
      "country": "Synthetic Demo Country",
      "timeZone": "America/Lima"
    }
  ]
}
```

Only eligible stored locations appear: the clinic, location, and relationship
visibility requirements are enforced by the backend. Coordinates, distance,
opening hours, phone, website, insurance, ratings, and availability are not
part of this DTO.

| Field | Type | Nullable | Meaning |
|---|---|---:|---|
| `clinicId` | UUID string | No | Public clinic identifier |
| `code` | string | No | Exact stored synthetic clinic code |
| `name` | string | No | Exact stored synthetic clinic name |
| `locations` | `ClinicLocation[]` | No | Eligible published locations ordered by location UUID; may be empty |
| `locations[].locationId` | UUID string | No | Stored location identifier |
| `locations[].name` | string | No | Stored synthetic location name |
| `locations[].locality` | string | No | Exact stored locality |
| `locations[].administrativeArea` | string | No | Exact stored administrative area |
| `locations[].country` | string | No | Exact stored country |
| `locations[].timeZone` | string | No | Domain-validated stored IANA timezone |

Missing, unpublished, and the all-zero UUID return the same concealed `404`:

```json
{
  "title": "Clinic not found.",
  "status": 404,
  "detail": "The requested clinic could not be found.",
  "instance": "/api/v1/clinics/00000000-0000-0000-0000-000000000000",
  "correlationId": "<response correlation ID>"
}
```

A value that does not satisfy the route's GUID constraint also produces `404`
at routing level. Do not use differences in a routing-level body to infer
whether a clinic exists.

**Frontend recommendation:** Use one generic unavailable state, such as “This
clinic is unavailable.” Never distinguish missing from unpublished.

## 8. `GET /api/v1/doctors`

### Purpose and exact parameters

**Backend contract:** With no criteria, this is a neutral public doctor
directory. Supplying any valid supported criterion activates deterministic demo
matching after the same criterion has constrained candidates as a hard filter.

Exact query parameters:

| Parameter | Type | Required | Semantics |
|---|---|---:|---|
| `cursor` | string | No | Opaque neutral or ranked cursor for the unchanged normalized criteria |
| `pageSize` | integer | No | Default `20`; minimum `1`; maximum `100` |
| `specialtyCode` | string | No | Exact stored specialty relationship code |
| `languageCode` | string | No | Exact stored language relationship code |
| `locality` | string | No | Exact field on one eligible affiliation location |
| `administrativeArea` | string | No | Exact field on the same eligible affiliation location |
| `country` | string | No | Exact field on the same eligible affiliation location |
| `insurancePlanCode` | string | No | Exact stored synthetic doctor-insurance participation code |

No `matching`, `matching=true`, `sort`, `ruleVersion`, `distance`, `rating`,
`availability`, symptom, patient, or clinical query parameter exists.

### Validation

All six criteria are optional. Codes and location parts follow the shared
100-character normalization rules. A supplied blank string is invalid; it does
not become “no criterion.” Unsupported or repeated query parameters return
`422`.

Valid unknown criteria return:

```json
{
  "items": [],
  "nextCursor": null
}
```

The request succeeds with `200` even though matching was activated and found no
eligible candidates.

Every valid search returns `200 OK` with a `DoctorPage` JSON object containing
`items` and `nextCursor`. Each item uses the public projection below; `match` is
omitted for neutral search and present for every matching-active result.

## 9. Hard-filter semantics

Every supplied dimension narrows candidates by intersection:

```text
specialty
AND language
AND one eligible affiliation location
AND stored insurance participation
```

Only supplied dimensions participate in the expression. The three location
fields form one location dimension: if more than one is supplied, all must
match the same eligible published clinic location reached through an eligible
published affiliation. A locality from one location cannot combine with a
country or administrative area from another.

Criteria are never relaxed because a result set is small or empty. There is no
fuzzy spelling, semantic synonym, prefix, popularity, proximity, or fallback
search. The frontend should offer exact canonical values from a product-owned
source when available, but Phase 7 itself provides no taxonomy-discovery
endpoint.

## 10. Matching activation and result ordering

### No criteria: neutral directory

When all six criteria are omitted:

- the matching rule is not read;
- doctors order by UUID ascending;
- pagination uses a neutral filter-bound cursor;
- every item omits the `match` property.

`pageSize` and `cursor` are pagination controls, not matching criteria.

### Any criterion: matching active

Supplying any one or more normalized criteria activates matching automatically:

1. The backend applies every criterion as an exact hard filter.
2. It evaluates the complete eligible filtered candidate set using the approved
   rule version.
3. It globally orders candidates by score descending, then canonical doctor
   UUID text ascending.
4. It paginates that global order.
5. It returns `match` on every result item.

The frontend must render the returned order unchanged and must never calculate,
adjust, normalize, compare, or use the score to create a second ranking.

## 11. Matching rule and semantics

The approved configuration identity is:

```text
beeexy-demo-doctor-match-rules@2026.08.29-demo.1
```

The public response exposes only `ruleVersion: "2026.08.29-demo.1"`; it does
not expose the package code, content hash, formula metadata, or import record.

| Factor code | `semanticsVersion` | Weight | Explanation-data keys |
|---|---|---:|---|
| `specialty_exact` | `exact_canonical_doctor_specialty_relationship_v1` | 25 | `specialtyCode` |
| `language_exact` | `exact_canonical_doctor_language_relationship_v1` | 25 | `languageCode` |
| `location_exact` | `exact_same_eligible_affiliation_location_fields_v1` | 25 | present values in `locality`, `administrativeArea`, `country` order |
| `stored_insurance_participation_exact` | `exact_stored_doctor_insurance_participation_v1` | 25 | `insurancePlanCode` |

The internal formula identity is
`sum_matched_weight_points_no_reweight_v1`: each matched factor contributes its
configured 25 points. The maximum is 100. A missing criterion makes its factor
`not_applicable`, contributes 0, and does not redistribute its weight. The
missing-input semantics are `not_applicable_zero_contribution_v1`.

Factor order is always specialty, language, location, stored insurance.
Possible public state strings are:

```text
matched
not_matched
not_applicable
```

Explanation codes follow:

```text
demo_match.<factorCode>.<state>
```

Because public doctor search applies supplied criteria as hard filters, every
surviving result matches each supplied factor dimension; omitted dimensions are
`not_applicable`. The matching engine defines `not_matched`, but the frontend
must not manufacture it or infer candidates removed by hard filters.

The tie-break identity is `score_desc_uuid_text_asc_v1`. Do not reproduce that
comparison in the frontend; preserving backend order is sufficient.

## 12. Neutral doctor item example

This is the actual list projection for the synthetic Amber doctor during a
neutral search. Notice that `match` is absent.

```json
{
  "doctorId": "71020000-0000-4200-8000-000000000021",
  "code": "demo-doctor-amber",
  "displayName": "Synthetic Demo Doctor Amber",
  "specialties": [
    {
      "code": "demo-specialty-general",
      "name": "Synthetic General Care"
    }
  ],
  "languages": [
    {
      "code": "demo-language-en",
      "name": "Synthetic English Capability"
    },
    {
      "code": "demo-language-es",
      "name": "Synthetic Spanish Capability"
    }
  ],
  "affiliations": [
    {
      "clinicId": "71020000-0000-4000-8000-000000000001",
      "clinicCode": "demo-clinic-aurora",
      "clinicName": "Synthetic Demo Clinic Aurora",
      "location": {
        "locationId": "71020000-0000-4100-8000-000000000011",
        "name": "Synthetic Aurora Central Location",
        "locality": "Demo Central",
        "administrativeArea": "Synthetic Demo Region",
        "country": "Synthetic Demo Country",
        "timeZone": "America/Lima"
      }
    }
  ],
  "storedInsuranceParticipations": [
    {
      "code": "demo-plan-amber",
      "name": "Synthetic Stored Plan Amber"
    },
    {
      "code": "demo-plan-blue",
      "name": "Synthetic Stored Plan Blue"
    }
  ],
  "credentials": [
    {
      "name": "Synthetic Demo Dataset Credential Amber"
    }
  ]
}
```

## 13. Matching-active doctor item example

For this request:

```http
GET /api/v1/doctors?specialtyCode=demo-specialty-general&languageCode=demo-language-es&locality=Demo%20Harbor&administrativeArea=Synthetic%20Demo%20Region&country=Synthetic%20Demo%20Country&insurancePlanCode=demo-plan-blue
```

the response contains this complete matching-active synthetic Blue doctor item
inside the exact page envelope:

```json
{
  "items": [
    {
      "doctorId": "71020000-0000-4200-8000-000000000022",
      "code": "demo-doctor-blue",
      "displayName": "Synthetic Demo Doctor Blue",
      "specialties": [
        {
          "code": "demo-specialty-general",
          "name": "Synthetic General Care"
        }
      ],
      "languages": [
        {
          "code": "demo-language-es",
          "name": "Synthetic Spanish Capability"
        }
      ],
      "affiliations": [
        {
          "clinicId": "71020000-0000-4000-8000-000000000002",
          "clinicCode": "demo-clinic-mosaic",
          "clinicName": "Synthetic Demo Clinic Mosaic",
          "location": {
            "locationId": "71020000-0000-4100-8000-000000000013",
            "name": "Synthetic Mosaic Harbor Location",
            "locality": "Demo Harbor",
            "administrativeArea": "Synthetic Demo Region",
            "country": "Synthetic Demo Country",
            "timeZone": "America/Lima"
          }
        }
      ],
      "storedInsuranceParticipations": [
        {
          "code": "demo-plan-blue",
          "name": "Synthetic Stored Plan Blue"
        }
      ],
      "credentials": [],
      "match": {
        "ruleVersion": "2026.08.29-demo.1",
        "matchScore": 100,
        "factors": [
          {
            "factorCode": "specialty_exact",
            "semanticsVersion": "exact_canonical_doctor_specialty_relationship_v1",
            "configuredWeightPoints": 25,
            "state": "matched",
            "contributionPoints": 25,
            "explanationCode": "demo_match.specialty_exact.matched",
            "explanationData": [
              {
                "key": "specialtyCode",
                "value": "demo-specialty-general"
              }
            ]
          },
          {
            "factorCode": "language_exact",
            "semanticsVersion": "exact_canonical_doctor_language_relationship_v1",
            "configuredWeightPoints": 25,
            "state": "matched",
            "contributionPoints": 25,
            "explanationCode": "demo_match.language_exact.matched",
            "explanationData": [
              {
                "key": "languageCode",
                "value": "demo-language-es"
              }
            ]
          },
          {
            "factorCode": "location_exact",
            "semanticsVersion": "exact_same_eligible_affiliation_location_fields_v1",
            "configuredWeightPoints": 25,
            "state": "matched",
            "contributionPoints": 25,
            "explanationCode": "demo_match.location_exact.matched",
            "explanationData": [
              {
                "key": "locality",
                "value": "Demo Harbor"
              },
              {
                "key": "administrativeArea",
                "value": "Synthetic Demo Region"
              },
              {
                "key": "country",
                "value": "Synthetic Demo Country"
              }
            ]
          },
          {
            "factorCode": "stored_insurance_participation_exact",
            "semanticsVersion": "exact_stored_doctor_insurance_participation_v1",
            "configuredWeightPoints": 25,
            "state": "matched",
            "contributionPoints": 25,
            "explanationCode": "demo_match.stored_insurance_participation_exact.matched",
            "explanationData": [
              {
                "key": "insurancePlanCode",
                "value": "demo-plan-blue"
              }
            ]
          }
        ]
      }
    }
  ],
  "nextCursor": null
}
```

If, for example, only `languageCode` is supplied, the language factor is
`matched` with 25 points and the other three factors are `not_applicable`, have
zero contribution, use an empty `explanationData` array, and are not reweighted.
The frontend should render the returned state and explanation data rather than
derive this behavior.

## 14. Doctor contracts

Public doctor field meanings are:

| Field | Type | Nullable/optional | Meaning |
|---|---|---:|---|
| `doctorId` | UUID string | No | Public doctor identifier used by detail |
| `code` | string | No | Exact stored synthetic doctor code |
| `displayName` | string | No | Exact stored synthetic display name |
| `specialties` | catalog-value array | No | Exact stored specialty code/name relationships; may be empty |
| `languages` | catalog-value array | No | Exact stored language code/name relationships; may be empty |
| `affiliations` | affiliation array | No | Eligible public clinic relationships; may be empty |
| `affiliations[].location` | location or `null` | Yes | Eligible specific location, or `null` for a clinic-only affiliation |
| `storedInsuranceParticipations` | catalog-value array | No | Stored synthetic participation only; may be empty |
| `credentials` | `{ name: string }[]` | No | Names of demo-dataset `Verified` credentials only; may be empty |
| `match` | `DoctorMatch` | Search-only optional | Omitted in neutral search; present in matching-active search; absent from detail |

```ts
export interface DoctorQuery {
  cursor?: string;
  pageSize?: number;
  specialtyCode?: string;
  languageCode?: string;
  locality?: string;
  administrativeArea?: string;
  country?: string;
  insurancePlanCode?: string;
}

export interface DirectoryCatalogValue {
  code: string;
  name: string;
}

export type DoctorSpecialty = DirectoryCatalogValue;
export type DoctorLanguage = DirectoryCatalogValue;
export type StoredInsuranceParticipation = DirectoryCatalogValue;

export interface DoctorAffiliationLocation {
  locationId: string;
  name: string;
  locality: string;
  administrativeArea: string;
  country: string;
  timeZone: string;
}

export interface DoctorAffiliation {
  clinicId: string;
  clinicCode: string;
  clinicName: string;
  location: DoctorAffiliationLocation | null;
}

export interface VerifiedDemoCredential {
  name: string;
}

export interface DoctorProfile {
  doctorId: string;
  code: string;
  displayName: string;
  specialties: DoctorSpecialty[];
  languages: DoctorLanguage[];
  affiliations: DoctorAffiliation[];
  storedInsuranceParticipations: StoredInsuranceParticipation[];
  credentials: VerifiedDemoCredential[];
}

export type DoctorMatchFactorState =
  | "matched"
  | "not_matched"
  | "not_applicable";

export type DoctorMatchFactorCode =
  | "specialty_exact"
  | "language_exact"
  | "location_exact"
  | "stored_insurance_participation_exact";

export type DoctorMatchRuleVersion = "2026.08.29-demo.1";

export type DoctorMatchSemanticsVersion =
  | "exact_canonical_doctor_specialty_relationship_v1"
  | "exact_canonical_doctor_language_relationship_v1"
  | "exact_same_eligible_affiliation_location_fields_v1"
  | "exact_stored_doctor_insurance_participation_v1";

export interface DoctorMatchExplanationValue {
  key: string;
  value: string;
}

export interface DoctorMatchFactor {
  factorCode: DoctorMatchFactorCode;
  semanticsVersion: DoctorMatchSemanticsVersion;
  configuredWeightPoints: number;
  state: DoctorMatchFactorState;
  contributionPoints: number;
  explanationCode: string;
  explanationData: DoctorMatchExplanationValue[];
}

export interface DoctorMatch {
  ruleVersion: DoctorMatchRuleVersion;
  matchScore: number;
  factors: DoctorMatchFactor[];
}

export interface DoctorSearchItem extends DoctorProfile {
  match?: DoctorMatch;
}

export interface DoctorPage {
  items: DoctorSearchItem[];
  nextCursor: string | null;
}

export type DoctorDetail = DoctorProfile;
```

The TypeScript names are recommendations; the property names are the JSON
contract. `match?` models actual omission in neutral search. Do not broaden it
to a frontend-computed match. `VerifiedDemoCredential` is intentionally named
to preserve truthfulness; the JSON object itself contains only `name` and does
not expose a status field.

## 15. `GET /api/v1/doctors/{id}`

`id` is a doctor UUID. Send the ordinary hyphenated UUID returned by doctor
search:

```http
GET /api/v1/doctors/71020000-0000-4200-8000-000000000021
Accept: application/json
```

The operation defines no query parameters. A `200` body is exactly
`DoctorProfile`: the same public profile fields shown in the neutral item
example, without `match`. Detail is non-personalized and cannot inherit a score,
criteria, explanations, or cursor from a previous search.

An affiliation may legitimately have `"location": null`; this means the
eligible stored affiliation has no specific stored location. It is not an
error, unknown clinic, or instruction to fetch a hidden location.

Missing, unpublished, and the all-zero UUID return the same concealed `404`:

```json
{
  "title": "Doctor not found.",
  "status": 404,
  "detail": "The requested doctor could not be found.",
  "instance": "/api/v1/doctors/00000000-0000-0000-0000-000000000000",
  "correlationId": "<response correlation ID>"
}
```

A value that fails the route's GUID constraint returns routing-level `404`.
The frontend must not use body differences to infer whether a doctor is missing
or unpublished.

**Frontend recommendation:** Show one generic state such as “This doctor is
unavailable.”

## 16. Ranked and neutral pagination in TypeScript

Keep criteria separate from cursor state. These examples use the client
functions defined later.

```ts
const clinicFilters: Omit<ClinicQuery, "cursor"> = {
  country: "Synthetic Demo Country",
  pageSize: 20,
};

const firstClinics = await listClinics(clinicFilters, signal);
const secondClinics = firstClinics.nextCursor
  ? await listClinics(
      { ...clinicFilters, cursor: firstClinics.nextCursor },
      signal,
    )
  : null;
```

```ts
const doctorCriteria: Omit<DoctorQuery, "cursor"> = {
  specialtyCode: "demo-specialty-general",
  languageCode: "demo-language-es",
  pageSize: 20,
};

const firstDoctors = await searchDoctors(doctorCriteria, signal);
const secondDoctors = firstDoctors.nextCursor
  ? await searchDoctors(
      { ...doctorCriteria, cursor: firstDoctors.nextCursor },
      signal,
    )
  : null;
```

The same code works for neutral and ranked doctor pages because the backend
selects the cursor kind. Never decide cursor kind by parsing the token.

When a filter changes:

```ts
function replaceDoctorCriteria(next: Omit<DoctorQuery, "cursor">) {
  return {
    criteria: next,
    items: [] as DoctorSearchItem[],
    nextCursor: null as string | null,
  };
}
```

Discard the old items as well as the cursor so results from different hard
filter sets are not mixed.

## 17. Error handling and Problem Details

### Proven endpoint outcomes

| Operation | Situation | Status | Programmatic handling |
|---|---|---:|---|
| Clinic/doctor list | Valid request, including no results | `200` | Render `items`; empty is a normal state |
| Clinic/doctor list | Invalid page size | `422` | Read `*.page_size_invalid` |
| Clinic/doctor list | Invalid/repeated filter | `422` | Read `*.filter_invalid` |
| Clinic/doctor list | Unsupported query name | `422` | Read `*.filter_unsupported` |
| Clinic/doctor list | Invalid/mismatched/stale cursor | `422` | Read `*.cursor_invalid`; discard traversal state |
| Clinic/doctor detail | Missing, unpublished, or empty UUID | `404` | Show one concealed unavailable state |
| Any Phase 7 operation | Unexpected internal failure | `500` | Generic error; retain correlation ID |

The `*` prefix is `clinic_directory` or `doctor_directory`.

Phase 7 list/search validation uses `application/problem+json`. A representative
actual shape is:

```json
{
  "title": "Request validation failed.",
  "status": 422,
  "detail": "Page size must be between 1 and 100.",
  "instance": "/api/v1/doctors",
  "errorCode": "doctor_directory.page_size_invalid",
  "correlationId": "<response correlation ID>"
}
```

Shared TypeScript:

```ts
export interface BeeexyProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errorCode?: string;
  correlationId?: string;
}
```

`type`, `detail`, and `errorCode` are not guaranteed on every global failure
path. `instance` is the request path. Use `errorCode` for known validation
branches; do not parse human-readable `detail` as a stable code. Prefer the
body correlation ID, falling back to the `X-Correlation-ID` response header,
for support.

Do not display raw exception text, database/provider details, cursor content,
or internal metadata. A `422` cursor failure does not prove why the boundary
became invalid. A `404` does not prove whether a resource is absent or hidden.

## 18. Recommended API client shape

These functions are **frontend recommendations**. `apiFetch` and
`throwProblem` stand for the consuming frontend's existing Beeexy base-URL and
Problem Details abstractions.

```ts
function appendQuery(
  path: string,
  query: ClinicQuery | DoctorQuery,
): string {
  const parameters = new URLSearchParams();

  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined) parameters.set(key, String(value));
  }

  const encoded = parameters.toString();
  return encoded.length === 0 ? path : `${path}?${encoded}`;
}

async function readJsonOrProblem<T>(response: Response): Promise<T> {
  if (!response.ok) await throwProblem(response);
  return (await response.json()) as T;
}

export async function listClinics(
  query: ClinicQuery = {},
  signal?: AbortSignal,
): Promise<ClinicPage> {
  const response = await apiFetch(appendQuery("/api/v1/clinics", query), {
    method: "GET",
    headers: { Accept: "application/json" },
    signal,
  });

  return readJsonOrProblem<ClinicPage>(response);
}

export async function getClinic(
  clinicId: string,
  signal?: AbortSignal,
): Promise<ClinicDetail> {
  const response = await apiFetch(
    `/api/v1/clinics/${encodeURIComponent(clinicId)}`,
    {
      method: "GET",
      headers: { Accept: "application/json" },
      signal,
    },
  );

  return readJsonOrProblem<ClinicDetail>(response);
}

export async function searchDoctors(
  query: DoctorQuery = {},
  signal?: AbortSignal,
): Promise<DoctorPage> {
  const response = await apiFetch(appendQuery("/api/v1/doctors", query), {
    method: "GET",
    headers: { Accept: "application/json" },
    signal,
  });

  return readJsonOrProblem<DoctorPage>(response);
}

export async function getDoctor(
  doctorId: string,
  signal?: AbortSignal,
): Promise<DoctorDetail> {
  const response = await apiFetch(
    `/api/v1/doctors/${encodeURIComponent(doctorId)}`,
    {
      method: "GET",
      headers: { Accept: "application/json" },
      signal,
    },
  );

  return readJsonOrProblem<DoctorDetail>(response);
}
```

The query union accepted by `appendQuery` prevents unrelated names and arrays;
repeating query keys is invalid. Validate or trim UI input before calling if
that improves immediate feedback, but let the backend remain authoritative.
Do not transform case or reproduce canonical-code rules independently.

## 19. Frontend flow recommendations

### Doctor flow

This is a **UX recommendation**, not a required route layout:

```text
Doctor directory
→ select exact filters
→ GET /api/v1/doctors without an old cursor
→ backend hard-filters, matches, and globally ranks
→ render returned order
→ optionally render backend factor explanations
→ load more with nextCursor and identical criteria
→ open doctor card
→ GET /api/v1/doctors/{id}
```

Use the doctor list projection to render cards; it already includes all public
profile collections. Do not issue a detail request for every item. Detail is
for a deliberate profile view.

### Clinic flow

```text
Clinic directory
→ GET /api/v1/clinics
→ render clinic summaries
→ open one clinic card
→ GET /api/v1/clinics/{id}
→ render eligible stored locations
```

Do not issue one detail request per clinic while rendering the list.

### Suggested presentation states

| State | Trigger | UX recommendation |
|---|---|---|
| Loading | Request in flight | Preserve or skeleton the relevant list; cancel obsolete requests |
| Empty | `200` with `items: []` | Show a normal “No exact matches” state, not an API error |
| Neutral doctors | No criteria and no `match` property | Render a directory, not recommendations |
| Matched doctors | Criteria and returned `match` | Optional “Match score” / “Why this result matches” presentation |
| End | `nextCursor === null` | Disable or hide load-more |
| Invalid traversal | Cursor-related `422` | Clear old cursor/results and restart current criteria |
| Unavailable detail | Concealed `404` | One generic unavailable state |
| Unexpected failure | `500` or transport error | Generic retry/error state with privacy-safe correlation ID |

## 20. Match truthfulness

The matching result is deterministic, versioned, structured, explainable, and
limited to the approved synthetic demo/MVP data. It is:

- not clinically validated;
- not production recommendation logic;
- not probability or confidence;
- not provider quality;
- not medical suitability;
- not an AI, LLM, ML, semantic, or vector result;
- not patient-specific clinical advice.

The frontend must not label results as “Best doctor,” “Recommended by AI,”
“Clinical confidence,” “Probability,” “Doctor quality,” or “Medical
suitability.”

**UX recommendation:** “Match score” and “Why this result matches” are safer
labels when accompanied by clear demo wording. They remain presentation choices,
not required API text.

## 21. Credential truthfulness

The database models `Submitted`, `PendingVerification`, `Verified`, and
`Rejected`, but public DTOs expose none of those state strings and no evidence.
Only credential names admitted by the backend's `Verified` visibility rule can
appear.

For this dataset, `Verified` means verified within the approved synthetic/demo
dataset only. It does not mean government, licensing-board, third-party,
external, or production professional verification. Do not create an
authoritative verification badge, license status, or inferred credential from
the returned name.

## 22. Insurance and location truthfulness

`insurancePlanCode` and `storedInsuranceParticipations` mean exact stored
synthetic/demo directory participation only. They do not establish:

- patient eligibility;
- active coverage;
- current in-network status;
- verified benefits;
- payer confirmation;
- guaranteed coverage.

Phase 7 performs no live payer or network verification.

Location matching uses only exact stored locality, administrative-area, and
country fields on eligible affiliation locations. Phase 7 exposes no
coordinates, geocoding, radius, distance, nearest-doctor ranking, travel time,
or “near me” behavior. `timeZone` is informational stored IANA data, not a
distance or availability signal.

## 23. Synthetic/demo directory warning

All current clinic, doctor, location, specialty, language, insurance,
affiliation, and credential data is synthetic/demo. The frontend must not
present these entities as authoritative real-world providers or professional
records.

**UX recommendation:** Keep a visible demo disclaimer near directory results,
for example: “Synthetic demo directory. Provider, credential, and insurance
information is not real-world verification.” Product/content review owns final
copy.

## 24. Taxonomy endpoint warning

Standalone endpoints for specialties, languages, insurance plans, and
locations are:

> Not defined by the current Phase 7 API contract.

Do not invent `/specialties`, `/languages`, `/insurance-plans`, `/locations`, or
equivalent routes. If filter dropdown discovery is required, that is a separate
frontend/product data-source decision. It must not scrape doctor results and
present the observed values as a complete authoritative taxonomy.

## 25. Security and privacy

Public API does not mean every directory database record is public. The backend
enforces publication, parent, relationship, location, and credential-state
eligibility. Frontend responsibilities are:

- render only returned DTO fields;
- never infer or probe unpublished doctors, clinics, locations, affiliations,
  or credentials;
- keep cursor tokens opaque and out of UI, analytics, and logs;
- never expose matching configuration hashes, package/import metadata, or
  credential evidence;
- never send symptoms, pre-triage narrative, diagnosis, urgency, Clinical
  History, FHIR payloads, patient IDs, or other clinical data to directory
  search;
- avoid storing directory responses as authoritative provider records;
- avoid placing filters or returned names in privacy-sensitive analytics unless
  a separate reviewed policy permits it;
- preserve only a privacy-safe correlation ID for support diagnostics.

## 26. Explicitly unsupported functionality

Phase 7 does not provide:

- ratings or reviews;
- popularity, quality, or “best doctor” scoring;
- live insurance, eligibility, benefits, or network verification;
- appointment availability, scheduling, or Phase 8 behavior;
- clinic/doctor onboarding;
- external credential verification or credential evidence;
- AI, LLM, ML, semantic, or vector matching;
- fuzzy or synonym search;
- coordinates, geocoding, distance, travel time, or nearest-provider search;
- photos, biographies, years of experience, phone numbers, email addresses,
  websites, opening hours, or maps;
- FHIR `Practitioner` or `Organization` mappings;
- patient-personalized or clinical recommendations;
- public matching configuration, matching-rule selection, or matching audit
  APIs;
- standalone taxonomy-discovery endpoints.

The frontend must not fabricate any of these as backend data.

## 27. Frontend tests to implement later

This section recommends frontend tests; this documentation task does not add
frontend code or tests.

### API client

- Calls all four exact routes with `GET`.
- Serializes only exact query names and omits unset values.
- URL-encodes filters and opaque cursors once through `URLSearchParams`.
- Does not require a token for directory reads.
- Passes `AbortSignal` through the established client.
- Maps safe Problem Details and preserves correlation ID.

### Clinics

- Renders summary list fields without per-item detail calls.
- Appends pages in backend order and stops on `nextCursor: null`.
- Handles a successful empty page.
- Renders eligible detail locations and IANA timezone.
- Uses one generic state for concealed `404`.
- Handles filter/page/cursor `422` without exposing cursor data.

### Doctors — neutral

- Sends no criteria for neutral browsing.
- Preserves backend UUID order.
- Treats absent `match` as neutral and does not synthesize one.
- Traverses pages without duplicates or client sorting.

### Doctors — matching active

- Covers each individual criterion and representative combinations.
- Preserves AND/intersection and same-location expectations.
- Renders `ruleVersion`, `matchScore`, factors, states, and explanation data
  directly from the response.
- Never recomputes score, contribution, explanation, or ordering.
- Preserves backend ranked order and traverses ranked ties.
- Clears cursor and accumulated results when any criterion changes.
- Handles mismatched/stale cursor `422` and `200` empty results.
- Never carries search `match` data into doctor detail.

### Truthfulness and security

- Does not display authoritative credential badges.
- Does not claim live insurance, eligibility, coverage, or network status.
- Does not introduce ratings, “best doctor,” AI recommendation, clinical
  confidence, probability, provider quality, medical suitability, distance, or
  appointment availability.
- Does not send clinical/pre-triage/FHIR data in directory queries.
- Does not log or display opaque cursor/configuration/evidence data.
- Keeps the synthetic/demo disclaimer visible where product design requires it.

## 28. Backend contract versus UX recommendation

**Backend contract** means behavior proven by endpoint code, DTOs, use cases,
repositories, tests, and OpenAPI: routes, fields, exact filters, statuses,
visibility, ordering, matching, and cursor behavior.

**UX recommendation** means suggested cards, labels, empty states, loading
behavior, disclaimers, and navigation. The backend does not prescribe a route
hierarchy, component framework, state library, filter-control design, or exact
copy.

If a needed behavior or field is absent above, treat it as:

> Not defined by the current Phase 7 API contract.

Do not infer a hidden contract from database terminology or synthetic fixture
content.

## 29. Frontend integration checklist

- [ ] Exactly four Phase 7 endpoints are used.
- [ ] Anonymous/public behavior is respected.
- [ ] Exact query names and JSON contracts are used.
- [ ] Codes and location values retain exact case and stored-value semantics.
- [ ] Query parameters are not repeated and unsupported names are not sent.
- [ ] Default page size 20 and range 1–100 are respected.
- [ ] Cursors are treated as opaque.
- [ ] Cursor and accumulated results are cleared when filters change.
- [ ] Backend ordering is preserved.
- [ ] Score, contributions, factors, and explanations are never recomputed.
- [ ] `match` is rendered only when the backend returns it.
- [ ] Exact `ruleVersion`, `matchScore`, and factor fields are consumed.
- [ ] Neutral no-criteria behavior is handled.
- [ ] Ranked criteria-active behavior is handled.
- [ ] `200` empty pages are normal UI states.
- [ ] Concealed `404` uses one unavailable state.
- [ ] Known `422` validation codes are handled safely.
- [ ] No unpublished-resource inference is attempted.
- [ ] No external credential-verification claim is made.
- [ ] No live insurance/coverage/network claim is made.
- [ ] No clinical confidence, probability, quality, or suitability claim is made.
- [ ] No ratings or reviews are fabricated.
- [ ] No AI/LLM/ML/vector matching claim is made.
- [ ] No geocoding, distance, travel-time, or “near me” behavior is invented.
- [ ] No scheduling or availability assumption is made.
- [ ] No taxonomy endpoints are invented.
- [ ] No clinical, pre-triage, patient, Clinical History, or FHIR data is sent to
  directory search.
- [ ] Synthetic/demo nature is preserved.
- [ ] Existing Beeexy API, cancellation, error, correlation, and state
  conventions are reused.
- [ ] Phase 8 behavior is not assumed or implemented.
