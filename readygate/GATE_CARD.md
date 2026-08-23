---
readygate_version: 1
profile: general
subject: LanRemote v3 輸入、FPS、縮放與雙游標本機測試候選
scope: Windows 11 25H2 與 Windows 10 22H2 私人區網換角雙向控制
revision: 9515ca698ad1b262fcd1c765dde7082099f61ade
evidence_cutoff: 2026-08-23 08:39 +08:00
status: NOT_READY
workflow_state: DELIVERY_REVIEW
work_order: WO-LANREMOTE-INPUT-FPS-CURSOR-V1
cycles_used: 2
reviewer: Codex (Agent); Yulin 為工作單確認者、舊版連線回報者與 v3 實機操作者
---

# ReadyGate：LanRemote v3 測試候選

## 結論

**NOT_READY** — v3 原始碼、38 項測試、固定用途 SAS 服務、唯一-entry ZIP 與關鍵檔案雜湊皆可回讀，但目前 revision 尚無完整 GUI 互動及 Windows 10／11 雙向實機證據，因此只可作為自有電腦測試候選，停止正式發布。

## 五道閘門

| 閘門 | 狀態 | 一句話證據／缺口 |
|---|---|---|
| G1 目的與範圍 | VERIFIED | 已確認工作單固定 v3、服務手動安裝、安全邊界、60／48／30 fps、三種縮放、快捷鍵與無文字游標樣式。 |
| G2 輸入與版本 | VERIFIED | Git `9515ca6`、.NET SDK 10.0.400、v3 magic／版本及 ZIP `CFEE020B…10BCC9B` 可追溯。 |
| G3 耦合與風險 | VERIFIED | SAS 只接受固定命令；pipe 限本機 interactive／SYSTEM／Administrators；一般 `SendInput` 仍攔截 CAD；腳本不改安全性原則。 |
| G4 驗證與證據 | NOT_READY | Release 38/38、build、NuGet audit、ZIP 回讀通過；目前 revision 的選單互動、服務安裝／安全桌面及兩機雙向行為仍為 UNKNOWN。 |
| G5 交付與回復 | CONDITIONAL | self-contained ZIP、內嵌 SHA-256、安裝／移除／證據腳本與 README 齊全；未簽章、無 remote，正式發布停止。 |

## Critical 證據

| 項目 | 狀態 | 證據／來源 | 範圍與版本 | 缺口 |
|---|---|---|---|---|
| 已確認工作單 | VERIFIED | `readygate/WORK_ORDER-INPUT-FPS-CURSOR.md` | WO-LANREMOTE-INPUT-FPS-CURSOR-V1；2 cycles | 無 |
| 可回讀原始碼 | VERIFIED | Git `9515ca698ad1b262fcd1c765dde7082099f61ade` | 本機 `main`；協定 v3 | 無 remote，維持 local-only |
| 自動驗證 | VERIFIED | Release 38/38；Release build 0 warning／0 error；format 與三個 PowerShell 腳本 parser PASS | protocol、TLS loopback、SAS 結果、快捷鍵、store、mapper、latest-frame、pipe | Windows SCM／安全桌面不能由單元測試證明 |
| 依賴風險 | VERIFIED | `dotnet list package --vulnerable --include-transitive` | 六個專案 | NuGet 來源未回報已知弱點套件 |
| v3 發包完整性 | VERIFIED | ZIP 135,966,577 bytes；SHA-256 `CFEE020B5DF15720832DD17AD760760D81483728838E5A56C378183EC10BCC9B` | 743 entries、743 unique、0 duplicate；9 個 manifest hash 回讀 PASS | EXE／服務均未簽章，只限自有電腦測試 |
| GUI 啟動畫面 | VERIFIED | Computer Use 截圖與 `MainWindow.xaml` 回讀 | 工具列可見 48 fps、縮放、CAD、Ctrl+Alt+0、自訂按鍵；游標只有箭頭與光圈、無「控制」文字標籤 | 截圖後 code-behind 有非版面修正；最終 binary 的互動未全跑 |
| GUI 選單與 session 互動 | UNKNOWN | 點擊時工具回報 `failed to activate captured window`，依規則停止 | 新增／移除、三縮放、雙游標、latest-frame 狀態列 | Yulin 於兩臺實機操作 |
| SAS 服務與安全桌面 | UNKNOWN | 未安裝服務、未修改 policy、未自動操作安全桌面 | Windows 10 22H2／Windows 11 25H2；兩個角色方向 | Yulin 手動安裝並按工具列 CAD 驗證 |
| v3 Windows 11 → Windows 10 | UNKNOWN | 尚無 `readygate/evidence-inbox` JSON | 畫面、鍵鼠、快捷鍵、游標、縮放、三 FPS 模式 | Yulin 執行 |
| v3 Windows 10 → Windows 11 | UNKNOWN | 尚無 `readygate/evidence-inbox` JSON | 斷線換角後重測 | Yulin 執行 |

## 最短補強路徑

1. 兩臺都換成同一份 v3 ZIP；需要接受 CAD 的被控端各自手動執行 `install-sas-service.ps1`，需要時由本人把 Windows policy 設成允許 Services。
2. 先測 Windows 11 控制 Windows 10，再斷線交換角色；每方向驗證 CAD、Ctrl+Alt+0、自訂按鍵新增／送出／移除、Fit／Stretch／Crop、雙游標與 60／48／30 fps 的實際值。
3. 兩方向各產生 evidence JSON 並回收後，重新審查 Gate Card；若要公開發布，再另補簽章與發布授權。

## 例外與責任

- Windows 10 22H2、服務安裝、Local Security Policy 與安全桌面為人工實機驗證 — owner：Yulin；期限：下一次雙機測試；核准：未核准正式發布例外。
- GDI/JPEG 的 60／48／30 fps 是目標上限，不是已證明效能 — owner：Yulin 實測、後續 Agent 回讀；期限：下一次雙機測試；核准：只允許測試候選。
- EXE 與服務 `NotSigned` — owner：未指派；期限：公開發布前；核准：未核准公開發布。

## 放行決定

- 決定：停止正式 delivery／release；依已確認工作單保留本機 ZIP，供 Yulin 在自有兩臺電腦執行測試。
- override：無。

## 回復／交棒

- rollback：任一端立即斷線或關閉程式；SAS 服務用 `uninstall-sas-service.ps1` 移除；原始碼用 `git revert 9515ca6` 建立可追蹤回復。
- 下一位：Yulin 執行 v3 Windows 10／11 雙向人工測試；後續 Agent 唯讀回讀 evidence。
- 重新檢查條件：原始碼、ZIP／manifest hash、SDK/runtime、Windows build、服務／policy、網路拓撲、安全模型或工作單任一項變更。

> 本卡是證據式準備度摘要，不取代 Microsoft 平臺支援政策、安全審查或合格專業人員正式 sign-off。
