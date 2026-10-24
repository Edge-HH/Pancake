using System.Collections;
using System.Reflection;

namespace Pancake.Services;

/// <summary>
/// 把一份设置的值原地写进另一份设置：嵌套对象和集合保持原实例，只替换内容。
/// 设置页的控件闭包持有背景样式、补全设置等子对象的引用，整体替换设置对象会让这些控件改到旧对象上。
/// </summary>
public static class SettingsCopy
{
    public static void Into(object target, object source)
    {
        foreach (PropertyInfo property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0) continue;
            object? value = property.GetValue(source);
            object? current = property.GetValue(target);
            Type type = property.PropertyType;
            if (value is null || current is null || type.IsValueType || type == typeof(string)) { property.SetValue(target, value); continue; }
            if (ReferenceEquals(value, current)) continue;
            switch (current)
            {
                case IDictionary dictionary when value is IDictionary items:
                    dictionary.Clear();
                    foreach (DictionaryEntry entry in items) dictionary[entry.Key] = entry.Value;
                    break;
                case IList list when value is IEnumerable items:
                    list.Clear();
                    foreach (object? item in items.Cast<object?>().ToList()) list.Add(item);
                    break;
                case IEnumerable when type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>):
                    MethodInfo add = type.GetMethod("Add")!;
                    List<object?> copied = ((IEnumerable)value).Cast<object?>().ToList();
                    type.GetMethod("Clear")!.Invoke(current, null);
                    foreach (object? item in copied) add.Invoke(current, [item]);
                    break;
                default:
                    if (type.IsClass && type.GetConstructor(Type.EmptyTypes) is not null) Into(current, value);
                    else property.SetValue(target, value);
                    break;
            }
        }
    }
}
