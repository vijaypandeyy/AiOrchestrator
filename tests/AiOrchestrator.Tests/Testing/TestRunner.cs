using System.Reflection;

namespace AiOrchestrator.Tests.Testing;

/// <summary>Discovers every [Fact]-attributed method in this assembly, via reflection, and runs it.</summary>
public static class TestRunner
{
    public static async Task<int> RunAllAsync()
    {
        var testMethods = Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<FactAttribute>() is not null)
            .OrderBy(m => m.DeclaringType!.Name)
            .ThenBy(m => m.Name)
            .ToList();

        Console.WriteLine($"Running {testMethods.Count} test(s)...\n");

        var passed = 0;
        var failed = 0;

        foreach (var method in testMethods)
        {
            var declaringType = method.DeclaringType!;
            var displayName = $"{declaringType.Name}.{method.Name}";
            object? instance = method.IsStatic ? null : Activator.CreateInstance(declaringType);

            try
            {
                var result = method.Invoke(instance, null);
                if (result is Task task)
                {
                    await task;
                }

                Console.WriteLine($"  PASS  {displayName}");
                passed++;
            }
            catch (Exception ex)
            {
                var actual = ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException : ex;
                Console.WriteLine($"  FAIL  {displayName}");
                Console.WriteLine($"        {actual!.GetType().Name}: {actual.Message}");
                failed++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Total: {passed + failed}   Passed: {passed}   Failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
