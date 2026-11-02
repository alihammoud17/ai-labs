# /ask test set v1

30 questions for `POST /ask`, each with its expected behaviour, its source in the corpus and a gold answer. It replaces the 20 questions scattered across the week 4 results, and it's the input for every eval from week 5 on.

- **Corpus:** `data/corpus/` (MultiTenantSaaSApi docs at `c87cc20`): `README.md`, `docs/architecture.md`, `docs/contracts.md`, `docs/decisions.md`
- **Gold labelled:** 2026-11-02, before any run of this set
- **Line numbers** are lines in the corpus file. A retrieved chunk counts as a hit when it comes from the same file and its line range contains the answer line. This holds for any chunker, so the set doesn't need relabelling when chunking changes.

## Fields

| Field | Meaning |
|---|---|
| expected | `answer`: the docs answer it. `partial`: the docs answer only part, and the rest must not be invented. `abstain`: not in the docs, so the reply must say so. `gate`: unrelated, so it's stopped before the model |
| source | file, section, and the line(s) holding the answer. When two sources are listed, either one answers alone unless the row says "both needed" |
| check | `exact`: the gold string appears in the answer. `all items`: every listed item is present. `judge`: pass/fail against the gold answer (by hand for now). `abstain`: the reply says it's not in the docs, and the gate row doesn't call the model |

## Mix

| Category | Rows | IDs |
|---|---|---|
| Single fact | 5 | A1, A3, N1, N2, N3 |
| Exact name or number | 4 | F1, F4, F5, G2 |
| List | 3 | A15, B7, N4 |
| Two sources needed | 5 | B3, B6, G1, N5, N6 |
| False premise | 3 | C1, C3, N8 |
| Typo or paraphrase | 2 | H6, N7 |
| Partial | 2 | D1, D3 |
| Not in the docs | 4 | D2, D9, INV, N9 |
| Out of domain | 1 | E5 |
| Non-English | 1 | H5 |
| **Total** | **30** | 25 answerable (24 in English), 5 that shouldn't be answered |

## Index

| ID | Question | Expected | Source doc | Check |
|---|---|---|---|---|
| A1 | What does tenant registration return? | answer | architecture.md | judge |
| A3 | What is the current internal billing contract version? | answer | contracts.md | exact |
| N1 | Does the API health endpoint check Redis? | answer | README.md / architecture.md | judge |
| N2 | What happens to a tenant's refresh tokens when it's suspended, and do they come back when it's reactivated? | answer | contracts.md | judge |
| N3 | When does the public API need a new major version? | answer | decisions.md | judge |
| F1 | What is the signature in the HMAC test vector? | answer | contracts.md | exact |
| F4 | What is the internal billing callback endpoint? | answer | contracts.md / README.md | exact |
| F5 | Which environment variable sets the JWT secret? | answer | README.md | exact |
| G2 | What rate limit applies to password reset? | answer | architecture.md | exact |
| A15 | What does the consumer verify on a billing callback? | answer | contracts.md | all items |
| B7 | List every rate limit in the system. | answer | architecture.md | all items |
| N4 | Which headers come with an outbound webhook delivery? | answer | contracts.md | all items |
| B3 | Is the Stripe integration live? | answer | README.md + architecture.md | judge |
| B6 | What's the MFA rate limit, and which PostgreSQL version is required? | answer | architecture.md + README.md | all items |
| G1 | What is the contract version? | answer | contracts.md (two sections) | all items |
| N5 | Where does BillingService keep its retry and dead-letter state, and can it run as several instances? | answer | architecture.md + decisions.md | judge |
| N6 | Can CORS origins that tenants register themselves be used with credentials? | answer | decisions.md + README.md | judge |
| C1 | Is the API unauthenticated? | answer | architecture.md | judge |
| C3 | Is there an account lockout after failed logins? | answer | architecture.md | judge |
| N8 | Is webhook signing-secret rotation fully automated? | answer | contracts.md / architecture.md | judge |
| H6 | wat status dos tenant registraton retrun | answer | architecture.md / README.md | judge |
| N7 | I just cloned the repo. Which commands get it working on my machine? | answer | README.md | all items |
| D1 | How long do access tokens last by default? | partial | architecture.md / README.md | judge |
| D3 | How many times does the webhook dispatcher retry? | partial | contracts.md | judge |
| D2 | How long is a refresh token valid? | abstain | none | abstain |
| D9 | What is multi-tenancy? | abstain | none | abstain |
| INV | How are invoices emailed? | abstain | none | abstain |
| N9 | How long are audit logs kept? | abstain | none | abstain |
| E5 | How do I center a div in CSS? | gate | none | abstain |
| H5 | Que retourne l'inscription d'un tenant ? | answer | architecture.md / README.md | judge |

