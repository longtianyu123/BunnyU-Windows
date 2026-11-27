# 第三方组件

- LibVLCSharp / LibVLCSharp.WPF 3.10.2，VideoLAN。NuGet 元数据声明 LGPL-2.1-or-later。
- VideoLAN.LibVLC.Windows 3.0.24，VideoLAN。保留发布包内的组件与插件结构；各组件的版权和许可由其上游文件说明。
- .NET 10 Windows Desktop Runtime，Microsoft / .NET contributors。

本项目采用动态链接，没有修改 LibVLCSharp 或 LibVLC；发布目录保留可独立替换的动态库与插件。LGPL 文本及包元数据位于 licenses 目录。对应组件源码、版权和构建信息可通过下列上游项目取得；其中 WPF 包仅为历史版本引用，当前界面使用 WinForms。

上游项目与包信息：

```text
https://code.videolan.org/videolan/LibVLCSharp
https://www.videolan.org/vlc/libvlc.html
https://www.nuget.org/packages/LibVLCSharp.WPF/3.10.2
https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.24
https://github.com/dotnet/runtime
https://code.videolan.org/videolan/vlc/-/tree/3.0.24
https://code.videolan.org/videolan/LibVLCSharp/-/tree/3.10.2
```

界面结构参考本机 Maoer-FM 项目（Copyright 2026 yinyukun2023，MIT）。保留其许可于 licenses/Maoer-FM-MIT.txt；网络协议及播放实现仍为本项目 C# 代码。
