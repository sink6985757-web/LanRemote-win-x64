LanRemote 區網雙向控制 MVP
===========================

這是一份未簽章的本機測試包，不是公開發布版本。

重要：本包版本為 v4.4.1、使用協定 v7。建議兩臺都使用同一份測試包；v4.4 host 與 v4.4.1 controller 的 protocol v7 相容，但修正版鍵盤擷取只存在於 v4.4.1 控制端。

程式已使用原創的深藍／青綠雙螢幕連線圖示；同一份 PNG、Windows 多尺寸 ICO 與生成來源紀錄分別放在 `LanRemote.App.png`、`LanRemote.App.ico`、`ICON-PROVENANCE.md`，並納入 `SHA256.txt`。

適用情境
--------
- 同一個私人區網內的兩臺 x64 Windows 電腦。
- 本次測試目標：Windows 11 25H2 與 Windows 10 22H2。
- Windows 10 22H2 不在 .NET 10 官方支援清單，必須以這臺實機測試結果為準。

第一次使用
----------
1. 把整個資料夾解壓縮到兩臺電腦；不要只複製 LanRemote.App.exe。
2. 兩臺電腦分別執行 LanRemote.App.exe。
3. 若 Windows Firewall 詢問，僅由你本人決定是否允許「私人網路」；不要允許公用網路。
4. 若檔案來源或 SHA256 與專案內 SHA256.txt 不一致，請勿執行。

連線視窗與畫面模式
------------------
- 程式先顯示高對比連線介面；核准後控制端直接進入遠端桌面，不再顯示工作階段、遠端控制或檔案傳輸標籤頁。
- TCP、TLS 與初始協定交握最多等待 15 秒；配對人工核准獨立保留 120 秒。錯誤位址、拒絕或逾時會顯示「未連線成功」。
- 預設為可調整大小的視窗化與「拉伸滿版」，調整視窗時畫面會自動填滿。
- 縮放可選「符合視窗」（保留比例，可能有黑邊）、「拉伸滿版」（預設、無黑邊）或「裁切滿版」（保留比例、裁掉邊緣）。
- 頂部工具列可選「視窗化」、「最大化」與「全螢幕」；在「本機操作」狀態，F11 可切換全螢幕，Esc 可退出全螢幕。
- 按「隱藏工具列」後，把滑鼠移到視窗最頂端即可暫時叫回選單和工具列。
- 斷線後會回到連線介面，並保留剛才的位址與畫面模式。

三種畫面設定
------------
- 流暢：最高 1280x720 / 60 fps，適合優先追求動態流暢。
- 平衡：最高 1600x900 / 48 fps，預設模式。
- 畫質：最高 1920x1080 / 30 fps，優先提高靜態畫面品質。
- 可在已連線時直接從 Quality 選單或工具列下拉選單切換，不必重新連線。
- 上述是目標上限；Toolbar 簡易狀態的「實際 fps」才是目前電腦與網路真正呈現速度。負載過高時會丟棄過期 frame，不會累積延遲佇列。

Toolbar 狀態
--------------
- View > 狀態資訊可選「關閉」、「簡易（預設）」或「詳細」。
- 簡易固定顯示連線、實際 FPS 與傳輸進度；詳細會多一條薄診斷列。
- 底部長駐狀態列已移除；錯誤以短暫提示顯示。

