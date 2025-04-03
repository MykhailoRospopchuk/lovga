namespace LovgaSatellite.ActionHolder;

using System.Collections.Concurrent;
using Models;

// TODO: need to come up with something smarter
internal static class ActionHolder
{
    private static ConcurrentDictionary<string, Action<ActionModel>> _actions = new();

    public static bool AddAction(string topic, Action<ActionModel> action)
    {
        return _actions.TryAdd(topic, action);
    }

    public static Action<ActionModel>? GetAction(string topic)
    {
        return _actions.GetValueOrDefault(topic);
    }
}