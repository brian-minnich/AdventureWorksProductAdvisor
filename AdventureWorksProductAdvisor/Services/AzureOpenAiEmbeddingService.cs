using System.Net.Http;
using Azure.Core;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace AdventureWorksProductAdvisor.Services
{
    // Talks to Azure OpenAI's embeddings endpoint to turn text into a vector
    // of numbers. This is the only class in the app that knows the actual
    // shape of that REST call (URL, headers, JSON) - IEmbeddingService, the
    // interface everything else depends on, exposes none of that.
    public class AzureOpenAiEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly TokenCredential _credential;
        private readonly string _endpoint;
        private readonly string _deploymentName;
        private readonly string _apiVersion;

        // HttpClient and TokenCredential are passed in rather than created
        // here (this is "dependency injection"). That's what lets a test
        // substitute a fake HttpClient (backed by FakeHttpMessageHandler)
        // and a fake TokenCredential (FakeTokenCredential) instead of making
        // a real network call or a real Azure AD login - see
        // AzureOpenAiEmbeddingServiceTests.cs.
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
            // Azure OpenAI's URL shape: {endpoint}/openai/deployments/{deployment}/embeddings?api-version={version}
            var url = $"{_endpoint}/openai/deployments/{_deploymentName}/embeddings?api-version={_apiVersion}";
            // The request body Azure OpenAI's embeddings endpoint expects: {"input": "<text>"}
            var payload = new JObject { ["input"] = text };

            // AzureOpenAiRequestSender handles token acquisition, the actual
            // HTTP POST, and response-status checking - shared with
            // AzureOpenAiChatCompletionService so that plumbing exists in one place.
            var parsed = await AzureOpenAiRequestSender.SendAsync(_httpClient, _credential, url, payload);

            // Azure's response shape is {"data":[{"embedding":[...]}]} -
            // this line reaches in and pulls out just the array of numbers.
            return parsed["data"][0]["embedding"].ToObject<float[]>();
        }
    }
}
