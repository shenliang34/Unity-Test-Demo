using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Prefab 合并编辑器主窗口
/// 支持打开两个 Prefab（其中一个可来自外部工程），对比节点差异，
/// 交互式选择保留/删除节点，最终保存为新 Prefab。
/// </summary>
public class PrefabComparerWindow : EditorWindow
{
    private GameObject _prefabA;
    private GameObject _prefabB;
    private string _externalPrefabPath;

    private GameObject _loadedRootA;
    private GameObject _loadedRootB;

    private TreeViewState _treeViewState;
    private PrefabMergeTreeView _treeView;
    private MergeNodeData _mergeRoot;

    private PrefabComparerLogic _logic = new PrefabComparerLogic();

    private Vector2 _detailScroll;
    private bool _isCompared;
    private string _statusMessage = "";
    private bool _hideSameNodes;
    private string _searchKeyword = "";
    private int _searchMatchIndex;
    private float _splitRatio = 0.55f;
    private bool _draggingSplitter;
    private const float SplitterHeight = 6f;
    private float _contentTop;

    private PrefabCompareOptions _compareOptions = new PrefabCompareOptions();

    [MenuItem("Tools/Prefab Merge Editor")]
    public static void ShowWindow()
    {
        var win = GetWindow<PrefabComparerWindow>("Prefab 合并编辑器");
        win.minSize = new Vector2(700, 500);
    }

    private void OnEnable()
    {
        _treeViewState = new TreeViewState();
        _treeView = new PrefabMergeTreeView(_treeViewState);
        _treeView.OnSelectionChanged = Repaint;
        _treeView.Reload();
    }

    private void OnDisable()
    {
        UnloadPrefabContents();
        CleanupExternalImport();
    }

    private const float BottomBarHeight = 62f;

    private void OnGUI()
    {
        DrawToolbar();
        DrawPrefabSelection();

        if (_isCompared && _mergeRoot != null)
        {
            DrawStatsBar();
            DrawSearchBar();

            // 记录搜索栏结束位置，后续全部用绝对定位避免重叠
            if (Event.current.type == EventType.Repaint)
                _contentTop = GUILayoutUtility.GetLastRect().yMax;

            float w = position.width;
            float totalAvailable = position.height - _contentTop - BottomBarHeight;
            if (totalAvailable < 200f) totalAvailable = 200f;

            float treeHeight = totalAvailable * _splitRatio;
            float detailHeight = totalAvailable * (1f - _splitRatio) - SplitterHeight;

            float treeY = _contentTop;
            float splitterY = treeY + treeHeight;
            float detailY = splitterY + SplitterHeight;

            // 树视图
            var treeRect = new Rect(0, treeY, w, treeHeight);
            _treeView.OnGUI(treeRect);

            // 分割条（可拖拽）
            DrawSplitter(treeY, splitterY, totalAvailable, w);

            // 详情面板
            var detailRect = new Rect(0, detailY, w, detailHeight);
            GUILayout.BeginArea(detailRect);
            DrawDetailPanel(detailHeight);
            GUILayout.EndArea();

            // 操作栏和图例固定在窗口最底部
            var bottomRect = new Rect(0, position.height - BottomBarHeight, w, BottomBarHeight);
            GUILayout.BeginArea(bottomRect);
            EditorGUI.DrawRect(new Rect(0, 0, w, 1), new Color(0.3f, 0.3f, 0.3f));
            DrawActionBar();
            DrawLegend();
            GUILayout.EndArea();
        }
        else
        {
            DrawLegend();
        }
    }

