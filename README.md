# OmniOnlineModules

Angelways 的 Omni Toolbox 在线模块仓库。一个发布文件只包含一个可实例化模块，源码与模块本地化随仓库公开维护。

## 商城物品收录导出 1.3.0

已迁移至 **模块 API 1**，最低要求 **Omni 1.1.8.2**。使用当前本机 Omni 1.1.8.7、Dalamud CN API 15、.NET 10 编译。

在“插件设置 → 在线模块”添加仓库地址：

```text
https://github.com/Angelways/OmniOnlineModules
```

此前订阅的模块清单地址继续保留，不需要重复添加：

```text
https://raw.githubusercontent.com/Angelways/OmniOnlineModules/main/Modules/ShopCollectionExport/TreeHouseModules.json
```

Omni 会自动处理 GitHub API 到 raw 的回退。根清单与原模块清单均使用新格式，模块文件路径相对仓库根目录。刷新仓库并按 Omni 的更新提示应用；首次安装后在“树树妙妙屋 → 在线”中手动启用。旧安装记录若被新版 Omni 提示不兼容，请使用上述仓库地址重新建立记录，保留用户配置。

1.3.0 使用 `OmniModuleBase`、`ModuleApiVersion(1)`、`FeatureLifetime` 和模块内 `ModuleLocalization.Create`，提供完整简体源词条及宿主生成的繁体。配置交由宿主绑定的 `Settings/config` 管理，首次启用导入旧导出路径；保留原解锁快照及导出格式。DLL 更名为 `ShopCollectionExport.dll`，与入口类型及清单 InternalName 一致。共享依赖由 Omni 提供，不随模块打包。

仍需手动选择角色并导出 `OmniShopOwned.json`。模块启用及登录不会自动导出。浏览器商城脚本 **3.2.1** 已同步 2026-10-29 的官方 813 件商品（新增 9 件），包括新套装完整部件；见 [商城脚本仓库](https://github.com/Angelways/ff14-official-mall-enhancements)。

从旧本地模块切换时，在 Omni 本地模块列表移除旧的 `OmniShopOwned.dll` 路径，避免同名模块重复加载。新 DLL 是 Omni 模块，需要由 Omni 加载。

详细行为、构建命令和验证范围见 [模块说明](Modules/ShopCollectionExport/README.md)。

## 验证

Release 编译 0 警告、0 错误；使用已安装宿主执行 API、本地化、配置迁移、反复启停、文件夹窗口取消等检查；原有导出检查通过，真实库存缓存成功读取 5 个角色。发布脚本核对最终 DLL、清单、程序集版本与 SHA256，并生成可加载目录及 ZIP 备份。

自动化测试使用宿主程序集和模拟服务，未在当前游戏进程中执行在线安装、模块加载和解锁采集；这些交互仍需游戏内验收。

接口遵循 [Omni 官方新版说明](https://github.com/YouShux/OmniToolbox.TreeHouseOnline)。
