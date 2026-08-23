---
work_order: WO-LANREMOTE-GITHUB-APACHE2-SHUTDOWN-20260823
status: WORK_ORDER_CONFIRMED
confirmed_at: 2026-08-23
profile: general
cycles_used: 0
---

# LanRemote GitHub、Apache-2.0 與收工工作單

## 目標

把目前 LanRemote v4.3.1／protocol v6 原始碼測試候選建立為既有公開 GitHub repository 的首次 `main` checkpoint，加入 Apache License 2.0，完成本輪收工文件與遠端 SHA 回讀。

## 已確認輸入

- Project：`區網windwos`
- Local branch／baseline：`main`／`ca147365bf4a2e7dea951781ae23645399843539`
- Existing remote repository：`sink6985757-web/LanRemote-win-x64`
- Remote preflight：public、empty、viewer permission `ADMIN`
- Checkpoint policy：`manual`
- Product state：v4.3.1／protocol v6 自有兩機測試候選；正式軟體發布仍為 `NOT_READY`

## 授權動作

- 新增根目錄 `LICENSE`，使用 Apache License 2.0 官方完整原文；README 標示 SPDX 識別碼 `Apache-2.0`。
- 把 `LICENSE` 與 GitHub repository identity 登記到 portable project manifest。
- 移除公開文件內的本機電腦名稱，更新 README、CHANGELOG、handoff 與 ReadyGate 證據。
- 執行 build、test、format、PowerShell parser、NuGet vulnerability audit、secret scan 與 publishable file size scan。
- 只 stage manifest allowlist 內已知原始碼、測試、腳本、文件及本輪刪除項目。
- 對既有空白 repository 新增 `origin`，在目前 `main` 建立 scoped commit，使用非 force push 建立遠端 `main`。
- 回讀遠端 SHA、GitHub 預設分支、README 與 LICENSE；需要時再建立一個只含 shutdown／ReadyGate 文件的 checkpoint 並再次回讀。

## 明確不納入

- 不上傳 ignored `artifacts/`、`bin/`、`obj/`、`TestResults/` 或 `readygate/evidence-inbox/`。
- 不上傳 136 MB 的 v4.3.1 ZIP；不建立 tag、GitHub Release、PR、branch protection 或權限變更。
- 不建立新的 GitHub repository、不 force push、不 merge／rebase、不刪除既有資料。
- 不把 source checkpoint 宣稱為 Windows 10／11 正式相容性或簽章 Release。
- 本輪不新增 `NOTICE`；若未來納入要求 attribution 的第三方內容，再另行審核。

## 驗收條件

1. Apache License 2.0 官方原文可由 GitHub `main` 回讀，README 明確標示 `Apache-2.0`。
2. GitHub 預設分支為 `main`，遠端 `refs/heads/main` 與本機最終 HEAD 一致。
3. 已確認原始碼、測試與文件完整進入 Git；ignored build/package/evidence 沒有進入 commit。
4. 自動驗證通過，公開掃描沒有 credential、private key、本機名稱或裝置絕對路徑。
5. CHANGELOG 與 handoff 記錄 source checkpoint、產品 `NOT_READY` 限制、回復方式及唯一續跑點。

## 風險與回復

- Apache-2.0 對已公開副本提供永久授權；未來移除 LICENSE 不能溯及撤回既有取得者的權利。
- 已發布錯誤只使用 `git revert` 與後續非 force push 修正。
- 軟體功能發生問題時，維持兩臺同時退回使用者已驗證版本的 package 回復路徑。