---

## Gold answers

### Single fact

**A1. What does tenant registration return?**
- Source: `docs/architecture.md` › Authentication and authorization, line 38. Also `README.md` › Implemented capabilities, line 9 (status only).
- Gold: `202 Accepted` with `{ tenantId, status: "pending_verification", verificationExpiresAt }` and no tokens.
- Evidence: "A successful registration returns `202 Accepted` with `{ tenantId, status: "pending_verification", verificationExpiresAt }` and no tokens."
- Pass if: it gives 202, the pending-verification status and no tokens.

**A3. What is the current internal billing contract version?**
- Source: `docs/contracts.md` › Current version and body, line 45.
- Gold: `2026-03-18`
- Evidence: "Current contract version: `2026-03-18`."

**N1. Does the API health endpoint check Redis?**
- Source: `README.md` › Endpoints and API discovery, line 87. Also `docs/architecture.md` › Observability, line 88.
- Gold: No. It checks the process and PostgreSQL, not Redis.
- Evidence: "The API health endpoint checks the process and PostgreSQL, but not Redis."
- Pass if: it says no and names what is checked.

**N2. What happens to a tenant's refresh tokens when it's suspended, and do they come back when it's reactivated?**
- Source: `docs/contracts.md` › Internal tenant operations › Behavior, lines 117–118.
- Gold: Suspension revokes every refresh token of the tenant. Reactivation doesn't restore them; they stay revoked.
- Evidence: "every refresh token of the tenant revoked" and "Refresh tokens revoked by the suspension stay revoked."
- Pass if: both halves are there.

**N3. When does the public API need a new major version?**
- Source: `docs/decisions.md` › Public APIs use URL-segment versions, line 111.
- Gold: Only for breaking contract changes. Backward-compatible additions don't need one.
- Evidence: "A new major API version is reserved for breaking contract changes. Backward-compatible additions do not require a new major version."

### Exact name or number

**F1. What is the signature in the HMAC test vector?**
- Source: `docs/contracts.md` › Internal billing subscription events › Authentication, line 39.
- Gold: `sha256=b82b6991c877ed60e6a337a1928478d323770ca01846828fa0a77c424b19d8ae`
- Note: week 4, llama3.2 had the right signature but cited the wrong chunk (M2).

**F4. What is the internal billing callback endpoint?**
- Source: `docs/contracts.md` › Endpoint, line 12. Also `README.md` › Endpoints and API discovery, line 81.
- Gold: `POST /api/internal/billing/subscription-events`

**F5. Which environment variable sets the JWT secret?**
- Source: `README.md` › Configuration summary, line 71.
- Gold: `Jwt__Secret`
- Note: week 4, llama3.2 added a citation that doesn't hold the secret (M2).

**G2. What rate limit applies to password reset?**
- Source: `docs/architecture.md` › Authentication and authorization, line 46.
- Gold: 10 per minute, a budget shared by the anonymous account-recovery routes (separate from the register and login budget).
- Check: `10 per minute` (exact). Saying it's shared with register and login is a fail.

### Lists