鍵盤鎖定、文字剪貼簿、快捷鍵與游標
----------------------------------
- 在遠端畫面內按一下才會進入「遠端輸入」；畫面會顯示 2 px 青色細框，Toolbar 顯示目前是「遠端輸入」或「本機操作」。
- 點 Toolbar／對話框或切換到其他 Windows 視窗會立即回到本機並釋放遠端按鍵；滑鼠移出畫面本身不解除鎖定。重新點遠端畫面即可繼續控制。
- 鎖定「遠端輸入」後，實體鍵盤的所有按鍵都以 scan code 送到被控端，包括 F1～F12、Home、End、方向鍵、Tab、左右 Ctrl／Alt／Shift、Right Alt、Caps Lock、Num Lock、F11、Esc、Alt+Tab 與 Windows 鍵。
- 鎖定期間唯一留在本機的是實體 Ctrl+Alt+Delete；要送遠端 Ctrl+Alt+Delete，請明確按 Toolbar 的同名按鈕。不要以自動化測試安全桌面。
- 因為 F11、Esc、Alt+Tab 也會送遠端，解除鎖定請用滑鼠點 Toolbar／本機對話框，或切換到其他 Windows 視窗；失焦、斷線與關閉都會 release-all。
- 控制端不再傳送本機輸入法完成後的 Unicode 組字；標準英文實體鍵盤直接送 scan code，由被控端自己的鍵盤配置、Caps Lock 與輸入法解讀。
- 被控端每次核准配對時可允許純文字剪貼簿；預設勾選，但仍須由本人按下允許。
- Toolbar「剪貼簿」可選關閉、單向（主控到被控）或雙向，預設雙向。
- 只要在一端複製／剪下純文字，就能在另一端直接按 Ctrl+V；圖片、HTML、RTF、檔案及資料夾不會背景同步。
- 文字以 256 KB 分塊、SHA-256 驗證，UTF-8 上限 64 MB；關閉、斷線或結束程式會清除同步快取，但不清除 Windows 原生剪貼簿。
- 工具列有 Ctrl+Alt+0，可直接送到遠端目前作用中的應用程式。
- 「自訂按鍵」選單可新增、送出與移除最多 20 組本機快捷鍵；設定保存在目前 Windows 使用者的 LocalAppData。
- Windows 鍵、Ctrl+Alt+Delete、Alt+F4、Ctrl+Escape 不接受為自訂項目。
- 被控端原生游標會合成在畫面中；控制端游標是 15% 填色、70% 白色邊框、縮小 20% 的空心幽靈箭頭，不顯示光圈或文字標籤，停止 350 ms 後淡出。

檔案傳輸
--------
1. 發起端連線前勾選「要求啟用雙向檔案傳輸」，接收端配對時再勾選允許；兩者都同意才會啟用，斷線後授權自動失效。
2. 可把本機檔案／資料夾拖進遠端畫面；檔案不走 Ctrl+C／Ctrl+V 文字剪貼簿，請一律使用拖放或 Toolbar「檔案」。
3. 任一端都可從頂端 Toolbar「檔案」開啟同一主視窗內的檔案面板，選本機來源、瀏覽對方磁碟／資料夾並主動傳送或接收。
4. 傳送與接收各有獨立狀態列，可以同時執行；返回遠端桌面後仍在背景繼續，Toolbar 會顯示進度與取消入口。
5. 拖放時畫面會顯示遠端桌面或目前檔案總管路徑；無法可靠辨識時，會開啟 Toolbar 使用的檔案面板讓你選路徑。
6. 遠端檔案總管複製檔案後，選 File >「接收遠端剪貼簿檔案」，再選本機接收路徑。
7. 同名預設保留兩者並自動改為「檔名 (1)」；衝突提示也可明確選擇覆寫或略過。
8. 單檔上限 2 GB、單批 10 GB，完成後驗證 SHA-256。
9. 取消時可選擇刪除 partial，或保留供同一批路徑下次續傳。
10. 本機來源與對方檔案清單的選取列使用深藍綠高亮與白字，作用中或失焦後都應保持檔名可讀。

啟用 Ctrl+Alt+Delete（選用）
---------------------------
1. 只在需要接受 Ctrl+Alt+Delete 的被控端安裝；若兩臺交換角色後都要接受，兩臺都要各裝一次。
2. 保持整個解壓縮資料夾在固定位置，不要安裝後再移動或刪除 service 子資料夾。
3. 用系統管理員 PowerShell 執行：
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install-sas-service.ps1
4. 腳本會安裝並啟動 LanRemoteSas 自動啟動服務，但不會修改 Windows 安全性原則。
5. 若按工具列 Ctrl+Alt+Delete 仍未切換安全桌面，由你本人開啟本機群組原則：
   Computer Configuration > Administrative Templates > Windows Components > Windows Logon Options
   將 "Disable or enable software Secure Attention Sequence" 設為允許 Services。
