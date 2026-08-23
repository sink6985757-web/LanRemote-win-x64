# WORK_ORDER_CONFIRMED

- id: `WO-LANREMOTE-V4-FILE-TRANSFER-UX-20260823`
- confirmed_by: Yulin
- confirmed_at: 2026-08-23
- baseline: `f97586d` (v3)
- target: protocol v4 local test candidate

## 目標

- 移除控制端游標光圈，保留無文字的青色／白邊箭頭並縮小至 80%。
- 在已配對且接收端明確允許的 session 內提供雙向檔案／資料夾傳輸。
- 提供 VM 式拖放、檔案剪貼簿貼上，以及 Toolbar 雙欄檔案傳輸視窗。
- 移除底部狀態列；Toolbar 狀態可切換「關閉／簡易（預設）／詳細」。

## 傳輸與安全邊界

- 目的地只能是遠端桌面、可靠解析的檔案總管目前資料夾，或使用者在傳輸視窗明確選擇的路徑。
- 無法可靠解析拖放位置時改開路徑選擇視窗，不猜測寫入位置。
- 單檔上限 2 GB、單批 10 GB、256 KiB 分塊、SHA-256 完成驗證，支援取消與斷線續傳。
- 同名自動改名，永不覆寫；拒絕路徑逸出、ADS、UNC、reparse point、symlink 與 junction。
- 不包含持續資料夾同步、文字／圖片剪貼簿、遠端刪除／執行、公網 relay 或無人值守收檔。

## 狀態顯示

- 簡易（預設）：連線、實際 FPS 與傳輸進度的常駐簡短摘要。
- 詳細：另展開一條薄診斷列，顯示目標 FPS、解析度、掉幀、frame 大小／年齡與最後訊息。
- 關閉：隱藏 Toolbar 摘要與詳細列；必要錯誤仍以短暫 toast 顯示。

## 已授權

- 修改 Protocol、Core、Windows、WPF App、測試、發包／證據腳本與專案文件。
- 升級協定 v4、建置 self-contained ZIP、回讀 hash／ZIP，並建立範圍限定的本機 Git commit。

## 未授權

- GitHub push／release／公開發布。
- 安裝服務、修改防火牆／Windows 安全性原則。
