# 商城物品收录导出

- 英文入口：`ShopCollectionExport`
- 程序集：`OmniShopOwned.dll`
- 作者：Angelways
- 版本：1.2.4
- 最低 Omni 版本：1.1.8.1

1.2.4 修复新版缓存 `HomeWorldID` 字段造成的角色读取异常，兼容旧版 `HomeWorldId`；适配 `HousingStorerooms` 房库关联和离线角色背包缓存，防止实时库存重复计数。真实缓存 4 个角色及 29 项导出检查通过；游戏内加载和解锁采集仍需验收。

## 安装

在 Omni 的在线模块设置中添加：

```text
https://raw.githubusercontent.com/Angelways/OmniOnlineModules/main/Modules/ShopCollectionExport/TreeHouseModules.json
```

首次安装后手动启用。不要同时加载同名本地模块与在线模块；从本地切换时只需移除旧加载路径，保留原配置文件和快照。

## 使用

1. 登录后模块静默读取角色列表，已登录时启用也会读取；可点击“读取 / 刷新角色列表”。
2. 选择需要导出的角色，然后点击“导出选中角色的收录 JSON”。解锁采集仅针对当前登录角色；其他角色需分别登录并手动导出。
3. 在角色列表下设置导出文件夹，可以选择文件夹、输入完整路径或恢复默认位置。默认在 Omni 配置目录的 `Exports` 中。设置自动保存，导出时创建文件夹。
4. 使用“打开导出文件夹”或“复制地址”找到 `OmniShopOwned.json`，再在浏览器的“商城道具收录”独立窗口中导入并选择角色。

模块不会在启用或登录时自动采集解锁或导出，不提供自动导出指令。退出或切换角色时中止未完成的当前采集。

## 数据与兼容

读取 Omni 库存快照；当前角色额外读取背包、兵装库、装备和坐骑、宠物、表情、发型、配饰、陆行鸟装甲及乐谱等永久解锁状态。房屋仓库另列，不混入个人库存；共享房库快照不代表角色独占。雇员上架物品和部队仓库不计入。

其他角色没有采集的解锁状态不能从库存快照推断。快照可能过期；浏览器中的“已获取”表示已解锁或快照持有，不表示商城购买历史。

使用原有 `ShopCollectionExport.Settings.json` 保存导出路径、`ShopCollectionUnlocks.json` 保存角色解锁快照。模块不联网、不修改 `InventoryCache.json`。本仓库不包含个人角色、库存快照、配置文件或导出 JSON。

## 构建与发布

源码在 `Source`。本机默认引用路径为 `D:\XIVLauncherCN`；其他安装环境可指定宿主程序集路径：

```powershell
dotnet build Source/OmniShopOwned.csproj -c Release -p:OmniPath="D:\path\to\Omni" -p:DalamudPath="D:\path\to\Dalamud"
```

`Publish.ps1` 可构建本模块、复制 DLL 到版本目录并同步清单 SHA256；不会自动提交或推送。发布新版时先提高项目的 `Version`，运行该脚本后检查 Git 差异，将新 DLL 与清单一起发布。保留旧版本 DLL，不修改已发布版本的文件。

清单 `File` 相对本清单目录解析，例如 `1.2.4/OmniShopOwned.dll`。Omni 不需要额外下载宿主 DLL。

在线更新只替换程序文件。按 Omni 官方更新流程，刷新已订阅清单可下载更高版本，下次启动 Omni 时应用；卸载默认保留配置，普通刷新不会重新安装已卸载模块。
