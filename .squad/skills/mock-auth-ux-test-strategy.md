# Skill: Mock Authentication Test Strategy

**Domain:** QA / Testing  
**Category:** UX & Integration Testing  
**Status:** Validated (2026-04-05)  
**Difficulty:** Medium  

---

## Overview

When planning UI authentication flows that will later wire real OAuth providers (Google, Microsoft), *don't implement real auth yet.* Instead:

1. **Stage mock auth in testable gates** (unauthenticated → mock login → contract audit → provider pluggability)
2. **Prove contract & pattern before provider logic** (swap providers via config, not code)
3. **Validate cross-service tenant isolation** (auth layer must not break existing data scoping)

This skill captures the multi-layer test strategy and gate sequence for such flows.

---

## Quick Reference

| Phase | Test Artifact | Gate | Pass Criteria |
|-------|---------------|------|---------------|
| 1. Unauthenticated UX | `LandingPageTests.cs` | Sign-in buttons visible; no auth required | ✅ 200 OK, buttons rendered |
| 2. Mock auth contract | `MockAuthEndpointTests.cs` | POST /auth/login returns token & user DTO | ✅ Token issued, tenant ID in response |
| 3. Cross-service audit | `test_p0_auth_contract_audit.py` | Tenant ID persists through auth layer | ✅ Python DB has tenant ID |
| 4. E2E sign-in flow | `AuthFlowE2ETests.cs` (Playwright) | Mock login succeeds, user email displays | ✅ No errors, post-login state visible |
| 5. Provider pluggability | `AuthProviderFactoryTests.cs` | Config-only provider swap (no code changes) | ✅ All providers load cleanly |

---

## The Strategy

### Layer Model (Bottom-Up Testing)

```
LAYER                          XUNIT / PYTEST              GATES (& what can break)
─────────────────────────────────────────────────────────────────────────────────
UI Interaction (Playwright)   → AuthFlowE2ETests.cs        ✅ Mock sign-in visual flow
Component State (Razor)       → (optional) Bunit            ⚠️  Skippable if logic is simple
Integration (C# API)          → MockAuthEndpointTests.cs   ✅ POST /auth/login contract
Service (C# provider)         → AuthProviderFactoryTests   ✅ Pluggable backend swap
Cross-Service (Python audit)  → test_p0_auth_contract... → ✅ Tenant isolation
```

**Key:** Test each layer in isolation first, then end-to-end.

---

## Gate Sequence (Do NOT Skip)

### Gate 1: Unauthenticated Landing Works

**Test:** `LandingPageTests.cs`

```csharp
[Fact]
public async Task UnauthenticatedLanding_ShowsSignInOptions()
{
    var client = CreateUnauthenticatedHttpClient();
    var response = await client.GetAsync("/");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    
    var html = await response.Content.ReadAsStringAsync();
    Assert.Contains("Sign in with Google", html);
    Assert.Contains("Sign in with Microsoft", html);
    Assert.DoesNotContain("Chat", html); // Authenticated-only hidden
}

[Fact]
public async Task UnauthenticatedAccess_ToChatPage_Returns401Or_Redirect()
{
    var client = CreateUnauthenticatedHttpClient();
    var response = await client.GetAsync("/chat");
    // Either 401 (explicit deny) or redirect to /login (implicit deny)
    Assert.True(response.StatusCode == HttpStatusCode.Unauthorized 
                || response.StatusCode == HttpStatusCode.Redirect);
}
```

**Why first:** Proves landing page exists and sign-in flow is discoverable before wiring any auth logic.

---

### Gate 2: Mock Auth Endpoint Contract

**Test:** `MockAuthEndpointTests.cs`

```csharp
[Fact]
public async Task MockAuthEndpoint_LoginWithEmail_ReturnsTokenAndUser()
{
    var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
    var request = new { email = "alice@example.com", provider = "mock" };
    
    var response = await client.PostAsJsonAsync("/auth/login", request);
    response.EnsureSuccessStatusCode();
    
    var result = await response.Content.ReadFromJsonAsync<AuthLoginResponse>();
    Assert.NotNull(result.AccessToken);
    Assert.Equal("alice@example.com", result.User.Email);
    Assert.Equal("default", result.User.TenantId); // ← Tenant scoping proof
}

[Fact]
public void AuthProviderFactory_LoadsConfiguredProvider()
{
    var config = new Dictionary<string, string>
    {
        { "Auth:Provider", "Mock" },
        { "Auth:Mock:DefaultUserId", "test-user" }
    };
    var provider = AuthProviderFactory.CreateProvider(
        new ConfigurationBuilder().AddInMemoryCollection(config).Build()
    );
    
    Assert.IsType<MockAuthProvider>(provider);
}
```

