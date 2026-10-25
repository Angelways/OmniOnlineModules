# OmniOnlineModules

Angelways 的 Omni Toolbox 在线模块仓库。每个模块拥有独立目录和订阅清单，目录名称使用英文模块类名。

```text
OmniOnlineModules/
  Modules/
    ShopCollectionExport/
      TreeHouseModules.json
      README.md
      Publish.ps1
      1.2.4/
        OmniShopOwned.dll
      Source/
        ...
```

## 商城物品收录导出 1.2.4

在 Omni 的“插件设置 → 在线模块”中添加以下清单地址：

```text
https://raw.githubusercontent.com/Angelways/OmniOnlineModules/main/Modules/ShopCollectionExport/TreeHouseModules.json
```

也可填写 [GitHub 清单页面](https://github.com/Angelways/OmniOnlineModules/blob/main/Modules/ShopCollectionExport/TreeHouseModules.json)。本仓库没有根目录总清单，订阅时使用上面的模块清单地址。

首次安装后，在“树树妙妙屋 → 在线”中启用“商城物品收录导出”。需要 Omni **1.1.8.1** 或更新版本；构建基线为 Omni 1.1.8.1、Dalamud CN API 15、.NET 10。

1.2.4 修复新版库存缓存将 `HomeWorldId` 改为 `HomeWorldID` 后角色列表读取失败的问题，同时兼容旧版字段。支持新版房库关联和离线角色背包缓存，避免当前角色库存重复计数。已订阅用户可在 Omni 在线模块页面刷新更新，下次启动 Omni 时应用。

从本地模块切换时，先在 Omni 本地模块列表停用并移除旧的 `OmniShopOwned.dll` 加载路径，避免同名模块同时加载。现有导出文件夹配置和解锁快照继续使用，不需要删除文件或重新导入配置。

选择角色后，手动点击“导出选中角色的收录 JSON”，在浏览器的 FF14 网站优化脚本或商城收录脚本中导入 `OmniShopOwned.json`。启用模块和登录不会自动导出。

详细行为、数据范围和更新方式见 [模块说明](Modules/ShopCollectionExport/README.md)。

## 验证

1.2.4 Release 构建通过，0 警告、0 错误，29 项导出检查通过，当前真实缓存成功读取 4 个角色。DLL 和清单使用 SHA256 校验。游戏内在线更新、加载和当前角色解锁采集仍需实际使用验收。

清单格式参考 [Omni 官方在线模块说明](https://github.com/YouShux/OmniToolbox.TreeHouseOnline)；发布组织方式参考 [OmniModules](https://github.com/xiaozhunuonuode/OmniModules)。
