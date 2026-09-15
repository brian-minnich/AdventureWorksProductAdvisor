# Slice 2: Stored Procedure Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make "Stored Procedure" mode in the AdventureWorks Product Advisor UI actually work, and add a daily call-count safety cap shared by both pipelines.

**Architecture:** Add `StoredProcAskService` (a thin ADO.NET wrapper implementing the existing `IAskService` interface, calling the now-parameterized `dbo.AskProductQuestion`) and register it in `AskServiceFactory`'s pipeline dictionary — `AskController` needs no new branching logic to support it, only the daily-cap check that already gates both pipelines uniformly. A new `dbo.CallLog` table plus `ICallLogService`/`CallLogService` record every call (success or failure) and answer "how many calls happened today" for the cap check.

**Tech Stack:** ASP.NET MVC5 + Web API 2, .NET Framework 4.8, C#, ADO.NET (`System.Data.SqlClient`), Azure SQL (T-SQL, `AI_GENERATE_EMBEDDINGS`, `VECTOR_SEARCH`, `sp_invoke_external_rest_endpoint`), MSTest v2 + Moq.

**Spec:** `docs/superpowers/specs/2026-09-15-slice-2-stored-proc-pipeline-design.md`

## Global Constraints

- Daily call limit is `200`, configured via `Web.config` `AppSettings["DailyCallLimit"]`.
- If the daily-cap count-check read itself fails, fail open (treat as under the limit) — never block Q&A over a broken safety-net table. `Trace.TraceError` either way.
- If writing a `CallLog` row fails, swallow it (`Trace.TraceError` only) — never turn a completed answer into a user-facing error over a logging failure.
- `CallLog.ErrorMessage` holds the real exception detail (`ex.Message`), not the generic client-facing string — `CallLog` is never exposed to the browser.
- `EstimatedInputTokens`/`EstimatedOutputTokens` stay `NULL` on every row this slice — not computed by either pipeline yet.
- The real Azure OpenAI endpoint/credential name and `USE MODEL brianminnich_embedding_model` are the only "real" values that go in `Sql/dbo.AskProductQuestion.sql`'s committed form is the `your-openai-resource` placeholder for `@url`/`@credential` — never commit the real endpoint. `brianminnich_embedding_model` is intentional and stays as-is (confirmed with the user — not a secret, just a name).
- `Sql/dbo.AskProductQuestion.sql` gets re-saved as UTF-8 in this change (currently UTF-16LE).
- No new unit tests for `StoredProcAskService`/`CallLogService` — thin ADO.NET wrappers, same reasoning already applied to `ReviewRepository` in this codebase.
- This is a non-SDK-style `.csproj` (`<Project ToolsVersion="15.0">`) — every new `.cs` file needs an explicit `<Compile Include="...">` entry in `AdventureWorksProductAdvisor.csproj` or it will not build.

---

## Task 1: Database changes — `CallLog` table and `dbo.AskProductQuestion` parameterization

**Files:**
- Create: `Sql/CreateCallLogTable.sql`
- Modify: `Sql/dbo.AskProductQuestion.sql`

**Interfaces:**
- Produces: `dbo.CallLog` table (columns per spec) and `dbo.AskProductQuestion(@Question NVARCHAR(1000), @MaxTokens INT = 500, @TopN INT = 5, @Answer NVARCHAR(MAX) OUTPUT)`, consumed by `StoredProcAskService` (Task 4) and `CallLogService` (Task 3).

- [ ] **Step 1: Create the `CallLog` table script**

Create `Sql/CreateCallLogTable.sql`:

```sql
CREATE TABLE dbo.CallLog (
    CallLogId INT IDENTITY(1,1) PRIMARY KEY,
    CallTimestamp DATETIME2 NOT NULL,
    Mode NVARCHAR(20) NOT NULL,
    Question NVARCHAR(1000) NOT NULL,
    MaxCompletionTokens INT NOT NULL,
    EstimatedInputTokens INT NULL,
    EstimatedOutputTokens INT NULL,
    Success BIT NOT NULL,
    ErrorMessage NVARCHAR(MAX) NULL
);
```

- [ ] **Step 2: Rewrite `dbo.AskProductQuestion.sql` with the new parameters, saved as UTF-8**

Replace the full contents of `Sql/dbo.AskProductQuestion.sql` with:

