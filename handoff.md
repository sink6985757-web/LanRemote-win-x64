# Handoff

## 目前做到哪

LanRemote 目前為 package v4.3.1／協定 v6。v4.3 連線後預設停在「工作階段概覽」，而接收端又會丟棄未選取遠端控制分頁時收到的 frame；v4.3.1 已移除這項 UI 路由依賴，控制端配對成功後直接顯示遠端桌面。

工作階段概覽、遠端控制與檔案傳輸三個 Tab 已移除。控制端顯示遠端桌面，被控端顯示簡潔受控狀態；檔案面板只由 Toolbar「檔案」開啟，在同一主視窗按「返回桌面」後繼續背景傳輸。既有雙端主動傳輸、文字剪貼簿、拖放、三種畫質、縮放、快捷鍵與雙游標均保留。

連線初始介面改為明確高對比文字。TCP、TLS 與初始協定交握預設 15 秒逾時，人工配對核准獨立保留 120 秒，遠端桌面初始化再限制 15 秒；錯誤格式、拒絕、版本不符或逾時會清理連線並顯示「未連線成功」。

## 目前狀態

- Git：本機 `main`，基準 HEAD `ca147365bf4a2e7dea951781ae23645399843539`；本輪已確認把既有 v5／v6 功能與 v4.3.1 修復建立為首次公開 source checkpoint，完成狀態以 GitHub remote SHA 回讀為準。
- v4.3.1 測試包：[artifacts/LanRemote-v4.3.1-win-x64.zip](artifacts/LanRemote-v4.3.1-win-x64.zip)，136,112,419 bytes。
- ZIP SHA-256：`A9954D63939A6580DCE98E4589041D36D33367525F791D33138372B303F083F2`。
- ZIP 回讀：743 entries、743 unique、0 duplicate、9/9 manifest hash PASS；內嵌 README 可見 v4.3.1、協定 v6、直接遠端桌面與無 Tab 介面。
- Debug／Release tests：61/61 PASS；包含 TLS 無回應逾時、配對核准獨立逾時、UI 無 Tab source contract、雙端檔案傳輸、文字剪貼簿與既有遠端控制協定。
- Debug／Release build：0 warning／0 error；`dotnet format --verify-no-changes` PASS。
- 四個 PowerShell 腳本 parser：0 error；雙機證據腳本為 schema v6。
- 六專案 NuGet vulnerability audit：目前來源未回報已知弱點套件。
- 封裝版 GUI smoke：實際可見高對比 launcher、無 session Tab 與 Toolbar「檔案」；輸入 `bad-key` 後實際出現「未連線成功」對話框。未啟動 host、未配對、未操作 Windows Firewall。
- Authenticode：應用程式與 SAS 服務均 `NotSigned`，只限自有電腦測試。
- Source delivery：`sink6985757-web/LanRemote-win-x64`，Apache License 2.0；只包含原始碼與文件，ignored 測試 ZIP／build outputs／雙機 evidence 不公開。
- ReadyGate：GitHub source checkpoint 已獲工作單授權；正式軟體發布仍為 `NOT_READY`，缺 Windows 11 25H2／Windows 10 22H2 schema v6 兩機遠端畫面與完整回歸證據。
- 先前 Google Drive 鎖定留下的 `artifacts/LanRemote-v4.2-win-x64.previous-42583b27cd364d1d8441569d065984d8/` 仍是 0-entry 空目錄，不含程式或資料；本輪未刪除。

## 唯一續跑點

在 Windows 11 25H2 與 Windows 10 22H2 使用同一份 v4.3.1 ZIP，核對 SHA-256 後完成 schema v6 實機測試：

1. 兩臺都關閉舊版、完整解壓 v4.3.1，發起端要求檔案傳輸，接收端核准。
2. 確認控制端配對後直接顯示第一張與持續更新的遠端畫面，且兩端都沒有三個 session Tab。
3. 在兩端確認 Toolbar「檔案」可開啟面板、返回桌面不中止傳輸，並回歸控制端／被控端主動傳送與同時雙向傳送。
4. 輸入錯誤格式與沒有服務的私人區網位址，確認會在限制時間內顯示「未連線成功」並可重新連線。
5. 回歸文字剪貼簿三模式、拖放、鍵鼠、游標、三種畫質／縮放、立即斷線、衝突模式與 partial 續傳。
6. 每個控制方向執行 `New-TwoPcEvidence.ps1`，把 schema v6 JSON 放回 ignored `readygate/evidence-inbox/`。

## 回復方式

- Session：任一端按「立即斷線」或關閉程式；檔案與文字剪貼簿授權隨 session 失效。
- Package：若 v4.3.1 實機仍有 critical 問題，兩臺都完整換回使用者已確認畫面正常的同一份 v4 ZIP；不同協定版本不得混用。
- Source：已發布錯誤使用 `git revert <commit>` 後非強制推送修正，不要 `reset --hard` 或 force push。
- SAS 服務：在對應電腦以系統管理員 PowerShell 手動執行 `uninstall-sas-service.ps1`。

## 注意事項

- 協定仍為 v6，v4.3.1 可與 v4.3 協定交握，但兩臺實機驗證時必須都換成 v4.3.1，避免另一端仍帶有畫面路由回歸。
- 遠端控制仍是單一方向；檔案傳輸才是在同一 session 中允許雙方主動發起。
- 一般 `SendInput` 仍受 UIPI 限制；`Ctrl+Alt+Delete` 只走固定用途 SAS 服務。
- 上層 `gogoYulin` 是多個獨立 repository 的工作區索引，不得把上層 Git 工作樹當成本專案。
- `readygate/evidence-inbox/` 含電腦名稱與私人 IP，已忽略，不得直接提交。

## 最近更新

- 時間：2026-08-23（GitHub source checkpoint）
- 更新者：Codex
- 執行環境：runtime device（未寫入裝置識別）
- GitHub：`sink6985757-web/LanRemote-win-x64`；最終 SHA 待 push 後回讀
