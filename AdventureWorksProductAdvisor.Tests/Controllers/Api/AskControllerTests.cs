using System;
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
    // These tests never touch AskServiceFactory, a real database, or Azure -
    // they use AskController's testable constructor (pipelines + token cap
    // passed in directly) with a Moq mock standing in for IAskService.
    [TestClass]
    public class AskControllerTests
    {
        [TestMethod]
        public async Task Post_KnownMode_InvokesPipelineAndReturnsAnswer()
        {
            // Moq.Setup says "when AskAsync is called with exactly these
            // arguments, return this canned result" - if the controller
            // called it with different arguments, this mock wouldn't match
            // and the test would fail with a null result instead.
            var askService = new Mock<IAskService>();
            askService.Setup(s => s.AskAsync("What is the best product?", 500, 5))
                .ReturnsAsync(new AskServiceResult { Success = true, Answer = "It's great." });

            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var controller = CreateController(pipelines);

            var result = await controller.Post(new AskRequest { Question = "What is the best product?", Mode = "CSharp", TopN = 5 });

            // ApiController actions return IHttpActionResult, not the
            // response object directly - casting to OkNegotiatedContentResult<T>
            // is how a test gets at the actual AskResponse that was returned.
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
            // Only "CSharp" is registered - "StoredProc" below isn't in the
            // dictionary at all, exercising the not-yet-implemented branch.
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var controller = CreateController(pipelines);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "StoredProc", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsNotNull(okResult);
            Assert.IsFalse(okResult.Content.Success);
            StringAssert.Contains(okResult.Content.ErrorMessage, "not yet implemented");
            // The important assertion: AskAsync was never called at all for
            // an unrecognized mode - not just that the error message looks right.
            askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public async Task Post_TopNOutOfRange_ReturnsValidationErrorWithoutInvokingPipeline()
        {
            var askService = new Mock<IAskService>();
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var controller = CreateController(pipelines);

            // 11 is outside the allowed 1-10 range.
            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 11 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsNotNull(okResult);
            Assert.IsFalse(okResult.Content.Success);
            StringAssert.Contains(okResult.Content.ErrorMessage, "between 1 and 10");
            // Confirms the TopN check happens before any pipeline is ever touched.
            askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public async Task Post_QuestionTooLong_ReturnsValidationErrorWithoutInvokingPipeline()
        {
            var askService = new Mock<IAskService>();
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var controller = CreateController(pipelines);

            // dbo.CallLog.Question is NVARCHAR(1000) - 1001 characters is
            // one over the limit.
            var tooLongQuestion = new string('a', 1001);
            var result = await controller.Post(new AskRequest { Question = tooLongQuestion, Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsNotNull(okResult);
            Assert.IsFalse(okResult.Content.Success);
            StringAssert.Contains(okResult.Content.ErrorMessage, "1000 characters or fewer");
            // Confirms the length check happens before any pipeline is ever touched.
            askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public async Task Post_DailyLimitReached_ReturnsFriendlyErrorWithoutInvokingPipelineOrLogging()
        {
            var askService = new Mock<IAskService>();
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var callLogService = new Mock<ICallLogService>();
            callLogService.Setup(s => s.GetTodaysCallCountAsync()).ReturnsAsync(5);
            var controller = CreateController(pipelines, callLogService.Object, dailyCallLimit: 5);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsNotNull(okResult);
            Assert.IsFalse(okResult.Content.Success);
            StringAssert.Contains(okResult.Content.ErrorMessage, "Daily call limit reached");
            askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            callLogService.Verify(s => s.LogCallAsync(It.IsAny<CallLogEntry>()), Times.Never);
        }

        [TestMethod]
        public async Task Post_UnderDailyLimit_LogsSuccessfulCall()
        {
            var askService = new Mock<IAskService>();
            askService.Setup(s => s.AskAsync("What is the best product?", 500, 5))
                .ReturnsAsync(new AskServiceResult { Success = true, Answer = "It's great." });
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var callLogService = new Mock<ICallLogService>();
            callLogService.Setup(s => s.GetTodaysCallCountAsync()).ReturnsAsync(0);
            CallLogEntry loggedEntry = null;
            callLogService.Setup(s => s.LogCallAsync(It.IsAny<CallLogEntry>()))
                .Callback<CallLogEntry>(e => loggedEntry = e)
                .Returns(Task.CompletedTask);
            var controller = CreateController(pipelines, callLogService.Object);

            var result = await controller.Post(new AskRequest { Question = "What is the best product?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsTrue(okResult.Content.Success);
            Assert.IsNotNull(loggedEntry);
            Assert.AreEqual("CSharp", loggedEntry.Mode);
            Assert.AreEqual("What is the best product?", loggedEntry.Question);
            Assert.AreEqual(500, loggedEntry.MaxCompletionTokens);
            Assert.IsTrue(loggedEntry.Success);
            Assert.IsNull(loggedEntry.ErrorMessage);
        }

        [TestMethod]
        public async Task Post_PipelineThrows_LogsFailureAndReturnsGenericError()
        {
            var askService = new Mock<IAskService>();
            askService.Setup(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("SQL timeout"));
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var callLogService = new Mock<ICallLogService>();
            callLogService.Setup(s => s.GetTodaysCallCountAsync()).ReturnsAsync(0);
            CallLogEntry loggedEntry = null;
            callLogService.Setup(s => s.LogCallAsync(It.IsAny<CallLogEntry>()))
                .Callback<CallLogEntry>(e => loggedEntry = e)
                .Returns(Task.CompletedTask);
            var controller = CreateController(pipelines, callLogService.Object);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsFalse(okResult.Content.Success);
            StringAssert.Contains(okResult.Content.ErrorMessage, "An error occurred");
            Assert.IsNotNull(loggedEntry);
            Assert.IsFalse(loggedEntry.Success);
            Assert.AreEqual("SQL timeout", loggedEntry.ErrorMessage);
        }

        [TestMethod]
        public async Task Post_LogCallAsyncThrows_StillReturnsRealAnswer()
        {
            var askService = new Mock<IAskService>();
            askService.Setup(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new AskServiceResult { Success = true, Answer = "It's great." });
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var callLogService = new Mock<ICallLogService>();
            callLogService.Setup(s => s.GetTodaysCallCountAsync()).ReturnsAsync(0);
            callLogService.Setup(s => s.LogCallAsync(It.IsAny<CallLogEntry>()))
                .ThrowsAsync(new InvalidOperationException("log write failed"));
            var controller = CreateController(pipelines, callLogService.Object);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsNotNull(okResult);
            Assert.IsTrue(okResult.Content.Success);
            Assert.AreEqual("It's great.", okResult.Content.Answer);
        }

        [TestMethod]
        public async Task Post_CallCountCheckThrows_FailsOpenAndInvokesPipeline()
        {
            var askService = new Mock<IAskService>();
            askService.Setup(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new AskServiceResult { Success = true, Answer = "It's great." });
            var pipelines = new Dictionary<string, IAskService> { ["CSharp"] = askService.Object };
            var callLogService = new Mock<ICallLogService>();
            callLogService.Setup(s => s.GetTodaysCallCountAsync()).ThrowsAsync(new InvalidOperationException("CallLog unreachable"));
            callLogService.Setup(s => s.LogCallAsync(It.IsAny<CallLogEntry>())).Returns(Task.CompletedTask);
            var controller = CreateController(pipelines, callLogService.Object);

            var result = await controller.Post(new AskRequest { Question = "Anything?", Mode = "CSharp", TopN = 5 });

            var okResult = result as OkNegotiatedContentResult<AskResponse>;
            Assert.IsTrue(okResult.Content.Success);
            askService.Verify(s => s.AskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }

        // Building an ApiController for a unit test needs a bit of manual
        // setup - Configuration/Request aren't populated automatically
        // outside of a real HTTP pipeline, and calling Ok(...) inside the
        // controller throws without them. callLogService defaults to a mock
        // that reports 0 calls today and accepts any LogCallAsync call, so
        // existing tests that don't care about logging/the cap keep working
        // unchanged.
        private static AskController CreateController(
            IReadOnlyDictionary<string, IAskService> pipelines,
            ICallLogService callLogService = null,
            int dailyCallLimit = 200)
        {
            if (callLogService == null)
            {
                var defaultMock = new Mock<ICallLogService>();
                defaultMock.Setup(s => s.GetTodaysCallCountAsync()).ReturnsAsync(0);
                defaultMock.Setup(s => s.LogCallAsync(It.IsAny<CallLogEntry>())).Returns(Task.CompletedTask);
                callLogService = defaultMock.Object;
            }

            var controller = new AskController(pipelines, 500, callLogService, dailyCallLimit);
            controller.Configuration = new HttpConfiguration();
            controller.Request = new HttpRequestMessage();
            return controller;
        }
    }
}