**Why second:** Proves the mock auth backend works before E2E wiring.

---

### Gate 3: Cross-Service Tenant Audit

**Test:** `test_p0_auth_contract_audit.py` (pytest)

```python
def test_authenticated_request_preserves_tenant_id():
    """After mock auth, tenant ID must flow to Python DB."""
    # Arrange
    client = AuthTestClient(provider="mock", user_email="alice@example.com")
    
    # Act: Call API that forwards to Python
    response = client.upload_document(
        file_path="test.pdf",
        file_name="test.pdf"
    )
    assert response.status_code == 200
    doc_id = response.json()["id"]
    
    # Assert: Tenant ID persisted in Python DB
    db = get_test_database()
    file_record = db.query("SELECT tenant_id FROM files WHERE id = %s", (doc_id,))
    assert file_record["tenant_id"] == "default"

def test_unauthenticated_request_returns_401():
    """Endpoints must reject unauthenticated requests."""
    response = requests.get("http://localhost:5000/api/documents")
    assert response.status_code == 401
```

**Why third:** Proves tenant isolation is **not broken** by auth layer.

---

### Gate 4: E2E Sign-In Flow (Playwright)

**Test:** `AuthFlowE2ETests.cs`

```csharp
[Fact, Priority(100)]
public async Task AuthFlow_MockProvider_SignInAndRedirect()
{
    await WithPageAsync(async page =>
    {
        // Navigate to landing
        await page.GotoAsync("http://localhost:5000/", _options);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Assert: Sign-in buttons visible
        var mockBtn = page.Locator("button:has-text('Sign in with Mock')");
        await mockBtn.WaitForAsync();
        
        // Click mock provider
        await mockBtn.ClickAsync();
        
        // Redirect to auth page
        await page.WaitForURLAsync(
            url => url.Contains("/auth") || url.Contains("/login"),
            new PageWaitForURLOptions { Timeout = 5000 }
        );
        
        // Enter email and sign in
        var emailInput = page.Locator("input[type='email']");
        await emailInput.FillAsync("test@example.com");
        var submitBtn = page.Locator("button:has-text('Sign In')");
        await submitBtn.ClickAsync();
        
        // Redirect to authenticated landing
        await page.WaitForURLAsync(
            url => !url.Contains("/auth") && !url.Contains("/login"),
            new PageWaitForURLOptions { Timeout = 10000 }
        );
        
        // User email visible
        var userDisplay = page.Locator("[data-testid='current-user']");
        var userName = await userDisplay.TextContentAsync();
        Assert.Contains("test@example.com", userName);
        
        // Tenant selector still works (no regression)
        var tenantSelector = page.Locator("#tenant-select");
        await tenantSelector.WaitForAsync();
        Assert.True(await tenantSelector.IsVisibleAsync());
    });
}
```

**Why fourth:** Proves UI flow works end-to-end after backend is validated.

---

### Gate 5: Provider Pluggability

**Test:** `AuthProviderFactoryTests.cs`

```csharp
[Theory]
[InlineData("mock")]
[InlineData("google")]  // Future; configuration exists but impl deferred
[InlineData("microsoft")]  // Future; configuration exists but impl deferred
public void AuthProviderFactory_SupportsAllRegisteredProviders(string providerName)
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            { "Auth:Provider", providerName },
            { $"Auth:{providerName}:ClientId", "test-id" }
        })
        .Build();
    
    var provider = AuthProviderFactory.CreateProvider(config);
    Assert.NotNull(provider);
    // If not yet implemented, returns "not-ready" handler (not null-ref)
}

[Fact]
public void AuthProviderFactory_ThrowsOnUnknownProvider()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            { "Auth:Provider", "UnknownProviderX" }
        })
        .Build();
    
    Assert.Throws<InvalidOperationException>(() =>
        AuthProviderFactory.CreateProvider(config)
    );
}
```

**Why fifth:** Proves provider can be swapped via config only (enabling later Google/Microsoft wiring without re-testing all previous gates).

---

## What NOT to Test Yet

