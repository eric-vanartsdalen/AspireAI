# Critique Mode UI Implementation Guide

**For:** Jeff (.NET Dev)  
**From:** Buster (QA/Tester)  
**Date:** 2026-04-22  
**Status:** Test coverage ready, awaiting implementation

## What I Did

Created 8 comprehensive tests in `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs` that validate Critique-mode UI/product behavior:

1. ✅ Critique toggle enabled
2. ✅ Mode selection updates component state  
3. ✅ Critique mode propagates to `BrainChatClient.ChatAsync`
4. ✅ Regular mode propagates correctly (regression check)
5. ✅ Reasoning panel renders with steps
6. ✅ Regular mode doesn't show reasoning panel
7. ✅ Progress details visible in reasoning steps
8. ✅ Mode hint text changes correctly
9. ✅ Conversations load with stored mode

## What You Need to Implement

### 1. Enable Critique Toggle

**File:** `src\AspireApp.Web\Components\Pages\Chat.razor` (line 850-857)

**Current:**
```razor
<span class="chat-mode-option disabled" title="Critique mode is coming soon — requires agent framework">
    <input type="radio"
           id="mode-critique"
           name="chatMode"
           value="@ChatConversationModes.Critique"
           disabled
           data-testid="chat-mode-critique" />
    <label for="mode-critique">Critique</label>
</span>
```

**Change to:**
```razor
<span class="chat-mode-option">
    <input type="radio"
           id="mode-critique"
           name="chatMode"
           value="@ChatConversationModes.Critique"
           checked="@(SelectedChatMode == ChatConversationModes.Critique)"
           @onchange="@(() => OnChatModeChangedAsync(ChatConversationModes.Critique))"
           disabled="@IsAIResponsing"
           data-testid="chat-mode-critique" />
    <label for="mode-critique">Critique</label>
</span>
```

**Changes:**
- Remove `class="chat-mode-option disabled"` → `class="chat-mode-option"`
- Remove `title="Critique mode is coming soon..."`
- Remove `disabled` → add `disabled="@IsAIResponsing"` (matches Regular mode pattern)
- Add `checked="@(SelectedChatMode == ChatConversationModes.Critique)"`
- Add `@onchange="@(() => OnChatModeChangedAsync(ChatConversationModes.Critique))"`

### 2. Add Reasoning Panel Rendering

**File:** `src\AspireApp.Web\Components\Pages\Chat.razor` (after evidence panel, ~line 760)

**Add this block after the evidence panel:**
```razor
@if (message.User == "Assistant" && currentAssistantIdx >= 0 && 
     _messageEvidence.TryGetValue(currentAssistantIdx, out var evidence) &&
     evidence.ReasoningSteps.Count > 0)
{
    <div class="reasoning-panel" data-testid="chat-reasoning-panel">
        <div class="reasoning-header">
            <span class="reasoning-badge">🔍 Critique Process</span>
            <span class="reasoning-count">@evidence.ReasoningSteps.Count step@(evidence.ReasoningSteps.Count == 1 ? "" : "s")</span>
        </div>
        @foreach (var step in evidence.ReasoningSteps)
        {
            <div class="reasoning-step" data-testid="chat-reasoning-step">
                <div class="reasoning-step-header">
                    <span class="reasoning-step-name">@step.Step</span>
                    @if (!string.IsNullOrWhiteSpace(step.Tool))
                    {
                        <span class="reasoning-step-tool">🛠️ @step.Tool</span>
                    }
                </div>
                <div class="reasoning-step-content">
                    <p class="reasoning-step-reasoning">@step.Reasoning</p>
                    @if (!string.IsNullOrWhiteSpace(step.Result))
                    {
                        <p class="reasoning-step-result"><strong>Result:</strong> @step.Result</p>
                    }
                </div>
            </div>
        }
    </div>
}
```

### 3. Add Reasoning Panel Styles

**File:** `src\AspireApp.Web\Components\Pages\Chat.razor` (after evidence-panel styles, ~line 444)

**Add these CSS rules:**
```css
.reasoning-panel {
    margin-top: 0.75rem;
    padding: 0.75rem;
    border-left: 3px solid #0d6efd;
    background-color: rgba(13, 110, 253, 0.05);
    border-radius: 0.5rem;
}

.reasoning-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 0.75rem;
    padding-bottom: 0.5rem;
    border-bottom: 1px solid rgba(13, 110, 253, 0.2);
}

.reasoning-badge {
    font-weight: 600;
    color: #0d6efd;
}

.reasoning-count {
    font-size: 0.85rem;
    opacity: 0.8;
}

.reasoning-step {
    margin-bottom: 0.75rem;
    padding: 0.5rem;
    background-color: rgba(255, 255, 255, 0.02);
    border-radius: 0.375rem;
}

.reasoning-step:last-child {
    margin-bottom: 0;
}

.reasoning-step-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 0.5rem;
}

.reasoning-step-name {
    font-weight: 600;
    color: #0d6efd;
}

.reasoning-step-tool {
    font-size: 0.85rem;
    opacity: 0.8;
}

.reasoning-step-content {
    font-size: 0.9rem;
}

.reasoning-step-reasoning {
    margin-bottom: 0.5rem;
}

.reasoning-step-result {
    margin: 0;
    padding: 0.5rem;
    background-color: rgba(255, 255, 255, 0.04);
    border-radius: 0.25rem;
    font-size: 0.85rem;
}
```

## What's Already Done

✅ **Mode wiring:** `SelectedChatMode` already passed to `BrainChatClient.ChatAsync` (line 988 in `Chat.razor.cs`)  
✅ **Mode storage:** `OnChatModeChangedAsync` already updates conversation mode  
✅ **Mode persistence:** Conversations already load with stored mode (line 328, 342 in `Chat.razor.cs`)  
✅ **Evidence tracking:** `_messageEvidence` dictionary already tracks responses by assistant message index

## Test Validation

After implementing the above changes:

```powershell
# Run the Critique-mode UI tests
dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~ChatCritiqueModeTests"
```

**Expected:** All 8 tests PASS

## Troubleshooting

**If tests fail:**

1. **`CritiqueToggle_IsEnabled_AfterProductLayerImplementation` fails:**
   - Check: `disabled` attribute removed from Critique radio?
   - Check: `class="chat-mode-option disabled"` changed to `class="chat-mode-option"`?

2. **`SendingMessage_InCritiqueMode_PassesCritiqueModeToClient` fails:**
   - Check: `@onchange` handler wired to Critique radio?
   - Check: `checked` binding present?

3. **`CritiqueResponse_WithReasoningSteps_RendersReasoningPanel` fails:**
   - Check: `data-testid="chat-reasoning-panel"` present on reasoning panel div?
   - Check: `data-testid="chat-reasoning-step"` present on each step div?
   - Check: Conditional renders when `evidence.ReasoningSteps.Count > 0`?

4. **`ModeHintText_ChangesBasedOnSelectedMode` fails:**
   - Check: Existing mode hint conditional still works? (line 860 in `Chat.razor`)

## Questions?

Ping Buster if:
- Tests still fail after implementation
- Unclear what a test is validating
- Need help debugging rendering logic

---

**Bottom line:** Remove `disabled` from Critique radio, add reasoning panel rendering with correct `data-testid` attributes, and tests should go green. 🟢
