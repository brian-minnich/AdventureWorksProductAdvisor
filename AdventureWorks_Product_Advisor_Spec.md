# AdventureWorks Product Advisor — Project Brief

## Purpose

A personal project to refresh C#, .NET Framework 4.8, and JavaScript skills that have started
to feel rusty, built as a small RAG (Retrieval-Augmented Generation) chat application over the
AdventureWorksLT sample database. Designed to be extensible — this is a starting point, not a
one-off script, and future features should be addable without a rewrite.

A secondary goal: compare two ways of implementing the same RAG pipeline side by side —
one built by hand in C#, one delegated to a SQL Server stored procedure — using the same
question, the same model, and the same token budget, to see how the results and behavior differ.

## Background / Prior Work

- Built the Microsoft Learn lab ["Implement RAG solutions"](https://microsoftlearning.github.io/mslearn-sql-developer/Instructions/Labs/11-implement-rag-solutions.html)
  against an existing Azure SQL Database provisioned with the AdventureWorksLT sample data.
- `dbo.ProductReview` table exists, populated with 140 sample reviews (from the lab's seed script).
- `dbo.AskProductQuestion` stored procedure exists, implementing the full RAG pipeline in T-SQL:
  question → embedding (`AI_GENERATE_EMBEDDINGS`) → `VECTOR_SEARCH` retrieval → prompt
  construction → `sp_invoke_external_rest_endpoint` call to Azure OpenAI (via managed identity) →
  answer extraction.
- A native `VECTOR(1536)` column (`ReviewVector`) on `ProductReview` holds embeddings generated
  by the lab's T-SQL script. This column is **not** used by the app directly (see Architecture)
  and stays untouched as the stored procedure's data source.

## Goals

1. Refresh hands-on C# and .NET Framework 4.8 skills (ADO.NET, async/await, LINQ, HttpClient,
   JSON serialization, MVC5 controllers/services).
2. Refresh JavaScript skills (fetch API, DOM updates, async UI state) building a real chat widget.
3. Build something extensible — a home base app that new features can be added to later, not a
   single-purpose script.
4. Compare a hand-built C# RAG pipeline against the existing SQL stored procedure's pipeline,
   side by side, under identical constraints (model, token budget).
5. Incur no required cost beyond light Azure OpenAI token usage (estimated well under $2 for the
   full build-and-test cycle — see Cost Notes).

## Non-Goals

- Not a production app. No need for auth, multi-user support, or high availability.
- Not attempting to reproduce the lab's native `VECTOR`/`VECTOR_SEARCH` T-SQL feature set in the
  C# pipeline — the C# path implements its own storage and similarity search independently.

## Architecture

**Stack**: ASP.NET MVC5 + Web API 2, .NET Framework 4.8, C#, Razor views (`.cshtml`), vanilla
JavaScript front end (no SPA framework). Single web application project.

**Layout**

```
/Controllers
    HomeController.cs        — serves the main page
    /Api
        AskController.cs     — POST endpoint, routes to the selected pipeline
/Services
    IAskService.cs           — shared interface for both pipelines
    CSharpAskService.cs      — full pipeline: embed -> retrieve -> rank -> prompt -> call -> parse
    StoredProcAskService.cs  — thin ADO.NET wrapper around dbo.AskProductQuestion
    IEmbeddingService.cs / AzureOpenAiEmbeddingService.cs
    IChatCompletionService.cs / AzureOpenAiChatCompletionService.cs
    SimilarityRanker.cs      — cosine similarity over in-memory embeddings
    ICallLogService.cs / CallLogService.cs
/Data
    IReviewRepository.cs / ReviewRepository.cs  — ADO.NET access to ProductReview
/Models
    AskRequest.cs, AskResponse.cs, CallLogEntry.cs
/Views
    /Home/Index.cshtml       — host page with embedded chat widget
/Scripts
    chat-widget.js           — self-contained widget module (fetch calls, rendering, mode toggle)
/Sql
    (reference copies of any schema changes — see Database Changes below)
Web.config                   — app settings (max_completion_tokens, daily call cap, etc.)
```

**Two pipelines behind one interface**

```csharp
public interface IAskService
{
    Task<AskResponse> AskAsync(string question, int maxCompletionTokens);
}
```

- `CSharpAskService`: calls `IEmbeddingService` to embed the question, calls
  `IReviewRepository` to load all reviews + their C#-owned embeddings, uses `SimilarityRanker`
  to compute cosine similarity and select the top N reviews, builds the augmented prompt itself,
  calls `IChatCompletionService` (an `HttpClient` call to the Azure OpenAI chat completions
  endpoint), and parses the response.
- `StoredProcAskService`: opens an ADO.NET connection, calls `dbo.AskProductQuestion` with
  `@Question` and `@MaxTokens`, returns the `@Answer` output parameter.

`AskController` accepts a `Mode` field (`"CSharp"` or `"StoredProc"`) in the request body,
resolves the matching `IAskService` implementation, and returns the answer along with which
pipeline produced it. The response is labeled in the UI so it's clear which mode answered.

**Front end**

- Chat widget lives directly on the MVC5 host page (`Home/Index.cshtml`), not a separate page.
- A mode toggle (radio buttons or switch) above the input: "C# Pipeline" / "Stored Procedure".
- Each response bubble is labeled with the mode that produced it.
- `chat-widget.js` is written as a self-contained module so it can be reused or relocated to a
  different page later without rework.

## Database Changes

1. **New column on `dbo.ProductReview`**: `ReviewEmbeddingJson NVARCHAR(MAX) NULL` — stores each
   review's embedding as a JSON-serialized `float[]`, generated and owned entirely by the C# app
   (via `IEmbeddingService`), independent of the lab's native `VECTOR` column. This avoids relying
   on `System.Data.SqlClient` (.NET Framework 4.8) to marshal the `VECTOR` type, which predates
   that feature and is not a reliable fit.
2. **Modify `dbo.AskProductQuestion`**: add a `@MaxTokens INT = 500` parameter, and change the
   payload from a hardcoded value to use it. Note the procedure currently uses
   `max_completion_tokens` in its JSON payload (the current, non-deprecated parameter for
   token-limiting chat completions — see Notes on Token Parameters below), not the older
   `max_tokens`. Keep using `max_completion_tokens`; just parameterize the value:

   ```sql
   CREATE OR ALTER PROCEDURE dbo.AskProductQuestion
       @Question NVARCHAR(1000),
       @MaxTokens INT = 500,
       @Answer NVARCHAR(MAX) OUTPUT
   AS
   BEGIN
       ...
       SET @payload = JSON_OBJECT(
           'messages': JSON_ARRAY(...),
           'max_completion_tokens': CAST(@MaxTokens AS INT),
           'temperature': 0.5
       );
       ...
   END;
   ```

3. **New table**: `dbo.CallLog` — records every call to either pipeline.

   | Column           | Type            | Notes                              |
   |------------------|-----------------|-------------------------------------|
   | CallLogId        | INT IDENTITY    | PK                                   |
   | CallTimestamp    | DATETIME2       | UTC                                   |
   | Mode             | NVARCHAR(20)    | 'CSharp' or 'StoredProc'             |
   | Question         | NVARCHAR(1000)  |                                       |
   | MaxCompletionTokens | INT          | the cap used for this call           |
   | EstimatedInputTokens | INT | nullable; C# path can estimate, SP path may be null |
   | EstimatedOutputTokens | INT | nullable |
   | Success          | BIT             |                                       |
   | ErrorMessage     | NVARCHAR(MAX)   | nullable                             |

## Cost & Safety Controls

- **`max_completion_tokens`** is a single configurable value in `Web.config`
  (`AppSettings["MaxCompletionTokens"]`, default 500), read by both `CSharpAskService` and
  `StoredProcAskService` (the latter passes it as `@MaxTokens`), so both pipelines are bounded
  identically — a fair comparison and a real cost guard.
- **Daily call cap**: `AppSettings["DailyCallLimit"]` (e.g., 200). `AskController` checks
  `dbo.CallLog` for today's call count before invoking either pipeline; if the limit is reached,
  return a friendly "daily limit reached" response instead of calling out to Azure OpenAI. This
  is the main protection against a bug or accidental loop running up real charges.
- **Every call is logged** to `dbo.CallLog` regardless of success/failure, so usage is auditable
  per pipeline and per day.

### Cost Notes (approximate, Azure OpenAI, as of Sept 2026)

- `text-embedding-3-small`: ~$0.02 / 1M tokens. Embedding all 140 reviews once: a fraction of a cent.
- `gpt-5.4-mini`: ~$0.75 / 1M input tokens, ~$4.50 / 1M output tokens. A single question/answer
  round trip: roughly $0.001–0.003. A few hundred test calls during development: well under $2 total.
- No dedicated Azure OpenAI free tier exists; this is pay-as-you-go, but at this volume the cost
  is trivial. The daily call cap above exists specifically to prevent an unexpected spike.

## Notes on Token Parameters

`max_tokens` is the original chat completions parameter — it bounds only the visible output
tokens. `max_completion_tokens` is the current parameter, introduced for reasoning models (o1,
o3, and increasingly required across newer model families including GPT-5-series models on
Azure), and it bounds the *total* tokens generated, including any hidden reasoning tokens the
model produces before the visible answer. For `gpt-5.4-mini` specifically the practical
difference is small, but `max_completion_tokens` is the forward-compatible choice and is what
the existing stored procedure already uses — keep both pipelines consistent with it.

## Hosting

- Azure App Service, **Windows** plan (required for .NET Framework 4.8), **F1 (Free) tier**.
- Existing Azure SQL Database (AdventureWorksLT), already on a cost-appropriate tier per the
  free offer used in the original lab.

## Source Control

- Git repository, initialized locally (or on GitHub first, then cloned).
- `.gitignore` for Visual Studio / .NET Framework: `bin/`, `obj/`, `.vs/`, `*.user`, `packages/`
  (if using `packages.config` rather than PackageReference).
- **No secrets committed.** Connection strings and any API keys go in a `Web.ConnectionStrings.config`
  or similar file that is gitignored; check in a `.config.example` template instead.
- `README.md` documenting: prerequisites, how to restore the DB schema changes, how to configure
  connection strings/API keys locally, how to run and test both pipeline modes.

## Open Items / Decisions Still Available

- Whether to add a "run both pipelines on the same question and show side by side" comparison
  view, versus switch-and-retest one at a time (the toggle as specified supports the latter;
  the side-by-side view is a nice stretch feature, not required for v1).
- Exact top-N value for retrieved reviews in the C# similarity ranking (the lab uses 5 as a
  starting point).
- Whether `IEmbeddingService`/`IChatCompletionService` should be built provider-agnostic from the
  start (in case of a future non-Azure provider) or hardcoded to Azure OpenAI for now, given the
  stored procedure path requires Azure OpenAI regardless.
