using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using System.Collections.Generic;

public class MergeTreeViewItem : TreeViewItem
{
    public MergeNodeData NodeData;
}

/// <summary>
/// 带 checkbox 和颜色标记的合并节点 TreeView
/// </summary>
public class PrefabMergeTreeView : TreeView
{
    private MergeNodeData _rootData;
    private List<MergeNodeData> _flatList = new List<MergeNodeData>();

    private static readonly Color ColorSame = new Color(0.7f, 1f, 0.7f);
    private static readonly Color ColorDiff = new Color(1f, 1f, 0.5f);
    private static readonly Color ColorOnlyA = new Color(0.6f, 0.8f, 1f);
    private static readonly Color ColorOnlyB = new Color(1f, 0.7f, 0.5f);

    public System.Action OnSelectionChanged;

    private string _searchKeyword = "";
    private List<int> _searchMatchIds = new List<int>();

    public int SearchMatchCount { get { return _searchMatchIds.Count; } }

    public void SetSearchKeyword(string keyword)
    {
        string newKw = keyword ?? "";
        if (newKw == _searchKeyword) return;
        _searchKeyword = newKw;
        RebuildSearchMatches();
        Reload();

        // 自动选中并跳转到第一个匹配
        if (_searchMatchIds.Count > 0)
            SelectAndFrameMatch(0);
    }

    /// <summary>
    /// 跳转到第 N 个匹配项（0-based）
    /// </summary>
    public void SelectAndFrameMatch(int index)
    {
        if (_searchMatchIds.Count == 0) return;
        index = Mathf.Clamp(index, 0, _searchMatchIds.Count - 1);
        int id = _searchMatchIds[index];
        SetSelection(new List<int> { id }, TreeViewSelectionOptions.RevealAndFrame);
        OnSelectionChanged?.Invoke();
    }

    /// <summary>
    /// 获取当前选中节点在搜索匹配列表中的索引，没命中返回 -1
    /// </summary>
    public int GetCurrentMatchIndex()
    {
        var sel = GetSelection();
        if (sel == null || sel.Count == 0) return -1;
        return _searchMatchIds.IndexOf(sel[0]);
    }

    private void RebuildSearchMatches()
    {
        _searchMatchIds.Clear();
        if (string.IsNullOrEmpty(_searchKeyword)) return;
        string kwLower = _searchKeyword.ToLower();
        foreach (var node in _flatList)
        {
            if (node.NodeName != null && node.NodeName.ToLower().Contains(kwLower))
                _searchMatchIds.Add(node.Id);
        }
    }

    private bool IsSearchMatch(MergeNodeData node)
    {
        if (string.IsNullOrEmpty(_searchKeyword)) return false;
        return node.NodeName != null && node.NodeName.ToLower().Contains(_searchKeyword.ToLower());
    }

    private bool _hideSameNodes;
    /// <summary>
    /// 隐藏相同节点后实际隐藏的数量（用于 UI 提示）
    /// </summary>
    public int HiddenSameCount { get; private set; }

    public bool HideSameNodes
    {
        get { return _hideSameNodes; }
        set
        {
            if (_hideSameNodes != value)
            {
                _hideSameNodes = value;
                RebuildFlatList();
                Reload();
            }
        }
    }

    public PrefabMergeTreeView(TreeViewState state) : base(state)
    {
        showAlternatingRowBackgrounds = true;
        showBorder = true;
    }

    public void SetData(MergeNodeData root)
    {
        _rootData = root;
        if (_rootData != null)
            ApplyKeepAOnlyInBRuleFullTree();
        RebuildFlatList();
        Reload();
    }

    private void RebuildFlatList()
    {
        _flatList.Clear();
        HiddenSameCount = 0;
        if (_rootData != null)
        {
            if (_hideSameNodes)
            {
                int hidden = 0;
                FlattenTreeFiltered(_rootData, _flatList, ref hidden);
                HiddenSameCount = hidden;
            }
            else
            {
                var logic = new PrefabComparerLogic();
                logic.FlattenTree(_rootData, _flatList);
            }
        }
        RebuildSearchMatches();
    }

