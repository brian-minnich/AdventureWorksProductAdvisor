# AdventureWorks Product Advisor — Design (Scaffolding & First Slice)

This document supplements `AdventureWorks_Product_Advisor_Spec.md` (the project brief) with the
decisions made during planning and the definition of the first implementation slice. Read the
brief first for full context (purpose, goals, cost controls, full target architecture); this doc
does not repeat what's unchanged there.

## Decisions Resolved From "Open Items"

1. **V1 scope**: toggle-and-retest, not side-by-side. The mode toggle switches which pipeline
   answers the next question; a side-by-side comparison view (run both pipelines on one question,
   show both answers) is an explicit v2 candidate, not part of this build.
2. **Top-N reviews retrieved by the C# similarity ranker**: a number input on the page, default
   `5`, sent per-request as part of the ask payload. It only affects `CSharpAskService`.
   `StoredProcAskService` ignores it — the stored procedure's `VECTOR_SEARCH` retrieval count is
   fixed in existing T-SQL that this project doesn't modify (per the brief's non-goals).
3. **Provider design for `IEmbeddingService`/`IChatCompletionService`**: the interfaces never leak
   Azure-specific shapes — method signatures use plain types (`string`, `float[]`), and all Azure
   REST details (endpoint, headers, JSON payload/response shape) stay inside
   `AzureOpenAiEmbeddingService`/`AzureOpenAiChatCompletionService`. No provider-selection config,
   second implementation, or plugin registry is being built — that's out of scope until a second
   provider actually exists.

## Environment / Tooling

- Visual Studio Community 2026 (v18.9), workload "ASP.NET and web development", installed and
  confirmed on this machine. No prior VS installation existed; only this one project's tooling
  needs are in scope here.
- Project is created via VS's **ASP.NET Web Application (.NET Framework)** wizard → MVC template,
  with both **MVC** and **Web API** checkboxes checked, **Authentication: No Authentication**,
  target framework **.NET Framework 4.8**.
- Solution name and project name: `AdventureWorksProductAdvisor`, created directly in the repo
  root (`C:\Git\AdventureWorksProductAdvisor`), solution and project in the same directory.
- A second project, `AdventureWorksProductAdvisor.Tests` (VS's **Unit Test Project (.NET
  Framework)** template — scaffolds MSTest v2 by default), is added to the same solution. **Moq**
  is added via NuGet for mocking.

## Post-Scaffold Cleanup

The MVC template generates more than this project needs. After scaffolding:
- Delete `Views/Home/About.cshtml`, `Views/Home/Contact.cshtml`, and their `HomeController` actions
  and nav-menu links.
- Trim the default Bootstrap starter markup in `Views/Home/Index.cshtml` down to a bare host page
  for the chat widget.
- Add the brief's folders not already created by the template: `Controllers/Api`, `Services`,
  `Data`, `Models`, `Sql`. (`Controllers`, `Views`, `Scripts` already exist from the template.)
- No membership/identity scaffolding is added (non-goal: no auth).

## Interface Shapes

```csharp
public interface IAskService
{
    Task<AskResponse> AskAsync(string question, int maxCompletionTokens, int topN);
}

public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text);
}

public interface IChatCompletionService
{
    Task<string> GetCompletionAsync(string prompt, int maxCompletionTokens);
}
```

- `StoredProcAskService.AskAsync` accepts `topN` to satisfy the interface but ignores it (documented
  inline with why, since a silently-ignored parameter is exactly the kind of non-obvious behavior
  worth a comment).
- `AskRequest` (bound from the JSON POST body) carries `Question`, `Mode`, and `TopN` (defaults to
  `5` if omitted). `MaxCompletionTokens` is **not** client-supplied — it's read server-side from
  `Web.config` (`AppSettings["MaxCompletionTokens"]`) so the cost guard can't be overridden from
  the browser.

## First Slice: C# Pipeline Walking Skeleton

The two-pipeline comparison is the interesting part of this project but also the highest-effort
part. The first slice is scoped to the **C# pipeline only**, end to end, deferring
`StoredProcAskService` and the `CallLog`/daily-cap machinery to a second slice. Rationale: the
C# pipeline is the largest chunk of genuinely new code (embedding backfill, similarity ranking,
prompt construction, HTTP calls to Azure OpenAI) and the part most worth pressure-testing before
layering the second pipeline and cost-guard bookkeeping on top, which are comparatively mechanical
once this works.

**Included in slice 1:**
- Project scaffolding (per above), committed clean.
- `Web.ConnectionStrings.config` (gitignored) + a `.config.example` template checked in; connection
  string points at the existing Azure SQL AdventureWorksLT database.
- SQL script (checked into `/Sql`) adding `ReviewEmbeddingJson NVARCHAR(MAX) NULL` to
  `dbo.ProductReview`.
- `IReviewRepository` / `ReviewRepository` (ADO.NET) to load reviews and their embeddings.
- `AzureOpenAiEmbeddingService` implementing `IEmbeddingService`.
- A one-time backfill action (admin/console-style trigger) that embeds all 140 existing reviews and
  writes `ReviewEmbeddingJson`.
- `SimilarityRanker` (cosine similarity, top-N selection).
- `AzureOpenAiChatCompletionService` implementing `IChatCompletionService`.
- `CSharpAskService` implementing `IAskService`, wiring the above into the full
  embed → retrieve → rank → prompt → call → parse pipeline.
- `AskController` (C# mode only — the Stored Procedure option can exist in the UI but return a
  "not yet implemented" response), the Razor host page (trimmed `Home/Index.cshtml`), and
  `chat-widget.js`, wired end to end so a question typed in the browser gets a real,
  review-grounded answer from Azure OpenAI.
- Unit tests (see below).

**Explicitly deferred to slice 2:** `StoredProcAskService`, `dbo.CallLog` table and logging,
daily call cap enforcement in `AskController`, the `@MaxTokens` parameterization of
`dbo.AskProductQuestion`.

## Testing (Slice 1)

`AdventureWorksProductAdvisor.Tests` (MSTest v2 + Moq), added in the same solution:

- **`SimilarityRanker`** — pure logic, no mocks: cosine similarity math, correct top-N
  ordering/truncation, edge cases (fewer reviews than N, empty embedding list).
- **`CSharpAskService`** — mock `IReviewRepository`, `IEmbeddingService`, `IChatCompletionService`;
  verify orchestration (question gets embedded, ranker is called with the requested `topN`, the
  prompt sent to chat completion includes retrieved review text, the returned `AskResponse` is
  populated correctly).
- **`AzureOpenAiEmbeddingService` / `AzureOpenAiChatCompletionService`** — inject `HttpClient` with
  a fake `HttpMessageHandler` returning canned Azure OpenAI-shaped JSON; assert outgoing request
  shape (endpoint, `api-key` header, JSON payload) and correct parsing of the response into
  `float[]` / `string`. No real network calls, no token cost, runs offline.
- **`AskController`** — mock `IAskService`; verify it resolves the implementation based on `Mode`,
  passes `TopN` and the server-side `MaxCompletionTokens` through correctly, and shapes the HTTP
  response as expected.

**Not unit tested in slice 1:**
- `ReviewRepository` — a thin ADO.NET wrapper; unit testing it would require either a real
  database or heavy `SqlConnection` mocking for low value. Exercised implicitly by running the app
  against the real Azure SQL database.
- The Razor view and `chat-widget.js` — verified manually in a browser (golden path + edge cases),
  per this project's UI-testing convention.
