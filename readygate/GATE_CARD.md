---
readygate_version: 1
profile: general
subject: LanRemote win-x64 self-contained MVP 測試候選
scope: Windows 11 25H2 與 Windows 10 22H2 私人區網雙向換角控制
revision: 4fd543b1391ba5ae5d8c876387bb14ab0abbb7d9
evidence_cutoff: 2026-08-22 22:40 +08:00
status: NOT_READY
workflow_state: DELIVERY_REVIEW
work_order: WO-LANREMOTE-MVP-20260822
cycles_used: 3
reviewer: Codex (Agent); Yulin 僅為工作單確認者與後續實機操作者
---

# ReadyGate：LanRemote MVP

## 結論

**NOT_READY** — 本機安全垂直切片與測試包已成立，但 Windows 10／11 兩個方向的實機證據皆缺失，且確認工作單要求的 WGC + Media Foundation H.264 尚未實作。

## 五道閘門

| 閘門 | 狀態 | 一句話證據／缺口 |
|---|---|---|
| G1 目的與範圍 | VERIFIED | `WORK_ORDER.md` 固定同一私人區網、單一方向 session、斷線換角與明確排除項目。 |
| G2 輸入與版本 | PARTIAL | implementation revision 與套件雜湊可回讀；Windows 10 22H2 非 .NET 10 官方支援 OS。 |
| G3 耦合與風險 | PARTIAL | TLS/SAS、私人 IPv4、核准 gate、UIPI 與防火牆邊界已落實；影像仍是 GDI/JPEG 相容路徑。 |
| G4 驗證與證據 | NOT_READY | 本機 Release 16/16、TLS loopback 與 UI 冒煙通過，但缺雙機五分鐘、1080p30 與 p95 < 200 ms 證據。 |
| G5 交付與回復 | NOT_READY | 有 self-contained ZIP、SHA-256、操作與證據腳本；未取得 Windows 10 成功啟動與雙向控制證據。 |

## Critical 證據

| 項目 | 狀態 | 證據／來源 | 範圍與版本 | 缺口 |
|---|---|---|---|---|
| 工作單與安全邊界 | VERIFIED | `readygate/WORK_ORDER.md`、`AGENTS.md` | WO-LANREMOTE-MVP-20260822 | 無 |
| 可回讀 implementation | VERIFIED | Git commit `4fd543b1391ba5ae5d8c876387bb14ab0abbb7d9` | 本機 `main` | 無 remote，維持 local-only |
| Release build／測試 | VERIFIED | `CHANGELOG.md`；`dotnet test` 16/16 | .NET SDK 10.0.400、win-x64 | 尚未在第二臺重跑 |
| 核准前不得傳畫面 | VERIFIED | `RemoteSessionTests.RejectedSession_DoesNotStartScreenCapture` | TLS loopback | 仍需實機觀察 |
| SAS／畫面／輸入 session | VERIFIED | `RemoteSessionTests.ApprovedSession_MatchesPairingCode_ThenTransfersFrameAndInput` | TLS loopback | 仍需跨裝置實測 |
| self-contained 測試包 | VERIFIED | `artifacts/LanRemote-win-x64.zip`，SHA-256 `86392CCE768C800E3BE1B87DDCB6961FE2402ECBE78242EA2A56794307223AEA` | commit `4fd543b` | artifacts 不進 Git，須保留雜湊核對 |
| Windows 11 → Windows 10 | UNKNOWN | 尚無 `readygate/evidence-inbox` JSON | 五分鐘、主要螢幕、滑鼠鍵盤 | 使用者在兩臺實機執行 |
| Windows 10 → Windows 11 | UNKNOWN | 尚無 `readygate/evidence-inbox` JSON | 五分鐘、主要螢幕、滑鼠鍵盤 | 斷線換角後重測 |
| 1080p30／p95 < 200 ms | UNKNOWN | 尚無雙機效能紀錄 | 兩個控制方向 | 各方向至少五分鐘 |
| WGC + Media Foundation H.264 | FAIL | `GdiJpegScreenFrameSource.cs`、`README.md` | confirmed performance baseline | 實作 WGC/H.264 後重跑全部效能測試 |
| Windows 10 平臺支援 | EXCEPTION | .NET 10 官方 OS 支援表不含 Windows 10 22H2 | self-contained 只能實測 | 即使通過，最終最高為 CONDITIONAL |

## 最短補強路徑

1. 先以 WGC + Media Foundation H.264 取代 GDI/JPEG，關閉 critical `FAIL`。
2. 在 Windows 11 25H2 與 Windows 10 22H2 各跑兩個控制方向五分鐘，產生四份 evidence JSON。
3. 回讀 ZIP／EXE 雜湊、配對碼、輸入、斷線、1080p30 與 p95 結果後重新產生 Gate Card。

## 例外與責任

- Windows 10 22H2 非 .NET 10 官方支援 OS — owner：Yulin（實機決策）；期限：下一次雙機測試；核准：尚未核准 release override。
- GDI/JPEG 相容路徑不符合確認效能基線 — owner：後續 implementation agent；期限：下一個效能工作項目；核准：僅限本機測試，不是交付例外。

## 放行決定

- 決定：停止；只可依既有授權把本機測試包帶到兩臺自有電腦做驗證，不得宣稱正式 READY、公開發布或擴大範圍。
- override：無。

## 回復／交棒

- rollback：任一端按「立即斷線」或關閉程式；移除複製的本機測試資料夾即可撤回套件；程式碼以 `git revert` 建立可追蹤回復。
- 下一位：Yulin 執行兩臺實機；後續 Agent 實作 WGC/H.264 並審查 evidence。
- 重新檢查條件：原始碼、SDK/runtime、套件雜湊、Windows build、網路拓撲、安全模型或工作單任一項變更。

> 本卡是證據式準備度摘要，不取代 Microsoft 平臺支援政策、安全審查或合格專業人員正式 sign-off。
