using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Newtonsoft.Json.Linq;

namespace AdventureWorksProductAdvisor.Services
{
    // Shared REST-call plumbing for both Azure OpenAI services
    // (AzureOpenAiEmbeddingService and AzureOpenAiChatCompletionService):
    // acquire a bearer token, POST a JSON payload, parse the JSON response.
    // Each service still owns its own URL shape and payload/response shape -
    // this only exists to avoid duplicating the auth/HTTP mechanics in both places.
    internal static class AzureOpenAiRequestSender
    {
        public static async Task<JObject> SendAsync(
            HttpClient httpClient, TokenCredential credential, string url, JObject payload)
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }),
                CancellationToken.None);

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

                using (var response = await httpClient.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return JObject.Parse(responseJson);
                }
            }
        }
    }
}
