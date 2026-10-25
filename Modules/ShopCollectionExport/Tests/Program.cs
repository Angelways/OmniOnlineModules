using System.Text.Json;
using OmniToolbox.ShopOwned;

var count = 0;
void Check(bool valid, string message) { if (!valid) throw new Exception(message); count++; }
var directory = Path.Combine(Path.GetTempPath(), "omni-export-tests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    var settingsPath = Path.Combine(directory, "settings.json");
    var settings = ExportSettings.Load(settingsPath);
    Check(settings.ResolveDirectory(directory) == Path.Combine(directory, "Exports") && !File.Exists(settingsPath),
        "fresh settings use host-relative default without writing a file");
    var customDirectory = Path.Combine(directory, "中文 文件夹", "exports");
    new ExportSettings { DirectoryPath = "  " + customDirectory + "  " }.Save(settingsPath);
    settings = ExportSettings.Load(settingsPath);
    Check(settings.ResolveDirectory(directory) == customDirectory, "custom path with Chinese and spaces survives restart");
    var customFile = Path.Combine(settings.ResolveDirectory(directory), "OmniShopOwned.json");
    ExportCodec.WriteAtomic(customFile, new OwnedExport());
    Check(File.Exists(customFile), "manual export creates selected folder and file");
    var rejectedRelative = false;
    try { new ExportSettings { DirectoryPath = "relative-folder" }.ResolveDirectory(directory); }
    catch (ArgumentException) { rejectedRelative = true; }
    Check(rejectedRelative, "relative folder cannot silently resolve to game's working directory");
    var rejectedFile = false;
    try { new ExportSettings { DirectoryPath = customFile }.ResolveDirectory(directory); }
    catch (ArgumentException) { rejectedFile = true; }
    Check(rejectedFile, "file path is rejected as export directory");
    new ExportSettings().Save(settingsPath);
    Check(ExportSettings.Load(settingsPath).ResolveDirectory(directory) == Path.Combine(directory, "Exports"), "reset default persists");
    var path = Path.Combine(directory, "InventoryCache.json");
    File.WriteAllText(path, """
    {"Characters":{"18014449511650611":{"DisplayName":"示例角色","HomeWorldId":1170,"CapturedLocations":828,
    "SaddlebagItems":[{"ItemID":123,"Quantity":2},{"ItemID":0,"Quantity":999}],
    "RetainerItems":{"33777048205981717":[{"ItemID":1000123,"Quantity":3}]},
    "RetainerMarketItems":{"33777048205981717":[{"ItemID":999,"Quantity":10}]},
    "GlamourDresserItems":[{"ItemID":234,"Quantity":1}],"ArmoireItems":[345]}},
    "FreeCompanyChestItems":[{"ItemID":888,"Quantity":99}]}
    """);
    var characters = ExportCodec.ReadInventory(path, id => $"物品{id}", id => $"世界{id}");
    Check(characters.Count == 1, "character parse");
    var c = characters[0];
    Check(c.Id == "18014449511650611", "content ID precision");
    Check(c.Items.Count == 3, "inventory formats and exclusion of market/shared items");
    Check(c.Items.Single(x=>x.ItemId==123).Quantity == 5, "HQ normalized and quantities merged");
    Check(c.Items.Single(x=>x.ItemId==345).Quantity == 1, "armoire bare ID");
    Check(c.Items.All(x=>!x.Unlocked), "inventory never fabricated as permanent unlock");
    var setsPath = Path.Combine(directory, "sets.json");
    File.WriteAllText(setsPath, """
    {"Characters":{"18014449511650611":{"DisplayName":"示例角色","HomeWorldId":1170,
    "GlamourDresserItems":[{"ItemID":45328,"Quantity":1,"SlotIndex":7,"ContainerType":9999},
    {"ItemID":24153,"Quantity":1,"SlotIndex":7,"ContainerType":9999}],
    "RetainerItems":{"1":[{"ItemID":45328,"Quantity":1,"SlotIndex":7,"ContainerType":10000}],
    "2":[{"ItemID":24153,"Quantity":1,"SlotIndex":7,"ContainerType":10000}]}},
    "2":{"DisplayName":"其他角色","HomeWorldId":1}},"HousingStoreroomOwnerContentId":18014449511650611,
    "HousingInteriorStoreroomItems":[{"ItemID":777,"Quantity":3}],"FreeCompanyChestItems":[{"ItemID":777,"Quantity":99}]}
    """);
    var expanded = ExportCodec.ReadInventory(setsPath, id => $"物品{id}", id => id.ToString(),
        id => id == 45328 ? new uint[] {24153,24154,24155,24156,24157} : Array.Empty<uint>());
    Check(expanded[0].Items.Single(x=>x.ItemId==24153).Quantity == 3, "same-slot components not double counted; retainers remain independent");
    Check(expanded[0].Items.Single(x=>x.ItemId==24154).Quantity == 2, "combined set expands in each physical container");
    Check(expanded[0].Items.Single(x=>x.ItemId==24154).SetItemIds.SequenceEqual(new uint[]{45328}), "combined set provenance");
    Check(expanded[0].HousingItems.Single().Quantity == 3 && expanded[1].HousingItems.Count==0,
        "housing attributed to capturing character only and kept separate from personal ownership");
    var modernPath = Path.Combine(directory, "modern.json");
    File.WriteAllText(modernPath, """
    {"Characters":{"18014449511650611":{"DisplayName":"新版角色","HomeWorldID":1170,
    "InventoryItems":[{"ItemID":1000123,"Quantity":4}],
    "RetainerItems":[{"ItemID":45328,"Quantity":1,"SlotIndex":7,"ContainerType":10000,"HolderID":11},
    {"ItemID":24153,"Quantity":1,"SlotIndex":7,"ContainerType":10000,"HolderID":12}]},
    "2":{"DisplayName":"旧版角色","HomeWorldId":1},"3":{"DisplayName":"其他角色","HomeWorldID":1}},
    "HousingStorerooms":{"estate1":{"CharacterContentIDs":[18014449511650611,"2"],
    "InteriorItems":[{"ItemID":777,"Quantity":3}],"ExteriorItems":[{"ItemID":777,"Quantity":2}]},
    "estate2":{"CharacterContentIDs":[18014449511650611],"InteriorItems":[{"ItemID":777,"Quantity":1}],"ExteriorItems":[]}}}
    """);
    var modern = ExportCodec.ReadInventory(modernPath, id => $"物品{id}", id => id.ToString(),
        id => id == 45328 ? new uint[] {24153} : Array.Empty<uint>());
    Check(modern.Count == 3 && modern[0].HomeWorldId == 1170 && modern[1].HomeWorldId == 1,
        "HomeWorldID and legacy HomeWorldId both parse");
    Check(modern[0].Items.Single(x => x.ItemId == 123).Quantity == 4, "offline characters include cached main inventory");
    Check(modern[0].Items.Single(x => x.ItemId == 24153).Quantity == 2,
        "flat retainer snapshots keep holder boundaries for same-slot set expansion");
    Check(modern[0].HousingItems.Single().Quantity == 6 && modern[1].HousingItems.Single().Quantity == 5 &&
        modern[2].HousingItems.Count == 0 && modern.All(c => c.Items.All(i => i.ItemId != 777)),
        "new housing schema associates each estate with its characters, merges counts, and excludes personal ownership");
    var live = ExportCodec.ReadInventory(modernPath, id => id.ToString(), id => id.ToString(),
        liveCharacterId: "18014449511650611");
    Check(live[0].Items.All(x => x.ItemId != 123) && live[0].Items.Any(x => x.ItemId == 45328),
        "current character's cached main inventory is excluded before live read without dropping retainers");
    var invalidPath = Path.Combine(directory, "missing-world.json");
    File.WriteAllText(invalidPath, """{"Characters":{"1":{"DisplayName":"损坏缓存"}}}""");
    var missingWorldRejected = false;
    try { ExportCodec.ReadInventory(invalidPath, id => "", id => ""); }
    catch (InvalidDataException ex) { missingWorldRejected = ex.Message.Contains("HomeWorldID"); }
    Check(missingWorldRejected, "missing server field reports the unsupported cache field explicitly");
    var snapshot = new OwnedCharacter { Id=c.Id, UnlockCapturedAt=DateTimeOffset.UtcNow,
        Items=[new OwnedItem { ItemId=456, Name="坐骑角笛", Unlocked=true, Quantity=99 }] };
    ExportCodec.MergeUnlocks(c, snapshot);
    Check(c.Items.Single(x=>x.ItemId==456).Quantity == 0, "unlock snapshot does not duplicate inventory");
    Check(c.UnlockCapturedAt is not null, "unlock capture date");
    var output = Path.Combine(directory, "export.json");
    ExportCodec.WriteAtomic(output, new OwnedExport { Characters=characters });
    using var json = JsonDocument.Parse(File.ReadAllText(output));
    Check(json.RootElement.GetProperty("characters")[0].GetProperty("id").ValueKind == JsonValueKind.String, "JSON ID type");
    ExportCodec.WriteAtomic(output, new OwnedExport { Characters=characters });
    Check(Directory.GetFiles(directory, "*.tmp-*").Length == 0, "atomic export cleanup");
    if (args.Length > 0)
    {
        var actual = ExportCodec.ReadInventory(args[0], id => $"物品{id}", id => id.ToString());
        using var cacheDocument = JsonDocument.Parse(File.ReadAllText(args[0]));
        var cachedIds = cacheDocument.RootElement.GetProperty("Characters").EnumerateObject().Select(x => x.Name).ToHashSet();
        Check(actual.Count == cachedIds.Count && actual.All(x=>cachedIds.Contains(x.Id)), "live cache read");
        Check(actual.SelectMany(x=>x.Items).All(x=>x.ItemId>0 && x.Quantity>0), "live item integrity");
        Check(actual.All(c => c.HomeWorldId == (cacheDocument.RootElement.GetProperty("Characters").GetProperty(c.Id)
            .TryGetProperty("HomeWorldID", out var w) ? w : cacheDocument.RootElement.GetProperty("Characters")
            .GetProperty(c.Id).GetProperty("HomeWorldId")).GetUInt32()), "live cache world ID fidelity");
        Console.WriteLine($"Live cache: {actual.Count} characters / {actual.Sum(x=>x.Items.Count)} deduplicated item records.");
    }
    Console.WriteLine($"Passed {count} export checks.");
}
finally { Directory.Delete(directory, true); }
