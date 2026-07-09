<div align="center">
 
# 灵魂面甲服务端管理器（SSM）

![GitHub last commit](https://img.shields.io/github/last-commit/aghosto/Soulmask-Server-Manager?logo=github)
[![Release](https://img.shields.io/github/v/release/aghosto/Soulmask-Server-Manager)](https://github.com/aghosto/Soulmask-Server-Manager/releases)
[![MIT license](https://img.shields.io/badge/license-MIT-brightgreen.svg)](https://opensource.org/licenses/MIT)
![github stars](https://img.shields.io/github/stars/aghosto/Soulmask-Server-Manager?style=social)

</div>

* [软件信息](#软件信息)
* [说明](#说明)
  * [使用说明](#使用说明)
  * [特别声明](#特别声明)
* [下载与安装](#下载与安装)
  * [下载](#下载)
  * [系统要求](#系统要求)
  * [首次使用](#首次使用)
  * [服务器连接配置编辑](#服务器连接配置编辑)
  * [游戏系数配置编辑](#游戏系数配置编辑)
  * [MOD 管理](#mod-管理)
  * [远程控制(RCON)](#远程控制rcon)
* [致开发者](#致开发者)
* [贡献者](#贡献者)
* [截图](#截图)

# 软件信息

**灵魂面甲服务端管理器（Soulmask Server Manager，简称 SSM）** 是一款专为《灵魂面甲（Soulmask）》游戏服务器管理而开发的全功能桌面工具。集一键开服、服务器连接配置、游戏系数参数编辑、MOD 管理、RCON 远程控制等各项功能于一体，提供图形化界面替代手动修改配置文件的繁琐操作。

本项目基于 V Rising Server Manager 的架构，针对《灵魂面甲》的游戏特性进行了全面重构和适配。

# 说明

## 使用说明

运行软件所需环境：
[.NET Runtime 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

本项目仅供学习交流使用。若您基于本项目进行商业行为，您将承担全部法律责任，作者与其他贡献者不承担任何责任。

This project is for learning and communication purposes only. If you conduct business behavior based on this project, you will bear all legal responsibilities, and the author and other contributors will not bear any responsibility.

## 特别声明

本软件是基于 V Rising Server Manager 架构，针对《灵魂面甲（Soulmask）》游戏服务端管理需求重构的版本。核心功能框架源自原项目，但游戏系数参数、RCON 协议、MOD 管理等功能均针对 Soulmask 进行了全面适配。

由于个人精力有限，如有使用中遇到问题（Bug、翻译遗漏、功能建议等），欢迎提交 Issue。

# 下载与安装

## 下载

下载项目最新发布的版本：
https://github.com/aghosto/Soulmask-Server-Manager/releases/latest

解压到任意目录，双击 `SoulmaskServerManager.exe` 即可启动。

## 系统要求

- **操作系统**：Windows 10 / Windows Server 2016 及以上
- **运行时**：[.NET Runtime 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
- **磁盘空间**：至少 500MB（用于游戏服务端文件）
- **SteamCMD**：软件会自动下载安装

## 首次使用

1. 启动 SSM，点击`添加服务器`，输入服务器名称（仅管理端显示），点击创建。
2. 切换到新建的服务器标签页，点击`更新服务器`，软件将自动通过 SteamCMD 下载 Soulmask 服务端。
3. 更新完成后点击`启动服务器`，等待服务端生成默认配置文件后点击`停止服务器`。
4. 游戏服务端的默认配置文件位于服务端目录下的 `SaveData\Settings` 文件夹中。
5. 返回软件主界面，进入 `设置` -> `主要设置`，勾选`自动加载设置文件`，保存设置。
6. 现在可以点击`服务器连接配置编辑器`和`游戏系数配置编辑器`进行配置了。

> **提示**：首次使用建议先启动一次服务器生成默认配置，再通过 SSM 进行编辑。

## 服务器连接配置编辑

打开`服务器连接配置编辑器`，可配置以下内容：

- **服务器名称** — 显示在 Steam 服务器列表中的名称
- **连接端口 / 查询端口** — 服务器网络通信端口
- **最大玩家数** — 服务器同时在线人数上限
- **地图选择** — 选择游戏地图（云雾之森 / 金色浮沙）
- **保存间隔 / 自动保存数** — 存档策略配置
- **密码 / 管理员密码** — 服务器访问控制
- **RCON 配置** — 远程控制端口和密码
- **公网 IP** — 支持自动获取和手动填写
- **服务器集群** — 支持主/副服务器集群模式
- **MOD 管理** — 输入 MOD ID 列表

配置完成后，点击`文件` -> `保存` 保存配置。

## 游戏系数配置编辑

打开`游戏系数配置编辑器`，可对游戏内几乎所有系数参数进行调整。所有参数按功能分类整理，鼠标悬停在输入框上即可查看详细说明。支持加载预设模板快速应用配置。

### 参数分类

| 分类 | 主要内容 |
|------|---------|
| **通用** | 驯服速度、时间流速、招募上限、负重倍率、休眠/唤醒距离等 |
| **经验与成长** | 各项经验倍率、属性点分配、等级上限、训练场倍率等 |
| **产出与掉落** | 采集/伐木/采矿产出倍率、怪物掉落倍率、武器装备掉落率/耐久等 |
| **建筑** | 建筑腐烂、修建速度、营火/传送门数量上限等 |
| **刷新** | 资源重生速度、禁止刷新半径等 |
| **战斗** | 伤害倍率、PVP 伤害系数、韧性/削体系数、弹反难度等 |
| **消耗** | 食物/水/气息消耗、耐久消耗、燃料消耗、物品腐坏等 |
| **入侵** | 入侵热度、怪物规模/强度/等级、时间窗口、冷却等 |
| **PVP 设置** | PVP 时间窗口（各区域分设）、意识等级上限等 |
| **AI 相关** | AI 级别、族人和动物出战数量等 |
| **战场时间** | 亚服/欧服/美服战场时间独立设置 |
| **全服事件** | 各区域事件时间、触发间隔/概率等 |
| **开关设置** | 飞行禁止、随机入侵、自动备份、建筑限制等各种开关 |

## MOD 管理

SSM 内置了 MOD 管理功能，支持查看和管理已安装的 MOD。也可在服务器连接配置中直接输入 MOD ID（多个用英文逗号分隔）。

## 远程控制(RCON)

1. 在`服务器连接配置编辑器`中启用 RCON，设置密码和端口。
2. 主界面勾选`绑定到IP`，点击`远程控制(RCON)`。
3. 输入 IP、端口和密码，点击连接。
4. 支持发送公告、重启公告等指令。

也可以使用其他 RCON 客户端连接。

# 致开发者

如果你想自行开发或自定义功能，可以克隆源码后在本地编译运行。

所需工具：
- [.NET 6.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
- [Visual Studio 2022](https://visualstudio.microsoft.com/)（推荐）或 VS Code

编译命令：

```bash
dotnet build SSM/SoulmaskServerManager.csproj -c Release
```

### 所用库

- [ModernWpfUI](https://github.com/Kinnara/ModernWpf) — Fluent Design 风格的 WPF 控件库
- [Newtonsoft.Json](https://www.newtonsoft.com/json) — JSON 序列化
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) — 系统托盘图标
- Source RCON Protocol — 游戏 RCON 远程控制协议实现

# 贡献者

<!-- 仅列出本项目维护者，如有贡献者欢迎 PR -->

[![aghosto](https://avatars.githubusercontent.com/u/aghosto?s=64)](https://github.com/aghosto)

**aghosto** — 项目维护与开发

# 截图

> 🖼️ **待上传** — 软件主界面

> 🖼️ **待上传** — 服务器管理界面

> 🖼️ **待上传** — 游戏系数配置编辑器

> 🖼️ **待上传** — 服务器连接配置编辑器

> 🖼️ **待上传** — MOD 管理界面

> 🖼️ **待上传** — RCON 远程控制界面
