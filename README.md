# EmbyFilenameTagger

按文件名里的关键词，自动为 Emby 媒体条目写入**标签 / 类型 / 演员 / 制片**，每条规则都可以限定只在指定媒体库生效。

- 目标服务端：Emby Server **4.10.x**
- SDK：`MediaBrowser.Server.Core` 4.10.0.24-beta2（官方 NuGet）
- 产物：单个 `EmbyFilenameTagger.dll`，不携带任何 SDK 依赖

---

## 功能

| 能力 | 说明 |
| --- | --- |
| 标签 / 类型 / 演员 / 制片 | 四种可写入的目标 |
| 按媒体库限定 | 每条规则可多选生效库，也可排除指定库 |
| 一条规则写多处 | 勾选「同时写入」后，同一关键词可同时写标签+类型+演员+制片 |
| 制片置顶 | 制片人会自动排在演职人员列表最前面 |
| 实时监听 | 新条目入库、或手动刷新条目元数据时立即处理 |
| 计划任务 | 全库扫描，可手动运行或自行配置触发器 |
| 追加模式 | 只增不删，不会破坏已有元数据 |

---

## 安装

1. 从 [Releases](../../releases) 下载 `EmbyFilenameTagger.dll`
2. 复制到 Emby 插件目录：
   - Windows：`%AppData%\Emby-Server\programdata\plugins\`
   - Linux：`/var/lib/emby/plugins/`
   - Docker：容器内 `/config/plugins/`
3. 重启 Emby Server
4. **仪表板 → 插件** 中会出现 `EmbyFilenameTagger`，点「设置」进入配置页

配置保存在 `plugins/configurations/EmbyFilenameTagger.xml`，由 Emby 自动管理。

---

## 规则写法

配置页里每条规则只有一个输入框，语法为：

```
"关键词"="目标值"
```

**引号是必填的。** 多条规则用英文逗号分隔，可以写在同一个输入框里：

```
"2160p"|"4K"|"UHD"="4K", "1080p"="1080P", "国语"|"粤语"="中文配音"
```

### 分隔符

| 符号 | 作用 |
| --- | --- |
| `,` | 分隔不同的映射条目 |
| `=` | 分隔关键词与目标值 |
| `\|` | 同一侧写多个关键词或多个目标值 |

### 目标值留空

写成 `"周星驰"=` 时，命中后**直接写入关键词本身**，不做任何解析：

```
"长离（鸣潮）"=, "琳奈（鸣潮）"=
```

文件名里含 `长离（鸣潮）`，就给该条目写入演员/制片「长离（鸣潮）」。

### 为什么必须加引号

名字里可能含 `=`、`,`、`|` 这类符号。统一加引号后，分隔符与内容不会混淆：

```
"带,逗号的名字"="目标"
```

### 正则模式（进阶）

规则右上角有「使用正则」勾选框。勾选后，同一个输入框改为填写正则表达式，**映射表内容会被忽略**。

适用于「关键词无法穷举」的情况，例如把文件名开头任意中文名当作演员：

```
^(?<actor>[\u4e00-\u9fa5]{2,4})\.
```

命名捕获组（`?<名字>`）的值会自动作为目标值。

---

## 构建

需要 .NET SDK 8：

```bash
dotnet build EmbyFilenameTagger/EmbyFilenameTagger.csproj -c Release
```

产物路径：`EmbyFilenameTagger/bin/Release/EmbyFilenameTagger.dll`

`csproj` 内含 `DeployToEmby` 目标，若检测到 `%AppData%\Emby-Server\programdata\plugins` 会自动复制。禁用：

```bash
dotnet build ... -p:DeployToEmby=false
```

### 测试

```bash
dotnet run --project Tests/Tests.csproj
```

覆盖文件名解析、映射表解析、媒体库范围、多类型写入、制片排序、配置序列化等 **105 个用例**。

### 加载性校验

```bash
dotnet run --project Verify/Verify.csproj -- <dll 路径>
```

检查插件能否被 Emby 正确加载，包括若干历史踩坑点的回归校验：

- HTML 配置页不得含 `<script>`（否则服务端 500）
- 根元素必须含 `class="view"`（否则前端 `getAttribute` 崩溃）
- 配置页名必须全小写
- 脚本必须是 AMD `define` 模块
- 枚举名与数字的互转

---

## 项目结构

```
EmbyFilenameTagger/
├─ EmbyFilenameTagger/
│  ├─ Plugin.cs                     插件入口、页面注册
│  ├─ Configuration.cs              配置模型与读写 API
│  ├─ Core/
│  │  ├─ RuleModel.cs               规则数据结构（无 Emby 依赖）
│  │  ├─ MappingParser.cs           「关键词=目标值」解析（无 Emby 依赖）
│  │  ├─ RuleEngine.cs              文件名解析与匹配（无 Emby 依赖）
│  │  ├─ GenreMapper.cs             中英文类型名归一（无 Emby 依赖）
│  │  ├─ PeopleOrdering.cs          制片置顶排序（无 Emby 依赖）
│  │  └─ ItemTagger.cs              与 Emby 交互：读条目、算计划、写库
│  ├─ Tasks/TaggingTask.cs          计划任务
│  ├─ EntryPoints/ItemChangeMonitor.cs  实时监听
│  └─ Web/                          配置页 HTML 与脚本（嵌入 DLL）
├─ Tests/                           单元测试
├─ Verify/                          加载性校验
└─ FragmentCheck/                   配置页片段结构检查
```

`Core` 下标注「无 Emby 依赖」的文件不引用任何 Emby 程序集，可脱离服务端测试与复用。

---

## 实现要点

几个反编译 Emby 源码后确认的行为，供二次开发参考：

- **HTML 里不能有 `<script>`**
  `WebAppService.Get` 会把配置页交给 `PackageCreator.ModifyHtml`，
  非 index 页面若含脚本标签会走特殊分支并抛空引用，请求返回 500。
  因此 HTML 与脚本拆成两个 `PluginPageInfo`。

- **根元素需匹配 `.view`**
  前端 `viewmanager.normalizeNewView` 用
  `querySelector('.view, div[data-role="page"]')` 定位根视图，
  取不到会在 `view.getAttribute(...)` 处崩溃。

- **人员顺序由 `ListOrder` 决定**
  `SqliteItemRepository.InsertPeople` 按传入 List 的下标写 `ListOrder`，
  读取时 `order by ListOrder, PersonType`。所以制片置顶 = 放在列表首位。

- **构造函数里不能访问 `Configuration`**
  `BasePlugin<T>.ConfigurationFilePath` 依赖 `AssemblyFilePath`，
  后者要等 Emby 调用 `SetAttributes()` 后才赋值。

- **插件 API 需要认证**
  仅带 Cookie 会 401。`ApiClient.ajax` 会自动追加 `X-Emby-Token` 到查询串。

---

## 许可证

[MIT](LICENSE)

## 免责声明

本插件会修改 Emby 媒体库中的标签、类型与人员信息。建议在正式使用前做好媒体库备份。
作者不对因使用本插件造成的数据丢失或损坏负责。
