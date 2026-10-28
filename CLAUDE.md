# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

An ASP.NET MVC5 + Web API 2 app on **.NET Framework 4.8** that answers product questions by RAG over
the AdventureWorksLT `dbo.ProductReview` table (140 lab-seeded reviews in Azure SQL). Its defining
feature: **the same RAG pipeline implemented twice** — once by hand in C#, once inside the
`dbo.AskProductQuestion` T-SQL stored procedure — selectable per question from the UI so the two can
be compared under identical constraints (same model, same token cap, same top-N, same similarity
threshold). Project brief and rationale: `AdventureWorks_Product_Advisor_Spec.md`.

## Build and test

There is **no `dotnet` CLI path** — both projects are hand-authored non-SDK csproj files, so use
MSBuild and vstest from the VS 2026 install:

```powershell
# Build (restore + build both projects)
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build

# All tests
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" `
  "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll"

# One class or one test (append to the vstest command)
-TestCaseFilter:"FullyQualifiedName~CSharpAskServiceTests"
```

Run the app from Visual Studio (IIS Express, `http://localhost:63330/`); it needs real Azure SQL and
Azure OpenAI access to answer anything.

## Non-SDK project mechanics (easy to get wrong)

- **No file globbing.** Every new `.cs` needs an explicit `<Compile Include="..." />`, and every new
  `.cshtml`/`.js`/`.css` a `<Content Include="..." />`, in the relevant `.csproj`. Without it the file
  simply isn't compiled and the build still "succeeds" — a silent no-op.
- **Binding redirects are maintained by hand in `Web.config`.** `AutoGenerateBindingRedirects` writes
  them to the side-car `bin\AdventureWorksProductAdvisor.dll.config`, which ASP.NET never reads. After
  adding a main-project `PackageReference`: build, open that `.dll.config`, copy any new
  `<dependentAssembly>` blocks into `Web.config`'s `<runtime><assemblyBinding>`.
- NuGet is `PackageReference` (not `packages.config`) deliberately — packages.config restore in this
  csproj format does not resolve transitive dependencies.

## Architecture

Request flow: `Views/Home/Index.cshtml` + `Scripts/chat-widget.js` → `POST /api/ask`
(`Controllers/Api/AskController.cs`) → an `IAskService` → Azure OpenAI and/or Azure SQL.

- **`IAskService` is the pipeline seam** (`AskAsync(question, maxCompletionTokens, topN)`).
  `AskServiceFactory.CreatePipelines()` returns a `Mode → IAskService` dictionary (`"CSharp"`,
  `"StoredProc"`); `AskController` only does a dictionary lookup. Adding a pipeline = new class + one
  dictionary entry, no controller change.
- **`AskServiceFactory` is the composition root** — the only place that reads `ConfigurationManager`
  for service construction and the only place that builds real HTTP/DB dependencies. It holds a
  `static HttpClient` and a `static DefaultAzureCredential` because ASP.NET creates a new controller
  per request. `DefaultAzureCredentialOptions.TenantId` is required (the dev account is a B2B guest;
  without it tokens come from the wrong directory and Azure OpenAI returns 401).
- **Every controller has two constructors**: a parameterless one that calls the factory (used at
  runtime) and an injectable one (used by tests). Keep that pattern for new controllers.
- `CSharpAskService`: embed question + load all reviews in parallel → `SimilarityRanker` cosine
  top-N over `ReviewEmbeddingJson` → builds the prompt → `IChatCompletionService`.
  `StoredProcAskService`: thin ADO.NET call to `dbo.AskProductQuestion`, mapping the proc's known
  error-message prefixes to `Success=false`.
- `AskServiceResult` (pipeline layer) is deliberately separate from `Models/AskResponse` (HTTP shape,
  adds `Mode`/`ErrorMessage`). Pipelines know nothing about HTTP.
- `Data/SqlRetry.OpenAsync` wraps *every* `connection.OpenAsync()` (transient Azure SQL error numbers,
  5s/10s backoff). Use it for any new SQL access.
- `/api/admin/backfill-embeddings` runs `ReviewEmbeddingBackfillRunner` to populate missing
  `ReviewEmbeddingJson` values — run it once after seeding reviews or the C# pipeline has nothing to
  rank.

### Pipeline parity is a hard requirement

