# AdventureWorks Product Advisor — Design (Slice 2: Stored Procedure Pipeline)

This document supplements `AdventureWorks_Product_Advisor_Spec.md` (the project brief) and
`docs/superpowers/specs/2026-09-01-adventureworks-product-advisor-design.md` (the slice-1 design
doc). Slice 1 delivered the C# pipeline end to end and explicitly deferred three things to slice
2: `StoredProcAskService`, `dbo.CallLog` + daily-cap enforcement, and parameterizing
`dbo.AskProductQuestion`. This doc covers all three, built together as one slice.

Read the brief first for full context (purpose, goals, cost controls). This doc does not repeat
what's unchanged there.

## Decisions Resolved From This Round of Planning

1. **Scope**: the full original slice-2 bundle — stored-proc pipeline, `CallLog`, and the daily
   cap — rather than splitting the cap/logging into a separate later task. Rationale: the daily
   cap is explicitly called out in the brief as "the main protection against a bug or accidental
   loop running up real charges," and that protection matters most once a second pipeline exists
   and both are being actively exercised.
2. **Daily call limit**: `200`, the brief's own example value. At the brief's cost estimates
   (~$0.001–0.003/call), 200 calls/day stays well under $1 even at the cap.
3. **CallLog write failure**: swallowed. If writing the log row fails after a pipeline call has
   already succeeded and returned an answer, the user still gets that answer — the failure is
   `Trace.TraceError`'d (same pattern `AskController` already uses for pipeline exceptions) but
   never turned into a user-facing error. The point of `CallLog` is auditability, not gatekeeping
   an already-completed, already-billed call.
4. **Daily-cap count-check failure**: fails open. If the read that counts today's `CallLog` rows
   itself fails (e.g. a transient DB hiccup on `CallLog`, as opposed to the main data path), the
   request proceeds as if under the limit rather than blocking all Q&A because the safety-net
   table is temporarily unreachable. `Trace.TraceError`'d either way.

## Database Changes

### New table: `dbo.CallLog`

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

Checked into `Sql/CreateCallLogTable.sql`, matching the existing convention of
`Sql/AddReviewEmbeddingJsonColumn.sql` (a standalone script, run by hand against the real Azure
SQL database — nothing in this project applies schema changes automatically).

`EstimatedInputTokens`/`EstimatedOutputTokens` stay `NULL` on every row written in this slice —
neither pipeline computes them yet. The columns exist now per the brief's schema so a future slice
can start populating them without another migration.

### Modified: `dbo.AskProductQuestion`

Adds `@MaxTokens` and `@TopN` parameters (defaults match the C# pipeline's own defaults —
`MaxCompletionTokens` config default of 500, `TopN` UI default of 5), replaces the hardcoded `TOP
5` with `TOP (@TopN)`, and uses `@MaxTokens` in the JSON payload instead of the literal `500`.
Everything else in the procedure body (embedding, vector search shape, prompt construction,
`sp_invoke_external_rest_endpoint` call, response parsing) is unchanged from the current
`Sql/dbo.AskProductQuestion.sql`:

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

Note the `@url`/`@credential` above use the placeholder `your-openai-resource` hostname
consistent with what's already committed — this file, like `Web.config`, gets the real endpoint
substituted locally and never committed with the real value (see the earlier
`Web.AppSettings.config` externalization work).

