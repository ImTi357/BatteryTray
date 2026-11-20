# BatteryTray

Ứng dụng Windows nhẹ hiển thị trực tiếp biểu tượng và phần trăm pin của chuột, bàn phím không dây ngay trên thanh Taskbar.

## Tính năng chính
- Hiển thị trực tiếp `[Icon thiết bị + % pin]` cạnh khay hệ thống Windows 10/11 hoặc thu gọn thành icon trong System Tray.
- Hỗ trợ mọi thiết bị ngoại vi kết nối qua **Bluetooth** (chuẩn Windows PnP Battery Service).
- Hỗ trợ đọc phần trăm pin qua đầu thu **USB 2.4GHz** cho:
  - **ATK** (đầu thu Nearlink 8K `VID_373B&PID_108C`).
  - **Logitech** (đầu thu Lightspeed, Logi Bolt, Unifying `VID_046D` theo chuẩn HID++ 2.0).
  - Các hãng chuột khác sẽ được cập nhật trong những phiên bản tiếp theo.
- Xây dựng theo kiến trúc mô-đun `IBatteryProvider`, thuận tiện để mở rộng thêm giao thức của các hãng mới.

## Cài đặt
Tải file cài đặt tại mục **Releases** bên phải và chạy trình cài đặt.
