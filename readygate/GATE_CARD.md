---
readygate_version: 1
profile: general
subject: LanRemote v4.3.1 遠端畫面與介面修復測試候選
scope: 本機程式、self-contained win-x64 ZIP 與雙機測試準備度
revision: working tree based on ca147365bf4a2e7dea951781ae23645399843539
evidence_cutoff: 2026-08-23 13:30 +08:00
status: NOT_READY
workflow_state: DELIVERY_REVIEW
work_order: WO-LANREMOTE-V431-REMOTE-DISPLAY-FIX-20260823
cycles_used: 0
reviewer: Codex；人類 approver 尚未完成 v4.3.1 雙機驗收
---

# ReadyGate：LanRemote v4.3.1

## 結論

**NOT_READY** — v4.3 畫面路由根因已修復，本機測試、封裝版 UI 與 ZIP 完整性均通過；但 Windows 11 25H2／Windows 10 22H2 尚未實際驗證 v4.3.1 的遠端畫面與完整回歸，因此只能作為自有兩機測試候選，不能正式發布。

## 五道閘門

| 閘門 | 狀態 | 一句話證據／缺口 |
|---|---|---|
| G1 目的與範圍 | VERIFIED | 確認工作單只含畫面路由、無 Tab 介面、對比、逾時、測試與本機封裝。 |
| G2 輸入與版本 | VERIFIED | v4.3／protocol v6 dirty tree、Git baseline 與 v4.3.1 ZIP 均可追溯。 |
| G3 耦合與風險 | VERIFIED | protocol 保持 v6；逾時分成 15 秒交握、120 秒人工核准與 15 秒桌面初始化，未改安全模型。 |
| G4 驗證與證據 | UNKNOWN | 61/61、自動建置、GUI smoke 與 ZIP 回讀通過；兩臺實機第一張／持續畫面仍未知。 |
| G5 交付與回復 | VERIFIED | 僅產出本機未簽章測試包；可讓兩臺同時退回使用者已確認畫面正常的同一份 v4。 |

## Critical 證據

| 項目 | 狀態 | 證據／來源 | 範圍與版本 | 缺口 |
|---|---|---|---|---|
| v4.3 畫面根因 | VERIFIED | `EnterSessionView` 原先選 index 0，`EnqueueFrame` 原先拒絕非 RemoteControlTab frame；新程式已移除兩項依賴 | v4.3.1 source | 無 |
| 無 Tab／Toolbar 檔案入口 | VERIFIED | XAML compile、UI source-contract test、封裝版 GUI smoke | v4.3.1 | 雙機 session 中仍需人工回讀 |
| 連線逾時 | VERIFIED | TLS 無回應與配對核准分段逾時 integration tests | protocol v6 core | 實際 LAN 15 秒體感待雙機確認 |
| 失敗對話框／launcher 對比 | VERIFIED | 封裝版輸入 `bad-key`，實際顯示「未連線成功」；launcher screenshot 可回讀高對比文字 | v4.3.1 ZIP | 無 |
| 自動測試與建置 | VERIFIED | Debug／Release 61/61；Release 0 warning／0 error；format、4 scripts、6-project audit PASS | v4.3.1 | 無 |
| 封裝完整性 | VERIFIED | 136,112,419 bytes；SHA-256 `A9954D63939A6580DCE98E4589041D36D33367525F791D33138372B303F083F2`；743 unique；9/9 manifest | v4.3.1 ZIP | 未簽章，只限自有電腦 |
| Windows 11 ↔ Windows 10 遠端畫面 | UNKNOWN | 尚無 schema v6 evidence JSON | v4.3.1 ZIP | 兩個控制方向都要驗證第一張與持續 frame |
| 既有功能雙機回歸 | UNKNOWN | 尚無 v4.3.1 兩機證據 | v4.3.1 ZIP | 檔案、剪貼簿、鍵鼠、畫質、縮放、斷線需回歸 |

## 最短補強路徑

1. 兩臺都完整換成同一份 v4.3.1 ZIP並核對 SHA-256，避免 v4.3 舊 UI 混入。
2. 先驗證 Windows 11 控制 Windows 10，確認配對後直接出現第一張與持續遠端畫面，再交換角色重測。
3. 驗證 Toolbar「檔案」、背景傳輸、剪貼簿、鍵鼠、三種畫質／縮放及錯誤連線提示，回收兩份 schema v6 JSON。

## 例外與責任

- Windows 10 22H2 不在 .NET 10 官方支援清單 — owner：Yulin；期限：實機測試完成前；核准：未核准正式相容性宣稱。
- Authenticode `NotSigned` — owner：Yulin；期限：任何公開發布前；核准：僅限自有兩機測試。

## 放行決定

- 決定：停止正式發布；允許依確認工作單使用本機 v4.3.1 ZIP進行自有兩機驗收。
- override：無。

## 回復／交棒

- rollback：兩臺同時退回使用者已確認遠端畫面正常的同一份 v4 ZIP；不得混用協定版本。
- 下一位：Yulin 執行 Windows 11 25H2／Windows 10 22H2 雙機測試，Codex 回讀 schema v6 證據後重算 Gate。
- 重新檢查條件：程式、ZIP、協定、OS、配對／權限或測試結果任一變更。

> 本卡是證據式準備度摘要，不取代 Microsoft 支援政策、Windows 安全模型或正式軟體發布審查。
