using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace NksHub.Textida.Plugin.Tests;

/// <summary>
/// The public surface of an assembly as sorted lines of text: its types, their members with signatures, and the values of constants (the method names and error codes of the plugin API are constants,
/// so a changed value shows). Nullability and attributes are not part of it.
/// </summary>
internal static class PublicApi
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>The lines for the exported types of <paramref name="assembly"/> that pass <paramref name="include"/>: a line per type, in order of name, each followed by its members in order of text.</summary>
    public static List<string> Describe(Assembly assembly, Func<Type, bool>? include = null)
    {
        var types = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var type in assembly.GetExportedTypes().Where(t => include?.Invoke(t) ?? true))
        {
            // the members of a JSON context come from the source generator and follow its version, not a decision of ours
            var members = typeof(System.Text.Json.Serialization.JsonSerializerContext).IsAssignableFrom(type) ? []
                : type.GetMembers(Declared).Where(IsApi).Where(m => !typeof(Delegate).IsAssignableFrom(type) || m.Name == "Invoke").Select(m => "  " + Describe(type, m)).ToList();
            members.Sort(StringComparer.Ordinal);
            types[Describe(type)] = members;
        }
        return [.. types.SelectMany(t => t.Value.Prepend(t.Key))];
    }

    private static bool IsApi(MemberInfo member)
    {
        if (member.GetCustomAttribute<CompilerGeneratedAttribute>() is not null || member.Name.StartsWith('<')) return false;
        return member switch
        {
            MethodBase { IsSpecialName: true } m => m.IsConstructor && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly), // accessors are listed with their property or event
            MethodBase m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly,
            FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
            PropertyInfo p => (p.GetMethod?.IsPublic ?? false) || (p.GetMethod?.IsFamily ?? false) || (p.GetMethod?.IsFamilyOrAssembly ?? false)
                || (p.SetMethod?.IsPublic ?? false) || (p.SetMethod?.IsFamily ?? false) || (p.SetMethod?.IsFamilyOrAssembly ?? false),
            EventInfo e => (e.AddMethod?.IsPublic ?? false) || (e.AddMethod?.IsFamily ?? false),
            Type => false,
            _ => false,
        };
    }

    private static string Describe(Type type)
    {
        var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : typeof(Delegate).IsAssignableFrom(type) ? "delegate" : type.IsValueType ? "struct" : type.IsAbstract && type.IsSealed ? "static class"
            : type.IsAbstract ? "abstract class" : type.IsSealed ? "sealed class" : "class";
        var sb = new StringBuilder("type ").Append(Name(type)).Append(" : ").Append(kind);
        if (type.BaseType is { } b && b != typeof(object) && b != typeof(ValueType) && b != typeof(Enum) && b != typeof(MulticastDelegate)) sb.Append(" : ").Append(Name(b));
        foreach (var i in type.GetInterfaces().Select(Name).Order(StringComparer.Ordinal)) sb.Append(" , ").Append(i);
        return sb.ToString();
    }

    private static string Describe(Type owner, MemberInfo member)
    {
        switch (member)
        {
            case FieldInfo f when f.IsLiteral:
                return $"const {f.Name} : {Name(f.FieldType)} = {Value(f.GetRawConstantValue())}";
            case FieldInfo f:
                return $"field {Access(f.IsPublic)}{(f.IsStatic ? " static" : "")}{(f.IsInitOnly ? " readonly" : "")} {f.Name} : {Name(f.FieldType)}";
            case ConstructorInfo c:
                return $"ctor {Access(c.IsPublic)}({Parameters(c)})";
            case MethodInfo m:
                return $"method {Access(m.IsPublic)}{(m.IsStatic ? " static" : "")}{(m.IsAbstract ? " abstract" : m.IsVirtual && !m.IsFinal ? " virtual" : "")} {m.Name}{Generic(m)}({Parameters(m)}) : {Name(m.ReturnType)}";
            case PropertyInfo p:
            {
                var get = p.GetMethod is { } g && (g.IsPublic || g.IsFamily || g.IsFamilyOrAssembly) ? " get;" : "";
                var set = p.SetMethod is { } s && (s.IsPublic || s.IsFamily || s.IsFamilyOrAssembly) ? (s.ReturnParameter.GetRequiredCustomModifiers().Any(x => x.Name == "IsExternalInit") ? " init;" : " set;") : "";
                var any = p.GetMethod ?? p.SetMethod!;
                return $"property {Access(any.IsPublic)}{(any.IsStatic ? " static" : "")} {p.Name} : {Name(p.PropertyType)} {{{get}{set} }}";
            }
            case EventInfo e:
                return $"event {e.Name} : {Name(e.EventHandlerType!)}";
            default:
                return member.Name;
        }
    }

    private static string Access(bool isPublic) => isPublic ? "public" : "protected";

    private static string Generic(MethodInfo m) => m.IsGenericMethod ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";

    private static string Parameters(MethodBase m) => string.Join(", ", m.GetParameters().Select(p =>
        (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + Name(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = " + Value(p.DefaultValue) : "")));

    private static string Value(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s.Replace("\"", "\\\"") + "\"",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(void)] = "void", [typeof(string)] = "string", [typeof(object)] = "object", [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte", [typeof(short)] = "short",
        [typeof(ushort)] = "ushort", [typeof(int)] = "int", [typeof(uint)] = "uint", [typeof(long)] = "long", [typeof(ulong)] = "ulong", [typeof(float)] = "float", [typeof(double)] = "double",
        [typeof(decimal)] = "decimal", [typeof(char)] = "char",
    };

    private static string Name(Type type)
    {
        if (Keywords.TryGetValue(type, out var keyword)) return keyword;
        if (type.IsByRef) return Name(type.GetElementType()!);
        if (type.IsArray) return Name(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter) return type.Name;
        var name = type.IsNested ? Name(type.DeclaringType!) + "." + type.Name : (type.Namespace is { } ns ? ns + "." : "") + type.Name;
        if (!type.IsGenericType) return name;
        var tick = name.IndexOf('`');
        return (tick < 0 ? name : name[..tick]) + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
    }
}
