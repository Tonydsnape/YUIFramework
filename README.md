# YUIFramework

YUIFramework 是一个面向 **Unity uGUI** 的可扩展 UI 框架，当前仓库实现了 **P1 核心骨架 + P2 栈式页面导航 + P3 资源加载体系增强 + P4 UI 对象池缓存增强 + P5 UI 消息中心 + P6 轻量虚拟列表**。

设计灵感来自：
- 原神 `MoleMole.UIManager`（Context / Layer / 配置驱动）
- GameFramework（分组深度、生命周期、资源抽象）
- LoxodonFramework（后续 MVVM 与数据绑定）

> Unity 版本：**2022.3.14f1 LTS**

## 当前阶段

当前实现包含：
- P1 核心骨架（✅）
- P2 栈式页面导航 `UINavigator`（✅）
- P3 资源加载体系增强（✅，Resources + 可选 Addressables）
- P4 UI 对象池 / 缓存增强（✅）
- P5 UI 消息中心 / 事件总线（✅）
- P6 虚拟列表 / 大量 UI 元素优化（✅）

核心能力：
- 分层系统（`UILayer` + 每层独立 Canvas）
- Context 生命周期（`OnInit -> OnShow -> OnHide -> OnClose -> OnDestroy`）
- 资源加载抽象（`IResourceLoader` + `ResourcesLoader`）
- 可选 Addressables 接入（`AddressablesLoader`，按包版本自动启用）
- 核心调度器（`UIManager`）
- Page 栈导航（`Push / Pop / Replace / Back`）
- UI 缓存池（`CacheOnClose` + `MaxPoolSize`）
- 轻量虚拟列表（`UIVirtualList`，固定尺寸 item 复用）
- 纯代码示例（无需提交 prefab / scene 二进制资源）

## 架构总览

```text
+--------------------------+
|        UIManager         |
| Init/Register/Open/Close |
+-----------+--------------+
            |
            v
+--------------------------+      +---------------------+
|      UILayerManager      |----->|      UIRoot         |
| layer root/sorting order |      | Canvas + EventSystem|
+-----------+--------------+      +----------+----------+
            |                                |
            v                                v
+--------------------------+      +---------------------+
|        BaseContext       |<---->|       UIView        |
| lifecycle + state        |      | GameObject bridge   |
+-----------+--------------+      +---------------------+
            |
            v
+--------------------------+
|      IResourceLoader     |
| ResourcesLoader (P1)     |
+--------------------------+
```

## 分层说明

| 层级 | sortingOrder | 用途 |
|---|---:|---|
| Scene | 0 | 场景内 UI（预留） |
| Bottom | 100 | 底层 UI |
| Normal | 200 | 普通全屏页面（后续导航栈主工作层） |
| Fixed | 300 | 常驻 HUD / 固定挂件 |
| Popup | 400 | 弹窗层 |
| Guide | 500 | 引导层 |
| Top | 600 | 高优先级覆盖层 |
| System | 700 | Loading / 断线重连等系统层 |

## 生命周期

```text
OnInit -> OnShow -> OnHide -> OnClose -> OnDestroy
```

- `OnInit`：只调用一次，用于初始化绑定与控件缓存。
- `OnShow`：每次打开或重新显示时触发。
- `OnHide`：关闭流程中的隐藏阶段。
- `OnClose`：关闭流程中的业务收尾阶段。
- `OnDestroy`：对象释放前触发；缓存关闭策略下可能暂不触发。

P4 生命周期语义：
- 首次创建：`OnInit -> OnShow`
- 关闭入池：`OnHide -> OnClose -> SetActive(false)`
- 池中取回：`SetActive(true) -> OnShow`（不会重复 `OnInit`）
- 池满/不缓存：`OnDestroy -> Release`

## 快速开始

1. 在场景中创建空物体，挂载 `HelloUIBootstrap`。
2. 运行场景后会自动初始化框架并打开示例页面。

最小示例：

```csharp
using UnityEngine;
using YUIFramework;

public class HelloUIBootstrap : MonoBehaviour
{
    private async void Start()
    {
        var uiManager = UIManager.Instance;
        uiManager.Init(new CodeViewLoader());
        uiManager.Register<SampleHelloPage>(new UIConfig
        {
            Id = "HelloPage",
            PrefabKey = "SampleHelloPage",
            Layer = UILayer.Normal,
            CacheOnClose = true,
            MaxPoolSize = 1,
            FullScreen = true,
        });

        await uiManager.Navigator.PushAsync<SampleHelloPage>("Hello YUIFramework!");
    }
}
```

## P2 用法示例

```csharp
await UIManager.Instance.Navigator.PushAsync<MainMenuPageContext>();
await UIManager.Instance.Navigator.PushAsync<SettingPageContext>();
await UIManager.Instance.Navigator.PopAsync();
await UIManager.Instance.Navigator.ReplaceAsync<LoginPageContext>();
```

类型职责：
- Page：进入 `Navigator` 栈。
- Widget：常驻，不进导航栈。
- Dialog：弹窗，不进导航栈，可直接使用 `UIManager.OpenAsync<T>()` 管理。

## P3 资源加载体系

### 使用 ResourcesLoader

```csharp
UIManager.Instance.Init(new ResourcesLoader());
```

