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

                using (var response = await _httpClient.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();

                    var responseJson = await response.Content.ReadAsStringAsync();
                    var parsed = JObject.Parse(responseJson);
                    return parsed["choices"][0]["message"]["content"].ToString();
                }
            }
        }
    }
}
