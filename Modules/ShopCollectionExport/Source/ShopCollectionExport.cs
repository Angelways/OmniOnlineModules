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

namespace OmniToolbox.TreeHouseOnline;

public sealed class ShopCollectionExport : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "商城物品收录导出",
        Description = "按角色导出库存与已解锁收录，供商城已获取标记油猴脚本导入。",
        Category = ModuleCategory.Item,
        Author = "Angelways"
    };

    private readonly HashSet<string> selected = [];
    private List<OwnedCharacter> characters = [];
    private Dictionary<uint, string> names = [];
    private IEnumerator<Item>? scan;
    private OwnedCharacter? activeSnapshot;
    private OwnedExport? pending;
    private string status = "登录后自动读取角色列表，也可手动刷新。";
    private bool initialized;
    private ulong listedContentId;
    private int scanned;
    private ExportSettings settings = new();
    private string directoryInput = "";
    private string pendingExportPath = "";
    private Task<string?>? folderSelection;
    private Dictionary<uint, IReadOnlyList<uint>> sets = [];
    private HashSet<uint> furniture = [];
    private string ConfigDir => DalamudServices.PluginInterface.GetPluginConfigDirectory();
    private string SettingsPath => Path.Combine(ConfigDir, "ShopCollectionExport.Settings.json");
    private string SnapshotPath => Path.Combine(ConfigDir, "ShopCollectionUnlocks.json");
    private string ExportDirectory => settings.ResolveDirectory(ConfigDir);
    private string ExportPath => Path.Combine(ExportDirectory, "OmniShopOwned.json");
    public override bool HasSettings => true;

    protected override void OnEnable()
    {
        try
        {
            settings = ExportSettings.Load(SettingsPath);
            directoryInput = settings.ResolveDirectory(ConfigDir);
        }
        catch (Exception ex)
        {
            settings = new ExportSettings();
            directoryInput = ExportDirectory;
            status = $"无法读取导出文件夹设置，暂用默认地址：{ex.Message}";
            DalamudServices.PluginLog.Error(ex, "Shop export directory settings failed to load");
        }
        listedContentId = 0;
        DalamudServices.Framework.Update += Update;
    }
    protected override void OnDisable()
    {
        DalamudServices.Framework.Update -= Update;
        listedContentId = 0;
        scan?.Dispose();
        scan = null;
        pending = null;
        activeSnapshot = null;
        pendingExportPath = "";
    }

    public override bool DrawSettings()
    {
        ImGui.TextWrapped("使用说明：选择好角色后导出JSON文件，然后在浏览器打开商城页面后在脚本中导入文件。");
        if (scan is not null) ImGui.TextUnformatted($"正在采集收录：已检查 {scanned} 件物品");
        else
        {
            if (ImGui.Button("读取 / 刷新角色列表")) Refresh();
            if (ImGui.Button("全选")) foreach (var c in characters) selected.Add(c.Id);
            ImGui.SameLine();
            if (ImGui.Button("清空选择")) selected.Clear();
            foreach (var c in characters)
            {
                var value = selected.Contains(c.Id);
                if (ImGui.Checkbox($"{c.Name} @ {c.WorldName}##{c.Id}", ref value))
                {
                    if (value) selected.Add(c.Id); else selected.Remove(c.Id);
                }
            }
            ImGui.BeginDisabled(folderSelection is not null);
            if (ImGui.Button("导出选中角色的收录 JSON")) BeginExport();
            ImGui.EndDisabled();
        }
        ImGui.Separator();
        ImGui.BeginGroup();
        ImGui.TextWrapped(status);
        ImGui.TextUnformatted("导出文件夹");
        ImGui.BeginDisabled(scan is not null || folderSelection is not null);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##ExportDirectory", ref directoryInput, 1024);
        if (ImGui.Button("选择文件夹…"))
        {
            try
            {
                var initial = Directory.Exists(directoryInput) ? directoryInput : ExportDirectory;
                folderSelection = WindowsFolderPicker.SelectAsync(initial, Process.GetCurrentProcess().MainWindowHandle);
                status = "请在 Windows 窗口中选择导出文件夹。";
            }
            catch (Exception ex) { status = $"无法选择文件夹：{ex.Message}"; }
        }
        ImGui.SameLine();
        if (ImGui.Button("保存文件夹地址")) SaveDirectory(directoryInput);
        ImGui.SameLine();
        if (ImGui.Button("恢复默认文件夹")) SaveDirectory("");
        ImGui.EndDisabled();
        ImGui.TextWrapped($"导出位置：{ExportPath}");
        if (ImGui.Button("打开导出文件夹"))
        {
            try
            {
                if (!Directory.Exists(ExportDirectory)) status = "导出文件夹尚未创建，请先导出一次。";
                else Process.Start(new ProcessStartInfo("explorer.exe", File.Exists(ExportPath)
                    ? $"/select,\"{ExportPath}\"" : $"\"{ExportDirectory}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { status = $"无法打开文件夹：{ex.Message}"; }
        }
        ImGui.SameLine();
        if (ImGui.Button("复制地址"))
        {
            ImGui.SetClipboardText(ExportDirectory);
            status = "已复制导出文件夹地址。";
        }
        ImGui.EndGroup();
        return false;
    }

    private void SaveDirectory(string path)
    {
        try
        {
            var next = new ExportSettings { DirectoryPath = path.Trim() };
            var directory = next.ResolveDirectory(ConfigDir);
            next.Save(SettingsPath);
            settings = next;
            directoryInput = directory;
            status = "导出文件夹地址已保存。";
        }
        catch (Exception ex) { status = $"保存地址失败：{ex.Message}"; }
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
            status = $"已读取 {characters.Count} 个角色。房屋仓库另列数量及依据；雇员上架与部队仓库不计入。";
            return true;
        }
        catch (Exception ex)
        {
            status = $"读取失败：{ex.Message}";
            characters = [];
            DalamudServices.PluginLog.Error(ex, "Shop character inventory cache failed to load");
            return false;
        }
    }

    private void BeginExport()
    {
        if (scan is not null) return;
        if (!Refresh()) return;
        if (characters.Count == 0 || selected.Count == 0) { status = "没有选中可导出的角色。"; return; }
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
            status = "正在采集当前角色的已解锁收录，请保持当前角色登录。";
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
                else status = "已取消选择文件夹。";
            }
            catch (Exception ex) { status = $"选择文件夹失败：{ex.Message}"; }
        }
        if (scan is null || activeSnapshot is null) return;
        try
        {
            var player = DalamudServices.PlayerState;
            if (!player.IsLoaded || player.ContentId.ToString() != activeSnapshot.Id)
                throw new InvalidOperationException("角色已切换或退出登录，请重新导出。");
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
        status = $"导出成功：{pending.Characters.Count} 个角色，{pending.Characters.Sum(x => x.Items.Count)} 条收录。请在商城脚本中导入该 JSON。";
        DalamudServices.ChatGUI.Print($"[商城收录导出] {status} 文件：{pendingExportPath}");
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
        status = $"导出失败：{ex.Message}";
        DalamudServices.PluginLog.Error(ex, "Shop collection export failed");
    }
}
