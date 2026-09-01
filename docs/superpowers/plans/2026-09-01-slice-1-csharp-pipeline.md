# Slice 1: C# RAG Pipeline Walking Skeleton — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the C# RAG pipeline end to end — embed reviews, embed a question, rank by cosine similarity, build a prompt, call Azure OpenAI, and return an answer through a browser chat widget — so a real question typed on the page gets a real, review-grounded answer.

**Architecture:** Single ASP.NET MVC5 + Web API 2 project (.NET Framework 4.8) plus one MSTest v2 unit test project. `AskController` resolves an `IAskService` from a mode-keyed dictionary built by a small static composition root (`AskServiceFactory`); `CSharpAskService` is the only pipeline wired up this slice. All Azure OpenAI calls authenticate via `DefaultAzureCredential` (managed identity pattern) — no API keys anywhere in this codebase.

**Tech Stack:** C#, ASP.NET MVC5, ASP.NET Web API 2, .NET Framework 4.8, `System.Data.SqlClient` (ADO.NET), `Azure.Identity`/`Azure.Core`, Newtonsoft.Json, vanilla JavaScript (`fetch`), MSTest v2, Moq.

**Spec:** `docs/superpowers/specs/2026-09-01-adventureworks-product-advisor-design.md` (supplements `AdventureWorks_Product_Advisor_Spec.md`, the original project brief).

## Global Constraints

- Target framework is exactly **.NET Framework 4.8** for both projects.
- No authentication/membership scaffolding (non-goal — "No Authentication" in the project template).
- `MaxCompletionTokens` is read server-side only (`Web.config`), never accepted from the client.
- `TopN` must be validated server-side to `[1, 10]` regardless of the UI spinner's `min`/`max`, since a direct POST to the API bypasses HTML attributes.
- No secrets are committed. `Web.ConnectionStrings.config` is gitignored; `Web.ConnectionStrings.config.example` is checked in as a template.
- All Azure OpenAI calls use `DefaultAzureCredential` (`Azure.Identity`) with scope `https://cognitiveservices.azure.com/.default` — no `api-key` header, no key anywhere in config.
- `IEmbeddingService`/`IChatCompletionService` never leak Azure-specific shapes across their method signatures — only `string`/`float[]` in and out. All Azure REST details stay inside the two `AzureOpenAi*` implementation classes.
- This slice implements the **C# pipeline only**. `StoredProcAskService`, `dbo.CallLog`, and the daily call cap are out of scope — `AskController` returns a "not yet implemented" `AskResponse` for any mode other than `"CSharp"`.
- Azure OpenAI resource: endpoint `https://your-openai-resource.openai.azure.com`, chat deployment `gpt-5.4-mini`, embedding deployment `text-embedding-3-small`, api-version `2024-10-21` for both.
- `dbo.ProductReview` columns (confirmed schema): `ReviewID int`, `ProductID int`, `Rating tinyint`, `ReviewTitle nvarchar(400)`, `ReviewText nvarchar(max)`, `ReviewDate date`, `ReviewVector vector` (untouched by this app).
- **Both `.csproj` files are hand-authored, non-SDK-style projects with explicit `<ItemGroup>` item lists — there is no auto-globbing of `.cs`/`.cshtml` files.** Every task that adds a new file MUST also add a matching `<Compile Include="..." />` (or `<Content Include="..." />` for non-code files) to the relevant `.csproj`, or the file will not be part of the build at all and the task's own build-verification step will silently pass without the new code compiled in. Each task below spells out the exact XML to add.

## Build & Test Commands (reference — full paths used in every task)

- Build: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
- Run tests (all): `& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll"`
- Run tests (filtered): append `-TestCaseFilter:"FullyQualifiedName~<ClassName>"` to the above.

---

## Task 1: Scaffold the Solution — COMPLETE (commit 6935190)

**This task is done. The rest of this section is a record of what actually happened, kept for reference — do not redo it.**

VS Community 2026's "Create a new project" dialog no longer surfaces the classic ASP.NET Web Application (.NET Framework) template as a discoverable entry (confirmed by inspecting the installed template manifests directly: the template package `Microsoft.WAP.CSharp.ASPNET.WebCore.FullFramework` is present and the required `Microsoft.VisualStudio.Web.Mvc`/`.Common` components are installed, but the template is marked `Hidden` and the modern "ASP.NET Core Web App" wizard's Framework dropdown only ever offered ".NET 10.0 (Long Term Support)" — no Framework 4.8 option, even after a full template-cache rebuild via `devenv /updateconfiguration`). This is a tooling gap in this VS build, not a misconfiguration.

**Resolution:** the project files were hand-authored directly instead of generated by a wizard:

