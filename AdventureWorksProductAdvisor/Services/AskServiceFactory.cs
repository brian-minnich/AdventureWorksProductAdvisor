using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using Azure.Core;
using Azure.Identity;
using AdventureWorksProductAdvisor.Data;

namespace AdventureWorksProductAdvisor.Services
{
    public static class AskServiceFactory
    {
        private static readonly HttpClient SharedHttpClient = new HttpClient();
        private static readonly TokenCredential SharedCredential = new DefaultAzureCredential();

        public static IReviewRepository CreateReviewRepository()
        {
            var connectionString = ConfigurationManager.ConnectionStrings["AdventureWorksLT"].ConnectionString;
            return new ReviewRepository(connectionString);
        }

        public static IEmbeddingService CreateEmbeddingService()
        {
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
            return new ReviewEmbeddingBackfillRunner(CreateReviewRepository(), CreateEmbeddingService());
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
