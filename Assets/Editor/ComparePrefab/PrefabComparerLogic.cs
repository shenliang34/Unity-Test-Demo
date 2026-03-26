using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Prefab 节点对比匹配与合并逻辑
/// </summary>
public class PrefabComparerLogic
{
    private int _nextId;
    private PrefabCompareOptions _options = new PrefabCompareOptions();

    public MergeNodeData BuildMergeTree(Transform rootA, Transform rootB, PrefabCompareOptions options = null)
    {
        _options = options ?? new PrefabCompareOptions();
        _nextId = 0;
        return BuildMergeNodeRecursive(rootA, rootB, -1, 0);
    }

    private MergeNodeData BuildMergeNodeRecursive(Transform tA, Transform tB, int parentId, int depth)
    {
        var node = new MergeNodeData
        {
            Id = _nextId++,
            ParentId = parentId,
            Depth = depth,
        };

        if (tA != null && tB != null)
        {
            node.NodeName = tA.name;
            node.PathInA = GetPath(tA);
            node.PathInB = GetPath(tB);
            node.TransformA = tA;
            node.TransformB = tB;
            node.IsActiveA = tA.gameObject.activeSelf;
            node.IsActiveB = tB.gameObject.activeSelf;
            node.ComponentsA = GetComponentNames(tA);
            node.ComponentsB = GetComponentNames(tB);
            node.ChildCountA = tA.childCount;
            node.ChildCountB = tB.childCount;

            bool hasDiff = false;

            hasDiff |= AddPropertyDiff(node, "名称", tA.name, tB.name);
            if (!_options.IgnoreActive)
                hasDiff |= AddPropertyDiff(node, "激活状态", tA.gameObject.activeSelf.ToString(), tB.gameObject.activeSelf.ToString());
            if (!_options.IgnoreLayerTag)
            {
                hasDiff |= AddPropertyDiff(node, "Layer", LayerMask.LayerToName(tA.gameObject.layer), LayerMask.LayerToName(tB.gameObject.layer));
                hasDiff |= AddPropertyDiff(node, "Tag", tA.gameObject.tag, tB.gameObject.tag);
            }
            if (!_options.IgnoreTransform)
            {
                hasDiff |= AddVectorDiff(node, "Position", tA.localPosition, tB.localPosition);
                hasDiff |= AddVectorDiff(node, "Rotation", tA.localEulerAngles, tB.localEulerAngles);
                hasDiff |= AddVectorDiff(node, "Scale", tA.localScale, tB.localScale);
            }
            hasDiff |= AddPropertyDiff(node, "子节点数", tA.childCount.ToString(), tB.childCount.ToString());

            hasDiff |= BuildComponentDiffs(tA, tB, node);

            node.Presence = hasDiff ? NodePresence.BothDifferent : NodePresence.BothSame;
            node.Choice = MergeChoice.KeepA;

            MatchAndBuildChildren(tA, tB, node);
        }
        else if (tA != null)
        {
            node.NodeName = tA.name;
            node.PathInA = GetPath(tA);
            node.TransformA = tA;
            node.IsActiveA = tA.gameObject.activeSelf;
            node.ComponentsA = GetComponentNames(tA);
            node.ChildCountA = tA.childCount;
            node.Presence = NodePresence.OnlyInA;
            node.Choice = MergeChoice.KeepA;

            BuildSingleSidePropertyDiffs(node, tA, true);

            for (int i = 0; i < tA.childCount; i++)
                node.Children.Add(BuildMergeNodeRecursive(tA.GetChild(i), null, node.Id, depth + 1));
        }
        else if (tB != null)
        {
            node.NodeName = tB.name;
            node.PathInB = GetPath(tB);
            node.TransformB = tB;
            node.IsActiveB = tB.gameObject.activeSelf;
            node.ComponentsB = GetComponentNames(tB);
            node.ChildCountB = tB.childCount;
            node.Presence = NodePresence.OnlyInB;
            // 默认不勾选「保留 B」，需显式选择才并入
            node.Choice = MergeChoice.Discard;

            BuildSingleSidePropertyDiffs(node, tB, false);

            for (int i = 0; i < tB.childCount; i++)
                node.Children.Add(BuildMergeNodeRecursive(null, tB.GetChild(i), node.Id, depth + 1));
        }

        return node;
    }

