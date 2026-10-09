# YUIFramework

YUIFramework 是一个面向 **Unity uGUI** 的可扩展 UI 框架，当前仓库实现了 **P1 核心骨架 + P2 栈式页面导航 + P3 资源加载体系增强 + P4 UI 对象池缓存增强 + P5 UI 消息中心 + P6 轻量虚拟列表 + P7 轻量转场动画 + P8 轻量 MVVM / 数据绑定基础层 + P9 YooAsset 资源 Bootstrap**。

设计灵感来自：
- 原神 `MoleMole.UIManager`（Context / Layer / 配置驱动）
- GameFramework（分组深度、生命周期、资源抽象）
- LoxodonFramework（后续 MVVM 与数据绑定）

> 已验证 Unity 版本：**2022.3.62f2 LTS**

## Y2.0 改造基线

Y2.0 正在按阶段建立商用基线。阶段 0 到阶段 6 已完成：除阶段 0-5 的 UI 生命周期、并发、输入和资源所有权外，现已提供不可变 Bootstrap Profile、集中状态图、三运行模式、弱网重试/超时、主备 CDN、验证回退、磁盘/确认门禁、结构化进度与实例化 Reset/Shutdown。

- 基线说明：[`Documentation/Y2.0/Baseline.md`](Documentation/Y2.0/Baseline.md)
- 分阶段路线：[`Documentation/Y2.0/Roadmap.md`](Documentation/Y2.0/Roadmap.md)
- API 迁移矩阵：[`Documentation/Y2.0/ApiMigrationMatrix.md`](Documentation/Y2.0/ApiMigrationMatrix.md)
- Y2 运行时契约：[`Documentation/Y2.0/Contracts.md`](Documentation/Y2.0/Contracts.md)
- 资源所有权体系：[`Documentation/Y2.0/Resources.md`](Documentation/Y2.0/Resources.md)
- 资源 Bootstrap：[`Documentation/Y2.0/Bootstrap.md`](Documentation/Y2.0/Bootstrap.md)
- Bootstrap 迁移：[`Documentation/Y2.0/Migration.md`](Documentation/Y2.0/Migration.md)
- 测试说明：[`Documentation/Y2.0/Testing.md`](Documentation/Y2.0/Testing.md)
- 变更记录：[`CHANGELOG.md`](CHANGELOG.md)

## 当前阶段

当前实现包含：
- P1 核心骨架（✅）
- P2 栈式页面导航 `UINavigator`（✅）
- P3 资源加载体系增强（✅，Resources；Y2.0 阶段 5 起生产后端为 YooAsset）
- P4 UI 对象池 / 缓存增强（✅）
- P5 UI 消息中心 / 事件总线（✅）
- P6 虚拟列表 / 大量 UI 元素优化（✅）
- P7 UI 转场动画 / 页面过渡系统（✅）
- P8 MVVM / 数据绑定基础层（✅）
- P9 YooAsset 资源 Bootstrap（✅，Y2.0 阶段 6；YooAsset 3.0.5 + UniTask）

核心能力：
- 分层系统（`UILayer` + 每层独立 Canvas）
- Context 生命周期（`OnInit -> OnShow -> OnHide -> OnClose -> OnDestroy`）
- 资源加载抽象（`IResourceLoader` + `ResourcesLoader`）
- 资源所有权体系（`IUIResourceService` + `UIResourceKey` + 资源/实例租约，Y2.0 阶段 5）
- 资源更新 Bootstrap（不可变 Profile + 状态图 + `BootstrapRunner`，Y2.0 阶段 6）
- 核心调度器（`UIManager`）
- Page 栈导航（`Push / Pop / Replace / Back`）
- UI 缓存池（`CacheOnClose` + `MaxPoolSize`）
- 可选商业虚拟列表（SuperScrollView 2.5.3，纵横列表、动态尺寸与固定 Grid；需本地合法安装）
- 轻量转场动画（Fade / Scale / Slide）
- 轻量 MVVM（`ObservableProperty` + `UIDataBinding`）
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

P7 生命周期语义：
- 首次创建：`OnInit -> OnShow`
- 打开（启用转场）：`OnInit（首次） -> SetActive(true) -> OnShow(args) -> ShowTransition`
- 关闭：`HideTransition -> OnHide -> OnClose -> Pool/Destroy`
- 关闭入池：`HideTransition -> OnHide -> OnClose -> SetActive(false)`
- 池中取回：`SetActive(true) -> OnShow -> ShowTransition`（不会重复 `OnInit`）
- 池满/不缓存：`OnDestroy -> Release`

