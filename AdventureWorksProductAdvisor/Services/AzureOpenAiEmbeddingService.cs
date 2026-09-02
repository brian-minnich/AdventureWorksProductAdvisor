using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Newtonsoft.Json.Linq;

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
            // Ask for a token scoped to Cognitive Services (the Azure
            // service family Azure OpenAI runs under). In production this
            // goes through DefaultAzureCredential, which tries several login
            // methods in turn (managed identity, Azure CLI, Visual Studio,
            // etc.) - notably, no API key is used anywhere in this app.
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }),
                CancellationToken.None);

            // Azure OpenAI's URL shape: {endpoint}/openai/deployments/{deployment}/embeddings?api-version={version}
            var url = $"{_endpoint}/openai/deployments/{_deploymentName}/embeddings?api-version={_apiVersion}";
            // The request body Azure OpenAI's embeddings endpoint expects: {"input": "<text>"}
            var requestJson = new JObject { ["input"] = text }.ToString();

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                // Bearer token in the Authorization header - this is how the
                // token acquired above actually authenticates the call.
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using (var response = await _httpClient.SendAsync(request))
                {
                    // Throws if Azure returned a non-2xx status (e.g. 401 for
                    // a bad token, 429 for rate limiting) rather than letting
                    // bad data flow silently into the parsing below.
                    response.EnsureSuccessStatusCode();

                    var responseJson = await response.Content.ReadAsStringAsync();
                    var parsed = JObject.Parse(responseJson);
                    // Azure's response shape is {"data":[{"embedding":[...]}]} -
                    // this line reaches in and pulls out just the array of numbers.
                    return parsed["data"][0]["embedding"].ToObject<float[]>();
                }
            }
        }
    }
}