```sql
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[AskProductQuestion]
    @Question NVARCHAR(1000),
    @MaxTokens INT = 500,
    @TopN INT = 5,
    @Answer NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @questionVector VECTOR(1536);
    DECLARE @context NVARCHAR(MAX);
    DECLARE @payload NVARCHAR(MAX);
    DECLARE @response NVARCHAR(MAX);
    DECLARE @returnValue INT;

    -- Step 1: Convert the question to an embedding
    SELECT @questionVector = AI_GENERATE_EMBEDDINGS(@Question USE MODEL brianminnich_embedding_model);

    -- Step 2: Retrieve relevant reviews using ANN vector search
    SET @context = (
        SELECT TOP (@TopN) WITH APPROXIMATE
            p.Name AS ProductName,
            p.ListPrice,
            pc.Name AS Category,
            r.Rating,
            r.ReviewTitle,
            r.ReviewText
        FROM VECTOR_SEARCH(
            TABLE = dbo.ProductReview AS r,
            COLUMN = ReviewVector,
            SIMILAR_TO = @questionVector,
            METRIC = 'cosine'
        ) AS vs
        INNER JOIN SalesLT.Product p
            ON r.ProductID = p.ProductID
        INNER JOIN SalesLT.ProductCategory pc
            ON p.ProductCategoryID = pc.ProductCategoryID
        ORDER BY vs.distance
        FOR JSON PATH
    );

    -- Check if context was retrieved
    IF @context IS NULL
    BEGIN
        SET @Answer = 'No reviews found matching your query. Please try a different question.';
        RETURN;
    END

    -- Step 3: Build the augmented prompt
    SET @payload = JSON_OBJECT(
        'messages': JSON_ARRAY(
            JSON_OBJECT(
                'role': 'system',
                'content': 'You are an Adventure Works product assistant. Follow these rules:
1. Answer only using the provided product reviews and data
2. Reference specific customer experiences from the reviews when relevant
3. Include star ratings to help the customer assess product quality
4. Keep responses under 150 words
5. Suggest related products when relevant'
            ),
            JSON_OBJECT(
                'role': 'user',
                'content': 'Product reviews: ' + @context + CHAR(10) + CHAR(10) + 'Customer question: ' + @Question
            )
        ),
        'max_completion_tokens': CAST(@MaxTokens AS INT),
        'temperature': 0.5
    );

    -- Step 4: Call the model
    EXECUTE @returnValue = sp_invoke_external_rest_endpoint
        @url = N'https://your-openai-resource.openai.azure.com/openai/deployments/gpt-5.4-mini/chat/completions?api-version=2024-10-21',
        @method = 'POST',
        @payload = @payload,
        @credential = [https://your-openai-resource.openai.azure.com],
        @response = @response OUTPUT;

    -- Step 5: Extract the answer or handle errors
    IF @returnValue = 0
        SET @Answer = JSON_VALUE(@response, '$.result.choices[0].message.content');
    ELSE IF @returnValue = 429
        SET @Answer = 'The service is currently busy. Please try again in a moment.';
    ELSE IF @returnValue IN (401, 403)
        SET @Answer = 'Authentication failed. Please check the credential configuration.';
    ELSE
        SET @Answer = 'Unable to process your question at this time. HTTP status: ' + CAST(@returnValue AS NVARCHAR(10));
END;
```

