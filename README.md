# GameView Recorder

Unity Editor 内的 GameView 录制工具，用于在 Play Mode 下录制 GameView 的有效显示区域，并输出 MP4/H.264 视频。

## 功能

- 录制 GameView 有效画面区域，自动避开 GameView 工具栏和黑边。
- 输出 MP4/H.264，文件保存在项目 `Assets` 同级目录的 `GameViewRecord` 文件夹。
- 支持 30 FPS 和 60 FPS 录制。
- 支持三档画质：高画质（接近原画）、普通（推荐）、性能优先。
- 支持录制真实系统鼠标光标。
- 支持录制游戏音频，优先采集 FMOD master 输出，失败时回退 Unity Audio。
- 支持开始前倒计时，并在 GameView 上显示录制边框和 REC 标记。
- 支持停止录制后自动打开输出文件夹。

## 特点与优势

- 相比 OBS、系统录屏等第三方工具，直接集成在 Unity Editor 内，不需要额外配置录制窗口或场景源。
- 自动识别 GameView 的有效显示区域，减少手动框选窗口、裁剪黑边和避开工具栏的重复操作。
- 录制状态、倒计时和边框直接显示在 GameView 附近，方便确认录制范围和开始时机。
- 录制参数保存在本地，下次打开窗口会自动恢复常用设置。
- 输出目录固定在项目根目录下的 `GameViewRecord`，方便项目内协作和素材归档。
- 支持 FMOD 项目音频录制，适合游戏内音频不走 Unity Audio 的项目。
- 工具在 Editor 内使用，不需要打包 Player，也不需要切换到外部录屏软件操作。

## 环境要求

- Unity 2020.3 或更高版本。
- Windows Editor。
- 已安装 FFmpeg，并确保 `ffmpeg` 可以在命令行中直接执行。
- 当前版本引用了 `FMODUnity` 程序集；项目需要包含 FMOD Unity 集成。

## 安装方式

### 方式一：Unity Package Manager 安装

在 Unity 中打开 `Window > Package Manager`，点击 `+`，选择 `Add package from git URL...`，输入：

```text
https://github.com/wuchunpeng777/unity-game-view-recorder.git
```

### 方式二：复制到 Assets

将本仓库复制到项目的 `Assets/game-view-recorder` 目录下，等待 Unity 编译完成。

## FFmpeg 安装

Windows 推荐使用 Scoop 或 winget：

```powershell
scoop install ffmpeg
```

或：

```powershell
winget install Gyan.FFmpeg
```

安装后在命令行执行以下命令，确认能看到版本信息：

```powershell
ffmpeg -version
```

## 使用方式

1. 进入 Unity Play Mode。
2. 打开菜单 `Tools > GameView Recorder`。
3. 设置倒计时、录制帧率、画质、鼠标光标和音频选项。
4. 点击 `开始录制`。
5. 录制完成后点击 `停止录制`。
6. 视频会输出到项目根目录下的 `GameViewRecord` 文件夹。

## 画质建议

- `高画质（接近原画）`：适合素材留档、宣传视频录制，文件较大。
- `普通（推荐）`：默认选项，兼顾画质、体积和性能。
- `性能优先`：适合录制时更关注游戏运行帧率的场景，画质会有所下降。

## 注意事项

- 当前录制的是 GameView 屏幕显示区域，不是重新离屏渲染的 Unity 原生分辨率画面。
- GameView 尺寸、位置或显示缩放会影响最终录制分辨率。
- 录制过程中请不要移动、缩放或遮挡 GameView。
- 如果开启音频但没有采集到声音，会保留无音频视频并在 Console 输出警告。
- 如果 FFmpeg 启动失败，录制会直接失败；请检查 FFmpeg 是否安装并加入 `PATH`。
