using System.Diagnostics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.ShopOwned;
using OmniToolbox.UI;
using TreeTools.Lifecycle;

namespace OmniToolbox.TreeHouseOnline;

[ModuleApiVersion(1)]
public sealed class ShopCollectionExport : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item,
        Author = "Angelways"
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.Create(
        new Dictionary<string, string>
        {
            ["Ready"] = "登录后自动读取角色列表，也可手动刷新。",
            ["Title"] = "商城物品收录导出",
            ["Description"] = "按角色导出库存与已解锁收录，供商城已获取标记油猴脚本导入。",
            ["Instructions"] = "使用说明：选择好角色后导出JSON文件，然后在浏览器打开商城页面后在脚本中导入文件。",
            ["Scanning"] = "正在采集收录：已检查 {0} 件物品",
            ["Refresh"] = "读取 / 刷新角色列表",
            ["SelectAll"] = "全选",
            ["ClearSelection"] = "清空选择",
            ["Export"] = "导出选中角色的收录 JSON",
            ["ExportFolder"] = "导出文件夹",
            ["ChooseFolder"] = "选择文件夹…",
            ["SaveFolder"] = "保存文件夹地址",
            ["DefaultFolder"] = "恢复默认文件夹",
            ["ExportLocation"] = "导出位置：{0}",
            ["OpenFolder"] = "打开导出文件夹",
            ["CopyPath"] = "复制地址",
            ["SettingsReadFailed"] = "无法读取导出文件夹设置，暂用默认地址。详情请查看日志。",
            ["ChooseFolderPrompt"] = "请在 Windows 窗口中选择导出文件夹。",
            ["ChooseFolderFailed"] = "无法选择文件夹。详情请查看日志。",
            ["FolderNotCreated"] = "导出文件夹尚未创建，请先导出一次。",
            ["OpenFolderFailed"] = "无法打开文件夹。详情请查看日志。",
            ["PathCopied"] = "已复制导出文件夹地址。",
            ["FolderSaved"] = "导出文件夹地址已保存。",
            ["SaveFolderFailed"] = "保存地址失败，请检查文件夹地址和访问权限。详情请查看日志。",
            ["CharactersLoaded"] = "已读取 {0} 个角色。房屋仓库另列数量及依据；雇员上架与部队仓库不计入。",
            ["ReadFailed"] = "读取失败。详情请查看日志。",
            ["NoCharacters"] = "没有选中可导出的角色。",
            ["Capturing"] = "正在采集当前角色的已解锁收录，请保持当前角色登录。",
            ["ChooseFolderCancelled"] = "已取消选择文件夹。",
            ["CharacterChanged"] = "角色已切换或退出登录，请重新导出。",
            ["ExportComplete"] = "导出成功：{0} 个角色，{1} 条收录。请在商城脚本中导入该 JSON。",
            ["ChatExportComplete"] = "[{0}] {1} 文件：{2}",
            ["ExportFailed"] = "导出失败。详情请查看日志。",
            ["FolderDialogTitle"] = "选择商城收录导出文件夹",
            ["FolderDialogAccept"] = "选择文件夹",
        });

    private readonly HashSet<string> selected = [];
    private List<OwnedCharacter> characters = [];
    private Dictionary<uint, string> names = [];
    private IEnumerator<Item>? scan;
    private OwnedCharacter? activeSnapshot;
    private OwnedExport? pending;
    private string statusKey = "Ready";
    private object[] statusArguments = [];
    private string Status => Text(statusKey, statusArguments);
    private string Text(string key, params object[] arguments) => string.Format(OmniLoc.Get(this, key), arguments);
    private void SetStatus(string key, params object[] arguments) { statusKey = key; statusArguments = arguments; }
    private bool initialized;
    private ulong listedContentId;
    private int scanned;
    private Settings config = new();
    private bool configChanged;
    private sealed class Settings
    {
        public string DirectoryPath { get; set; } = "";
        public bool LegacySettingsImported { get; set; }
    }
    private string directoryInput = "";
    private string pendingExportPath = "";
    private Task<string?>? folderSelection;
    private Dictionary<uint, IReadOnlyList<uint>> sets = [];
    private HashSet<uint> furniture = [];
    private string ConfigDir => DalamudServices.PluginInterface.GetPluginConfigDirectory();
    private string SettingsPath => Path.Combine(ConfigDir, "ShopCollectionExport.Settings.json");
    private string SnapshotPath => Path.Combine(ConfigDir, "ShopCollectionUnlocks.json");
    private string ExportDirectory => new ExportSettings { DirectoryPath = config.DirectoryPath }.ResolveDirectory(ConfigDir);
    private string ExportPath => Path.Combine(ExportDirectory, "OmniShopOwned.json");
    public override bool HasSettings => true;

    public override bool ResetSettings()
    {
        if (config.DirectoryPath.Length == 0) return false;
        config.DirectoryPath = "";
        config.LegacySettingsImported = true;
        directoryInput = ExportDirectory;
        SetStatus("FolderSaved");
        return true;
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        SetStatus("Ready");
        try
        {
            if (!config.LegacySettingsImported)
            {
                if (config.DirectoryPath.Length == 0 && File.Exists(SettingsPath))
                    config.DirectoryPath = ExportSettings.Load(SettingsPath).DirectoryPath;
                config.LegacySettingsImported = true;
                configChanged = true;
            }
            directoryInput = new ExportSettings { DirectoryPath = config.DirectoryPath }.ResolveDirectory(ConfigDir);
        }
        catch (Exception ex)
        {
            config.DirectoryPath = "";
            configChanged = true;
            directoryInput = ExportDirectory;
            SetStatus("SettingsReadFailed");
            DalamudServices.PluginLog.Error(ex, "Shop export directory settings failed to load");
        }
        listedContentId = 0;
        var framework = DalamudServices.Framework;
        activation.OnStopping(() => framework.Update -= Update);
        framework.Update += Update;
    }
    protected override void OnStop()
    {
        listedContentId = 0;
        scan?.Dispose();
        scan = null;
        pending = null;
        activeSnapshot = null;
        pendingExportPath = "";
        folderSelection = null;
    }

    public override bool DrawSettings()
    {
        ImGui.TextWrapped(Text("Instructions"));
        if (scan is not null) ImGui.TextUnformatted(Text("Scanning", scanned));
        else
        {
            if (ImGui.Button(Text("Refresh"))) Refresh();
            if (ImGui.Button(Text("SelectAll"))) foreach (var c in characters) selected.Add(c.Id);
            ImGui.SameLine();
            if (ImGui.Button(Text("ClearSelection"))) selected.Clear();
            foreach (var c in characters)
            {
                var value = selected.Contains(c.Id);
                if (ImGui.Checkbox($"{c.Name} @ {c.WorldName}##{c.Id}", ref value))
                {
                    if (value) selected.Add(c.Id); else selected.Remove(c.Id);
                }
            }
            ImGui.BeginDisabled(folderSelection is not null);
            if (ImGui.Button(Text("Export"))) BeginExport();
            ImGui.EndDisabled();
        }
        ImGui.Separator();
        ImGui.BeginGroup();
        ImGui.TextWrapped(Status);
        ImGui.TextUnformatted(Text("ExportFolder"));
        ImGui.BeginDisabled(scan is not null || folderSelection is not null);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##ExportDirectory", ref directoryInput, 1024);
        if (ImGui.Button(Text("ChooseFolder")))
        {
            try
            {
                var initial = Directory.Exists(directoryInput) ? directoryInput : ExportDirectory;
                folderSelection = WindowsFolderPicker.SelectAsync(initial, Process.GetCurrentProcess().MainWindowHandle,
                    Text("FolderDialogTitle"), Text("FolderDialogAccept"), Activation.CancellationToken);
                Activation.Track(folderSelection);
                SetStatus("ChooseFolderPrompt");
            }
            catch (Exception ex) { SetStatus("ChooseFolderFailed"); DalamudServices.PluginLog.Error(ex, "Folder selection failed"); }
        }
        ImGui.SameLine();
        if (ImGui.Button(Text("SaveFolder"))) SaveDirectory(directoryInput);
        ImGui.SameLine();
        if (ImGui.Button(Text("DefaultFolder"))) SaveDirectory("");
        ImGui.EndDisabled();
        ImGui.TextWrapped(Text("ExportLocation", ExportPath));
        if (ImGui.Button(Text("OpenFolder")))
        {
            try
            {
                if (!Directory.Exists(ExportDirectory)) SetStatus("FolderNotCreated");
                else Process.Start(new ProcessStartInfo("explorer.exe", File.Exists(ExportPath)
                    ? $"/select,\"{ExportPath}\"" : $"\"{ExportDirectory}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { SetStatus("OpenFolderFailed"); DalamudServices.PluginLog.Error(ex, "Opening export folder failed"); }
        }
        ImGui.SameLine();
        if (ImGui.Button(Text("CopyPath")))
        {
            ImGui.SetClipboardText(ExportDirectory);
            SetStatus("PathCopied");
        }
        ImGui.EndGroup();
        var changed = configChanged;
        configChanged = false;
        return changed;
    }

    private void SaveDirectory(string path)
    {
        try
        {
            var next = new ExportSettings { DirectoryPath = path.Trim() };
            var directory = next.ResolveDirectory(ConfigDir);
            if (config.DirectoryPath != next.DirectoryPath)
            {
                config.DirectoryPath = next.DirectoryPath;
                configChanged = true;
            }
            directoryInput = directory;
            SetStatus("FolderSaved");
        }
        catch (Exception ex) { SetStatus("SaveFolderFailed"); DalamudServices.PluginLog.Error(ex, "Saving export folder failed"); }
    }

    private string ItemName(uint id) => names.GetValueOrDefault(id, "");
    private string WorldName(uint id) => DalamudServices.DataManager.GetExcelSheet<World>()?.GetRowOrDefault(id)?.Name.ExtractText() ?? id.ToString();

    private bool Refresh()
    {
        try
        {
            if (names.Count == 0)
            {
                names = DalamudServices.DataManager.GetExcelSheet<Item>()!
                    .Where(x => !x.Name.IsEmpty).ToDictionary(x => x.RowId, x => x.Name.ExtractText());
                sets = DalamudServices.DataManager.GetExcelSheet<MirageStoreSetItem>()!
                    .Where(x => x.RowId != 0).ToDictionary(x => x.RowId, x => (IReadOnlyList<uint>)new[] {
                        x.MainHand.RowId, x.OffHand.RowId, x.Head.RowId, x.Body.RowId, x.Hands.RowId,
                        x.Legs.RowId, x.Feet.RowId, x.Earrings.RowId, x.Necklace.RowId, x.Bracelets.RowId,
                        x.Ring.RowId }.Where(id => id != 0).Distinct().ToArray());
                furniture = DalamudServices.DataManager.GetExcelSheet<HousingFurniture>()!.Select(x => x.Item.RowId)
                    .Concat(DalamudServices.DataManager.GetExcelSheet<HousingYardObject>()!.Select(x => x.Item.RowId))
                    .Where(id => id != 0).ToHashSet();
            }
            var player = DalamudServices.PlayerState;
            var cachePath = Path.Combine(ConfigDir, "InventoryCache.json");
            characters = File.Exists(cachePath)
                ? ExportCodec.ReadInventory(cachePath, ItemName, WorldName,
                    id => sets.GetValueOrDefault(id, Array.Empty<uint>()),
                    player.IsLoaded ? player.ContentId.ToString() : null)
                : [];
            if (player.IsLoaded && player.ContentId != 0 && characters.All(x => x.Id != player.ContentId.ToString()))
                characters.Add(new OwnedCharacter { Id = player.ContentId.ToString(), Name = player.CharacterName,
                    HomeWorldId = player.HomeWorld.RowId, WorldName = WorldName(player.HomeWorld.RowId) });
            if (!initialized) foreach (var c in characters) selected.Add(c.Id);
            initialized = true;
            SetStatus("CharactersLoaded", characters.Count);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("ReadFailed");
            characters = [];
            DalamudServices.PluginLog.Error(ex, "Shop character inventory cache failed to load");
            return false;
        }
    }

    private void BeginExport()
    {
        if (scan is not null) return;
        if (!Refresh()) return;
        if (characters.Count == 0 || selected.Count == 0) { SetStatus("NoCharacters"); return; }
        try
        {
            pendingExportPath = ExportPath;
            var snapshots = File.Exists(SnapshotPath)
                ? JsonSerializer.Deserialize<OwnedExport>(File.ReadAllText(SnapshotPath), ExportCodec.Options) ?? new OwnedExport()
                : new OwnedExport();
            pending = new OwnedExport { Characters = characters.Where(c => selected.Contains(c.Id)).ToList() };
            foreach (var c in pending.Characters)
            {
                var old = snapshots.Characters.Find(x => x.Id == c.Id);
                if (old is not null) ExportCodec.MergeUnlocks(c, old);
            }
            var player = DalamudServices.PlayerState;
            activeSnapshot = player.IsLoaded && player.ContentId != 0
                ? pending.Characters.Find(c => c.Id == player.ContentId.ToString()) : null;
            if (activeSnapshot is null) { FinishExport(); return; }
            // Live inventory is not in Omni's persisted cache. Read it on the framework thread only.
            foreach (var type in new[] { GameInventoryType.Inventory1, GameInventoryType.Inventory2,
                GameInventoryType.Inventory3, GameInventoryType.Inventory4, GameInventoryType.EquippedItems,
                GameInventoryType.ArmoryMainHand, GameInventoryType.ArmoryOffHand, GameInventoryType.ArmoryHead,
                GameInventoryType.ArmoryBody, GameInventoryType.ArmoryHands, GameInventoryType.ArmoryLegs,
                GameInventoryType.ArmoryFeets, GameInventoryType.ArmoryEar, GameInventoryType.ArmoryNeck,
                GameInventoryType.ArmoryWrist, GameInventoryType.ArmoryRings })
            foreach (var item in OmenTools.DService.Instance().GameInventory.GetInventoryItems(type))
            {
                if (item.ItemId == 0 || item.Quantity == 0) continue;
                var id = item.ItemId % 1_000_000;
                ExportCodec.AddItem(activeSnapshot, id, ItemName(id), item.Quantity, type.ToString());
                foreach (var component in sets.GetValueOrDefault(id, Array.Empty<uint>()))
                {
                    ExportCodec.AddItem(activeSnapshot, component, ItemName(component), item.Quantity, type + ":Set");
                    activeSnapshot.Items.Find(x => x.ItemId == component)!.SetItemIds.Add(id);
                }
            }
            scanned = 0;
            scan = DalamudServices.DataManager.GetExcelSheet<Item>()!.GetEnumerator();
            SetStatus("Capturing");
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Update(IFramework _)
    {
        // Wait for player state to be ready; this also covers enabling the module while logged in.
        // Reading the list is silent and never starts unlock capture or writes an export.
        var loginPlayer = DalamudServices.PlayerState;
        var contentId = loginPlayer.IsLoaded ? loginPlayer.ContentId : 0;
        if (contentId == 0) listedContentId = 0;
        else if (contentId != listedContentId && scan is null)
        {
            listedContentId = contentId;
            Refresh();
        }
        if (folderSelection is { IsCompleted: true } selection)
        {
            folderSelection = null;
            try
            {
                var directory = selection.GetAwaiter().GetResult();
                if (directory is not null) SaveDirectory(directory);
                else SetStatus("ChooseFolderCancelled");
            }
            catch (Exception ex) { SetStatus("ChooseFolderFailed"); DalamudServices.PluginLog.Error(ex, "Folder selection failed"); }
        }
        if (scan is null || activeSnapshot is null) return;
        try
        {
            var player = DalamudServices.PlayerState;
            if (!player.IsLoaded || player.ContentId.ToString() != activeSnapshot.Id)
                throw new InvalidOperationException("CharacterChanged");
            // Bound the work per frame; do not access game data from a background thread.
            for (var i = 0; i < 256; i++)
            {
                if (!scan.MoveNext())
                {
                    scan.Dispose(); scan = null;
                    activeSnapshot.UnlockCapturedAt = DateTimeOffset.UtcNow;
                    FinishExport();
                    return;
                }
                var row = scan.Current;
                scanned++;
                if (row.RowId == 0 || row.Name.IsEmpty) continue;
                if (DalamudServices.UnlockState.IsItemUnlockable(row) && DalamudServices.UnlockState.IsItemUnlocked(row))
                    ExportCodec.AddItem(activeSnapshot, row.RowId, ItemName(row.RowId), 0, "Unlock", true);
            }
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void FinishExport()
    {
        if (pending is null) return;
        foreach (var c in pending.Characters)
        foreach (var item in c.Items.Concat(c.HousingItems))
            item.Category = Category(item.ItemId);
        if (activeSnapshot is not null)
        {
            var saved = File.Exists(SnapshotPath)
                ? JsonSerializer.Deserialize<OwnedExport>(File.ReadAllText(SnapshotPath), ExportCodec.Options) ?? new OwnedExport()
                : new OwnedExport();
            saved.Characters.RemoveAll(x => x.Id == activeSnapshot.Id);
            saved.Characters.Add(new OwnedCharacter { Id = activeSnapshot.Id, Name = activeSnapshot.Name,
                HomeWorldId = activeSnapshot.HomeWorldId, WorldName = activeSnapshot.WorldName,
                UnlockCapturedAt = activeSnapshot.UnlockCapturedAt,
                Items = activeSnapshot.Items.Where(x => x.Unlocked).Select(x => new OwnedItem
                { ItemId = x.ItemId, Name = x.Name, Unlocked = true, Category = x.Category, Locations = ["Unlock"] }).ToList() });
            ExportCodec.WriteAtomic(SnapshotPath, saved);
        }
        foreach (var c in pending.Characters) c.Items = c.Items.OrderBy(x => x.ItemId).ToList();
        ExportCodec.WriteAtomic(pendingExportPath, pending);
        SetStatus("ExportComplete", pending.Characters.Count, pending.Characters.Sum(x => x.Items.Count));
        DalamudServices.ChatGUI.Print(Text("ChatExportComplete", Text("Title"), Status, pendingExportPath));
        pending = null; activeSnapshot = null;
        pendingExportPath = "";
    }

    private string Category(uint id)
    {
        if (furniture.Contains(id)) return "housing";
        var row = DalamudServices.DataManager.GetExcelSheet<Item>()!.GetRowOrDefault(id);
        if (row is null) return "item";
        var name = ItemName(id);
        if (name.StartsWith("演技教材")) return "emote";
        if (name.StartsWith("美容师") || name.StartsWith("发型样式")) return "hairstyle";
        if (row.Value.ItemAction.RowId == 0) return sets.ContainsKey(id) ? "glamourSet" : "item";
        return row.Value.ItemAction.Value.Action.RowId switch
        {
            853 => "minion", 1013 => "barding", 1322 => "mount", 20086 => "fashionAccessory",
            25183 => "orchestrion", 2633 => "unlockLink", _ => "item"
        };
    }

    private void Fail(Exception ex)
    {
        scan?.Dispose(); scan = null; pending = null; activeSnapshot = null;
        pendingExportPath = "";
        SetStatus(ex is InvalidOperationException { Message: "CharacterChanged" } ? "CharacterChanged" : "ExportFailed");
        DalamudServices.PluginLog.Error(ex, "Shop collection export failed");
    }
}
