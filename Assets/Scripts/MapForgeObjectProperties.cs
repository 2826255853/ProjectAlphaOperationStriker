using UnityEngine;

/// <summary>One authored per-wave override of a MapForge spawn point.</summary>
[System.Serializable]
public sealed class MapForgeWaveData
{
    public bool enabled = true;
    public int enemyCount = 10;
    [Tooltip("留空表示使用出怪口的默认类型。")]
    public string monsterType = string.Empty;
    [Min(0f)] public float flightHeight = 3f;
}

/// <summary>One authored MapForge trigger event, kept as the strings the map file uses.</summary>
[System.Serializable]
public sealed class MapForgeTriggerEventData
{
    public string when = "enter";
    public string action = "message";
    public string target = string.Empty;
    public string message = string.Empty;
    [Min(0f)] public float amount = 1f;
    [Min(0f)] public float delay;
}

/// <summary>
/// Authored MapForge metadata. Gameplay systems may interpret prefabType and parametersJson.
/// The Unity mapping block mirrors the "unity" section of the map file, so an imported scene
/// still describes where every generated component came from after the JSON is gone.
/// Live inspectors are the real configuration; this mirror records the authored source.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapForgeObjectProperties : MonoBehaviour
{
    public string objectId;
    public string objectType;
    public string[] tags;
    public string prefabType;
    [TextArea(3, 12)] public string parametersJson = "{}";
    public string category;
    public bool visible = true;
    public bool locked;
    public bool collision = true;
    public bool collapsed;

    [Header("Prefab 映射")]
    [Tooltip("MapForge 导出的 Prefab ID。")]
    public string prefabId = string.Empty;
    public string prefabGuid = string.Empty;
    [Tooltip("导入时用来加载 Prefab 的资产路径。")]
    public string prefabAssetPath = string.Empty;

    [Header("Collider 类型")]
    [Tooltip("auto 保留 Unity 图元碰撞体，none 关闭碰撞体。")]
    public string colliderType = "auto";
    public bool colliderIsTrigger;
    public Vector3 colliderCenter = Vector3.zero;
    public Vector3 colliderSize = Vector3.one;
    [Min(0.01f)] public float colliderRadius = .5f;
    [Min(0.01f)] public float colliderHeight = 2f;
    [Range(0, 2)] public int colliderDirection = 1;

    [Header("NavMesh 标记")]
    public string navRole = "none";
    [Range(0, 31)] public int navArea;
    public string navAgentType = "Humanoid";
    public bool navIgnoreFromBuild;
    public bool navApplyToChildren;
    public Vector3 navLinkStart = Vector3.zero;
    public Vector3 navLinkEnd = Vector3.zero;
    [Min(0.01f)] public float navLinkWidth = 1f;
    public bool navBidirectional = true;

    [Header("SpawnPoint 出怪口")]
    public bool spawnPointEnabled;
    [Min(0.01f)] public float spawnInterval = 1f;
    [Min(0f)] public float spawnInitialDelay = 2f;
    [Min(1)] public int spawnEnemiesPerWave = 10;
    [Min(0f)] public float spawnMoveSpeed = 2f;
    public Vector3 spawnTravelDirection = Vector3.forward;
    public string spawnMonsterType = "Ground";
    [Min(0f)] public float spawnFlightHeight = 3f;
    public bool spawnFlyingEntrance;
    [Min(0f)] public float spawnEntranceAltitude = 12f;
    [Tooltip("MapForge 分道 ID，供游戏逻辑读取。")]
    public string spawnLaneId = string.Empty;
    public string spawnEnemyPrefabId = string.Empty;
    public MapForgeWaveData[] spawnWaves = new MapForgeWaveData[0];

    [Header("EnemyCore 核心")]
    public bool enemyCoreEnabled;
    [Min(1f)] public float enemyCoreMaxHealth = 30f;
    [Min(0f)] public float enemyCoreGroundDamage = 2f;
    [Min(0f)] public float enemyCoreFlyingDamage = 1f;
    [Min(0.01f)] public float enemyCoreArrivalRadius = .35f;

    [Header("路径节点")]
    public bool pathNodeEnabled;
    public string pathNodeRole = "node";
    [Min(0f)] public float pathNodeWaitTime;
    [Tooltip("后继节点的 MapForge 对象 ID。")]
    public string[] pathNodeLinks = new string[0];

    [Header("触发器")]
    public bool triggerEnabled;
    public string triggerShape = "box";
    public bool triggerIsTrigger = true;
    public bool triggerOnce;
    public Vector3 triggerCenter = Vector3.zero;
    public Vector3 triggerSize = Vector3.one;
    [Min(0.01f)] public float triggerRadius = 1f;
    public MapForgeTriggerEventData[] triggerEvents = new MapForgeTriggerEventData[0];

    /// <summary>Prefab ID of the mapping, or the object ID when the map did not name one.</summary>
    public string EffectivePrefabId => string.IsNullOrEmpty(prefabId) ? objectId : prefabId;
}
