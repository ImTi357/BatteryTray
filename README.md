# BatteryTray

[![Build Status](https://github.com/ImTi357/BatteryTray/actions/workflows/build-release.yml/badge.svg)](https://github.com/ImTi357/BatteryTray/actions/workflows/build-release.yml) [![Latest Release](https://img.shields.io/github/v/release/ImTi357/BatteryTray?label=Release&color=brightgreen)](https://github.com/ImTi357/BatteryTray/releases/latest) [![Total Downloads](https://img.shields.io/github/downloads/ImTi357/BatteryTray/total?label=Downloads&color=blue)](https://github.com/ImTi357/BatteryTray/releases) ![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?logo=windows) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Ứng dụng Windows nhẹ hiển thị trực tiếp biểu tượng và phần trăm pin của chuột, bàn phím không dây ngay trên thanh Taskbar.

## Giao diện thực tế

| Hiển thị trên Taskbar | Menu điều khiển & danh sách thiết bị |
| :---: | :---: |
| ![BatteryTray Taskbar](assets/taskbar.png) | ![BatteryTray Menu](assets/menu.png) |

## Tính năng chính
- Hiển thị trực tiếp `[Icon thiết bị + % pin]` cạnh khay hệ thống Windows 10/11 hoặc thu gọn thành icon trong System Tray.
- Hỗ trợ mọi thiết bị ngoại vi kết nối qua **Bluetooth** (chuẩn Windows PnP Battery Service).
- Hỗ trợ đọc phần trăm pin qua đầu thu **USB 2.4GHz** cho:
  - **ATK** (đầu thu Nearlink 8K `VID_373B&PID_108C`).
  - **Logitech** (đầu thu Lightspeed, Logi Bolt, Unifying `VID_046D` theo chuẩn HID++ 2.0).
  - **Razer** (đầu thu HyperSpeed / Wireless `VID_1532` theo giao thức OpenRazer 90-byte).
  - **Darmoshark, Ajazz, Zaopin, Delux** (nhóm đầu thu dùng chip Compx `VID_3554` / `VID_25A7`).
  - **Keychron, NuPhy & Bàn phím QMK/VIA** (`VID_3434`, `VID_362D` qua kênh Raw HID `0xFF60:0x61`).
  - **Tai nghe không dây (SteelSeries, HyperX, Corsair, Logitech G, Razer)** qua đầu thu USB 2.4GHz và tai nghe Bluetooth (AirPods, Sony, JBL, Marshall...).
  - Các hãng khác sẽ tiếp tục được cập nhật trong những phiên bản sau.
- Xây dựng theo kiến trúc mô-đun `IBatteryProvider`, thuận tiện để mở rộng thêm giao thức của các hãng mới.

## Cài đặt
Tải phiên bản mới nhất tại mục **[Releases](https://github.com/ImTi357/BatteryTray/releases/latest)** và chọn **1 trong 2 tệp** sau:
- **`BatteryTray_Setup_v*.exe` (Khuyên dùng):** Bộ cài chuẩn cho Windows 10/11, tự tạo lối tắt, hỗ trợ khởi động cùng Windows và gỡ cài đặt gọn gàng.
- **`BatteryTray.exe` (Portable):** Bản chạy trực tiếp không cần cài đặt.

### Lưu ý khi Windows hiện cảnh báo SmartScreen
Vì **BatteryTray** là dự án mã nguồn mở cá nhân và chưa mua chứng chỉ chữ ký số thương mại (Code Signing Certificate), Windows có thể hiện màn hình xanh **"Windows bảo vệ máy tính của bạn"** ở lần mở đầu tiên:
1. Bấm vào dòng chữ **Thông tin thêm** (*More info*).
2. Bấm nút **Vẫn chạy** (*Run anyway*).

> Toàn bộ tệp phát hành đều được biên dịch tự động trên **GitHub Actions** trực tiếp từ mã nguồn công khai trong kho chứa này. Bạn có thể kiểm tra kết quả quét **VirusTotal** đính kèm trong từng bản **Release Notes**.
## Yêu cầu hỗ trợ chuột / bàn phím / tai nghe mới
Nếu thiết bị không dây của bạn chưa hiển thị pin qua đầu thu USB 2.4GHz:
1. Mở menu **BatteryTray** trên thanh Taskbar -> chọn **Export device diagnostic log (GitHub)**.
2. Mở một **Issue** mới trên GitHub và dán nội dung log vừa xuất (Ctrl + V) kèm tên hãng, tên model thiết bị.