❌ Real OAuth callback handling  
❌ Encrypted token storage  
❌ Session timeout / refresh token lifecycle  
❌ MFA flow  
❌ PKCE or SAML integration  

**Rationale:** These are implementation details of *specific providers*. Mock auth proves the *contract shape*. Once mock is stable, add provider-specific logic without rearchitecting.

---

## Contract Design (Before Implementation)

### Request Shape
```json
POST /auth/login
{
  "email": "user@example.com",
  "provider": "mock"
}
```

### Response Shape
```json
{
  "accessToken": "jwt-token-xxx",
  "user": {
    "userId": "user-123",
    "email": "user@example.com",
    "tenantId": "default"
  }
}
```

### Key: Tenant ID in Response

The response **must** include `tenantId`. This proves the contract binds authentication to tenant context *at the API boundary*, not just at the database layer.

---

## Service Pattern to Mirror

```csharp
// Mirrors TenantContextService (existing pattern)
public class AuthContextService
{
    private UserContext? _currentUser;
    
    public UserContext? CurrentUser => _currentUser;
    public event Action? OnAuthStateChanged;
    
    public async Task LoginAsync(string email, string provider)
    {
        var result = await _authClient.PostAsync(
            "/auth/login",
            new { email, provider }
        );
        _currentUser = new UserContext(result.User);
        OnAuthStateChanged?.Invoke();
    }
    
    public void Logout()
    {
        _currentUser = null;
        OnAuthStateChanged?.Invoke();
    }
}

public record UserContext(string UserId, string Email, string TenantId);
```

**Why:** Scoped to Blazor session; event-driven state updates; mirrors existing pattern (reduces cognitive load).

---

## Regression Testing: Tenant Selector Must Still Work

```csharp
[Fact]
public async Task PostAuth_TenantSelector_StillFunctional()
{
    // Login via mock auth
    var client = AuthTestClient(provider="mock", user_email="alice@example.com");
    
    // Navigate to authenticated page
    var page = await client.GetPageAsync("/dashboard");
    
    // Assert: Tenant selector visible and can change tenant
    var tenantSelect = page.Locator("#tenant-select");
    await tenantSelect.WaitForAsync();
    
    await tenantSelect.SelectOptionAsync("tenant-a");
    
    // Documents now scoped to tenant-a
    var docs = await client.GetDocumentsAsync();
    Assert.All(docs, doc => Assert.Equal("tenant-a", doc.TenantId));
}
```

**Why:** Auth layer must not break existing data isolation; this is a critical regression gate.

---

## CI/CD Integration

```yaml
name: Auth Feature Gates
on: [pull_request]

jobs:
  auth-tests:
    runs-on: ubuntu-latest
    steps:
      - name: Landing Page Tests
        run: dotnet test src/AspireApp.WebTest --filter "Category=AuthUI"
      
      - name: Mock Auth Endpoint Tests
        run: dotnet test src/AspireApp.WebTest --filter "Category=AuthContract"
      
      - name: Auth × Tenant Contract Audit
        run: pytest src/AspireApp.PythonServices/tests/test_p0_auth_contract_audit.py -v
      
      - name: End-to-End Auth Flow
        run: dotnet test src/AspireApp.WebTest --filter "Category=AuthE2E"
      
      - name: Tenant Selector Regression
        run: dotnet test src/AspireApp.WebTest --filter "Category=TenantContext"
```

**Strategy:** Stagger by layer; fail fast on contract breaks; allow E2E to timeout gracefully.

---

## When to Move to Real Providers

✅ **Proceed to Google/Microsoft wiring only when:**

1. All 5 gates pass consistently
2. Tenant isolation audit passes (test_p0_auth_contract_audit.py)
3. E2E sign-in flow stable (no flaky timeouts)
4. Provider factory test passes (no code changes needed to add Google/Microsoft)
5. Tenant selector regression test passes (no impact on data layer)

**Then:**
- Implement `GoogleAuthProvider : IAuthProvider`
- Implement `MicrosoftAuthProvider : IAuthProvider`
- Configuration update: `"Auth:Provider": "google"`
- Existing 5 gates + provider-specific tests all pass without modification

---

## Decision Log

- **2026-04-05:** Strategy designed and validated against current codebase state
- **2026-04-05:** 5-gate sequence defined; contract shape finalized
- **2026-04-05:** Skill extracted for reuse in future auth/UX work