`PrefabKey` 推荐写法：

```csharp
PrefabKey = "UI/Pages/MainMenuPage"
```

Resources 资源文件路径示例：

```text
Assets/Resources/UI/Pages/MainMenuPage.prefab
```

### 使用 AddressablesLoader（仅安装 Addressables 后）

```csharp
#if YUIFRAMEWORK_ADDRESSABLES
UIManager.Instance.Init(new AddressablesLoader());
#endif
```

说明：
- Addressables 包安装后，`YUIFRAMEWORK_ADDRESSABLES` 会通过 asmdef 的 versionDefines 自动生效。
- 未安装 Addressables 时，`AddressablesLoader` 不会参与编译，不影响项目构建。
- 推荐将 UI Prefab Address 设为与 Resources 同风格的 key（如 `UI/Pages/MainMenuPage`）。

### 如何避免错误路径

避免把 `PrefabKey` 写成：
- `Assets/Resources/UI/Pages/MainMenuPage.prefab`
- `\\UI\\Pages\\MainMenuPage.prefab`

`ResourcesLoader` 会对常见错误做规范化与日志提示，但建议在配置阶段直接使用逻辑 key。

### Resources vs Addressables

| 对比项 | ResourcesLoader | AddressablesLoader |
|---|---|---|
| 是否开箱可用 | ✅ Unity 内置 | ⚠️ 需安装 `com.unity.addressables` |
| Key 约定 | `UI/Pages/MainMenuPage` | Address（建议同上） |
| 包体与更新策略 | 简单，适合小中型项目 | 更灵活，适合中大型项目 |
| 句柄管理 | 无需额外句柄 | 内置 handle + 引用计数释放 |

## P4 对象池 / UI 缓存增强

`CacheOnClose` 在 P4 中升级为对象池语义：关闭后会从 active contexts 移除并尝试入池。

初始化方式（两种都可用）：

```csharp
UIManager.Instance.Init(new ResourcesLoader());
UIManager.Instance.Init(new ResourcesLoader(), new UIObjectPool());
```

说明：重复 `Init` 不会清空已注册配置和 active contexts；如果传入新的 `IUIObjectPool`，会替换旧池并释放旧池缓存对象。

基础配置：

```csharp
CacheOnClose = true,
MaxPoolSize = 1,
```

额外字段：
- `MaxPoolSize`：每个 UI 类型最大池容量，`<= 0` 视为不缓存。
- `PreloadCount`：预加载数量预留字段（当前仅保留配置）。

适合缓存：
- 高频页面
- HUD
- 背包/角色面板
- 初始化复杂但会重复打开的界面

不适合缓存：
- 一次性弹窗
- 很少打开的大型页面
- 强绑定临时数据且释放成本低的 UI

清理缓存池：

```csharp
UIManager.Instance.ClearPool<MainMenuPageContext>();
UIManager.Instance.ClearAllPools();
```

## P5 UI 消息中心 / 事件总线

框架已内置 `UIMessageCenter`，用于 Context 间或 UI 与业务系统的轻量解耦通信。

基础用法：

```csharp
UIManager.Instance.MessageCenter.Subscribe<string>(
    "player.coin.changed",
    value => Debug.Log(value));

UIManager.Instance.MessageCenter.Publish("player.coin.changed", "100");
```

Context 内推荐用法：

```csharp
protected override void HandleInit()
{
    SubscribeMessage<int>("player.coin.changed", OnCoinChanged);
}

private void OnCoinChanged(int value)
{
    // refresh UI
}
```

生命周期建议：
- 长生命周期监听：`HandleInit` 订阅，`OnDestroy` 自动清理。
- 仅显示期间监听：`HandleShow` 订阅，`HandleHide` 手动 `Dispose`。
- 入池对象不会触发 `OnDestroy`，因此入池期间订阅可能保留，请按业务选择订阅时机。

## P6 虚拟列表 / 大量 UI 元素优化

P6 新增 `Runtime/VirtualList`，用于背包、邮件、排行榜、任务列表等大数据量 UI 场景，避免一次性创建大量 Item。

基础用法：

```csharp
public sealed class MailPage : BasePageContext, IUIVirtualListDataSource
{
    private UIVirtualList _list;

    protected override void HandleInit()
    {
        _list.SetDataSource(this);
        _list.ReloadData();
    }

    public int Count => 1000;

    public void BindItem(UIVirtualListItem item, int index)
    {
        // bind item
    }
}
```

当前限制：
- P6 仅支持固定 Item 尺寸。
- 垂直列表优先（水平为基础预留）。
- Grid / 不等高 / 循环列表为后续扩展。

## 路线图

- P1 核心骨架（✅）
- P2 栈式导航 `UINavigator`（✅）
- P3 资源加载体系增强（✅，Resources + 可选 Addressables）
- P4 对象池 / UI 缓存增强（✅）
- P5 消息中心（✅）
- P6 虚拟列表 / 大量 UI 元素优化（✅）
- P7 转场动画（⏳）
- P8 MVVM / 数据绑定
- P9 Editor 工具
- P10 示例与测试完善

---

当前仓库已落地 P1 + P2 + P3 + P4 + P5，P6 及后续模块待实现。
