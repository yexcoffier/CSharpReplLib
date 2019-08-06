using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
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

// add script feature in your software
namespace CSharpReplLib
{
    public class ScriptHandler
    {
        public struct ScriptResult
        {
            public string Result { get; set; }
            public bool IsError { get; set; }
        }

        public class ScriptRequest
        {
            public string Script { get; set; }
            public IScriptWriter Writer { get; set; }
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

        private ScriptState<object> _scriptState;

        private Func<Func<Task>, Task> _executionContext = null;

        public ScriptHandler(Func<Func<Task>, Task> executionContext = null)
        {
            _executionContext = executionContext;
        }

        public async Task InitScript()
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
                var result = image.Emit(stream);
                var imageArray = ImmutableArray.Create(stream.ToArray());

                var portableReference = MetadataReference.CreateFromImage(imageArray);

                var libAssembly = Assembly.Load(imageArray.ToArray());
                var globalsType = libAssembly.GetTypes().FirstOrDefault(t => t.Name == "ScriptGlobals");
                var globalsInstance = Activator.CreateInstance(globalsType);

                foreach (var propInfo in globalsType.GetFields())
                    propInfo.SetValue(globalsInstance, globals[propInfo.Name]);

                using (var loader = new InteractiveAssemblyLoader())
                {
                    loader.RegisterDependency(libAssembly);

                    var script = CSharpScript.Create(string.Empty, options.AddReferences(portableReference), globalsType, loader);
                    _scriptState = await script.RunAsync(globalsInstance);
                }
            }
            else
                _scriptState = await CSharpScript.RunAsync(string.Empty, options, null, null);
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

        public async Task<bool> ExecuteCode(string code, IScriptWriter sender = null)
        {
            string result; bool isError = false;

            ScriptExecuted?.Invoke(this, new ScriptRequest { Script = code, Writer = sender });

            try
            {
                if (_scriptState == null)
                    await InitScript();

                if (_executionContext != null)
                    await _executionContext(async () => _scriptState = await _scriptState.ContinueWithAsync(code));
                else
                    _scriptState = await _scriptState.ContinueWithAsync(code);

                result = _scriptState.ReturnValue?.ToString();
                if (_scriptState.ReturnValue != null && _scriptState.ReturnValue.GetType() == typeof(string))
                    result = $"\"{result}\"";
            }
            catch (CompilationErrorException e)
            {
                result = e.Message;
                isError = true;
            }

            if (result != null)
            {
                var scriptResult = new ScriptResult { Result = result, IsError = isError };
                Results.Add(scriptResult);
                ScriptResultReceived?.Invoke(this, scriptResult);
            }

            return !isError;
        }

        public Assembly[] GetReferences() => _references.ToArrayLocked(_lockReferences);
        public string[] GetUsings() => _usings.ToArrayLocked(_lockUsings);
        public IReadOnlyDictionary<string, object> GetGlobals() => _globals.ToDictionaryLocked(_lockGlobals);
    }

    public static class ScriptViewModelExtension
    {
        public static ScriptHandler WithReferences(this ScriptHandler model, params Assembly[] references)
        {
            model._references.AddRange(references);
            return model;
        }

        public static ScriptHandler WithUsings(this ScriptHandler model, params string[] usings)
        {
            model._usings.AddRange(usings);
            return model;
        }

        //public static ScriptHandler WithNugets(this ScriptHandler model, params (string name, string version)[] nugets)
        //{
        //    try
        //    {
        //        foreach (var (name, version) in nugets)
        //            model._nugets.Add(name, version);
        //    }
        //    catch (ArgumentException e)
        //    {
        //        throw new ArgumentException("You cannot have multiple times the same nuget", e);
        //    }

        //    return model;
        //}

        public static ScriptHandler WithGlobals(this ScriptHandler model, params (string name, object value)[] globals)
        {
            try
            {
                foreach (var (name, value) in globals)
                    model._globals.Add(name, value);
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException("You cannot have multiple properties with the same name", e);
            }

            return model;
        }
    }
}
