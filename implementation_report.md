# Simple-Video 実装報告書 (コアモジュール)

設計書 [architecture.md](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/architecture.md) に基づき、ソリューション構成のマルチプロジェクト化および各主要モジュールのコア実装を完了しました。全体のビルドは警告・エラーなしで正常に通ることを確認しています。

---

## 1. ディレクトリとソリューション構成

ルートディレクトリにソリューションファイル [SimpleVideo.sln](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.sln) を作成し、役割ごとにプロジェクトを分割しました。

```text
SimpleVideo/
├─ SimpleVideo.sln (ソリューション)
├─ SimpleVideo.UI/             (UI - Avalonia Desktop)
├─ SimpleVideo.Core/           (ドメインモデル、インターフェース)
├─ SimpleVideo.Media/          (映像デコード、WAVデコード)
├─ SimpleVideo.Rendering/      (SkiaSharp レンダリング)
├─ SimpleVideo.Encoding/       (FFmpeg エクスポート、インポート用プリエンコード)
├─ SimpleVideo.Audio/          (MiniAudio再生、SoundTouch倍速ミキシング)
├─ SimpleVideo.Infrastructure/ (プロジェクト保存・読込、キャッシュ制御)
└─ tests/
   └─ SimpleVideo.Tests/       (単体テスト)
```

---

## 2. モジュール別実装内容

### 📂 SimpleVideo.Core
タイムライン上のモデルおよびメディア操作のインターフェースを定義。
- [Project.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Core/Models/Project.cs): 解像度、Fps、および各トラックの定義。
- [IClip.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Core/Models/IClip.cs): Polymorphicシリアライズ用アノテーション付きクリップインターフェース。
- [IVideoDecoder.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Core/Media/IVideoDecoder.cs), [IAudioDecoder.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Core/Media/IAudioDecoder.cs), [IVideoExporter.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Core/Media/IVideoExporter.cs): 外部ライブラリに依存しないメディア抽象化レイヤ。

### 📂 SimpleVideo.Media
- [FFMediaToolkitVideoDecoder.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Media/FFMediaToolkitVideoDecoder.cs): `FFMediaToolkit` (4.8.1) を使った映像デコーダ。
  - CPU-GPU間のポインタ処理を排除し、`SKBitmap.GetPixelSpan()` による安全かつ高速なメモリコピーを実現。
  - Linux用のFFmpeg共有ライブラリパスの自動検出ロジックを実装。
- [WavAudioDecoder.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Media/WavAudioDecoder.cs): プリエンコードされた非圧縮 WAV（48kHz, 16bit, Stereo）から、部分PCMサンプル範囲をロードする軽量で堅牢なマネージドデコーダ。

### 📂 SimpleVideo.Rendering
- [SkiaRenderer.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Rendering/SkiaRenderer.cs): SkiaSharp (3.x) を使用したフレーム描画エンジン。
  - レイヤ順（背景 ➔ 映像 ➔ 画像 ➔ テキスト）の描画ロジック。
  - 画像配置（Top, Center, Bottom, Custom）および行列変換による **拡大縮小（Scale）** と **回転（Rotation）**。
  - 字幕用テキスト描画（フォント選択、サイズ、色、配置位置の決定）。
  - SkiaSharp 3.x の最新 API (`SKFont`, `DrawText` オーバーロード) に完全追従。

### 📂 SimpleVideo.Audio
- [AudioEngine.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Audio/AudioEngine.cs): 音声のミキシングおよび再生制御。
  - `Miniaudio-CS` を使用して `ma_device` に PInvoke 経由で関数ポインタコールバックを登録。
  - 再生スレッドでのディスクI/Oを完全に回避するため、キャッシュWAVから全PCMサンプルをメモリに先行ロードする設計。
  - `SoundTouch.Net` (2.3.2) を用いたピッチ変更なしの**テンポ倍速処理**。
  - 複数トラック（映像音声 + BGM）の重ね合わせミキシングおよび `Math.Clamp` によるクリッピングノイズ防止。

### 📂 SimpleVideo.Encoding
- [MediaEncoderService.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Encoding/MediaEncoderService.cs): FFmpeg を用いたトランスコードおよび動画エクスポート。
  - プレビュー用動画（H.264 All Intra, 24fps, 480p）の生成。
  - プレビュー用音声（48kHz, 16bit, Stereo WAV）の生成。
  - プレビュー用画像（アスペクト比維持、最大518400画素以下）の生成。
  - レンダラー出力（`SKBitmap` ストリーム）から動画一時ファイルを生成し、オフラインミキシング音声とマージする **動画エクスポート機能**。
  - ハードウェアエンコーダ自動選択フォールバック（`NVENC` ➔ `QSV` ➔ `AMF` ➔ `libx264`）。

### 📂 SimpleVideo.Infrastructure
- [ProjectSerializer.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Infrastructure/ProjectSerializer.cs): `.svp` ファイルの保存・読み込み。
  - Polymorphic型（Video/Image/ColorClip）のデシリアライズに対応。
- [ProjectCacheManager.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.Infrastructure/ProjectCacheManager.cs): `ProjectCache/` 以下のキャッシュディレクトリ構造の初期化およびアセットのMD5ハッシュ変換（プリエンコード）自動調整。

---

## 3. 次のステップ: UI統合の設計

Avalonia を使用した UI 統合を開始しました。

1. **メインウィンドウ設計**:
   - 暗い高級感のある Fluent テーマを適用。
   - 上部：メニューバーおよびプロジェクト設定（解像度・FPS）。
   - 左部：メディアライブラリ（インポートされたアセット一覧）。
   - 右部：動画プレビュープレイヤー（`SKCanvas` や `WriteableBitmap` を用いた Skia 描画）。
   - 下部：タイムライン（映像・テキスト・BGMの3つのトラック、トリミング・ドラッグ編集用のスライダー）。
