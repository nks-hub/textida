using Jint;
using Jint.Native;
using Jint.Native.Function;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace NksHub.Textida.App.Services;

/// <summary>The script ran too long, used too much memory, threw, or has no <c>transform</c> function.</summary>
internal sealed class ScriptException(string message) : Exception(message);

/// <summary>
/// A prototype of plugin code that runs inside the editor (PZ.8.3), behind the setting <c>scripting.enabled</c>, which is off. A script is JavaScript run by Jint with no access to anything but what is handed to it:
/// the function <c>transform(text)</c> it defines is called with a string and returns the string that replaces it; <c>log(text)</c> writes a line to the output window. There is no .NET interop, no file, no network, and the
/// run is bounded in time, memory, statements and recursion. This is a prototype to measure and try: the result goes to the change review, never to the document directly.
/// </summary>
internal static class InEditorScript
{
    public const int MaxStatements = 2_000_000;
    public const int MaxMemoryBytes = 32 << 20;
    public const int MaxRecursion = 200;
    public const int MaxInputChars = 1 << 20;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    public static string Transform(string script, string input, Action<string> log, TimeSpan? timeout = null)
    {
        if (input.Length > MaxInputChars) throw new ScriptException($"The text is longer than {MaxInputChars} characters.");
        try
        {
            var engine = new Engine(options =>
            {
                options.TimeoutInterval(timeout ?? DefaultTimeout);
                options.MaxStatements(MaxStatements);
                options.LimitMemory(MaxMemoryBytes);
                options.LimitRecursion(MaxRecursion);
                options.Strict();
            });
            // a function object of the engine, not a .NET delegate: the delegate bridge of Jint uses reflection, which NativeAOT cannot trim safely
            engine.SetValue("log", new ClrFunction(engine, "log", (_, arguments) =>
            {
                log(arguments.Length > 0 ? arguments[0].ToString() : "");
                return JsValue.Undefined;
            }));
            engine.Execute(script);
            var function = engine.GetValue("transform");
            if (function is not Function) throw new ScriptException("The script defines no function transform(text).");
            var result = engine.Invoke(function, input);
            if (!result.IsString()) throw new ScriptException("transform(text) must return a string.");
            string text = result.AsString();
            if (text.Length > MaxInputChars * 2) throw new ScriptException("The result is too long.");
            return text;
        }
        catch (TimeoutException) { throw new ScriptException("The script ran too long."); }
        catch (StatementsCountOverflowException) { throw new ScriptException("The script ran too many statements."); }
        catch (MemoryLimitExceededException) { throw new ScriptException("The script used too much memory."); }
        catch (RecursionDepthOverflowException) { throw new ScriptException("The script recursed too deep."); }
        catch (JavaScriptException e) { throw new ScriptException(e.Message); }
        catch (Acornima.ParseErrorException e) { throw new ScriptException(e.Message); }
    }
}
