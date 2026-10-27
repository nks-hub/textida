// Stand-in for an external tool in tests: prints the arguments it received, one per line in brackets.
// --sleep: prints "started" and then waits a minute. --both: writes one line to each output stream. A trailing "--exit N" sets the exit code.
using System.Text;

Console.OutputEncoding = new UTF8Encoding(false);
if (args is ["--sleep", ..])
{
    Console.WriteLine("started");
    Thread.Sleep(TimeSpan.FromMinutes(1));
    return 0;
}
if (args is ["--both"])
{
    Console.WriteLine("to-out");
    Console.Error.WriteLine("to-err");
    return 0;
}
int code = 0;
if (args is [.., "--exit", var number] && int.TryParse(number, out code)) args = args[..^2];
foreach (var arg in args) Console.WriteLine("[" + arg + "]");
return code;
