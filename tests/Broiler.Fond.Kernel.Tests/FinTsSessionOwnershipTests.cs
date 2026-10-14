using System.Collections;
using System.Reflection;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsSessionOwnershipTests
{
    internal static void VerifyReleased(object attempt, Action<bool, string> check)
    {
        foreach (var field in attempt.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (field.FieldType.IsValueType || field.Name is "_gate" or "_clock") { continue; }
            var value = field.GetValue(attempt);
            check(value is IList list ? list.Count == 0 : value is null,
                $"Terminal {attempt.GetType().Name} releases {field.Name}.");
        }
    }
}
