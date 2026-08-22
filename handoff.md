# Handoff

## 目前做到哪

已完成獨立 Git、.NET 10 solution、TLS/SAS 安全 session、同一 WPF 程式的被控端／控制端角色、GDI/JPEG 主要螢幕傳輸、受限滑鼠鍵盤控制、測試與 self-contained win-x64 測試包。兩臺電腦可以在斷線後交換角色，因此產品路徑是雙向控制，但單一 session 不同時雙向。

## 目前狀態

- 可執行：是，`artifacts/LanRemote-win-x64.zip` 是包含 runtime 的本機測試包。
- 測試包：由 implementation commit `4fd543b1391ba5ae5d8c876387bb14ab0abbb7d9` 建置；ZIP SHA-256 `86392CCE768C800E3BE1B87DDCB6961FE2402ECBE78242EA2A56794307223AEA`；EXE SHA-256 `BBC3E3FF1CD715BC89744278872E9B0B25B6A27DB477F662D87E9B660D2C98CC`。
- 已驗證：Debug／Release build、16/16 tests、真實 TLS loopback、配對拒絕 gate、畫面／輸入協定、WPF 被控端啟停 UI 冒煙測試。
- 尚未驗證：Windows 10 22H2 實際啟動、兩個控制方向各五分鐘、1080p30 與互動延遲 p95 < 200 ms。
- 技術差距：目前是 GDI/JPEG 相容垂直切片；WGC + Media Foundation H.264 尚未實作。
- ReadyGate：目前 `NOT_READY`；即使雙機測試成功，因 Windows 10 22H2 非 .NET 10 官方支援 OS，最高仍為 `CONDITIONAL`。

## 下一步

1. 把 `artifacts/LanRemote-win-x64.zip` 複製到 Windows 10 22H2，兩臺都完整解壓縮。
2. 依 ZIP 內 `README.txt` 先做 Windows 11 控制 Windows 10 五分鐘，再斷線交換角色做 Windows 10 控制 Windows 11 五分鐘。
3. 兩臺各用 ZIP 內 `New-TwoPcEvidence.ps1` 產生 JSON，複製回 `readygate/evidence-inbox/`。
4. 回到本專案執行 `startup` 後，只讀審查證據；若功能／效能失敗，保留 `FAIL`，不暗中降級 .NET。
5. 後續獨立工作項目：以 WGC + Media Foundation H.264 取代相容傳輸，再重跑完整雙機效能測試。

## 注意事項

- 上層 `gogoYulin` 是多個獨立 repository 的工作區索引，不得把上層 Git 工作樹當成本專案 repository。
- Manifest 的 authority commit 是目前遠端基線；本機執行中的 lifecycle／ReadyGate 內容與本機 Full Core schema 尚有未 checkpoint 的新變更，因此權威內容回溯狀態為 `PARTIAL`。
- MVP 不提供隱蔽、未授權、無人值守或跨網際網路的遠端控制。
- Windows `SendInput` 受 UIPI 限制，普通程序不能藉此繞過較高權限視窗或安全桌面。
- 防火牆只由使用者本人處理私人網路提示；程式與腳本不自動修改規則。
- `readygate/evidence-inbox/` 包含電腦名稱與私人 IP，已加入 `.gitignore`，不得直接提交。

## 最近更新

- 時間：2026-08-22 22:40 +08:00
- 更新者：Codex
- 電腦：YULIN-SFG16-72
- 成果 commit：`4fd543b1391ba5ae5d8c876387bb14ab0abbb7d9`
- GitHub：`NOT_CONFIGURED`