The file gets re-saved as UTF-8 in this change (it's currently UTF-16LE, a side effect of being
generated via SSMS's "Generate Script As") — flagged in the slice-1 design doc as planned for
whenever this file was next touched. Expect a full-file diff in git even though the conceptual
change is a few lines.

## C# Changes

### New: `Models/CallLogEntry.cs`

```csharp
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
```

### New: `Services/ICallLogService.cs` / `CallLogService.cs`

```csharp
public interface ICallLogService
{
    Task<int> GetTodaysCallCountAsync();
    Task LogCallAsync(CallLogEntry entry);
}
```

Same ADO.NET style as `ReviewRepository` — plain `SqlConnection`/`SqlCommand`, connection string
passed into the constructor, no ORM. `GetTodaysCallCountAsync` counts rows where
`CallTimestamp >= <today's UTC midnight>`; `LogCallAsync` is a parameterized `INSERT`.

### New: `Services/StoredProcAskService.cs`

Implements `IAskService` (`Task<AskServiceResult> AskAsync(string question, int
maxCompletionTokens, int topN)`). Opens a `SqlConnection`, builds a `SqlCommand` with
`CommandType.StoredProcedure` targeting `dbo.AskProductQuestion`, sets `@Question`,
`@MaxTokens` (= `maxCompletionTokens`), `@TopN` (= `topN`) as input parameters and `@Answer`
(`NVARCHAR(MAX)`) as an output parameter, calls `ExecuteNonQueryAsync` (the procedure has no
result set — it only sets `@Answer` — so there's no reader involved), then reads the output
parameter's value into `AskServiceResult { Success = true, Answer = <value> }`.

No try/catch inside this class — SQL exceptions (bad connection, timeout, the procedure itself
erroring) propagate to `AskController`'s existing catch block, exactly like unhandled exceptions
from `CSharpAskService` do today.

### `AskServiceFactory.cs`

- Add `CreateCallLogService()`, following the same pattern as `CreateReviewRepository()` (same
  `AdventureWorksLT` connection string).
- Add a `CreateStoredProcAskService()` (or build it inline in `CreatePipelines()`, matching how
  `CSharpAskService` is built there today) using the same connection string.
- Add `["StoredProc"] = <the new service>` to the dictionary `CreatePipelines()` returns.

### `AskController.cs`

- Constructor gains an `ICallLogService`, following the existing two-constructor pattern (a
  parameterless one wired to the real factory-built service for ASP.NET, a testable one that
  takes it as a parameter).
- New appSetting `DailyCallLimit` read the same way `MaxCompletionTokens` is today (`TryParse`
  with a fallback — 200 — so a bad config value can't take down every request).
- In `Post`, after the existing Question/TopN validation and before resolving the pipeline: call
  `GetTodaysCallCountAsync()`; if the count is `>= DailyCallLimit`, return
  `AskResponse { Success = false, Mode = request.Mode, ErrorMessage = "Daily call limit reached. Please try again tomorrow." }`
  immediately — no pipeline call, no `CallLog` row (nothing happened worth logging).
- After the pipeline call (both the success path and the existing catch block), build a
  `CallLogEntry` from what's known (`Mode`, `Question`, `_maxCompletionTokens`,
  `EstimatedInputTokens`/`EstimatedOutputTokens` left `null`, `Success`) and call `LogCallAsync`,
  wrapped in its own try/catch that swallows any exception (`Trace.TraceError` only) so a logging
  failure never changes what gets returned to the browser. `CSharpAskService` today always returns
  `Success = true` when it returns at all (an unretrievable-context case like "no reviews found"
  is baked into `Answer` text, not a `Success:false` result) — so in practice `Success` is only
  ever `false` in `CallLog` on the exception path. There, `ErrorMessage` logged to `CallLog` is
  the real exception detail (`ex.Message`, same value already passed to `Trace.TraceError`) rather
  than the generic client-facing string — `CallLog` is never exposed to the browser, so there's no
  reason to throw away the more useful detail there. `ErrorMessage` is `null` whenever `Success`
  is `true`.

### `Web.config`

Add `<add key="DailyCallLimit" value="200"/>` to `<appSettings>`.

## Testing

Following the project's established split (see the slice-1 design doc's Testing section):

- **`StoredProcAskService` / `CallLogService`**: not unit tested — thin ADO.NET wrappers, same
  reasoning already applied to `ReviewRepository` ("unit testing it would require either a real
  database or heavy `SqlConnection` mocking for low value"). Exercised via manual testing against
  the real Azure SQL database.
- **`AskControllerTests`**: extended with the new cap/logging logic, mocking `ICallLogService`:
  - Count `>= DailyCallLimit` → returns the friendly error; the mocked `IAskService` for the
    selected mode is never invoked; `LogCallAsync` is never called.
  - Count `< DailyCallLimit` → pipeline is invoked as today; `LogCallAsync` is called once with
    the expected `Mode`/`Question`/`MaxCompletionTokens`/`Success`/`Answer`-derived fields.
  - Pipeline throws → the existing catch-block response is unchanged; `LogCallAsync` is still
    called, with `Success = false` and `ErrorMessage` set to the real exception detail (not the
    generic message returned to the browser).
  - `LogCallAsync` itself throws → the real answer/error response is still returned unchanged
    (verifies the swallow behavior).
- **Manual, live**: ask a question in Stored Procedure mode through the UI and confirm a real,
  review-grounded answer; confirm a `CallLog` row is written for calls in both modes; temporarily
  lower `DailyCallLimit` to something small (e.g. `1`) to confirm the cap message appears on the
  next call without an actual Azure OpenAI request going out.

## Rollout

Both `Sql/CreateCallLogTable.sql` and the modified `Sql/dbo.AskProductQuestion.sql` need to be run
by hand against the real Azure SQL database — same manual-migration approach already used for the
`ReviewEmbeddingJson` column in slice 1. The implementation plan will spell out the exact steps
(run script X, verify table/procedure exists, then build/test).

Before running the modified `dbo.AskProductQuestion.sql`, substitute the placeholder
`your-openai-resource` in `@url`/`@credential` with the real endpoint, same as every other
placeholder in this repo — never commit the real value. This is a one-time step only if the
target Azure OpenAI resource itself is changing; slice 2 doesn't change which resource is used, so
if it's the same resource already backing the current deployment, the existing scoped credential
(matched by URL prefix) keeps working unchanged and no new credential needs to be created. Also
confirm a `CREATE EXTERNAL MODEL` object named `brianminnich_embedding_model` exists in the target
database (create or rename it to match if it currently has a different name) before running the
script, since `USE MODEL` fails if no object by that exact name exists.

## Non-Goals (this slice)

- Computing `EstimatedInputTokens`/`EstimatedOutputTokens` for either pipeline — columns exist,
  stay `null`.
- Any UI for viewing `CallLog` (an admin dashboard, a "calls remaining today" indicator, etc.) —
  not requested, not part of the original brief's scope for this slice.
- Resetting or overriding the daily cap outside of editing `Web.config` — no admin endpoint.
