using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Newtonsoft.Json.Linq;

namespace AdventureWorksProductAdvisor.Services
{
    // Same shape as AzureOpenAiEmbeddingService (same auth pattern, same
    // dependency-injected HttpClient/TokenCredential for testability), but
    // calls the chat completions endpoint instead of embeddings, and sends a
    // "messages" array (system instructions + the user's actual prompt)
    // rather than a single string.
    public class AzureOpenAiChatCompletionService : IChatCompletionService
    {
        // Kept identical to dbo.AskProductQuestion's system message (sql/dbo.AskProductQuestion.sql)
        // so both pipelines answer under the same instructions for a fair comparison.
        // If this ever needs to change, the SQL stored procedure's system
        // message must be updated to match, word for word, or the two
        // pipelines are no longer a fair comparison.
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

        // "prompt" here is the augmented prompt CSharpAskService.BuildPrompt
        // constructed - the retrieved reviews plus the customer's question.
        // This method's only job is wrapping that in the request shape Azure
        // OpenAI expects and unwrapping its response.
        public async Task<string> GetCompletionAsync(string prompt, int maxCompletionTokens)
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }),
                CancellationToken.None);

            var url = $"{_endpoint}/openai/deployments/{_deploymentName}/chat/completions?api-version={_apiVersion}";

            // Chat completions expect a list of messages with roles, not a
            // single string: "system" sets the model's behavior/instructions,
            // "user" is the actual question+context being asked about.
            var payload = new JObject
            {
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = SystemPrompt },
                    new JObject { ["role"] = "user", ["content"] = prompt }
                },
                // max_completion_tokens caps how much the model can generate -
                // this is the actual cost guard; the value comes all the way
                // from Web.config via AskController, never from the client.
                ["max_completion_tokens"] = maxCompletionTokens,
                // Lower temperature = more focused/deterministic answers,
                // matching the stored procedure's setting for a fair comparison.
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
                    // Azure's chat completions response nests the actual
                    // answer text at choices[0].message.content.
                    return parsed["choices"][0]["message"]["content"].ToString();
                }
            }
        }
    }
}
