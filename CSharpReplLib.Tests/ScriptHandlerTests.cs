using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CSharpReplLib.Tests
{
    public class ScriptHandlerTests
    {
        [Fact]
        public async Task InitScriptTest()
        {
            var scriptHandler = new ScriptHandler()
                .AddReferences(typeof(string).Assembly)
                .AddUsings("System")
                .AddGlobals(("TestString", "TestStringValue"));

            var initSucceeded = await scriptHandler.InitScript();
            Assert.True(initSucceeded);
        }

        [Fact]
        public async Task ExecuteCodeTest()
        {
            var scriptHandler = new ScriptHandler();

            List<ScriptHandler.ScriptResult> results = new List<ScriptHandler.ScriptResult>();
            scriptHandler.ScriptResultReceived += (sender, args) =>
            {
                results.Add(args);
            };

            var initSucceeded = await scriptHandler.InitScript();
            Assert.True(initSucceeded);

            var codeSucceeded = await scriptHandler.ExecuteCode("1 + 1");

            Assert.True(codeSucceeded);

            var result = results.FirstOrDefault();
            Assert.False(result.IsError);
            Assert.False(result.IsCancelled);
            Assert.Equal("2", result.Result);
            Assert.Equal(2, result.ReturnedValue);
        }

        [Fact]
        public async Task InitScriptCancellationTest()
        {
            var scriptHandler = new ScriptHandler()
                .AddReferences(typeof(string).Assembly)
                .AddUsings("System")
                .AddGlobals(("TestString", "TestStringValue"));

            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            tokenSource.Cancel();

            var initSucceeded = await scriptHandler.InitScript(token);

            Assert.False(initSucceeded);
        }

        [Fact]
        public async Task ExecuteCodeCancellationTest()
        {
            var scriptHandler = new ScriptHandler();

            List<ScriptHandler.ScriptResult> results = new List<ScriptHandler.ScriptResult>();
            scriptHandler.ScriptResultReceived += (sender, args) =>
            {
                results.Add(args);
            };

            var initSucceeded = await scriptHandler.InitScript();
            Assert.True(initSucceeded);

            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            tokenSource.Cancel();
            var codeSucceeded = await scriptHandler.ExecuteCode("1 + 1", token);

            Assert.False(codeSucceeded);
            Assert.True(results.FirstOrDefault().IsCancelled);
        }
    }
}
