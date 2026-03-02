# Task Brief: Meeting Assistant User Portal

**Priority:** High — Parallel workstream to AWS infrastructure deployment  
**Created:** 2026-02-25 14:36 PM EST  
**Owner:** Tony Stark (software-engineer) → Clint Barton (code-reviewer)

---

## Mission

Build a user-facing web portal where users can register, configure their meeting bot, view meeting history, and access detailed meeting summaries with transcripts.

---

## User Stories (Phase 1)

### 1. User Authentication
**As a** Refuge Group employee  
**I want to** log in with my company Microsoft account (Entra SSO)  
**So that** I can access my personal meeting assistant

**Acceptance Criteria:**
- [ ] Login page with Entra ID SSO
- [ ] Successful auth creates/updates user record in SQL Server
- [ ] User sees dashboard after login
- [ ] Logout button works

---

### 2. Dashboard — My Meetings
**As a** user  
**I want to** see a list of all my meetings  
**So that** I can quickly find past conversations

**Acceptance Criteria:**
- [ ] Dashboard shows meetings in reverse chronological order (newest first)
- [ ] Each meeting shows: date, title, platform (Teams/Zoom/etc), duration, status
- [ ] Status indicators: Recording, Processing, Complete, Failed
- [ ] Click a meeting → navigate to detail view
- [ ] Empty state when no meetings: "No meetings yet. Submit a Teams URL to get started."
- [ ] Pagination (20 meetings per page)

---

### 3. Meeting Detail View
**As a** user  
**I want to** see the full details of a meeting  
**So that** I can review what was discussed and decided

**Acceptance Criteria:**
- [ ] Meeting header: title, date, platform, duration, participants (if available)
- [ ] AI Summary section (overview paragraph)
- [ ] Key Decisions section (bulleted list)
- [ ] Action Items section (table: item, owner, due date)
- [ ] Full Transcript section (speaker labels, timestamps, searchable)
- [ ] "Back to Dashboard" link
- [ ] If meeting still processing: show spinner + "Processing transcript..."
- [ ] If meeting failed: show error message

---

### 4. Bot Configuration
**As a** user  
**I want to** configure my bot's display name and preferences  
**So that** my bot appears in meetings the way I want

**Acceptance Criteria:**
- [ ] Settings page accessible from dashboard
- [ ] Form fields:
  - Bot Display Name (e.g., "Fred's Notetaker", max 50 chars)
  - Summary Style (dropdown: Brief, Detailed, Action-Oriented)
  - Include Action Items (checkbox, default: true)
  - Include Key Decisions (checkbox, default: true)
  - Include Open Questions (checkbox, default: true)
- [ ] Save button updates database
- [ ] Success message after save
- [ ] Cancel button discards changes

---

### 5. Join Meeting
**As a** user  
**I want to** submit a Teams meeting URL  
**So that** my bot joins and records the meeting

**Acceptance Criteria:**
- [ ] "Join Meeting" button on dashboard
- [ ] Modal or separate page with form:
  - Meeting URL (required, text input)
  - Meeting Title (optional, text input, auto-populated if possible)
- [ ] Submit button calls `POST /api/meetings/join`
- [ ] Success: redirect to meeting detail page (showing "Recording" status)
- [ ] Error: show validation message ("Invalid URL", "Meeting already ended", etc.)
- [ ] Loading spinner while API call in progress

---

### 6. Active Meeting Status
**As a** user  
**I want to** see which meetings my bot is currently in  
**So that** I know it's working

**Acceptance Criteria:**
- [ ] Dashboard shows "Active Recordings" section at top (if any exist)
- [ ] Each active meeting shows: title, started time, elapsed duration, "Stop" button
- [ ] Stop button calls `POST /api/meetings/{id}/stop`
- [ ] After stop: meeting moves to "Processing" status
- [ ] Auto-refresh every 30 seconds (or WebSocket if time allows)

---

