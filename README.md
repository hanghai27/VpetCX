# VpetCX

VpetCX là mod mở rộng cho VPet, gồm Core, giao diện quản lý và ba plugin đi kèm: Diary, Reminder, Weather. Core và các cửa sổ của mod giao tiếp qua HTTP API chỉ lắng nghe trên địa chỉ loopback `127.0.0.1`.

## Tính năng

- Tích hợp vòng đời của mod với VPet; khởi tạo tài nguyên khi game tải và dọn dẹp khi game kết thúc.
- Quản lý plugin: xem, bật/tắt plugin và chọn ngôn ngữ.
- Mở các cửa sổ Diary, Reminder và Weather từ menu DIY khi plugin tương ứng được bật.
- Tự động nạp plugin .NET đặt trong thư mục `plugin`.
- Lưu cấu hình bằng LiteDB; Diary và Reminder lưu dữ liệu riêng bằng JSON.
- Tải thời tiết hiện tại và dự báo 4 ngày từ Open-Meteo.
- Hiển thị thông báo trên VPet trong 4 giây khi HTTP API khởi động thành công.
- Cho phép thêm bản dịch bằng tên thư mục tùy chọn.

## Yêu cầu và tương thích

- Windows và .NET 8 SDK để build.
- Metadata mod khai báo `gamever#11074` trong `VpetCX.Mod/info.lps`.
- Mã nguồn build với gói `VPet-Simulator.Windows.Interface` phiên bản `1.1.0.50`. Đây là phiên bản gói API dùng lúc biên dịch; `gamever` là trường metadata riêng của mod.
- Weather cần kết nối Internet để truy vấn dịch vụ Open-Meteo.
- LiteDB được đóng gói dưới dạng assembly .NET. Dự án không dùng SQLite hoặc thư viện database native.

## Plugin đi kèm

| Plugin | Chức năng |
|---|---|
| Diary | Thêm, liệt kê, tìm kiếm và xóa mục nhật ký cục bộ. |
| Reminder | Tạo, sửa, xem và xóa hẹn giờ; VPet đọc nội dung khi đến hạn. |
| Weather | Tìm địa điểm và xem thời tiết hiện tại hoặc dự báo 4 ngày. |

Diary hiển thị tối đa 10 mục mới nhất mỗi lần xem/tìm kiếm. Reminder có thể tạo tối đa 100 mục; thời gian hẹn từ 1 giây đến 365 ngày và nội dung tối đa 500 ký tự. Nếu VPet chưa nhận được lời nhắc khi đến hạn, plugin sẽ thử lại sau mỗi 15 giây. Lời nhắc đã gửi vẫn được giữ trong danh sách; khi plugin khởi động lại, trạng thái đã gửi được đặt lại và lời nhắc quá hạn có thể được phát lại.

Weather hiện tại gồm điều kiện thời tiết, nhiệt độ, cảm giác như, độ ẩm, lượng mưa và gió. Dự báo gồm điều kiện, nhiệt độ thấp/cao và xác suất mưa nếu Open-Meteo cung cấp dữ liệu. Cửa sổ Weather cho phép tìm và chọn địa điểm, chọn loại truy vấn và bật/tắt đọc kết quả bằng VPet.

## Dữ liệu và cấu hình

Dữ liệu được lưu bên dưới thư mục Documents của người dùng, trong `VpetCX`:

| Thành phần | Đường dẫn tương đối | Nội dung |
|---|---|---|
| Core | `settings.db` | Cấu hình LiteDB: ngôn ngữ và danh sách plugin đã tắt |
| Diary | `Diary\diary.json` | Các mục nhật ký |
| Reminder | `Reminder\daily-reminders.json` | Các lời nhắc và trạng thái gửi |

