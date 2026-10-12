using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniToolbox.ShopOwned;

public sealed class OwnedExport
{
    public string Format { get; set; } = "omni-shop-owned";
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Source { get; set; } = "OmniToolbox";
    public string Language { get; set; } = "chs";
    public List<OwnedCharacter> Characters { get; set; } = [];
}

public sealed class OwnedCharacter
{
    // Content IDs exceed JavaScript's safe integer range; always serialize as strings.
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public uint HomeWorldId { get; set; }
    public string WorldName { get; set; } = "";
    public DateTimeOffset? UnlockCapturedAt { get; set; }
    public long CapturedLocations { get; set; }
    public List<OwnedItem> Items { get; set; } = [];
    // These are housing snapshots associated with the capturing character, not personal inventory.
    public List<OwnedItem> HousingItems { get; set; } = [];
}

public sealed class OwnedItem
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = "";
    public bool Unlocked { get; set; }
    public long Quantity { get; set; }
    public List<string> Locations { get; set; } = [];
    public string Category { get; set; } = "";
    public List<uint> SetItemIds { get; set; } = [];
}

public static class ExportCodec
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly string[] InventoryFields =
        ["SaddlebagItems", "RetainerItems", "GlamourDresserItems", "ArmoireItems"];

    public static List<OwnedCharacter> ReadInventory(string path, Func<uint, string> itemName,
        Func<uint, string> worldName, Func<uint, IReadOnlyList<uint>>? setItems = null)
    {
        // Omni may hold the file while updating it. We never write to the original cache.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var json = JsonDocument.Parse(file);
        var result = new List<OwnedCharacter>();
        foreach (var property in json.RootElement.GetProperty("Characters").EnumerateObject())
        {
            var value = property.Value;
            var world = value.GetProperty("HomeWorldId").GetUInt32();
            var character = new OwnedCharacter
            {
                Id = property.Name,
                Name = value.GetProperty("DisplayName").GetString() ?? property.Name,
                HomeWorldId = world,
                WorldName = worldName(world),
                CapturedLocations = value.TryGetProperty("CapturedLocations", out var locations)
                    ? locations.GetInt64() : 0
            };
            foreach (var field in InventoryFields)
            {
                if (!value.TryGetProperty(field, out var array)) continue;
                ReadContainer(character, array, field, itemName, setItems);
            }
            result.Add(character);
        }
        if (json.RootElement.TryGetProperty("HousingStoreroomOwnerContentId", out var owner))
        {
            var character = result.Find(c => c.Id == owner.ToString());
            if (character is not null)
            {
                var housing = new OwnedCharacter();
                foreach (var field in new[] { "HousingInteriorStoreroomItems", "HousingExteriorStoreroomItems", "ApartmentStoreroomItems" })
                    if (json.RootElement.TryGetProperty(field, out var value))
                        ReadContainer(housing, value, field, itemName, null);
                character.HousingItems = housing.Items;
            }
        }
        return result;
    }

    private static void ReadContainer(OwnedCharacter character, JsonElement value, string location,
        Func<uint, string> itemName, Func<uint, IReadOnlyList<uint>>? setItems)
    {
        // Keep retainer boundaries and physical slots when expanding combined glamour sets.
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
                ReadContainer(character, property.Value, location + ":" + property.Name, itemName, setItems);
            return;
        }
        var rows = EnumerateInventory(value).ToList();
        uint Id(JsonElement r) => (r.ValueKind == JsonValueKind.Number ? r.GetUInt32() : r.GetProperty("ItemID").GetUInt32()) % 1_000_000;
        string? Slot(JsonElement r) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("SlotIndex", out var slot)
            ? $"{(r.TryGetProperty("ContainerType", out var type) ? type.ToString() : "")}/{slot}" : null;
        foreach (var row in rows)
        {
            var id = Id(row);
            var quantity = row.ValueKind == JsonValueKind.Number ? 1 : row.GetProperty("Quantity").GetInt64();
            if (id == 0 || quantity <= 0) continue;
            AddItem(character, id, itemName(id), quantity, location);
            foreach (var component in setItems?.Invoke(id) ?? [])
            {
                // Omni can already contain components with the same slot as the combined item.
                if (Slot(row) is { } slot && rows.Any(r => Id(r) == component && Slot(r) == slot)) continue;
                AddItem(character, component, itemName(component), quantity, location + ":Set");
                var entry = character.Items.Find(x => x.ItemId == component)!;
                if (!entry.SetItemIds.Contains(id)) entry.SetItemIds.Add(id);
            }
        }
    }

    private static IEnumerable<JsonElement> EnumerateInventory(JsonElement value)
    {
        // RetainerItems is keyed by retainer content ID; ArmoireItems contains bare item IDs.
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
                foreach (var item in property.Value.EnumerateArray()) yield return item;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) yield return item;
        else throw new InvalidDataException("不支持的库存缓存结构。");
    }

    public static void AddItem(OwnedCharacter character, uint id, string name, long quantity,
        string location, bool unlocked = false)
    {
        var entry = character.Items.Find(x => x.ItemId == id);
        if (entry is null)
        {
            entry = new OwnedItem { ItemId = id, Name = name };
            character.Items.Add(entry);
        }
        entry.Quantity += quantity;
        entry.Unlocked |= unlocked;
        if (!string.IsNullOrEmpty(location) && !entry.Locations.Contains(location))
            entry.Locations.Add(location);
    }

    public static void MergeUnlocks(OwnedCharacter target, OwnedCharacter snapshot)
    {
        target.UnlockCapturedAt = snapshot.UnlockCapturedAt;
        foreach (var item in snapshot.Items.Where(x => x.Unlocked))
        {
            AddItem(target, item.ItemId, item.Name, 0, "Unlock", true);
            target.Items.Find(x => x.ItemId == item.ItemId)!.Category = item.Category;
        }
    }

    public static void WriteAtomic(string path, OwnedExport export)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(export, Options), new System.Text.UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
