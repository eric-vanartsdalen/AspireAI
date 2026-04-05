# Authentication Setup Guide

This guide walks you through setting up live sign-in with **Microsoft consumer accounts** and preparing the app infrastructure for **Google account support** (not yet implemented in the app).

**Current State:**
- ✅ **Microsoft Entra ID consumer authentication** is implemented and wired in.
- ❌ **Google OAuth** is not yet implemented in the app code. See [Google Setup](#google-setup-future-work) for external provider setup that you can prep now.

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Microsoft Consumer Authentication](#microsoft-consumer-authentication)
3. [Google Setup (Future Work)](#google-setup-future-work)
4. [Local Development Configuration](#local-development-configuration)
5. [Testing the Setup](#testing-the-setup)
6. [Troubleshooting](#troubleshooting)

---

## Quick Start

### For Local Testing (Mock Auth)

No setup required. The app defaults to **mock authentication** with demo accounts. Start the app:

```bash
dotnet run --project src/AspireApp.AppHost
```

Open the **Aspire dashboard** (shown in the terminal output on startup) and click the webfrontend endpoint URL. Navigate to `/signin` and select a demo provider to test the UI flow.

### For Live Microsoft Sign-In

1. Register an app in **Azure Portal** (see [Microsoft App Registration](#microsoft-app-registration) below)
2. Store Client ID and Client Secret in **user secrets** locally
3. Ensure **Redirect URIs** are registered in Azure
4. Verify the app routes are wired (they are by default)
5. Restart the app — it will auto-detect Microsoft config and enable live sign-in

---

## Microsoft Consumer Authentication

### Microsoft App Registration

**Where:** [Azure Portal — App Registrations](https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)

**Steps:**

1. **Sign in** to Azure Portal with your Microsoft account.
2. **Create new registration:**
   - Click **New Registration**.
   - **Name:** `AspireAI Dev` (or your preferred name).
   - **Supported account types:** Select **"Accounts in any organizational directory (any Azure AD directory — Multitenant) and personal Microsoft accounts (e.g. Skype, Xbox)"** to allow consumer sign-in.
   - **Redirect URI (optional for now):** Leave blank; you'll add it next.
   - Click **Register**.

3. **Note your Application (client) ID:**
   - On the **Overview** page, copy the **Application (client) ID** — you'll need this shortly.

4. **Add Redirect URIs:**
   - Go to **Authentication** (left sidebar).
   - Under **Redirect URIs**, click **+ Add URI**.
   - **Important:** Aspire assigns the webfrontend port dynamically. Start the app once (`dotnet run --project src/AspireApp.AppHost`), open the Aspire dashboard, and note the webfrontend HTTPS and HTTP URLs shown there (e.g., `https://localhost:XXXXX`). Use those URLs for the redirect URIs below.
   - Add URIs using **your actual ports**:
     - `https://localhost:<HTTPS_PORT>/signin-oidc-microsoft` (local HTTPS — primary)
     - `http://localhost:<HTTP_PORT>/signin-oidc-microsoft` (local HTTP fallback)
   - Also add a **Sign-out redirect URI** if you want federated sign-out (optional for local testing):
     - `https://localhost:<HTTPS_PORT>/signout-callback-oidc-microsoft`
   - **Tip:** Azure allows multiple redirect URIs, so you can register several `localhost:<port>` variants if your port changes between runs. The port only changes when Aspire cannot bind to the previously used port.
   - Click **Save**.

5. **Create a Client Secret:**
   - Go to **Certificates & Secrets** (left sidebar).
   - Click **+ New Client Secret**.
   - **Description:** `Local Dev` (or similar).
   - **Expires:** Choose an expiration (e.g., 24 months).
   - Click **Add**.
   - **Important:** Copy the **Value** of the secret immediately — it will only display once. Store it securely (you'll add it to local user secrets next).

6. **(Optional) Verify Token Configuration:**
   - Go to **Token Configuration** (left sidebar).
   - The app expects standard **OpenID Connect** scopes (`openid`, `profile`, `email`), which are already configured. No changes needed.

**Summary of credentials you now have:**
- **Tenant ID:** `common` (for consumer accounts) or your specific tenant GUID if you want organization-only sign-in
- **Client ID:** The Application ID from step 3
- **Client Secret:** The secret value from step 5

---

### Local Configuration

#### Option 1: User Secrets (Recommended for Development)

Use `dotnet user-secrets` to store credentials safely without committing to source code.

**From the `src/AspireApp.Web/` directory:**

```powershell
# Initialize user secrets for this project (one-time setup)
dotnet user-secrets init

# Store the Microsoft Entra ID configuration
dotnet user-secrets set "Authentication:Microsoft:TenantId" "common"
dotnet user-secrets set "Authentication:Microsoft:ClientId" "YOUR_CLIENT_ID_HERE"
dotnet user-secrets set "Authentication:Microsoft:ClientSecret" "YOUR_CLIENT_SECRET_HERE"
```

Replace:
- `YOUR_CLIENT_ID_HERE` with the Application ID from your Azure app registration
- `YOUR_CLIENT_SECRET_HERE` with the Client Secret value

**Verify the secrets were stored:**

```powershell
dotnet user-secrets list
```

#### Option 2: Environment Variables

If you prefer environment variables, set these before running the app:

```powershell
$env:Authentication__Microsoft__TenantId = "common"
$env:Authentication__Microsoft__ClientId = "YOUR_CLIENT_ID_HERE"
$env:Authentication__Microsoft__ClientSecret = "YOUR_CLIENT_SECRET_HERE"
```

#### Option 3: appsettings.Development.json (Not Recommended)

You can edit `src/AspireApp.Web/appsettings.Development.json` directly, but **never commit secrets to source control**:

```json
{
  "Authentication": {
    "Service": "auto",
    "Microsoft": {
      "TenantId": "common",
      "ClientId": "YOUR_CLIENT_ID_HERE",
      "ClientSecret": "YOUR_CLIENT_SECRET_HERE"
    }
  }
}
```

**Guidance:** User secrets (Option 1) is the safest approach for local development.

---

### How the App Detects and Routes Microsoft Auth

The app uses an **"auto" service resolver** to detect whether Microsoft credentials are configured:

1. **App startup** reads `Authentication:Microsoft:ClientId` and `ClientSecret` from configuration (user secrets, env vars, or appsettings).
2. If **both values are present and non-empty**, the app registers the Microsoft OpenID Connect handler and enables the `/auth/microsoft/signin` endpoint.
3. If **not configured**, the app falls back to mock authentication with demo accounts.
4. On the **Sign In page** (`/signin`), the available providers are displayed dynamically based on what's wired.

**Callback paths (no setup required):**
- **Sign-in callback:** `/signin-oidc-microsoft` ← Must match Azure redirect URI
- **Sign-out callback:** `/signout-callback-oidc-microsoft` ← Must match Azure sign-out redirect URI (if configured)

These are hardcoded in `MicrosoftEntraAuthenticationOptions` and already match the Azure app registration defaults.

---

### Testing Microsoft Sign-In Locally

#### Prerequisites

- ✅ AppHost is running: `dotnet run --project src/AspireApp.AppHost`
- ✅ Microsoft config is stored in user secrets or env vars
- ✅ Webfrontend URL noted from the Aspire dashboard (e.g., `https://localhost:<port>`)
- ✅ Azure redirect URIs match the webfrontend port

#### Manual Test

1. **Open the app:** Browse to the webfrontend URL shown in the Aspire dashboard.
2. **Navigate to Sign In:** Click the sign-in link or go directly to `/signin`.
3. **Check available providers:**
   - If configured correctly, you should see a **"Microsoft"** provider card labeled **"Hosted"** (not "Demo").
   - If you only see "Demo" providers, the Microsoft config was not picked up — check user secrets and restart the app.
4. **Click "Continue to hosted sign-in"** on the Microsoft card.
5. **You will be redirected** to `login.microsoftonline.com` (Microsoft's hosted login).
6. **Sign in** with a personal Microsoft account (e.g., `your-email@outlook.com` or a Microsoft account).
7. **Consent** to share basic profile data if prompted.
8. **Redirect back** to the app at `/signin-oidc-microsoft`, then to your original page (or the landing page `/` if no return URL was set).
9. **Verify sign-in:** Your email and display name should appear in the top-right user menu.

#### Automated Smoke Test (Playwright)

*(Once Playwright tests are added; currently manual testing is the primary method.)*

---

#### HTTPS and Localhost Caveats

- **Dynamic ports:** Aspire assigns the webfrontend port at startup. Check the **Aspire dashboard** for the actual URLs each time you start the app. If the port changes, update your Azure redirect URIs to match.
- **Localhost certificate:** Aspire automatically generates a self-signed certificate. Your browser will warn you; it's safe for local testing.
- **"Insecure":** The certificate is not recognized by browsers — this is normal for local development. Click through the warning or add the certificate to your local trusted store (optional).
- **HTTP redirect:** Azure AD allows `http://localhost` redirect URIs for development. However, always prefer HTTPS for testing because the production OAuth flow will require it.

---

## Google Setup (Future Work)

### ⚠️ Current Status

**Google OAuth is not yet implemented in the app.** The authentication layer is **provider-agnostic**, so adding Google support is straightforward, but the code doesn't exist yet.

**What you can do now:**
1. Create a Google OAuth app (steps below) and store the credentials locally.
2. Share them with the dev team or store securely for when the feature is implemented.
3. No app changes are needed to prepare — the abstraction is already in place.

---

### Google OAuth App Registration

**Where:** [Google Cloud Console — APIs & Services](https://console.cloud.google.com/)

**Steps:**

1. **Create a new project (or select existing):**
   - Go to [Google Cloud Console](https://console.cloud.google.com/).
   - Click the project dropdown at the top.
   - Click **"New Project"**.
   - **Name:** `AspireAI` (or your preferred name).
   - Click **Create**.
   - Wait for the project to initialize.

2. **Configure the OAuth Consent Screen:**
   - In the sidebar, go to **APIs & Services → OAuth consent screen**.
   - **User Type:** Select **"External"** (for personal Google accounts).
   - Click **Create**.
   - **App name:** `AspireAI`.
   - **User support email:** Your email.
   - **Developer contact information:** Your email.
   - Click **Save & Continue** through the remaining screens (default scopes are sufficient for sign-in).
   - Publish the app or add test users under **Test users** if it stays in "Testing" mode (only test users can sign in while in testing mode).

3. **Create OAuth 2.0 credentials:**
   - Go to **APIs & Services → Credentials**.
   - Click **+ Create Credentials** → **OAuth client ID**.

4. **Set up the OAuth client:**
   - **Application type:** Select **"Web application"**.
   - **Name:** `AspireAI Local Dev`.
   - **Authorized redirect URIs:** Add (using your actual Aspire-assigned ports — see [HTTPS and Localhost Caveats](#https-and-localhost-caveats)):
     - `https://localhost:<HTTPS_PORT>/signin-oidc-google` (preferred)
     - `http://localhost:<HTTP_PORT>/signin-oidc-google` (fallback)
   - **Note:** "Authorized JavaScript origins" are not needed for server-side OIDC; only redirect URIs are required.
   - Click **Create**.

5. **Note your credentials:**
   - A modal displays your **Client ID** and **Client Secret**. Copy both.
   - You can also download the credentials as JSON for safekeeping.

**Summary of credentials you now have:**
- **Client ID:** The `client_id` from the OAuth credentials
- **Client Secret:** The `client_secret` from the OAuth credentials

---

### Storing Google Credentials Locally

**Use the same approach as Microsoft:** Store credentials in user secrets (do NOT commit them).

*(These commands are for reference; they won't work until Google support is implemented in the app.)*

```powershell
cd src/AspireApp.Web

dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_GOOGLE_CLIENT_ID_HERE"
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_GOOGLE_CLIENT_SECRET_HERE"
```

---

### Next Steps for Google Implementation

Once the development team implements Google OAuth:

1. **App code changes** will add a `GoogleAuthenticationOptions` class (mirroring the Microsoft setup).
2. **DI registration** will conditionally wire the Google OpenID Connect handler.
3. **Redirect URI** will be `/signin-oidc-google` (you've already registered this in Google Cloud Console).
4. **Sign In page** will display Google as a provider card automatically.
5. **Testing** will follow the same flow as Microsoft (click, consent, redirect back).

The infrastructure is ready — only the provider-specific code is pending.

---

## Local Development Configuration

### Default Authentication Service Mode

The app detects the active authentication mode based on what's configured:

| Config State | Mode | Behavior |
|---|---|---|
| Microsoft ClientId & Secret present | `auto` (resolves to `combined`) | Both mock and Microsoft providers shown on sign-in page |
| Microsoft config empty | `auto` (resolves to `mock`) | Only demo providers shown |
| Explicit `Service: "mock"` | `mock` | Only demo providers, ignoring Microsoft config |
| Explicit `Service: "microsoft"` | `microsoft` | Only Microsoft provider (fails if config missing) |

**Current default** in `appsettings.json`: `"Service": "auto"` (recommended for development).

### Configuration Sources (Priority Order)

1. **User Secrets** (highest priority) — `dotnet user-secrets`
2. **Environment Variables** — `$env:Authentication__Microsoft__ClientId`
3. **appsettings.Development.json**
4. **appsettings.json** (lowest priority)

**Recommendation:** Use user secrets for all sensitive credentials.

---

### Disabling Microsoft Auth Temporarily

If you want to test with **only mock providers** (ignoring your stored Microsoft credentials):

**Option 1:** Override the service mode in `appsettings.Development.json`:
```json
{
  "Authentication": {
    "Service": "mock"
  }
}
```

**Option 2:** Clear user secrets:
```powershell
cd src/AspireApp.Web
dotnet user-secrets clear
```

Restart the app — mock auth will be active.

---

## Testing the Setup

### Smoke Test Checklist

Run through these steps after configuring Microsoft authentication:

- [ ] **Start the app:** `dotnet run --project src/AspireApp.AppHost`
- [ ] **Aspire dashboard is healthy:** All services show green in dashboard
- [ ] **Note the webfrontend URL** from the Aspire dashboard (e.g., `https://localhost:<port>`)
- [ ] **App is accessible:** The webfrontend URL loads without errors
- [ ] **Sign In page shows providers:** Navigate to `/signin`
  - [ ] If Microsoft config is present and Service is `auto` or `combined`, a "Microsoft" provider card appears labeled "Hosted"
  - [ ] Demo provider cards appear when Service is `auto` or `combined` (not when Service is `microsoft`)
- [ ] **Microsoft sign-in flow:**
  - [ ] Click "Continue to hosted sign-in" on Microsoft card
  - [ ] Redirected to `login.microsoftonline.com` 
  - [ ] Sign in with a personal Microsoft account (e.g., `user@outlook.com`)
  - [ ] Consent screen appears (one-time per app)
  - [ ] Redirected back to app at `/signin-oidc-microsoft`
  - [ ] Redirected to original requested page (or `/` if none)
- [ ] **User identity persists:**
  - [ ] Top-right corner shows your email and display name
  - [ ] Click your profile — sign-out button is available
- [ ] **Protected routes work:**
  - [ ] Unauthenticated users cannot access `/chat`, `/upload`, or `/weather`
  - [ ] Unauthenticated access redirects to `/signin?returnUrl=...`
  - [ ] After signing in, the original page loads
- [ ] **Sign-out works:**
  - [ ] Click sign-out from the user menu
  - [ ] Redirected to `/` (or signed-out page)
  - [ ] Identity is cleared; top-right no longer shows user
  - [ ] Re-accessing protected routes requires sign-in again

### Manual Testing with Mock Providers

Mock providers don't require any external credentials. They are available when the service mode is `auto` or `combined` (hidden when mode is `microsoft`):

1. Go to `/signin`
2. Click "Choose a demo account"
3. Select a demo user
4. Verify you're logged in; user info appears in the top-right

This confirms the sign-in flow works without needing Microsoft or Google credentials.

---

## Troubleshooting

### "No sign-in providers are available" on `/signin`

**Cause:** No providers are configured or wired.

**Fix:**
- Check that the app is running the latest code (rebuild: `dotnet build`).
- Verify mock auth is not explicitly disabled.
- If using Microsoft auth, check user secrets: `dotnet user-secrets list` (from `src/AspireApp.Web/`).
- Restart the app.

### Microsoft provider card not appearing (but mock providers are)

**Cause:** Microsoft ClientId or ClientSecret is missing or empty in configuration.

**Check:**
```powershell
cd src/AspireApp.Web
dotnet user-secrets list
```

Look for `Authentication:Microsoft:ClientId` and `Authentication:Microsoft:ClientSecret`.

**Fix:**
- If missing, add them: `dotnet user-secrets set "Authentication:Microsoft:ClientId" "YOUR_ID"`
- If present but empty, delete and re-add: `dotnet user-secrets remove "Authentication:Microsoft:ClientId"` then set a new value.
- Restart the app.

### "Invalid Client ID" or "Invalid Secret" during Microsoft sign-in

**Cause:** The credentials in Azure Portal don't match what's stored locally.

**Check:**
- Verify the Client ID matches the **Application (client) ID** in Azure Portal → Overview.
- Verify the Client Secret value matches what was displayed during creation (it's only shown once).
- Confirm the app is using the correct Azure tenant (check the Azure Portal URL).

**Fix:**
- Re-generate the Client Secret in Azure Portal if lost, then update user secrets.
- Double-check the Client ID copy-paste.

### "Redirect URI mismatch" during Microsoft sign-in

**Cause:** The redirect URI sent by the app doesn't match what's registered in Azure.

**Check:**
- In Azure Portal → **Authentication**, verify these URIs are registered using your actual Aspire-assigned port:
  - `https://localhost:<HTTPS_PORT>/signin-oidc-microsoft`
  - `http://localhost:<HTTP_PORT>/signin-oidc-microsoft` (if testing on HTTP)
- If the Aspire port changed since you registered the URIs, add the new port in Azure.

**Fix:**
- Add missing URIs to Azure Portal → Authentication → Redirect URIs.
- Ensure the callback path in `MicrosoftEntraAuthenticationOptions.cs` matches (default is `/signin-oidc-microsoft` — no change needed).

### Localhost certificate warnings in browser

**Cause:** Aspire generates a self-signed certificate that browsers don't recognize.

**Expected:** Warnings are normal for localhost development.

**Workaround:**
- Click through the warning (click "Advanced" → "Proceed to localhost").
- Or, add the certificate to your local trusted store (optional; search "Windows Manage User Certificates").

### App crashes or logs "The OIDC metadata endpoint failed" on startup

**Cause:** Microsoft config is set but invalid (e.g., empty Tenant ID, malformed credentials).

**Check:**
- Ensure **both** ClientId and ClientSecret are non-empty.
- Verify TenantId is either `"common"` (consumer accounts) or a valid GUID.

**Fix:**
- Clear user secrets: `dotnet user-secrets clear`.
- Set only the valid credentials you confirmed in Azure Portal.
- Restart the app.

### After switching authentication modes, old mode still appears

**Cause:** Configuration change wasn't picked up (app wasn't restarted).

**Fix:**
- Stop the app (Ctrl+C in terminal).
- Start the app again: `dotnet run --project src/AspireApp.AppHost`.

---

## Additional Resources

- [Microsoft Entra ID (formerly Azure AD) documentation](https://learn.microsoft.com/en-us/entra/identity-platform/)
- [OpenID Connect in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/oidc)
- [Google OAuth 2.0 documentation](https://developers.google.com/identity/protocols/oauth2)
- [ASP.NET Core Authentication and Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)

---

## Questions or Issues?

If setup doesn't work as described:

1. **Check logs:** The Aspire dashboard shows application logs. Search for "authentication" or "OIDC" errors.
2. **Verify credentials:** Re-read the values from Azure Portal or Google Cloud Console.
3. **Restart everything:** Stop the app, clear user secrets if needed, and start fresh.
4. **Ask the team:** File an issue or reach out to the dev team with the error logs.

---

**Last Updated:** 2026-04-05  
**Tested On:** .NET 10 SDK, Aspire Dashboard, localhost HTTPS  
**Security Review:** Warden (2026-04-05) — verified callback paths, secret handling, dynamic port guidance, and Google setup accuracy
