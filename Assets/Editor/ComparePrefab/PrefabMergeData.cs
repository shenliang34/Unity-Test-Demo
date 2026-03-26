using UnityEngine;
using System.Collections.Generic;

public enum NodePresence
{
    BothSame,
    BothDifferent,
    OnlyInA,
    OnlyInB,
}

public enum MergeChoice
{
    KeepA,
    KeepB,
    KeepBoth,
    Discard,
}

/// <summary>
/// 对比时可选忽略项（不影响合并保存，仅影响「是否有差异」判定与详情展示）
/// </summary>
public class PrefabCompareOptions
{
    /// <summary>忽略本地位置、旋转、缩放差异</summary>
    public bool IgnoreTransform;

    /// <summary>忽略激活状态（activeSelf）差异</summary>
    public bool IgnoreActive;

    /// <summary>忽略 Layer、Tag 差异</summary>
    public bool IgnoreLayerTag;

    /// <summary>忽略组件序列化字段（「文件内容」）；仍会比较组件类型列表是否一致</summary>
    public bool IgnoreComponentSerializedContent;
}

/// <summary>
/// 一条结构化的属性差异，用于在详情面板中左右对比展示
/// </summary>
public class PropertyDiff
{
    public string Label;
    public string ValueA;
    public string ValueB;
    public bool IsDifferent;

    public PropertyDiff(string label, string a, string b, bool isDiff)
    {
        Label = label;
        ValueA = a;
        ValueB = b;
        IsDifferent = isDiff;
    }
}

/// <summary>
/// 组件级别的差异信息
/// </summary>
public class ComponentDiff
{
    public string ComponentName;
    public bool OnlyInA;
    public bool OnlyInB;
    public List<PropertyDiff> PropertyDiffs = new List<PropertyDiff>();
}

public class MergeNodeData
{
    public int Id;
    public int ParentId = -1;
    public int Depth;

    public string NodeName;
    public string PathInA;
    public string PathInB;

    public NodePresence Presence;
    public MergeChoice Choice;

    /// <summary>
    /// 仅 OnlyInB 有效：用户是否显式点过「保留 B」。父节点选 A 时仍保留 B 独有子树需依赖此标记。
    /// </summary>
    public bool ExplicitKeepB;

    public Transform TransformA;
    public Transform TransformB;

    public List<string> DifferenceDetails = new List<string>();
    public List<PropertyDiff> PropertyDiffs = new List<PropertyDiff>();
    public List<ComponentDiff> ComponentDiffs = new List<ComponentDiff>();
    public List<MergeNodeData> Children = new List<MergeNodeData>();

    public List<string> ComponentsA = new List<string>();
    public List<string> ComponentsB = new List<string>();

    public bool IsActiveA;
    public bool IsActiveB;

    public int ChildCountA;
    public int ChildCountB;
}
