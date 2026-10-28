using System.Reflection;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Tests;

/// <summary>P8b.2.2: what the SDK says about the wire (permissions of a method, names of the environment) is what the editor does.</summary>
public class WireContractTests
{
    private static IEnumerable<string> Methods(Type constants) =>
        constants.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public async Task Every_method_of_the_document_api_is_refused_without_the_permission_the_table_names_and_passes_the_gate_with_exactly_that_one()
    {
        foreach (var method in Methods(typeof(DocumentMethods)))
        {
            var needed = PluginMethodPermissions.ForDocumentApi(method);

            await using (var none = await T.RawEditor())
            {
                var error = await ErrorOrNull(() => none.Raw!.RequestAsync(method));
                if (needed is null) Assert.NotEqual(DocumentErrorCodes.PermissionDenied, error?.Code);
                else
                {
                    Assert.True(error?.Code == DocumentErrorCodes.PermissionDenied, $"{method} without a grant: {error?.Code.ToString() ?? "no error"}");
                    Assert.Equal(needed, T.Data(error!, "permission"));
                }
            }

            if (needed is null) continue;
            await using var granted = await T.RawEditor(needed);
            var passed = await ErrorOrNull(() => granted.Raw!.RequestAsync(method));
            Assert.True(passed?.Code != DocumentErrorCodes.PermissionDenied, $"{method} with {needed} only: {passed?.Message}");
        }
    }

    [Fact]
    public void The_permission_table_covers_the_services_outside_the_document_api()
    {
        Assert.Equal(PluginPermissionNames.Network, PluginMethodPermissions.For(PluginServiceMethods.NetRequest));
        Assert.Equal(PluginPermissionNames.CommandExecute, PluginMethodPermissions.For(PluginServiceMethods.CommandExecute));
        Assert.Null(PluginMethodPermissions.For(PluginServiceMethods.SecretGet));
        Assert.Null(PluginMethodPermissions.For(PluginServiceMethods.SecretSet));
        Assert.Null(PluginMethodPermissions.For(PluginServiceMethods.SecretDelete));
        Assert.All(Methods(typeof(UiMethods)), method => Assert.Null(PluginMethodPermissions.For(method)));
        Assert.Equal(DocumentPermissions.EditDirect, PluginMethodPermissions.For(DocumentMethods.EditApply));
        // a manifest may ask for the permission of every method it can call, except the two that are granted when they are needed
        var declarable = PluginPermissionNames.Declarable;
        Assert.All(Methods(typeof(DocumentMethods)).Select(PluginMethodPermissions.For).OfType<string>(), permission => Assert.Contains(permission, declarable));
    }

    [Fact]
    public void The_names_a_plugin_reads_in_its_environment_are_those_the_editor_writes()
    {
        Assert.Equal(ChildProcessGuard.ParentPidVariable, EditorWatch.ParentPidVariable);
        Assert.Equal(ChildProcessGuard.ParentStartVariable, EditorWatch.ParentStartVariable);
        Assert.Equal(ParentWatch.PollVariable, EditorWatch.PollVariable);
        Assert.Equal(ParentWatch.ToleranceVariable, EditorWatch.ToleranceVariable);
    }

    [Fact]
    public void A_plugin_watches_the_editor_only_when_the_editor_asks_and_reads_what_the_editor_wrote()
    {
        string[] names = [EditorWatch.ParentPidVariable, EditorWatch.ParentStartVariable, EditorWatch.PollVariable, EditorWatch.ToleranceVariable];
        Assert.Null(EditorWatch.FromEnvironment()); // no editor asked for it

        var info = new System.Diagnostics.ProcessStartInfo();
        ChildProcessGuard.PassTo(info);
        ParentWatch.Apply(info, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(2));
        foreach (var name in names) Environment.SetEnvironmentVariable(name, info.Environment[name]);
        try
        {
            var watch = EditorWatch.FromEnvironment();

            Assert.NotNull(watch);
            Assert.False(watch.IsGone()); // this process is the editor of the test, and it runs
        }
        finally
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static async Task<RpcException?> ErrorOrNull(Func<Task> call)
    {
        try { await call().WaitAsync(TimeSpan.FromSeconds(10)); return null; }
        catch (RpcException e) { return e; }
    }
}
