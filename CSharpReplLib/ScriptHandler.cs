using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.CodeAnalysis.Text;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace CSharpReplLib
{
    public class ScriptHandler
    {
        public class TextContainer : SourceTextContainer
        {
			private bool _isWritingNewLine = false;
            SourceText _text;
            public int Length => _text.Length;
            public override SourceText CurrentText => _text;

            public override event EventHandler<TextChangeEventArgs> TextChanged;

            public TextContainer(string text)
            {
                _text = SourceText.From(text);
            }

			public void AddLine(string text)
			{
				//_isWritingNewLine = false;
				string oldText = _text.ToString();
				StringBuilder currentText = new StringBuilder(oldText);
				if (_isWritingNewLine)
				{
					var lastLine = _text.Lines.Last();
					currentText.Remove(lastLine.Start, lastLine.SpanIncludingLineBreak.End - lastLine.Start);
				}

				currentText.AppendLine(text);
				var previousText = _text;
				_text = SourceText.From(currentText.ToString());
				TextChanged?.Invoke(this, new TextChangeEventArgs(previousText, _text, _text.GetChangeRanges(previousText)));
			}

            public void SetCurrentLine(string text)
            {
				var oldText = _text.ToString();
				StringBuilder currentText = new StringBuilder(oldText);

				if (_isWritingNewLine)
				{
					var lastLine = _text.Lines.Last();
					currentText.Remove(lastLine.Start, lastLine.SpanIncludingLineBreak.End - lastLine.Start);
				}

				_isWritingNewLine = true;
				currentText.Append(text);

				var previousText = _text;
				_text = SourceText.From(currentText.ToString());
				TextChanged?.Invoke(this, new TextChangeEventArgs(previousText, _text, _text.GetChangeRanges(previousText)));
			}
        }


        public struct ScriptResult
        {
            public string Result { get; }
            public object ReturnedValue { get; }
            public bool IsError { get; }
            public bool IsCancelled { get; }

			public ScriptResult(string result = null, object returnedValue = null, bool isError = false, bool isCancelled = false)
			{
				Result = result;
				ReturnedValue = returnedValue;
				IsError = isError;
				IsCancelled = isCancelled;
			}
        }

        public struct ScriptRequest
        {
            public string Script { get; }
            public IScriptWriter Writer { get; }

			public ScriptRequest(string script = null, IScriptWriter writer = null)
			{
				Script = script;
				Writer = writer;
			}
        }

        public List<ScriptResult> Results = new List<ScriptResult>();
        public List<ScriptRequest> Requests = new List<ScriptRequest>();

        public event EventHandler<ScriptResult> ScriptResultReceived;
        public event EventHandler<ScriptRequest> ScriptExecuted;

        internal object _lockReferences = new object();
        internal readonly List<Assembly> _references = new List<Assembly>();

        internal object _lockUsings = new object();
        internal readonly List<string> _usings = new List<string>();

        internal readonly Dictionary<string, string> _nugets = new Dictionary<string, string>();

        internal object _lockGlobals = new object();
        internal readonly Dictionary<string, object> _globals = new Dictionary<string, object>();

        internal ScriptState<object> _scriptState;
		private Script<object> _script;
		private object _globalInstances;
        internal AsyncLock _scriptStateLock = new AsyncLock();
        

        private Func<Func<Task<bool>>, Task<bool>> _executionContext = null;

        private TextContainer _textContainer;
		private ProjectInfo _scriptProjectInfo;
		private MefHostServices _mefHost;


		public ScriptHandler(Func<Func<Task<bool>>, Task<bool>> executionContext = null)
        {
            _executionContext = executionContext;
        }

        public async Task<bool> InitScript(CancellationToken token = default)
        {
            using (await _scriptStateLock.LockAsync(token))
            {
                if (_scriptState != null)
                    return true;

                try
                {
                    var options = ScriptOptions.Default
                         .WithReferences(_references.ToArrayLocked(_lockReferences))
                         .WithImports(_usings.ToArrayLocked(_lockUsings));

                    var globals = _globals.ToDictionaryLocked(_lockGlobals);

					if (globals.Any())
					{
						var createGlobalsScript = CSharpScript.Create(CreateGlobalsType(), options);
						var image = createGlobalsScript.GetCompilation();

						var stream = new MemoryStream();
						var result = image.Emit(stream, cancellationToken: token);

						if (!result.Success)
						{
							var scriptResult = new ScriptResult
							(
								result : string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())),
								isError : true
							);

							Results.Add(scriptResult);
							ScriptResultReceived?.Invoke(this, scriptResult);

							return false;
						}

						var imageArray = ImmutableArray.Create(stream.ToArray());

						var portableReference = MetadataReference.CreateFromImage(imageArray);

						var libAssembly = Assembly.Load(imageArray.ToArray());
						var globalsType = libAssembly.GetTypes().FirstOrDefault(t => t.Name == "ScriptGlobals");
						_globalInstances = Activator.CreateInstance(globalsType);

						foreach (var propInfo in globalsType.GetFields())
							propInfo.SetValue(_globalInstances, globals[propInfo.Name]);

						using (var loader = new InteractiveAssemblyLoader())
						{
							loader.RegisterDependency(libAssembly);

							_script = CSharpScript.Create(string.Empty, options.AddReferences(portableReference), globalsType, loader);
							_scriptState = await _script.RunAsync(_globalInstances, cancellationToken: token);
						}
					}
					else
					{
						_script = CSharpScript.Create(string.Empty, options);
						_scriptState = await _script.RunAsync(cancellationToken: token);
					}
                }
                catch (OperationCanceledException)
                {
                    _scriptState = null;
                }
            }

            InitCompletion();

            return _scriptState != null;
        }

        private void InitCompletion()
        {
			_mefHost = MefHostServices.Create(MefHostServices.DefaultAssemblies);
			
            var compilationOptions = new CSharpCompilationOptions(
               OutputKind.DynamicallyLinkedLibrary,
               usings: _usings);

            _scriptProjectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Script", "Script", LanguageNames.CSharp, isSubmission: true)
                .WithMetadataReferences(_references.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)))
                .WithCompilationOptions(compilationOptions);

            _textContainer = new TextContainer(string.Empty);
        }

		private Document GetDocumentFromContainer(TextContainer container)
		{
			var workspace = new AdhocWorkspace(_mefHost);
			var project = workspace
				.AddProject(_scriptProjectInfo);

			var scriptDocumentInfo = DocumentInfo.Create(
				DocumentId.CreateNewId(project.Id), "Script",
				sourceCodeKind: SourceCodeKind.Script,
				loader: TextLoader.From(container, VersionStamp.Create()));

			return workspace.AddDocument(scriptDocumentInfo);
		}

		private void AddStateToCompletion(string code)
		{
			_textContainer.AddLine(code);
		}

        public async Task<CompletionList> GetCompletion(string code, int? carretPosition = null)
        {
            _textContainer.SetCurrentLine(code);

			int position;
			if (carretPosition != null)
			{
				string fullCode = _textContainer.CurrentText.ToString();
				position = fullCode.IndexOf(code) + carretPosition.Value;
			}
			else
				position = _textContainer.CurrentText.Lines.Last().End;

			var document = GetDocumentFromContainer(_textContainer);

			var completionService = CompletionService.GetService(document);
            return await completionService.GetCompletionsAsync(document, position);
        }

		public async Task ResetState()
		{
			_scriptState = await _script.RunAsync(_globalInstances);
		}

		public async Task<T> Generate<T>(string funcString, bool useCurrentState = false, CancellationToken token = default)
		{
			if (!await InitScript())
				throw new InvalidOperationException("ScriptHandler initialization failed");

			ScriptState<object> tempState;
			if (useCurrentState)
				tempState = _scriptState;
			else
				tempState = await _script.RunAsync(_globalInstances, token);

			try
			{
				var returnedState = await tempState.ContinueWithAsync<T>(funcString, cancellationToken: token);
				return returnedState.ReturnValue;
			}
			catch (CompilationErrorException e)
			{
				throw new InvalidOperationException(e.Message, e);
			}
		}

        private string CreateGlobalsType()
        {
            StringBuilder str = new StringBuilder();

            str.Append("public class ScriptGlobals {");

            foreach (var global in _globals)
                str.Append($" public {global.Value.GetType().GetFriendlyName()} {global.Key};");

            str.Append(" }");

            return str.ToString();
        }

		private async Task<bool> ExecuteCode(string code, CancellationToken token, IScriptWriter sender, Func<Func<Task<bool>>, Task<bool>> executionContext)
		{
			string result = null; object returnedValue = null; bool isError = false; bool isCancelled = false;

			try
			{
				if (!await InitScript())
					return false;

				if (executionContext != null)
					return await executionContext(() => ExecuteCode(code, token, sender, null));

				ScriptExecuted?.Invoke(this, new ScriptRequest(script: code, writer: sender));

				using (await _scriptStateLock.LockAsync(token))
				{ 
					_scriptState = await _scriptState.ContinueWithAsync(code, cancellationToken: token);

					returnedValue = _scriptState.ReturnValue;
					result = _scriptState.ReturnValue?.ToString();
					if (_scriptState.ReturnValue != null && _scriptState.ReturnValue.GetType() == typeof(string))
						result = $"\"{result}\"";
				}
			}
			catch (CompilationErrorException e)
			{
				result = e.Message;
				isError = true;
			}
			catch (OperationCanceledException)
			{
				result = string.Empty;
				isCancelled = true;
			}

			if (result != null)
			{
				var scriptResult = new ScriptResult ( result, returnedValue, isError, isCancelled );
				Results.Add(scriptResult);
				ScriptResultReceived?.Invoke(this, scriptResult);
			}

			if (!isError && !isCancelled)
				AddStateToCompletion(code);

			return !isError && !isCancelled;
		}

		private async Task<bool> ExecuteCode<T>(string code, CancellationToken token, IScriptWriter sender, Func<Func<Task<bool>>, Task<bool>> executionContext, bool useCurrentState)
		{
			string result = null; object returnedValue = null; bool isError = false; bool isCancelled = false;

			try
			{
				if (!await InitScript())
					return false;

				ScriptState<object> tempState;

				if (executionContext != null)
					return await executionContext(() => ExecuteCode<T>(code, token, sender, null, useCurrentState));

				ScriptExecuted?.Invoke(this, new ScriptRequest(script: code, writer: sender));

				if (useCurrentState)
					tempState = _scriptState;
				else
					tempState = await _script.RunAsync(_globalInstances, token);

				var resultState = await _scriptState.ContinueWithAsync<T>(code, cancellationToken: token);

				returnedValue = resultState.ReturnValue;
				result = resultState.ReturnValue?.ToString();
				if (resultState.ReturnValue != null && resultState.ReturnValue.GetType() == typeof(string))
					result = $"\"{result}\"";

			}
			catch (CompilationErrorException e)
			{
				result = e.Message;
				isError = true;
			}
			catch (OperationCanceledException)
			{
				result = string.Empty;
				isCancelled = true;
			}

			if (result != null)
			{
				var scriptResult = new ScriptResult(result, returnedValue, isError, isCancelled);
				Results.Add(scriptResult);
				ScriptResultReceived?.Invoke(this, scriptResult);
			}

			return !isError && !isCancelled;
		}

		private Task<bool> ExecuteCode<T>(string code, CancellationToken token, IScriptWriter sender, Func<Func<Task<bool>>, Task<bool>> executionContext, bool useCurrentState, T typeUsedForInference)
			=> ExecuteCode<T>(code, token, sender, executionContext, useCurrentState);


		public Task<bool> ExecuteCode(string code, CancellationToken token = default, IScriptWriter sender = null)
			=> ExecuteCode(code, token, sender, _executionContext);

		public Task<bool> ExecuteCode<T>(string code, CancellationToken token = default, IScriptWriter sender = null, bool useCurrentState = true)
			=> ExecuteCode<T>(code, token, sender, _executionContext, useCurrentState);

		public Task<bool> ExecuteCode(string code, Type returnType, CancellationToken token = default, IScriptWriter sender = null, bool useCurrentState = true)
			=> ExecuteCode(code, token, sender, _executionContext, useCurrentState, returnType);


		public Assembly[] GetReferences() => _references.ToArrayLocked(_lockReferences);
        public string[] GetUsings() => _usings.ToArrayLocked(_lockUsings);
        public IReadOnlyDictionary<string, object> GetGlobals() => _globals.ToDictionaryLocked(_lockGlobals);

		public async Task<IReadOnlyDictionary<string, object>> GetVariables(CancellationToken token = default)
		{
			using (await _scriptStateLock.LockAsync(token))
			{
				if (token.IsCancellationRequested)
					return null;

				return _scriptState?.Variables.ToDictionary(sv => sv.Name, sv => sv.Value);
			}
		}
    }


    public static class ScriptViewModelExtension
    {
        /// <summary>
        /// Add assemblies to use with the ScriptHandler.
        /// Typically : typeof(string).Assembly, myInstance.GetType().Assembly
        /// </summary>
        /// <param name="model"></param>
        /// <param name="references"></param>
        /// <returns></returns>
        public static ScriptHandler AddReferences(this ScriptHandler model, params Assembly[] references)
        {
            using (model._scriptStateLock.Lock())
            {
                if (model._scriptState != null)
                    throw new NotSupportedException("Cannot add reference after script was already initialized");
            }

            model._references.AddRange(
                references
                .Except(model._references)
                .Distinct()
                .ToArray()
            );
            return model;
        }

        /// <summary>
        /// Add assembly and optionally the referenced assembly. Can be use calling AddReferences(Assembly.GetExecutingAssembly(), true),
        /// this will take the full pack of the currently executing software assemblies
        /// </summary>
        /// <param name="scriptHandler"></param>
        /// <param name="reference"></param>
        /// <param name="includeReferencedAssemblies"></param>
        /// <returns></returns>
        public static ScriptHandler AddReferences(this ScriptHandler scriptHandler, Assembly reference, bool includeReferencedAssemblies = false)
        {
            using (scriptHandler._scriptStateLock.Lock())
            {
                if (scriptHandler._scriptState != null)
                    throw new NotSupportedException("Cannot add reference after script was already initialized");
            }

            scriptHandler._references.AddRange(
                (includeReferencedAssemblies
                    ? reference.GetReferencedAssemblies()
                        .Select(a => Assembly.Load(a))
                        .Concat(reference.Yield())
                    : reference.Yield())
                .Except(scriptHandler._references)
                .Distinct()
                .ToArray()
            );
            return scriptHandler;
        }

        /// <summary>
        /// Namespace to use "System", "System.IO", ...
        /// </summary>
        /// <param name="scriptHandler"></param>
        /// <param name="usings"></param>
        /// <returns></returns>
        public static ScriptHandler AddUsings(this ScriptHandler scriptHandler, params string[] usings)
        {
            using (scriptHandler._scriptStateLock.Lock())
            {
                if (scriptHandler._scriptState != null)
                    throw new NotSupportedException($"Cannot add usings after script was already initialized, call ExecuteCode(\"using {usings.FirstOrDefault()};\") instead");
            }

            scriptHandler._usings.AddRange(
                usings
                .Except(scriptHandler._usings)
                .Distinct()
                .ToArray()
            );
            return scriptHandler;
        }

        /// <summary>
        /// Add global instances to the script
        /// Typically : (nameof(MyInstance), MyInstance), ("SomeString", "Hello World"), ...
        /// </summary>
        /// <param name="scriptHandler"></param>
        /// <param name="globals"></param>
        /// <returns></returns>
        public static ScriptHandler AddGlobals(this ScriptHandler scriptHandler, params (string name, object value)[] globals)
        {
            using (scriptHandler._scriptStateLock.Lock())
            {
                if (scriptHandler._scriptState != null)
                    throw new NotSupportedException("Cannot add globals after script was already initialized");
            }

            if (globals == null)
                return scriptHandler;

            foreach (var (name, value) in globals)
            {
                if (!scriptHandler._globals.ContainsKey(name))
                    scriptHandler._globals.Add(name, value);
            }

            return scriptHandler;
        }
    }
}
