# BatteryTray

Ứng dụng Windows nhẹ hiển thị trực tiếp biểu tượng và phần trăm pin của chuột, bàn phím không dây ngay trên thanh Taskbar.

## Tính năng chính
- Hiển thị trực tiếp `[Icon thiết bị + % pin]` cạnh khay hệ thống Windows 10/11 hoặc thu gọn thành icon trong System Tray.
- Hỗ trợ mọi thiết bị ngoại vi kết nối qua **Bluetooth** (chuẩn Windows PnP Battery Service).
- Hỗ trợ đọc pin trực tiếp qua đầu thu **USB 2.4GHz Nearlink 8K** cho chuột **ATK X1** (`VID_373B&PID_108C`).
- Kiến trúc `IBatteryProvider` dễ dàng mở rộng thêm đầu thu của các hãng khác (Logitech, Razer...).

## Cài đặt
Tải file `BatteryTray_Setup_v1.0.exe` tại mục **Releases** bên phải và chạy trình cài đặt.
