using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pancake.Shared;

namespace Pancake.Services;

/// <summary>
/// 本地文件的 SHA-256 缓存。按“路径 + 大小 + 修改时间”判断是否需要重算，
/// 同步时每次保存都要比较内容哈希，不能每次都重读大文件。
/// </summary>
public sealed class CloudAssetHasher
{
    private readonly Dictionary<string, (long Length, DateTime Modified, string Sha)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string? Sha256Of(string path)
    {
        try
        {
            FileInfo info = new(path);
            if (!info.Exists) return null;
            if (_cache.TryGetValue(info.FullName, out var cached) && cached.Length == info.Length && cached.Modified == info.LastWriteTimeUtc) return cached.Sha;
            using FileStream stream = info.OpenRead();
            string sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            _cache[info.FullName] = (info.Length, info.LastWriteTimeUtc, sha);
            return sha;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>刚下载并校验过的文件直接登记，避免立刻再算一遍。</summary>
    public void Remember(string path, string sha)
    {
        FileInfo info = new(path);
        if (info.Exists) _cache[info.FullName] = (info.Length, info.LastWriteTimeUtc, sha);
    }
}

/// <summary>项目上传内容：去掉只属于本机的字段，把附件路径换成服务器资源引用。</summary>
public static class CloudPayload
{
    // 不转义中文：内容只作为 JSON 传输和存储，不会嵌进网页源码，体积可以小一半以上。
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// 生成规范化的项目 JSON。mapAsset 把本地附件路径换成 asset: 引用；返回 null 表示该附件无法同步（文件丢失等），路径置空。
    /// 同一份内容总是得到同一串文本，可以直接比较哈希判断是否需要上传。
    /// </summary>
    public static string BuildProjectPayload(ProjectDocument project, Func<string, string?> mapAsset)
    {
        ProjectDocument copy = ProjectStore.Clone(project);
        copy.LinkedFile = null;
        copy.SyncRevision = 0;
        copy.SyncDeleted = false;
        copy.SyncHash = null;
        // 最近使用时间每次切换项目都会变，属于本机状态，不能让它触发上传。
        copy.LastUsedAt = copy.CreatedAt;
        foreach (AttachmentState asset in ProjectStore.Attachments(copy))
            asset.Path = string.IsNullOrWhiteSpace(asset.Path) ? "" : mapAsset(asset.Path) ?? "";
        return JsonSerializer.Serialize(copy, Compact);
    }

    public static string Hash(params string?[] parts)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string? part in parts)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(part ?? "\0"));
            hash.AppendData([0x1F]);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

/// <summary>
/// 班级设置同步：按 SettingsSchema 挑出可在班级内共享的软件设置（布局、外观、主题、提醒与输入习惯等）。
/// 集控凭据、锁定密码、麦克风、备份路径、更新与启动行为等属于本机设置，见 SettingsSchema.DeviceLocalKeys。
/// 同步内容是一份扁平 JSON：键是设置路径（嵌套设置用点号），值与 BoardSettingsState 的序列化结果相同。
/// </summary>
public static class LayoutSync
{
    public static readonly string[] Keys = SettingsSchema.Keys.ToArray();

    /// <summary>含图片或视频路径的背景设置；路径需要换成服务器资源。</summary>
    public static readonly string[] BackgroundKeys = SettingsSchema.BackgroundKeys.ToArray();

    // 不转义中文：内容只作为 JSON 传输和存储，不会嵌进网页源码，体积可以小一半以上。
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// 提取设置 JSON。mapAsset 把本地背景文件换成 asset: 引用；返回 null 时保留原路径
    /// （网页壁纸目录等无法上传的内容），接收端只有在本机也有同一路径时才会使用。
    /// </summary>
    public static string Extract(BoardSettingsState settings, Func<string, string?> mapAsset) =>
        ExtractNode(settings, mapAsset).ToJsonString(Compact);

    public static JsonObject ExtractNode(BoardSettingsState settings, Func<string, string?> mapAsset)
    {
        JsonObject all = JsonSerializer.SerializeToNode(settings, ProjectStore.JsonOptions)!.AsObject();
        JsonObject layout = new();
        foreach (string key in Keys)
            if (SettingsSchema.Get(all, key) is { } value) layout[key] = value.DeepClone();
        layout[nameof(BoardSettingsState.Widgets)] = SharedWidgets(settings);
        foreach (string key in BackgroundKeys)
        {
            if (layout[key] is not JsonObject background) continue;
            if (background["ImagePath"]?.GetValue<string>() is { Length: > 0 } image) background["ImagePath"] = mapAsset(image) ?? image;
            if (background["Playlist"] is JsonArray playlist)
            {
                JsonArray mapped = new();
                foreach (string item in playlist.Select(x => x?.GetValue<string>()).OfType<string>().Where(x => x.Length > 0))
                    mapped.Add(mapAsset(item) ?? item);
                background["Playlist"] = mapped;
            }
        }
        return layout;
    }

