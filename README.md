# CSharpReplLib
Library to easily add a C# REPL feature

## How to start

You can clone this library or download the CSharpReplLib [NuGet package](https://www.nuget.org/packages/CSharpReplLib).

Create an instance of `ScriptHandler`. 

`ScriptHandler` takes an optional execution context ( `Func<Func<Task>,Task>` ) as parameter, which allow to execute all REPL script asynchronously in an application context. For instance, when using WPF you can call :

```C#
var handler = new ScriptHandler(func => DispatcherAsync.Invoke(func).Task);
```

When using a Reactive Scheduler :

```C#
// Method extension to put in a static class ***
public static Task ScheduleAsyncWait(this IScheduler scheduler, Func<Task> funcAsync)
{
    return Observable.Return<Unit>(default)
        .ObserveOn(scheduler)
        .SelectMany(async _ => { await funcAsync(); return _; })
        .FirstAsync()
        .ToTask();
}
// ***

var handler = new ScriptHandler(func => myScheduler.ScheduleAsyncWait(func));
```
_( if someone has a more elegant solution with `IScheduler` feel free to share)_ 

You can also specify to the `ScriptHandler` which assemblies it can references and which global variables to use using the class extensions :

### Adding assemblies
```C#
// .AddReferences(params Assembly[] references)

handler
    .AddReferences
    (
        Assembly.GetExecutingAssembly(), // current project assembly
        typeof(Newtonsoft.Json.JsonConverter).Assembly, // Newtonsoft.Json
        typeof(int).Assembly // mscorlib
    )
```
```C#
// .AddReferences(Assembly reference, bool includeReferencedAssemblies = false)

handler
    .AddReferences
    (
        Assembly.GetExecutingAssembly(), // current project assembly
        includeReferencedAssemblies : true // this will include all dependent assemblies
    )
```

### Adding usings
You can also add usings later by simply executing `"using System.Linq;"`
```C#
// .AddUsings(params string[] usings)

handler
    .AddUsings
    (
        "System",
        "System.Text",
        "System.IO",
        "MyCurrentNameSpace"
    )
```

### Adding globals
These globals will be directly accessible through your scripts
```C#
// .AddGlobal(params (string name, object value)[] globals)

Foo foo = new Foo();
Bar bar = new Bar();

handler
    .AddGlobals
    (
        (nameof(foo), foo),
        (nameof(bar), bar)
    )
```

## Handling Execution

`ScriptHandler` has 2 events :

  * `EventHandler<ScriptResult> ScriptResultReceived`
  * `EventHandler<ScriptRequest> ScriptExecuted`

Example with `ScriptResultReceived` :
```C#
handler.ScriptResultReceived += handler_ScriptResultReceived;

private void ScriptHandler_ScriptResultReceived(object sender, ScriptHandler.ScriptResult e)
{
    StringBuilder str = new StringBuilder();

    if (e.IsError)
        str.Append("[ERROR] ");
    
    if (e.IsCancelled)
        str.Append("[CANCELLED] ");

    str.Append(e.Result);

    Console.WriteLine(e.ToString());
}
```

## Executing code

To execute code, simply call the asynchronous method `ExecuteCode`.

```C#
// public async Task<bool> ExecuteCode(string code, CancellationToken token = default, IScriptWriter sender = null)

var success = await handler.ExecuteCode("1 + 1");
```

The executed code can be catch with `ScriptExecuted` and its result received with `ScriptResultReceived`.

At the first call, `ExecuteCode` will call the public method `InitScript`. This first call can be very long (a few seconds). It's advised to manually call `InitScript` earlier to avoid the waiting time. `InitScript` can be called in another thread.

Once `InitScript` has been called, you cannot modify the ScriptHandler (by calling `AddReferences`, `AddUsings` or `AddGlobals`), this will throw a `NotSupportedException`.

You can still however add usings by calling `await handler.ExecuteCode("using System.Linq;")`.

## Using Visual studio code

This library allows you to generate a temporary project and open Visual Studio Code to edit your script. Saving the script will call `ExecuteCode`.

### Requirement

The CSharpReplLib.VSCode [NuGet package](https://www.nuget.org/packages/CSharpReplLib.VSCode).

You obviously need Visual Studio code installed on your machine with `code` accessible in your PATH. 

### Usage

Simply call :
```C#
VSCodeWriter vsCode;
ScriptHandler handler;

void OpenVsCode()
{
    vsCode = new VSCodeWriter();
    vsCode.Open(handler);
}

void Dispose()
{
    vsCode?.Dispose();
}
```

This has the big advantage to have access to all the Intellisense, colorization and the tons of features included in Visual Studio Code. You can also access your global values with intellisense.

Don't forget to call `Dispose` to delete the temporary files.

I might add other tools in the future.
