# Prefab 对比与合并（ComparePrefab）

Unity Editor 扩展：在编辑器中对比两个 Prefab 的层级与组件差异，交互式选择合并策略，并将结果**保存为新 Prefab** 或**覆盖**现有 Prefab A / B。

> 新功能入口：**菜单 `Tools` → `Prefab Merge Editor`**。  
> 类 `PrefabComparer` 提供静态 API，供脚本快速统计差异（向后兼容）。

---

## 功能概览

| 能力 | 说明 |
|------|------|
| 双 Prefab 对比 | 指定 **Prefab A（本工程）** 与 **Prefab B（本工程或外部文件）** |
| 外部 Prefab | 通过「导入外部 Prefab...」复制到 `Assets/_TempPrefabMerge`，再作为 B 参与对比 |
| 合并策略 | 按节点选择保留 A、保留 B、**两边都保留（AB）** 或丢弃 |
| 输出 | **保存为新 Prefab...**、**覆盖 A**、**覆盖 B**（外部导入的 B 不可覆盖，仅可另存） |
| 详情面板 | 基础属性、组件级 `SerializedProperty` 对比；可勾选「显示全部属性」 |

合并结果始终以 **Prefab A 的资产为基底** 做增量修改（`BuildMergedOnPrefabA`），再保存到目标路径。

---

## 打开方式

1. 菜单：**`Tools` → `Prefab Merge Editor`**
2. 窗口标题：**Prefab 合并编辑器**（建议最小尺寸约 700×500）

---

## 使用流程

1. **Prefab A**：拖入本工程中的 Prefab 资源（作为合并基底）。
2. **Prefab B**：拖入本工程 Prefab，或点击 **「导入外部 Prefab...」** 选择 `.prefab` 文件。
3. 按需勾选 **对比选项**（见下文），点击 **「开始对比」**。
4. 在树形列表中逐节点选择 **A / B / AB / ✗**，或使用 **「全部采用 A 侧」/「全部采用 B 侧」** 批量策略。
5. 使用 **「保存为新 Prefab...」** 另存，或 **「覆盖 A」/「覆盖 B」** 写回原资产（覆盖前会提示变更统计）。

修改对比选项后需再次点击 **「开始对比」** 才会生效。

---

## 对比选项（`PrefabCompareOptions`）

仅影响**是否有差异**的判定与详情展示，不改变合并算法本身；合并时仍会按你在树上的选择执行。

| 选项 | 作用 |
|------|------|
| 忽略位置/旋转/缩放 | 不比较 `localPosition` / `localEulerAngles` / `localScale` |
| 忽略激活状态 | 不比较 `activeSelf` |
| 忽略 Layer/Tag | 不比较 Layer、Tag |
| 忽略组件序列化内容 | 仍比较组件**类型列表**是否一致，但不逐字段对比序列化属性 |

---

## 节点状态与图例

- **两边相同**：两侧节点匹配且无差异（绿色系）。
- **有差异**：同名匹配节点在属性或组件上不一致（黄色系）。
- **仅 A / 仅 B**：同层按**规范化后的名称**配对后，只在一侧存在的节点（蓝 / 橙色系）。

子节点按**名称**匹配（会 Trim 并去除常见零宽字符，减少误拆成「仅 A」「仅 B」）。

---

## 合并选择含义

- **两边都有且不同（≠）**
  - **A / B**：有差异时，合并结果中该节点采用 **A 侧或 B 侧** 的属性（不是「只在一侧才保留」的意思）。
  - **AB**：保留 A 上同名节点，并从 B **再克隆一份**，克隆体名称加 **`_B`**（若重名则 `_B1`、`_B2`…）。
  - **✗**：从合并结果中**删除**该节点（若 A 上有则删除）。
- **仅在一侧**
  - **保留 A / 保留 B**：写入结果，或从 B 克隆进 A 的结构。
  - **✗**：丢弃该侧独有节点（不并入结果）。

批量 **「全部采用 A 侧」**：有差异时偏向 A；**仅在 B** 的节点会批量丢弃。  
**「全部采用 B 侧」** 同理，**仅在 A** 的节点会批量丢弃。

详情面板顶部会显示当前节点合并语义的简短说明。

---

## 界面操作

- **隐藏相同节点**：只显示存在差异或单侧节点的子树路径。
- **搜索**：按节点名过滤，支持 **▲ / ▼** 在匹配项间跳转。
- **分割条**：拖拽可调整树视图与下方详情区高度。

---

## 代码 API（`PrefabComparer`）

在 Editor 脚本或其它工具中可调用：

```csharp
string summary = PrefabComparer.ComparePrefabs(gameObjectA, gameObjectB);
// 返回示例: "总节点: N, 相同: x, 有差异: y, 仅A: z, 仅B: w"
```

内部使用 `PrefabComparerLogic.BuildMergeTree` 与 `CountStats`，**不包含**合并选项与交互式窗口逻辑。

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `PrefabComparerWindow.cs` | 编辑器窗口 UI、外部导入、保存/覆盖流程 |
| `PrefabComparerLogic.cs` | 构建合并树、组件/属性对比、应用合并到 A 的实例 |
| `PrefabMergeTreeView.cs` | `TreeView`：行内 A/B/AB/✗、搜索、隐藏相同节点 |
| `PrefabMergeData.cs` | `MergeNodeData`、`PrefabCompareOptions`、枚举与差异数据结构 |
| `PrefabComparer.cs` | 静态入口 `ComparePrefabs`（简单统计字符串） |

---

## 注意事项

1. **覆盖 B**：若 Prefab B 来自 **外部导入**，界面会禁用对 B 的原地覆盖；请使用 **保存为新 Prefab** 或先将资源迁入工程。
2. **外部 Prefab**：若依赖本工程不存在的脚本或资源，导入可能不完整（Missing 组件），但**节点结构**仍可用于对比与合并；关闭窗口或清理时会尝试删除临时目录 `Assets/_TempPrefabMerge`。
3. **覆盖 / 另存** 后会重新执行一次对比以刷新状态；**不可撤销**，重要资产请先备份或使用版本控制。

---

## 依赖环境

- Unity Editor（使用 `PrefabUtility`、`SerializedObject`、IMGUI `TreeView` 等 API）
- 脚本位于 `Assets/Editor/`，仅编辑器下编译