    /// <summary>
    /// 把服务器设置合并进本机设置，返回新的设置对象，本机专属字段保持不变。只合并 keys 中的键（为 null 时合并全部）。
    /// 服务器值先按 SettingsSchema 校验，类型不符的键保留本机值。
    /// resolveAsset 把 asset: 引用换成本地文件；无法解析的背景（下载失败、本机没有该路径）保留本机原值，并记入 unresolved。
    /// </summary>
    public static BoardSettingsState Merge(BoardSettingsState current, string layoutJson, Func<string, string?> resolveAsset,
        IReadOnlyCollection<string>? keys = null, ICollection<string>? unresolved = null)
    {
        JsonObject all = JsonSerializer.SerializeToNode(current, ProjectStore.JsonOptions)!.AsObject();
        JsonObject layout = JsonNode.Parse(layoutJson) as JsonObject ?? throw new InvalidDataException("布局不是 JSON 对象。");
        foreach (string key in Keys)
        {
            if (keys is not null && !keys.Contains(key)) continue;
            if (!layout.TryGetPropertyValue(key, out JsonNode? incoming) || SettingsSchema.Find(key) is not { } descriptor) continue;
            if (SettingsSchema.SanitizeValue(descriptor, incoming) is not { } value) continue;
            if (descriptor.Kind == SettingKind.Background && value is JsonObject background && SettingsSchema.Get(all, key) is JsonObject local)
            {
                // 服务器只下发了部分背景字段时，其余字段沿用本机值。
                foreach (var (field, localValue) in local)
                    if (!background.ContainsKey(field)) background[field] = localValue?.DeepClone();
                if (background["ImagePath"]?.GetValue<string>() is { Length: > 0 } image)
                {
                    string? resolved = Resolve(image, resolveAsset);
                    if (resolved is null) unresolved?.Add(key);
                    background["ImagePath"] = resolved ?? local["ImagePath"]?.GetValue<string>() ?? "";
                }
                if (background["Playlist"] is JsonArray playlist)
                {
                    List<string> items = playlist.Select(x => x?.GetValue<string>()).OfType<string>().ToList();
                    List<string> resolvedItems = items.Select(x => Resolve(x, resolveAsset)).OfType<string>().ToList();
                    if (resolvedItems.Count < items.Count) unresolved?.Add(key);
                    JsonArray list = new();
                    if (resolvedItems.Count == 0 && items.Count > 0 && local["Playlist"] is JsonArray keep)
                        foreach (var item in keep) list.Add(item?.DeepClone());
                    else
                        foreach (string item in resolvedItems) list.Add(item);
                    background["Playlist"] = list;
                }
            }
            SettingsSchema.Set(all, key, value);
        }
        return all.Deserialize<BoardSettingsState>(ProjectStore.JsonOptions) ?? throw new InvalidDataException("布局无法应用。");
    }

    /// <summary>
    /// 只同步与屏幕尺寸无关的组件位置：自由布局的位置原样同步；分屏/仅时钟的停靠位置
    /// 每块屏幕会按自己的尺寸重新计算并居中，只有手动调整过的才同步，且不带水平坐标。
    /// 否则尺寸不同的两块大屏会互相覆盖，来回上传。
    /// </summary>
    private static JsonObject SharedWidgets(BoardSettingsState settings)
    {
        JsonObject widgets = new();
        foreach (var (key, placement) in (settings.Widgets ?? []).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            bool docked = key.StartsWith("Split", StringComparison.Ordinal) || key.StartsWith("ClockMode", StringComparison.Ordinal);
            if (docked && settings.CustomizedDockedWidgets?.Contains(key) != true) continue;
            widgets[key] = new JsonObject
            {
                [nameof(RegionPlacement.X)] = docked ? 0 : Math.Round(placement.X, 1),
                [nameof(RegionPlacement.Y)] = Math.Round(placement.Y, 1),
                [nameof(RegionPlacement.Width)] = Math.Round(placement.Width, 1),
                [nameof(RegionPlacement.Height)] = Math.Round(placement.Height, 1)
            };
        }
        return widgets;
    }

    private static string? Resolve(string value, Func<string, string?> resolveAsset)
    {
        if (AssetReference.TryParse(value, out _, out _)) return resolveAsset(value);
        return File.Exists(value) || Directory.Exists(value) ? value : null;
    }

    /// <summary>布局 JSON 里所有 asset: 引用。</summary>
    public static IEnumerable<string> AssetReferences(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root) yield break;
        foreach (string key in BackgroundKeys)
        {
            if (root[key] is not JsonObject background) continue;
            if (background["ImagePath"]?.GetValue<string>() is { } image && AssetReference.TryParse(image, out _, out _)) yield return image;
            if (background["Playlist"] is JsonArray playlist)
                foreach (string item in playlist.Select(x => x?.GetValue<string>()).OfType<string>())
                    if (AssetReference.TryParse(item, out _, out _)) yield return item;
        }
    }
}
