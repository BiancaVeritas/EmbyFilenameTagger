# 贡献指南

欢迎提交 Issue 和 Pull Request。

## 提交前

请确保以下命令通过：

```bash
# 编译
dotnet build EmbyFilenameTagger/EmbyFilenameTagger.csproj -c Release

# 单元测试（应全部通过）
dotnet run --project Tests/Tests.csproj

# 加载性校验
dotnet run --project Verify/Verify.csproj -- EmbyFilenameTagger/bin/Release/EmbyFilenameTagger.dll
```

`Verify` 会检查若干历史踩坑点（配置页 HTML 不得含 `<script>`、根元素需含 `class="view"`、
页名须全小写、脚本须为 AMD 模块等）。这些是硬性约束，请勿绕过。

## 代码约定

- `Core/` 下不引用 Emby 程序集的文件应当保持零 Emby 依赖，便于单独测试
- 新增逻辑请补充对应单元测试
- 中文注释，说明「为什么」而非「做了什么」
- 修改 `Web/config.js` 后务必同步提升 `csproj` 里的版本号

## 关于版本号

Emby 会用**插件版本号**参与配置页资源的 ETag 计算。
若只改前端资源而不升版本号，浏览器与 Service Worker 会继续使用旧缓存，
表现为「改了代码但界面没变」。

## 提交信息

建议使用简洁的祈使句，例如：

- `Fix producer ordering when actor already exists`
- `Add regex mode toggle to rule editor`
