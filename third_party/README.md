# Native BASS dependencies

Windows x64用の公式バイナリを、配布ZIPと実行用DLLに分けて保管しています。

| ファイル | 配布元 | SHA-256 |
| --- | --- | --- |
| `downloads/bass24.zip` | `https://www.un4seen.com/files/bass24.zip` | `3A03EC9A33D0F4F9D167660DA51C8BB1432E8977496995455AB137277D69636E` |
| `downloads/bass_fx24.zip` | `https://www.un4seen.com/files/z/0/bass_fx24.zip` | `A4BAF602865941963127ACB15ED12627D108189F99C2757970432AE7DA0366CD` |
| `native/win-x64/bass.dll` | `bass24.zip/x64/bass.dll` | `FEBB2CF1882D554C3A958280777DA0B69F07DE6E262DF271DE11C56E4A54AFD4` |
| `native/win-x64/bass_fx.dll` | `bass_fx24.zip/x64/bass_fx.dll` | `A6E1847EEF52D882B4137AF514D834C2E220DACEB417C821D1E502FB7A34C84A` |

- BASS: 2.4.18.3
- BASS_FX: 2.4.12.6
- 対象: Windows x64

公式の利用条件は各 `downloads/*.zip` 配布物内の `bass.txt` と `bass_fx.txt` に含まれています。展開済みSDK一式はGit管理対象外です。BASSは非商用利用では無料ですが、商用利用には用途に応じたライセンスが必要です。
