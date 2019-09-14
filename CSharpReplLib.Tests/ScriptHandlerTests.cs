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

		[Fact]
		public async Task ResetStateTest()
		{
			var scriptHandler = new ScriptHandler();

			List<ScriptHandler.ScriptResult> results = new List<ScriptHandler.ScriptResult>();
			scriptHandler.ScriptResultReceived += (sender, args) =>
			{
				results.Add(args);
			};

			var initSucceeded = await scriptHandler.InitScript();
			Assert.True(initSucceeded);

			string expected = "Hello world";

			await scriptHandler.ExecuteCode($"var a = \"{expected}\";");
			await scriptHandler.ExecuteCode("a");

			var returnedValue = results.Last().ReturnedValue as string;
			Assert.Equal(expected, returnedValue);

			await scriptHandler.ResetState();
			await scriptHandler.ExecuteCode("a");

			Assert.True(results.Last().IsError);
		}

		[Fact]
		public async Task GenerateTest()
		{
			var scriptHandler = new ScriptHandler();

			var initSucceeded = await scriptHandler.InitScript();
			Assert.True(initSucceeded);

			var generatedString = await scriptHandler.Generate<string>("\"test\"");
			Assert.Equal("test", generatedString);

			var generatedFunc = await scriptHandler.Generate<Func<int, string>>("i => i.ToString()");
			Assert.Equal("5", generatedFunc(5));
		}

		[Fact]
		public async Task ExecuteCodeWithReturningType()
		{
			var scriptHandler = new ScriptHandler();

			List<ScriptHandler.ScriptResult> results = new List<ScriptHandler.ScriptResult>();
			scriptHandler.ScriptResultReceived += (sender, args) =>
			{
				results.Add(args);
			};

			var initSucceeded = await scriptHandler.InitScript();
			Assert.True(initSucceeded);

			await scriptHandler.ExecuteCode<int>("1 + 1");

			Assert.Equal(2, results.Last().ReturnedValue);

			await scriptHandler.ExecuteCode<Func<int, int>>("i => i * 4");

			Assert.Equal(8, ((Func<int, int>)results.Last().ReturnedValue)(2));
		}

		[Fact]
		public async Task CompletionTest()
		{
			var scriptHandler = new ScriptHandler()
				.AddUsings("System")
				.AddReferences(typeof(object).Assembly);

			await scriptHandler.InitScript();

			var completion = await scriptHandler.GetCompletion("int.");
			Assert.NotNull(completion);
			Assert.Contains("Parse", completion.Items.Select(item => item.DisplayText));

			await scriptHandler.ExecuteCode("string a = string.Empty;");

			completion = await scriptHandler.GetCompletion("a.");
			Assert.NotNull(completion);
			Assert.Contains("ToLower", completion.Items.Select(item => item.DisplayText));

			string code =
@"
{
	string b = ""Some multiline code"";
	int c = b.Length;
	var d = c.ToString();
	var e = d.
}";

			var carretPos = code.IndexOf("d.") + 2;
			completion = await scriptHandler.GetCompletion(code, carretPos);
			Assert.NotNull(completion);
			Assert.Contains("Split", completion.Items.Select(item => item.DisplayText));
		}
	}
}
