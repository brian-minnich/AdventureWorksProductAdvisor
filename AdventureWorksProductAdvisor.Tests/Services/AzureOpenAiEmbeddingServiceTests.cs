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
            var canned = "{\"data\":[{\"embedding\":[0.5,0.25,0.125]}]}";
            var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(canned, Encoding.UTF8, "application/json")
            });
            var httpClient = new HttpClient(handler);
            var credential = new FakeTokenCredential("test-token");
            var service = new AzureOpenAiEmbeddingService(
                httpClient, credential, "https://example.openai.azure.com", "text-embedding-3-small", "2024-10-21");

            var result = await service.GetEmbeddingAsync("great product");

            CollectionAssert.AreEqual(new float[] { 0.5f, 0.25f, 0.125f }, result);
            Assert.AreEqual(
                "https://example.openai.azure.com/openai/deployments/text-embedding-3-small/embeddings?api-version=2024-10-21",
                handler.LastRequest.RequestUri.ToString());
            Assert.AreEqual("Bearer", handler.LastRequest.Headers.Authorization.Scheme);
            Assert.AreEqual("test-token", handler.LastRequest.Headers.Authorization.Parameter);
        }
    }
}
