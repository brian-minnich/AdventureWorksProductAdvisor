using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AdventureWorksProductAdvisor.Services;
using AdventureWorksProductAdvisor.Tests.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace AdventureWorksProductAdvisor.Tests.Services
{
    [TestClass]
    public class AzureOpenAiChatCompletionServiceTests
    {
        [TestMethod]
        public async Task GetCompletionAsync_SendsSystemAndUserMessages_ReturnsParsedContent()
        {
            var canned = "{\"choices\":[{\"message\":{\"content\":\"Test answer\"}}]}";
            var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(canned, Encoding.UTF8, "application/json")
            });
            var httpClient = new HttpClient(handler);
            var credential = new FakeTokenCredential("test-token");
            var service = new AzureOpenAiChatCompletionService(
                httpClient, credential, "https://example.openai.azure.com", "gpt-5.4-mini", "2024-10-21");

            var result = await service.GetCompletionAsync(
                "Product reviews: ...\n\nCustomer question: What is the best bike?", 500);

            Assert.AreEqual("Test answer", result);
            Assert.AreEqual(
                "https://example.openai.azure.com/openai/deployments/gpt-5.4-mini/chat/completions?api-version=2024-10-21",
                handler.LastRequest.RequestUri.ToString());
            Assert.AreEqual("Bearer", handler.LastRequest.Headers.Authorization.Scheme);
            Assert.AreEqual("test-token", handler.LastRequest.Headers.Authorization.Parameter);

            var sentPayload = JObject.Parse(handler.LastRequestBody);
            var messages = (JArray)sentPayload["messages"];
            Assert.AreEqual(2, messages.Count);
            Assert.AreEqual("system", messages[0]["role"].ToString());
            Assert.AreEqual("user", messages[1]["role"].ToString());
            StringAssert.Contains(messages[1]["content"].ToString(), "What is the best bike?");
            Assert.AreEqual(500, sentPayload["max_completion_tokens"].Value<int>());
        }
    }
}
