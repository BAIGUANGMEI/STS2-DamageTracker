# Damage Tracker

《Slay the Spire 2》伤害统计模组。

这个模组会在战斗界面中显示一个可拖动的伤害统计窗口，用于实时查看每位玩家在当前 Run 中的输出情况。

新版界面采用紧凑卡片式布局，在有限空间内同时展示玩家名称、当前操作角色、角色头像和核心伤害数据。

当前版本：`0.3.0`

## 功能

- 显示每位玩家的总伤害
- 显示当前战斗伤害
- 显示最近一次命中伤害
- 显示玩家当前操作角色的头像与角色标识
- 高亮当前正在操作的玩家卡片
- 窗口支持鼠标拖动
- 优先尝试显示平台玩家名称
- 战斗开始和结束时自动刷新状态
- 支持连续开启多轮新 Run，不会在通关后一局失去伤害显示
- 按角色颜色显示玩家卡片

## 安装方式


将模组所需文件放入游戏根目录的 `mods` 目录中。（若没有则需要新建）

请在 [Releases](https://github.com/BAIGUANGMEI/STS2-DamageTracker/releases) 页面下载最新版本的模组包，解压到 `mods` 目录中。

包含：

- `DamageTracker.dll`
- `DamageTracker.pck`

## 更新说明

### v0.3.0

- 修复连续进行多轮游戏时，下一局可能不再显示伤害的问题
- 修复角色颜色高亮丢失的问题
- 优化 Run 与玩家伤害归属解析，优先使用 STS2 强类型 API

## 使用说明

- 进入战斗后，屏幕上会出现伤害统计窗口
- 按住窗口左键可以拖动位置
- 点击右上角按钮可以折叠或展开面板
- 窗口会显示当前 Run、当前战斗状态、玩家数量和玩家伤害卡片
- 如果能从游戏角色对象读取到纹理，会直接显示角色头像；否则会回退为角色首字母徽标

## 技术实现

- 使用 Harmony 给游戏 Hook 打补丁
- 监听 `BeforeCombatStart`
- 监听 `AfterCombatEnd`
- 监听 `AfterPlayerTurnStart`
- 监听 `AfterDamageGiven`
- 通过 `RunState.Rng.StringSeed` 识别 Run 生命周期
- 优先用强类型 `Player` / `Creature` / `CardModel` 解析伤害归属，反射仅作为兜底
- 使用 Godot `CanvasLayer` 渲染覆盖层 UI

## 环境要求

- Windows
- .NET 9 SDK
- Godot .NET SDK 4.5.1
- 已安装《Slay the Spire 2》

## 项目结构

- `ModEntry.cs`：模组入口与 Hook 补丁注册
- `RunDamageTrackerService.cs`：伤害统计逻辑
- `ReflectionHelpers.cs`：运行时对象解析、Run 标识和玩家归属辅助
- `DamageTrackerOverlay.cs`：战斗界面覆盖层 UI
- `mod_manifest.json`：模组清单
- `project.godot`：Godot 项目配置
- `DamageTracker.csproj`：C# 项目配置

## 使用前配置

构建前需要先修改 `DamageTracker.csproj` 中的游戏目录：

```xml
<Sts2Dir>C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2</Sts2Dir>
```

如果你的游戏不在这个位置，请改成自己的安装目录。

项目会从下面目录引用游戏程序集：

```text
$(Sts2Dir)\data_sts2_windows_x86_64
```

依赖的程序集包括：

- `sts2.dll`
- `0Harmony.dll`
- `Steamworks.NET.dll`

## 构建

在仓库根目录执行：

```powershell
dotnet build
```

构建成功后会生成：

```text
.godot/mono/temp/bin/Debug/DamageTracker.dll
```

项目里还配置了自动复制逻辑，会尝试把生成的 DLL 复制到游戏目录下的：

```text
Slay the Spire 2\mods\DamageTracker\
```


## 常见问题

### 1. 构建失败，提示找不到游戏 DLL

检查 `DamageTracker.csproj` 中的 `Sts2Dir` 是否配置正确。

### 2. 构建失败，提示目标 DLL 被占用

如果游戏正在运行，`DamageTracker.dll` 可能会被 `SlayTheSpire2.exe` 锁定。关闭游戏后重新构建即可。

### 3. 通关一局后再开新局，不显示伤害

请确认你使用的是 `0.3.0` 或更高版本。这个问题已经在新版中修复，原因是旧版对 Run 生命周期的识别不够稳定。

### 4. 玩家名没有按预期显示

当前名称显示优先依赖平台名称接口，其次回退到游戏角色/运行时对象解析。如果游戏更新后对象结构变化，可能需要调整 `ReflectionHelpers.cs` 中的兜底逻辑。

### 5. 角色头像没有显示出来

头像读取依赖运行时角色对象中的 `CharacterSelectIcon` 或 `IconTexture`。如果游戏更新后字段变化，界面会自动回退为角色首字母徽标，但仍可能需要同步调整 `ReflectionHelpers.cs` 中的反射逻辑。

### 6. 玩家颜色没有按角色显示

请确认你使用的是 `0.3.0` 或更高版本。新版已经修复角色颜色映射丢失的问题。

### 7. UI 位置或样式不符合预期

可以在 `DamageTrackerOverlay.cs` 中调整窗口尺寸、配色和布局。

## 开发说明

这个项目是源码学习和模组开发练习用途。上传 GitHub 时建议保留源码、配置文件和清单文件，不要提交构建缓存与本地临时文件。

当前仓库已经通过 `.gitignore` 忽略了以下内容：

- `.godot/`
- `bin/`
- `obj/`
- `.vs/`
- `*.dll`
- `*.pck`

## 许可证

本项目采用 MIT License，详见 [LICENSE](LICENSE)。

说明：该许可证适用于本仓库中的模组源码与自定义资源，不适用于游戏本体资源、游戏商业内容或第三方受限内容。