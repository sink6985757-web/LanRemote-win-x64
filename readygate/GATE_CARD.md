---
readygate_version: 1
profile: general
subject: LanRemote v4.4.1 全鍵盤擷取、檔案選取對比與原創圖示
scope: 本機 protocol v7 實作、驗證與自有兩機測試候選
revision: b3ca0f26cbc964b04d5f6a77f9f2c93fc8a87722+local
evidence_cutoff: 2026-08-23 16:11 +08:00
status: CONDITIONAL
workflow_state: DELIVERY_REVIEW
work_order: WO-LANREMOTE-V441-LIVE-QA-KEYBOARD-HOOK-20260823
cycles_used: 3
reviewer: Codex；Yulin 為工作單與圖示增補確認者
---

# ReadyGate：LanRemote v4.4.1 本機測試候選

## 結論

**CONDITIONAL** — v4.4.1／protocol v7 的低階全鍵盤擷取、檔案選取高對比、原創應用程式圖示、自動測試、封裝與 launcher smoke 已通過，可交由 Yulin 在自有 Windows 11 25H2／Windows 10 22H2 兩機進行驗收；修正版 Right Alt、被控端輸入法、作用中／失焦選取視覺及完整雙向回歸尚無 v4.4.1 實機證據，因此正式軟體發布仍為 **NOT_READY**。

Yulin 已在本卡完成後另行明確授權目前 v4.4.1 的公開 source checkpoint 與 Open Source README；仍不授權 tag、GitHub Release、簽章或正式相容性／效能宣稱。

## 五道閘門

| 閘門 | 狀態 | 一句話證據／缺口 |
|---|---|---|
| G1 目的與範圍 | VERIFIED | 修改限於控制端低階鍵盤路由、檔案選取配色、雙機 evidence schema 與原創應用程式圖示；既有畫面、畫質、檔案／剪貼簿資料模型、配對及安全邊界未改。 |
| G2 輸入與版本 | VERIFIED | Git baseline `b3ca0f26...`、live v4.4／protocol v7 輸入、v4.4.1／protocol v7 輸出、圖示提示與三個資產 SHA-256 均可追溯。 |
| G3 耦合與風險 | VERIFIED | hook 只在前景＋遠端輸入狀態攔截非 injected 實體鍵，只有本機 SAS 不轉送；失焦／斷線解除並 release-all。圖示未使用第三方圖片或品牌元素。 |
| G4 驗證與證據 | CONDITIONAL | Debug／Release 91/91、build、format、scripts、NuGet audit、ZIP、EXE 圖示抽取、小尺寸預覽與 launcher smoke 通過；v4.4.1 兩機人工驗收待補。 |
| G5 交付與回復 | VERIFIED | v4.4.1 ZIP、SHA-256、746 unique entries 與 12/12 manifest 可回讀；鍵盤 critical 問題可把 controller 換回同為 protocol v7 的 v4.4。 |

## Critical 證據

| 項目 | 狀態 | 證據／來源 | 範圍與版本 | 缺口 |
|---|---|---|---|---|
| 已確認工作單 | VERIFIED | `readygate/WORK_ORDER-LIVE-QA-KEYBOARD-HOOK-V441.md` | Cycle 3／3；包含原創圖示增補 | 無 |
| v4.4 live 基線 | VERIFIED | ignored `readygate/evidence-inbox/20260823-v44-live-baseline.json` | 畫面／一般鍵／檔案傳輸通過；Right Alt／host IME／Caps Lock 失敗 | 只證明修正前問題，不證明 v4.4.1 |
| 低階鍵盤 routing | VERIFIED | hook／mapper／state tests、UI source contract、TLS loopback | 前景、非 injected、all keys remote、local SAS、release-all | 真實 Right Alt／被控端 IME 待 Yulin 測試 |
| 本機 SAS 邊界 | VERIFIED | routing state tests、injector defense-in-depth | 實體 Ctrl+Alt+Delete 不送遠端；Toolbar SAS 路徑保留 | 依工作單不以自動化觸發安全桌面 |
| 檔案選取對比 | VERIFIED | XAML resources／styles、UI source-contract test | active `#155E75`、inactive `#164E63`、text `#F8FAFC` | 連線後主觀可讀性待 Yulin 確認 |
| 原創圖示 | VERIFIED | `src/LanRemote.App/Assets/PROVENANCE.md`、RGBA／ICO 回讀、小尺寸 QA、EXE icon extraction、GUI smoke | 9 個 ICO 尺寸；EXE／WPF／ZIP；無第三方輸入圖 | 正式商標／法律檢索不在本輪 |
| 自動測試與建置 | VERIFIED | `dotnet test`／`build`／`format` | Debug 91/91、Release 91/91、六專案 0 warning／0 error | 無 |
| Script／dependency | VERIFIED | PowerShell parser、NuGet vulnerability audit | 四個 scripts；六個 projects | audit 只代表當次 NuGet 來源回報 |
| v4.4.1 ZIP | VERIFIED | ZIP／manifest／README／Authenticode 回讀 | 136,931,178 bytes；SHA-256 `85FD41AD...FDC59579`；746 unique；12/12 | EXE／服務未簽章 |
| 封裝版 GUI | VERIFIED | Computer Use launcher smoke | 標題列新圖示、高對比 launcher、無 Tab、既有 Toolbar／畫質入口 | 未啟動 host 或重新配對 |
| 正式軟體發布 | NOT_READY | `handoff.md` schema v8 續跑點 | Windows 11 25H2／Windows 10 22H2 | 兩個方向完整實機證據 |

## 最短補強路徑

1. 在 v4.4.1 實機鎖定遠端輸入後測 Right Alt、被控端輸入法、Caps／Num／Windows／F11／Esc／Alt+Tab，以及滑鼠點擊後恢復本機。
2. 在檔案面板實際確認作用中與失焦選取列的檔名可讀，並回歸雙端主動／同時雙向傳輸。
3. 兩個控制方向各產生一份 schema v8 evidence JSON，再重算正式發布結論。

## 例外與責任

- v4.4.1 ZIP 位於 ignored `artifacts/`，只供自有電腦測試，不是公開 Release。
- `LanRemote.App.exe` 與 SAS 服務仍未簽章；owner：Yulin；正式發布前需另行決定 code-signing。
- 原創圖示沒有第三方輸入圖片，provenance 可追溯，但不取代正式著作權／商標檢索；owner：Yulin。
- Windows 10 22H2 的實機可運行結果不等同 .NET 10 官方支援聲明。
- Google Drive 留下三個 recoverable previous 目錄；目前新版 ZIP 已獨立驗證，不以強制刪除鎖定檔換取表面乾淨。

## 放行決定

- 決定：放行自有兩機 v4.4.1 驗收，並依 2026-08-23 後續明確授權放行目前 `main` 的公開 source checkpoint。
- 停止：tag、GitHub Release、簽章發包、權限變更、正式相容性或效能宣稱。
- override：無。

## 回復／交棒

- rollback：若低階擷取有 critical 問題，關閉 v4.4.1 controller 並換回 v4.4；兩者都是 protocol v7。若回到 v4.3.1，兩端一起切回 protocol v6。
- 下一位：Yulin 依 `handoff.md` 執行 schema v8 的兩個控制方向測試並回傳 ignored evidence JSON。
- 重新檢查條件：鍵盤／IME、游標、畫面、檔案、剪貼簿、圖示、安全模型、dependency、ZIP 或雙機證據任一項變更。

> 本卡只證明已確認工作單範圍內的本機測試候選，不取代正式發布、安全審查、Microsoft 平臺支援政策或著作權／商標法律意見。