**A15. What does the consumer verify on a billing callback?**
- Source: `docs/contracts.md` › Validation and idempotency, lines 69–76.
- Gold, all six:
  1. the contract version is supported
  2. required fields and timestamps are valid
  3. tenant and subscription IDs map to API-owned records
  4. the subscription belongs to the stated tenant
  5. a target plan exists when required
  6. the event has not already been applied

**B7. List every rate limit in the system.**
- Source: `docs/architecture.md` › Authentication and authorization, line 46.
- Gold, all five:
  1. register and login: 10 per minute, shared
  2. refresh: 30 per minute
  3. anonymous account-recovery routes: 10 per minute, shared
  4. MFA code checks: 5 attempts per 5 minutes per user
  5. a plan-aware, Redis-backed monthly limiter for authenticated tenant traffic
- Note: week 4, llama3.2 invented limits and left out refresh. Haiku was cut off at 800 output tokens; fixed at 2000.

**N4. Which headers come with an outbound webhook delivery?**
- Source: `docs/contracts.md` › Outbound tenant webhooks › Delivery headers, lines 132–136.
- Gold, all five:
  - `X-Tenant-Webhook-Contract-Version`
  - `X-Tenant-Webhook-Timestamp`
  - `X-Tenant-Webhook-Delivery`
  - `X-Tenant-Webhook-Idempotency-Key`
  - `X-Tenant-Webhook-Signature`
- Watch for: the billing headers (`X-Billing-*`) or the operations headers (`X-Operations-*`) mixed in.

### Two sources needed

**B3. Is the Stripe integration live?**
- Source, both:
  - `README.md` › Current limitations, lines 101–102
  - `docs/architecture.md` › BillingService, line 67
- Gold: No. The Stripe gateway is implemented and tested but isn't connected to the default runtime flow. The runtime selects the placeholder webhook adapter even when the provider is set to `stripe`.
- Pass if: it says no and gives at least one reason.

**B6. What's the MFA rate limit, and which PostgreSQL version is required?**
- Source, both needed:
  - `docs/architecture.md` › Authentication and authorization, line 46
  - `README.md` › Prerequisites, line 38
- Gold: 5 attempts per 5 minutes per user; PostgreSQL 16 or later.

**G1. What is the contract version?**
- Source, both needed:
  - `docs/contracts.md` › Current version and body, line 45
  - `docs/contracts.md` › Outbound tenant webhooks › Envelope, line 128
- Gold: The question is ambiguous. There are two contracts: the internal billing callback is `2026-03-18` and outbound tenant webhooks are `2026-04-13`.
- Pass if: it gives both, or asks which contract is meant. Giving only one, silently, is a fail.

**N5. Where does BillingService keep its retry and dead-letter state, and can it run as several instances?**
- Source, both:
  - `docs/architecture.md` › Persistence and background processing, line 84
  - `docs/decisions.md` › Current billing durability is file-backed, line 125
- Gold: In a local JSON file. It isn't a distributed queue: it suits single-instance use only, not several instances.
- Evidence: "This is not a distributed queue and assumes controlled single-instance file access."

**N6. Can CORS origins that tenants register themselves be used with credentials?**
- Source, both:
  - `docs/decisions.md` › Tenants can register their own CORS origins, without credentials, line 137
  - `README.md` › Current limitations, line 105
- Gold: No. Tenant-registered origins never get `Access-Control-Allow-Credentials`; only the origins in `Cors:AllowedOrigins` do. On a shared host, one tenant's origin is accepted for every tenant's routes.
- Pass if: it says no and names `Cors:AllowedOrigins` as the only origins that get credentials.

### False premise

**C1. Is the API unauthenticated?**
- Source: `docs/architecture.md` › Authentication and authorization, line 34. Supporting: line 46.
- Gold: No. It uses JWT Bearer authentication with issuer, audience, lifetime and signing-key validation. Only some auth routes (register, login, refresh, account recovery) are anonymous, and those are rate-limited.
- Pass if: it rejects the premise.

