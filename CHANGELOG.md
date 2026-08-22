# Changelog

## [Unreleased] - 2026-08-22

### Changed

- 建立專案生命週期四檔與 portable manifest。
- 記錄區網雙向遠端控制的 MVP、模組邊界與安全限制。
- 初始化獨立本機 Git `main` 與 .NET 10 solution。
- 實作同一 WPF 應用程式的被控端／控制端雙角色切換。
- 實作私人 IPv4 限制、TLS 1.2/1.3、session-only 憑證、六位 SAS 配對碼與被控端明確核准。
- 實作版本化訊息 framing、主要螢幕 GDI/JPEG 相容傳輸、滑鼠／鍵盤 `SendInput` 與立即斷線。
- 加入 self-contained win-x64 發包與 Windows 10／11 雙機證據腳本。

### Validation

- 初始化檔案已使用 UTF-8 回讀。
- Portable manifest 已通過目前本機 Full Core validator 的結構檢查。
- Runtime skills 與本機發行來源的 SHA-256 一致；發行來源變更尚未進入 manifest 所指的不可變 commit，因此權威版本驗證為 `PARTIAL`。
- 初始化前確認此資料夾沒有獨立 Git repository；之後依已確認工作單建立本機 `main`。
- `.NET SDK 10.0.400` 已安裝；Debug 與 Release 均為 0 警告、0 錯誤。
- `dotnet test`：16/16 通過，包含真實 TLS loopback、配對碼一致、拒絕前不擷取、核准後畫面與輸入傳送。
- WPF UI 冒煙測試：初始畫面、區網位址、被控端啟動／停止與角色交換狀態通過。
- `dotnet format --verify-no-changes --no-restore`：通過。
- self-contained win-x64 測試資料夾與 ZIP 已依 implementation commit `4fd543b1391ba5ae5d8c876387bb14ab0abbb7d9` 重建；ZIP 79,776,123 bytes，SHA-256 `86392CCE768C800E3BE1B87DDCB6961FE2402ECBE78242EA2A56794307223AEA`。
- `LanRemote.App.exe` SHA-256：`BBC3E3FF1CD715BC89744278872E9B0B25B6A27DB477F662D87E9B660D2C98CC`。
- NuGet vulnerability audit：五個專案目前來源未回報已知弱點套件。

### Delivery

- GitHub：`LOCAL_ONLY/NOT_CONFIGURED`
- ReadyGate：`NOT_READY`，尚缺 Windows 10／11 雙向五分鐘實機證據，且 WGC + Media Foundation H.264 基線未完成。