6. 不再使用時，以系統管理員 PowerShell 執行：
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall-sas-service.ps1

Windows 11 控制 Windows 10
--------------------------
1. Windows 10：按「開始等候」，記下顯示的 IPv4:45873。
2. Windows 11：在「控制另一臺電腦」輸入該位址，按「連線並核對」。
3. 比對兩端六位數配對碼完全相同；發起端與接收端兩個檔案傳輸選項都開啟後再核准。
4. 在遠端畫面內按一下，再測試滑鼠、滾輪與鍵盤。
5. 任一端需要停止時，按「立即斷線」或「停止被控端」。

Windows 10 反向控制 Windows 11
------------------------------
1. 先結束上一個 session。
2. Windows 11 改按「開始等候」。
3. Windows 10 輸入 Windows 11 顯示的 IPv4:45873 並連線。
4. 重新核對六位數配對碼並由 Windows 11 明確核准。

留下兩機測試證據
------------------
完成每個方向的五分鐘測試後，在該臺電腦的 PowerShell 執行同資料夾內的
New-TwoPcEvidence.ps1。範例：

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\New-TwoPcEvidence.ps1 `
  -Direction Windows11-controls-Windows10 -Result PASS -PeerComputer 另一臺名稱 `
  -DurationSeconds 300 -ObservedFps 48 -P95InteractionLatencyMs 180 `
  -PairingCodesMatched $true -MousePassed $true -KeyboardPassed $true `
  -PhysicalKeysPassed $true -HostImePhysicalKeysPassed $true `
  -InputCaptureSwitchingPassed $true -RightAltHotkeyPassed $true `
  -AllKeysRemoteExceptLocalSasPassed $true -LocalInputRestoredAfterClickPassed $true `
  -StuckKeyReleasePassed $true `
  -ClipboardControllerToHostPassed $true -ClipboardHostToControllerPassed $true `
  -ClipboardModesPassed $true `
  -ImmediateDisconnectPassed $true -DragDropUploadPassed $true `
  -ToolbarUploadPassed $true -ToolbarDownloadPassed $true `
  -RemoteDesktopDisplayedPassed $true -TabsRemovedPassed $true `
  -ToolbarFileEntryPassed $true -ConnectionTimeoutDialogPassed $true `
  -LauncherContrastPassed $true -FileTransferWithoutRemoteDesktopPassed $true `
  -ControllerInitiatedFileTransferPassed $true -HostInitiatedFileTransferPassed $true `
  -ConcurrentBidirectionalFileTransferPassed $true -ConflictModesPassed $true `
  -FileClipboardPassed $true -ResumePassed $true -StatusModesPassed $true `
  -CursorPassed $true -FileSelectionReadablePassed $true `
  -Notes "主要螢幕、全鍵盤直通與雙向傳輸測試"

腳本會在 evidence 子資料夾產生 JSON。請把兩個方向、兩臺電腦的 JSON 複製回
專案 readygate/evidence-inbox；這些檔案包含電腦名稱與私人 IP，預設不進 Git。

已知限制
--------
- 一個 session 只允許單方向遠端控制；檔案傳輸則在已授權的同一 session 內允許雙方主動發起。
- 目前以 GDI/JPEG 相容模式傳畫面；WGC + H.264 效能基線尚未完成。
- 三種模式的 fps 是上限，不是對所有硬體與網路保證的實測值；GDI/JPEG 可能無法在較慢電腦達到上限。
- 只擷取主要螢幕；沒有多螢幕、音訊、圖片／格式化剪貼簿、剪貼簿歷史或持續資料夾同步。
- 檔案傳輸不會寫入 Windows、Program Files、ProgramData 或 Startup，不跟隨 reparse point／symlink／junction，不接受 UNC／ADS 或遠端刪除／執行要求。
- 不支援網際網路 relay、自動探索或無人值守連線。
- 一般鍵鼠不會繞過 UAC 或較高權限視窗（Windows UIPI 限制）。Ctrl+Alt+Delete 只經固定用途 SAS 服務，且是否切換安全桌面仍由 Windows 原則決定。
