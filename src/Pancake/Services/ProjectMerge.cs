using System.Text.Json;
using System.Text.Json.Nodes;
using Pancake.Shared;

namespace Pancake.Services;

/// <summary>
/// 项目的按科目三方合并：大屏和网页同时修改同一个项目时，只要改的不是同一个科目，就把两边的修改合在一起，
/// 不再另存“本机副本”。比较使用规范化的上传内容（附件路径已换成资源引用），选用时取本机或服务器的科目对象。
/// </summary>
public static class ProjectMerge
{
    /// <summary>返回合并后的项目（以服务器版本为底）；同一个科目两边都改了、科目重名等无法判断的情况返回 null。</summary>
    public static ProjectDocument? Merge(string basePayload, ProjectDocument local, string localPayload, ProjectDocument remote, string remotePayload)
    {
        JsonObject? b = Parse(basePayload), l = Parse(localPayload), r = Parse(remotePayload);
        if (b is null || l is null || r is null) return null;
        var bs = ByName(b); var ls = ByName(l); var rs = ByName(r);
        if (bs is null || ls is null || rs is null) return null;
        Dictionary<string, SubjectState> localSubjects = local.Subjects.GroupBy(Key).ToDictionary(x => x.Key, x => x.First());
        Dictionary<string, SubjectState> remoteSubjects = remote.Subjects.GroupBy(Key).ToDictionary(x => x.Key, x => x.First());
        if (localSubjects.Count != local.Subjects.Count || remoteSubjects.Count != remote.Subjects.Count) return null;

        // 同一字段：本机没改取服务器，服务器没改或两边改成一样取本机，两边改得不同则无法合并。
        static bool? TakeLocal(JsonNode? baseValue, JsonNode? localValue, JsonNode? remoteValue)
        {
            if (SettingsSchema.Same(localValue, baseValue)) return false;
            if (SettingsSchema.Same(remoteValue, baseValue) || SettingsSchema.Same(localValue, remoteValue)) return true;
            return null;
        }

        ProjectDocument merged = ProjectStore.Clone(remote);
        foreach (string field in new[] { nameof(ProjectDocument.ClockInkStrokes), nameof(ProjectDocument.Theme), nameof(ProjectDocument.Palette) })
        {
            if (TakeLocal(b[field], l[field], r[field]) is not { } takeLocal) return null;
            if (!takeLocal) continue;
            switch (field)
            {
                case nameof(ProjectDocument.ClockInkStrokes): merged.ClockInkStrokes = local.ClockInkStrokes; break;
                case nameof(ProjectDocument.Theme): merged.Theme = local.Theme; break;
                case nameof(ProjectDocument.Palette): merged.Palette = local.Palette; break;
            }
        }

        List<SubjectState> subjects = [];
        IEnumerable<string> order = rs.Keys.Concat(ls.Keys.Where(k => !rs.ContainsKey(k)));
        foreach (string name in order)
        {
            if (TakeLocal(bs.GetValueOrDefault(name), ls.GetValueOrDefault(name), rs.GetValueOrDefault(name)) is not { } takeLocal) return null;
            SubjectState? pick = takeLocal ? localSubjects.GetValueOrDefault(name) : remoteSubjects.GetValueOrDefault(name);
            if (pick is not null) subjects.Add(pick);
        }
        merged.Subjects = subjects;
        return merged;
    }

    private static string Key(SubjectState subject) => subject.Name.Trim();

    private static JsonObject? Parse(string json)
    {
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
    }

    /// <summary>按科目名索引（保持原顺序）；有重名科目时无法可靠匹配，返回 null。</summary>
    private static Dictionary<string, JsonNode?>? ByName(JsonObject doc)
    {
        Dictionary<string, JsonNode?> result = [];
        foreach (JsonObject subject in (doc["Subjects"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string name = (subject["Name"]?.GetValue<string>() ?? "").Trim();
            if (!result.TryAdd(name, subject)) return null;
        }
        return result;
    }
}