The comparison is the point, so anything that shapes an answer must change in **both** places at once:
the system prompt (`AzureOpenAiChatCompletionService.SystemPrompt` ↔ the proc's system message), the
off-topic rejection threshold and its canned message (`MinSimilarityScore` ↔ `@MinSimilarityScore`),
`max_completion_tokens` (never the legacy `max_tokens`), and top-N. Both pipelines read the same
`Web.config` values via the factory rather than keeping their own defaults in sync by hand.

### Cost and safety guards (don't weaken these)

`MaxCompletionTokens` and `TopN` are server-side only — `AskRequest` has no token field at all, and
`AskController` re-validates `TopN` (1–10) and question length (≤1000, matching
`dbo.CallLog.Question`) regardless of what the UI allows. `DailyCallLimit` is checked against
`dbo.CallLog` *before* either pipeline runs. Deliberate error-handling asymmetry: a failed
call-*count* read fails open (never block Q&A over a broken audit table), a failed CallLog *write* is
swallowed with `Trace.TraceError` (an already-billed answer must not become a user-facing error), and
the browser gets a generic message while `CallLog`/`Trace` keep the real exception. Validation
failures return HTTP 200 with `Success=false`; the JSON flag, not the status code, is the contract.

## Config and secrets

`Web.config` is committed with **placeholder** values. Real values live in two gitignored files:
`Web.ConnectionStrings.config` (`configSource`, template in `.config.example`) and
`Web.AppSettings.config` (`appSettings file=`, overrides `AzureOpenAiEndpoint` and
`AzureAdTenantId`). Never commit the real endpoint or tenant id — including into `Sql/*.sql`, where
`@url`/`@credential` keep the `your-openai-resource` placeholder (`dbo.AppConfig` gives the proc the
same "edit config, not code" story). `brianminnich_embedding_model` in the SQL *is* intentional and
stays.

## Database

Scripts in `Sql/` are applied manually (no migration tooling): `AddReviewEmbeddingJsonColumn.sql`,
`CreateCallLogTable.sql`, `CreateAppConfigTable.sql`, `dbo.AskProductQuestion.sql`. Two independent
embedding columns on `dbo.ProductReview` — native `VECTOR(1536) ReviewVector` belongs to the stored
proc (`AI_GENERATE_EMBEDDINGS`/`VECTOR_SEARCH`) and must stay untouched; `ReviewEmbeddingJson
NVARCHAR(MAX)` is the C# pipeline's own JSON `float[]`, because `System.Data.SqlClient` on .NET
Framework 4.8 predates the `VECTOR` type.

## Front end

Vanilla JS and plain CSS only — no SPA framework, no jQuery, no Bootstrap, no bundling. A single DOM
tree in `Index.cshtml` serves both the pre-first-question panel and the post-question sidebar layout;
`chat-widget.js` toggles `has-chat` on `#chatShell` and `chat-widget.css` has a rule set per state
(avoids duplicate element ids). LLM output is rendered through `marked` then sanitized with
`DOMPurify`, both pinned to exact CDN versions with SRI hashes — keep the sanitize step if you touch
rendering.

## Tests

MSTest v2 + Moq, mirroring the source tree under `AdventureWorksProductAdvisor.Tests/`. Names read
`Method_Scenario_Expectation`. `TestHelpers/FakeHttpMessageHandler` and `FakeTokenCredential` cover
the Azure OpenAI services without network or credentials. Thin ADO.NET wrappers (`ReviewRepository`,
`CallLogService`, `StoredProcAskService`) are intentionally untested — mocking `SqlConnection` buys
nothing. New test files need a `<Compile Include>` entry like any other file.

## Conventions

- Comments explain **why**, at unusually high density, and are part of the house style — new code is
  expected to match, including notes about cross-file invariants (e.g. prompt parity with the SQL).
  Don't strip them when editing nearby code.
- Commit subjects are imperative and plain, no prefixes: "Retry the SQL connect step on transient
  Azure SQL errors".
- **Before committing, revert local run settings** that Visual Studio writes into tracked files:
  `Web.config`'s `<compilation debug="true">` back to `false`, and the csproj's `UseIIS`/`IISUrl`/
  `Use64BitIISExpress` back to their committed values. They show up as spurious diffs every session.
- Design docs and task plans live in `docs/superpowers/specs/` and `docs/superpowers/plans/`;
  `.superpowers/sdd/` holds per-slice execution records. Check the newest plan for in-flight work.
