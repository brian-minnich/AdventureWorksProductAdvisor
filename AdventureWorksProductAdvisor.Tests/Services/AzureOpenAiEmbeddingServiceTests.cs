using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Services;
using AdventureWorksProductAdvisor.Tests.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdventureWorksProductAdvisor.Tests.Services
{
    [TestClass]
    public class AzureOpenAiEmbeddingServiceTests
    {
        [TestMethod]
        public async Task GetEmbeddingAsync_SendsBearerTokenAndCorrectUrl_ReturnsParsedEmbedding()
        {
            // This is a fake Azure OpenAI response - the real service would
            // return something like this, but we hand-write it here so the
            // test doesn't depend on the real Azure OpenAI endpoint being
            // reachable, configured, or free to call.
            var canned = "{\"data\":[{\"embedding\":[0.5,0.25,0.125]}]}";

            // FakeHttpMessageHandler intercepts every HttpClient.SendAsync
            // call and returns this canned response instead of making a real
            // network call. It also records the request it received, which
            // is checked further down.
            var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(canned, Encoding.UTF8, "application/json")
            });
            var httpClient = new HttpClient(handler);

            // FakeTokenCredential stands in for DefaultAzureCredential - it
            // just returns this fixed string instead of doing a real Azure
            // AD login.
            var credential = new FakeTokenCredential("test-token");

            // This is the REAL production class - nothing about it is
            // mocked. Only its two collaborators (HttpClient, TokenCredential)
            // are fakes, so its actual URL-building/parsing logic still runs for real.
            var service = new AzureOpenAiEmbeddingService(
                httpClient, credential, "https://example.openai.azure.com", "text-embedding-3-small", "2024-10-21");

            var result = await service.GetEmbeddingAsync("great product");

            // Check 1: did it correctly parse the canned response into numbers?
            CollectionAssert.AreEqual(new float[] { 0.5f, 0.25f, 0.125f }, result);

            // Check 2-4: did it build the RIGHT request? handler.LastRequest
            // is the actual HttpRequestMessage the service constructed and
            // tried to send - inspecting it here catches bugs like a wrong
            // URL, a missing auth header, or the wrong deployment name,
            // which a real call to Azure might otherwise mask or turn into a
            // confusing runtime error.
            Assert.AreEqual(
                "https://example.openai.azure.com/openai/deployments/text-embedding-3-small/embeddings?api-version=2024-10-21",
                handler.LastRequest.RequestUri.ToString());
            Assert.AreEqual("Bearer", handler.LastRequest.Headers.Authorization.Scheme);
            Assert.AreEqual("test-token", handler.LastRequest.Headers.Authorization.Parameter);
        }
    }
}
