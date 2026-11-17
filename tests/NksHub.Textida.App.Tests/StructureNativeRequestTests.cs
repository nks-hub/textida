using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using NksHub.Textida.App.Shell;

namespace NksHub.Textida.App.Tests;

public sealed class StructureNativeRequestTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Perf")]
    public void Repeated_small_requests_allocate_less_than_one_stream_buffer_per_read()
    {
        using var requests = new Requests();
        string expected = "{\"value\":\"město 😀\",\"padding\":\"" + new string('x', 96) + "\"}";
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(expected)];
        requests.Write("inspect.json", bytes);
        for (int i = 0; i < 32; i++)
            Assert.Equal(expected, requests.Read("inspect.json", "Inspection").GetAwaiter().GetResult());

        const int reads = 1000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < reads; i++)
            Assert.Equal(expected, requests.Read("inspect.json", "Inspection").GetAwaiter().GetResult());
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"NativeRequestAllocation requestBytes={bytes.Length} reads={reads} allocatedBytes={allocated} bytesPerRead={(double)allocated / reads:F2}");
        Assert.True(allocated <= 4096L * reads,
            $"Reading {reads} requests of {bytes.Length} bytes allocated {allocated} bytes ({(double)allocated / reads:F2} per read); limit is 4096 bytes per read.");
    }

    [Theory]
    [InlineData("inspect.json", "Inspection")]
    [InlineData("arm.json", "Arm")]
    public async Task A_request_temporarily_held_without_sharing_is_read_after_the_holder_closes(string name, string kind)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var requests = new Requests();
        const string expected = "{\"value\":\"město 😀\"}";
        string path = requests.Write(name, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(expected)]);
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Task<string?> read = requests.Read(name, kind);
        held.Dispose();
        Assert.Equal(expected, await read);
        var retries = await requests.Retries(1);
        Assert.InRange(retries.Count, 1, 4);
        foreach (var retry in retries) CheckRetry(retry, path, kind);
    }

    [Fact]
    public async Task A_request_still_held_after_four_retries_reports_the_original_open_failure()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var requests = new Requests();
        string path = requests.Write("inspect.json", Encoding.UTF8.GetBytes("{}"));
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var error = await Assert.ThrowsAnyAsync<IOException>(() => requests.Read("inspect.json", "Inspection"));
        Assert.Equal(0x80070020u, unchecked((uint)error.HResult));
        Assert.Equal("OpenInspection", error.Data["Textida.StructureNativeDiagnostic.Operation"]);
        var fileIo = Assert.IsType<JsonObject>(error.Data["Textida.StructureNativeDiagnostic.FileIo"]);
        Assert.Equal(path, fileIo["input"]!["path"]!.GetValue<string>());
        var retries = await requests.Retries(4);
        Assert.Equal(4, retries.Count);
        Assert.Equal([1, 2, 3, 4], retries.Select(row => row["attempt"]!.GetValue<int>()).Order());
        foreach (var retry in retries) CheckRetry(retry, path, "Inspection");
    }

    [Fact]
    public async Task Cancellation_interrupts_the_wait_between_shared_open_attempts()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var requests = new Requests();
        string path = requests.Write("inspect.json", Encoding.UTF8.GetBytes("{}"));
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Task<string?> read = requests.Read("inspect.json", "Inspection");
        requests.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        var retries = await requests.Retries(1);
        Assert.InRange(retries.Count, 1, 4);
        foreach (var retry in retries) CheckRetry(retry, path, "Inspection");
    }

    [Fact]
    public async Task A_request_not_yet_written_returns_null_without_retry()
    {
        using var requests = new Requests();
        Assert.Null(await requests.Read("inspect.json", "Inspection"));
        Assert.False(File.Exists(requests.RetryPath));
    }

    [Fact]
    public async Task Access_denied_is_reported_without_retry()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var requests = new Requests();
        string path = requests.Write("inspect.json", Encoding.UTF8.GetBytes("{}"));
        using var identity = WindowsIdentity.GetCurrent();
        var rule = new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny);
        var security = new FileInfo(path).GetAccessControl();
        security.AddAccessRule(rule);
        new FileInfo(path).SetAccessControl(security);
        try
        {
            Assert.True(File.Exists(path));
            var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => requests.Read("inspect.json", "Inspection"));
            Assert.Equal(0x80070005u, unchecked((uint)error.HResult));
            Assert.Equal("OpenInspection", error.Data["Textida.StructureNativeDiagnostic.Operation"]);
            Assert.False(File.Exists(requests.RetryPath));
        }
        finally
        {
            security.RemoveAccessRule(rule);
            new FileInfo(path).SetAccessControl(security);
        }
    }

    [Theory]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("utf8")]
    public async Task Invalid_length_or_utf8_is_reported_after_open_without_retry(string invalid)
    {
        using var requests = new Requests();
        byte[] bytes = invalid switch { "short" => [(byte)'{'], "long" => new byte[17], _ => [0x7B, 0xFF, 0x7D] };
        requests.Write("inspect.json", bytes);

        Exception error = invalid == "utf8"
            ? await Assert.ThrowsAsync<DecoderFallbackException>(() => requests.Read("inspect.json", "Inspection", 16))
            : await Assert.ThrowsAsync<InvalidDataException>(() => requests.Read("inspect.json", "Inspection", 16));
        Assert.Equal("ReadInspection" + (invalid == "utf8" ? "Text" : "Length"),
            error.Data["Textida.StructureNativeDiagnostic.Operation"]);
        Assert.False(File.Exists(requests.RetryPath));
    }

    private static void CheckRetry(JsonObject retry, string path, string kind)
    {
        Assert.Equal("retry", retry["status"]!.GetValue<string>());
        Assert.Equal("request-open-retry", retry["stage"]!.GetValue<string>());
        Assert.Equal("Read" + kind, retry["phase"]!.GetValue<string>());
        Assert.Equal("Open" + kind, retry["operation"]!.GetValue<string>());
        Assert.Equal("0x80070020", retry["hResultHex"]!.GetValue<string>());
        Assert.Equal(typeof(IOException).FullName, retry["exceptionType"]!.GetValue<string>());
        Assert.Equal(path, retry["fileIo"]!["input"]!["path"]!.GetValue<string>());
        Assert.InRange(retry["attempt"]!.GetValue<int>(), 1, 4);
        Assert.True(retry["observedQpc"]!.GetValue<long>() > 0);
    }

    private sealed class Requests : IDisposable
    {
        private static readonly MethodInfo ReadMethod = typeof(StructureNativeDiagnostic)
            .GetMethod("ReadRequest", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private readonly TempDir temp = new();
        private readonly CancellationTokenSource stopped = new();
        private readonly StructureNativeDiagnostic diagnostic;
        public string RetryPath => Path.Combine(temp.Path, "request-open-retry.jsonl");

        public Requests()
        {
            // Bypass the UI constructor so file tests cannot start the desktop poller.
            diagnostic = (StructureNativeDiagnostic)RuntimeHelpers.GetUninitializedObject(typeof(StructureNativeDiagnostic));
            Set("directory", temp.Path);
            Set("stopped", stopped);
            Set("outputGate", new object());
        }

        private void Set(string name, object value) => typeof(StructureNativeDiagnostic)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(diagnostic, value);

        public string Write(string name, byte[] bytes) => temp.Bytes(name, bytes);
        public void Cancel() => stopped.Cancel();

        public async Task<string?> Read(string name, string kind, int maximumBytes = 32768)
        {
            object? result;
            try { result = ReadMethod.Invoke(diagnostic, [name, maximumBytes, kind]); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
            return result is Task<string?> pending ? await pending : (string?)result;
        }

        public async Task<IReadOnlyList<JsonObject>> Retries(int minimum)
        {
            long start = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(start).TotalSeconds < 5)
            {
                if (File.Exists(RetryPath))
                {
                    try
                    {
                        using var file = new FileStream(RetryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(file, Encoding.UTF8);
                        string text = reader.ReadToEnd();
                        if (text.EndsWith('\n'))
                        {
                            var rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
                            if (rows.Length >= minimum) return rows;
                        }
                    }
                    catch (IOException error) when (unchecked((uint)error.HResult) is 0x80070002u or 0x80070020u) { }
                }
                await Task.Delay(10);
            }
            throw new TimeoutException("Request retry records were not published.");
        }

        public void Dispose() { stopped.Dispose(); temp.Dispose(); }
    }
}