Đường dẫn đầy đủ thường là `%USERPROFILE%\Documents\VpetCX`; nếu Windows chuyển hướng Documents, dữ liệu sẽ nằm trong Documents thực tế mà hệ điều hành trả về. Diary giới hạn 2.000 mục, tối đa 4.000 ký tự mỗi mục và tệp 12 MiB. Reminder giới hạn tệp dữ liệu 10 MiB. Giới hạn kích thước tệp được kiểm tra trước khi đọc.

## HTTP API cục bộ

Sau khi game tải xong, Core mở `http://127.0.0.1:52815/`. API dừng khi game kết thúc. Khi mở thành công, VPet hiển thị thông báo khởi động trong 4 giây bằng ngôn ngữ đang chọn.

API chỉ nhận `POST` từ loopback và yêu cầu `Content-Type: application/json`. Kích thước body tối đa 16 KiB; tối đa 8 request được xử lý đồng thời; mỗi request có timeout 45 giây. `/say` nhận văn bản dài từ 1 đến 2.000 ký tự. API không có xác thực hoặc TLS và chỉ được bind vào `127.0.0.1`; không cấu hình chuyển tiếp cổng hoặc mở dịch vụ này ra mạng.

| Endpoint | Request JSON | Tác dụng |
|---|---|---|
| `/say` | `{"text":"Xin chào"}` | Yêu cầu VPet nói văn bản. |
| `/open_manager` | `{}` | Mở cửa sổ quản lý VpetCX. |
| `/open_diary` | `{}` | Mở cửa sổ Diary nếu plugin đang bật. |
| `/open_reminders` | `{}` | Mở cửa sổ Reminder nếu plugin đang bật. |
| `/open_weather` | `{}` | Mở cửa sổ Weather nếu plugin đang bật. |
| `/diary` | `{"action":"write","text":"Nội dung"}` | Thực hiện thao tác Diary: `view`, `list`, `write`, `search` hoặc `delete`. |
| `/reminders` | `{"action":"save","time":10,"unit":"minutes","event":"Nghỉ giải lao"}` | Liệt kê, tạo/sửa hoặc xóa lời nhắc. |
| `/weather/search` | `{"city":"Hanoi"}` | Tìm địa điểm theo tên. |
| `/weather` | `{"city":"Hanoi","type":"current"}` | Lấy thời tiết `current` hoặc `forecast` cho thành phố/địa điểm. |

### Tham số API plugin

- `/diary`: `action` mặc định là `view`; `list` tương đương `view`. Dùng `text` cho `write` hoặc `search`, và `id` cho `delete`. Khi xóa, có thể dùng tiền tố ID; nếu tiền tố khớp nhiều mục thì cần cung cấp ID dài hơn.
- `/reminders`: `action` là `list`, `save` hoặc `delete` và mặc định là `list`. `save` cần `time` là số nguyên dương, `unit` là `seconds`, `minutes` hoặc `hours`, và `event` không quá 500 ký tự. Có thể gửi thêm `id` để cập nhật lời nhắc hiện có; không gửi `id` để tạo mới. `delete` cần `id`.
- `/weather/search`: `city` là tên cần tìm; kết quả là danh sách địa điểm được tuần tự hóa JSON.
- `/weather`: `city` là bắt buộc; `type` mặc định là `current`. Có thể truyền `name`, `admin1`, `admin2`, `country`, `latitude`, `longitude` cùng `city` để truy vấn địa điểm đã chọn từ kết quả tìm kiếm.

Các endpoint mở cửa sổ trả trạng thái `opened`; `/say` trả trạng thái `spoken`. Endpoint plugin thường trả `{"result":"..."}`. Với `/weather/search`, `result` chứa chuỗi JSON danh sách địa điểm; với `/reminders`, `result` chứa chuỗi JSON về danh sách hoặc kết quả thao tác. Vì vậy, các giá trị này là chuỗi JSON lồng trong phản hồi JSON ngoài. Lỗi giao thức HTTP trả trường `error`; lỗi kiểm tra dữ liệu hoặc lỗi dịch vụ của plugin có thể nằm trong `result` dù HTTP request đã được xử lý.