    /// <summary>
    /// 展平节点树，但跳过"自身及所有后代均为 BothSame"的纯相同子树叶节点。
    /// 如果一个 BothSame 节点的子树中包含差异节点，该节点仍会保留作为路径上的中间层。
    /// </summary>
    private bool FlattenTreeFiltered(MergeNodeData node, List<MergeNodeData> result, ref int hiddenCount)
    {
        bool hasDiffDescendant = node.Presence != NodePresence.BothSame;

        var childResults = new List<MergeNodeData>();
        foreach (var child in node.Children)
        {
            int before = childResults.Count;
            if (FlattenTreeFiltered(child, childResults, ref hiddenCount))
                hasDiffDescendant = true;
        }

        if (hasDiffDescendant)
        {
            result.Add(node);
            // 需要重新计算过滤后的 depth —— 保持原始 depth 不变即可，
            // 因为 TreeView 的 SetupParentsAndChildrenFromDepths 会根据 depth 重建层级
            result.AddRange(childResults);
            return true;
        }
        else
        {
            hiddenCount++;
            return false;
        }
    }

    protected override TreeViewItem BuildRoot()
    {
        var root = new TreeViewItem { id = -1, depth = -1, displayName = "Root" };

        if (_rootData == null || _flatList.Count == 0)
        {
            string msg = _hideSameNodes ? "(所有节点相同，无差异)" : "(无数据)";
            root.AddChild(new TreeViewItem { id = 0, depth = 0, displayName = msg });
            return root;
        }

        var items = new List<TreeViewItem>();
        foreach (var node in _flatList)
        {
            var item = new MergeTreeViewItem
            {
                id = node.Id,
                depth = node.Depth,
                displayName = BuildDisplayName(node),
                NodeData = node,
            };
            items.Add(item);
        }

        SetupParentsAndChildrenFromDepths(root, items);
        return root;
    }

