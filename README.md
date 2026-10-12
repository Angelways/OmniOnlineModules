# OmniOnlineModules

Angelways 的 Omni Toolbox 在线模块仓库。每个模块拥有独立目录和订阅清单，目录名称使用英文模块类名。

```text
OmniOnlineModules/
  Modules/
    ShopCollectionExport/
      TreeHouseModules.json
      README.md
      Publish.ps1
      1.2.3/
        OmniShopOwned.dll
      Source/
        ...
```

## 商城物品收录导出 1.2.3

在 Omni 的“插件设置 → 在线模块”中添加以下清单地址：

```text
https://raw.githubusercontent.com/Angelways/OmniOnlineModules/main/Modules/ShopCollectionExport/TreeHouseModules.json
```

也可填写 [GitHub 清单页面](https://github.com/Angelways/OmniOnlineModules/blob/main/Modules/ShopCollectionExport/TreeHouseModules.json)。本仓库没有根目录总清单，订阅时使用上面的模块清单地址。

首次安装后，在“树树妙妙屋 → 在线”中启用“商城物品收录导出”。需要 Omni **1.1.6.6** 或更新版本；构建基线为 Omni 1.1.6.6、Dalamud CN API 15、.NET 10。

从本地模块切换时，先在 Omni 本地模块列表停用并移除旧的 `OmniShopOwned.dll` 加载路径，避免同名模块同时加载。现有导出文件夹配置和解锁快照继续使用，不需要删除文件或重新导入配置。

选择角色后，手动点击“导出选中角色的收录 JSON”，在浏览器的 FF14 网站优化脚本或商城收录脚本中导入 `OmniShopOwned.json`。启用模块和登录不会自动导出。

详细行为、数据范围和更新方式见 [模块说明](Modules/ShopCollectionExport/README.md)。

## 验证

首次在线发布采用原 1.2.3 模块，未修改业务代码。发布前 Release 构建通过，0 警告、0 错误，22 项导出检查通过。DLL 和清单使用 SHA256 校验。游戏内在线安装、加载和当前角色解锁采集仍需实际使用验收。

清单格式参考 [Omni 官方在线模块说明](https://github.com/YouShux/OmniToolbox.TreeHouseOnline)；发布组织方式参考 [OmniModules](https://github.com/xiaozhunuonuode/OmniModules)。
