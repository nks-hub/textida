using System.Text;
using NksHub.Textida.Core.Encodings;

// Exercises every codec, detection, loss reporting and normalisation; a trimmed or AOT-broken code page table fails here.
int failures = 0;
void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("FAIL " + what); } }

const string cz = "Příliš žluťoučký kůň\r\núpěl ďábelské ódy\n";
foreach (var codec in TextCodec.All)
{
    if (codec == TextCodec.Cp1252) continue; // has no Czech letters; covered by the loss check below
    var bytes = codec.Encode(cz);
    var doc = codec.Decode(bytes);
    Check(doc.Text == cz, "round trip " + codec.Name);
    Check(ByteFidelity.Save(doc, []).AsSpan().SequenceEqual(bytes), "verbatim save " + codec.Name);
}
Check(EncodingGuesser.Guess(TextCodec.Cp1250.Encode(cz), true).Codec == TextCodec.Cp1250, "guess cp1250");
Check(EncodingGuesser.Guess(TextCodec.Latin2.Encode(cz), true).Codec == TextCodec.Latin2, "guess latin2");
Check(EncodingGuesser.Guess(TextCodec.Kamenicky.Encode(cz), true).Codec == TextCodec.Kamenicky, "guess kamenicky");
var utf8 = TextCodec.Utf8.Decode(TextCodec.Utf8.Encode("řč\U0001F600"));
Check(ByteFidelity.PlanConvert(utf8, TextCodec.Cp1250).Loss.Total == 1, "loss warning");
Check(ByteFidelity.Convert(utf8, TextCodec.Cp1250, "?").AsSpan().SequenceEqual(new byte[] { 0xF8, 0xE8, 0x3F }), "convert with replacement");
Check(ByteFidelity.PlanConvert(utf8, TextCodec.Cp1252).Loss.Total == 3, "cp1252 loss");
Check(UnicodeNormalizer.Preview(TextCodec.Utf8.Decode(TextCodec.Utf8.Encode("é")), NormalizationForm.FormC).Count == 1, "normalization");

await RemoteChecks.RunAsync(args, Check);

Console.WriteLine(failures == 0 ? "AOT check OK" : $"AOT check FAILED ({failures})");
return failures;