- `AdventureWorksProductAdvisor.sln` and `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj` — a classic non-SDK-style csproj (`<Project ToolsVersion="15.0">`, not `<Project Sdk="...">`), importing `Microsoft.WebApplication.targets`, with `ProjectTypeGuids` `{349c5851-65df-11da-9384-00065b846f21};{fae04ec0-301f-11d3-bf4b-00c04f79efbc}` (Web Application Project + C#), and `<VisualStudioVersion>18.0</VisualStudioVersion>` set explicitly (this VS install only has a `v18.0` MSBuild extensions folder — the old default of `10.0` would fail to resolve `Microsoft.WebApplication.targets` at all).
- **NuGet dependencies use `<PackageReference>`, not `packages.config`.** This was an empirically-verified decision, not a preference: `packages.config`-style restore (`-p:RestorePackagesConfig=true`) does NOT auto-resolve transitive dependencies — every indirect package would need to be hand-enumerated (verified: restoring `Azure.Identity` alone via packages.config downloaded exactly 1 package, not its ~25 transitive dependencies). `PackageReference` in the SAME non-SDK csproj format resolves the full transitive graph automatically and copies everything to `bin\` — verified working for both a simple package (`Newtonsoft.Json`) and a deep one (`Azure.Identity`, 25 transitive packages). Main project's `PackageReference` items: `Microsoft.AspNet.Mvc` 5.2.9, `Microsoft.AspNet.WebApi` 5.2.9, `Newtonsoft.Json` 13.0.3, `Azure.Identity` 1.13.1.
- `AutoGenerateBindingRedirects`/`GenerateBindingRedirectsOutputType` are set `true`, **but** for a web project (OutputType `Library`, hosted by IIS/ASP.NET) the auto-generated redirects land in a side-car `AdventureWorksProductAdvisor.dll.config` that ASP.NET never reads — only `Web.config` is honored at runtime. The redirects were generated once (by building and inspecting the `.dll.config`), then copied by hand into `Web.config`'s `<runtime><assemblyBinding>` section. If a future task adds a new main-project `PackageReference`, repeat this: build, open `bin\AdventureWorksProductAdvisor.dll.config`, copy any new `<dependentAssembly>` blocks into `Web.config`.
- Global.asax/.cs, `App_Start\{RouteConfig,WebApiConfig,FilterConfig}.cs`, `Controllers\HomeController.cs`, `Properties\AssemblyInfo.cs`, `Views\{_ViewStart.cshtml, Web.config, Shared\_Layout.cshtml, Home\Index.cshtml}` were written directly (standard MVC5 boilerplate — no bundling/`Microsoft.AspNet.Web.Optimization` since there's no Bootstrap/jQuery in this project; `Views\Home\Index.cshtml` currently holds a minimal placeholder page, fully replaced by Task 9).
- `.gitignore`, `Web.ConnectionStrings.config` (gitignored) + `.example`, and `Web.config` `appSettings` (`MaxCompletionTokens`, `AzureOpenAiEndpoint`, `AzureOpenAiApiVersion`, `AzureOpenAiEmbeddingDeployment`, `AzureOpenAiChatDeployment`) were created as originally planned.
- `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj` — same non-SDK-style approach, plain class library (no WAP targets needed), `PackageReference` items: `MSTest.TestFramework` 3.6.4, `MSTest.TestAdapter` 3.6.4, `Moq` 4.20.72, `Azure.Core` 1.44.1, `Newtonsoft.Json` 13.0.3, plus a `<ProjectReference>` to the main project.
- Verified end-to-end via CLI: `MSBuild.exe ... -t:Restore,Build` (0 errors, 0 warnings on both projects) and `vstest.console.exe` successfully discovered and ran a temporary smoke test (removed once confirmed working).

**Consequence for every later task:** since both `.csproj` files use explicit `<ItemGroup>` item lists (no SDK-style auto-globbing), **every new `.cs`/`.cshtml` file added in Tasks 2–9 must also be registered in the relevant `.csproj`** via a `<Compile Include="..." />` or `<Content Include="..." />` element, or it silently won't be compiled in. Each remaining task below now includes this as an explicit step.

<details>
<summary>Original Task 1 text (superseded by the above — kept for historical context only)</summary>

This task cannot be automated — classic ASP.NET MVC5 project creation has no CLI equivalent. Perform these steps yourself in Visual Studio Community 2026, then verify the build from the command line before moving on.

**Files:**
- Create: `AdventureWorksProductAdvisor.sln`
- Create: `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj` (and template-generated contents)
- Create: `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj`
- Create: `.gitignore`
- Create: `AdventureWorksProductAdvisor\Web.ConnectionStrings.config` (gitignored, not committed)
- Create: `AdventureWorksProductAdvisor\Web.ConnectionStrings.config.example`
- Modify: `AdventureWorksProductAdvisor\Web.config`
- Modify: `AdventureWorksProductAdvisor\Controllers\HomeController.cs`
- Delete: `AdventureWorksProductAdvisor\Views\Home\About.cshtml`, `AdventureWorksProductAdvisor\Views\Home\Contact.cshtml`

**Interfaces:**
- Produces: a buildable solution with two projects, all NuGet packages installed, ready for Task 2 onward to add code.

- [ ] **Step 1: Create the main web project**

  In Visual Studio: File → New → Project → **ASP.NET Web Application (.NET Framework)**.
  - Project name: `AdventureWorksProductAdvisor`
  - Solution name: `AdventureWorksProductAdvisor`
  - **Location: `C:\Git`** (the *parent* of the existing repo folder — not `C:\Git\AdventureWorksProductAdvisor` itself). This matters: with "Place solution and project in the same directory" checked, VS creates one folder named after the project directly under Location. Pointing Location at `C:\Git` makes VS create/reuse `C:\Git\AdventureWorksProductAdvisor` (the existing repo folder, which already has `.git` and the spec file) rather than nesting a second copy inside it.
  - Check **"Place solution and project in the same directory."**
  - Target Framework: **.NET Framework 4.8**.
  - Next screen: template **MVC**, check both **MVC** and **Web API** boxes, Authentication type **No Authentication**.
  - Create.

- [ ] **Step 2: Verify no double-nesting occurred**

  Run: `Get-ChildItem "C:\Git\AdventureWorksProductAdvisor" -Name`
  Expected: `AdventureWorksProductAdvisor.sln`, `AdventureWorksProductAdvisor.csproj`, `Controllers`, `Views`, `Web.config`, etc. all directly inside `C:\Git\AdventureWorksProductAdvisor`, alongside the pre-existing `AdventureWorks_Product_Advisor_Spec.md`, `docs\`, and `sql\`. If you instead see a nested `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor\...`, move everything up one level and delete the empty inner folder before continuing.

- [ ] **Step 3: Add the test project to the same solution**

  In Solution Explorer: right-click the solution → Add → New Project → **Unit Test Project (.NET Framework)**.
  - Name: `AdventureWorksProductAdvisor.Tests`
  - Target Framework: **.NET Framework 4.8**
  - This creates its own subfolder under the solution directory — that's expected and fine.

- [ ] **Step 4: Add a project reference from Tests to the main project**

  Right-click `AdventureWorksProductAdvisor.Tests` → Add → Reference → check `AdventureWorksProductAdvisor` → OK.

- [ ] **Step 5: Install NuGet packages**

  Main project (`AdventureWorksProductAdvisor`) — install latest stable:
  - `Azure.Identity`

  Test project (`AdventureWorksProductAdvisor.Tests`) — install latest stable:
  - `Moq`
  - `Azure.Core`
  - `Newtonsoft.Json`

- [ ] **Step 6: Remove unused template pages**

  Delete `Views\Home\About.cshtml` and `Views\Home\Contact.cshtml`. In `Controllers\HomeController.cs`, delete the `About()` and `Contact()` action methods, leaving only `Index()`. In `Views\Shared\_Layout.cshtml`, remove the "About" and "Contact" `<li>` nav links (keep "Home").

- [ ] **Step 7: Create `.gitignore`**

  Create `C:\Git\AdventureWorksProductAdvisor\.gitignore`:

  ```
  bin/
  obj/
  .vs/
  *.user
  packages/
  AdventureWorksProductAdvisor/Web.ConnectionStrings.config
  ```

- [ ] **Step 8: Split the connection string into a gitignored file**

  In `Web.config`, find the `<connectionStrings>` element (likely empty or absent) and replace it with:

  ```xml
  <connectionStrings configSource="Web.ConnectionStrings.config" />
  ```

  Create `AdventureWorksProductAdvisor\Web.ConnectionStrings.config` (this file is gitignored — it holds your real, local secret):

  ```xml
  <connectionStrings>
    <add name="AdventureWorksLT" connectionString="Server=tcp:YOUR_SERVER.database.windows.net,1433;Database=YOUR_DATABASE;User ID=YOUR_USER;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;" providerName="System.Data.SqlClient" />
  </connectionStrings>
  ```

  Fill in your actual Azure SQL AdventureWorksLT server/database/credentials in this file.

  Create `AdventureWorksProductAdvisor\Web.ConnectionStrings.config.example` (this one IS committed, with obvious placeholders) with the same content as above.

- [ ] **Step 9: Add app settings to `Web.config`**

  In `Web.config`'s `<appSettings>` element, add:

  ```xml
  <add key="MaxCompletionTokens" value="500" />
  <add key="AzureOpenAiEndpoint" value="https://your-openai-resource.openai.azure.com" />
  <add key="AzureOpenAiApiVersion" value="2024-10-21" />
  <add key="AzureOpenAiEmbeddingDeployment" value="text-embedding-3-small" />
  <add key="AzureOpenAiChatDeployment" value="gpt-5.4-mini" />
  ```

  (These are not secrets — access is controlled by Azure RBAC on the resource, not by hiding the endpoint URL or deployment names.)

- [ ] **Step 10: Build from the command line to verify the scaffold compiles**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (warnings from template-generated code are acceptable; there must be 0 errors).

- [ ] **Step 11: Commit**

  ```bash
  git add -A
  git commit -m "Scaffold ASP.NET MVC5 + Web API 2 solution with test project"
  ```

  Verify `git status` shows `Web.ConnectionStrings.config` is NOT tracked (only `.example` should be), and that `bin/`, `obj/`, `packages/` are not tracked.

</details>

---

## Task 2: Data Layer — Reviews and Embeddings

**Files:**
- Create: `sql/AddReviewEmbeddingJsonColumn.sql`
- Create: `AdventureWorksProductAdvisor\Data\ReviewRecord.cs`
- Create: `AdventureWorksProductAdvisor\Data\IReviewRepository.cs`
- Create: `AdventureWorksProductAdvisor\Data\ReviewRepository.cs`
- Modify: `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj` (register the three new `.cs` files — see Step 5)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `AdventureWorksProductAdvisor.Data.ReviewRecord` — properties `int ReviewId`, `int ProductId`, `string ProductName`, `byte Rating`, `string ReviewTitle`, `string ReviewText`, `float[] Embedding` (nullable).
  - `AdventureWorksProductAdvisor.Data.IReviewRepository` — `Task<List<ReviewRecord>> GetAllReviewsAsync()`, `Task SaveEmbeddingAsync(int reviewId, float[] embedding)`.
  - `AdventureWorksProductAdvisor.Data.ReviewRepository : IReviewRepository`, constructor `ReviewRepository(string connectionString)`.

Not unit tested per the design doc (thin ADO.NET wrapper — exercised for real in Task 10's end-to-end verification against the live Azure SQL database).

- [ ] **Step 1: Write the SQL migration script**

  Create `sql/AddReviewEmbeddingJsonColumn.sql`:

  ```sql
  ALTER TABLE dbo.ProductReview
  ADD ReviewEmbeddingJson NVARCHAR(MAX) NULL;
  ```

  Run this script against your Azure SQL AdventureWorksLT database (e.g. via SSMS) before Task 10.

- [ ] **Step 2: Write `ReviewRecord`**

  Create `AdventureWorksProductAdvisor\Data\ReviewRecord.cs`:

  ```csharp
  namespace AdventureWorksProductAdvisor.Data
  {
      public class ReviewRecord
      {
          public int ReviewId { get; set; }
          public int ProductId { get; set; }
          public string ProductName { get; set; }
          public byte Rating { get; set; }
          public string ReviewTitle { get; set; }
          public string ReviewText { get; set; }
          public float[] Embedding { get; set; }
      }
  }
  ```

- [ ] **Step 3: Write `IReviewRepository`**

  Create `AdventureWorksProductAdvisor\Data\IReviewRepository.cs`:

  ```csharp
  using System.Collections.Generic;
  using System.Threading.Tasks;

  namespace AdventureWorksProductAdvisor.Data
  {
      public interface IReviewRepository
      {
          Task<List<ReviewRecord>> GetAllReviewsAsync();
          Task SaveEmbeddingAsync(int reviewId, float[] embedding);
      }
  }
  ```

- [ ] **Step 4: Write `ReviewRepository`**

  Create `AdventureWorksProductAdvisor\Data\ReviewRepository.cs`:

  ```csharp
  using System.Collections.Generic;
  using System.Data.SqlClient;
  using System.Threading.Tasks;
  using Newtonsoft.Json;

  namespace AdventureWorksProductAdvisor.Data
  {
      public class ReviewRepository : IReviewRepository
      {
          private readonly string _connectionString;

          public ReviewRepository(string connectionString)
          {
              _connectionString = connectionString;
          }

          public async Task<List<ReviewRecord>> GetAllReviewsAsync()
          {
              const string sql = @"
                  SELECT r.ReviewID, r.ProductID, p.Name AS ProductName, r.Rating, r.ReviewTitle, r.ReviewText, r.ReviewEmbeddingJson
                  FROM dbo.ProductReview r
                  INNER JOIN SalesLT.Product p ON r.ProductID = p.ProductID";

              var results = new List<ReviewRecord>();
              using (var connection = new SqlConnection(_connectionString))
              using (var command = new SqlCommand(sql, connection))
              {
                  await connection.OpenAsync();
                  using (var reader = await command.ExecuteReaderAsync())
                  {
                      while (await reader.ReadAsync())
                      {
                          var embeddingJson = reader["ReviewEmbeddingJson"] as string;
                          results.Add(new ReviewRecord
                          {
                              ReviewId = (int)reader["ReviewID"],
                              ProductId = (int)reader["ProductID"],
                              ProductName = (string)reader["ProductName"],
                              Rating = (byte)reader["Rating"],
                              ReviewTitle = (string)reader["ReviewTitle"],
                              ReviewText = (string)reader["ReviewText"],
                              Embedding = string.IsNullOrEmpty(embeddingJson)
                                  ? null
                                  : JsonConvert.DeserializeObject<float[]>(embeddingJson)
                          });
                      }
                  }
              }
              return results;
          }

          public async Task SaveEmbeddingAsync(int reviewId, float[] embedding)
          {
              const string sql = "UPDATE dbo.ProductReview SET ReviewEmbeddingJson = @Json WHERE ReviewID = @ReviewId";
              var json = JsonConvert.SerializeObject(embedding);
              using (var connection = new SqlConnection(_connectionString))
              using (var command = new SqlCommand(sql, connection))
              {
                  command.Parameters.AddWithValue("@Json", json);
                  command.Parameters.AddWithValue("@ReviewId", reviewId);
                  await connection.OpenAsync();
                  await command.ExecuteNonQueryAsync();
              }
          }
      }
  }
  ```

- [ ] **Step 5: Register the new files in the csproj**

  This project is a non-SDK-style csproj with explicit item lists — new `.cs` files are NOT picked up automatically. Open `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj` and add to the existing `<ItemGroup>` that contains `<Compile Include="Controllers\HomeController.cs" />`:

  ```xml
    <Compile Include="Data\ReviewRecord.cs" />
    <Compile Include="Data\IReviewRepository.cs" />
    <Compile Include="Data\ReviewRepository.cs" />
  ```

- [ ] **Step 6: Build to verify it compiles**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 7: Commit**

  ```bash
  git add sql/AddReviewEmbeddingJsonColumn.sql AdventureWorksProductAdvisor/Data AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj
  git commit -m "Add ReviewRepository and the ReviewEmbeddingJson migration script"
  ```

---

## Task 3: Embedding Service (with shared test helpers)

**Files:**
- Create: `AdventureWorksProductAdvisor\Services\IEmbeddingService.cs`
- Create: `AdventureWorksProductAdvisor\Services\AzureOpenAiEmbeddingService.cs`
- Create: `AdventureWorksProductAdvisor.Tests\TestHelpers\FakeHttpMessageHandler.cs`
- Create: `AdventureWorksProductAdvisor.Tests\TestHelpers\FakeTokenCredential.cs`
- Test: `AdventureWorksProductAdvisor.Tests\Services\AzureOpenAiEmbeddingServiceTests.cs`
- Modify: `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`, `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj` (register the new files — see the steps below)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `AdventureWorksProductAdvisor.Services.IEmbeddingService` — `Task<float[]> GetEmbeddingAsync(string text)`.
  - `AdventureWorksProductAdvisor.Services.AzureOpenAiEmbeddingService : IEmbeddingService`, constructor `AzureOpenAiEmbeddingService(HttpClient httpClient, TokenCredential credential, string endpoint, string deploymentName, string apiVersion)`.
  - `AdventureWorksProductAdvisor.Tests.TestHelpers.FakeHttpMessageHandler` — constructor `FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)`, properties `HttpRequestMessage LastRequest`, `string LastRequestBody`. Reused by Task 4.
  - `AdventureWorksProductAdvisor.Tests.TestHelpers.FakeTokenCredential : TokenCredential` — constructor `FakeTokenCredential(string token = "fake-token")`. Reused by Task 4.

- [ ] **Step 1: Write `IEmbeddingService`**

  Create `AdventureWorksProductAdvisor\Services\IEmbeddingService.cs`:

  ```csharp
  using System.Threading.Tasks;

  namespace AdventureWorksProductAdvisor.Services
  {
      public interface IEmbeddingService
      {
          Task<float[]> GetEmbeddingAsync(string text);
      }
  }
  ```

- [ ] **Step 2: Write the test helpers**

  Create `AdventureWorksProductAdvisor.Tests\TestHelpers\FakeHttpMessageHandler.cs`:

  ```csharp
  using System;
  using System.Net.Http;
  using System.Threading;
  using System.Threading.Tasks;

  namespace AdventureWorksProductAdvisor.Tests.TestHelpers
  {
      public class FakeHttpMessageHandler : HttpMessageHandler
      {
          private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

          public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
          {
              _responder = responder;
          }

          public HttpRequestMessage LastRequest { get; private set; }
          public string LastRequestBody { get; private set; }

          protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
          {
              LastRequest = request;
              LastRequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync();
              return _responder(request);
          }
      }
  }
  ```

  Create `AdventureWorksProductAdvisor.Tests\TestHelpers\FakeTokenCredential.cs`:

  ```csharp
  using System;
  using System.Threading;
  using System.Threading.Tasks;
  using Azure.Core;

  namespace AdventureWorksProductAdvisor.Tests.TestHelpers
  {
      public class FakeTokenCredential : TokenCredential
      {
          private readonly string _token;

          public FakeTokenCredential(string token = "fake-token")
          {
              _token = token;
          }

          public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
          {
              return new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1));
          }

          public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
          {
              return new ValueTask<AccessToken>(GetToken(requestContext, cancellationToken));
          }
      }
  }
  ```

- [ ] **Step 3: Write the failing test**

  Create `AdventureWorksProductAdvisor.Tests\Services\AzureOpenAiEmbeddingServiceTests.cs`:

  ```csharp
  using System.Net;
  using System.Net.Http;
  using System.Text;
  using System.Threading.Tasks;
  using AdventureWorksProductAdvisor.Services;
  using AdventureWorksProductAdvisor.Tests.TestHelpers;
  using Microsoft.VisualStudio.TestTools.UnitTesting;

  namespace AdventureWorksProductAdvisor.Tests.Services
  {
      [TestClass]
      public class AzureOpenAiEmbeddingServiceTests
      {
          [TestMethod]
          public async Task GetEmbeddingAsync_SendsBearerTokenAndCorrectUrl_ReturnsParsedEmbedding()
          {
              var canned = "{\"data\":[{\"embedding\":[0.5,0.25,0.125]}]}";
              var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StringContent(canned, Encoding.UTF8, "application/json")
              });
              var httpClient = new HttpClient(handler);
              var credential = new FakeTokenCredential("test-token");
              var service = new AzureOpenAiEmbeddingService(
                  httpClient, credential, "https://example.openai.azure.com", "text-embedding-3-small", "2024-10-21");

              var result = await service.GetEmbeddingAsync("great product");

              CollectionAssert.AreEqual(new float[] { 0.5f, 0.25f, 0.125f }, result);
              Assert.AreEqual(
                  "https://example.openai.azure.com/openai/deployments/text-embedding-3-small/embeddings?api-version=2024-10-21",
                  handler.LastRequest.RequestUri.ToString());
              Assert.AreEqual("Bearer", handler.LastRequest.Headers.Authorization.Scheme);
              Assert.AreEqual("test-token", handler.LastRequest.Headers.Authorization.Parameter);
          }
      }
  }
  ```

  Register the new files (both csproj files are explicit-item-list, non-SDK style — nothing is auto-included). In `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`, add to the `<ItemGroup>` containing `<Compile Include="Data\ReviewRecord.cs" />`:

  ```xml
    <Compile Include="Services\IEmbeddingService.cs" />
    <Compile Include="Services\AzureOpenAiEmbeddingService.cs" />
  ```

  In `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj`, add to the `<ItemGroup>` containing `<Compile Include="Properties\AssemblyInfo.cs" />`:

  ```xml
    <Compile Include="TestHelpers\FakeHttpMessageHandler.cs" />
    <Compile Include="TestHelpers\FakeTokenCredential.cs" />
    <Compile Include="Services\AzureOpenAiEmbeddingServiceTests.cs" />
  ```

  (`AzureOpenAiEmbeddingService.cs` doesn't exist yet — that's fine, the next step's build is expected to fail.)

- [ ] **Step 4: Run the build to confirm it fails to compile**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: FAIL — a compilation error (either a missing-source-file error for `AzureOpenAiEmbeddingService.cs`, or a missing-type error if the item was added differently — either way, confirms the red state).

- [ ] **Step 5: Write the implementation**

  Create `AdventureWorksProductAdvisor\Services\AzureOpenAiEmbeddingService.cs`:

  ```csharp
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using Azure.Core;
  using Newtonsoft.Json.Linq;

  namespace AdventureWorksProductAdvisor.Services
  {
      public class AzureOpenAiEmbeddingService : IEmbeddingService
      {
          private readonly HttpClient _httpClient;
          private readonly TokenCredential _credential;
          private readonly string _endpoint;
          private readonly string _deploymentName;
          private readonly string _apiVersion;

          public AzureOpenAiEmbeddingService(
              HttpClient httpClient, TokenCredential credential, string endpoint, string deploymentName, string apiVersion)
          {
              _httpClient = httpClient;
              _credential = credential;
              _endpoint = endpoint;
              _deploymentName = deploymentName;
              _apiVersion = apiVersion;
          }

          public async Task<float[]> GetEmbeddingAsync(string text)
          {
              var token = await _credential.GetTokenAsync(
                  new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }),
                  CancellationToken.None);

              var url = $"{_endpoint}/openai/deployments/{_deploymentName}/embeddings?api-version={_apiVersion}";
              var requestJson = new JObject { ["input"] = text }.ToString();

              using (var request = new HttpRequestMessage(HttpMethod.Post, url))
              {
                  request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                  request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                  var response = await _httpClient.SendAsync(request);
                  response.EnsureSuccessStatusCode();

                  var responseJson = await response.Content.ReadAsStringAsync();
                  var parsed = JObject.Parse(responseJson);
                  return parsed["data"][0]["embedding"].ToObject<float[]>();
              }
          }
      }
  }
  ```

- [ ] **Step 6: Run the build and tests to confirm they pass**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: `Build succeeded. 0 Error(s)`.

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll" -TestCaseFilter:"FullyQualifiedName~AzureOpenAiEmbeddingServiceTests"`
  Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 7: Commit**

  ```bash
  git add AdventureWorksProductAdvisor/Services/IEmbeddingService.cs AdventureWorksProductAdvisor/Services/AzureOpenAiEmbeddingService.cs AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj AdventureWorksProductAdvisor.Tests/TestHelpers AdventureWorksProductAdvisor.Tests/Services/AzureOpenAiEmbeddingServiceTests.cs AdventureWorksProductAdvisor.Tests/AdventureWorksProductAdvisor.Tests.csproj
  git commit -m "Add AzureOpenAiEmbeddingService with managed-identity auth"
  ```