Save the file as UTF-8 (no BOM required, but if your editor defaults to UTF-16 for `.sql` files — likely, since that's how it got that way originally — explicitly pick UTF-8 in the save/encoding dialog).

- [ ] **Step 3: Run both scripts against the real Azure SQL database with the real endpoint substituted, without ever writing the real endpoint into the tracked file**

Never edit the tracked file's `@url`/`@credential` to the real value directly — do the substitution into a throwaway copy instead, so there's no risk of the real endpoint ending up in a commit:

```powershell
# From the repo root. Replace <your-real-endpoint> with your actual
# Azure OpenAI resource hostname (e.g. proj-brianminnich-ai-resource.openai.azure.com).
$real = "<your-real-endpoint>"
(Get-Content "Sql\dbo.AskProductQuestion.sql" -Raw) `
    -replace "your-openai-resource\.openai\.azure\.com", $real |
    Set-Content -Encoding utf8 "$env:TEMP\dbo.AskProductQuestion.deploy.sql"

sqlcmd -S <your-server>.database.windows.net -d AdventureWorksLT -G -i "Sql\CreateCallLogTable.sql"
sqlcmd -S <your-server>.database.windows.net -d AdventureWorksLT -G -i "$env:TEMP\dbo.AskProductQuestion.deploy.sql"

Remove-Item "$env:TEMP\dbo.AskProductQuestion.deploy.sql"
```

(`-G` uses Azure AD integrated auth; substitute your actual login method if different.) Before running, confirm a `CREATE EXTERNAL MODEL` object named `brianminnich_embedding_model` already exists in the target database — if not, this procedure will fail at `AI_GENERATE_EMBEDDINGS` regardless of the changes above (out of scope for this plan to create; assumed already set up from the original MS Learn lab, per the project brief).

- [ ] **Step 4: Verify both objects exist**

```sql
SELECT name FROM sys.tables WHERE name = 'CallLog';
SELECT name FROM sys.objects WHERE name = 'AskProductQuestion' AND type = 'P';
```

Expected: one row each.

- [ ] **Step 5: Smoke-test the procedure directly**

```sql
DECLARE @Answer NVARCHAR(MAX);
EXEC dbo.AskProductQuestion @Question = N'What lights are good for riding before sunrise?', @MaxTokens = 200, @TopN = 3, @Answer = @Answer OUTPUT;
SELECT @Answer;
```

Expected: a real, review-grounded answer text (not an error string like "Authentication failed" or "Unable to process").

- [ ] **Step 6: Commit**

```bash
git add "Sql/CreateCallLogTable.sql" "Sql/dbo.AskProductQuestion.sql"
git commit -m "Add dbo.CallLog table; parameterize dbo.AskProductQuestion with @MaxTokens/@TopN"
```

---

## Task 2: `CallLogEntry`, `ICallLogService`, `CallLogService`

**Files:**
- Create: `AdventureWorksProductAdvisor/Models/CallLogEntry.cs`
- Create: `AdventureWorksProductAdvisor/Services/ICallLogService.cs`
- Create: `AdventureWorksProductAdvisor/Services/CallLogService.cs`
- Modify: `AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj`

**Interfaces:**
- Consumes: `dbo.CallLog` table (Task 1).
- Produces: `CallLogEntry` (POCO), `ICallLogService.GetTodaysCallCountAsync() : Task<int>`, `ICallLogService.LogCallAsync(CallLogEntry) : Task`, `CallLogService(string connectionString) : ICallLogService` — consumed by `AskServiceFactory` (Task 5) and `AskController` (Task 6).

- [ ] **Step 1: Create `Models/CallLogEntry.cs`**

```csharp
using System;

namespace AdventureWorksProductAdvisor.Models
{
    // One row of dbo.CallLog - written after every pipeline call attempt
    // (success or failure) so usage is auditable per pipeline and per day.
    // EstimatedInputTokens/EstimatedOutputTokens stay null in this slice;
    // neither pipeline computes them yet.
    public class CallLogEntry
    {
        public DateTime CallTimestamp { get; set; }
        public string Mode { get; set; }
        public string Question { get; set; }
        public int MaxCompletionTokens { get; set; }
        public int? EstimatedInputTokens { get; set; }
        public int? EstimatedOutputTokens { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }
}
```

- [ ] **Step 2: Create `Services/ICallLogService.cs`**

```csharp
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Models;

namespace AdventureWorksProductAdvisor.Services
{
    // The daily-cap check (GetTodaysCallCountAsync) and the audit log
    // (LogCallAsync) both live behind this one interface, so AskController
    // never needs to know it's talking to a real database - same idea as
    // IReviewRepository.
    public interface ICallLogService
    {
        Task<int> GetTodaysCallCountAsync();
        Task LogCallAsync(CallLogEntry entry);
    }
}
```

- [ ] **Step 3: Create `Services/CallLogService.cs`**

```csharp
using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Models;

namespace AdventureWorksProductAdvisor.Services
{
    // Plain ADO.NET, same style as ReviewRepository - no ORM. Not unit
    // tested (see the plan's Global Constraints) - exercised via manual
    // testing against the real Azure SQL database.
    public class CallLogService : ICallLogService
    {
        private readonly string _connectionString;

        public CallLogService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<int> GetTodaysCallCountAsync()
        {
            const string sql = "SELECT COUNT(*) FROM dbo.CallLog WHERE CallTimestamp >= @TodayStartUtc";
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                // Computed here (not in T-SQL) so "today" is unambiguously
                // UTC-calendar-day, matching CallTimestamp's own UTC convention.
                command.Parameters.AddWithValue("@TodayStartUtc", DateTime.UtcNow.Date);
                await connection.OpenAsync();
                return (int)await command.ExecuteScalarAsync();
            }
        }

        public async Task LogCallAsync(CallLogEntry entry)
        {
            const string sql = @"
                INSERT INTO dbo.CallLog
                    (CallTimestamp, Mode, Question, MaxCompletionTokens, EstimatedInputTokens, EstimatedOutputTokens, Success, ErrorMessage)
                VALUES
                    (@CallTimestamp, @Mode, @Question, @MaxCompletionTokens, @EstimatedInputTokens, @EstimatedOutputTokens, @Success, @ErrorMessage)";
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@CallTimestamp", entry.CallTimestamp);
                command.Parameters.AddWithValue("@Mode", entry.Mode);
                command.Parameters.AddWithValue("@Question", entry.Question);
                command.Parameters.AddWithValue("@MaxCompletionTokens", entry.MaxCompletionTokens);
                // AddWithValue can't take a C# null directly for a nullable
                // int - it has to be boxed then coalesced to DBNull.Value,
                // or SqlClient throws.
                command.Parameters.AddWithValue("@EstimatedInputTokens", (object)entry.EstimatedInputTokens ?? DBNull.Value);
                command.Parameters.AddWithValue("@EstimatedOutputTokens", (object)entry.EstimatedOutputTokens ?? DBNull.Value);
                command.Parameters.AddWithValue("@Success", entry.Success);
                command.Parameters.AddWithValue("@ErrorMessage", (object)entry.ErrorMessage ?? DBNull.Value);
                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
```

- [ ] **Step 4: Register the three new files in the `.csproj`**

In `AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj`, in the `<ItemGroup>` containing `<Compile Include="Models\AskRequest.cs" />`, add the three new files:

```xml
    <Compile Include="Models\AskRequest.cs" />
    <Compile Include="Models\AskResponse.cs" />
    <Compile Include="Models\CallLogEntry.cs" />
    <Compile Include="Services\ICallLogService.cs" />
    <Compile Include="Services\CallLogService.cs" />
    <Compile Include="Services\AskServiceFactory.cs" />
```

(This replaces the existing three-line block `<Compile Include="Models\AskRequest.cs" />` / `<Compile Include="Models\AskResponse.cs" />` / `<Compile Include="Services\AskServiceFactory.cs" />` with the six lines above, inserting the three new entries between `AskResponse.cs` and `AskServiceFactory.cs`.)

- [ ] **Step 5: Build to confirm it compiles**

```bash
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add "AdventureWorksProductAdvisor/Models/CallLogEntry.cs" "AdventureWorksProductAdvisor/Services/ICallLogService.cs" "AdventureWorksProductAdvisor/Services/CallLogService.cs" "AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj"
git commit -m "Add CallLogEntry, ICallLogService, CallLogService"
```

---

## Task 3: `StoredProcAskService`

**Files:**
- Create: `AdventureWorksProductAdvisor/Services/StoredProcAskService.cs`
- Modify: `AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj`

**Interfaces:**
- Consumes: `dbo.AskProductQuestion` (Task 1), `IAskService` (existing — `Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN)`), `AskServiceResult { bool Success, string Answer }` (existing).
- Produces: `StoredProcAskService(string connectionString) : IAskService`, consumed by `AskServiceFactory` (Task 5).

- [ ] **Step 1: Create `Services/StoredProcAskService.cs`**

```csharp
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Services
{
    // The stored-procedure-backed twin of CSharpAskService - same
    // IAskService contract, but the entire embed -> retrieve -> rank ->
    // prompt -> call -> parse pipeline happens inside dbo.AskProductQuestion
    // instead of in C#. No try/catch here: SQL exceptions (bad connection,
    // timeout, the procedure itself erroring) propagate to AskController's
    // existing catch block, exactly like unhandled exceptions from
    // CSharpAskService do today. Not unit tested - see the plan's Global
    // Constraints.
    public class StoredProcAskService : IAskService
    {
        private readonly string _connectionString;

        public StoredProcAskService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN)
        {
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand("dbo.AskProductQuestion", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@Question", question);
                command.Parameters.AddWithValue("@MaxTokens", maxCompletionTokens);
                command.Parameters.AddWithValue("@TopN", topN);
                var answerParam = command.Parameters.Add("@Answer", SqlDbType.NVarChar, -1);
                answerParam.Direction = ParameterDirection.Output;

                await connection.OpenAsync();
                // The procedure has no result set - it only ever SETs
                // @Answer - so ExecuteNonQueryAsync is correct here; there's
                // no reader to read.
                await command.ExecuteNonQueryAsync();

                return new AskServiceResult { Success = true, Answer = answerParam.Value as string };
            }
        }
    }
}
```

- [ ] **Step 2: Register the new file in the `.csproj`**

In `AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj`, add it next to `CSharpAskService.cs`:

```xml
    <Compile Include="Services\IAskService.cs" />
    <Compile Include="Services\CSharpAskService.cs" />
    <Compile Include="Services\StoredProcAskService.cs" />
```

(Replaces the existing two-line `IAskService.cs`/`CSharpAskService.cs` block with the three lines above.)

- [ ] **Step 3: Build to confirm it compiles**

```bash
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add "AdventureWorksProductAdvisor/Services/StoredProcAskService.cs" "AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj"
git commit -m "Add StoredProcAskService"
```

---

## Task 4: Wire both new services into `AskServiceFactory`

**Files:**
- Modify: `AdventureWorksProductAdvisor/Services/AskServiceFactory.cs`

**Interfaces:**
- Consumes: `CallLogService` (Task 2), `StoredProcAskService` (Task 3).
- Produces: `AskServiceFactory.CreateCallLogService() : ICallLogService`, and `CreatePipelines()` now also returns a `"StoredProc"` entry — both consumed by `AskController` (Task 6).

- [ ] **Step 1: Add `CreateCallLogService()` and the `"StoredProc"` pipeline entry**

In `AdventureWorksProductAdvisor/Services/AskServiceFactory.cs`, add a new method after `CreateReviewRepository()`:

```csharp
        public static IReviewRepository CreateReviewRepository()
        {
            // The connection string lives in Web.ConnectionStrings.config,
            // which is gitignored - see Web.config's
            // <connectionStrings configSource="..."> for how it's wired in.
            var connectionString = ConfigurationManager.ConnectionStrings["AdventureWorksLT"].ConnectionString;
            return new ReviewRepository(connectionString);
        }

        public static ICallLogService CreateCallLogService()
        {
            var connectionString = ConfigurationManager.ConnectionStrings["AdventureWorksLT"].ConnectionString;
            return new CallLogService(connectionString);
        }
```

And replace `CreatePipelines()`:

```csharp
        public static IReadOnlyDictionary<string, IAskService> CreatePipelines()
        {
            var csharpAskService = new CSharpAskService(
                CreateReviewRepository(), CreateEmbeddingService(), CreateChatCompletionService());

            var connectionString = ConfigurationManager.ConnectionStrings["AdventureWorksLT"].ConnectionString;
            var storedProcAskService = new StoredProcAskService(connectionString);

            // This dictionary is what AskController.Post looks a Mode up in.
            // Adding a pipeline is just adding one more entry here -
            // AskController itself doesn't need to change.
            return new Dictionary<string, IAskService>
            {
                ["CSharp"] = csharpAskService,
                ["StoredProc"] = storedProcAskService
            };
        }
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add "AdventureWorksProductAdvisor/Services/AskServiceFactory.cs"
git commit -m "Wire StoredProcAskService and CallLogService into AskServiceFactory"
```

---

## Task 5: `AskController` — daily cap and call logging (TDD)

**Files:**
- Modify: `AdventureWorksProductAdvisor/Controllers/Api/AskController.cs`
- Modify: `AdventureWorksProductAdvisor.Tests/Controllers/Api/AskControllerTests.cs`

**Interfaces:**
- Consumes: `ICallLogService` (Task 2), `CallLogEntry` (Task 2), `AskServiceFactory.CreateCallLogService()` (Task 4).
- Produces: `AskController(IReadOnlyDictionary<string, IAskService>, int, ICallLogService, int)` — the testable constructor gains two parameters (`callLogService`, `dailyCallLimit`); this is a breaking change to the constructor signature, handled below.

- [ ] **Step 1: Write the new failing tests and update the test helper**

In `AdventureWorksProductAdvisor.Tests/Controllers/Api/AskControllerTests.cs`, replace the `CreateController` helper at the bottom of the class:

```csharp
        // Building an ApiController for a unit test needs a bit of manual
        // setup - Configuration/Request aren't populated automatically
        // outside of a real HTTP pipeline, and calling Ok(...) inside the
        // controller throws without them. callLogService defaults to a mock
        // that reports 0 calls today and accepts any LogCallAsync call, so
        // existing tests that don't care about logging/the cap keep working
        // unchanged.
        private static AskController CreateController(
            IReadOnlyDictionary<string, IAskService> pipelines,
            ICallLogService callLogService = null,
            int dailyCallLimit = 200)
        {
            if (callLogService == null)
            {
                var defaultMock = new Mock<ICallLogService>();
                defaultMock.Setup(s => s.GetTodaysCallCountAsync()).ReturnsAsync(0);
                defaultMock.Setup(s => s.LogCallAsync(It.IsAny<CallLogEntry>())).Returns(Task.CompletedTask);
                callLogService = defaultMock.Object;
            }

            var controller = new AskController(pipelines, 500, callLogService, dailyCallLimit);
            controller.Configuration = new HttpConfiguration();
            controller.Request = new HttpRequestMessage();
            return controller;
        }
```

Add `using AdventureWorksProductAdvisor.Models;` to the top of the file if it isn't already there (`CallLogEntry` lives in `Models`) — it is already there via the existing `AskRequest`/`AskResponse` usage, so no change needed.

Then add five new `[TestMethod]`s inside the class, after the existing three:

```csharp
        [TestMethod]
        public async Task Post_DailyLimitReached_ReturnsFriendlyErrorWithoutInvokingPipelineOrLogging()
        {
            var askService = new Mock<IAskService>();
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var callLogService = new Mock<ICallLogService>();
            callLogService.Setup(s => s.GetTodaysCallCountAsync()).ReturnsAsync(5);
            var controller = CreateController(pipelines, callLogService.Object, dailyCallLimit: 5);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsNotNull(okResult);
            Assert.IsFalse(okResult.Content.Success);
            StringAssert.Contains(okResult.Content.ErrorMessage, "Daily call limit reached");
            askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            callLogService.Verify(s => s.LogCallAsync(It.IsAny<CallLogEntry>()), Times.Never);
        }

        [TestMethod]
        public async Task Post_UnderDailyLimit_LogsSuccessfulCall()
        {
            var askService = new Mock<IAskService>();
            askService.Setup(s => s.AskAsync("What is the best product?", 500, 5))
                .ReturnsAsync(new AskServiceResult { Success = true, Answer = "It's great." });
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var callLogService = new Mock<ICallLogService>();
            callLogService.Setup(s => s.GetTodaysCallCountAsync()).ReturnsAsync(0);
            CallLogEntry loggedEntry = null;
            callLogService.Setup(s => s.LogCallAsync(It.IsAny<CallLogEntry>()))
                .Callback<CallLogEntry>(e => loggedEntry = e)
                .Returns(Task.CompletedTask);
            var controller = CreateController(pipelines, callLogService.Object);

            var result = await controller.Post(new AskRequest { Question = "What is the best product?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsTrue(okResult.Content.Success);
            Assert.IsNotNull(loggedEntry);
            Assert.AreEqual("CSharp", loggedEntry.Mode);
            Assert.AreEqual("What is the best product?", loggedEntry.Question);
            Assert.AreEqual(500, loggedEntry.MaxCompletionTokens);
            Assert.IsTrue(loggedEntry.Success);
            Assert.IsNull(loggedEntry.ErrorMessage);
        }

        [TestMethod]
        public async Task Post_PipelineThrows_LogsFailureAndReturnsGenericError()
        {
            var askService = new Mock<IAskService>();
            askService.Setup(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("SQL timeout"));
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var callLogService = new Mock<ICallLogService>();
            callLogService.Setup(s => s.GetTodaysCallCountAsync()).ReturnsAsync(0);
            CallLogEntry loggedEntry = null;
            callLogService.Setup(s => s.LogCallAsync(It.IsAny<CallLogEntry>()))
                .Callback<CallLogEntry>(e => loggedEntry = e)
                .Returns(Task.CompletedTask);
            var controller = CreateController(pipelines, callLogService.Object);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsFalse(okResult.Content.Success);
            StringAssert.Contains(okResult.Content.ErrorMessage, "An error occurred");
            Assert.IsNotNull(loggedEntry);
            Assert.IsFalse(loggedEntry.Success);
            Assert.AreEqual("SQL timeout", loggedEntry.ErrorMessage);
        }

        [TestMethod]
        public async Task Post_LogCallAsyncThrows_StillReturnsRealAnswer()
        {
            var askService = new Mock<IAskService>();
            askService.Setup(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new AskServiceResult { Success = true, Answer = "It's great." });
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var callLogService = new Mock<ICallLogService>();
            callLogService.Setup(s => s.GetTodaysCallCountAsync()).ReturnsAsync(0);
            callLogService.Setup(s => s.LogCallAsync(It.IsAny<CallLogEntry>()))
                .ThrowsAsync(new InvalidOperationException("log write failed"));
            var controller = CreateController(pipelines, callLogService.Object);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsNotNull(okResult);
            Assert.IsTrue(okResult.Content.Success);
            Assert.AreEqual("It's great.", okResult.Content.Answer);
        }

        [TestMethod]
        public async Task Post_CallCountCheckThrows_FailsOpenAndInvokesPipeline()
        {
            var askService = new Mock<IAskService>();
            askService.Setup(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new AskServiceResult { Success = true, Answer = "It's great." });
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var callLogService = new Mock<ICallLogService>();
            callLogService.Setup(s => s.GetTodaysCallCountAsync()).ThrowsAsync(new InvalidOperationException("CallLog unreachable"));
            callLogService.Setup(s => s.LogCallAsync(It.IsAny<CallLogEntry>())).Returns(Task.CompletedTask);
            var controller = CreateController(pipelines, callLogService.Object);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsTrue(okResult.Content.Success);
            askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }
```

- [ ] **Step 2: Run the tests to verify they fail (compile error expected)**

```bash
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build
```

Expected: FAIL — a compilation error, since `AskController` doesn't yet have a 4-argument constructor. This is the red state for a statically-typed language: the test code references an API that doesn't exist yet.

- [ ] **Step 3: Implement the daily-cap check and call logging in `AskController`**

Replace the full contents of `AdventureWorksProductAdvisor/Controllers/Api/AskController.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Http;
using AdventureWorksProductAdvisor.Models;
using AdventureWorksProductAdvisor.Services;

namespace AdventureWorksProductAdvisor.Controllers.Api
{
    // [RoutePrefix] + [Route("")] on Post() below means this whole class
    // answers POST requests to /api/ask (see WebApiConfig.Register, which
    // turns attribute routing on).
    [RoutePrefix("api/ask")]
    public class AskController : ApiController
    {
        // The "pipeline" is whichever IAskService implementation should
        // handle a given Mode ("CSharp" or "StoredProc"). Keeping it as a
        // dictionary means adding a new pipeline later is just adding a new
        // entry, not changing this controller.
        private readonly IReadOnlyDictionary<string, IAskService> _pipelinesByMode;

        // The token cap for the chat model. This is a cost guard, so it is
        // deliberately NOT something the caller can set via the request body
        // - see AskRequest, which has no MaxCompletionTokens field at all.
        private readonly int _maxCompletionTokens;

        // Records every call attempt (success or failure) and answers
        // "how many calls happened today" for the daily-cap check below.
        private readonly ICallLogService _callLogService;

        // The other cost guard: a hard ceiling on total calls/day across
        // both pipelines, checked before either one is ever invoked.
        private readonly int _dailyCallLimit;

        // This is the constructor ASP.NET actually uses at runtime. It builds
        // the real pipelines (real HTTP calls, real database) via
        // AskServiceFactory, and reads config from Web.config.
        public AskController()
            : this(AskServiceFactory.CreatePipelines(), GetMaxCompletionTokensFromConfig(), AskServiceFactory.CreateCallLogService(), GetDailyCallLimitFromConfig())
        {
        }

        // Falls back to the documented default (500) if the config key is
        // missing or gets mistyped during a config edit, rather than
        // crashing every single controller construction - since ASP.NET
        // builds a new AskController per request, an unguarded int.Parse
        // here would take down all of /api/ask on a bad config value.
        private static int GetMaxCompletionTokensFromConfig()
        {
            int value;
            return int.TryParse(ConfigurationManager.AppSettings["MaxCompletionTokens"], out value) ? value : 500;
        }

        // Same fallback reasoning as GetMaxCompletionTokensFromConfig above.
        private static int GetDailyCallLimitFromConfig()
        {
            int value;
            return int.TryParse(ConfigurationManager.AppSettings["DailyCallLimit"], out value) ? value : 200;
        }

        // This second constructor is the "seam" that makes the controller
        // unit-testable: a test can pass in fake IAskService/ICallLogService
        // objects and hardcoded config values instead of real config/network
        // dependencies, the same idea as AzureOpenAiEmbeddingService taking a
        // fake HttpClient. See AskControllerTests.cs for how it's used.
        public AskController(
            IReadOnlyDictionary<string, IAskService> pipelinesByMode,
            int maxCompletionTokens,
            ICallLogService callLogService,
            int dailyCallLimit)
        {
            _pipelinesByMode = pipelinesByMode;
            _maxCompletionTokens = maxCompletionTokens;
            _callLogService = callLogService;
            _dailyCallLimit = dailyCallLimit;
        }

        [HttpPost]
        [Route("")]
        public async Task<IHttpActionResult> Post(AskRequest request)
        {
            // Basic input check. Ok(...) here still returns HTTP 200 - the
            // Success flag inside AskResponse is what tells the browser
            // whether the answer worked, not the HTTP status code.
            if (request == null || string.IsNullOrWhiteSpace(request.Question))
            {
                return Ok(new AskResponse { Success = false, ErrorMessage = "Question is required." });
            }

            // TopN is validated here on the server no matter what the UI
            // spinner's min/max say client-side, because a direct POST to
            // this endpoint (e.g. via curl) can send anything - the browser
            // UI is not a security boundary.
            var topN = request.TopN ?? 5;
            if (topN < 1 || topN > 10)
            {
                return Ok(new AskResponse { Success = false, Mode = request.Mode, ErrorMessage = "TopN must be between 1 and 10." });
            }

            // Daily cap check, before either pipeline is ever touched. If
            // the count-check read itself fails, fail open - a broken
            // CallLog table shouldn't block real Q&A - and treat it as
            // under the limit.
            int todaysCallCount;
            try
            {
                todaysCallCount = await _callLogService.GetTodaysCallCountAsync();
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                todaysCallCount = 0;
            }

            if (todaysCallCount >= _dailyCallLimit)
            {
                return Ok(new AskResponse
                {
                    Success = false,
                    Mode = request.Mode,
                    ErrorMessage = "Daily call limit reached. Please try again tomorrow."
                });
            }

            // Look up which pipeline should handle this Mode. If the client
            // asks for a mode that isn't wired up, TryGetValue fails and we
            // return a clear message WITHOUT ever touching a real pipeline -
            // no wasted API calls, and nothing worth logging to CallLog.
            IAskService pipeline;
            if (!_pipelinesByMode.TryGetValue(request.Mode ?? string.Empty, out pipeline))
            {
                return Ok(new AskResponse
                {
                    Success = false,
                    Mode = request.Mode,
                    ErrorMessage = $"Mode '{request.Mode}' is not yet implemented."
                });
            }

            // AskServiceResult (Success/Answer only) is the pipeline's
            // internal shape. It gets mapped into AskResponse (the shape the
            // browser actually receives, adding Mode/ErrorMessage) below.
            // Keeping these as two separate types means the pipeline layer
            // doesn't need to know anything about HTTP.
            AskServiceResult result = null;
            Exception caughtException = null;
            try
            {
                result = await pipeline.AskAsync(request.Question, _maxCompletionTokens, topN);
            }
            catch (Exception ex)
            {
                // Anything from here down (network failure, Azure OpenAI
                // rejecting the request, a bad DB connection string, etc.)
                // lands here. We log the real exception server-side via
                // Trace so it's diagnosable, but we deliberately return a
                // generic message to the browser rather than ex.Message -
                // no need to expose internal details to the client.
                caughtException = ex;
                Trace.TraceError(ex.ToString());
            }

            // Every call attempt gets logged regardless of outcome, so
            // usage is auditable per pipeline and per day. CallLog is never
            // exposed to the browser, so the real exception detail (not the
            // generic client-facing message) is what gets stored.
            await LogCallSafeAsync(new CallLogEntry
            {
                CallTimestamp = DateTime.UtcNow,
                Mode = request.Mode,
                Question = request.Question,
                MaxCompletionTokens = _maxCompletionTokens,
                Success = caughtException == null,
                ErrorMessage = caughtException?.Message
            });

            if (caughtException != null)
            {
                return Ok(new AskResponse
                {
                    Success = false,
                    Mode = request.Mode,
                    ErrorMessage = "An error occurred while processing your question. Please try again."
                });
            }

            return Ok(new AskResponse
            {
                Success = result.Success,
                Answer = result.Answer,
                Mode = request.Mode
            });
        }

        // A CallLog write failure should never turn an already-completed
        // pipeline call into a user-facing error - the point of CallLog is
        // auditability, not gatekeeping a call that already happened (and
        // was already billed, if it succeeded).
        private async Task LogCallSafeAsync(CallLogEntry entry)
        {
            try
            {
                await _callLogService.LogCallAsync(entry);
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll" -TestCaseFilter:"FullyQualifiedName~AskControllerTests"
```

Expected: `Passed! - Failed: 0, Passed: 8` (the original 3 plus the 5 new ones — confirms the existing tests still pass unchanged with the new constructor shape).

- [ ] **Step 5: Commit**

```bash
git add "AdventureWorksProductAdvisor/Controllers/Api/AskController.cs" "AdventureWorksProductAdvisor.Tests/Controllers/Api/AskControllerTests.cs"
git commit -m "Add daily call-cap check and CallLog logging to AskController"
```

---

## Task 6: `Web.config` setting, end-to-end manual verification, rollout

**Files:**
- Modify: `AdventureWorksProductAdvisor/Web.config`

**Interfaces:**
- Consumes: `GetDailyCallLimitFromConfig()` (Task 5), reading `AppSettings["DailyCallLimit"]`.

- [ ] **Step 1: Add the `DailyCallLimit` setting**

In `AdventureWorksProductAdvisor/Web.config`, in `<appSettings>`, add it next to `MaxCompletionTokens`:

```xml
    <add key="MaxCompletionTokens" value="500"/>
    <add key="DailyCallLimit" value="200"/>
```

- [ ] **Step 2: Full solution build**

```bash
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Full test run**

```bash
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll"
```

Expected: `Passed! - Failed: 0` (every test across the whole project, not just `AskControllerTests`).

- [ ] **Step 4: Manual — ask a question in Stored Procedure mode through the running app**

Launch the app (IIS Express via F5, or per this session's established pattern: `& "C:\Program Files\IIS Express\iisexpress.exe" /path:"<repo>\AdventureWorksProductAdvisor" /port:<free port>`). In the browser, select "Stored Procedure" mode, ask a real question (e.g. "What lights are good for riding before sunrise?"), click Ask.

Expected: a real, review-grounded, `[StoredProc]`-labeled answer — same quality/shape as C# mode's answers, rendered with the same markdown formatting.

- [ ] **Step 5: Manual — confirm `CallLog` rows for both modes**

```sql
SELECT TOP 5 * FROM dbo.CallLog ORDER BY CallLogId DESC;
```

Expected: at least one row with `Mode = 'StoredProc'` from step 4, and (ask one more question in C# mode first if none exist yet) at least one row with `Mode = 'CSharp'`. Both rows have `Success = 1`, `ErrorMessage IS NULL`, `EstimatedInputTokens`/`EstimatedOutputTokens IS NULL`.

- [ ] **Step 6: Manual — confirm the daily cap actually blocks calls**

Temporarily set `DailyCallLimit` to `1` in `Web.config`, restart the app, ask one question (succeeds normally), ask a second question.

Expected: the second question returns `[<Mode>] Daily call limit reached. Please try again tomorrow.` without any noticeable delay (confirms no real Azure OpenAI call went out — a real call takes several seconds, per this session's own live testing earlier).

Revert `DailyCallLimit` back to `200` in `Web.config` afterward.

- [ ] **Step 7: Commit**

```bash
git add "AdventureWorksProductAdvisor/Web.config"
git commit -m "Add DailyCallLimit setting (200)"
```

---

## Self-Review Notes

- **Spec coverage:** `CallLog` table (Task 1), `@MaxTokens`/`@TopN` parameterization + real `USE MODEL` name (Task 1), `CallLogEntry`/`ICallLogService`/`CallLogService` (Task 2), `StoredProcAskService` (Task 3), `AskServiceFactory` wiring (Task 4), `AskController` cap check + logging with fail-open/swallow behavior (Task 5), `Web.config` setting (Task 6), all four spec test scenarios plus the fail-open case (Task 5), manual/live verification and the placeholder-substitution rollout process (Task 1 Step 3, Task 6) — all covered.
- **Type consistency:** `AskController`'s testable constructor is `(IReadOnlyDictionary<string, IAskService>, int, ICallLogService, int)` everywhere it's referenced (Task 5 production code and tests) and the real constructor's `AskServiceFactory.CreateCallLogService()`/`GetDailyCallLimitFromConfig()` calls match Task 4's/Task 5's own definitions. `CallLogEntry` field names match between `Models/CallLogEntry.cs` (Task 2) and every place it's constructed or asserted on (Task 5).
- **Non-goals respected:** no `EstimatedInputTokens`/`EstimatedOutputTokens` computation, no admin UI/endpoint, no unit tests for the two ADO.NET wrapper classes.
