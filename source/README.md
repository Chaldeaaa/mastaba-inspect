# 源码

[English](README.en.md)

使用 PowerShell 7，在本目录执行 `./build.ps1 -BepInExDir '你的配置/BepInEx'`。构建入口是 `MastabaInspect.slnx`，引用该配置中的 .NET 6、BepInEx 和游戏互操作程序集；构建后运行持刀、持枪动作检查。

游戏和 BepInEx 的程序集由本机提供。输出位于 `bin/`。在仓库根目录执行 `./tools/package.ps1 -Rebuilt`，使用新编译的 DLL 生成安装包。
