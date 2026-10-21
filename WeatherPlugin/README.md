# WeatherPlugin

WeatherPlugin tra cứu địa điểm và dữ liệu thời tiết toàn cầu qua Open-Meteo. Plugin tìm tọa độ từ tên địa điểm bằng Geocoding API, sau đó truy vấn Forecast API. Không cần API key.

## Sử dụng trong VpetCX

Mở cửa sổ **Thời tiết** từ menu DIY trên thanh công cụ VPet. Nhập tên địa điểm, tìm kiếm và chọn kết quả, sau đó chọn thời tiết hiện tại hoặc dự báo. Cửa sổ có tùy chọn để VPet đọc kết quả.

Plugin cũng hỗ trợ định dạng tham số nội bộ:

```text
weather city(Hanoi), type(current)
weather city(Tokyo), type(forecast)
```

- `city`: tên địa điểm, bắt buộc, tối đa 120 ký tự.
- `type`: `current`, `forecast` hoặc `search`; mặc định là `current`.
- `current`: tình trạng, nhiệt độ, cảm giác như, độ ẩm, lượng mưa, tốc độ và hướng gió.
- `forecast`: tối đa 4 ngày, gồm tình trạng, nhiệt độ thấp/cao và xác suất mưa nếu API trả dữ liệu.
- `search`: trả danh sách địa điểm tìm được để giao diện cho phép chọn chính xác kết quả.

Nhiệt độ hiển thị theo °C, tốc độ gió theo km/h. Ngôn ngữ dữ liệu địa điểm được chọn dựa trên ngôn ngữ VpetCX khi Open-Meteo hỗ trợ.

## API loopback

Core cung cấp endpoint chuyên biệt cho plugin:

```http
POST http://127.0.0.1:52815/weather/search
Content-Type: application/json

{"city":"Hanoi"}
```

Để lấy thời tiết, gửi `POST /weather` với `city` và `type` là `current` hoặc `forecast`. Có thể gửi thêm `name`, `admin1`, `admin2`, `country`, `latitude`, `longitude` từ kết quả tìm kiếm để truy vấn địa điểm đã chọn. Phản hồi được bọc trong trường `result` của JSON API; với tìm kiếm, `result` chứa chuỗi JSON của danh sách địa điểm.

## API Open-Meteo và xử lý lỗi

- Geocoding: `https://geocoding-api.open-meteo.com/v1/search`
- Forecast: `https://api.open-meteo.com/v1/forecast`
- Thời gian chờ yêu cầu HTTP tối đa là 15 giây.
- Lỗi kết nối, timeout, phản hồi không hợp lệ hoặc không tìm thấy địa điểm được trả về dưới dạng thông báo lỗi.

## Build

Từ thư mục gốc của dự án:

```powershell
dotnet build WeatherPlugin\WeatherPlugin.csproj --configuration Release
```

Để build và đóng gói toàn bộ mod:

```powershell
dotnet build VpetCX.sln --configuration Release
```