---

## Task 4: Chat Completion Service

**Files:**
- Create: `AdventureWorksProductAdvisor\Services\IChatCompletionService.cs`
- Create: `AdventureWorksProductAdvisor\Services\AzureOpenAiChatCompletionService.cs`
- Test: `AdventureWorksProductAdvisor.Tests\Services\AzureOpenAiChatCompletionServiceTests.cs`
- Modify: `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`, `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj` (register the new files — see the steps below)

**Interfaces:**
- Consumes: `AdventureWorksProductAdvisor.Tests.TestHelpers.FakeHttpMessageHandler`, `AdventureWorksProductAdvisor.Tests.TestHelpers.FakeTokenCredential` (from Task 3).
- Produces:
  - `AdventureWorksProductAdvisor.Services.IChatCompletionService` — `Task<string> GetCompletionAsync(string prompt, int maxCompletionTokens)`.
  - `AdventureWorksProductAdvisor.Services.AzureOpenAiChatCompletionService : IChatCompletionService`, constructor `AzureOpenAiChatCompletionService(HttpClient httpClient, TokenCredential credential, string endpoint, string deploymentName, string apiVersion)`.

- [ ] **Step 1: Write `IChatCompletionService`**

  Create `AdventureWorksProductAdvisor\Services\IChatCompletionService.cs`:

  ```csharp
  using System.Threading.Tasks;

  namespace AdventureWorksProductAdvisor.Services
  {
      public interface IChatCompletionService
      {
          Task<string> GetCompletionAsync(string prompt, int maxCompletionTokens);
      }
  }
  ```

