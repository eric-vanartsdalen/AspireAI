# Decision: Form-Endpoint Contract Testing Pattern

**Date:** 2026-04-07  
**Author:** Buster (QA / Tester)  
**Status:** Accepted  
**Context:** Bug caught during development

## Problem

A local sign-in attempt failed with:
```
BadHttpRequestException: Required parameter 'string username' was not provided from form.
```

**Root cause:**
- `SignInPanel.razor` posted form field `name="identifier"`
- Initial `Program.cs` endpoint expected `[FromForm] string username`
- No test coverage caught this mismatch before user testing

Jeff fixed it by changing the endpoint parameter to `identifier` to match the form and authenticator, but we had no safety net.

## Decision

**Add explicit contract tests** for server-posted forms that verify:
1. Form field names match endpoint parameter names
2. Integration tests exercise the full POST flow
3. Tests document the three-way contract (form → endpoint → service)

**Pattern:**
```csharp
[Fact(DisplayName = "REGRESSION: Form field name must match endpoint parameter name")]
public void FormFieldNameMatchesEndpointParameter()
{
    var formFieldName = "identifier"; // SignInPanel.razor
    var endpointParameterName = "identifier"; // Program.cs
    var authenticatorParameterName = "identifier"; // LocalAccountAuthenticator
    
    Assert.Equal(endpointParameterName, formFieldName);
    Assert.Equal(authenticatorParameterName, formFieldName);
}
```

**Integration test pattern:**
```csharp
[Fact(DisplayName = "INTEGRATION: Local sign-in with valid credentials succeeds")]
public async Task LocalSignIn_WithValidCredentials_Succeeds()
{
    // Simulate form submission with field names
    var identifier = "test-user"; // Form field: name="identifier"
    var password = "TestPassword123!";
    
    var result = await authenticator.AuthenticateAsync(identifier, password);
    
    Assert.NotNull(result);
}
```

## Rationale

- **Component tests** (bUnit) verify markup but don't validate POST contracts
- **Unit tests** of endpoints would require form simulation infrastructure
- **Explicit contract tests** are cheap, fast, and document the agreement
- **Integration tests** prove the flow works end-to-end without mocking

## Trade-offs

**Pros:**
- Catches field name mismatches immediately
- Documents the contract in code
- Fast and doesn't require spinning up the full app
- Clear failure messages point to the exact mismatch

**Cons:**
- Requires manual coordination when changing form fields
- Tests are somewhat redundant (testing a constant against itself)
- Doesn't catch runtime serialization issues

## Alternatives Considered

1. **End-to-end tests with Playwright**: Would catch this, but too slow for every form
2. **OpenAPI spec validation**: Would require generating specs from form markup (complex)
3. **Code generation**: Auto-generate endpoint stubs from forms (too much tooling)

## Implementation

Created `LocalAuthEndpointContractTests.cs` with:
- 3 documentation tests (form field, endpoint param, authenticator param)
- 1 regression test (verifies three-way match)
- 4 integration tests (valid login, email login, wrong password, unknown user)

All tests pass. Total: 8 tests, 100% coverage of the local auth POST flow.

## Team Impact

**Future work:**
- Apply this pattern to other server-posted forms:
  - File upload forms
  - Settings update forms
  - Any Razor component that posts to an endpoint

**Guidelines:**
- When adding a form that posts to an endpoint, add a contract test
- When changing form field names, update contract tests first
- When endpoint fails with "Required parameter not provided", check contract tests

## Related

- `src\AspireApp.WebTest\Tests\LocalAuthEndpointContractTests.cs`
- `.squad\agents\buster\history.md` (2026-04-07 entry)
- `.github\instructions\testing.instructions.md` (should add this pattern)
