# Changelog

## [Unreleased] - 2026-08-23

### Changed

- 建立專案生命週期四檔與 portable manifest。
- 記錄區網雙向遠端控制的 MVP、模組邊界與安全限制。
- 初始化獨立本機 Git `main` 與 .NET 10 solution。
- 實作同一 WPF 應用程式的被控端／控制端雙角色切換。
- 實作私人 IPv4 限制、TLS 1.2/1.3、session-only 憑證、六位 SAS 配對碼與被控端明確核准。
- 實作版本化訊息 framing、主要螢幕 GDI/JPEG 相容傳輸、滑鼠／鍵盤 `SendInput` 與立即斷線。
- 加入 self-contained win-x64 發包與 Windows 10／11 雙機證據腳本。
- 升級線上協定為 v2，加入初始畫質協商、連線中畫質切換與被控端套用回覆；舊協定會被明確拒絕。
- 把 launcher 與遠端桌面整合到同一個 WPF 視窗，加入傳統選單與工具列。
- 加入視窗化、最大化、F11 無邊框全螢幕、工具列隱藏／頂端叫回與斷線返回 launcher。
- 加入流暢 720p30、平衡 900p24、畫質 1080p15 三種 canonical GDI/JPEG 設定與 session 內熱切換。
- 加入等比例影像座標映射，黑邊區域不注入遠端滑鼠事件。

### Validation

- 初始化檔案已使用 UTF-8 回讀。
- Portable manifest 已通過目前本機 Full Core validator 的結構檢查。
- Runtime skills 與本機發行來源的 SHA-256 一致；發行來源變更尚未進入 manifest 所指的不可變 commit，因此權威版本驗證為 `PARTIAL`。
- 初始化前確認此資料夾沒有獨立 Git repository；之後依已確認工作單建立本機 `main`。
- `.NET SDK 10.0.400` 已安裝；Debug 與 Release 均為 0 警告、0 錯誤。
- `dotnet test`：22/22 通過，包含真實 TLS loopback、配對碼一致、拒絕前不擷取、核准後畫面／輸入、三種畫質參數、熱切換與黑邊座標。
- WPF GUI 雙實例測試：本機 TLS/SAS 核准後在同一視窗進入遠端工作區；視窗化、最大化、F11 全螢幕、工具列隱藏／叫回、流暢／平衡／畫質熱切換及斷線返回皆通過。
- `dotnet format --verify-no-changes --no-restore`：通過。
- self-contained win-x64 測試資料夾與 ZIP 已依 source revision `11ea76e28ce7d28064260ff3f49d9c1dfcd9f98a` 重建；ZIP 79,784,941 bytes，SHA-256 `5C971B6A750F652A6D22B99C887A3A65066000450FE6239E10701353CFD5A216`。
- `LanRemote.App.exe` SHA-256：`32CE7CD65AA2396DF246F38678BDB26E9D191C526DCBCE07E95FAEA62CE4020E`。
- 直接啟動 `artifacts/LanRemote-win-x64/LanRemote.App.exe` 的 self-contained launcher 冒煙測試：通過。
- NuGet vulnerability audit：五個專案目前來源未回報已知弱點套件。

### Delivery

- GitHub：`LOCAL_ONLY/NOT_CONFIGURED`
- ReadyGate（`WO-LANREMOTE-UX-PROFILES-V1`）：`CONDITIONAL` 本機測試候選；停止正式發布，等待兩臺都升級 v2 後的 Windows 10／11 雙向實機證據。
- 原 MVP 的 WGC + Media Foundation H.264 效能目標仍未完成，保留為後續獨立工作項目。
