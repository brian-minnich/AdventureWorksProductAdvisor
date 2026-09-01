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

                using (var response = await _httpClient.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();

                    var responseJson = await response.Content.ReadAsStringAsync();
                    var parsed = JObject.Parse(responseJson);
                    return parsed["data"][0]["embedding"].ToObject<float[]>();
                }
            }
        }
    }
}
