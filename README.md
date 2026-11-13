# AdventureWorks Product Advisor

A small retrieval-augmented generation (RAG) chat application that answers product questions from customer reviews in the AdventureWorksLT sample database on Azure SQL. Its defining feature is that the same RAG pipeline is implemented twice, once by hand in C# and once inside a T-SQL stored procedure, and the user can choose which pipeline answers each question. Both pipelines run under identical constraints (same model, token cap, number of retrieved reviews, and similarity threshold), so their answers and behavior can be compared directly.

I built it as a personal project with three aims: to refresh my hands-on C#, .NET Framework 4.8, and JavaScript skills; to gain experience building an AI feature end to end, from embeddings and retrieval to prompt construction and model calls; and to compare application-side and database-side approaches to RAG.

I chose .NET Framework 4.8 deliberately. Many production systems still run on it, and the application is intended to serve as the starting point for a future migration to modern .NET, carried out with Claude Code (see [Next steps](#next-steps)).

## How it was built

I developed this project with Claude Code as an agentic coding partner, using a spec-driven workflow:

- **Project brief:** [`AdventureWorks_Product_Advisor_Spec.md`](AdventureWorks_Product_Advisor_Spec.md) sets out the purpose, goals, non-goals, architecture, and cost controls.
- **Design specs and implementation plans:** generated with the Superpowers plugin for Claude Code and kept in [`docs/superpowers/`](docs/superpowers/). The work was delivered in slices: the C# pipeline first, then the stored procedure pipeline, call logging, and the daily call cap.
- **Working conventions for the agent:** [`CLAUDE.md`](CLAUDE.md) records the build and test commands, architecture, invariants that must hold across both pipelines, and house style.

Commits made with Claude Code carry a co-author trailer.

## Architecture

- **Stack:** ASP.NET MVC 5 and Web API 2 on .NET Framework 4.8, C#, Razor, and vanilla JavaScript and CSS (no front-end framework).
- **Request flow:** the chat widget posts to `POST /api/ask`, and `AskController` routes the question to the selected pipeline through a shared `IAskService` interface.
- **C# pipeline (`CSharpAskService`):** embeds the question with Azure OpenAI, ranks stored review embeddings by cosine similarity (`SimilarityRanker`), builds the prompt, and calls the chat completions endpoint.
- **Stored procedure pipeline (`StoredProcAskService`):** a thin ADO.NET call to `dbo.AskProductQuestion`, which performs embedding (`AI_GENERATE_EMBEDDINGS`), retrieval (`VECTOR_SEARCH`), and the Azure OpenAI call entirely in T-SQL.
- **Separate embedding stores:** the stored procedure uses the native `VECTOR(1536)` column; the C# pipeline keeps its own JSON embeddings, because `System.Data.SqlClient` on .NET Framework 4.8 predates the `VECTOR` type.
- **Authentication:** Microsoft Entra ID through `DefaultAzureCredential`; no API keys are stored in the application.

## Cost and safety controls

- The completion token cap and the number of retrieved reviews are enforced on the server, regardless of what the UI sends.
- A daily call limit is checked against `dbo.CallLog` before either pipeline runs, and every call is logged.
- Off-topic questions are rejected below a similarity threshold before the chat model is called.
- Transient Azure SQL connection errors are retried with backoff.
- Model output is rendered with `marked` and sanitized with `DOMPurify`.

## Running it locally

**Prerequisites:** Visual Studio (the projects are non-SDK `.csproj` files, built with MSBuild rather than the `dotnet` CLI); an Azure SQL database with AdventureWorksLT and the `dbo.ProductReview` table from the Microsoft Learn lab ["Implement RAG solutions"](https://microsoftlearning.github.io/mslearn-sql-developer/Instructions/Labs/11-implement-rag-solutions.html); and an Azure OpenAI resource with embedding and chat model deployments.

1. Apply the scripts in `sql/`: `AddReviewEmbeddingJsonColumn.sql`, `CreateCallLogTable.sql`, `CreateAppConfigTable.sql`, and `dbo.AskProductQuestion.sql`.
2. Copy `Web.ConnectionStrings.config.example` to `Web.ConnectionStrings.config` and add your connection string. Put your Azure OpenAI endpoint and tenant ID in `Web.AppSettings.config`. Both files are gitignored.
3. Build and run from Visual Studio (IIS Express).
4. Call `/api/admin/backfill-embeddings` once to generate the C# pipeline's review embeddings.
5. Ask a question, and switch between the C# and stored procedure pipelines to compare the answers.

## Tests

MSTest v2 and Moq unit tests in `AdventureWorksProductAdvisor.Tests` cover the controllers and services. Fake HTTP and credential helpers let the Azure OpenAI services be tested without network access or credentials. See `CLAUDE.md` for the build and test commands.

## Next steps

- Migrate the application from .NET Framework 4.8 to modern .NET (ASP.NET Core) with Claude Code, to see how an agentic workflow handles planning and carrying out a framework migration.
