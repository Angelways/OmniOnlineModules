# 商城物品收录导出

- 模块入口 / DLL：`ShopCollectionExport` / `ShopCollectionExport.dll`
- 作者：Angelways
- 版本：1.3.0；API：1；最低 Omni：1.1.8.2
- 构建基线：Omni 1.1.8.7、Dalamud CN API 15、.NET 10

## 1.3.0 更新

迁移新版模块基类、API 声明、生命周期与本地化。事件在 `FeatureLifetime` 中登记解绑，文件夹对话框在停用时取消并等待释放；重复启停不累积回调。用户界面、状态和文件夹窗口标题均从模块词条读取，繁体由 Omni 生成。

使用内嵌 `Settings` 与宿主绑定的 `config`。首次启用从原 `ShopCollectionExport.Settings.json` 读取导出路径，保留旧文件作迁移备份；后续保存及恢复默认交给宿主管理，不重新读取旧值覆盖新设置。`ShopCollectionUnlocks.json` 和导出的 `OmniShopOwned.json` 格式不变。

DLL 更名为 `ShopCollectionExport.dll`，在线模块标识仍是 `ShopCollectionExport`。每个文件仅包含一个可实例化模块。旧版本 DLL 保留，不覆盖已发布版本。

## 安装与使用

在 Omni 在线模块设置中添加 `https://github.com/Angelways/OmniOnlineModules`，首次安装后手动启用。原模块 raw 清单地址继续维护，无需额外添加备用链接。若旧订阅记录不兼容，按宿主提示重新建立仓库记录，保留现有配置。

1. 登录后静默读取角色列表，也可点击“读取 / 刷新角色列表”。
2. 选择角色，点击“导出选中角色的收录 JSON”。只采集当前登录角色的解锁；其他角色需分别登录并手动导出。
3. 选择或输入完整导出文件夹地址；默认在 Omni 配置目录的 `Exports` 中。手动导出时创建目录。
4. 在浏览器商城收录面板导入 `OmniShopOwned.json` 并选择角色。商城合集脚本 3.2.1 已更新为 813 件官方商品。

模块启用及登录不会自动导出。切换角色、退出登录或停用会中止未完成采集。从旧本地版本切换时移除 Omni 中旧 `OmniShopOwned.dll` 路径，避免同名模块重复加载；新 DLL 仍由 Omni 本地/在线模块系统加载。

## 数据范围

读取 Omni 库存快照；当前角色额外读取背包、兵装库、装备以及坐骑、宠物、表情、发型、配饰、陆行鸟装甲和乐谱等永久解锁。遍历当前客户端 Item 表，因此新品无需硬编码物品白名单。

房屋仓库另列，不混入个人库存；共享房库不表示角色独占。雇员上架物品与部队仓库不计入。未登录采集的其他角色解锁不能由库存推断；缓存可能过期，收录也不表示商城购买历史。

模块不联网，不修改 `InventoryCache.json`。仓库及发布包不包含个人角色、库存、配置或导出文件。

## 构建与发布

```powershell
./Publish.ps1
# 其他安装路径：
./Publish.ps1 -OmniPath 'D:\path\to\Omni' -DalamudPath 'D:\path\to\Dalamud'
```

脚本编译、运行契约/生命周期与导出检查、复制版本 DLL、更新根清单和原模块清单，并在 `E:\WorkPlace\Releases\OmniOnlineModules` 生成可加载目录及 ZIP 备份。自定义位置可传 `-ReleaseRoot`。脚本不执行 Git 操作；发布前递增项目版本，核对变更后提交 DLL 与清单。最终清单使用 `RepositoryURL`、`ApiVersion`、`Localization.Source` 等新版必填字段，`File` 相对仓库根目录。

发布目录内直接加载 `ShopCollectionExport.dll`；配套 `TreeHouseModules.json` 和相同相对路径的 DLL 用于清单校验与备份。Common、TreeTools、OmenTools、Dalamud 和 Lumina 均由宿主提供，按官方要求不打包共享依赖。

验证覆盖宿主 API 契约、完整简繁词库、旧路径迁移、宿主配置保存恢复、重复启停及原生对话框取消，以及原有 29 项导出检查（含真实库存缓存时）。自动化服务为模拟对象；当前游戏进程内的在线安装、实际加载和解锁采集尚需验收。
