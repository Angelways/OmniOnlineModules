using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.TreeHouseOnline;

var omniPath = args.ElementAtOrDefault(0) ?? @"D:\XIVLauncherCN\installedPlugins\OmniToolbox\1.1.8.7";
var dalamudPath = args.ElementAtOrDefault(1) ?? @"D:\XIVLauncherCN\Addon\Hooks\26-10-21-01";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var directory in new[] { omniPath, dalamudPath })
    {
        var path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
await Checks.Run();

static class Checks
{
    public static async Task Run()
    {
        var count = 0;
        void Check(bool valid, string message)
        {
            if (!valid) throw new Exception(message);
            count++;
        }
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var type = typeof(ShopCollectionExport);
        var module = new ShopCollectionExport();
        Check(type.Assembly.GetTypes().Count(t => !t.IsAbstract && typeof(OmniModuleBase).IsAssignableFrom(t)) == 1,
            "one concrete module per DLL");
        Check(type.Assembly.GetName().Name == type.Name && type.GetConstructor(Type.EmptyTypes) is not null,
            "file, assembly and entry point names agree; public default constructor");
        typeof(OmniModuleBase).Assembly.GetType("OmniToolbox.Common.Module.Runtime.ModuleContract")!
            .GetMethod("ValidateType", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [type]);
        Check(type.GetCustomAttribute<ModuleApiVersionAttribute>()?.Version == 1, "host API contract");
        Check(module.Localization.IsComplete(UILanguage.SimplifiedChinese)
              && module.Localization.IsComplete(UILanguage.TraditionalChinese), "host generates complete Chinese catalogs");
        Check(!module.Localization.IsComplete(UILanguage.English), "untranslated languages remain unavailable");
        var source = File.ReadAllText(Path.Combine(root, "Source/ShopCollectionExport.cs"));
        foreach (Match match in Regex.Matches(source, "\\b(?:Text|SetStatus)\\(\"([^\"]+)\""))
            Check(module.Localization.TryGet(match.Groups[1].Value, UILanguage.SimplifiedChinese, out _),
                "all UI/status keys resolve: " + match.Groups[1].Value);
        var configField = type.GetField("config", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var config = configField.GetValue(module)!;
        var configType = config.GetType();
        Check(configType.DeclaringType == type && configType.Name == "Settings", "host-bound nested Settings/config");
        var directory = Path.Combine(Path.GetTempPath(), "omni-contract-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var customPath = Path.Combine(directory, "中文 导出");
            File.WriteAllText(Path.Combine(directory, "ShopCollectionExport.Settings.json"),
                JsonSerializer.Serialize(new { directoryPath = customPath }));
            var plugin = DispatchProxy.Create<IDalamudPluginInterface, ServiceProxy>();
            ((ServiceProxy)(object)plugin).ConfigDirectory = directory;
            var framework = DispatchProxy.Create<IFramework, ServiceProxy>();
            var frameworkProxy = (ServiceProxy)(object)framework;
            typeof(DalamudServices).GetProperty("PluginInterface")!.SetValue(null, plugin);
            typeof(DalamudServices).GetProperty("Framework")!.SetValue(null, framework);
            var setEnabled = typeof(OmniModuleBase).GetMethod("SetEnabledAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Func<Action, Task> dispatch = action => { action(); return Task.CompletedTask; };
            async Task Enable(bool value) => await (Task)setEnabled.Invoke(module, [value, dispatch])!;
            for (var iteration = 0; iteration < 2; iteration++)
            {
                await Enable(true);
                Check(module.IsEnabled && frameworkProxy.UpdateHandlers == 1, "one framework subscription per activation");
                Check(ReferenceEquals(configField.GetValue(module), config), "activation preserves host-bound config identity");
                Check((string?)configType.GetProperty("DirectoryPath")!.GetValue(config) == customPath,
                    "legacy directory JSON migrates without loss");
                Check(!Directory.Exists(Path.Combine(directory, "Exports")) && !File.Exists(Path.Combine(customPath, "OmniShopOwned.json")),
                    "enable never automatically exports");
                await Enable(false);
                Check(!module.IsEnabled && frameworkProxy.UpdateHandlers == 0, "lifetime removes framework handler on stop");
            }
            var saveDirectory = type.GetMethod("SaveDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!;
            saveDirectory.Invoke(module, [""]);
            await Enable(true);
            Check((string?)configType.GetProperty("DirectoryPath")!.GetValue(config) == "", "reset default is not undone by legacy import");
            await Enable(false);
            var hostConfig = new TreeHouseConfig();
            Check(ReferenceEquals(config, hostConfig.GetOrCreateModuleConfig(type.Name, config)), "host accepts config serialization contract");
            hostConfig.MoveModuleConfigToRaw(type.Name);
            var restored = hostConfig.GetOrCreateModuleConfig(type.Name, Activator.CreateInstance(configType, true)!);
            Check((bool)configType.GetProperty("LegacySettingsImported")!.GetValue(restored)!, "migration marker survives host save/reload");
            var picker = type.Assembly.GetType("OmniToolbox.ShopOwned.WindowsFolderPicker")!
                .GetMethod("SelectAsync")!;
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var task = (Task<string?>)picker.Invoke(null, [directory, (nint)0, "Test", "Select", cancelled.Token])!;
            try { await task.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("picker did not cancel"); }
            catch (OperationCanceledException) { Check(task.IsCanceled, "picker handles cancellation before startup"); }
            using var duringShow = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            task = (Task<string?>)picker.Invoke(null, [directory, (nint)0, "Omni lifecycle cancellation test", "Select", duringShow.Token])!;
            try { await task.WaitAsync(TimeSpan.FromSeconds(10)); throw new Exception("open picker did not cancel"); }
            catch (OperationCanceledException) { Check(task.IsCanceled, "picker closes and drains native resources during cancellation"); }
            Console.WriteLine($"Passed {count} API/localization/configuration/lifecycle checks against installed Omni.");
        }
        finally { Directory.Delete(directory, true); }
    }
}

public class ServiceProxy : DispatchProxy
{
    public string ConfigDirectory = "";
    public int UpdateHandlers;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        switch (method?.Name)
        {
            case "GetPluginConfigDirectory": return ConfigDirectory;
            case "add_Update": UpdateHandlers++; return null;
            case "remove_Update": UpdateHandlers--; return null;
            default: throw new NotSupportedException(method?.Name);
        }
    }
}
