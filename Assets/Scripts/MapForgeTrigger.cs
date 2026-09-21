using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One authored entry of a MapForge trigger volume.</summary>
[Serializable]
public sealed class MapForgeTriggerEvent
{
    public enum When { Enter = 0, Exit = 1 }
    public enum Action { Spawn = 0, Damage = 1, Goal = 2, Message = 3, Enable = 4, Disable = 5 }

    public When when = When.Enter;
    public Action action = Action.Message;
    [Tooltip("目标 MapForge 对象 ID，可留空。")]
    public string target = string.Empty;
    public string message = string.Empty;
    [Min(0f)] public float amount = 1f;
    [Min(0f)] public float delay;
}

/// <summary>
/// Authored MapForge trigger volume. The importer adds the matching collider (isTrigger) and
/// registers the authored event list here; gameplay code subscribes to <see cref="Fired"/> for
/// the actions this component does not perform itself (spawn, damage, goal).
/// </summary>
[AddComponentMenu("MapForge/Trigger Volume")]
[DisallowMultipleComponent]
public sealed class MapForgeTrigger : MonoBehaviour
{
    [Tooltip("仅触发一次后自动停用本组件。")]
    public bool once;

    [Tooltip("来自 MapForge 的事件列表。")]
    public MapForgeTriggerEvent[] events = new MapForgeTriggerEvent[0];

    private readonly List<int> consumed = new List<int>();
    private readonly List<PendingInvoke> pending = new List<PendingInvoke>();

    /// <summary>Raised for every authored event that this component forwards to gameplay code.</summary>
    public event Action<MapForgeTriggerEvent> Fired;

    private struct PendingInvoke
    {
        public MapForgeTriggerEvent entry;
        public float dueTime;
        public Collider other;
    }

    /// <summary>Resolves a target ID against the imported scene.</summary>
    public static GameObject FindTarget(string objectId)
    {
        if (string.IsNullOrEmpty(objectId)) return null;
        foreach (var metadata in FindObjectsByType<MapForgeObjectProperties>(FindObjectsSortMode.None))
            if (metadata != null && metadata.objectId == objectId) return metadata.gameObject;
        return null;
    }

    private void OnTriggerEnter(Collider other) => Handle(MapForgeTriggerEvent.When.Enter, other);
    private void OnTriggerExit(Collider other) => Handle(MapForgeTriggerEvent.When.Exit, other);

    private void Handle(MapForgeTriggerEvent.When when, Collider other)
    {
        if (events == null) return;
        for (int i = 0; i < events.Length; i++)
        {
            var entry = events[i];
            if (entry == null || entry.when != when) continue;
            if (consumed.Contains(i)) continue;
            if (entry.delay > 0f) pending.Add(new PendingInvoke { entry = entry, dueTime = Time.time + entry.delay, other = other });
            else Invoke(entry, other);
            if (once) { consumed.Add(i); if (consumed.Count >= events.Length) enabled = false; }
        }
    }

    private void Update()
    {
        if (pending.Count == 0) return;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (pending[i].dueTime > Time.time) continue;
            var invoke = pending[i];
            pending.RemoveAt(i);
            Invoke(invoke.entry, invoke.other);
        }
    }

    private void Invoke(MapForgeTriggerEvent entry, Collider other)
    {
        switch (entry.action)
        {
            case MapForgeTriggerEvent.Action.Message:
                if (!string.IsNullOrEmpty(entry.message)) Debug.Log("MapForge trigger " + name + ": " + entry.message);
                break;
            case MapForgeTriggerEvent.Action.Enable:
            case MapForgeTriggerEvent.Action.Disable:
                var target = FindTarget(entry.target);
                if (target != null) target.SetActive(entry.action == MapForgeTriggerEvent.Action.Enable);
                break;
        }
        Fired?.Invoke(entry);
    }
}
