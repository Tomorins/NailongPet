# NailongPet · 奶龙桌宠

独立运行的 Windows 奶龙桌宠，支持透明动画、鼠标拖动、任意角度行走和爆笑音效。

无需安装 Codex、Python 或其他桌宠软件。动画、音效和程序图标全部内置在一个 EXE 中。

<p align="center"><img src="docs/preview.png" width="268" alt="奶龙桌宠预览" /></p>

## 运行

下载仓库中的 [NailongPet.exe](NailongPet.exe)，双击运行即可。系统要求：Windows 10 / 11，.NET Framework 4.x。

## 操作

| 操作 | 效果 |
| --- | --- |
| 左键拖动 | 奶龙跟随鼠标移动，持续播放走路动画，左右朝向随移动切换；松开回到待机 |
| 左键轻点 | 开心爆笑，然后回到待机 |
| 右键 | 打开动作、行走、音效、大小、置顶和退出菜单 |
| 走两步 | 随机选择一个角度自由行走约 6 秒，支持斜向和纵向，碰到屏幕四边会反弹 |
| 托盘图标双击 | 把奶龙找回到鼠标所在屏幕 |

演示动作包括：开心爆笑、完成欢呼、托腮思考、认真敲代码、检查代码、垂头丧气。菜单中的代码相关动作仅为动画演示。

可切换爆笑音效、始终置顶和 75% / 100% / 125% / 150% / 200% 大小。位置和偏好保存在 `%LOCALAPPDATA%\NailongPet\settings.txt`。程序不联网，不添加开机启动。

更多说明见 [使用说明.md](使用说明.md)。

## 从源码构建

使用 Windows PowerShell 运行：

```powershell
.\src\build.ps1
```

构建使用 Windows 自带 .NET Framework C# 编译器，输出 `NailongPet.exe`，无需下载第三方依赖。`src` 中包含源码、应用清单、图标、动画和音频。

## 验证

```powershell
.\NailongPet.exe --self-test
```

自检会短暂展示窗口并生成 `self-test.txt`，覆盖透明窗口、九组动画、菜单、缩放、任意角度移动、四边与角落反弹、拖动时持续动画、掉头和松开复位。自检不播放声音，也不修改个人设置。

## 素材来源

动画和笑声来自 [Tomorins/nailong-codex-pet](https://github.com/Tomorins/nailong-codex-pet)，基于提交 `53c3d40`。本项目新增独立 Windows 运行器，不需要 Codex。

原素材的开心爆笑与完成欢呼共用同一组笑态帧，完成欢呼增加了轻跳效果。程序图标由用户提供的奶龙举勾图片转换而来。

本项目为非官方个人项目。角色及素材的相关权利归原权利人所有，不代表获得角色方或 OpenAI 的授权。保留原仓库的 [权利说明](NOTICE.md) 与 [素材使用说明](ASSET-USAGE.md)。
