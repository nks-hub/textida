using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginSdk;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P3b.5.1: the helpers of the internal C# SDK (commands, typed calls, cancellation, tasks with progress, document edits) against a plugin on a real connection and a stand-in for the editor.</summary>
public partial class PluginSdkTests
{
    internal sealed record Ping(string Text);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Ping))]
    internal sealed partial class ProbeJson : JsonSerializerContext;

    sealed class Probe : PluginBase
    {
        public readonly TaskCompletionSource TaskStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource SawCancel = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Probe() : base("probe", apiVersion: 1)
        {
            OnCommand("probe.echo", (c, _) => ValueTask.FromResult(new CommandResult($"{c.Language}|{c.DocumentId}|{c.Revision}|{c.String("word")}|{c.Flag("flag")}")));
            OnCommand("probe.raw", (_, _) => ValueTask.FromResult(CommandResult.Raw("{\"custom\":true}"u8.ToArray())));
            OnCommand("probe.diagnostics", (_, _) => ValueTask.FromResult(new CommandResult("done", ["first", "second"])));
            OnCommand("probe.typed", async (_, ct) =>
            {
                var answer = await Rpc.RequestAsync("editor/ping", new Ping("ping"), ProbeJson.Default.Ping, ProbeJson.Default.Ping, ct);
                await Rpc.NotifyAsync("editor/note", new Ping(answer.Text + "!"), ProbeJson.Default.Ping, ct);
                return new CommandResult(answer.Text);
            });
            OnCommand("probe.wait", async (_, ct) =>
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException)
                {
                    SawCancel.TrySetResult();
                    throw;
                }
                return new CommandResult("finished");
            });
            OnCommand("probe.task", async (_, ct) =>
            {
                await using var task = await StartTaskAsync("Probe work", ct, reportEvery: TimeSpan.FromHours(1));
                await task.ReportAsync(0.25, "a quarter");
                await task.ReportAsync(0.5, "half"); // closer than the interval to the one before, so it is not sent
                TaskStarted.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, task.Cancellation); }
                catch (OperationCanceledException) { return new CommandResult("cancelled"); }
                return new CommandResult("finished");
            });
            OnCommand("probe.edit", async (c, ct) =>
            {
                var document = DocumentOf(c);
                var info = await document.InfoAsync(ct);
                await document.ApplyAsync([new RangeEdit(info.BomLength, info.ByteLength, "é\"")], "batch-1", ct);
                return new CommandResult("edited");
            });
        }
    }

    sealed class Editor : IAsyncDisposable
    {
        public readonly Probe Plugin = new();
        public readonly List<(string Method, string Json)> Requests = [];
        public readonly List<(string Method, string Json)> Notifications = [];
        public Func<string, JsonElement, byte[]?> Answer = (_, _) => "{}"u8.ToArray();
        /// <summary>Runs before the answer to a request is sent, so a test can put something on the wire ahead of it.</summary>
        public Func<string, ValueTask>? BeforeAnswer;
        public JsonRpcDispatcher Rpc = null!;
        Task _run = null!;

        public static async Task<Editor> StartAsync()
        {
            var editor = new Editor();
            var toPlugin = new MemoryPipe();
            var toEditor = new MemoryPipe();
            editor._run = PluginRunner.RunAsync(editor.Plugin, T.Roomy, toPlugin, toEditor);
            editor.Rpc = new JsonRpcDispatcher(toEditor, toPlugin, RpcProtocols.Plugin, T.Roomy, new DelegateHandler(
                async (method, p, _) =>
                {
                    lock (editor.Requests) editor.Requests.Add((method, p.GetRawText()));
                    if (editor.BeforeAnswer is { } before) await before(method);
                    return editor.Answer(method, p);
                },
                (method, p) =>
                {
                    lock (editor.Notifications) editor.Notifications.Add((method, p.GetRawText()));
                    return ValueTask.CompletedTask;
                }));
            await editor.Rpc.RequestAsync(PluginMethods.Initialize, T.Json("{\"apiVersion\":1,\"restarted\":false,\"hostName\":\"test\"}"));
            return editor;
        }

        public Task<JsonElement> CommandAsync(string command, string extra = "", CancellationToken ct = default) =>
            Rpc.RequestAsync("command/execute", T.Json($"{{\"command\":\"{command}\",\"document\":\"d1\",\"revision\":7,\"language\":\"cs\"{extra}}}"), null, ct);

        public (string Method, string Json)[] Seen(string method)
        {
            lock (Requests) return [.. Requests.Where(r => r.Method == method)];
        }

        public async ValueTask DisposeAsync()
        {
            await Rpc.NotifyAsync(PluginMethods.Exit);
            await _run.WaitAsync(TimeSpan.FromSeconds(30));
            await Rpc.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_command_gets_the_parts_of_the_request_and_answers_with_a_status()
    {
        await using var editor = await Editor.StartAsync();

        var answer = await editor.CommandAsync("probe.echo", ",\"word\":\"x\",\"flag\":true");
        Assert.Equal("cs|d1|7|x|True", answer.GetProperty("status").GetString());

        var plain = await editor.Rpc.RequestAsync("command/execute", T.Json("{\"command\":\"probe.echo\"}"));
        Assert.Equal("en||||False", plain.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_command_the_plugin_does_not_know_and_a_request_without_one_are_errors_of_the_parameters()
    {
        await using var editor = await Editor.StartAsync();

        var unknown = await Assert.ThrowsAsync<RpcException>(() => editor.CommandAsync("probe.nothing"));
        Assert.Equal((RpcErrorCodes.InvalidParams, "Unknown command."), (unknown.Code, unknown.Message));
        var missing = await Assert.ThrowsAsync<RpcException>(() => editor.Rpc.RequestAsync("command/execute", T.Json("{}")));
        Assert.Equal(RpcErrorCodes.InvalidParams, missing.Code);
    }

    [Fact]
    public async Task A_command_may_send_diagnostic_lines_or_an_answer_of_its_own()
    {
        await using var editor = await Editor.StartAsync();

        var diagnostics = await editor.CommandAsync("probe.diagnostics");
        Assert.Equal("done", diagnostics.GetProperty("status").GetString());
        Assert.Equal(["first", "second"], diagnostics.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetString()));
        var raw = await editor.CommandAsync("probe.raw");
        Assert.True(raw.GetProperty("custom").GetBoolean());
        Assert.False(raw.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Typed_requests_and_notifications_carry_their_types_without_reflection()
    {
        await using var editor = await Editor.StartAsync();
        editor.Answer = (method, p) => method == "editor/ping" ? Encoding.UTF8.GetBytes($"{{\"text\":\"{p.GetProperty("text").GetString()}-pong\"}}") : "{}"u8.ToArray();

        var answer = await editor.CommandAsync("probe.typed");

        Assert.Equal("ping-pong", answer.GetProperty("status").GetString());
        await T.Until(() => { lock (editor.Notifications) return editor.Notifications.Any(n => n.Method == "editor/note"); });
        lock (editor.Notifications) Assert.Equal("{\"text\":\"ping-pong!\"}", editor.Notifications.First(n => n.Method == "editor/note").Json);
    }

    [Fact]
    public async Task A_request_the_editor_cancels_cancels_the_token_of_the_command()
    {
        await using var editor = await Editor.StartAsync();
        using var cancel = new CancellationTokenSource();

        var running = editor.CommandAsync("probe.wait", ct: cancel.Token);
        await Task.Delay(200);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await editor.Plugin.SawCancel.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task A_task_reports_progress_ends_with_the_command_and_is_cancelled_by_the_user()
    {
        await using var editor = await Editor.StartAsync();
        editor.Answer = (method, _) => method == "ui/task/start" ? "{\"task\":\"t1\"}"u8.ToArray() : "{}"u8.ToArray();

        var running = editor.CommandAsync("probe.task");
        await editor.Plugin.TaskStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(running.IsCompleted);
        Assert.Contains("\"title\":\"Probe work\"", Assert.Single(editor.Seen("ui/task/start")).Json);
        Assert.Contains("\"cancellable\":true", editor.Seen("ui/task/start")[0].Json);

        // the user presses cancel: the editor tells the plugin through the event
        await editor.Rpc.NotifyAsync(PluginMethods.Event, T.Json("{\"name\":\"ui/taskCancelled\",\"payload\":{\"task\":\"t1\"}}"));

        Assert.Equal("cancelled", (await running.WaitAsync(TimeSpan.FromSeconds(15))).GetProperty("status").GetString());
        var progress = Assert.Single(editor.Seen("ui/task/progress"));
        Assert.Equal("{\"task\":\"t1\",\"fraction\":0.25,\"message\":\"a quarter\"}", progress.Json);
        Assert.Equal("{\"task\":\"t1\"}", Assert.Single(editor.Seen("ui/task/end")).Json);
    }

    [Fact]
    public async Task A_cancel_that_reaches_the_plugin_before_the_answer_to_the_start_of_its_task_is_not_lost()
    {
        await using var editor = await Editor.StartAsync();
        editor.Answer = (method, _) => method == "ui/task/start" ? "{\"task\":\"t1\"}"u8.ToArray() : "{}"u8.ToArray();
        // the editor shows the task as soon as it has the start; a cancel then reaches the plugin before the answer that names the task, and the plugin has handled it when the answer arrives
        editor.BeforeAnswer = async method =>
        {
            if (method != "ui/task/start") return;
            await editor.Rpc.NotifyAsync(PluginMethods.Event, T.Json("{\"name\":\"ui/taskCancelled\",\"payload\":{\"task\":\"t1\"}}"));
            await T.Until(() => { lock (editor.Notifications) return editor.Notifications.Any(n => n.Method == PluginMethods.EventsHandled); });
        };

        var running = editor.CommandAsync("probe.task");

        Assert.Equal("cancelled", (await running.WaitAsync(TimeSpan.FromSeconds(15))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_document_client_sends_an_edit_batch_for_the_revision_it_is_bound_to()
    {
        await using var editor = await Editor.StartAsync();
        editor.Answer = (method, _) => method == "document/info"
            ? "{\"id\":\"d1\",\"revision\":7,\"byteLength\":10,\"codec\":\"utf-8\",\"bomLength\":3,\"path\":null}"u8.ToArray() : "{}"u8.ToArray();

        Assert.Equal("edited", (await editor.CommandAsync("probe.edit")).GetProperty("status").GetString());

        using var sent = JsonDocument.Parse(Assert.Single(editor.Seen("edit/apply")).Json);
        var root = sent.RootElement;
        Assert.Equal(("d1", 7L, "batch-1"), (root.GetProperty("document").GetString(), root.GetProperty("revision").GetInt64(), root.GetProperty("batchId").GetString()));
        var edit = Assert.Single(root.GetProperty("edits").EnumerateArray());
        Assert.Equal((3L, 10L, "é\""), (edit.GetProperty("start").GetInt64(), edit.GetProperty("end").GetInt64(), edit.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task The_document_client_refuses_to_go_on_when_the_document_is_past_the_revision_of_the_command()
    {
        await using var editor = await Editor.StartAsync();
        editor.Answer = (method, _) => method == "document/info"
            ? "{\"id\":\"d1\",\"revision\":8,\"byteLength\":10,\"codec\":\"utf-8\",\"bomLength\":0,\"path\":null}"u8.ToArray() : "{}"u8.ToArray();

        var stale = await Assert.ThrowsAsync<RpcException>(() => editor.CommandAsync("probe.edit"));

        Assert.Equal(DocumentErrorCodes.RevisionConflict, stale.Code);
        Assert.Empty(editor.Seen("edit/apply"));
    }

    [Fact]
    public async Task A_command_that_names_no_document_cannot_ask_for_a_document_client()
    {
        await using var editor = await Editor.StartAsync();

        var error = await Assert.ThrowsAsync<RpcException>(() => editor.Rpc.RequestAsync("command/execute", T.Json("{\"command\":\"probe.edit\"}")));

        Assert.Equal(RpcErrorCodes.InvalidParams, error.Code);
    }
}