- [ ] **Step 2: Write the failing test**

  Create `AdventureWorksProductAdvisor.Tests\Services\AzureOpenAiChatCompletionServiceTests.cs`:

  ```csharp
  using System.Net;
  using System.Net.Http;
  using System.Text;
  using System.Threading.Tasks;
  using AdventureWorksProductAdvisor.Services;
  using AdventureWorksProductAdvisor.Tests.TestHelpers;
  using Microsoft.VisualStudio.TestTools.UnitTesting;
  using Newtonsoft.Json.Linq;

  namespace AdventureWorksProductAdvisor.Tests.Services
  {
      [TestClass]
      public class AzureOpenAiChatCompletionServiceTests
      {
          [TestMethod]
          public async Task GetCompletionAsync_SendsSystemAndUserMessages_ReturnsParsedContent()
          {
              var canned = "{\"choices\":[{\"message\":{\"content\":\"Test answer\"}}]}";
              var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
              {
                  Content = new StringContent(canned, Encoding.UTF8, "application/json")
              });
              var httpClient = new HttpClient(handler);
              var credential = new FakeTokenCredential("test-token");
              var service = new AzureOpenAiChatCompletionService(
                  httpClient, credential, "https://example.openai.azure.com", "gpt-5.4-mini", "2024-10-21");

              var result = await service.GetCompletionAsync(
                  "Product reviews: ...\n\nCustomer question: What is the best bike?", 500);

              Assert.AreEqual("Test answer", result);
              Assert.AreEqual(
                  "https://example.openai.azure.com/openai/deployments/gpt-5.4-mini/chat/completions?api-version=2024-10-21",
                  handler.LastRequest.RequestUri.ToString());
              Assert.AreEqual("Bearer", handler.LastRequest.Headers.Authorization.Scheme);
              Assert.AreEqual("test-token", handler.LastRequest.Headers.Authorization.Parameter);

              var sentPayload = JObject.Parse(handler.LastRequestBody);
              var messages = (JArray)sentPayload["messages"];
              Assert.AreEqual(2, messages.Count);
              Assert.AreEqual("system", messages[0]["role"].ToString());
              Assert.AreEqual("user", messages[1]["role"].ToString());
              StringAssert.Contains(messages[1]["content"].ToString(), "What is the best bike?");
              Assert.AreEqual(500, sentPayload["max_completion_tokens"].Value<int>());
          }
      }
  }
  ```

  Register the new files. In `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`'s `Compile` `<ItemGroup>`, add:

  ```xml
    <Compile Include="Services\IChatCompletionService.cs" />
    <Compile Include="Services\AzureOpenAiChatCompletionService.cs" />
  ```

  In `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj`'s `Compile` `<ItemGroup>`, add:

  ```xml
    <Compile Include="Services\AzureOpenAiChatCompletionServiceTests.cs" />
  ```

  (`AzureOpenAiChatCompletionService.cs` doesn't exist yet — expected, the next step's build should fail.)

- [ ] **Step 3: Run the build to confirm it fails to compile**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: FAIL — a compilation error (missing source file or missing type) confirming the red state.

- [ ] **Step 4: Write the implementation**

  Create `AdventureWorksProductAdvisor\Services\AzureOpenAiChatCompletionService.cs`:

  ```csharp
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using Azure.Core;
  using Newtonsoft.Json.Linq;

  namespace AdventureWorksProductAdvisor.Services
  {
      public class AzureOpenAiChatCompletionService : IChatCompletionService
      {
          // Kept identical to dbo.AskProductQuestion's system message (sql/dbo.AskProductQuestion.sql)
          // so both pipelines answer under the same instructions for a fair comparison.
          private const string SystemPrompt =
              "You are an Adventure Works product assistant. Follow these rules:\n" +
              "1. Answer only using the provided product reviews and data\n" +
              "2. Reference specific customer experiences from the reviews when relevant\n" +
              "3. Include star ratings to help the customer assess product quality\n" +
              "4. Keep responses under 150 words\n" +
              "5. Suggest related products when relevant";

          private readonly HttpClient _httpClient;
          private readonly TokenCredential _credential;
          private readonly string _endpoint;
          private readonly string _deploymentName;
          private readonly string _apiVersion;

          public AzureOpenAiChatCompletionService(
              HttpClient httpClient, TokenCredential credential, string endpoint, string deploymentName, string apiVersion)
          {
              _httpClient = httpClient;
              _credential = credential;
              _endpoint = endpoint;
              _deploymentName = deploymentName;
              _apiVersion = apiVersion;
          }

          public async Task<string> GetCompletionAsync(string prompt, int maxCompletionTokens)
          {
              var token = await _credential.GetTokenAsync(
                  new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }),
                  CancellationToken.None);

              var url = $"{_endpoint}/openai/deployments/{_deploymentName}/chat/completions?api-version={_apiVersion}";

              var payload = new JObject
              {
                  ["messages"] = new JArray
                  {
                      new JObject { ["role"] = "system", ["content"] = SystemPrompt },
                      new JObject { ["role"] = "user", ["content"] = prompt }
                  },
                  ["max_completion_tokens"] = maxCompletionTokens,
                  ["temperature"] = 0.5
              };

              using (var request = new HttpRequestMessage(HttpMethod.Post, url))
              {
                  request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                  request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

                  var response = await _httpClient.SendAsync(request);
                  response.EnsureSuccessStatusCode();

                  var responseJson = await response.Content.ReadAsStringAsync();
                  var parsed = JObject.Parse(responseJson);
                  return parsed["choices"][0]["message"]["content"].ToString();
              }
          }
      }
  }
  ```

- [ ] **Step 5: Run the build and tests to confirm they pass**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: `Build succeeded. 0 Error(s)`.

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll" -TestCaseFilter:"FullyQualifiedName~AzureOpenAiChatCompletionServiceTests"`
  Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 6: Commit**

  ```bash
  git add AdventureWorksProductAdvisor/Services/IChatCompletionService.cs AdventureWorksProductAdvisor/Services/AzureOpenAiChatCompletionService.cs AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj AdventureWorksProductAdvisor.Tests/Services/AzureOpenAiChatCompletionServiceTests.cs AdventureWorksProductAdvisor.Tests/AdventureWorksProductAdvisor.Tests.csproj
  git commit -m "Add AzureOpenAiChatCompletionService with managed-identity auth"
  ```

---

## Task 5: Similarity Ranker

**Files:**
- Create: `AdventureWorksProductAdvisor\Services\SimilarityRanker.cs`
- Test: `AdventureWorksProductAdvisor.Tests\Services\SimilarityRankerTests.cs`
- Modify: `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`, `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj` (register the new files — see the steps below)

**Interfaces:**
- Consumes: `AdventureWorksProductAdvisor.Data.ReviewRecord` (from Task 2).
- Produces: `AdventureWorksProductAdvisor.Services.SimilarityRanker` — `List<ReviewRecord> GetTopN(IEnumerable<ReviewRecord> reviews, float[] queryEmbedding, int topN)`.

- [ ] **Step 1: Write the failing tests**

  Create `AdventureWorksProductAdvisor.Tests\Services\SimilarityRankerTests.cs`:

  ```csharp
  using System.Collections.Generic;
  using System.Linq;
  using AdventureWorksProductAdvisor.Data;
  using AdventureWorksProductAdvisor.Services;
  using Microsoft.VisualStudio.TestTools.UnitTesting;

  namespace AdventureWorksProductAdvisor.Tests.Services
  {
      [TestClass]
      public class SimilarityRankerTests
      {
          [TestMethod]
          public void GetTopN_OrdersByDescendingCosineSimilarity()
          {
              var query = new float[] { 1f, 0f };
              var reviews = new List<ReviewRecord>
              {
                  new ReviewRecord { ReviewId = 1, Embedding = new float[] { 0f, 1f } },
                  new ReviewRecord { ReviewId = 2, Embedding = new float[] { 1f, 0f } },
                  new ReviewRecord { ReviewId = 3, Embedding = new float[] { 0.5f, 0.5f } }
              };
              var ranker = new SimilarityRanker();

              var result = ranker.GetTopN(reviews, query, 3);

              CollectionAssert.AreEqual(new[] { 2, 3, 1 }, result.Select(r => r.ReviewId).ToList());
          }

          [TestMethod]
          public void GetTopN_LimitsToRequestedCount()
          {
              var query = new float[] { 1f, 0f };
              var reviews = new List<ReviewRecord>
              {
                  new ReviewRecord { ReviewId = 1, Embedding = new float[] { 1f, 0f } },
                  new ReviewRecord { ReviewId = 2, Embedding = new float[] { 0.9f, 0.1f } },
                  new ReviewRecord { ReviewId = 3, Embedding = new float[] { 0.8f, 0.2f } }
              };
              var ranker = new SimilarityRanker();

              var result = ranker.GetTopN(reviews, query, 2);

              Assert.AreEqual(2, result.Count);
              CollectionAssert.AreEqual(new[] { 1, 2 }, result.Select(r => r.ReviewId).ToList());
          }

          [TestMethod]
          public void GetTopN_IgnoresReviewsWithoutEmbeddings()
          {
              var query = new float[] { 1f, 0f };
              var reviews = new List<ReviewRecord>
              {
                  new ReviewRecord { ReviewId = 1, Embedding = null },
                  new ReviewRecord { ReviewId = 2, Embedding = new float[] { 1f, 0f } }
              };
              var ranker = new SimilarityRanker();

              var result = ranker.GetTopN(reviews, query, 5);

              CollectionAssert.AreEqual(new[] { 2 }, result.Select(r => r.ReviewId).ToList());
          }
      }
  }
  ```

  Register the new files. In `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`'s `Compile` `<ItemGroup>`, add `<Compile Include="Services\SimilarityRanker.cs" />`. In `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj`'s `Compile` `<ItemGroup>`, add `<Compile Include="Services\SimilarityRankerTests.cs" />`. (`SimilarityRanker.cs` doesn't exist yet — expected, the next step's build should fail.)

