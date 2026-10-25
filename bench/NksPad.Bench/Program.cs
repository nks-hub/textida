using System.Text;
using NksPad.Bench;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Paths.Configure(args);
string cmd = args.Length > 0 ? args[0] : "help";
switch (cmd)
{
    case "corpus":
        {
            bool force = Args.Flag(args, "--force");
            var names = Corpus.PositionalArgs(args).ToList();
            if (names.Count == 0) names = Corpus.Names.ToList();
            names = names.Where(n => Corpus.InProfile(n)).ToList();
            foreach (var n in names) await Corpus.GenerateAsync(n, force);
            Corpus.WriteManifest();
            break;
        }
    case "a2": A2AvaloniaEdit.Run(args); break;
    case "a3": await A3Paged.Run(args); break;
    case "a4": await A4LongLine.Run(args); break;
    case "a5": await A5Search.Run(args); break;
    case "a7": await A7Startup.Run(args); break;
    case "writetest": WriteTest.Run(args); break;
    case "a6": A6Encoding.Run(args); break;
    case "hex": await HexView.Run(args); break;
    default:
        Console.WriteLine("commands: corpus [names] [--force] | a3 <file> [--reps N] [--samples N] [--inplace] | a4 | a5 file <names...> | a5 tree | a6 | hex <file> [--samples N] [--reps N] [--save]");
        break;
}