**C3. Is there an account lockout after failed logins?**
- Source: `docs/architecture.md` › Authentication and authorization, line 46.
- Gold: No, there is no account lockout. Login is limited by the shared register-and-login budget of 10 requests per minute per client.
- Pass if: it says no. The rate limit is a bonus.

**N8. Is webhook signing-secret rotation fully automated?**
- Source: `docs/contracts.md` › Rotation limitation, line 152. Also `docs/architecture.md` › Outbound tenant webhooks, line 73.
- Gold: No. Endpoint management can only start a rotation and issue a pending secret. Active cutover and finalization are future work, and a pending secret doesn't mean the rotation is complete.

### Typo or paraphrase

**H6. wat status dos tenant registraton retrun**
- Source: `docs/architecture.md`, line 38. Also `README.md`, line 9.
- Gold: same as A1.
- Note: the typo version of A1. Compare the two runs: the difference shows what the typos cost.

**N7. I just cloned the repo. Which commands get it working on my machine?**
- Source: `README.md` › Quick start, lines 48–58.
- Gold:
  1. From the repository root: `scripts/dev.sh bootstrap`, `scripts/dev.sh seed`, `scripts/dev.sh run`
  2. In another shell: `scripts/dev.sh smoke` and `scripts/dev.sh test`
- Pass if: bootstrap, seed and run are there, in that order.
- Note: no "quick start" or "setup" in the question, so retrieval has to match on meaning.

### Partial

**D1. How long do access tokens last by default?**
- Source: `docs/architecture.md` › Authentication and authorization, line 34. Also `README.md` › Configuration summary, line 71.
- Gold: The docs give no default. The lifetime follows `Jwt:ExpirationMinutes` (`Jwt__ExpirationMinutes`).
- Pass if: it names the setting and says the docs give no value. Any number is a fail.
- Note: week 4, llama3.2 invented "default 15 minutes" (M1).

**D3. How many times does the webhook dispatcher retry?**
- Source: `docs/contracts.md` › Delivery and replay behavior, line 142. Supporting: `docs/architecture.md`, line 71.
- Gold: The docs give no number. Failed deliveries are retried according to dispatcher policy until success or terminal exhaustion.
- Pass if: it says no number is documented and describes the policy. Any number is a fail.

### Not in the docs

Each one was checked against the corpus before labelling.

**D2. How long is a refresh token valid?**
- Gold: not in the docs.
- Searched: "refresh". The corpus has only rotation and revocation, the refresh budget (30 per minute) and the access token's `Jwt:ExpirationMinutes`.
- Trap: answering with the access-token setting.

**D9. What is multi-tenancy?**
- Gold: not in the docs. The docs describe this system, not the general term.
- Trap: a general-knowledge definition (M4). llama3.2 did exactly this in week 4.

**INV. How are invoices emailed?**
- Gold: not in the docs.
- Searched: "invoice". The corpus has only "billing invoice history" indexes and Stripe "invoice synchronization".
- Note: `ask-v1.md` uses this topic in its example, and once returned the example sentence word for word. Keep the row, but read a failure here with that in mind.

**N9. How long are audit logs kept?**
- Gold: not in the docs.
- Searched: "retention", "retain", "days", "audit". The audit sections cover tenant scope, analytics and entry contents, with no retention period. "Retained" appears only once, about the historical docs archive (`decisions.md`, line 3).

### Out of domain

**E5. How do I center a div in CSS?**
- Gold: stopped at the gate, with no model call. In week 4, its best distance was 0.5938 against a `MaxDistance` of 0.50.

### Non-English

**H5. Que retourne l'inscription d'un tenant ?**
- Gold: same as A1, ideally in French.
- **Expected to fail today.** The embeddings are English-only, and in week 4 the chunk with the full answer wasn't in the top 10 (R1). Report it separately; it isn't counted against the retrieval threshold.