## 快速开始

1. 在场景中创建空物体，挂载 `HelloUIBootstrap`。
2. 运行场景后会自动初始化框架并打开示例页面。

最小示例：

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework;

public class HelloUIBootstrap : MonoBehaviour
{
    private void Start()
    {
        RunAsync(destroyCancellationToken).Forget(Debug.LogException);
    }

    private async UniTask RunAsync(System.Threading.CancellationToken cancellationToken)
    {
        var uiManager = new UIManager();
        await uiManager.InitializeAsync(
            new CodeViewLoader(),
            cancellationToken: cancellationToken);
        uiManager.Register<SampleHelloPage>(new UIConfig
        {
            Id = "HelloPage",
            PrefabKey = "SampleHelloPage",
            Layer = UILayer.Normal,
            CacheOnClose = true,
            MaxPoolSize = 1,
            FullScreen = true,
        });

        await uiManager.Navigator.PushAsync<SampleHelloPage>(
            "Hello YUIFramework!",
            cancellationToken: cancellationToken);
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

### 生产资源后端：YooAsset（Y2.0 阶段 5）

Y2.0 阶段 5 起，YooAsset 3.x 是唯一的生产资源后端，Addressables 支持已移除
（`AddressablesLoader` 与 `YUIFRAMEWORK_ADDRESSABLES` 已删除），Resources 仅保留用于测试与
最小化 Editor 兼容。

```csharp
var registry = new UIResourcePackageRegistry();
registry.Register(
    new YUIFramework.Bootstrap.YooAsset.YooAssetResourceProvider(package),
    isDefault: true);
UIManager.Instance.Initialize(new UIResourceService(registry));
```

资源租约与实例租约分离、同 key 并发共享加载、逐调用方取消、预加载、批量部分失败与泄漏诊断
详见 [`Documentation/Y2.0/Resources.md`](Documentation/Y2.0/Resources.md)。

### 如何避免错误路径

避免把 `PrefabKey` 写成：
- `Assets/Resources/UI/Pages/MainMenuPage.prefab`
- `\\UI\\Pages\\MainMenuPage.prefab`

`ResourcesLoader` 会对常见错误做规范化与日志提示，但建议在配置阶段直接使用逻辑 key。

### Resources vs YooAsset

| 对比项 | ResourcesResourceProvider | YooAssetResourceProvider |
|---|---|---|
| 用途 | 仅测试 / 最小 Editor 兼容 | 生产唯一后端 |
| Key 约定 | `UI/Pages/MainMenuPage` | package + location + type |
| 是否支持资源更新 | ❌ | ✅ |
| 句柄管理 | 无底层句柄 | 底层句柄 + 引用计数租约 |

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

Y2.0 阶段10已将旧 `UIVirtualList` 内核替换为可选 **SuperScrollView 2.5.3**
适配器。旧类型为 Obsolete 转发 facade，不再维护第二套内核；未安装插件时基础框架
仍可编译，但调用列表 facade 会明确提示安装。

新接入使用 `UIListDataSource<T>`、稳定 ID 的 `UIListSelection` 和
`SuperScrollViewList<T>`，支持纵向/横向列表、动态列表尺寸、固定 Grid、增删改移与
anchor 保持、带 generation 和资源租约的异步图片绑定。显示期清理由
`TrackDisplayBinding(list.BeginDisplay(this))` 接入现有 Context 生命周期。

供应商源码仅在用户合法持有的本地副本中安装，不随框架公开分发。安装命令、程序集
边界、迁移限制和原创 `SuperScrollViewSamplePage` 示例见
[Virtualization.md](Documentation/Y2.0/Virtualization.md)。动态 Grid、无限循环和
staggered 布局未由当前适配器提供；不根据供应商 Demo 名称推断支持。

## P7 UI 转场动画 / 页面过渡系统

P7 新增 `Runtime/Transitions`，默认不开启。单个页面可在 `UIConfig` 中配置：

```csharp
uiManager.Register<MainMenuPageContext>(new UIConfig
{
    Id = "MainMenuPage",
    PrefabKey = "UI/Pages/MainMenuPage",
    Layer = UILayer.Normal,
    CacheOnClose = true,
    FullScreen = true,
    UseTransition = true,
    TransitionType = UITransitionType.Fade,
    ShowDuration = 0.25f,
    HideDuration = 0.15f,
});
```

支持类型：
- `None`
- `Fade`
- `Scale`
- `SlideLeft / SlideRight / SlideUp / SlideDown`

说明：
- Navigator 的 `Push/Pop/Replace/Back` 通过 `UIManager.OpenAsync/CloseAsync` 自动触发转场。
- `HideWithoutClose`（栈下页面临时隐藏）保持原行为，不播放 close transition。
- 对象池复用时：出池播放 Show，入池前播放 Hide。

## P8 MVVM / 数据绑定基础层

P8 新增 `Runtime/MVVM`，提供轻量可观察属性、集合和 uGUI 代码式绑定：

- `ObservableProperty<T>`：值变化通知
- `ObservableCollection<T>`：集合变更通知（Add / Remove / Clear / Reset）
- `ViewModelBase`：统一跟踪并释放订阅
- `UIDataBinding`：绑定 `Text / Toggle / Slider`
- 绑定模式：`OneWay / TwoWay / OneTime`

基础示例：

```csharp
public sealed class LoginViewModel : ViewModelBase
{
    public ObservableProperty<string> UserName { get; } = new ObservableProperty<string>(string.Empty);
    public ObservableProperty<bool> RememberMe { get; } = new ObservableProperty<bool>(false);
}

protected override void HandleInit()
{
    var vm = new LoginViewModel();
    SetViewModel(vm);
    TrackBinding(UIDataBinding.BindText(titleText, vm.UserName));
    TrackBinding(UIDataBinding.BindToggle(toggle, vm.RememberMe));
}
```

生命周期说明：
- `BaseContext.OnDestroy` 会自动调用 `ClearBindings()` 与 `ClearViewModel()`。
- 入池对象不会触发 `OnDestroy`，因此 ViewModel 与绑定会保留。
- 若业务要求隐藏即解绑，可在 `HandleHide` 手动调用 `ClearBindings()` / `ClearViewModel()`。

## P9 / Y2 阶段 6：YooAsset 资源 Bootstrap

阶段 6 将资源更新与代码热更新彻底分名：`BootstrapRunner` 只保证 YooAsset 资源就绪；
`IBootstrapCodeLoader` 是资源就绪后的可选扩展点，默认 no-op，本阶段不依赖 HybridCLR。

> 依赖：`com.tuyoogame.yooasset` 3.0.5 + `com.cysharp.unitask`。

### 单向程序集边界

```text
YUIFramework.Runtime
  -> YUIFramework.Bootstrap
  -> YUIFramework.Bootstrap.YooAsset
  -> YUIFramework.HotUpdate（仅 Obsolete 兼容 facade）
```

| 类型 | 作用 |
|---|---|
| `BootstrapProfile` | 不可变且全面验证的模式/package/app/channel/version/CDN/超时/重试/磁盘/确认策略 |
| `BootstrapStateGraph` | 唯一合法状态边；未验证资源绝不进入业务 |
| `BootstrapRunner` | single-flight、逐调用方取消、重试/超时、fallback、Reset/Shutdown |
| `IBootstrapBackend` | 可 fake 的纯后端契约 |
| `YooAssetBootstrapBackend` | YooAsset 3.0.5 生产适配器，检查所有 operation status/error |
| `YooAssetBootstrapComposition` | 把 ready packages 组合成多 package `UIResourceService` |
| `IBootstrapCodeLoader` | ResourcesReady 后、EnteringGame 前的可选代码扩展 |
| `IBootstrapProgressSink` / `IBootstrapTelemetrySink` | 带 RunId 的结构化进度与诊断 |

### 启动门禁

```text
Idle -> InitializingPackage -> RequestingVersion -> ActivatingManifest
     -> CalculatingDownload -> [Confirm -> Disk -> Download -> Verify]
     -> ResourcesReady -> LoadingCodeExtension -> EnteringGame -> Completed
```

`EditorSimulate` 仅允许 Editor 且不联网；`Offline` 只读内置文件；`Host` 才走完整远端流程。
主 CDN 按有界指数退避重试后才切备用 CDN。仅 Profile 允许且本地/内置 manifest 已验证时
才可 fallback；结果显式标记 `Degraded`，绝不伪装为“已更新到最新”。

### 最小生产接线

```csharp
var profile = new BootstrapProfile(
    BootstrapMode.Host,
    new[] { new BootstrapPackageProfile("DefaultPackage") },
    "com.example.game",
    "release",
    Application.version,
    new Uri("https://cdn.example.com/content/"),
    new Uri("https://backup.example.com/content/"));

var runner = new BootstrapRunner(
    new YooAssetBootstrapBackend(),
    gameEntry,
    progress: progressSink,
    telemetry: telemetrySink);

var result = await runner.RunAsync(profile, cancellationToken);
```

`IBootstrapGameEntry.EnterAsync` 中使用：

```csharp
var composition = YooAssetBootstrapComposition.Create(readyContext);
await ui.InitializeAsync(
    composition.ResourceService,
    cancellationToken: cancellationToken);
```

关闭顺序固定为 UI → `composition.ShutdownResourceServiceAsync()` → `runner.ShutdownAsync()`，
确保 UI 租约和 YooAsset handle 先释放，再销毁 package。

编辑器菜单 `Tools/YUIFramework/Bootstrap Profile` 只保存/验证创作默认值，不向运行时写
static 配置。`Examples/HotUpdateStartupSample.cs` 因保留旧 MonoScript 引用而保留文件名，
内部已完全改用实例化 Y2 Bootstrap API。

旧 `HotUpdateConfig/Manager/Launcher/RemoteServices/StartupFlowTrace/YooAssetLoader` 均为
`[Obsolete]` 转发 facade；原 GameLauncher/ProgressUI 等 `.cs.meta` GUID 保留，且静态事件在
`SubsystemRegistration` 全量清理。

### YooAsset 2.x → 3.0.5 原生 API 说明

参考代码基于 YooAsset 2.3.18，本项目使用 3.0.5，且**采用 3.x 原生 API**（未开启 `YOOASSET_LEGACY_API` 兼容层，因此没有 `[Obsolete]` 警告）。关键映射：

| 用途 | 2.3 兼容写法（本项目未用） | 3.0.5 原生写法（本项目采用） |
| --- | --- | --- |
| 远端地址 | `IRemoteServices.GetRemoteMainURL/FallbackURL` | `IRemoteService.GetRemoteUrls`（返回候选地址列表） |
| 初始化参数 | `InitializeParameters` + `XxxModeParameters` | `InitializePackageOptions` + `EditorSimulateModeOptions`/`OfflinePlayModeOptions`/`HostPlayModeOptions` |
| 初始化 | `package.InitializeAsync(params)` | `package.InitializePackageAsync(options)` |
| 缓存文件系统 | `CreateDefaultCacheFileSystemParameters` | `CreateDefaultSandboxFileSystemParameters`（Cache 更名 Sandbox） |
| 请求版本 | `RequestPackageVersionAsync(bool,int)` | `RequestPackageVersionAsync(new RequestPackageVersionOptions(bool,int))` |
| 更新清单 | `UpdatePackageManifestAsync(version)` | `LoadPackageManifestAsync(new LoadPackageManifestOptions(version,timeout))` |
| 创建下载器 | `CreateResourceDownloader(int,int)` | `CreateResourceDownloader(new ResourceDownloaderOptions(int,int))` |
| 下载进度 | `DownloadUpdateCallback` + `BeginDownload()` | `DownloadProgressChanged` 事件 + `StartDownload()` |
| 资源校验 | `package.CheckLocationValid(location)` | `package.GetAssetInfo(location).IsValid` |
| 等待操作 | `await op.Task` | `await op`（`OperationAwaiter`） |
| 句柄错误 | `handle.LastError` | `handle.Error` |
| 编辑器模拟构建 | `EditorSimulateModeHelper.SimulateBuild(name)` | `EditorSimulateBuildInvoker.Build(name, (int)EBundleType.VirtualAssetBundle)` |

内置文件系统用 `CreateDefaultBuiltinFileSystemParameters()`，状态枚举统一使用 `EOperationStatus.Succeeded`。

## 路线图

- P1 核心骨架（✅）
- P2 栈式导航 `UINavigator`（✅）
- P3 资源加载体系增强（✅，Resources；Y2.0 阶段 5 起生产后端为 YooAsset）
- P4 对象池 / UI 缓存增强（✅）
- P5 消息中心（✅）
- P6 虚拟列表 / 大量 UI 元素优化（✅）
- P7 转场动画（✅）
- P8 MVVM / 数据绑定（✅）
- P9 YooAsset 资源 Bootstrap（✅，Y2.0 阶段 6）
- P10 Editor 工具 / 代码生成 / 测试完善（⏳）

---

当前仓库已落地 P1 ~ P9；Y2.0 阶段 6 已完成，阶段 7 尚未开始。
