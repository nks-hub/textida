using System.Reflection;

namespace NksHub.Textida.Plugin.Tests.Contracts;

/// <summary>
/// P8b.2.2: every interface of the plugin API has a contract test class, and every class has the three scenarios: it works (<c>Positive_</c>), a stale revision is refused (<c>Revision_conflict_</c>) and a plugin
/// without the permission is refused (<c>Permission_</c>). Where an interface has no revision or no permission the scenario checks the nearest thing it has, and the test says so in its summary.
/// </summary>
public class ContractMatrixTests
{
    private static readonly string[] Interfaces = ["Documents", "Edits", "Ui", "Diff", "Virtual", "Net", "Secrets"];

    private static IEnumerable<Type> ContractClasses() =>
        typeof(ContractMatrixTests).Assembly.GetTypes().Where(t => t.Namespace == typeof(ContractMatrixTests).Namespace && t.Name.EndsWith("ContractTests", StringComparison.Ordinal));

    [Fact]
    public void Every_interface_of_the_plugin_api_has_a_contract_class()
    {
        Assert.Equal(Interfaces.Order(), ContractClasses().Select(t => t.Name[..^"ContractTests".Length]).Order());
    }

    [Theory]
    [InlineData("Positive_")]
    [InlineData("Revision_conflict_")]
    [InlineData("Permission_")]
    public void Every_contract_class_has_the_scenario(string prefix)
    {
        foreach (var type in ContractClasses())
            Assert.True(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Any(m => m.GetCustomAttribute<FactAttribute>() is not null && m.Name.StartsWith(prefix, StringComparison.Ordinal)),
                $"{type.Name} has no test whose name starts with {prefix}");
    }
}
