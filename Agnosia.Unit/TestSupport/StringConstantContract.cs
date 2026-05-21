using System.Reflection;
using Xunit;

namespace Agnosia.Unit.TestSupport;

internal static class StringConstantContract
{
    public static string[] ValuesOf(Type type)
    {
        return FieldsOf(type)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
    }

    public static IReadOnlyDictionary<string, string> NamesByValueOf(Type type)
    {
        return FieldsOf(type)
            .ToDictionary(
                field => (string)field.GetRawConstantValue()!,
                field => field.Name,
                StringComparer.Ordinal);
    }

    public static void AssertNonEmptyUniqueValues(IEnumerable<string> values)
    {
        var materialized = values.ToArray();

        Assert.NotEmpty(materialized);
        Assert.All(materialized, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        AssertUniqueValues(materialized);
    }

    public static void AssertUniqueValues(IEnumerable<string> values)
    {
        var duplicates = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    private static FieldInfo[] FieldsOf(Type type)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string) && field.IsLiteral)
            .ToArray();
    }
}
