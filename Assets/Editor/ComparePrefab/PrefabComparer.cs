using UnityEngine;
using UnityEditor;

/// <summary>
/// Prefab 比较静态工具入口（保留向后兼容）
/// 新功能请使用 Tools/Prefab Merge Editor 菜单
/// </summary>
public class PrefabComparer
{
    public static string ComparePrefabs(GameObject prefab1, GameObject prefab2)
    {
        if (prefab1 == null || prefab2 == null)
            return "一个或两个 prefab 为 null";

        var logic = new PrefabComparerLogic();
        var root = logic.BuildMergeTree(prefab1.transform, prefab2.transform);

        int total, same, diff, onlyA, onlyB;
        logic.CountStats(root, out total, out same, out diff, out onlyA, out onlyB);

        return $"总节点: {total}, 相同: {same}, 有差异: {diff}, 仅A: {onlyA}, 仅B: {onlyB}";
    }
}
