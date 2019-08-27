using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpReplLib.VSCode
{
    public class VSCodeWriter : IScriptWriter
    {
        private DirectoryInfo _tempFolder;

        private FileSystemWatcher _watcher = null;
        private CancellationTokenSource _readingScriptFileTokenSource;

        private ScriptHandler _scriptHandler;
		private Type _returnType = null;

		public void Open(ScriptHandler scriptHandler, Type returnType = null)
		{
			if (_tempFolder != null && _tempFolder.Exists)
				_tempFolder.Delete();

			_returnType = returnType;
			_scriptHandler = scriptHandler;

			_tempFolder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
			_tempFolder.Create();

			FileInfo csprojFile = new FileInfo(Path.Combine(_tempFolder.FullName, "ScriptProject.csproj"));
			FileInfo globalFile = new FileInfo(Path.Combine(_tempFolder.FullName, "ScriptGlobals.cs"));
			FileInfo scriptTemplate = new FileInfo(Path.Combine(_tempFolder.FullName, "ScriptTemplate.cs"));

			var usings = scriptHandler.GetUsings();
			var references = scriptHandler.GetReferences();
			var globals = scriptHandler.GetGlobals();

			File.WriteAllText(csprojFile.FullName, CreateProject(references));
			File.WriteAllText(globalFile.FullName, CreateGlobals(usings, globals));
			File.WriteAllText(scriptTemplate.FullName, CreateTemplate(usings, returnType));

			// dotnet restore on the project just created
			Process.Start(
				new ProcessStartInfo
				{
					FileName = "dotnet",
					Arguments = "restore",
					WorkingDirectory = _tempFolder.FullName,
					CreateNoWindow = true
				});

			// Start VS Code
			Process.Start(
				new ProcessStartInfo
				{
					FileName = "code",
					Arguments = $"\"{_tempFolder.FullName}\" -g \"{scriptTemplate.FullName}\":{11 + usings.Length}:43",
					CreateNoWindow = true
				});


			if (_watcher != null)
			{
				_watcher.Changed -= WatcherChanged;
				_watcher.Dispose();
			}

			_watcher = new FileSystemWatcher(_tempFolder.FullName)
			{
				NotifyFilter = NotifyFilters.LastWrite,
				Filter = "*ScriptTemplate.cs",
				EnableRaisingEvents = true
			};

			_watcher.Changed += WatcherChanged;
		}

		private string CreateGlobals(string[] usings, IReadOnlyDictionary<string, object> globals, IReadOnlyDictionary<string, object> variables = null)
        {
            StringBuilder str = new StringBuilder();

            foreach (var use in usings)
                str.AppendLine($"using {use};");

            str.AppendLine();
            str.AppendLine("public static class ScriptGlobals");
            str.AppendLine("{");

            foreach (var global in globals)
                str.AppendLine($"\tpublic static {global.Value.GetType().GetFriendlyName()} {global.Key} {{ get; }}");

			if (variables != null)
				foreach (var variable in variables)
					str.AppendLine($"\tpublic static {variable.Value.GetType().GetFriendlyName()} {variable.Key} {{ get; }}");

			str.AppendLine("}");

            return str.ToString();
        }

		private string CreateTemplate(string[] usings, Type returnType = null)
		{
			StringBuilder str = new StringBuilder();

			str.AppendLine("using static ScriptGlobals; // first line is removed at script execution, don't change the first line");

			foreach (var use in usings)
				str.AppendLine($"using {use};");

			str.AppendLine();

			str.AppendLine(
@"public class ScriptTemplate // Class declaration will also be removed so we stay in same scope as the script state
{
	// Write your script inside the ExecuteScript method. 
	// You can create other methods or class inside this class as long as you keep a method named ExecuteScript().
	// The script is automatically executed every time the file is saved");

			str.AppendFormat("	public {0} ExecuteScript()", returnType?.GetFriendlyName() ?? "object");
			str.AppendLine(
@"
	{
		return ""Return your script result here"";
	}

	// Let this comment just before the class closing bracket
}");

			return str.ToString();
		}

		private string CreateProject(Assembly[] references)
        {
            StringBuilder str = new StringBuilder();

            str.AppendLine(
@"<Project Sdk=""Microsoft.NET.Sdk"">

	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<TargetFramework>net471</TargetFramework>
	</PropertyGroup>"
            );

            //if (_nugets.Any())
            //{
            //    str.AppendLine("\t<ItemGroup>");
            //    foreach (var nuget in _nugets)
            //        str.AppendLine($"\t\t<PackageReference Include=\"{nuget.Key}\" Version=\"{nuget.Value}\" />");
            //    str.AppendLine("\t</ItemGroup>");
            //}

            if (references.Any())
            {
                str.AppendLine("\t<ItemGroup>");
                foreach (var reference in references)
                {
                    str.AppendLine($"\t\t<Reference Include=\"{reference.GetName().Name}\">");
                    str.AppendLine($"\t\t\t<HintPath>{reference.Location}</HintPath>");
                    str.AppendLine($"\t\t</Reference>");
                }
                str.AppendLine("\t</ItemGroup>");
            }

            str.AppendLine("</Project>");

            return str.ToString();
        }

        private async void WatcherChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed || !e.FullPath.EndsWith("ScriptTemplate.cs"))
                return;

            await ReadScriptTemplate(e.FullPath, GetToken());
        }

        private async Task ReadScriptTemplate(string fullPath, CancellationToken token)
        {
            string[] allLines = null;

            // Wait for Visual Studio Code to close the file before reading it
            await WaitDelayOrCancel(TimeSpan.FromMilliseconds(50), token);
            while (allLines == null && !token.IsCancellationRequested)
            {
                try
                {
                    allLines = File.ReadAllLines(fullPath);
                }
                catch (IOException)
                {
                    await WaitDelayOrCancel(TimeSpan.FromMilliseconds(50), token);
                }
            }

            if (token.IsCancellationRequested)
                return;

            // remove class
            var classIndex = allLines.FindIndex(line => line.Contains("class ScriptTemplate"));
            // remove bracket
            var afterClass = string.Join("\n", allLines.Skip(classIndex + 1)).Trim(' ', '\t', '\n', '{', '}');
            // reconstruct
            var script = string.Join("\n", allLines.Take(classIndex).Skip(1).Concat(afterClass.Yield()));

			if (_returnType != null)
				await _scriptHandler.ExecuteCode(script + "\nExecuteScript()", sender: this);
			else
				await _scriptHandler.ExecuteCode(script + "\nExecuteScript()", _returnType, sender: this);
		}

        private CancellationToken GetToken()
        {
            if (_readingScriptFileTokenSource != null)
                _readingScriptFileTokenSource.Cancel();

            _readingScriptFileTokenSource = new CancellationTokenSource();
            return _readingScriptFileTokenSource.Token;
        }

        private async Task WaitDelayOrCancel(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (TaskCanceledException)
            { }
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.Changed -= WatcherChanged;
                _watcher.Dispose();
            }

            try
            {
                if (_tempFolder != null && _tempFolder.Exists)
                    _tempFolder.Delete(true);
            }
            catch (IOException)
            { }
        }
    }
}
