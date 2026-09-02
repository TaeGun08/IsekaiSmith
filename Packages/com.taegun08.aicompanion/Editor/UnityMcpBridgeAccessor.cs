using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// Soft dependency on the MCPForUnity package - reflection instead of a hard `using
// MCPForUnity.Editor.Services;` reference, so this tool still compiles and runs (chat works,
// just no Unity-side tool calls) in a project that doesn't have MCPForUnity installed at all
// (2026-07-23 request: "mcp가 필요없을 수도 있으니깐" - a real scenario once this ships as a
// standalone asset, since buyers won't all have MCPForUnity). All members degrade to a safe
// no-op/false when the package isn't present, rather than throwing.
internal static class UnityMcpBridgeAccessor
{
    private static readonly PropertyInfo BridgeProperty;
    private static readonly PropertyInfo ServerProperty;
    private static readonly PropertyInfo IsRunningProperty;
    private static readonly MethodInfo StartAsyncMethod;
    private static readonly MethodInfo StopAsyncMethod;
    private static readonly MethodInfo IsLocalHttpServerRunningMethod;
    private static readonly MethodInfo StartLocalHttpServerMethod;
    private static readonly MethodInfo StopLocalHttpServerMethod;

    public static bool IsAvailable { get; }

    static UnityMcpBridgeAccessor()
    {
        try
        {
            Type locatorType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeGetTypes)
                .FirstOrDefault(t => t.FullName == "MCPForUnity.Editor.Services.MCPServiceLocator");
            if (locatorType == null)
            {
                return;
            }

            BridgeProperty = locatorType.GetProperty("Bridge", BindingFlags.Public | BindingFlags.Static);
            ServerProperty = locatorType.GetProperty("Server", BindingFlags.Public | BindingFlags.Static);
            Type bridgeInterface = BridgeProperty.PropertyType;
            Type serverInterface = ServerProperty.PropertyType;

            IsRunningProperty = bridgeInterface.GetProperty("IsRunning");
            StartAsyncMethod = bridgeInterface.GetMethod("StartAsync");
            StopAsyncMethod = bridgeInterface.GetMethod("StopAsync");

            IsLocalHttpServerRunningMethod = serverInterface.GetMethod("IsLocalHttpServerRunning");
            StartLocalHttpServerMethod = serverInterface.GetMethod("StartLocalHttpServer");
            StopLocalHttpServerMethod = serverInterface.GetMethod("StopLocalHttpServer");

            IsAvailable = true;
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
    }

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).ToArray();
        }
    }

    private static object GetBridge()
    {
        return BridgeProperty.GetValue(null);
    }

    private static object GetServer()
    {
        return ServerProperty.GetValue(null);
    }

    public static bool IsBridgeRunning
    {
        get
        {
            if (!IsAvailable)
            {
                return false;
            }
            try
            {
                return (bool)IsRunningProperty.GetValue(GetBridge());
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static bool IsLocalHttpServerRunning()
    {
        if (!IsAvailable)
        {
            return false;
        }
        try
        {
            return (bool)IsLocalHttpServerRunningMethod.Invoke(GetServer(), null);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void StartLocalHttpServer()
    {
        if (!IsAvailable)
        {
            return;
        }
        // IServerManagementService.StartLocalHttpServer(bool quiet = false) - reflection can't
        // use the C# default-parameter sugar, so the default is spelled out explicitly here.
        StartLocalHttpServerMethod.Invoke(GetServer(), new object[] { false });
    }

    public static void StopLocalHttpServer()
    {
        if (!IsAvailable)
        {
            return;
        }
        StopLocalHttpServerMethod.Invoke(GetServer(), null);
    }

    public static async Task StartBridgeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }
        // IBridgeControlService.StartAsync() actually returns Task<bool>, but Task<T> derives
        // from Task so the non-generic await here works without needing the bool result.
        Task task = (Task)StartAsyncMethod.Invoke(GetBridge(), null);
        await task;
    }

    public static async Task StopBridgeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }
        Task task = (Task)StopAsyncMethod.Invoke(GetBridge(), null);
        await task;
    }
}
