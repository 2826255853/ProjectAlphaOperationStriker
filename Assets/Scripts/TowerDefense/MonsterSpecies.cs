using System;
using System.Collections.Generic;

/// <summary>
/// 一种怪物的玩法数值定义（血量 / 攻击 / 移速 / 伤害抗性 / 模型缩放）。
/// 数值来源是关卡设计文档《关卡设计示例（15波）》的“怪物详细介绍”，
/// 由编辑器工具 <c>FastRunnerAuthoring</c> 写进对应预制体，运行时只读。
/// 预制体仍是唯一权威的战斗配置；本类型负责“这个怪物应该是什么数值”，
/// 避免同一份数值在生成器、测试和文档里各写一遍。
/// </summary>
public sealed class MonsterSpecies
{
    /// <summary>稳定标识，用于存档、日志与测试查询；不要随手改名。</summary>
    public readonly string Id;

    /// <summary>面向玩家的名字，也是 HUD / 图鉴的显示名。</summary>
    public readonly string DisplayName;

    /// <summary>最大生命值。</summary>
    public readonly float MaxHealth;

    /// <summary>每次近战攻击的伤害。</summary>
    public readonly float AttackDamage;

    /// <summary>两次近战攻击之间的间隔（秒）。</summary>
    public readonly float AttackInterval;

    /// <summary>该怪物默认移动速度（米/秒），由出怪口写入 MonsterPathFollower。</summary>
    public readonly float MoveSpeed;

    /// <summary>模型统一缩放，用来把同一套骨架做出体型差异。</summary>
    public readonly float ModelScale;

    /// <summary>七类伤害抗性百分比，索引与 <see cref="DamageType"/> 的数值一致。</summary>
    public readonly float[] ResistancePercent;

    /// <summary>设计文档里的特性说明，供策划核对与 HUD 提示使用。</summary>
    public readonly string TraitSummary;

    public MonsterSpecies(string id, string displayName, float maxHealth, float attackDamage,
        float attackInterval, float moveSpeed, float modelScale, string traitSummary,
        params (DamageType type, float percent)[] resistances)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("怪物标识不能为空", nameof(id));
        if (maxHealth <= 0f) throw new ArgumentOutOfRangeException(nameof(maxHealth));
        if (attackDamage < 0f) throw new ArgumentOutOfRangeException(nameof(attackDamage));
        if (attackInterval <= 0f) throw new ArgumentOutOfRangeException(nameof(attackInterval));
        if (moveSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(moveSpeed));
        if (modelScale <= 0f) throw new ArgumentOutOfRangeException(nameof(modelScale));

        Id = id;
        DisplayName = displayName;
        MaxHealth = maxHealth;
        AttackDamage = attackDamage;
        AttackInterval = attackInterval;
        MoveSpeed = moveSpeed;
        ModelScale = modelScale;
        TraitSummary = traitSummary ?? string.Empty;

        ResistancePercent = new float[DamageTypeCatalog.Count];
        if (resistances == null) return;
        foreach (var entry in resistances)
        {
            if (!DamageTypeCatalog.IsDefined(entry.type))
                throw new ArgumentOutOfRangeException(nameof(resistances), "未定义的伤害类别");
            ResistancePercent[(int)entry.type] = entry.percent;
        }
    }

    /// <summary>带符号的抗性百分比：正数减伤、负数易伤、0 不变。</summary>
    public float GetResistancePercent(DamageType type)
    {
        return DamageTypeCatalog.IsDefined(type) ? ResistancePercent[(int)type] : 0f;
    }
}

/// <summary>
/// 本关怪物种类的唯一数值表。新增怪物时在这里追加一项，再由对应的
/// Authoring 工具把数值写进预制体；不要在生成器里另写一份数字。
/// </summary>
public static class MonsterSpeciesCatalog
{
    public const string FastRunnerId = "fast_runner";

    /// <summary>
    /// 快速小怪：攻击 8、血量 40、移速快、光属性易伤（关卡设计文档“怪物详细介绍·普通怪·地面”）。
    /// 设计意图：体型小、相对不易命中，逼迫手打玩家停下开火；只靠走位甩不掉它。
    /// </summary>
    public static readonly MonsterSpecies FastRunner = new MonsterSpecies(
        FastRunnerId,
        "快速小怪",
        maxHealth: 40f,
        attackDamage: 8f,
        attackInterval: 0.8f,
        moveSpeed: 4f,
        modelScale: 0.9f,
        traitSummary: "移速快、体型小不易命中；光属性易伤",
        (DamageType.Light, -30f));

    /// <summary>全部已定义种类；顺序稳定，便于测试与图鉴遍历。</summary>
    public static readonly IReadOnlyList<MonsterSpecies> All = new[] { FastRunner };

    /// <summary>按标识查找；未定义时返回 null。</summary>
    public static MonsterSpecies Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var species in All)
        {
            if (string.Equals(species.Id, id, StringComparison.Ordinal))
            {
                return species;
            }
        }

        return null;
    }
}
