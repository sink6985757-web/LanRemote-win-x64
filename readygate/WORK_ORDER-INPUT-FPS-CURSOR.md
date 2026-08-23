# ReadyGate Confirmed Work Order — Input, FPS, Scaling and Cursors

- Work order: `WO-LANREMOTE-INPUT-FPS-CURSOR-V1`
- Status: `WORK_ORDER_CONFIRMED / EXECUTION`
- Confirmed: 2026-08-23
- Clarification cycles used: 2 of 3

## Objective

在既有 LanRemote v2 單一視窗遠端工作區上，加入安全注意序列、可自訂快捷鍵、加倍 FPS、三種縮放模式，以及可區分的本機與遠端游標顯示。

## Confirmed implementation boundary

- `Ctrl+Alt+Delete` 走專用 Windows LocalSystem 服務與固定用途本機 named pipe；不透過一般 `SendInput` 假裝成功。
- ZIP 內提供由使用者以系統管理員身分手動執行的服務安裝／移除腳本；程式只偵測狀態並顯示說明，不自動安裝服務或修改安全性原則。
- `Ctrl+Alt+0` 同時提供工具列選單與可自訂快捷鍵，按下時送到遠端目前作用中的應用程式。
- 工具列提供「新增自訂按鍵」與「移除自訂按鍵」選單；設定只保存在本機使用者設定目錄。
- 自訂快捷鍵阻擋 Windows 鍵、`Ctrl+Alt+Delete`、`Alt+F4`、`Ctrl+Escape` 等保留組合。
- 三種畫面模式的目標 FPS 全部加倍：流暢 60、平衡 48、畫質 30；接收端只保留最新待顯示 frame，負載不足時丟棄過期 frame，不累積無界佇列。
- 提供「符合視窗」、「拉伸滿版」、「裁切滿版」三種縮放模式，預設為拉伸滿版；滑鼠座標依模式正確換算。
- 被控端原生游標直接合成到擷取畫面。
- 控制端游標使用青色箭頭、白色描邊與半透明光圈，在遠端畫面上即時顯示。
- 控制端游標旁不顯示任何「控制」或其他文字標籤。
- 協定升級為 v3；兩臺電腦必須使用相同新版。

## Acceptance

1. 自動測試證明 SAS 請求只走固定協定訊息與專用 provider，成功／失敗結果能回到控制端。
2. 發包包含可執行的 SAS 服務與手動安裝／移除腳本；程式在服務或 Windows 原則未就緒時顯示可操作說明。
3. `Ctrl+Alt+0` 與合法自訂快捷鍵可由工具列送出；新增、移除、驗證及本機持久化有測試。
4. 三種畫質設定為 60／48／30 fps，顯示路徑採 latest-frame drop，畫面負載過高時不堆積。
5. Fit／Stretch／Crop 三種模式可以切換，預設 Stretch，座標映射測試涵蓋黑邊與裁切。
6. 被控端畫面包含原生游標；控制端覆蓋游標符合青色箭頭、白色描邊、半透明光圈，且無文字標籤。
7. build、test、自包含 win-x64 ZIP 與本機 GUI smoke test 通過。

## Explicit exclusions

- 不新增網際網路 relay、NAT traversal、無人值守、隱蔽監控或任意提權命令。
- SAS 服務只接受固定的安全注意序列命令，不提供 shell、程序啟動、檔案或任意 IPC 執行能力。
- 不自動安裝服務、不修改 Local Security Policy、Registry 或 Windows Firewall。
- 不由 Codex 自動操作 Windows 安全桌面；真正 `Ctrl+Alt+Delete` 由使用者在兩臺實機手動驗證。
- 不建立 MSI、不進行程式簽章、不公開發布。
- 不建立 remote／GitHub repository，不 push、merge、tag 或 release。

## Authorization

- 可修改原始碼、協定、Windows 服務、手動腳本、測試、文件與本機發包內容。
- 可執行 build、test、NuGet audit、本機雙實例 GUI 驗證與 self-contained ZIP 打包。
- 可建立 scoped local commits。
- 服務安裝、安全性原則調整、跨 Windows 10／11 實機驗證均由使用者操作。

## Delivery evidence boundary

本機測試可證明 v3 協定、固定用途 SAS provider、快捷鍵、縮放、frame drop、游標合成與發包結構成立；不能取代 Windows 10 22H2 與 Windows 11 25H2 上由使用者完成的服務安裝、Local Security Policy 與安全桌面雙向實測。在兩個方向都取得實機證據前，只能判定為有條件的測試候選。