2. **プレビュープレイヤーの動作**:
   - `AudioEngine` からの再生時刻に連動して、バックグラウンドスレッドで `SkiaRenderer` から次のフレームを生成し、画面上に描画。
   - 前3秒・後1秒のキャッシュ範囲を管理する `FrameBuffer` の実装。

---

## 4. UI統合の進捗

### 実装済み

- [MainWindow.axaml](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/Views/MainWindow.axaml): メイン編集画面を実装。
  - 上部ツールバー: 新規作成、プロジェクト読み込み、保存、解像度/FPS設定。
  - 左ペイン: 動画・画像・音声のメディアライブラリ。
  - 中央: プレビュー領域と再生位置スライダー。
  - 右ペイン: 選択メディアのタイムライン追加、テロップ追加、背景追加。
  - 下部: Video/Text/BGM クリップを一覧表示するタイムライン。
- [MainWindowViewModel.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/ViewModels/MainWindowViewModel.cs): UI 操作用 ViewModel を実装。
  - `Project` モデルへの解像度/FPS反映。
  - メディア取り込み状態の保持。
  - `VideoClip` / `ImageClip` / `AudioClip` / `TextClip` / `ColorClip` のタイムライン追加。
  - `.svp` プロジェクト保存・読み込み。
  - タイムライン表示モデルの再計算。
- [MainWindow.axaml.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/Views/MainWindow.axaml.cs): Avalonia のファイルピッカーを接続。
  - 動画、画像、音声ファイルの複数選択インポート。
  - `.svp` の保存・読み込み。
- [App.axaml](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/App.axaml): ダークテーマを明示設定。

### 検証

```text
dotnet build SimpleVideo.slnx -m:1 -nr:false
Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/SimpleVideo.Tests/SimpleVideo.Tests.csproj -m:1 -nr:false
Passed. Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

※ sandbox 環境では MSBuild/vstest のローカル socket 作成が制限されるため、`-m:1 -nr:false` を付けてビルドし、テストは権限昇格で実行しました。

### 次のステップ

1. Export ボタンを `MediaEncoderService` に接続する。
2. クリップのトリミング、開始位置、再生速度を編集する Inspector を追加する。
3. タイムライン上でクリップを移動・選択できる編集操作を追加する。

---

## 5. プレビューシステムの進捗

### 実装済み

- [FrameBuffer.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/Preview/FrameBuffer.cs): 最大 240 フレームを保持するプレビュー用フレームバッファを追加。
- [FrameGenerationWorker.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/Preview/FrameGenerationWorker.cs): バックグラウンドでフレーム生成する worker を追加。
  - 現在フレームを最優先で生成。
  - 現在位置から前方 3 秒、後方 1 秒の不足フレームを生成。
  - 新しい seek 要求が来た場合、古いプリフェッチを中断。
- [AvaloniaBitmapConverter.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/Preview/AvaloniaBitmapConverter.cs): `SKBitmap` を Avalonia `WriteableBitmap` に変換するヘルパーを追加。
- [SynchronizedRenderer.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/Preview/SynchronizedRenderer.cs): UI 操作とバックグラウンド描画の同時アクセスを防ぐ同期ラッパーを追加。
- [MainWindowViewModel.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/ViewModels/MainWindowViewModel.cs):
  - `SkiaRenderer` をプレビューに接続。
  - `PreviewImage` を `Image.Source` にバインド。
  - スライダー移動時に該当フレームを要求。
  - UI タイマーによる Play/Pause プレビューを追加。
  - Window クローズ時に worker / renderer / buffer を破棄。

### 検証

```text
dotnet build SimpleVideo.slnx -m:1 -nr:false
Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/SimpleVideo.Tests/SimpleVideo.Tests.csproj -m:1 -nr:false
Passed. Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

---

## 6. 音声同期とキャッシュ接続の進捗

### 実装済み

- [MainWindowViewModel.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/ViewModels/MainWindowViewModel.cs):
  - `AudioEngine` を UI の Play/Pause に接続。
  - 音声・動画クリップが存在する場合、`AudioEngine.CurrentTime` をマスタークロックとしてプレビュー時刻を更新。
  - 音声デバイスを使用できない環境では、既存の UI タイマー再生へフォールバック。
  - スライダー操作中は `AudioEngine.Seek()` を呼び、ユーザー seek と音声クロック更新を分離。
  - タイムライン変更・プロジェクト読込時に AudioEngine を再構築。
- [MainWindow.axaml.cs](file:///home/shono/%E3%83%89%E3%82%AD%E3%83%A5%E3%83%A1%E3%83%B3%E3%83%88/simple-video/SimpleVideo.UI/Views/MainWindow.axaml.cs):
  - メディアインポートを async 化。
- `ProjectCacheManager` 接続:
  - 動画インポート時にプレビュー動画と音声 WAV キャッシュを生成。
  - 音声インポート時に 48kHz/16bit/Stereo WAV キャッシュを生成。
  - 画像インポート時に縮小済み PNG キャッシュを生成。
  - 動画・音声クリップは元ファイルパスを保持し、AudioEngine の MD5 キャッシュ規則と一致させる。

### 検証

```text
dotnet build SimpleVideo.slnx -m:1 -nr:false
Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/SimpleVideo.Tests/SimpleVideo.Tests.csproj -m:1 -nr:false
Passed. Failed: 0, Passed: 1, Skipped: 0, Total: 1
```