    /// <summary>
    /// 用于子节点配对：Hierarchy 里看起来同名，但 <see cref="Transform.name"/> 可能含首尾空格、零宽字符等，
    /// 用精确 == 会拆成「仅 A」「仅 B」两行。此处规范化后再比。
    /// </summary>
    private static string NormalizeNameForMatch(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var s = name.Trim();
        // 常见：零宽空格、BOM（从外部文本粘贴到物体名时）
        s = s.Replace("\u200B", "").Replace("\u200C", "").Replace("\u200D", "").Replace("\uFEFF", "");
        return s;
    }

    private static bool NamesMatchForChildren(Transform a, Transform b)
    {
        return string.Equals(NormalizeNameForMatch(a.name), NormalizeNameForMatch(b.name), System.StringComparison.Ordinal);
    }

    /// <summary>
    /// 按名称匹配两侧子节点，未匹配的标记为仅在一侧存在
    /// </summary>
    private void MatchAndBuildChildren(Transform tA, Transform tB, MergeNodeData parent)
    {
        var childrenA = new List<Transform>();
        var childrenB = new List<Transform>();
        for (int i = 0; i < tA.childCount; i++) childrenA.Add(tA.GetChild(i));
        for (int i = 0; i < tB.childCount; i++) childrenB.Add(tB.GetChild(i));

        var matchedB = new HashSet<int>();
        var pairs = new List<KeyValuePair<Transform, Transform>>();

        // 第一轮：规范化后的名称匹配（同层、按 A 顺序，同名多个则按「第一个未占用的 B」配对）
        for (int i = 0; i < childrenA.Count; i++)
        {
            int bestMatch = -1;
            for (int j = 0; j < childrenB.Count; j++)
            {
                if (matchedB.Contains(j)) continue;
                if (NamesMatchForChildren(childrenA[i], childrenB[j]))
                {
                    bestMatch = j;
                    break;
                }
            }

            if (bestMatch >= 0)
            {
                pairs.Add(new KeyValuePair<Transform, Transform>(childrenA[i], childrenB[bestMatch]));
                matchedB.Add(bestMatch);
            }
            else
            {
                pairs.Add(new KeyValuePair<Transform, Transform>(childrenA[i], null));
            }
        }

        // B 侧未匹配的节点
        for (int j = 0; j < childrenB.Count; j++)
        {
            if (!matchedB.Contains(j))
                pairs.Add(new KeyValuePair<Transform, Transform>(null, childrenB[j]));
        }

        foreach (var pair in pairs)
        {
            parent.Children.Add(BuildMergeNodeRecursive(pair.Key, pair.Value, parent.Id, parent.Depth + 1));
        }
    }

    /// <summary>
    /// 基于 Prefab A 进行增量修改，保留 A 的原始结构和引用关系。
    /// prefabAPath: A 的资源路径，用于 LoadPrefabContents 获取可编辑实例。
    /// 返回修改后的根 GameObject（调用方需要 SavePrefabContents 或 SaveAsPrefabAsset）。
    /// </summary>
    public GameObject BuildMergedOnPrefabA(string prefabAPath, MergeNodeData root)
    {
        var editableRoot = PrefabUtility.LoadPrefabContents(prefabAPath);
        if (editableRoot == null) return null;

        ApplyMergeToNode(editableRoot.transform, root);
        return editableRoot;
    }