## Technical Architecture

### Frontend: Blazor Server
- **Framework:** .NET 8, Blazor Server (Rob's team can support this)
- **Auth:** Entra ID (Azure AD) via `Microsoft.Identity.Web`
- **UI:** Professional, clean, responsive (Bootstrap 5 or Radzen Blazor)
- **Components:**
  - `Pages/Index.razor` — Dashboard (meeting list)
  - `Pages/MeetingDetail.razor` — Meeting detail view
  - `Pages/Settings.razor` — Bot configuration
  - `Shared/MainLayout.razor` — Nav bar, logout
  - `Components/MeetingCard.razor` — Meeting list item
  - `Components/MeetingStatusBadge.razor` — Status indicator (Recording, Processing, Complete, Failed)

### Backend: .NET API (Already Built)
- **Base URL (dev):** `http://localhost:5000` (local) or `http://<ecs-task-ip>:5000` (AWS)
- **Endpoints:**
  - `POST /api/meetings/join` — Submit Teams URL
  - `GET /api/meetings` — List meetings (paginated)
  - `GET /api/meetings/{id}` — Get meeting details
  - `GET /api/meetings/{id}/summary` — Get AI summary from LMA
  - `GET /api/meetings/{id}/transcript` — Get full transcript from LMA
  - `POST /api/meetings/{id}/stop` — Stop recording
  - `GET /api/bot-config` — Get user's bot config
  - `PUT /api/bot-config` — Update bot config
  - `GET /api/action-items` — List action items

**API Client:**
- Use `HttpClient` with dependency injection
- Service: `MeetingAssistantApiClient.cs` — wraps all API calls
- Handle auth via Entra token passed as Bearer token

### Data Flow
1. User logs in with Entra → token stored in session
2. Blazor page loads → calls API with Bearer token
3. API queries SQL Server (meeting metadata, user data)
4. API queries LMA AppSync (transcripts, summaries) via LmaClient service
5. API returns JSON → Blazor renders UI

### Auth Implementation
```csharp
// Program.cs
builder.Services.AddMicrosoftIdentityWebAppAuthentication(
    builder.Configuration, "AzureAd");

builder.Services.AddHttpClient<MeetingAssistantApiClient>(client => {
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]);
});

// appsettings.json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<refuge-tenant-id>",
    "ClientId": "<app-registration-client-id>",
    "CallbackPath": "/signin-oidc"
  },
  "ApiBaseUrl": "http://localhost:5000"
}
```

---

## UI/UX Guidelines

### Look and Feel
- **Professional** — this will be shown to Steve, Tom, potentially broader team
- **Clean** — no clutter, focus on content
- **Responsive** — should work on mobile (even if not optimized)

### Status Indicators (Color Coding)
- 🔴 **Recording** — Red badge, "Bot is in meeting"
- 🟡 **Processing** — Yellow badge, "Generating transcript..."
- 🟢 **Complete** — Green badge, "Ready to view"
- ⚫ **Failed** — Gray badge, "Recording failed"

### Dashboard Layout
```
┌─────────────────────────────────────┐
│ Meeting Assistant          [Logout] │
├─────────────────────────────────────┤
│ Active Recordings (if any)          │
│ ┌─────────────────────────────────┐ │
│ │ 🔴 Fred.AI Meeting with Steve   │ │
│ │ Started: 2:15 PM • 21 min       │ │
│ │ [Stop Recording]                 │ │
│ └─────────────────────────────────┘ │
│                                     │
│ [Join Meeting] [Settings]           │
│                                     │
│ My Meetings                         │
│ ┌─────────────────────────────────┐ │
│ │ 🟢 Budget Planning Q1            │ │
│ │ Feb 25, 2026 • Teams • 47 min   │ │
│ └─────────────────────────────────┘ │
│ ┌─────────────────────────────────┐ │
│ │ 🟢 Product Roadmap Review        │ │
│ │ Feb 24, 2026 • Teams • 1h 12m   │ │
│ └─────────────────────────────────┘ │
│                                     │
│ « Prev | 1 2 3 4 | Next »          │
└─────────────────────────────────────┘
```

### Meeting Detail Layout
```
┌─────────────────────────────────────┐
│ Meeting Assistant     « Back        │
├─────────────────────────────────────┤
│ Budget Planning Q1                  │
│ Feb 25, 2026 • Teams • 47 min       │
│ Participants: Fred, Steve, Tom      │
│                                     │
│ ▼ AI Summary                        │
│   Discussed Q1 budget allocation... │
│                                     │
│ ▼ Key Decisions                     │
│   • Approved $50K for marketing     │
│   • Delayed hiring until Q2         │
│                                     │
│ ▼ Action Items                      │
│ ┌───────────────────────────────┐   │
│ │ Item              Owner  Due  │   │
│ │ Draft budget      Steve  3/1  │   │
│ │ Review contracts  Tom    2/28 │   │
│ └───────────────────────────────┘   │
│                                     │
│ ▼ Full Transcript                   │
│   [00:02] Fred: Let's start with... │
│   [00:15] Steve: I think we should..│
│   [Search transcript...]            │
└─────────────────────────────────────┘
```

---

## File Structure

```
meeting-assistant-aws/
└── src/
    └── RefugeMeetingAssistant.Web/
        ├── Program.cs
        ├── appsettings.json
        ├── appsettings.Development.json
        ├── Pages/
        │   ├── _Host.cshtml
        │   ├── Index.razor              (Dashboard)
        │   ├── MeetingDetail.razor      (Meeting detail view)
        │   ├── Settings.razor            (Bot configuration)
        │   └── Login.razor               (Login/auth redirect)
        ├── Shared/
        │   ├── MainLayout.razor          (Nav bar, layout)
        │   └── NavMenu.razor             (Navigation links)
        ├── Components/
        │   ├── MeetingCard.razor         (Meeting list item)
        │   ├── MeetingStatusBadge.razor  (Status indicator)
        │   ├── ActiveRecordingCard.razor (Active meeting display)
        │   └── JoinMeetingModal.razor    (Join meeting form)
        ├── Services/
        │   └── MeetingAssistantApiClient.cs  (API wrapper)
        ├── Models/
        │   ├── MeetingViewModel.cs
        │   ├── BotConfigViewModel.cs
        │   └── MeetingDetailViewModel.cs
        └── wwwroot/
            ├── css/
            │   └── app.css               (Custom styles)
            └── js/
                └── site.js               (Any JS helpers)
```

---

## Implementation Notes

### API Client Service
```csharp
public class MeetingAssistantApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public async Task<List<MeetingViewModel>> GetMeetingsAsync(int page = 1, int pageSize = 20)
    {
        var token = await GetAccessTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);
        
        var response = await _httpClient.GetAsync($"/api/meetings?page={page}&pageSize={pageSize}");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<MeetingViewModel>>(json);
    }
    
    // Similar methods for other endpoints...
}
```

### Dashboard Page (Index.razor)
```razor
@page "/"
@inject MeetingAssistantApiClient ApiClient
@attribute [Authorize]

<h3>My Meetings</h3>

@if (activeRecordings.Any())
{
    <div class="active-recordings mb-4">
        <h5>Active Recordings</h5>
        @foreach (var meeting in activeRecordings)
        {
            <ActiveRecordingCard Meeting="@meeting" OnStop="HandleStopRecording" />
        }
    </div>
}

<div class="actions mb-3">
    <button class="btn btn-primary" @onclick="ShowJoinMeetingModal">Join Meeting</button>
    <a href="/settings" class="btn btn-secondary">Settings</a>
</div>

<div class="meetings-list">
    @foreach (var meeting in meetings)
    {
        <MeetingCard Meeting="@meeting" />
    }
</div>

<Pagination CurrentPage="@currentPage" TotalPages="@totalPages" OnPageChanged="LoadPage" />

@code {
    private List<MeetingViewModel> meetings = new();
    private List<MeetingViewModel> activeRecordings = new();
    private int currentPage = 1;
    private int totalPages = 1;
    
    protected override async Task OnInitializedAsync()
    {
        await LoadMeetings();
        await LoadActiveRecordings();
    }
    
    private async Task LoadMeetings()
    {
        meetings = await ApiClient.GetMeetingsAsync(currentPage, 20);
        // TODO: Get total count from API for pagination
    }
    
    private async Task LoadActiveRecordings()
    {
        // TODO: API endpoint for active recordings
        // activeRecordings = await ApiClient.GetActiveRecordingsAsync();
    }
}
```

---

## Testing Checklist

**Auth:**
- [ ] Login redirects to Entra ID
- [ ] Successful login creates user record
- [ ] Token passed to API calls
- [ ] Logout clears session

**Dashboard:**
- [ ] Shows meetings in correct order
- [ ] Status badges correct colors
- [ ] Pagination works
- [ ] Empty state shows correctly

**Meeting Detail:**
- [ ] Loads summary, decisions, action items
- [ ] Transcript searchable
- [ ] Back button works

**Bot Config:**
- [ ] Form loads current config
- [ ] Save updates database
- [ ] Validation works (max length, required fields)

**Join Meeting:**
- [ ] Form validates Teams URL format
- [ ] API call succeeds
- [ ] Redirects to meeting detail with "Recording" status

---

## Acceptance Criteria (Overall)

- [ ] User can log in with Entra ID
- [ ] Dashboard shows all user's meetings
- [ ] User can click a meeting and see full details
- [ ] User can configure bot display name and preferences
- [ ] User can submit a Teams URL to join a meeting
- [ ] User can see active recordings and stop them
- [ ] UI is professional and responsive
- [ ] No console errors in browser
- [ ] All API calls use Bearer token authentication

---

## Deliverables

**From Tony (Build):**
1. Complete Blazor Server app in `src/RefugeMeetingAssistant.Web/`
2. All 6 user stories implemented
3. Entra auth configured
4. API client service with all endpoints
5. Professional UI (Bootstrap 5 or Radzen)
6. Build Report: `USER-PORTAL-BUILD-REPORT.md`

**From Clint (Review):**
1. Code review of Blazor components
2. Auth implementation verified
3. API integration checked
4. UI/UX assessment
5. Review Report: `USER-PORTAL-REVIEW-REPORT.md`

---

## Resources

- **Product Spec:** `/home/fredw/.openclaw/workspace/memory/projects/meeting-assistant-aws-spec.md`
- **🎨 DESIGN SPEC (REQUIRED):** `/home/fredw/.openclaw/workspace/memory/projects/meeting-assistant-portal-design-spec.md`
  - **Brand:** Refuge brand (deep teal + copper accent from refugems.com)
  - **CSS variables:** Ready-to-use custom properties block
  - **Components:** Detailed styling for all UI elements
  - **Layouts:** Dashboard, meeting detail, settings page designs
  - **Typography:** Font families, sizes, weights specified
  - **USE THIS for all UI styling** (not Bootstrap defaults)
- **API source:** `/home/fredw/.openclaw/workspace/meeting-assistant-aws/src/RefugeMeetingAssistant.Api/`
- **API Swagger:** `http://localhost:5000/swagger`
- **Entra auth docs:** https://learn.microsoft.com/en-us/aspnet/core/blazor/security/

---

## Timeline

**Build (Tony):** 2-3 hours  
**Review (Clint):** 30 minutes  
**Total:** ~3.5 hours

**Can deploy to ECS once Rhodey's infrastructure is ready.**

---

*This is the user-facing window into our Meeting Assistant. Make it professional—Steve and Tom will see this.*
