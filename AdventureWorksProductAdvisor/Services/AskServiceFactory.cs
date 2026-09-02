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
        private static readonly TokenCredential SharedCredential = new DefaultAzureCredential();

        public static IReviewRepository CreateReviewRepository()
        {
            // The connection string lives in Web.ConnectionStrings.config,
            // which is gitignored - see Web.config's
            // <connectionStrings configSource="..."> for how it's wired in.
            var connectionString = ConfigurationManager.ConnectionStrings["AdventureWorksLT"].ConnectionString;
            return new ReviewRepository(connectionString);
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

        public static ReviewEmbeddingBackfillRunner CreateBackfillRunner()
        {
            // Reuses the same Create* methods above rather than building a
            // repository/embedding service a second, separate way - so
            // there's exactly one place that knows how to build each piece.
            return new ReviewEmbeddingBackfillRunner(CreateReviewRepository(), CreateEmbeddingService());
        }

        public static IReadOnlyDictionary<string, IAskService> CreatePipelines()
        {
            var csharpAskService = new CSharpAskService(
                CreateReviewRepository(), CreateEmbeddingService(), CreateChatCompletionService());

            // This dictionary is what AskController.Post looks a Mode up in.
            // Adding a second pipeline later (e.g. "StoredProc") means adding
            // one more entry here - AskController itself doesn't need to change.
            return new Dictionary<string, IAskService>
            {
                ["CSharp"] = csharpAskService
            };
        }
    }
}