    private string BuildDisplayName(MergeNodeData node)
    {
        string tag = "";
        switch (node.Presence)
        {
            case NodePresence.BothSame: tag = "[=]"; break;
            case NodePresence.BothDifferent: tag = "[≠]"; break;
            case NodePresence.OnlyInA: tag = "[A]"; break;
            case NodePresence.OnlyInB: tag = "[B]"; break;
        }

        // 两边一致时无需「选 A 还是 B」，避免与「仅在一侧」的保留语义混淆
        if (node.Presence == NodePresence.BothSame)
            return $"{tag} {node.NodeName}  [一致]";

        string choiceTag = "";
        switch (node.Choice)
        {
            case MergeChoice.KeepA: choiceTag = "→A"; break;
            case MergeChoice.KeepB: choiceTag = "→B"; break;
            case MergeChoice.KeepBoth: choiceTag = "→AB"; break;
            case MergeChoice.Discard: choiceTag = "→✗"; break;
        }

        return $"{tag} {node.NodeName}  [{choiceTag}]";
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        var item = args.item as MergeTreeViewItem;
        if (item == null || item.NodeData == null)
        {
            base.RowGUI(args);
            return;
        }

        var node = item.NodeData;
        var rowRect = args.rowRect;

        // 背景色
        Color bgColor = GetNodeColor(node);
        bgColor.a = 0.25f;
        EditorGUI.DrawRect(rowRect, bgColor);

        // 搜索匹配高亮
        if (IsSearchMatch(node))
        {
            var highlightColor = new Color(1f, 0.95f, 0f, 0.2f);
            EditorGUI.DrawRect(rowRect, highlightColor);
            // 左侧标记条
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3, rowRect.height), new Color(1f, 0.8f, 0f));
        }

        // 缩进
        float indent = GetContentIndent(item);
        var contentRect = new Rect(rowRect.x + indent, rowRect.y, rowRect.width - indent, rowRect.height);

        // 折叠箭头由 base 绘制，我们只画内容
        var labelRect = new Rect(contentRect.x, contentRect.y, contentRect.width - 220, contentRect.height);

        // 状态标签颜色
        var oldColor = GUI.color;
        GUI.color = GetNodeColor(node);
        string presenceTag = GetPresenceTag(node);
        EditorGUI.LabelField(new Rect(labelRect.x, labelRect.y, 30, labelRect.height), presenceTag);
        GUI.color = oldColor;

        // 节点名称
        var nameRect = new Rect(labelRect.x + 32, labelRect.y, labelRect.width - 32, labelRect.height);
        var nameStyle = new GUIStyle(EditorStyles.label);
        if (node.Choice == MergeChoice.Discard)
        {
            nameStyle.fontStyle = FontStyle.Italic;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
        }
        EditorGUI.LabelField(nameRect, node.NodeName, nameStyle);
        GUI.color = oldColor;

        // 右侧操作按钮
        float btnWidth = 36;
        float btnStart = rowRect.xMax - 220;
        float btnY = rowRect.y + 1;
        float btnH = rowRect.height - 2;

        if (node.Presence == NodePresence.BothDifferent)
        {
            // 文案：表示「有差异时采用哪一侧」，不是「仅 A/仅 B 才保留」
            if (GUI.Button(new Rect(btnStart, btnY, btnWidth, btnH),
                new GUIContent("A", "有差异时采用 A 侧的属性写入合并结果"),
                node.Choice == MergeChoice.KeepA ? GetActiveButtonStyle() : EditorStyles.miniButton))
            {
                node.Choice = MergeChoice.KeepA;
                EnsureParentsKept(node);
                EnsureChildrenNotDiscard(node);
                ApplyKeepAOnlyInBRuleFullTree();
                Reload();
            }
            if (GUI.Button(new Rect(btnStart + btnWidth + 2, btnY, btnWidth, btnH),
                new GUIContent("B", "有差异时采用 B 侧的属性写入合并结果"),
                node.Choice == MergeChoice.KeepB ? GetActiveButtonStyle() : EditorStyles.miniButton))
            {
                node.Choice = MergeChoice.KeepB;
                EnsureParentsKept(node);
                EnsureChildrenNotDiscard(node);
                ApplyKeepAOnlyInBRuleFullTree();
                Reload();
            }
            if (GUI.Button(new Rect(btnStart + (btnWidth + 2) * 2, btnY, btnWidth, btnH),
                new GUIContent("AB", "保留 A 上同名节点，并再克隆一份 B 侧（名称加 _B）"),
                node.Choice == MergeChoice.KeepBoth ? GetActiveButtonStyle() : EditorStyles.miniButton))
            {
                node.Choice = MergeChoice.KeepBoth;
                EnsureParentsKept(node);
                EnsureChildrenNotDiscard(node);
                ApplyKeepAOnlyInBRuleFullTree();
                Reload();
            }
            if (GUI.Button(new Rect(btnStart + (btnWidth + 2) * 3, btnY, btnWidth, btnH),
                new GUIContent("✗", "合并结果里删除此节点（若 A 上有则删除）"),
                node.Choice == MergeChoice.Discard ? GetActiveButtonStyle() : EditorStyles.miniButton))
            {
                SetChoiceRecursive(node, MergeChoice.Discard);
                Reload();
            }
        }
        else if (node.Presence == NodePresence.BothSame)
        {
            GUI.Label(new Rect(btnStart, btnY, 200, btnH), "两侧一致", EditorStyles.miniLabel);
        }
        else if (node.Presence == NodePresence.OnlyInA || node.Presence == NodePresence.OnlyInB)
        {
            string keepLabel = node.Presence == NodePresence.OnlyInA ? "保留A" : "保留B";
            MergeChoice keepChoice = node.Presence == NodePresence.OnlyInA ? MergeChoice.KeepA : MergeChoice.KeepB;

            string keepTip = node.Presence == NodePresence.OnlyInA
                ? "仅在 A 中存在：保留则写入合并结果，丢弃则从结果中删除"
                : "仅在 B 中存在：保留则从 B 克隆到合并结果，丢弃则不加入";
            if (GUI.Button(new Rect(btnStart, btnY, 50, btnH), new GUIContent(keepLabel, keepTip),
                node.Choice != MergeChoice.Discard ? GetActiveButtonStyle() : EditorStyles.miniButton))
            {
                SetChoiceRecursive(node, keepChoice);
                EnsureParentsKept(node);
                EnsureChildrenNotDiscard(node);
                ApplyKeepAOnlyInBRuleFullTree();
                Reload();
            }
            if (GUI.Button(new Rect(btnStart + 54, btnY, 36, btnH), "✗",
                node.Choice == MergeChoice.Discard ? GetActiveButtonStyle() : EditorStyles.miniButton))
            {
                SetChoiceRecursive(node, MergeChoice.Discard);
                ApplyKeepAOnlyInBRuleFullTree();
                Reload();
            }
        }

        // 组件信息简要
        string compInfo = GetCompactComponentInfo(node);
        if (!string.IsNullOrEmpty(compInfo))
        {
            var compRect = new Rect(rowRect.xMax - 60, rowRect.y, 58, rowRect.height);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            EditorGUI.LabelField(compRect, compInfo, EditorStyles.miniLabel);
            GUI.color = oldColor;
        }
    }

    protected override void SelectionChanged(IList<int> selectedIds)
    {
        base.SelectionChanged(selectedIds);
        OnSelectionChanged?.Invoke();
    }

    public MergeNodeData GetSelectedNode()
    {
        var sel = GetSelection();
        if (sel == null || sel.Count == 0) return null;
        foreach (var node in _flatList)
        {
            if (node.Id == sel[0]) return node;
        }
        return null;
    }

    protected override float GetCustomRowHeight(int row, TreeViewItem item)
    {
        return 22f;
    }

    private void RefreshItem(MergeTreeViewItem item)
    {
        item.displayName = BuildDisplayName(item.NodeData);
        Repaint();
    }

    private void SetChoiceRecursive(MergeNodeData node, MergeChoice choice)
    {
        node.Choice = choice;
        if (node.Presence == NodePresence.OnlyInB)
            node.ExplicitKeepB = (choice == MergeChoice.KeepB);
        foreach (var child in node.Children)
            SetChoiceRecursive(child, choice);
    }

    /// <summary>
    /// 当一个节点被设为保留时，确保其所有父节点也不是 Discard。
    /// 父节点恢复为默认的保留策略（根据其 Presence 类型）。
    /// </summary>
    private void EnsureParentsKept(MergeNodeData node)
    {
        int parentId = node.ParentId;
        while (parentId >= 0)
        {
            MergeNodeData parent = null;
            foreach (var n in _flatList)
            {
                if (n.Id == parentId) { parent = n; break; }
            }
            if (parent == null) break;

            if (parent.Choice == MergeChoice.Discard)
            {
                switch (parent.Presence)
                {
                    case NodePresence.OnlyInA: parent.Choice = MergeChoice.KeepA; break;
                    case NodePresence.OnlyInB: parent.Choice = MergeChoice.KeepB; break;
                    default: parent.Choice = MergeChoice.KeepA; break;
                }
                EnsureChildrenNotDiscard(parent);
                ApplyKeepAOnlyInBRuleFullTree();
            }
            else
            {
                break;
            }

            parentId = parent.ParentId;
        }
    }

    /// <summary>
    /// 父节点已选择保留时，子节点不能再为 Discard（OnlyInB 且父选 A 时例外，见 EnsureChildrenNotDiscard）。
    /// </summary>
    private void EnsureChildrenNotDiscard(MergeNodeData node)
    {
        foreach (var child in node.Children)
        {
            bool parentKeepA = node.Presence == NodePresence.BothDifferent && node.Choice == MergeChoice.KeepA;

            if (parentKeepA && child.Presence == NodePresence.OnlyInB)
            {
                child.Choice = child.ExplicitKeepB ? MergeChoice.KeepB : MergeChoice.Discard;
                EnsureChildrenNotDiscard(child);
                continue;
            }

            if (child.Choice == MergeChoice.Discard)
            {
                switch (child.Presence)
                {
                    case NodePresence.OnlyInA:
                        child.Choice = MergeChoice.KeepA;
                        break;
                    case NodePresence.OnlyInB:
                        child.Choice = MergeChoice.KeepB;
                        break;
                    default:
                        child.Choice = MergeChoice.KeepA;
                        break;
                }
            }
            EnsureChildrenNotDiscard(child);
        }
    }

    /// <summary>
    /// 在「沿 A 保留」的分支上，OnlyInB 默认不应再选 B，除非 ExplicitKeepB。
    /// 若中途出现 BothDifferent 选 B/AB，则其下不再施加此约束。
    /// </summary>
    private void ApplyKeepAOnlyInBRuleFullTree()
    {
        if (_rootData == null) return;
        bool startUnderA = _rootData.Choice != MergeChoice.Discard &&
            !(_rootData.Presence == NodePresence.BothDifferent &&
                (_rootData.Choice == MergeChoice.KeepB || _rootData.Choice == MergeChoice.KeepBoth));
        ApplyKeepAOnlyInBRecursive(_rootData, startUnderA);
    }

    private void ApplyKeepAOnlyInBRecursive(MergeNodeData n, bool underA)
    {
        foreach (var child in n.Children)
        {
            bool childUnderA = underA;
            if (child.Presence == NodePresence.BothDifferent)
            {
                if (child.Choice == MergeChoice.KeepB || child.Choice == MergeChoice.KeepBoth)
                    childUnderA = false;
                else if (child.Choice == MergeChoice.KeepA)
                    childUnderA = true;
                else if (child.Choice == MergeChoice.Discard)
                    childUnderA = false;
            }

            if (childUnderA && child.Presence == NodePresence.OnlyInB && !child.ExplicitKeepB)
                child.Choice = MergeChoice.Discard;

            ApplyKeepAOnlyInBRecursive(child, childUnderA);
        }
    }

    private string GetPresenceTag(MergeNodeData node)
    {
        switch (node.Presence)
        {
            case NodePresence.BothSame: return "=";
            case NodePresence.BothDifferent: return "≠";
            case NodePresence.OnlyInA: return "A";
            case NodePresence.OnlyInB: return "B";
            default: return "";
        }
    }

    private Color GetNodeColor(MergeNodeData node)
    {
        switch (node.Presence)
        {
            case NodePresence.BothSame: return ColorSame;
            case NodePresence.BothDifferent: return ColorDiff;
            case NodePresence.OnlyInA: return ColorOnlyA;
            case NodePresence.OnlyInB: return ColorOnlyB;
            default: return Color.white;
        }
    }

    private string GetCompactComponentInfo(MergeNodeData node)
    {
        int count = 0;
        if (node.ComponentsA != null && node.ComponentsA.Count > 0)
            count = node.ComponentsA.Count;
        else if (node.ComponentsB != null && node.ComponentsB.Count > 0)
            count = node.ComponentsB.Count;
        if (count <= 1) return "";
        return $"{count} comp";
    }

    private GUIStyle _activeButtonStyle;

    private GUIStyle GetActiveButtonStyle()
    {
        if (_activeButtonStyle == null)
        {
            _activeButtonStyle = new GUIStyle(EditorStyles.miniButton);
            _activeButtonStyle.fontStyle = FontStyle.Bold;
            _activeButtonStyle.normal.textColor = Color.green;
        }
        return _activeButtonStyle;
    }
}