    private void DrawSplitter(float treeY, float splitterY, float totalAvailable, float width)
    {
        var splitterRect = new Rect(0, splitterY, width, SplitterHeight);
        EditorGUI.DrawRect(splitterRect, new Color(0.15f, 0.15f, 0.15f, 1f));

        var lineRect = new Rect(width * 0.3f, splitterY + 2, width * 0.4f, 2);
        EditorGUI.DrawRect(lineRect, new Color(0.5f, 0.5f, 0.5f, 0.6f));

        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

        var evt = Event.current;
        if (evt.type == EventType.MouseDown && splitterRect.Contains(evt.mousePosition))
        {
            _draggingSplitter = true;
            evt.Use();
        }
        if (_draggingSplitter)
        {
            if (evt.type == EventType.MouseDrag)
            {
                float mouseRelative = evt.mousePosition.y - treeY;
                _splitRatio = Mathf.Clamp(mouseRelative / totalAvailable, 0.15f, 0.85f);
                evt.Use();
                Repaint();
            }
            if (evt.type == EventType.MouseUp)
            {
                _draggingSplitter = false;
                evt.Use();
            }
        }
    }

    // ─── 顶部工具栏 ───
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.FlexibleSpace();
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            GUILayout.Label(_statusMessage, EditorStyles.miniLabel);
        }
        EditorGUILayout.EndHorizontal();
    }

    // ─── Prefab 选择区域 ───
    private void DrawPrefabSelection()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Prefab A（本工程）", EditorStyles.boldLabel, GUILayout.Width(160));
        _prefabA = (GameObject)EditorGUILayout.ObjectField(_prefabA, typeof(GameObject), false);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Prefab B（本工程或外部）", EditorStyles.boldLabel, GUILayout.Width(160));
        _prefabB = (GameObject)EditorGUILayout.ObjectField(_prefabB, typeof(GameObject), false);

        if (GUILayout.Button("导入外部Prefab...", GUILayout.Width(120)))
        {
            ImportExternalPrefab();
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(_externalPrefabPath))
        {
            EditorGUILayout.HelpBox($"外部导入: {_externalPrefabPath}", MessageType.Info);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("对比选项（修改后请重新「开始对比」）", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        _compareOptions.IgnoreTransform = GUILayout.Toggle(_compareOptions.IgnoreTransform, "忽略位置/旋转/缩放", GUILayout.Width(140));
        _compareOptions.IgnoreActive = GUILayout.Toggle(_compareOptions.IgnoreActive, "忽略激活状态", GUILayout.Width(100));
        _compareOptions.IgnoreLayerTag = GUILayout.Toggle(_compareOptions.IgnoreLayerTag, "忽略 Layer/Tag", GUILayout.Width(120));
        _compareOptions.IgnoreComponentSerializedContent = GUILayout.Toggle(_compareOptions.IgnoreComponentSerializedContent, "忽略组件序列化内容", GUILayout.Width(140));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        GUI.enabled = _prefabA != null && _prefabB != null;
        if (GUILayout.Button("开始对比", GUILayout.Width(120), GUILayout.Height(28)))
        {
            RunCompare();
        }
        GUI.enabled = true;

        if (_isCompared)
        {
            if (GUILayout.Button("重置", GUILayout.Width(60), GUILayout.Height(28)))
            {
                ResetCompare();
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    // ─── 统计栏 ───
    private void DrawStatsBar()
    {
        int total, same, diff, onlyA, onlyB;
        _logic.CountStats(_mergeRoot, out total, out same, out diff, out onlyA, out onlyB);

        EditorGUILayout.BeginHorizontal("box");
        GUILayout.Label($"总节点: {total}", EditorStyles.miniLabel);
        GUILayout.Label($"| 相同: {same}", EditorStyles.miniLabel);

        var oldColor = GUI.color;
        GUI.color = new Color(1f, 1f, 0.5f);
        GUILayout.Label($"| 有差异: {diff}", EditorStyles.miniLabel);
        GUI.color = new Color(0.6f, 0.8f, 1f);
        GUILayout.Label($"| 仅A: {onlyA}", EditorStyles.miniLabel);
        GUI.color = new Color(1f, 0.7f, 0.5f);
        GUILayout.Label($"| 仅B: {onlyB}", EditorStyles.miniLabel);
        GUI.color = oldColor;

        GUILayout.FlexibleSpace();

        EditorGUI.BeginChangeCheck();
        _hideSameNodes = GUILayout.Toggle(_hideSameNodes, "隐藏相同节点", EditorStyles.toolbarButton, GUILayout.Width(100));
        if (EditorGUI.EndChangeCheck())
        {
            _treeView.HideSameNodes = _hideSameNodes;
            if (!_hideSameNodes)
                _treeView.ExpandAll();
        }

        if (_hideSameNodes && _treeView.HiddenSameCount > 0)
        {
            GUILayout.Label($"(已隐藏 {_treeView.HiddenSameCount} 个)", EditorStyles.miniLabel);
        }

        EditorGUILayout.EndHorizontal();
    }

    // ─── 搜索栏 ───
    private void DrawSearchBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("搜索:", EditorStyles.miniLabel, GUILayout.Width(32));

        EditorGUI.BeginChangeCheck();
        _searchKeyword = EditorGUILayout.TextField(_searchKeyword, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
        if (EditorGUI.EndChangeCheck())
        {
            _searchMatchIndex = 0;
            _treeView.SetSearchKeyword(_searchKeyword);
        }

        int matchCount = _treeView.SearchMatchCount;
        if (!string.IsNullOrEmpty(_searchKeyword))
        {
            if (matchCount > 0)
            {
                GUILayout.Label($"{_searchMatchIndex + 1}/{matchCount}", EditorStyles.miniLabel, GUILayout.Width(50));

                if (GUILayout.Button("▲", EditorStyles.toolbarButton, GUILayout.Width(22)))
                {
                    _searchMatchIndex--;
                    if (_searchMatchIndex < 0) _searchMatchIndex = matchCount - 1;
                    _treeView.SelectAndFrameMatch(_searchMatchIndex);
                }
                if (GUILayout.Button("▼", EditorStyles.toolbarButton, GUILayout.Width(22)))
                {
                    _searchMatchIndex++;
                    if (_searchMatchIndex >= matchCount) _searchMatchIndex = 0;
                    _treeView.SelectAndFrameMatch(_searchMatchIndex);
                }
            }
            else
            {
                GUILayout.Label("无匹配", EditorStyles.miniLabel, GUILayout.Width(50));
            }

            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20)))
            {
                _searchKeyword = "";
                _searchMatchIndex = 0;
                _treeView.SetSearchKeyword("");
                GUI.FocusControl(null);
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private bool _detailShowAllProps = false;
    private bool _detailFoldoutTransform = true;
    private Dictionary<string, bool> _compFoldouts = new Dictionary<string, bool>();

    private static readonly Color DiffHighlight = new Color(1f, 0.85f, 0.4f, 0.3f);
    private static readonly Color OnlyAHighlight = new Color(0.5f, 0.75f, 1f, 0.25f);
    private static readonly Color OnlyBHighlight = new Color(1f, 0.6f, 0.4f, 0.25f);
    private static readonly Color HeaderBg = new Color(0.22f, 0.22f, 0.22f, 1f);

    /// <summary>
    /// 说明合并语义：两边都有的节点上，A/B 表示「有差异时采用哪一侧」，不是「仅一侧才有才保留」。
    /// </summary>
    private static string GetMergeChoiceDescription(MergeNodeData node)
    {
        switch (node.Presence)
        {
            case NodePresence.BothSame:
                return "合并：两侧无差异，无需在 A/B 间选择";
            case NodePresence.BothDifferent:
                switch (node.Choice)
                {
                    case MergeChoice.KeepA: return "合并：有差异时采用 A 侧属性";
                    case MergeChoice.KeepB: return "合并：有差异时采用 B 侧属性";
                    case MergeChoice.KeepBoth: return "合并：保留 A 上节点并再克隆 B 侧一份(_B)";
                    case MergeChoice.Discard: return "合并：从结果中移除此节点";
                    default: return node.Choice.ToString();
                }
            case NodePresence.OnlyInA:
                return node.Choice == MergeChoice.Discard
                    ? "合并：丢弃（结果中不含此仅 A 节点）"
                    : "合并：保留（仅存在于 A 的节点写入结果）";
            case NodePresence.OnlyInB:
                return node.Choice == MergeChoice.Discard
                    ? "合并：不加入（丢弃 B 独有节点）"
                    : "合并：从 B 克隆此节点到结果";
            default:
                return "选择: " + node.Choice;
        }
    }

    // ─── 详情面板 ───
    private void DrawDetailPanel(float height)
    {
        EditorGUILayout.Space(2);

        var selected = _treeView.GetSelectedNode();
        if (selected == null)
        {
            EditorGUILayout.HelpBox("点击上方节点查看详情，拖拽上方分割条调整区域大小", MessageType.None);
            return;
        }

        // 标题栏
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("节点详情", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        _detailShowAllProps = GUILayout.Toggle(_detailShowAllProps, "显示全部属性", EditorStyles.toolbarButton, GUILayout.Width(90));
        EditorGUILayout.EndHorizontal();

        float scrollHeight = Mathf.Max(80, height - 24);
        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.Height(scrollHeight));

        // 节点信息头
        DrawDetailHeader(selected);
        EditorGUILayout.Space(4);

        // Transform / 基础属性对比
        DrawPropertyDiffSection(selected);

        // 组件级别对比
        DrawComponentDiffSection(selected);

        EditorGUILayout.Space(8);
        EditorGUILayout.EndScrollView();
    }

    private void DrawDetailHeader(MergeNodeData node)
    {
        var headerRect = EditorGUILayout.BeginVertical("box");

        // 存在状态标签
        EditorGUILayout.BeginHorizontal();
        Color tagColor;
        string tagText;
        switch (node.Presence)
        {
            case NodePresence.BothSame:
                tagColor = new Color(0.4f, 0.8f, 0.4f); tagText = "两边相同"; break;
            case NodePresence.BothDifferent:
                tagColor = new Color(0.9f, 0.8f, 0.2f); tagText = "有差异"; break;
            case NodePresence.OnlyInA:
                tagColor = new Color(0.4f, 0.6f, 1f); tagText = "仅存在于 A"; break;
            default:
                tagColor = new Color(1f, 0.5f, 0.3f); tagText = "仅存在于 B"; break;
        }

        var oldBg = GUI.backgroundColor;
        GUI.backgroundColor = tagColor;
        GUILayout.Label(tagText, "sv_label_3", GUILayout.ExpandWidth(false));
        GUI.backgroundColor = oldBg;

        GUILayout.Label("  " + node.NodeName, EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.Label(GetMergeChoiceDescription(node), EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        // 路径
        if (!string.IsNullOrEmpty(node.PathInA))
            DrawMiniLabel("路径 A", node.PathInA);
        if (!string.IsNullOrEmpty(node.PathInB))
            DrawMiniLabel("路径 B", node.PathInB);

        EditorGUILayout.EndVertical();
    }

    private void DrawPropertyDiffSection(MergeNodeData node)
    {
        if (node.PropertyDiffs == null || node.PropertyDiffs.Count == 0) return;

        _detailFoldoutTransform = EditorGUILayout.Foldout(_detailFoldoutTransform, "基础属性", true, EditorStyles.foldoutHeader);
        if (!_detailFoldoutTransform) return;

        // 列表头
        DrawCompareTableHeader();

        foreach (var diff in node.PropertyDiffs)
        {
            if (!_detailShowAllProps && !diff.IsDifferent) continue;
            DrawPropertyDiffRow(diff);
        }
    }

    private void DrawComponentDiffSection(MergeNodeData node)
    {
        if (node.ComponentDiffs == null || node.ComponentDiffs.Count == 0) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("组件对比", EditorStyles.boldLabel);

        for (int i = 0; i < node.ComponentDiffs.Count; i++)
        {
            var cd = node.ComponentDiffs[i];
            string key = i + "_" + cd.ComponentName;

            if (!_compFoldouts.ContainsKey(key))
                _compFoldouts[key] = cd.OnlyInA || cd.OnlyInB || cd.PropertyDiffs.Count > 0;

            // 组件标题行
            EditorGUILayout.BeginHorizontal();

            if (cd.OnlyInA)
            {
                var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.label, GUILayout.ExpandWidth(true), GUILayout.Height(20));
                EditorGUI.DrawRect(rect, OnlyAHighlight);
                EditorGUI.LabelField(rect, $"  [仅A] {cd.ComponentName}", EditorStyles.boldLabel);
            }
            else if (cd.OnlyInB)
            {
                var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.label, GUILayout.ExpandWidth(true), GUILayout.Height(20));
                EditorGUI.DrawRect(rect, OnlyBHighlight);
                EditorGUI.LabelField(rect, $"  [仅B] {cd.ComponentName}", EditorStyles.boldLabel);
            }
            else
            {
                bool hasDiffs = false;
                foreach (var pd in cd.PropertyDiffs)
                {
                    if (pd.IsDifferent) { hasDiffs = true; break; }
                }

                string suffix = hasDiffs ? "  (有差异)" : "  (相同)";
                _compFoldouts[key] = EditorGUILayout.Foldout(_compFoldouts[key], cd.ComponentName + suffix, true);
            }
            EditorGUILayout.EndHorizontal();

            if (!cd.OnlyInA && !cd.OnlyInB && _compFoldouts[key] && cd.PropertyDiffs.Count > 0)
            {
                EditorGUI.indentLevel++;
                DrawCompareTableHeader();

                foreach (var pd in cd.PropertyDiffs)
                {
                    if (!_detailShowAllProps && !pd.IsDifferent) continue;
                    DrawPropertyDiffRow(pd);
                }
                EditorGUI.indentLevel--;
            }
        }
    }

    private void DrawCompareTableHeader()
    {
        var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
        EditorGUI.DrawRect(rect, HeaderBg);

        GUILayout.Label("属性", EditorStyles.miniBoldLabel, GUILayout.Width(120));
        GUILayout.Label("Prefab A", EditorStyles.miniBoldLabel, GUILayout.MinWidth(100));
        GUILayout.Label("Prefab B", EditorStyles.miniBoldLabel, GUILayout.MinWidth(100));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPropertyDiffRow(PropertyDiff diff)
    {
        var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(18));

        if (diff.IsDifferent)
            EditorGUI.DrawRect(rect, DiffHighlight);

        GUILayout.Label(diff.Label, EditorStyles.miniLabel, GUILayout.Width(120));

        var oldColor = GUI.color;
        if (diff.IsDifferent)
            GUI.color = new Color(0.5f, 0.8f, 1f);
        GUILayout.Label(diff.ValueA ?? "—", EditorStyles.miniLabel, GUILayout.MinWidth(100));

        if (diff.IsDifferent)
            GUI.color = new Color(1f, 0.65f, 0.4f);
        GUILayout.Label(diff.ValueB ?? "—", EditorStyles.miniLabel, GUILayout.MinWidth(100));
        GUI.color = oldColor;

        EditorGUILayout.EndHorizontal();
    }

    private void DrawMiniLabel(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label + ": ", EditorStyles.miniLabel, GUILayout.Width(50));
        GUILayout.Label(value, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ─── 底部操作栏 ───
    private void DrawActionBar()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button(new GUIContent("全部采用 A 侧", "两边都有的节点：有差异时以 A 为准；仅在 B 的节点批量丢弃。"), GUILayout.Width(100)))
            SetAllChoice(MergeChoice.KeepA);
        if (GUILayout.Button(new GUIContent("全部采用 B 侧", "两边都有的节点：有差异时以 B 为准；仅在 A 的节点批量丢弃。"), GUILayout.Width(100)))
            SetAllChoice(MergeChoice.KeepB);

        EditorGUILayout.Space(16);

        var oldBg = GUI.backgroundColor;

        // 覆盖 A
        GUI.backgroundColor = new Color(0.5f, 0.75f, 1f);
        if (GUILayout.Button("覆盖 A", GUILayout.Width(70), GUILayout.Height(28)))
        {
            OverwritePrefab(true);
        }

        // 覆盖 B（外部导入的不允许覆盖）
        bool canOverwriteB = _prefabB != null && string.IsNullOrEmpty(_externalPrefabPath);
        GUI.enabled = canOverwriteB;
        GUI.backgroundColor = new Color(1f, 0.7f, 0.45f);
        if (GUILayout.Button(canOverwriteB ? "覆盖 B" : "覆盖 B(外部)", GUILayout.Width(90), GUILayout.Height(28)))
        {
            OverwritePrefab(false);
        }
        GUI.enabled = true;

        EditorGUILayout.Space(16);

        GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
        if (GUILayout.Button("保存为新 Prefab...", GUILayout.Width(140), GUILayout.Height(28)))
        {
            SaveMergedPrefab();
        }
        GUI.backgroundColor = oldBg;

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);
    }

    // ─── 图例 ───
    private void DrawLegend()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        DrawLegendItem(new Color(0.7f, 1f, 0.7f), "= 两边相同");
        DrawLegendItem(new Color(1f, 1f, 0.5f), "≠ 有差异");
        DrawLegendItem(new Color(0.6f, 0.8f, 1f), "A 仅在A");
        DrawLegendItem(new Color(1f, 0.7f, 0.5f), "B 仅在B");
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawLegendItem(Color color, string text)
    {
        var rect = GUILayoutUtility.GetRect(14, 14);
        EditorGUI.DrawRect(rect, color);
        GUILayout.Label(text, EditorStyles.miniLabel);
        GUILayout.Space(8);
    }

    // ─── 核心逻辑 ───
    private void RunCompare()
    {
        UnloadPrefabContents();

        string pathA = AssetDatabase.GetAssetPath(_prefabA);
        string pathB = AssetDatabase.GetAssetPath(_prefabB);

        if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB))
        {
            EditorUtility.DisplayDialog("错误", "无法获取 Prefab 路径，请确保两个 Prefab 都是有效的资产。", "确定");
            return;
        }

        _loadedRootA = PrefabUtility.LoadPrefabContents(pathA);
        _loadedRootB = PrefabUtility.LoadPrefabContents(pathB);

        if (_loadedRootA == null || _loadedRootB == null)
        {
            EditorUtility.DisplayDialog("错误", "加载 Prefab 失败。", "确定");
            UnloadPrefabContents();
            return;
        }

        _mergeRoot = _logic.BuildMergeTree(_loadedRootA.transform, _loadedRootB.transform, _compareOptions);
        _treeView.SetData(_mergeRoot);
        _treeView.ExpandAll();
        _isCompared = true;
        _statusMessage = $"对比完成 - A: {pathA}  B: {pathB}";
        Repaint();
    }

    private void ResetCompare()
    {
        UnloadPrefabContents();
        _mergeRoot = null;
        _isCompared = false;
        _statusMessage = "";
        _treeView.SetData(null);
        Repaint();
    }

    private void UnloadPrefabContents()
    {
        if (_loadedRootA != null)
        {
            PrefabUtility.UnloadPrefabContents(_loadedRootA);
            _loadedRootA = null;
        }
        if (_loadedRootB != null)
        {
            PrefabUtility.UnloadPrefabContents(_loadedRootB);
            _loadedRootB = null;
        }
    }

    // ─── 外部 Prefab 导入 ───
    private void ImportExternalPrefab()
    {
        string filePath = EditorUtility.OpenFilePanel("选择外部 Prefab", "", "prefab");
        if (string.IsNullOrEmpty(filePath))
            return;

        CleanupExternalImport();

        string tempDir = "Assets/_TempPrefabMerge";
        if (!AssetDatabase.IsValidFolder(tempDir))
            AssetDatabase.CreateFolder("Assets", "_TempPrefabMerge");

        string fileName = Path.GetFileName(filePath);
        string destPath = tempDir + "/" + fileName;

        // 避免重名
        int counter = 0;
        while (File.Exists(destPath))
        {
            counter++;
            destPath = tempDir + "/" + Path.GetFileNameWithoutExtension(fileName) + "_" + counter + ".prefab";
        }

        File.Copy(filePath, destPath, true);
        AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        var imported = AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
        if (imported != null)
        {
            _prefabB = imported;
            _externalPrefabPath = filePath;
            _statusMessage = "已导入外部 Prefab: " + fileName;
        }
        else
        {
            EditorUtility.DisplayDialog("导入失败",
                "无法导入外部 Prefab，可能是因为引用了本工程中不存在的脚本或资源。\n" +
                "文件已复制到: " + destPath + "\n" +
                "部分组件可能显示为 Missing，但节点结构仍可用于合并。", "确定");

            _prefabB = AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
            if (_prefabB != null)
                _externalPrefabPath = filePath;
        }

        Repaint();
    }

    private void CleanupExternalImport()
    {
        if (string.IsNullOrEmpty(_externalPrefabPath))
            return;

        string tempDir = "Assets/_TempPrefabMerge";
        if (AssetDatabase.IsValidFolder(tempDir))
        {
            AssetDatabase.DeleteAsset(tempDir);
        }
        _externalPrefabPath = null;
    }

    // ─── 覆盖原 Prefab ───
    private void OverwritePrefab(bool overwriteA)
    {
        if (_mergeRoot == null)
        {
            EditorUtility.DisplayDialog("错误", "请先执行对比。", "确定");
            return;
        }

        if (_loadedRootA == null && _loadedRootB == null)
        {
            EditorUtility.DisplayDialog("错误", "Prefab 数据已卸载，请重新对比。", "确定");
            return;
        }

        GameObject target = overwriteA ? _prefabA : _prefabB;
        string targetPath = AssetDatabase.GetAssetPath(target);
        string label = overwriteA ? "A" : "B";

        if (string.IsNullOrEmpty(targetPath))
        {
            EditorUtility.DisplayDialog("错误", $"无法获取 Prefab {label} 的路径。", "确定");
            return;
        }

        int modified, added, removed;
        _logic.CountChanges(_mergeRoot, overwriteA, out modified, out added, out removed);
        int totalChanges = modified + added + removed;

        if (totalChanges == 0)
        {
            EditorUtility.DisplayDialog($"无需覆盖 Prefab {label}",
                $"合并结果与原始 Prefab {label} 完全一致，没有任何改动。", "确定");
            return;
        }

        string summary = $"即将覆盖 Prefab {label}:\n{targetPath}\n\n"
            + $"相对于原始 Prefab {label} 的改动:\n"
            + $"  • 修改节点: {modified} 个\n"
            + $"  • 新增节点: {added} 个\n"
            + $"  • 删除节点: {removed} 个\n"
            + $"  • 合计变更: {totalChanges} 个节点\n\n"
            + "此操作不可撤销，是否继续？";

        if (!EditorUtility.DisplayDialog($"确认覆盖 Prefab {label}", summary, "确认覆盖", "取消"))
            return;

        // 只卸载 A 的编辑实例（BuildMergedOnPrefabA 会重新加载 A）
        // 保留 B 的实例不动，因为增量合并过程中需要从 B 的 Transform 拷贝节点
        if (_loadedRootA != null)
        {
            PrefabUtility.UnloadPrefabContents(_loadedRootA);
            _loadedRootA = null;
        }

        string prefabAPath = AssetDatabase.GetAssetPath(_prefabA);
        GameObject editableRoot = null;
        try
        {
            editableRoot = _logic.BuildMergedOnPrefabA(prefabAPath, _mergeRoot);
            if (editableRoot == null)
            {
                EditorUtility.DisplayDialog("错误", "加载 Prefab 失败。", "确定");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(editableRoot, targetPath);

            _statusMessage = $"已覆盖 Prefab {label}: {targetPath}";
        }
        finally
        {
            if (editableRoot != null)
                PrefabUtility.UnloadPrefabContents(editableRoot);
        }

        // 现在可以安全卸载 B 了
        if (_loadedRootB != null)
        {
            PrefabUtility.UnloadPrefabContents(_loadedRootB);
            _loadedRootB = null;
        }

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("覆盖成功",
            $"Prefab {label} 已覆盖:\n{targetPath}\n\n共变更 {totalChanges} 个节点", "确定");

        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(targetPath));

        // 重新加载用于对比
        RunCompare();
    }

    // ─── 保存合并结果 ───
    private void SaveMergedPrefab()
    {
        if (_mergeRoot == null)
        {
            EditorUtility.DisplayDialog("错误", "请先执行对比。", "确定");
            return;
        }

        if (_loadedRootA == null && _loadedRootB == null)
        {
            EditorUtility.DisplayDialog("错误", "Prefab 数据已卸载，请重新对比。", "确定");
            return;
        }

        string savePath = EditorUtility.SaveFilePanelInProject("保存合并后的 Prefab", "MergedPrefab", "prefab",
            "选择保存位置");
        if (string.IsNullOrEmpty(savePath))
            return;

        // 只卸载 A（会重新加载），保留 B 的实例供增量合并读取
        if (_loadedRootA != null)
        {
            PrefabUtility.UnloadPrefabContents(_loadedRootA);
            _loadedRootA = null;
        }

        string prefabAPath = AssetDatabase.GetAssetPath(_prefabA);
        GameObject editableRoot = null;
        try
        {
            editableRoot = _logic.BuildMergedOnPrefabA(prefabAPath, _mergeRoot);
            if (editableRoot == null)
            {
                EditorUtility.DisplayDialog("错误", "加载 Prefab A 失败。", "确定");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(editableRoot, savePath);
            _statusMessage = "已保存: " + savePath;
        }
        finally
        {
            if (editableRoot != null)
                PrefabUtility.UnloadPrefabContents(editableRoot);
        }

        if (_loadedRootB != null)
        {
            PrefabUtility.UnloadPrefabContents(_loadedRootB);
            _loadedRootB = null;
        }

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("保存成功",
            $"合并后的 Prefab 已保存到:\n{savePath}", "确定");

        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(savePath));

        // 重新加载用于对比
        RunCompare();
    }

    /// <summary>
    /// 批量选择：「全部保留 A」表示结果与 Prefab A 对齐——仅 B 有的节点应丢弃，不能设成 KeepA
    /// （KeepA 在 OnlyInB 上会回退到用 B 的 Transform，等价于把 B 独有节点加入 A，仍会计为改动）。
    /// 「全部保留 B」同理：仅 A 有的节点丢弃。
    /// </summary>
    private void SetAllChoice(MergeChoice bulkChoice)
    {
        if (_mergeRoot == null) return;
        SetAllChoiceRecursive(_mergeRoot, bulkChoice);
        _treeView.SetData(_mergeRoot);
        _treeView.ExpandAll();
    }

    private void SetAllChoiceRecursive(MergeNodeData node, MergeChoice bulkChoice)
    {
        if (bulkChoice == MergeChoice.KeepA)
        {
            if (node.Presence == NodePresence.OnlyInB)
            {
                SetChoiceRecursive(node, MergeChoice.Discard);
                return;
            }
            node.Choice = MergeChoice.KeepA;
        }
        else if (bulkChoice == MergeChoice.KeepB)
        {
            if (node.Presence == NodePresence.OnlyInA)
            {
                SetChoiceRecursive(node, MergeChoice.Discard);
                return;
            }
            node.Choice = MergeChoice.KeepB;
        }
        else
        {
            node.Choice = bulkChoice;
        }

        foreach (var child in node.Children)
            SetAllChoiceRecursive(child, bulkChoice);
    }

    private void SetChoiceRecursive(MergeNodeData node, MergeChoice choice)
    {
        node.Choice = choice;
        if (node.Presence == NodePresence.OnlyInB)
            node.ExplicitKeepB = (choice == MergeChoice.KeepB);
        foreach (var child in node.Children)
            SetChoiceRecursive(child, choice);
    }
}
