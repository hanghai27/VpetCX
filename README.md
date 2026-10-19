# VpetCX - Hệ thống Plugin mở rộng cho VPet

VpetCX là một dự án mã nguồn mở, cung cấp nền tảng (Core) để quản lý và mở rộng các tính năng cho tựa game VPet Simulator.

## Cấu trúc thư mục
- `plugin/`: Thư mục ch���a các file .dll (Runtime)
- `src/Interface/`: Project chứa các interface cho plugin (Interface.dll)
- `src/Reminder/`: Mẫu plugin Reminder (sẽ build ra Reminder.dll)

## Cách cài đặt (người dùng)
1. Tải file zip từ Releases hoặc clone repo này.
2. Giải nén/thả thư mục `VpetCX` vào thư mục `mod` của VPet.
3. Đảm bảo trong `VpetCX/plugin/` có `Core.dll` và `Interface.dll` cùng các plugin `.dll`.
4. Khởi động lại game.

## Hướng dẫn phát triển (developer)
- Yêu cầu: .NET 6 SDK hoặc mới hơn (dotnet 6+).
- Cấu trúc source:
  - `src/Interface/` — Project `Interface` (xuất ra `plugin/Interface.dll`).
  - `src/Reminder/` — Mẫu plugin `Reminder` (tham chiếu project `Interface`).

Xây dựng:
- `dotnet build src/Interface/Interface.csproj -c Release`
- `dotnet build src/Reminder/Reminder.csproj -c Release`

Lưu ý: các csproj mẫu cấu hình OutputPath để bản build đưa file .dll vào `plugin/`.

## Giấy phép
- Apache License 2.0

