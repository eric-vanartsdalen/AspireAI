# Session Log: Upload Authentication Regression Fix

**Date:** 2026-04-09T15:39:11Z  
**Topic:** Upload authentication regression after tenant hardening

---

## Scope

Fixed upload flow regression where UploadData component was self-HTTPing through unauthenticated /api/FileUpload boundary, breaking tenant context after tenant scoping was hardened in Web layer.

---

## Agents & Outcomes

1. **Jeff (Developer)** — Removed HTTP self-call pattern; injected FileStorageService directly into UploadData circuit as scoped dependency. Tenant context now naturally preserved. Build passed.

2. **Buster (QA)** — Hardened regression coverage: AuthenticatedUploadUxTests now verifies backend persistence via authenticated client; OperationalUploadStoreTests uses user's actual tenant instead of demo tenant.

---

## Result

- ✓ UploadData → FileStorageService (scoped, in-circuit)
- ✓ No HTTP self-call boundary
- ✓ Tenant context preserved across upload pipeline
- ✓ Build success (WebTest project)
- ✓ Regression coverage tightened

---

## Next Steps

- Monitor upload flow for regression in E2E tests
- Tenant multi-tenancy UI (NavMenu selector, session state) remains in backlog
