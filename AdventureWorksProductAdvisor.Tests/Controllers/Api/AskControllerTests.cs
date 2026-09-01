using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using AdventureWorksProductAdvisor.Controllers.Api;
using AdventureWorksProductAdvisor.Models;
using AdventureWorksProductAdvisor.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AdventureWorksProductAdvisor.Tests.Controllers.Api
{
    [TestClass]
    public class AskControllerTests
    {
        [TestMethod]
        public async Task Post_KnownMode_InvokesPipelineAndReturnsAnswer()
        {
            var askService = new Mock<IAskService>();
            askService.Setup(s => s.AskAsync("What is the best product?", 500, 5))
                .ReturnsAsync(new AskServiceResult { Success = true, Answer = "It's great." });

            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var controller = CreateController(pipelines);

            var result = await controller.Post(new AskRequest { Question = "What is the best product?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsNotNull(okResult);
            Assert.IsTrue(okResult.Content.Success);
            Assert.AreEqual("It's great.", okResult.Content.Answer);
            Assert.AreEqual("CSharp", okResult.Content.Mode);
        }

        [TestMethod]
        public async Task Post_UnknownMode_ReturnsNotImplementedMessageWithoutInvokingAnyPipeline()
        {
            var askService = new Mock<IAskService>();
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var controller = CreateController(pipelines);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "StoredProc", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsNotNull(okResult);
            Assert.IsFalse(okResult.Content.Success);
            StringAssert.Contains(okResult.Content.ErrorMessage, "not yet implemented");
            askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public async Task Post_TopNOutOfRange_ReturnsValidationErrorWithoutInvokingPipeline()
        {
            var askService = new Mock<IAskService>();
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var controller = CreateController(pipelines);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 11 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsNotNull(okResult);
            Assert.IsFalse(okResult.Content.Success);
            StringAssert.Contains(okResult.Content.ErrorMessage, "between 1 and 10");
            askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        private static AskController CreateController(IReadOnlyDictionary<string, IAskService> pipelines)
        {
            var controller = new AskController(pipelines, 500);
            controller.Configuration = new HttpConfiguration();
            controller.Request = new HttpRequestMessage();
            return controller;
        }
    }
}
