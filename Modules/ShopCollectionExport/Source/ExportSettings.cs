using System.Text.Json;

namespace OmniToolbox.ShopOwned;

public sealed class ExportSettings
{
    public string DirectoryPath { get; set; } = "";

    public string ResolveDirectory(string configDirectory)
    {
        var path = DirectoryPath.Trim();
        if (path.Length == 0) return Path.GetFullPath(Path.Combine(configDirectory, "Exports"));
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("请输入完整的文件夹地址，例如 E:\\OmniExports。");
        if (path.IndexOfAny(['"', '<', '>', '|', '?', '*']) >= 0)
            throw new ArgumentException("文件夹地址包含无效字符。");
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath)) throw new ArgumentException("该地址是文件，请选择文件夹。");
        return fullPath;
    }

    public static ExportSettings Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<ExportSettings>(File.ReadAllText(path), ExportCodec.Options)
            ?? throw new InvalidDataException("导出文件夹设置为空。")
        : new ExportSettings();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this, ExportCodec.Options), new System.Text.UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
