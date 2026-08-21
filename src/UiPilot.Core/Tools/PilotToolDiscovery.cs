using System;
using System.Reflection;
using System.Text.Json;
using UiPilot.Tools;

namespace UiPilot;

/// <summary>
/// Discovers <see cref="PilotToolAttribute"/> methods and registers them on a
/// <see cref="ToolRegistry"/>. Built-in catalog names are never overwritten.
/// </summary>
public static class PilotToolDiscovery
{
    /// <summary>
    /// Scan <paramref name="assembly"/> for public static methods marked
    /// <see cref="PilotToolAttribute"/>. Supported signatures:
    /// <c>(ToolContext, JsonElement)</c>, <c>(JsonElement)</c>, or parameterless.
    /// </summary>
    public static int RegisterFrom(ToolRegistry registry, Assembly? assembly)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (assembly == null) return 0;

        var added = 0;
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<PilotToolAttribute>();
                if (attr == null || string.IsNullOrWhiteSpace(attr.Name))
                    continue;
                if (registry.Contains(attr.Name))
                    continue;

                var handler = Bind(method);
                if (handler == null)
                    continue;

                registry.Register(
                    attr.Name,
                    string.IsNullOrWhiteSpace(attr.Description)
                        ? "Custom app tool '" + attr.Name + "'."
                        : attr.Description!,
                    handler);
                added++;
            }
        }

        return added;
    }

    private static ToolHandler? Bind(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 2 &&
            parameters[0].ParameterType == typeof(ToolContext) &&
            parameters[1].ParameterType == typeof(JsonElement))
        {
            return (ctx, args) => Invoke(method, new object[] { ctx, args });
        }

        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(JsonElement))
            return (_, args) => Invoke(method, new object[] { args });

        if (parameters.Length == 0)
            return (_, _) => Invoke(method, Array.Empty<object>());

        return null;
    }

    private static object? Invoke(MethodInfo method, object[] args)
    {
        try
        {
            return method.Invoke(null, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }
}
