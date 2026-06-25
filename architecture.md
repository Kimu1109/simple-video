# Simple-Video 設計書

Version: 1.0

---

# 1. 概要

Simple-Videoは初心者向けのシンプルな動画編集ソフトである。

目的は以下。

* 動画カット
* 倍速編集
* テロップ追加
* BGM追加

のみを簡単に行うこと。

高度な動画編集機能は提供しない。

対象ユーザーは

* YouTube投稿者
* 学校の課題制作
* SNS向け動画作成

とする。

---

# 2. 技術スタック

| 用途      | ライブラリ            |
| ------- | ---------------- |
| UI      | Avalonia         |
| 描画      | SkiaSharp        |
| 映像デコード  | FFMediaToolkit   |
| 映像エンコード | FFMpegCore       |
| 音声再生    | MiniAudio        |
| 音声倍速    | SoundTouch.NET   |
| JSON保存  | System.Text.Json |

---

# 3. ディレクトリ構成

```text
SimpleVideo/

├─ SimpleVideo.UI/
│
├─ SimpleVideo.Core/
│
├─ SimpleVideo.Media/
│
├─ SimpleVideo.Rendering/
│
├─ SimpleVideo.Encoding/
│
├─ SimpleVideo.Audio/
│
├─ SimpleVideo.Infrastructure/
│
├─ docs/
│
└─ tests/
```

---

# 4. レイヤ構造

```text
UI
 ↓

Application

 ↓

Domain

 ↓

Media Services

 ↓

FFMediaToolkit
MiniAudio
FFmpeg
```

UIからFFMediaToolkitを直接呼ばない。

必ず抽象化レイヤを挟む。

---

# 5. タイムライン構造

## 映像トラック

固定1本

配置可能オブジェクト

```text
VideoClip
ImageClip
ColorClip
```

---

## テキストトラック

固定1本

同時配置可能数

```text
1
```

重複不可。

---

## BGMトラック

固定1本

配置可能

```text
AudioClip
```

のみ。

---

# 6. プロジェクトモデル

```csharp
Project
{
    VideoTrack VideoTrack;
    TextTrack TextTrack;
    AudioTrack AudioTrack;

    int Width;
    int Height;

    double Fps;
}
```

---

# 7. 映像クリップ

```csharp
VideoClip
{
    string SourceFile;

    TimeSpan StartTime;
    TimeSpan EndTime;

    TimeSpan TrimStart;
    TimeSpan TrimEnd;

    double PlaybackRate;
}
```

---

# 8. 画像クリップ

```csharp
ImageClip
{
    string SourceFile;

    TimeSpan StartTime;
    TimeSpan EndTime;

    double Scale;
    double Rotation;

    PositionMode PositionMode;

    double X;
    double Y;
}
```

---

# 9. 背景クリップ

```csharp
ColorClip
{
    Color BackgroundColor;

    TimeSpan StartTime;
    TimeSpan EndTime;
}
```

---

# 10. テキストクリップ

```csharp
TextClip
{
    string Text;

    TimeSpan StartTime;
    TimeSpan EndTime;

    string FontFamily;

    double FontSize;

    Color Color;

    TextPositionMode PositionMode;

    double X;
    double Y;
}
```

---

# 11. テキスト位置

```csharp
enum TextPositionMode
{
    Top,
    Center,
    Bottom,
    Custom
}
```

---

# 12. 画像位置

```csharp
enum PositionMode
{
    Top,
    Center,
    Bottom,
    Custom
}
```

---

# 13. メディアライブラリ

## 登録時

### 動画

元動画

↓

プレビュー用動画生成

```text
H.264
All Intra
24fps
480p
```

↓

Media Libraryへ登録

---

### 音声

元音声

↓

48kHz
16bit
Stereo
WAV

↓

保存

---

### 画像

読み込み

↓

縮小版生成

↓

保存

---

# 14. キャッシュ構造

```text
ProjectCache/

├─ video/
├─ audio/
├─ image/
└─ waveform/
```

---

# 15. フレームバッファ

最大

```text
240フレーム
```

保持。

---

## キャッシュ範囲

再生位置

```text
前方 3秒
後方 1秒
```

生成。

---

## 生成優先順位

```text
現在フレーム

↓

前方

↓

後方
```

---

# 16. フレーム生成スレッド

常時起動。

```text
FrameGenerationWorker
```

を利用。

---

状態

```csharp
Idle
Seeking
Playing
```

---

処理フロー

```text
Seek

↓

必要フレーム算出

↓

キュー投入

↓

バックグラウンド生成

↓

FrameBufferへ格納
```

---

# 17. プレビューシステム

音声をマスタークロックとする。

```text
Audio Time

↓

Current Time

↓

Video Render
```

映像は追随。

---

# 18. 音声システム

内部フォーマット

```text
48kHz
16bit
Stereo
PCM
```

統一。

---

## 倍速

```text
SoundTouch.NET
```

利用。

---

## 合成

PCM同士を加算。

```csharp
output = bgm + videoAudio;
```

---

クリッピング防止

```csharp
Math.Clamp(...)
```

を適用。

※ 実際にはリミッタを実装した方が良い。

---

# 19. 映像レンダリング

各フレーム生成時

```text
背景

↓

映像

↓

画像

↓

テキスト
```

順に描画。

---

レンダラー

```csharp
IRenderer
```

```csharp
interface IRenderer
{
    SKBitmap RenderFrame(TimeSpan time);
}
```

---

# 20. メディア抽象化

将来的なFFmpegAutoGen移行を考慮。

---

## 映像デコーダ

```csharp
interface IVideoDecoder
{
    VideoFrame GetFrame(TimeSpan time);
}
```

---

## 音声デコーダ

```csharp
interface IAudioDecoder
{
    AudioBuffer GetAudio(
        TimeSpan start,
        TimeSpan duration);
}
```

---

## エンコーダ

```csharp
interface IVideoExporter
{
    Task ExportAsync(
        ExportOptions options);
}
```

---

# 21. 出力

出力形式

```text
mp4
```

固定。

---

## 映像

優先順位

```text
NVENC

↓

QuickSync

↓

AMF

↓

x264

↓

x265
```

※ x264/x265は「ハードウェア利用不可時」のフォールバック。

---

## 音声

```text
AAC
```

固定。

---

# 22. 保存形式

拡張子

```text
.svp
```

(Simple Video Project)

---

内部形式

```json
{
  "version":1,
  "project":{}
}
```

---

# 23. 非機能要件

## メモリ使用量

プレビュー時

```text
1GB以内
```

目標。

---

## 起動時間

```text
3秒以内
```

---

## UI応答時間

```text
100ms以内
```

---

# 24. 今後の拡張予定

対象外だが将来追加可能。

* トランジション
* フェードイン
* フェードアウト
* 複数BGM
* 複数テキスト
* GIF
* WebM出力
* FFmpegAutoGen移行

---