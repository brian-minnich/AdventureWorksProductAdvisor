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