    /// <summary>
    /// 检查节点或其子树中是否有任何需要保留（非 Discard）的节点
    /// </summary>
    private bool HasAnyKeptDescendant(MergeNodeData node)
    {
        if (node.Choice != MergeChoice.Discard)
            return true;
        foreach (var child in node.Children)
        {
            if (HasAnyKeptDescendant(child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 在可编辑的 A 节点上递归应用合并决策。
    /// 即使当前节点是 Discard，也会检查子树中是否有需要保留的节点。
    /// </summary>
    private void ApplyMergeToNode(Transform currentA, MergeNodeData node)
    {
        if (node.Choice != MergeChoice.Discard && node.Presence == NodePresence.BothDifferent && node.TransformB != null)
        {
            if (node.Choice == MergeChoice.KeepB)
            {
                CopyNodeProperties(node.TransformB, currentA);
            }
            else if (node.Choice == MergeChoice.KeepBoth)
            {
                // KeepBoth 在根节点级别：保留 A 不动，把 B 的属性覆盖到 A（两者合一）
                // 子节点层面才真正体现"两边都保留"
            }
        }

        var toRemove = new List<Transform>();
        var toAdd = new List<MergeNodeData>();
        // KeepBoth 时需要额外从 B 克隆一份的节点
        var toCloneBoth = new List<MergeNodeData>();

        var aChildMap = new Dictionary<string, Transform>();
        for (int i = 0; i < currentA.childCount; i++)
        {
            var child = currentA.GetChild(i);
            if (!aChildMap.ContainsKey(child.name))
                aChildMap[child.name] = child;
        }

        foreach (var childNode in node.Children)
        {
            bool childKept = childNode.Choice != MergeChoice.Discard;
            bool hasKeptDescendant = !childKept && HasAnyKeptDescendant(childNode);

            if (!childKept && !hasKeptDescendant)
            {
                // 如果该节点原来存在于 A 中，需要从 A 编辑实例里删除
                bool existedInA = childNode.Presence == NodePresence.BothSame
                    || childNode.Presence == NodePresence.BothDifferent
                    || childNode.Presence == NodePresence.OnlyInA;
                Transform aChild;
                if (existedInA && aChildMap.TryGetValue(childNode.NodeName, out aChild))
                    toRemove.Add(aChild);
                continue;
            }

            switch (childNode.Presence)
            {
                case NodePresence.BothSame:
                {
                    Transform aChild;
                    if (aChildMap.TryGetValue(childNode.NodeName, out aChild))
                        ApplyMergeToNode(aChild, childNode);
                    break;
                }

                case NodePresence.BothDifferent:
                {
                    if (childNode.Choice == MergeChoice.KeepBoth)
                    {
                        // 保留 A 的版本不动（递归处理其子节点）
                        Transform aChild;
                        if (aChildMap.TryGetValue(childNode.NodeName, out aChild))
                            ApplyMergeToNode(aChild, childNode);

                        // 额外从 B 克隆一份副本
                        if (childNode.TransformB != null)
                            toCloneBoth.Add(childNode);
                    }
                    else
                    {
                        Transform aChild;
                        if (aChildMap.TryGetValue(childNode.NodeName, out aChild))
                            ApplyMergeToNode(aChild, childNode);
                    }
                    break;
                }

                case NodePresence.OnlyInA:
                {
                    Transform aChild;
                    if (aChildMap.TryGetValue(childNode.NodeName, out aChild))
                        ApplyMergeToNode(aChild, childNode);
                    break;
                }

                case NodePresence.OnlyInB:
                {
                    toAdd.Add(childNode);
                    break;
                }
            }
        }

        foreach (var t in toRemove)
            Object.DestroyImmediate(t.gameObject);

        foreach (var addNode in toAdd)
            AddNodeFromB(currentA, addNode);

        // KeepBoth: 从 B 克隆副本，加 _B 后缀避免重名
        foreach (var bothNode in toCloneBoth)
            AddNodeFromBAsClone(currentA, bothNode);
    }

    /// <summary>
    /// KeepBoth 专用：从 B 克隆一份节点副本到 A 的父节点下，名称加 _B 后缀避免重名
    /// </summary>
    private void AddNodeFromBAsClone(Transform parentInA, MergeNodeData node)
    {
        if (node.TransformB == null) return;

        GameObject cloned = Object.Instantiate(node.TransformB.gameObject);
        // 避免与 A 侧同名节点冲突
        string cloneName = node.NodeName + "_B";
        // 如果已经存在同名的，继续加数字
        int counter = 0;
        while (parentInA.Find(cloneName) != null)
        {
            counter++;
            cloneName = node.NodeName + "_B" + counter;
        }
        cloned.name = cloneName;
        cloned.transform.SetParent(parentInA, false);
        CopyLocalTransform(node.TransformB, cloned.transform);

        RemoveDiscardedChildren(cloned.transform, node);
    }

    /// <summary>
    /// 从 B 侧深拷贝一个节点及其子树到 A 的父节点下
    /// </summary>
    private void AddNodeFromB(Transform parentInA, MergeNodeData node)
    {
        if (node.TransformB == null) return;

        // 深拷贝 B 节点（含所有子节点和组件）
        GameObject cloned = Object.Instantiate(node.TransformB.gameObject);
        cloned.name = node.NodeName;
        cloned.transform.SetParent(parentInA, false);
        CopyLocalTransform(node.TransformB, cloned.transform);

        // 如果子节点中有需要 discard 的，递归删除
        RemoveDiscardedChildren(cloned.transform, node);
    }

    /// <summary>
    /// 在从 B 克隆过来的子树中，递归删除被标记为 Discard 的节点
    /// </summary>
    private void RemoveDiscardedChildren(Transform clonedParent, MergeNodeData parentNode)
    {
        foreach (var childNode in parentNode.Children)
        {
            if (childNode.Choice == MergeChoice.Discard)
            {
                var found = clonedParent.Find(childNode.NodeName);
                if (found != null)
                    Object.DestroyImmediate(found.gameObject);
            }
            else
            {
                var found = clonedParent.Find(childNode.NodeName);
                if (found != null)
                    RemoveDiscardedChildren(found, childNode);
            }
        }
    }

    /// <summary>
    /// 将 B 节点的 Transform 属性和组件属性覆盖到 A 节点
    /// </summary>
    private void CopyNodeProperties(Transform src, Transform dst)
    {
        dst.localPosition = src.localPosition;
        dst.localRotation = src.localRotation;
        dst.localScale = src.localScale;
        dst.gameObject.SetActive(src.gameObject.activeSelf);
        dst.gameObject.layer = src.gameObject.layer;
        dst.gameObject.tag = src.gameObject.tag;

        // 用 EditorUtility.CopySerialized 覆盖同类型组件的属性
        var srcComps = src.GetComponents<Component>();
        var dstComps = dst.GetComponents<Component>();

        var dstCompMap = new Dictionary<string, List<Component>>();
        foreach (var dc in dstComps)
        {
            if (dc == null) continue;
            string typeName = dc.GetType().Name;
            if (!dstCompMap.ContainsKey(typeName))
                dstCompMap[typeName] = new List<Component>();
            dstCompMap[typeName].Add(dc);
        }

        var usedDst = new HashSet<Component>();
        foreach (var sc in srcComps)
        {
            if (sc == null || sc is Transform) continue;
            string typeName = sc.GetType().Name;

            List<Component> candidates;
            if (dstCompMap.TryGetValue(typeName, out candidates))
            {
                Component match = null;
                foreach (var c in candidates)
                {
                    if (!usedDst.Contains(c)) { match = c; break; }
                }
                if (match != null)
                {
                    EditorUtility.CopySerialized(sc, match);
                    usedDst.Add(match);
                    continue;
                }
            }

            // A 上没有这个类型的组件，新增
            var newComp = dst.gameObject.AddComponent(sc.GetType());
            if (newComp != null)
                EditorUtility.CopySerialized(sc, newComp);
        }
    }

    private void CopyLocalTransform(Transform src, Transform dst)
    {
        dst.localPosition = src.localPosition;
        dst.localRotation = src.localRotation;
        dst.localScale = src.localScale;
    }

    private string GetPath(Transform t)
    {
        if (t.parent == null) return t.name;
        return GetPath(t.parent) + "/" + t.name;
    }

    private List<string> GetComponentNames(Transform t)
    {
        var result = new List<string>();
        var comps = t.GetComponents<Component>();
        foreach (var c in comps)
        {
            if (c == null)
                result.Add("(Missing)");
            else
                result.Add(c.GetType().Name);
        }
        return result;
    }

    private bool ComponentListsMatch(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    private bool ApproxEqual(Vector3 a, Vector3 b, float eps = 0.001f)
    {
        return Vector3.Distance(a, b) < eps;
    }

    private bool AddPropertyDiff(MergeNodeData node, string label, string valA, string valB)
    {
        bool isDiff = valA != valB;
        node.PropertyDiffs.Add(new PropertyDiff(label, valA, valB, isDiff));
        if (isDiff)
            node.DifferenceDetails.Add($"{label}: {valA} vs {valB}");
        return isDiff;
    }

    private bool AddVectorDiff(MergeNodeData node, string label, Vector3 vA, Vector3 vB)
    {
        string sA = $"({vA.x:F3}, {vA.y:F3}, {vA.z:F3})";
        string sB = $"({vB.x:F3}, {vB.y:F3}, {vB.z:F3})";
        bool isDiff = !ApproxEqual(vA, vB);
        node.PropertyDiffs.Add(new PropertyDiff(label, sA, sB, isDiff));
        if (isDiff)
            node.DifferenceDetails.Add($"{label}: {sA} vs {sB}");
        return isDiff;
    }

    /// <summary>
    /// 为仅存在于一侧的节点构建属性摘要
    /// </summary>
    private void BuildSingleSidePropertyDiffs(MergeNodeData node, Transform t, bool isA)
    {
        string name = t.name;
        string active = t.gameObject.activeSelf.ToString();
        string layer = LayerMask.LayerToName(t.gameObject.layer);
        string tag = t.gameObject.tag;
        string pos = $"({t.localPosition.x:F3}, {t.localPosition.y:F3}, {t.localPosition.z:F3})";
        string rot = $"({t.localEulerAngles.x:F3}, {t.localEulerAngles.y:F3}, {t.localEulerAngles.z:F3})";
        string scl = $"({t.localScale.x:F3}, {t.localScale.y:F3}, {t.localScale.z:F3})";
        string children = t.childCount.ToString();

        string na = "—";
        node.PropertyDiffs.Add(new PropertyDiff("名称", isA ? name : na, isA ? na : name, true));
        if (!_options.IgnoreActive)
            node.PropertyDiffs.Add(new PropertyDiff("激活状态", isA ? active : na, isA ? na : active, true));
        if (!_options.IgnoreLayerTag)
        {
            node.PropertyDiffs.Add(new PropertyDiff("Layer", isA ? layer : na, isA ? na : layer, true));
            node.PropertyDiffs.Add(new PropertyDiff("Tag", isA ? tag : na, isA ? na : tag, true));
        }
        if (!_options.IgnoreTransform)
        {
            node.PropertyDiffs.Add(new PropertyDiff("Position", isA ? pos : na, isA ? na : pos, true));
            node.PropertyDiffs.Add(new PropertyDiff("Rotation", isA ? rot : na, isA ? na : rot, true));
            node.PropertyDiffs.Add(new PropertyDiff("Scale", isA ? scl : na, isA ? na : scl, true));
        }
        node.PropertyDiffs.Add(new PropertyDiff("子节点数", isA ? children : na, isA ? na : children, true));

        var comps = GetComponentNames(t);
        foreach (var comp in comps)
        {
            var cd = new ComponentDiff { ComponentName = comp, OnlyInA = isA, OnlyInB = !isA };
            node.ComponentDiffs.Add(cd);
        }
    }

    /// <summary>
    /// 构建两侧组件的逐一对比，按类型名匹配，收集属性差异
    /// </summary>
    private bool BuildComponentDiffs(Transform tA, Transform tB, MergeNodeData node)
    {
        var compsA = tA.GetComponents<Component>();
        var compsB = tB.GetComponents<Component>();

        bool hasDiff = false;
        var usedB = new HashSet<int>();

        for (int i = 0; i < compsA.Length; i++)
        {
            if (compsA[i] == null)
            {
                node.ComponentDiffs.Add(new ComponentDiff { ComponentName = "(Missing)", OnlyInA = true });
                hasDiff = true;
                continue;
            }

            string typeName = compsA[i].GetType().Name;
            int matchIdx = -1;
            for (int j = 0; j < compsB.Length; j++)
            {
                if (usedB.Contains(j)) continue;
                if (compsB[j] != null && compsB[j].GetType().Name == typeName)
                {
                    matchIdx = j;
                    break;
                }
            }

            if (matchIdx >= 0)
            {
                usedB.Add(matchIdx);
                var cd = new ComponentDiff { ComponentName = typeName };
                bool compDiff = false;
                if (!_options.IgnoreComponentSerializedContent)
                    compDiff = CompareComponentProperties(compsA[i], compsB[matchIdx], cd);
                if (compDiff) hasDiff = true;
                node.ComponentDiffs.Add(cd);
            }
            else
            {
                node.ComponentDiffs.Add(new ComponentDiff { ComponentName = typeName, OnlyInA = true });
                hasDiff = true;
            }
        }

        for (int j = 0; j < compsB.Length; j++)
        {
            if (usedB.Contains(j)) continue;
            string typeName = compsB[j] != null ? compsB[j].GetType().Name : "(Missing)";
            node.ComponentDiffs.Add(new ComponentDiff { ComponentName = typeName, OnlyInB = true });
            hasDiff = true;
        }

        return hasDiff;
    }

    /// <summary>
    /// 用 SerializedObject 对比同类型组件的各个可序列化属性
    /// </summary>
    private bool CompareComponentProperties(Component a, Component b, ComponentDiff cd)
    {
        bool hasDiff = false;

        // Transform 的属性已经在上层单独对比过了，跳过
        if (a is Transform) return false;

        try
        {
            var soA = new SerializedObject(a);
            var soB = new SerializedObject(b);

            var propA = soA.GetIterator();
            bool enterChildren = true;

            while (propA.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (propA.name == "m_Script") continue;

                var propB = soB.FindProperty(propA.propertyPath);
                if (propB == null)
                {
                    cd.PropertyDiffs.Add(new PropertyDiff(propA.displayName,
                        SerializedPropertyToString(propA), "—", true));
                    hasDiff = true;
                    continue;
                }

                string sA = SerializedPropertyToString(propA);
                string sB = SerializedPropertyToString(propB);

                bool isDiff = sA != sB;
                if (isDiff) hasDiff = true;

                cd.PropertyDiffs.Add(new PropertyDiff(propA.displayName, sA, sB, isDiff));
            }
        }
        catch
        {
            // 某些组件可能无法序列化，安全跳过
        }

        return hasDiff;
    }

    private string SerializedPropertyToString(SerializedProperty prop)
    {
        switch (prop.propertyType)
        {
            case SerializedPropertyType.Integer:
                return prop.intValue.ToString();
            case SerializedPropertyType.Boolean:
                return prop.boolValue.ToString();
            case SerializedPropertyType.Float:
                return prop.floatValue.ToString("F4");
            case SerializedPropertyType.String:
                return string.IsNullOrEmpty(prop.stringValue) ? "(empty)" : prop.stringValue;
            case SerializedPropertyType.Color:
                return prop.colorValue.ToString();
            case SerializedPropertyType.ObjectReference:
                return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "(null)";
            case SerializedPropertyType.Enum:
                return prop.enumDisplayNames != null && prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                    ? prop.enumDisplayNames[prop.enumValueIndex]
                    : prop.enumValueIndex.ToString();
            case SerializedPropertyType.Vector2:
                return prop.vector2Value.ToString();
            case SerializedPropertyType.Vector3:
                return prop.vector3Value.ToString();
            case SerializedPropertyType.Vector4:
                return prop.vector4Value.ToString();
            case SerializedPropertyType.Rect:
                return prop.rectValue.ToString();
            case SerializedPropertyType.Bounds:
                return prop.boundsValue.ToString();
            case SerializedPropertyType.Quaternion:
                return prop.quaternionValue.eulerAngles.ToString();
            case SerializedPropertyType.Vector2Int:
                return prop.vector2IntValue.ToString();
            case SerializedPropertyType.Vector3Int:
                return prop.vector3IntValue.ToString();
            default:
                return $"({prop.propertyType})";
        }
    }

    /// <summary>
    /// 将 MergeNodeData 树展平为列表（用于 TreeView）
    /// </summary>
    public void FlattenTree(MergeNodeData node, List<MergeNodeData> result)
    {
        result.Add(node);
        foreach (var child in node.Children)
            FlattenTree(child, result);
    }

    /// <summary>
    /// 统计节点树中各类型节点数量
    /// </summary>
    public void CountStats(MergeNodeData root, out int total, out int same, out int diff, out int onlyA, out int onlyB)
    {
        total = same = diff = onlyA = onlyB = 0;
        CountStatsRecursive(root, ref total, ref same, ref diff, ref onlyA, ref onlyB);
    }

    private void CountStatsRecursive(MergeNodeData node, ref int total, ref int same, ref int diff, ref int onlyA, ref int onlyB)
    {
        total++;
        switch (node.Presence)
        {
            case NodePresence.BothSame: same++; break;
            case NodePresence.BothDifferent: diff++; break;
            case NodePresence.OnlyInA: onlyA++; break;
            case NodePresence.OnlyInB: onlyB++; break;
        }
        foreach (var child in node.Children)
            CountStatsRecursive(child, ref total, ref same, ref diff, ref onlyA, ref onlyB);
    }

    /// <summary>
    /// 统计相对于目标 Prefab 有多少节点会发生改动（与 ApplyMergeToNode 语义一致）。
    /// overwriteA=true：覆盖 A；false：覆盖 B（结果仍基于 A 增量合并后保存到 B 路径）。
    /// </summary>
    public void CountChanges(MergeNodeData root, bool overwriteA, out int modified, out int added, out int removed)
    {
        modified = added = removed = 0;
        CountChangesRecursive(root, overwriteA, ref modified, ref added, ref removed);
    }

    private void CountChangesRecursive(MergeNodeData node, bool overwriteA, ref int modified, ref int added, ref int removed)
    {
        if (node.Choice == MergeChoice.Discard)
        {
            bool existsInTarget = overwriteA
                ? (node.Presence == NodePresence.BothSame || node.Presence == NodePresence.BothDifferent || node.Presence == NodePresence.OnlyInA)
                : (node.Presence == NodePresence.BothSame || node.Presence == NodePresence.BothDifferent || node.Presence == NodePresence.OnlyInB);
            if (existsInTarget)
                removed++;
        }
        else if (overwriteA)
        {
            switch (node.Presence)
            {
                case NodePresence.BothSame:
                    break;
                case NodePresence.BothDifferent:
                    if (node.Choice == MergeChoice.KeepB)
                        modified++;
                    else if (node.Choice == MergeChoice.KeepBoth)
                        added++;
                    break;
                case NodePresence.OnlyInA:
                    break;
                case NodePresence.OnlyInB:
                    added++;
                    break;
            }
        }
        else
        {
            switch (node.Presence)
            {
                case NodePresence.BothSame:
                    break;
                case NodePresence.BothDifferent:
                    if (node.Choice == MergeChoice.KeepA)
                        modified++;
                    else if (node.Choice == MergeChoice.KeepBoth)
                        added++;
                    break;
                case NodePresence.OnlyInA:
                    added++;
                    break;
                case NodePresence.OnlyInB:
                    break;
            }
        }

        foreach (var child in node.Children)
            CountChangesRecursive(child, overwriteA, ref modified, ref added, ref removed);
    }
}
