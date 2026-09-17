using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using Azure.Core;
using Azure.Identity;
using AdventureWorksProductAdvisor.Data;

namespace AdventureWorksProductAdvisor.Services
{
    // This is the "composition root" - the one place in the app that knows
    // how to build the real, production versions of everything (real DB
    // connection, real Azure OpenAI calls). Controllers ask this factory for
    // what they need instead of constructing dependencies themselves, which
    // keeps the controllers small and lets tests substitute their own fakes
    // instead of ever calling into this class.
    public static class AskServiceFactory
    {
        // HttpClient and DefaultAzureCredential are both meant to be reused,
        // not recreated per call. ASP.NET creates a new AskController for
        // every single request, so if these were "new"-ed up inside
        // CreateEmbeddingService()/CreateChatCompletionService() below, every
        // question would pay the cost of a fresh credential (which re-checks
        // several ways of logging in) and a fresh HttpClient (which opens its
        // own connection pool) from scratch. Making them static means they're
        // created once, the first time this class is touched, and reused for
        // the lifetime of the application.
        private static readonly HttpClient SharedHttpClient = new HttpClient();

        // TenantId is required here because the signed-in account
        // (bjminnich@comcast.net) is a B2B guest in the Azure AD tenant that
        // owns this subscription, not a native member - without an explicit
        // TenantId, DefaultAzureCredential can't infer which directory to
        // request a token from and silently resolves to the wrong one,
        // producing a token Azure OpenAI rejects with 401 even though the
        // "Cognitive Services OpenAI User" role assignment is correct.
        private static readonly TokenCredential SharedCredential = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions { TenantId = ConfigurationManager.AppSettings["AzureAdTenantId"] });

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

        public static IEmbeddingService CreateEmbeddingService()
        {
            // All the Azure-specific values (endpoint, which deployment,
            // which API version) come from Web.config's <appSettings>, not
            // from anywhere in the request - so changing models/regions is a
            // config change, not a code change.
            return new AzureOpenAiEmbeddingService(
                SharedHttpClient,
                SharedCredential,
                ConfigurationManager.AppSettings["AzureOpenAiEndpoint"],
                ConfigurationManager.AppSettings["AzureOpenAiEmbeddingDeployment"],
                ConfigurationManager.AppSettings["AzureOpenAiApiVersion"]);
        }

        public static IChatCompletionService CreateChatCompletionService()
        {
            return new AzureOpenAiChatCompletionService(
                SharedHttpClient,
                SharedCredential,
                ConfigurationManager.AppSettings["AzureOpenAiEndpoint"],
                ConfigurationManager.AppSettings["AzureOpenAiChatDeployment"],
                ConfigurationManager.AppSettings["AzureOpenAiApiVersion"]);
        }

        // Same fallback reasoning as AskController's GetMaxCompletionTokensFromConfig:
        // a bad/missing config value falls back to CSharpAskService's own
        // default (see its constructor) rather than crashing pipeline
        // construction, which happens on every request.
        private static double GetMinSimilarityScoreFromConfig()
        {
            double value;
            // InvariantCulture, not the server's current culture - Web.config
            // always writes this with a period ("0.35"), but double.TryParse
            // without an explicit culture reads the decimal separator from
            // the OS/thread culture. On a server whose culture uses "," for
            // that (e.g. de-DE), a period is instead read as a thousands
            // separator, so "0.35" would silently parse as 35 - not fail,
            // just be wrong - and a threshold of 35 rejects every question,
            // since cosine similarity never exceeds 1.
            return double.TryParse(
                ConfigurationManager.AppSettings["MinSimilarityScore"],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value) ? value : 0.35;
        }

        public static ReviewEmbeddingBackfillRunner CreateBackfillRunner()
        {
            // Reuses the same Create* methods above rather than building a
            // repository/embedding service a second, separate way - so
            // there's exactly one place that knows how to build each piece.
            return new ReviewEmbeddingBackfillRunner(CreateReviewRepository(), CreateEmbeddingService());
        }

        public static IReadOnlyDictionary<string, IAskService> CreatePipelines()
        {
            // Both pipelines get the same threshold from the same config
            // read, so the off-topic-question guardrail behaves identically
            // regardless of which one answers.
            var minSimilarityScore = GetMinSimilarityScoreFromConfig();

            var csharpAskService = new CSharpAskService(
                CreateReviewRepository(), CreateEmbeddingService(), CreateChatCompletionService(), minSimilarityScore);

            var connectionString = ConfigurationManager.ConnectionStrings["AdventureWorksLT"].ConnectionString;
            var storedProcAskService = new StoredProcAskService(connectionString, minSimilarityScore);

            // This dictionary is what AskController.Post looks a Mode up in.
            // Adding a pipeline is just adding one more entry here -
            // AskController itself doesn't need to change.
            return new Dictionary<string, IAskService>
            {
                ["CSharp"] = csharpAskService,
                ["StoredProc"] = storedProcAskService
            };
        }
    }
}