Ví dụ tạo lời nhắc sau 10 phút bằng PowerShell:

```powershell
curl.exe -X POST http://127.0.0.1:52815/reminders `
  -H "Content-Type: application/json" `
  -d '{"action":"save","time":10,"unit":"minutes","event":"Nghỉ giải lao"}'
```

## Bản dịch

Tài nguyên LPS của Core và các plugin đi kèm nằm trong `VpetCX.Core\language\<tên-thư-mục>\`. Các ngôn ngữ hiện có là `en`, `vi`, `ja`, `zh-hans` và `zh-hant`; mỗi thư mục hiện có `system.lps`, `interface.lps`, `diary.lps`, `reminder.lps` và `weather.lps`.

Có thể dùng tên thư mục tùy ý, kể cả tên locale như `ko` hoặc tên bản địa như `한국어`. Bộ nạp dùng nguyên tên thư mục làm mã ngôn ngữ; thư mục cần có ít nhất một tệp `.lps`. Tên hiển thị lấy từ khóa `language_name` trong `interface.lps`; nếu không có, giao diện dùng tên thư mục. Khóa dịch thiếu sẽ được tìm trong tiếng Anh (`en`), nếu vẫn thiếu thì khóa sẽ được hiển thị nguyên văn.

Để thêm ngôn ngữ:

1. Tạo thư mục mới trong `VpetCX.Core\language`, ví dụ `ko`.
2. Thêm các tệp `.lps` chứa bản dịch. Giữ nguyên tên khóa và các placeholder như `{0}`.
3. Thêm khóa `language_name#한국어:|` vào `interface.lps` để đặt tên hiển thị.
4. Khởi động lại VPet và chọn ngôn ngữ mới trong cài đặt VpetCX.
( người dùng, chỉ cần copy thư mục ngôn ngữ vào plugin/language/ và khởi động lại game là được).

Tên thư mục được lưu nguyên văn trong cấu hình. Nếu đổi tên thư mục ngôn ngữ, hãy chọn lại ngôn ngữ trong giao diện.

## Cấu trúc repository

```text
VpetCX/
├── VpetCX.Abstractions/       # Hợp đồng plugin, cài đặt và đường dẫn dữ liệu
├── VpetCX.Core/
│   ├── CoreLoader.cs          # Nạp plugin, bản dịch và cấu hình LiteDB
│   ├── LocalApi.cs            # HTTP API loopback và các endpoint
│   ├── VpetCXCore.cs          # Plugin entry point và vòng đời VPet
│   ├── Windows/               # Các cửa sổ WPF
│   └── language/              # Tài nguyên bản dịch LPS
├── VpetCX.Mod/                # Metadata mod (info.lps)
├── DiaryPlugin/
├── ReminderPlugin/
├── WeatherPlugin/
└── VpetCX.sln
```

## Phát triển plugin

1. Tạo project thư viện .NET 8 và tham chiếu `VpetCX.Abstractions`.
2. Tạo lớp kế thừa `VPetPlugin`; triển khai `Name`, `Author`, `Description`, `Parameters`, `Examples`, `Initialize`, `Function` và `Unload`.
3. Thêm project vào `VpetCX.sln` và thêm project reference với `ReferenceOutputAssembly="false"` vào `VpetCX.Core.csproj`.
4. Thêm assembly plugin vào target `PackageVpetCXMod` để được đóng gói vào `Release\VpetCX\plugin`.
5. Nếu plugin cần endpoint HTTP, thêm endpoint chuyên biệt với request/response rõ ràng vào `LocalApi`; không tạo endpoint chạy lệnh tùy ý.

Plugin được nạp từ các DLL trong thư mục `plugin` khi VpetCX khởi động. Bản dịch cho plugin có thể đặt trong các thư mục locale tương ứng dưới `plugin\language`.
