# ⚡ TouchpadTurbo (触摸板极速倍增调节器)

[![GitHub Release](https://img.shields.io/github/v/release/NoelJudeNoel/TouchpadTurbo?color=brightgreen)](https://github.com/NoelJudeNoel/TouchpadTurbo/releases/latest)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg)](https://microsoft.com)
[![Runtime](https://img.shields.io/badge/.NET-4.5%2B%20(Built--in)-green.svg)](https://dotnet.microsoft.com)
[![Dependencies](https://img.shields.io/badge/Dependencies-Zero-brightgreen.svg)]()
[![License](https://img.shields.io/badge/License-MIT-orange.svg)](LICENSE)

专为 **Windows 蓝牙触摸板** 与 **2.5K / 4K 高分屏 / 多显示器** 打造的轻量级硬件级光标动力引擎。彻底解决第三方触摸板（如 Surface 键盘保护套）物理采样率（DPI）低、光标移动慢如泥潭、跨屏来回搓板累死人的核心痛点。

<p align="center">
  <img src="docs/preview.png" alt="TouchpadTurbo 调节面板" width="450">
</p>

<p align="center">
  <a href="https://github.com/NoelJudeNoel/TouchpadTurbo/releases/latest/download/TouchpadTurbo.exe">
    <img src="https://img.shields.io/badge/Download-TouchpadTurbo.exe%20(Green%20Binary)-00B4D8?style=for-the-badge&logo=windows&logoColor=white" alt="Download EXE">
  </a>
  &nbsp;&nbsp;
  <a href="https://github.com/NoelJudeNoel/TouchpadTurbo/releases/latest/download/TouchpadTurbo-v1.0.0-windows.zip">
    <img src="https://img.shields.io/badge/Download-Complete%20Zip%20Package-2EC4B6?style=for-the-badge&logo=github&logoColor=white" alt="Download Zip">
  </a>
</p>

---

## 🌟 核心特性 (Features)

* 🚀 **1.0x ~ 6.0x 物理动力倍增**：实时拦截底层指针位移增量，将微小的手指推移放大为充沛的像素移动距离。
* ⚡ **快速甩指额外爆发（Flick Acceleration）**：独创三段式非线性贝塞尔爆发算法，随手轻轻一甩光标即可飞跃整块 2.5K 屏幕或跨双屏。
* 🎯 **微操智能平抑（Precision Hold）**：在极慢速微移时自动平抑降速至 1.3x 纯净档，瞄准小按钮、框选单字稳如磐石，彻底消除高速抖动。
* 🖥️ **175% / 200% 高分屏矢量暗色 UI**：原生针对 Surface Pro 等高 DPI 缩放优化，无模糊、无重叠、无换行截断。
* 🎛️ **系统托盘与全局快捷键**：
  * 随时按下 **`Ctrl + Alt + End`** 瞬间切换开启/暂停，无缝对比手感。
  * 最小化静默常驻系统托盘，右键一键切换预设档位（2.0x, 2.5x, 2.8x, 3.5x, 5.0x）。
* 🔒 **单实例互斥保护（Single-Instance Mutex）**：底层全局互斥锁，彻底杜绝开机或误点导致多开实例的问题。
* 🪶 **极致轻量，零第三方依赖**：
  * 单文件绿色运行（可执行文件仅 ~11KB）。
  * 内存占用仅 ~15MB，CPU 占用率日常 **0.00%**。
  * 纯原生 Win32 P/Invoke 驱动，无需安装 Visual Studio，任何 Windows 自带的 `csc.exe` 1 秒内一键编译完成。

---

## 🛠️ 技术原理 (Architecture)

### 为什么 Windows 自带设置对蓝牙触摸板无效？
1. **双管道隔离机制**：Windows 10 对传统鼠标（`Control Panel\Mouse`）与高精度触摸板（PTP）采用了完全隔离的输入管道。在控制面板里拉满鼠标速度和提高精确度，触摸板完全不继承。
2. **硬件物理采样率受限**：大多数便携蓝牙键盘（如 Inateck、各类副厂键盘）物理触摸板面积狭小（约 2 英寸），硬件传感器仅 400~600 DPI。面对 2560×1440 等高分屏，每次物理扫描返回的脉冲计数天然过少。
3. **TouchpadTurbo 的解决方式**：
   * 挂载在 Windows 用户会话的 `WH_MOUSE_LL` 核心消息队列中。
   * 通过 `MAGIC_ID` 专属回流签名防抖，在硬件输入到达系统之前，瞬时将物理相对增量乘以智能动态倍数并注入，既绕开了硬件 DPI 缺陷，又保留了系统的原生手感。

---

## 🚀 编译与运行 (Build & Run)

本项目无需安装臃肿的 Visual Studio 或 .NET SDK，利用 Windows 10/11 系统自带的 .NET Framework 编译器即可秒级完成编译：

```cmd
# 运行仓库根目录的 build.bat 即可一键生成二进制文件：
build.bat
```

编译出的成品位于 `bin\TouchpadTurbo.exe`，双击即可直接使用。

---

## ⌨️ 快捷键说明 (Shortcuts)

| 快捷键 | 功能说明 |
| :--- | :--- |
| **`Ctrl + Alt + End`** | 一键暂停 / 恢复倍增加速 |
| **双击托盘图标** | 呼出 / 隐藏调节面板 |
| **右键托盘图标** | 快速切换常用倍速档位 (2.0x ~ 5.0x) |

---

## 📄 开源许可 (License)

本项目基于 [MIT 许可证](LICENSE) 开源。
