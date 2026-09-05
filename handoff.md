# Handoff

## 目前狀態

- 更新：2026-09-05，Codex；工作單 `WO-DRIVE-GITHUB-ALIGN-20260905-v2` 已確認。
- 對齊 v4.4.1 source、實際 protocol v7 與既有 GitHub main；修正 AGENTS 的 v6／NOT_CONFIGURED 舊值，移除 GitHub 不存在的 ZIP 下載連結。
- 驗證：Git root／remote identity、四檔與 manifest schema、相對連結及 diff whitespace 檢查；程式與 runtime 未變，不將歷史實機測試標為本輪重跑。
- GitHub：`sink6985757-web/LanRemote-win-x64`，default branch `main`。本輪成果以本文件所在 commit 識別；完成非 force push 後，以 `git ls-remote origin refs/heads/main` 與 GitHub API 回讀核對。
- Checkpoint：`manual`；三個 authority immutable SHA 已寫入 `.agents/project-lifecycle.json`，不啟用 standing_scoped。

## 風險與保留

v4.4.1 ZIP 只在本機 artifacts/：136,931,178 bytes，SHA-256 85FD41ADDE5D106AFC53122D3C596AFF0597D0BA0C4430889F0223F0FDC59579；不在 GitHub Code ZIP，也未建立 Release。先前 Debug／Release 91/91 為歷史；兩機 Right Alt／IME／選取視覺與 schema v8 驗收仍待完成，binary NotSigned。保持可見配對、session 授權與本機停止入口，不操作正在使用的遠端 session。

既有測試與版本歷史查閱 CHANGELOG／Git；沒有本輪執行的裝置、安裝、部署或帳號驗證不得視為重新通過。

## 唯一續跑點

兩台自有電腦使用同一 v4.4.1 測試包，先確認雜湊，再完成人工鍵盤／IME／傳輸驗收，證據存 ignored readygate/evidence-inbox/；本輪僅 source 文件交付。

跨裝置接續先讀 manifest、AGENTS、本檔與 Git 狀態，fetch 並比較 default branch。GitHub SHA 回讀與 Drive 雲端回讀分別記錄；若任一未完成，保留該項 PARTIAL，不推論整體已同步。
