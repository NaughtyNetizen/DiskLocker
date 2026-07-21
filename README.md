# DiskLocker

一款 Windows 桌面工具，通过隐藏/恢复盘符来锁定和解锁外部 USB 硬盘，保护你的数据安全。

## 功能特性

- **多磁盘支持** — 可同时保护多块外部 USB 硬盘
- **密码保护** — 基于 BCrypt 算法加密密码，解锁需输入密码
- **自动锁定** — 支持空闲超时、系统锁屏、系统休眠时自动锁定
- **全局热键** — 默认 `Ctrl + Alt + L` 快速呼出解锁对话框
- **设备热插拔监听** — 目标磁盘插入时自动锁定
- **系统托盘** — 后台静默运行，托盘图标实时显示锁定状态
- **开机自启** — 可选随 Windows 启动
- **Win11 风格 UI** — Mica 云母材质 + Fluent Design 设计语言
- **纯 Win32 API** — 不依赖 WMI/CIM，通过 `DeviceIoControl` 直接操作

## 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | .NET 8 (net8.0-windows, win-x64) |
| UI | WPF (XAML) |
| 系统托盘 | WinForms `NotifyIcon` |
| 密码 | BCrypt.Net-Next |
| 磁盘操作 | Win32 API (`DeviceIoControl`, `SetVolumeMountPoint`, `DeleteVolumeMountPoint` 等) |
| 发布 | 单文件自包含可执行程序 |

## 项目结构

```
DiskLocker/
├── Behaviors/          # WPF 附加行为（窗口拖拽、关闭按钮）
├── Models/             # 数据模型（AppSettings、DiskInfo）
├── Native/             # Win32 API 声明和 Mica 辅助
├── Services/           # 业务服务层
│   ├── LockManager.cs         # 核心协调器（锁定/解锁/状态管理）
│   ├── DiskService.cs         # 磁盘操作（枚举/隐藏/恢复盘符）
│   ├── PasswordService.cs     # 密码哈希与验证
│   ├── SettingsService.cs     # 设置持久化（JSON → %APPDATA%）
│   ├── DeviceWatcherService.cs # 设备热插拔监听（轮询）
│   ├── AutoLockService.cs     # 空闲超时自动锁定
│   ├── AutoStartService.cs    # 开机自启（注册表）
│   ├── GlobalHotkeyService.cs # 全局热键注册
│   └── SimpleLogger.cs        # 简易日志
├── Themes/             # WPF 样式资源字典
├── ViewModels/         # 视图模型
├── Views/              # WPF 窗口
│   ├── SettingsWindow   # 主窗口（状态/磁盘管理/设置 三列布局）
│   ├── PasswordDialog   # 密码解锁对话框
│   └── SetupWizard      # 首次运行配置向导
└── App.xaml.cs         # 应用入口（生命周期/托盘/协调）
```

## 工作原理

**锁定（隐藏盘符）：**

1. 通过 `IOCTL_STORAGE_QUERY_PROPERTY` 枚举物理磁盘，获取型号、序列号、总线类型
2. 通过 `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS` 查找目标磁盘上的所有卷及其盘符
3. 使用 `GetVolumeNameForVolumeMountPointW` 获取卷 GUID
4. 使用 `DeleteVolumeMountPointW` 移除挂载点（盘符在资源管理器中消失）
5. 将卷 GUID 和原始盘符保存到 `%APPDATA%\DiskLocker\settings.json`

**解锁（恢复盘符）：**

1. 验证 BCrypt 密码
2. 使用 `SetVolumeMountPointW` 将存储的卷 GUID 挂载回原始盘符
3. 如果存储的 GUID 失效，自动回退：重新枚举目标磁盘上的所有卷并分配可用盘符
4. 通过 `SHChangeNotify` 通知资源管理器刷新

## 构建 & 发布

```bash
# 编译（调试）
dotnet build -c Debug

# 编译（发布）
dotnet build -c Release

# 发布为单文件可执行程序
dotnet publish -c Release -o ./Release
```

输出位于 `Release/DiskLocker.exe`，自包含，无需安装 .NET 运行时。

## 运行要求

- **操作系统**：Windows 10/11 x64
- **权限**：需要**管理员权限**运行（`app.manifest` 已配置 `requireAdministrator`）
- **运行时**：发布版自包含，无需额外安装

## 设置存储

配置文件位于 `%APPDATA%\DiskLocker\settings.json`，包含：

- 目标磁盘列表（型号、序列号、PNP 设备 ID）
- 密码哈希
- 自动锁定设置
- 热键配置
- 隐藏卷 GUID（锁定状态数据）

## 许可证

本项目仅供学习和个人使用。