- [ ] **Step 2: Run the build to confirm it fails to compile**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: FAIL — a compilation error (missing source file or missing type) confirming the red state.

- [ ] **Step 3: Write the implementation**

  Create `AdventureWorksProductAdvisor\Services\SimilarityRanker.cs`:

  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using AdventureWorksProductAdvisor.Data;

  namespace AdventureWorksProductAdvisor.Services
  {
      public class SimilarityRanker
      {
          public List<ReviewRecord> GetTopN(IEnumerable<ReviewRecord> reviews, float[] queryEmbedding, int topN)
          {
              return reviews
                  .Where(r => r.Embedding != null && r.Embedding.Length == queryEmbedding.Length)
                  .Select(r => new { Review = r, Score = CosineSimilarity(r.Embedding, queryEmbedding) })
                  .OrderByDescending(x => x.Score)
                  .Take(topN)
                  .Select(x => x.Review)
                  .ToList();
          }

          private static double CosineSimilarity(float[] a, float[] b)
          {
              double dot = 0, magA = 0, magB = 0;
              for (var i = 0; i < a.Length; i++)
              {
                  dot += a[i] * b[i];
                  magA += a[i] * a[i];
                  magB += b[i] * b[i];
              }
              if (magA == 0 || magB == 0)
              {
                  return 0;
              }
              return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
          }
      }
  }
  ```

- [ ] **Step 4: Run the build and tests to confirm they pass**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: `Build succeeded. 0 Error(s)`.

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll" -TestCaseFilter:"FullyQualifiedName~SimilarityRankerTests"`
  Expected: `Passed! - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**

  ```bash
  git add AdventureWorksProductAdvisor/Services/SimilarityRanker.cs AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj AdventureWorksProductAdvisor.Tests/Services/SimilarityRankerTests.cs AdventureWorksProductAdvisor.Tests/AdventureWorksProductAdvisor.Tests.csproj
  git commit -m "Add SimilarityRanker for cosine-similarity top-N selection"
  ```

---

## Task 6: C# Ask Service (Pipeline Orchestration)

**Files:**
- Create: `AdventureWorksProductAdvisor\Services\IAskService.cs`
- Create: `AdventureWorksProductAdvisor\Services\AskResult.cs` (internal orchestration DTO — see note below)
- Create: `AdventureWorksProductAdvisor\Services\CSharpAskService.cs`
- Test: `AdventureWorksProductAdvisor.Tests\Services\CSharpAskServiceTests.cs`
- Modify: `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`, `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj` (register the new files — see the steps below)

**Interfaces:**
- Consumes: `AdventureWorksProductAdvisor.Data.IReviewRepository`, `AdventureWorksProductAdvisor.Data.ReviewRecord` (Task 2); `AdventureWorksProductAdvisor.Services.IEmbeddingService` (Task 3); `AdventureWorksProductAdvisor.Services.IChatCompletionService` (Task 4); `AdventureWorksProductAdvisor.Services.SimilarityRanker` (Task 5).
- Produces:
  - `AdventureWorksProductAdvisor.Services.IAskService` — `Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN)`.
  - `AdventureWorksProductAdvisor.Services.AskServiceResult` — properties `bool Success`, `string Answer`. (This is the pipeline-internal result type; the HTTP-facing `AskResponse` model, which also carries `Mode`/`ErrorMessage`, is built by `AskController` in Task 7 from this.)
  - `AdventureWorksProductAdvisor.Services.CSharpAskService : IAskService`, constructor `CSharpAskService(IReviewRepository reviewRepository, IEmbeddingService embeddingService, IChatCompletionService chatCompletionService)`.

- [ ] **Step 1: Write `IAskService` and `AskServiceResult`**

  Create `AdventureWorksProductAdvisor\Services\AskResult.cs`:

  ```csharp
  namespace AdventureWorksProductAdvisor.Services
  {
      public class AskServiceResult
      {
          public bool Success { get; set; }
          public string Answer { get; set; }
      }
  }
  ```

  Create `AdventureWorksProductAdvisor\Services\IAskService.cs`:

  ```csharp
  using System.Threading.Tasks;

  namespace AdventureWorksProductAdvisor.Services
  {
      public interface IAskService
      {
          Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN);
      }
  }
  ```

- [ ] **Step 2: Write the failing tests**

  Create `AdventureWorksProductAdvisor.Tests\Services\CSharpAskServiceTests.cs`:

  ```csharp
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using AdventureWorksProductAdvisor.Data;
  using AdventureWorksProductAdvisor.Services;
  using Microsoft.VisualStudio.TestTools.UnitTesting;
  using Moq;

  namespace AdventureWorksProductAdvisor.Tests.Services
  {
      [TestClass]
      public class CSharpAskServiceTests
      {
          [TestMethod]
          public async Task AskAsync_RetrievesTopNReviewsAndReturnsAnswer()
          {
              var reviews = new List<ReviewRecord>
              {
                  new ReviewRecord { ReviewId = 1, ProductName = "Mountain Bike", Rating = 5, ReviewTitle = "Great", ReviewText = "Loved it", Embedding = new float[] { 1f, 0f } },
                  new ReviewRecord { ReviewId = 2, ProductName = "Road Bike", Rating = 2, ReviewTitle = "Meh", ReviewText = "It was okay", Embedding = new float[] { 0f, 1f } }
              };

              var reviewRepository = new Mock<IReviewRepository>();
              reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(reviews);

              var embeddingService = new Mock<IEmbeddingService>();
              embeddingService.Setup(e => e.GetEmbeddingAsync("What bike is best for trails?"))
                  .ReturnsAsync(new float[] { 1f, 0f });

              var chatCompletionService = new Mock<IChatCompletionService>();
              chatCompletionService
                  .Setup(c => c.GetCompletionAsync(
                      It.Is<string>(p => p.Contains("Mountain Bike") && p.Contains("What bike is best for trails?")),
                      500))
                  .ReturnsAsync("The Mountain Bike is best for trails.");

              var service = new CSharpAskService(reviewRepository.Object, embeddingService.Object, chatCompletionService.Object);

              var result = await service.AskAsync("What bike is best for trails?", 500, 1);

              Assert.IsTrue(result.Success);
              Assert.AreEqual("The Mountain Bike is best for trails.", result.Answer);
              chatCompletionService.Verify(c => c.GetCompletionAsync(It.IsAny<string>(), 500), Times.Once);
          }

          [TestMethod]
          public async Task AskAsync_NoMatchingReviews_ReturnsFriendlyAnswerWithoutCallingChatCompletion()
          {
              var reviewRepository = new Mock<IReviewRepository>();
              reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(new List<ReviewRecord>());

              var embeddingService = new Mock<IEmbeddingService>();
              embeddingService.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(new float[] { 1f, 0f });

              var chatCompletionService = new Mock<IChatCompletionService>();

              var service = new CSharpAskService(reviewRepository.Object, embeddingService.Object, chatCompletionService.Object);

              var result = await service.AskAsync("Anything?", 500, 5);

              Assert.IsTrue(result.Success);
              Assert.AreEqual("No reviews found matching your query. Please try a different question.", result.Answer);
              chatCompletionService.Verify(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
          }
      }
  }
  ```

  Register the new files. In `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`'s `Compile` `<ItemGroup>`, add:

  ```xml
    <Compile Include="Services\AskResult.cs" />
    <Compile Include="Services\IAskService.cs" />
    <Compile Include="Services\CSharpAskService.cs" />
  ```

  In `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj`'s `Compile` `<ItemGroup>`, add `<Compile Include="Services\CSharpAskServiceTests.cs" />`. (`CSharpAskService.cs` doesn't exist yet — expected, the next step's build should fail.)

- [ ] **Step 3: Run the build to confirm it fails to compile**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: FAIL — a compilation error (missing source file or missing type) confirming the red state.

- [ ] **Step 4: Write the implementation**

  Create `AdventureWorksProductAdvisor\Services\CSharpAskService.cs`:

  ```csharp
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using AdventureWorksProductAdvisor.Data;

  namespace AdventureWorksProductAdvisor.Services
  {
      public class CSharpAskService : IAskService
      {
          private readonly IReviewRepository _reviewRepository;
          private readonly IEmbeddingService _embeddingService;
          private readonly IChatCompletionService _chatCompletionService;
          private readonly SimilarityRanker _ranker = new SimilarityRanker();

          public CSharpAskService(
              IReviewRepository reviewRepository, IEmbeddingService embeddingService, IChatCompletionService chatCompletionService)
          {
              _reviewRepository = reviewRepository;
              _embeddingService = embeddingService;
              _chatCompletionService = chatCompletionService;
          }

          public async Task<AskServiceResult> AskAsync(string question, int maxCompletionTokens, int topN)
          {
              var queryEmbedding = await _embeddingService.GetEmbeddingAsync(question);
              var allReviews = await _reviewRepository.GetAllReviewsAsync();
              var topReviews = _ranker.GetTopN(allReviews, queryEmbedding, topN);

              if (topReviews.Count == 0)
              {
                  return new AskServiceResult
                  {
                      Success = true,
                      Answer = "No reviews found matching your query. Please try a different question."
                  };
              }

              var prompt = BuildPrompt(topReviews, question);
              var answer = await _chatCompletionService.GetCompletionAsync(prompt, maxCompletionTokens);

              return new AskServiceResult { Success = true, Answer = answer };
          }

          private static string BuildPrompt(System.Collections.Generic.List<ReviewRecord> reviews, string question)
          {
              var sb = new StringBuilder();
              sb.AppendLine("Product reviews:");
              foreach (var r in reviews)
              {
                  sb.AppendLine($"- Product: {r.ProductName}, Rating: {r.Rating}/5, Title: \"{r.ReviewTitle}\"");
                  sb.AppendLine($"  {r.ReviewText}");
              }
              sb.AppendLine();
              sb.Append("Customer question: ").Append(question);
              return sb.ToString();
          }
      }
  }
  ```

- [ ] **Step 5: Run the build and tests to confirm they pass**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: `Build succeeded. 0 Error(s)`.

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll" -TestCaseFilter:"FullyQualifiedName~CSharpAskServiceTests"`
  Expected: `Passed! - Failed: 0, Passed: 2`.

- [ ] **Step 6: Commit**

  ```bash
  git add AdventureWorksProductAdvisor/Services/IAskService.cs AdventureWorksProductAdvisor/Services/AskResult.cs AdventureWorksProductAdvisor/Services/CSharpAskService.cs AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj AdventureWorksProductAdvisor.Tests/Services/CSharpAskServiceTests.cs AdventureWorksProductAdvisor.Tests/AdventureWorksProductAdvisor.Tests.csproj
  git commit -m "Add CSharpAskService orchestrating embed -> rank -> prompt -> complete"
  ```

---

## Task 7: Composition Root, Models, and AskController

**Files:**
- Create: `AdventureWorksProductAdvisor\Services\AskServiceFactory.cs`
- Create: `AdventureWorksProductAdvisor\Models\AskRequest.cs`
- Create: `AdventureWorksProductAdvisor\Models\AskResponse.cs`
- Create: `AdventureWorksProductAdvisor\Controllers\Api\AskController.cs`
- Test: `AdventureWorksProductAdvisor.Tests\Controllers\Api\AskControllerTests.cs`
- Modify: `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`, `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj` (register the new files — see the steps below)

**Interfaces:**
- Consumes: `IReviewRepository`/`ReviewRepository` (Task 2), `IEmbeddingService`/`AzureOpenAiEmbeddingService` (Task 3), `IChatCompletionService`/`AzureOpenAiChatCompletionService` (Task 4), `IAskService`/`CSharpAskService`/`AskServiceResult` (Task 6).
- Produces:
  - `AdventureWorksProductAdvisor.Services.AskServiceFactory` — static methods `IReviewRepository CreateReviewRepository()`, `IEmbeddingService CreateEmbeddingService()`, `IChatCompletionService CreateChatCompletionService()`, `IReadOnlyDictionary<string, IAskService> CreatePipelines()`. Used by Task 8's `AdminController` too.
  - `AdventureWorksProductAdvisor.Models.AskRequest` — properties `string Question`, `string Mode`, `int? TopN`.
  - `AdventureWorksProductAdvisor.Models.AskResponse` — properties `bool Success`, `string Answer`, `string Mode`, `string ErrorMessage`.
  - `AdventureWorksProductAdvisor.Controllers.Api.AskController : ApiController`, testable constructor `AskController(IReadOnlyDictionary<string, IAskService> pipelinesByMode, int maxCompletionTokens)`, route `POST api/ask`.

Not unit tested: `AskServiceFactory` (pure composition wiring against real config/network — no meaningful assertion without a live environment).

- [ ] **Step 1: Write the models**

  Create `AdventureWorksProductAdvisor\Models\AskRequest.cs`:

  ```csharp
  namespace AdventureWorksProductAdvisor.Models
  {
      public class AskRequest
      {
          public string Question { get; set; }
          public string Mode { get; set; }
          public int? TopN { get; set; }
      }
  }
  ```

  Create `AdventureWorksProductAdvisor\Models\AskResponse.cs`:

  ```csharp
  namespace AdventureWorksProductAdvisor.Models
  {
      public class AskResponse
      {
          public bool Success { get; set; }
          public string Answer { get; set; }
          public string Mode { get; set; }
          public string ErrorMessage { get; set; }
      }
  }
  ```

- [ ] **Step 2: Write the failing tests**

  Create `AdventureWorksProductAdvisor.Tests\Controllers\Api\AskControllerTests.cs`:

  ```csharp
  using System.Collections.Generic;
  using System.Net.Http;
  using System.Threading.Tasks;
  using System.Web.Http;
  using System.Web.Http.Results;
  using AdventureWorksProductAdvisor.Controllers.Api;
  using AdventureWorksProductAdvisor.Models;
  using AdventureWorksProductAdvisor.Services;
  using Microsoft.VisualStudio.TestTools.UnitTesting;
  using Moq;

  namespace AdventureWorksProductAdvisor.Tests.Controllers.Api
  {
      [TestClass]
      public class AskControllerTests
      {
          [TestMethod]
          public async Task Post_KnownMode_InvokesPipelineAndReturnsAnswer()
          {
              var askService = new Mock<IAskService>();
              askService.Setup(s => s.AskAsync("What is the best product?", 500, 5))
                  .ReturnsAsync(new AskServiceResult { Success = true, Answer = "It's great." });

              var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
              var controller = CreateController(pipelines);

              var result = await controller.Post(new AskRequest { Question = "What is the best product?", Mode = "CSharp", TopN = 5 });

              var okResult = result as OkNegotiatedContentResult<AskResponse>;
              Assert.IsNotNull(okResult);
              Assert.IsTrue(okResult.Content.Success);
              Assert.AreEqual("It's great.", okResult.Content.Answer);
              Assert.AreEqual("CSharp", okResult.Content.Mode);
          }

          [TestMethod]
          public async Task Post_UnknownMode_ReturnsNotImplementedMessageWithoutInvokingAnyPipeline()
          {
              var askService = new Mock<IAskService>();
              var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
              var controller = CreateController(pipelines);

              var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "StoredProc", TopN = 5 });

              var okResult = result as OkNegotiatedContentResult<AskResponse>;
              Assert.IsNotNull(okResult);
              Assert.IsFalse(okResult.Content.Success);
              StringAssert.Contains(okResult.Content.ErrorMessage, "not yet implemented");
              askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
          }

          [TestMethod]
          public async Task Post_TopNOutOfRange_ReturnsValidationErrorWithoutInvokingPipeline()
          {
              var askService = new Mock<IAskService>();
              var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
              var controller = CreateController(pipelines);

              var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 11 });

              var okResult = result as OkNegotiatedContentResult<AskResponse>;
              Assert.IsNotNull(okResult);
              Assert.IsFalse(okResult.Content.Success);
              StringAssert.Contains(okResult.Content.ErrorMessage, "between 1 and 10");
              askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
          }

          private static AskController CreateController(IReadOnlyDictionary<string, IAskService> pipelines)
          {
              var controller = new AskController(pipelines, 500);
              controller.Configuration = new HttpConfiguration();
              controller.Request = new HttpRequestMessage();
              return controller;
          }
      }
  }
  ```

  Register the new files. In `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`'s `Compile` `<ItemGroup>`, add:

  ```xml
    <Compile Include="Models\AskRequest.cs" />
    <Compile Include="Models\AskResponse.cs" />
    <Compile Include="Services\AskServiceFactory.cs" />
    <Compile Include="Controllers\Api\AskController.cs" />
  ```

  In `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj`'s `Compile` `<ItemGroup>`, add `<Compile Include="Controllers\Api\AskControllerTests.cs" />`. (`AskServiceFactory.cs` and `AskController.cs` don't exist yet — expected, the next step's build should fail.)

- [ ] **Step 3: Run the build to confirm it fails to compile**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: FAIL — a compilation error (missing source file or missing type) confirming the red state.

- [ ] **Step 4: Write `AskServiceFactory`**

  Create `AdventureWorksProductAdvisor\Services\AskServiceFactory.cs`:

  ```csharp
  using System.Collections.Generic;
  using System.Configuration;
  using System.Net.Http;
  using Azure.Identity;
  using AdventureWorksProductAdvisor.Data;

  namespace AdventureWorksProductAdvisor.Services
  {
      public static class AskServiceFactory
      {
          public static IReviewRepository CreateReviewRepository()
          {
              var connectionString = ConfigurationManager.ConnectionStrings["AdventureWorksLT"].ConnectionString;
              return new ReviewRepository(connectionString);
          }

          public static IEmbeddingService CreateEmbeddingService()
          {
              return new AzureOpenAiEmbeddingService(
                  new HttpClient(),
                  new DefaultAzureCredential(),
                  ConfigurationManager.AppSettings["AzureOpenAiEndpoint"],
                  ConfigurationManager.AppSettings["AzureOpenAiEmbeddingDeployment"],
                  ConfigurationManager.AppSettings["AzureOpenAiApiVersion"]);
          }

          public static IChatCompletionService CreateChatCompletionService()
          {
              return new AzureOpenAiChatCompletionService(
                  new HttpClient(),
                  new DefaultAzureCredential(),
                  ConfigurationManager.AppSettings["AzureOpenAiEndpoint"],
                  ConfigurationManager.AppSettings["AzureOpenAiChatDeployment"],
                  ConfigurationManager.AppSettings["AzureOpenAiApiVersion"]);
          }

          public static IReadOnlyDictionary<string, IAskService> CreatePipelines()
          {
              var csharpAskService = new CSharpAskService(
                  CreateReviewRepository(), CreateEmbeddingService(), CreateChatCompletionService());

              return new Dictionary<string, IAskService>
              {
                  ["CSharp"] = csharpAskService
              };
          }
      }
  }
  ```

- [ ] **Step 5: Write `AskController`**

  Create `AdventureWorksProductAdvisor\Controllers\Api\AskController.cs`:

  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Configuration;
  using System.Threading.Tasks;
  using System.Web.Http;
  using AdventureWorksProductAdvisor.Models;
  using AdventureWorksProductAdvisor.Services;

  namespace AdventureWorksProductAdvisor.Controllers.Api
  {
      [RoutePrefix("api/ask")]
      public class AskController : ApiController
      {
          private readonly IReadOnlyDictionary<string, IAskService> _pipelinesByMode;
          private readonly int _maxCompletionTokens;

          public AskController()
              : this(AskServiceFactory.CreatePipelines(), int.Parse(ConfigurationManager.AppSettings["MaxCompletionTokens"]))
          {
          }

          public AskController(IReadOnlyDictionary<string, IAskService> pipelinesByMode, int maxCompletionTokens)
          {
              _pipelinesByMode = pipelinesByMode;
              _maxCompletionTokens = maxCompletionTokens;
          }

          [HttpPost]
          [Route("")]
          public async Task<IHttpActionResult> Post(AskRequest request)
          {
              if (request == null || string.IsNullOrWhiteSpace(request.Question))
              {
                  return Ok(new AskResponse { Success = false, ErrorMessage = "Question is required." });
              }

              var topN = request.TopN ?? 5;
              if (topN < 1 || topN > 10)
              {
                  return Ok(new AskResponse { Success = false, Mode = request.Mode, ErrorMessage = "TopN must be between 1 and 10." });
              }

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

              try
              {
                  var result = await pipeline.AskAsync(request.Question, _maxCompletionTokens, topN);
                  return Ok(new AskResponse
                  {
                      Success = result.Success,
                      Answer = result.Answer,
                      Mode = request.Mode
                  });
              }
              catch (Exception)
              {
                  return Ok(new AskResponse
                  {
                      Success = false,
                      Mode = request.Mode,
                      ErrorMessage = "An error occurred while processing your question. Please try again."
                  });
              }
          }
      }
  }
  ```

- [ ] **Step 6: Run the build and tests to confirm they pass**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: `Build succeeded. 0 Error(s)`.

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll" -TestCaseFilter:"FullyQualifiedName~AskControllerTests"`
  Expected: `Passed! - Failed: 0, Passed: 3`.

- [ ] **Step 7: Commit**

  ```bash
  git add AdventureWorksProductAdvisor/Services/AskServiceFactory.cs AdventureWorksProductAdvisor/Models AdventureWorksProductAdvisor/Controllers/Api/AskController.cs AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj AdventureWorksProductAdvisor.Tests/Controllers AdventureWorksProductAdvisor.Tests/AdventureWorksProductAdvisor.Tests.csproj
  git commit -m "Add AskController with mode routing, TopN validation, and composition root"
  ```

---

## Task 8: Embedding Backfill

**Files:**
- Create: `AdventureWorksProductAdvisor\Services\ReviewEmbeddingBackfillRunner.cs`
- Create: `AdventureWorksProductAdvisor\Controllers\Api\AdminController.cs`
- Test: `AdventureWorksProductAdvisor.Tests\Services\ReviewEmbeddingBackfillRunnerTests.cs`
- Test: `AdventureWorksProductAdvisor.Tests\Controllers\Api\AdminControllerTests.cs`
- Modify: `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`, `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj` (register the new files — see the steps below; this task registers files in two batches since it has two red/green cycles)

**Interfaces:**
- Consumes: `IReviewRepository` (Task 2), `IEmbeddingService` (Task 3), `AskServiceFactory` (Task 7).
- Produces:
  - `AdventureWorksProductAdvisor.Services.ReviewEmbeddingBackfillRunner`, constructor `ReviewEmbeddingBackfillRunner(IReviewRepository reviewRepository, IEmbeddingService embeddingService)`, method `Task<int> RunAsync()`.
  - `AdventureWorksProductAdvisor.Controllers.Api.AdminController : ApiController`, testable constructor `AdminController(ReviewEmbeddingBackfillRunner runner)`, route `POST api/admin/backfill-embeddings`.

- [ ] **Step 1: Write the failing test for the runner**

  Create `AdventureWorksProductAdvisor.Tests\Services\ReviewEmbeddingBackfillRunnerTests.cs`:

  ```csharp
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using AdventureWorksProductAdvisor.Data;
  using AdventureWorksProductAdvisor.Services;
  using Microsoft.VisualStudio.TestTools.UnitTesting;
  using Moq;

  namespace AdventureWorksProductAdvisor.Tests.Services
  {
      [TestClass]
      public class ReviewEmbeddingBackfillRunnerTests
      {
          [TestMethod]
          public async Task RunAsync_EmbedsAndSavesEveryReview()
          {
              var reviews = new List<ReviewRecord>
              {
                  new ReviewRecord { ReviewId = 1, ReviewText = "Loved it" },
                  new ReviewRecord { ReviewId = 2, ReviewText = "It was okay" }
              };

              var reviewRepository = new Mock<IReviewRepository>();
              reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(reviews);

              var embeddingService = new Mock<IEmbeddingService>();
              embeddingService.Setup(e => e.GetEmbeddingAsync("Loved it")).ReturnsAsync(new float[] { 0.5f });
              embeddingService.Setup(e => e.GetEmbeddingAsync("It was okay")).ReturnsAsync(new float[] { 0.25f });

              var runner = new ReviewEmbeddingBackfillRunner(reviewRepository.Object, embeddingService.Object);

              var count = await runner.RunAsync();

              Assert.AreEqual(2, count);
              reviewRepository.Verify(
                  r => r.SaveEmbeddingAsync(1, It.Is<float[]>(a => a.SequenceEqual(new float[] { 0.5f }))), Times.Once);
              reviewRepository.Verify(
                  r => r.SaveEmbeddingAsync(2, It.Is<float[]>(a => a.SequenceEqual(new float[] { 0.25f }))), Times.Once);
          }
      }
  }
  ```

  Register the new files (batch 1). In `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`'s `Compile` `<ItemGroup>`, add `<Compile Include="Services\ReviewEmbeddingBackfillRunner.cs" />`. In `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj`'s `Compile` `<ItemGroup>`, add `<Compile Include="Services\ReviewEmbeddingBackfillRunnerTests.cs" />`. (`ReviewEmbeddingBackfillRunner.cs` doesn't exist yet — expected.)

- [ ] **Step 2: Run the build to confirm it fails to compile**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: FAIL — a compilation error (missing source file or missing type) confirming the red state.

- [ ] **Step 3: Write `ReviewEmbeddingBackfillRunner`**

  Create `AdventureWorksProductAdvisor\Services\ReviewEmbeddingBackfillRunner.cs`:

  ```csharp
  using System.Threading.Tasks;
  using AdventureWorksProductAdvisor.Data;

  namespace AdventureWorksProductAdvisor.Services
  {
      public class ReviewEmbeddingBackfillRunner
      {
          private readonly IReviewRepository _reviewRepository;
          private readonly IEmbeddingService _embeddingService;

          public ReviewEmbeddingBackfillRunner(IReviewRepository reviewRepository, IEmbeddingService embeddingService)
          {
              _reviewRepository = reviewRepository;
              _embeddingService = embeddingService;
          }

          public async Task<int> RunAsync()
          {
              var reviews = await _reviewRepository.GetAllReviewsAsync();
              var count = 0;
              foreach (var review in reviews)
              {
                  var embedding = await _embeddingService.GetEmbeddingAsync(review.ReviewText);
                  await _reviewRepository.SaveEmbeddingAsync(review.ReviewId, embedding);
                  count++;
              }
              return count;
          }
      }
  }
  ```

- [ ] **Step 4: Run the build and tests to confirm the runner test passes**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: `Build succeeded. 0 Error(s)`.

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll" -TestCaseFilter:"FullyQualifiedName~ReviewEmbeddingBackfillRunnerTests"`
  Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 5: Write the failing test for `AdminController`**

  Create `AdventureWorksProductAdvisor.Tests\Controllers\Api\AdminControllerTests.cs`:

  ```csharp
  using System.Collections.Generic;
  using System.Net.Http;
  using System.Threading.Tasks;
  using System.Web.Http;
  using System.Web.Http.Results;
  using AdventureWorksProductAdvisor.Controllers.Api;
  using AdventureWorksProductAdvisor.Data;
  using AdventureWorksProductAdvisor.Services;
  using Microsoft.VisualStudio.TestTools.UnitTesting;
  using Moq;

  namespace AdventureWorksProductAdvisor.Tests.Controllers.Api
  {
      [TestClass]
      public class AdminControllerTests
      {
          [TestMethod]
          public async Task BackfillEmbeddings_ReturnsCountOfEmbeddedReviews()
          {
              var reviews = new List<ReviewRecord> { new ReviewRecord { ReviewId = 1, ReviewText = "Loved it" } };
              var reviewRepository = new Mock<IReviewRepository>();
              reviewRepository.Setup(r => r.GetAllReviewsAsync()).ReturnsAsync(reviews);

              var embeddingService = new Mock<IEmbeddingService>();
              embeddingService.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(new float[] { 0.5f });

              var runner = new ReviewEmbeddingBackfillRunner(reviewRepository.Object, embeddingService.Object);
              var controller = new AdminController(runner);
              controller.Configuration = new HttpConfiguration();
              controller.Request = new HttpRequestMessage();

              var result = await controller.BackfillEmbeddings();

              var okResult = result as OkNegotiatedContentResult<int>;
              Assert.IsNotNull(okResult);
              Assert.AreEqual(1, okResult.Content);
          }
      }
  }
  ```

  Register the new files (batch 2). In `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj`'s `Compile` `<ItemGroup>`, add `<Compile Include="Controllers\Api\AdminController.cs" />`. In `AdventureWorksProductAdvisor.Tests\AdventureWorksProductAdvisor.Tests.csproj`'s `Compile` `<ItemGroup>`, add `<Compile Include="Controllers\Api\AdminControllerTests.cs" />`. (`AdminController.cs` doesn't exist yet — expected.)

- [ ] **Step 6: Run the build to confirm it fails to compile**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: FAIL — a compilation error (missing source file or missing type) confirming the red state.

- [ ] **Step 7: Write `AdminController`**

  Create `AdventureWorksProductAdvisor\Controllers\Api\AdminController.cs`:

  ```csharp
  using System.Threading.Tasks;
  using System.Web.Http;
  using AdventureWorksProductAdvisor.Services;

  namespace AdventureWorksProductAdvisor.Controllers.Api
  {
      [RoutePrefix("api/admin")]
      public class AdminController : ApiController
      {
          private readonly ReviewEmbeddingBackfillRunner _runner;

          public AdminController()
              : this(new ReviewEmbeddingBackfillRunner(AskServiceFactory.CreateReviewRepository(), AskServiceFactory.CreateEmbeddingService()))
          {
          }

          public AdminController(ReviewEmbeddingBackfillRunner runner)
          {
              _runner = runner;
          }

          [HttpPost]
          [Route("backfill-embeddings")]
          public async Task<IHttpActionResult> BackfillEmbeddings()
          {
              var count = await _runner.RunAsync();
              return Ok(count);
          }
      }
  }
  ```

- [ ] **Step 8: Run the build and tests to confirm everything passes**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: `Build succeeded. 0 Error(s)`.

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.Tests\bin\Debug\AdventureWorksProductAdvisor.Tests.dll"`
  Expected: `Passed! - Failed: 0` (all tests across every task so far).

- [ ] **Step 9: Commit**

  ```bash
  git add AdventureWorksProductAdvisor/Services/ReviewEmbeddingBackfillRunner.cs AdventureWorksProductAdvisor/Controllers/Api/AdminController.cs AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj AdventureWorksProductAdvisor.Tests/Services/ReviewEmbeddingBackfillRunnerTests.cs AdventureWorksProductAdvisor.Tests/Controllers/Api/AdminControllerTests.cs AdventureWorksProductAdvisor.Tests/AdventureWorksProductAdvisor.Tests.csproj
  git commit -m "Add one-time embedding backfill runner and admin trigger endpoint"
  ```

---

## Task 9: Chat Widget UI

**Files:**
- Modify: `AdventureWorksProductAdvisor\Views\Home\Index.cshtml`
- Create: `AdventureWorksProductAdvisor\Scripts\chat-widget.js`

**Interfaces:**
- Consumes: `POST /api/ask` (Task 7) with body `{ Question, Mode, TopN }`, response `{ Success, Answer, Mode, ErrorMessage }`.
- Produces: a browser page where a question can be typed, mode/top-N chosen, and an answer bubble rendered.

No automated tests — verified manually in a browser in Task 10, per the design doc's UI-testing convention.

- [ ] **Step 1: Write `Views\Home\Index.cshtml`**

  This project has no Bootstrap/jQuery (dropped during Task 1's scaffolding — no bundling, nothing else in the app needs it), so this markup uses plain inline styles rather than Bootstrap classes. Replace the contents of `AdventureWorksProductAdvisor\Views\Home\Index.cshtml` with:

  ```html
  @{
      ViewBag.Title = "AdventureWorks Product Advisor";
  }

  <div style="max-width: 700px; margin: 0 auto;">
      <h2>AdventureWorks Product Advisor</h2>

      <div style="margin-bottom: 12px;">
          <label>
              <input type="radio" name="mode" value="CSharp" checked="checked" /> C# Pipeline
          </label>
          <label style="margin-left: 20px;">
              <input type="radio" name="mode" value="StoredProc" /> Stored Procedure
          </label>
      </div>

      <div style="margin-bottom: 12px;">
          <label for="topN">Reviews to retrieve (1-10):</label>
          <input type="number" id="topN" min="1" max="10" step="1" value="5" style="width: 60px;" />
      </div>

      <div style="margin-bottom: 12px;">
          <input type="text" id="question" style="width: 100%; padding: 6px;" placeholder="Ask a question about our products..." />
      </div>

      <button type="button" id="askButton">Ask</button>

      <div id="chatHistory" style="margin-top: 20px;"></div>
  </div>

  @section scripts {
      <script src="~/Scripts/chat-widget.js"></script>
  }
  ```

- [ ] **Step 2: Write `Scripts\chat-widget.js`**

  Create `AdventureWorksProductAdvisor\Scripts\chat-widget.js`:

  ```javascript
  (function () {
      "use strict";

      function getSelectedMode() {
          var radios = document.getElementsByName("mode");
          for (var i = 0; i < radios.length; i++) {
              if (radios[i].checked) {
                  return radios[i].value;
              }
          }
          return "CSharp";
      }

      function appendBubble(mode, text, isError) {
          var history = document.getElementById("chatHistory");
          var bubble = document.createElement("div");
          bubble.style.border = "1px solid #ccc";
          bubble.style.borderRadius = "6px";
          bubble.style.padding = "10px";
          bubble.style.marginBottom = "10px";
          if (isError) {
              bubble.style.borderColor = "#a94442";
              bubble.style.color = "#a94442";
          }
          bubble.innerHTML = "<strong>[" + mode + "]</strong> " + text;
          history.appendChild(bubble);
      }

      function askQuestion() {
          var question = document.getElementById("question").value.trim();
          if (!question) {
              return;
          }

          var mode = getSelectedMode();
          var topN = parseInt(document.getElementById("topN").value, 10);

          var askButton = document.getElementById("askButton");
          askButton.disabled = true;

          fetch("/api/ask", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ Question: question, Mode: mode, TopN: topN })
          })
              .then(function (response) {
                  return response.json();
              })
              .then(function (data) {
                  if (data.Success) {
                      appendBubble(data.Mode, data.Answer, false);
                  } else {
                      appendBubble(data.Mode || mode, data.ErrorMessage, true);
                  }
              })
              .catch(function () {
                  appendBubble(mode, "Something went wrong contacting the server.", true);
              })
              .then(function () {
                  askButton.disabled = false;
              });
      }

      document.addEventListener("DOMContentLoaded", function () {
          document.getElementById("askButton").addEventListener("click", askQuestion);
      });
  })();
  ```

  `Views\Home\Index.cshtml` is already registered as a `Content` item (from Task 1) — only its contents changed, no csproj edit needed for it. `Scripts\chat-widget.js` is new: add `<Content Include="Scripts\chat-widget.js" />` to the `Content` `<ItemGroup>` in `AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.csproj` (not required for IIS Express to serve the file locally, but keeps the project correct for a future publish/deploy).

- [ ] **Step 3: Build to verify it compiles**

  Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Git\AdventureWorksProductAdvisor\AdventureWorksProductAdvisor.sln" -p:Configuration=Debug -t:Restore,Build`
  Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

  ```bash
  git add AdventureWorksProductAdvisor/Views/Home/Index.cshtml AdventureWorksProductAdvisor/Scripts/chat-widget.js AdventureWorksProductAdvisor/AdventureWorksProductAdvisor.csproj
  git commit -m "Add chat widget UI wired to POST /api/ask"
  ```

---

## Task 10: End-to-End Verification (Manual)

This task needs your real Azure SQL database and real Azure OpenAI resource, so it's performed by you, not a subagent.

**Files:** none (verification only).

- [ ] **Step 1: Confirm the RBAC role and local credential are working**

  Run: `az account show`
  Expected: shows the account you assigned "Cognitive Services OpenAI User" to (see earlier setup instructions).

- [ ] **Step 2: Run the SQL migration**

  In SSMS, connect to the AdventureWorksLT database and run `sql/AddReviewEmbeddingJsonColumn.sql` (from Task 2) if you haven't already.

- [ ] **Step 3: Fill in your real connection string**

  Confirm `AdventureWorksProductAdvisor\Web.ConnectionStrings.config` (created in Task 1, gitignored) has your real Azure SQL server, database, and credentials.

- [ ] **Step 4: Run the app**

  In Visual Studio, press F5 (or Ctrl+F5) to launch the app via IIS Express.

- [ ] **Step 5: Trigger the embedding backfill**

  With the app running, POST to the admin endpoint once (e.g. via a browser extension, Postman, or `curl`) — for example, in a separate terminal:

  Run: `curl -k -X POST https://localhost:44300/api/admin/backfill-embeddings` (adjust the port to whatever IIS Express shows in the browser address bar after F5)
  Expected: a JSON integer response, `140` (the number of seeded reviews).

- [ ] **Step 6: Ask a real question through the browser**

  On the running page, leave mode on "C# Pipeline," leave Top N at 5, type a product-related question (e.g. "What do people say about the mountain bike?"), click Ask.
  Expected: an answer bubble labeled `[CSharp]` appears with a grounded answer referencing review content within a few seconds.

- [ ] **Step 7: Verify TopN validation**

  Change the Top N field to 11 (bypassing the spinner by typing directly) and ask another question.
  Expected: an error bubble reading "TopN must be between 1 and 10."

- [ ] **Step 8: Verify the Stored Procedure mode's placeholder response**

  Switch the mode radio to "Stored Procedure" and ask a question.
  Expected: an error bubble reading something like "Mode 'StoredProc' is not yet implemented." (Building the real `StoredProcAskService` is slice 2.)

- [ ] **Step 9: Final commit (if any local fixes were needed during verification)**

  If verification surfaced any small fixes, commit them with a message describing what was wrong and why (e.g. `git commit -m "Fix embedding parse for reviews with special characters"`).
