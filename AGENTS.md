# AGENT.md

## 1. Project Overview
A multi-platform osu! music player and audio server that extracts, parses, and plays audio tracks, backgrounds, hitsounds, and storyboards directly from local osu!(stable/lazer) installations.

- **ターゲットプラットフォーム:** Windows, macOS, Linux (Future: iOS, Android via Server)
- **技術スタック:** .NET 8, C#, Avalonia UI, ManagedBass, ASP.NET Core (Kestrel)

---

## 2. コア制約とプラットフォームルール
- **プラットフォームの分離:**
  - **Windows:** osu!stableとosu!lazer両方サポートします。両方のインスタンス間で重複を排除する必要がある。
  - **macOS / Linux:** Supports osu!lazer ONLY. Never attempt to query Windows Registry or resolve osu!stable directories on non-Windows platforms.
- **Safety First:**
  - OS固有の呼び出しは、常に`System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform`を使用して保護してください。
  - すべてのローカルファイルアクセスは、読み取り専用ロック、ファイルの欠落、読み取り不能なアーカイブを適切に処理する必要がある。

---

## 3. データ構造とディレクトリ構造

### osu!stable (Windows Only)
- **Path Resolution:** Registry `HKCU\Software\osu!` or `%LocalAppData%\osu!`
- **Database:** `osu!.db` and `collection.db` (Binary format parsed via `OsuParsers`)
- **Storage:** Folder-based under `Songs/{SetID} {Artist} - {Title}/`

### osu!lazer (All Platforms)
- **Path Resolution:**
  - Windows: `%AppData%\osu\`
  - macOS: `~/Library/Application Support/osu/`
  - Linux: `~/.local/share/osu/` (or `~/.var/app/sh.ppy.osu/data/osu/` for Flatpak)
- **Database:** `client.realm` (Realm DB format)
- **Storage:** Flat file hash storage under `files/{first_2_chars_of_sha256}/{full_sha256}`. Mapping from original file names (e.g., `audio.mp3`) to hashes is resolved via Realm entities.

---

## 4. コーディング標準とガイドライン

### 言語とランタイム
- **Target:** .NET 8 (C# 12)
- **Nullable Reference Types:** Strictly enabled (`<Nullable>enable</Nullable>`). Avoid null-forgiving operators (`!`) unless proven safe.
- **Dependency Injection:** Use standard `Microsoft.Extensions.DependencyInjection` across modules.
- **Async/Await:** Prefer non-blocking asynchronous I/O (`Task`, `ValueTask`, `IAsyncEnumerable`) for database reads and file searches. Do not block the UI thread.

### アーキテクチャパターン
- **Separation of Concerns:**
  - `Core`: Pure domain models, interfaces, parsing logic, deduplication algorithms (zero UI dependencies).
  - `Audio`: ManagedBass wrappers, DSP, mod pitch/speed handlers (DT/NC/HT/DC).
  - `Server`: Kestrel-based minimal APIs for streaming and remote control.
  - `UI`: Avalonia UI (MVVM with CommunityToolkit.Mvvm).

---

## 5. サードパーディーライブラリとリファレンス実装
- **Audio:** `ManagedBass`, `ManagedBass.Fx`
- **Parsers:** `OsuParsers`, `Realms` (.NET Realm SDK)
- **UI:** `Avalonia`, `Avalonia.Themes.Fluent`
- **Storyboard & Hitsounds:** Milkitic/KeyASIO.Net, MikiraSora/ReOsuStoryboardPlayer

---

## 6. 新しいタスクの実装方法
1. Read existing interfaces and contracts before writing implementations.
2. Ensure new classes are covered by unit tests using xUnit and FluentAssertions.
3. Keep code idiomatic, self-documenting, and robust against corrupt or partially written beatmap files.

## 7. 絶対守るべきこと
- osuディレクトリは編集しない。読み取りだけ。
- トークン節約のため、単純作業で誰がやっても成果が変わらない作業は他のモデルにさせること。

## 8. テスト
```
dotnet restore .\OsuMusicPlayer.sln --configfile .\NuGet.Config
dotnet run --project .\src\OsuMusicPlayer.App\OsuMusicPlayer.App.csproj
```
