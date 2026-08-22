# Handoff

## 目前做到哪

已完成 LanRemote 協定 v2 與 VNC-style 單視窗工作區。程式先顯示 launcher；TLS/SAS 核准後在同一視窗顯示遠端桌面，可切換視窗化、最大化、無邊框全螢幕，並在不中斷 session 的情況下切換流暢、平衡、畫質模式。斷線後回到 launcher，保留位址與最後模式。

兩臺電腦仍是「單一 session 單向、斷線交換角色」的雙向控制模型，不是同時互控。

## 目前狀態

- 可執行：是，`artifacts/LanRemote-win-x64.zip` 是包含 .NET runtime 的未簽章本機測試候選。
- source revision：`11ea76e28ce7d28064260ff3f49d9c1dfcd9f98a`；implementation commit：`b976710`。
- ZIP：79,784,941 bytes；SHA-256 `5C971B6A750F652A6D22B99C887A3A65066000450FE6239E10701353CFD5A216`。
- EXE SHA-256：`32CE7CD65AA2396DF246F38678BDB26E9D191C526DCBCE07E95FAEA62CE4020E`。
- 已驗證：Debug／Release build、22/22 tests、format、NuGet audit、真實 TLS loopback 與本機 WPF 雙實例 GUI 流程。
- GUI 已驗證：同視窗進入遠端桌面、視窗化、最大化、F11 全螢幕、工具列隱藏／頂端叫回、三種畫質熱切換、斷線返回 launcher。
- 使用者先前回報舊版兩臺連線正常；這是 `REPORTED`，不能取代新版協定 v2 的 Windows 10／11 跨機證據。
- 尚未驗證：v2 在 Windows 10 22H2 實際啟動，以及兩個控制方向的跨機畫面、輸入、視窗／畫質操作。
- ReadyGate：`WO-LANREMOTE-UX-PROFILES-V1` 為 `CONDITIONAL` 本機測試候選；無 release override，正式發布停止。

## 唯一續跑點

1. 把同一份 `artifacts/LanRemote-win-x64.zip` 複製到 Windows 11 25H2 與 Windows 10 22H2，完整解壓縮；兩臺舊版都先關閉。
2. 先測 Windows 11 控制 Windows 10：核對 SAS，測試滑鼠、鍵盤、視窗化／最大化／全螢幕、工具列顯隱，以及三種畫質熱切換。
3. 斷線交換角色，再測 Windows 10 控制 Windows 11；確認斷線後位址與模式保留。
4. 兩個方向都通過後，用 ZIP 內 `New-TwoPcEvidence.ps1` 產生 JSON，複製回 `readygate/evidence-inbox/`。
5. 回到本專案執行 `startup`，以唯讀方式審查 evidence 並重產 ReadyGate Gate Card。

## 後續獨立工作

- 以 Windows Graphics Capture + Media Foundation H.264／硬體編碼取代 GDI/JPEG，再建立 1080p30 與互動延遲 p95 的效能基線。
- Windows 10 22H2 非 .NET 10 官方支援 OS；即使實機成功，對外相容性宣稱仍需明確例外審查。

## 注意事項

- 協定 v2 不向下相容，兩臺必須同時使用本輪新版。
- 上層 `gogoYulin` 是多個獨立 repository 的工作區索引，不得把上層 Git 工作樹當成本專案 repository。
- MVP 不提供隱蔽、未授權、無人值守或跨網際網路的遠端控制。
- Windows `SendInput` 受 UIPI 限制；防火牆私人網路提示只由使用者本人操作。
- `readygate/evidence-inbox/` 包含電腦名稱與私人 IP，已加入 `.gitignore`，不得直接提交。

## 最近更新

- 時間：2026-08-23 00:10 +08:00
- 更新者：Codex
- 電腦：YULIN-SFG16-72
- GitHub：`NOT_CONFIGURED`
