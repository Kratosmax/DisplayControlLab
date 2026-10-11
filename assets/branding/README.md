# 应用图标

确认方案：前屏六色色环，后屏五色色谱与黑白分区，蓝色双屏叠层。透明背景，扁平化并保留轻微层次感。

- `displaycontrol.svg`：可编辑矢量源，256×256 坐标。
- `displaycontrol.png`：1024×1024 透明位图，用于共享 UI 与仓库展示。
- `displaycontrol.ico`：16、20、24、32、40、48、64、96、128、256 像素，用于三个 exe、窗口和安装器。

SVG 和 PNG 为同一确认方案。修改 SVG 后应重新渲染 PNG，再生成 ICO；日常构建直接使用已提交资产，无需安装绘图工具。

用 Python / Pillow 从 PNG 重新生成 ICO（仓库根目录）：

```powershell
python -c "from PIL import Image; Image.open('assets/branding/displaycontrol.png').save('assets/branding/displaycontrol.ico', sizes=[(s,s) for s in [16,20,24,32,40,48,64,96,128,256]])"
```

控制器与读取器从共享 UI 程序集的嵌入资源加载图标，不依赖运行目录中的外部图片。安装器编译直接引用同一 ICO，快捷方式和卸载项继承 exe 图标。
