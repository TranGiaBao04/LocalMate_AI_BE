# Bàn giao FE: lịch trình ban đêm + giờ mở cửa địa điểm

Cập nhật 2026-10-16. Gom mọi thay đổi API của đợt "chế độ ban đêm" (BE Phần 2–6).

**Tóm tắt một dòng:** lịch trình giờ được đi qua nửa đêm, nhưng hệ thống chỉ xếp những nơi **đang mở cửa** lúc tham quan; admin nhập giờ mở cửa cho từng địa điểm; đi Metro mà lúc bắt đầu không có tàu thì bị chặn.

> ⚠️ **Hiện chưa địa điểm nào có giờ mở cửa.** Nơi chưa có giờ mở cửa chỉ được xếp trong khung **06:00–22:00**, nên cho tới khi admin nhập giờ cho các quán mở khuya, lịch buổi tối vẫn dừng khoảng 22:00 và lịch bắt đầu sau ~21:00 thường nhận lỗi `no_places_open`. Đây là hành vi đúng, không phải lỗi.

---

## Mục lục

1. [Tạo lịch: bỏ giới hạn "không quá 24:00"](#1-tạo-lịch-bỏ-giới-hạn-không-quá-2400)
2. [Tạo lịch: hai lý do / mã lỗi mới](#2-tạo-lịch-hai-lý-do--mã-lỗi-mới)
3. [Chi tiết lịch: chặng hôm sau + trạng thái mở cửa](#3-chi-tiết-lịch-chặng-hôm-sau--trạng-thái-mở-cửa)
4. [Thay địa điểm: đổi mã lỗi](#4-thay-địa-điểm-đổi-mã-lỗi)
5. [Admin: nhập giờ mở cửa](#5-admin-nhập-giờ-mở-cửa)
6. [Admin: import địa điểm](#6-admin-import-địa-điểm)
7. [Checklist cho FE](#7-checklist-cho-fe)

---

## 1. Tạo lịch: bỏ giới hạn "không quá 24:00"

Áp dụng cho `POST /api/trips/generate`, `/feasibility-check`, `/match`, `/fallback-itinerary`, `POST /api/itineraries/curated/{id}/apply` và AI `POST /api/trips/parse-request`.

| | Trước | Bây giờ |
|---|---|---|
| `startTime` + `durationHours` | phải ≤ 24:00, vượt ⇒ 400 field `DurationHours` | **không giới hạn giờ kết thúc**, chỉ còn `durationHours` 1–24 |
| `GET /api/master-data` → `timeSlots[].maxDurationHours` | sáng 16 / chiều 11 / tối 6 | **24** cho mọi khung |
| AI đọc câu "xuất phát 22h đi 5 tiếng" | giữ giờ, bỏ số giờ | **giữ cả hai** |
| Apply lịch mẫu bắt đầu 22:00 | 400 field `StartTime` | tạo được |

**FE cần làm:** bỏ đoạn tự giới hạn số giờ theo giờ bắt đầu ở form tạo lịch (ví dụ "bắt đầu 20:00 thì tối đa 4 giờ") và bỏ thông báo lỗi tương ứng.

Apply lịch mẫu vẫn còn một trường hợp lỗi 400 field `StartTime` với câu "Lịch trình không được dài quá 24 giờ.". Thực tế gần như không gặp, vì lịch mẫu dài tối đa 16 giờ.

---

## 2. Tạo lịch: hai lý do / mã lỗi mới

### 2.1. Không có nơi nào mở cửa — `no_places_open` / `NoPlacesOpen`

Có địa điểm vừa ngân sách và thời gian, nhưng **không nơi nào mở cửa** vào giờ đó.

| Endpoint | Cách trả |
|---|---|
| `POST /api/trips/generate` | **409** `code: "no_places_open"` |
| `/feasibility-check` | 200, `isFeasible: false`, `reason: "NoPlacesOpen"` |
| `/match` | 200, `isSufficient: false`, `insufficiencyReason: "NoPlacesOpen"` |
| `/fallback-itinerary` | 200, `isSufficient: false`, `insufficiencyReason: "NoPlacesOpen"` |

```json
{
  "type": "https://httpstatuses.com/409",
  "title": "No place is open at the planned time.",
  "status": 409,
  "instance": "/api/trips/generate",
  "code": "no_places_open"
}
```

Gợi ý câu hiển thị: *"Giờ này chưa có địa điểm nào đang mở cửa. Bạn thử chọn giờ bắt đầu sớm hơn nhé."*

Khác với `duration_too_short` / `DurationTooShort` (đã có từ trước): lỗi đó nghĩa là có nơi mở nhưng thời gian rảnh quá ngắn.

**Luật xếp lịch theo giờ mở cửa** (để FE giải thích cho user nếu cần):
- Nơi **có** giờ mở cửa: chỉ xếp khi cả lúc tham quan nằm trọn trong giờ mở. Ví dụ quán đóng 02:00, tham quan 75 phút thì không xếp lúc 01:00.
- Nơi **chưa có** giờ mở cửa: chỉ xếp trong 06:00–22:00.
- Hệ thống không "đợi quán mở": lịch bắt đầu 05:00 mà chặng đầu tới trước 06:00 thì không xếp được.

### 2.2. Metro không chạy lúc bắt đầu — `metro_not_running` / `MetroNotRunning`

Chỉ khi `travelMode: "Metro"`: lúc `startTime`, **không có chuyến tàu nào rời ga lên trong 60 phút tới** (đêm khuya hoặc trước chuyến đầu).

| Endpoint | Cách trả |
|---|---|
| `POST /api/trips/generate` | **409** `code: "metro_not_running"` |
| `/feasibility-check` | 200, `isFeasible: false`, `reason: "MetroNotRunning"` |
| `/match`, `/fallback-itinerary` | 200, `insufficiencyReason: "MetroNotRunning"` |

```json
{
  "type": "https://httpstatuses.com/409",
  "title": "The Metro is not running at the start time; choose walking, motorbike or auto.",
  "status": 409,
  "instance": "/api/trips/generate",
  "code": "metro_not_running"
}
```

Gợi ý câu hiển thị: *"Metro không chạy vào giờ này. Bạn chọn Đi bộ, Xe máy hoặc Tự động nhé."*, kèm nút đổi phương tiện.

Giờ tàu theo **lịch của ngày đi**: hiện thứ 2–5 chuyến cuối khoảng 22:00, thứ 6–CN khoảng 23:00, chuyến đầu khoảng 05:00. Lịch tàu là số giữ chỗ, có thể đổi.

**Bắt đầu lúc tàu còn chạy mà lịch kéo qua giờ tàu ngừng:** vẫn tạo lịch. Đoạn nào cần tàu sau chuyến cuối tự đổi sang xe máy, `items[].leg.fallback = "metro_unavailable"` (đã có từ trước). Nếu lịch kéo tới sáng hôm sau khi tàu chạy lại thì các đoạn đó lại đi tàu.

---

## 3. Chi tiết lịch: chặng hôm sau + trạng thái mở cửa

Áp dụng cho mọi response `TripDetailResponse`: `GET /api/trips/{id}`, 201 của `POST /api/trips/generate`, 201 của apply lịch mẫu.

### 3.1. Field mới

| Field | Kiểu | Ý nghĩa |
|---|---|---|
| `endsNextDay` | bool | Chặng cuối kết thúc **từ 00:00 của ngày hôm sau** trở đi. Kết thúc đúng 24:00 cũng là `true` (`endTime` khi đó là `"00:00:00"`). |
| `items[].isNextDay` | bool | Chặng **bắt đầu** sau 00:00 của ngày hôm sau ngày đi (`plannedDate`). |
| `items[].openingStatus` | `"Open"` \| `"Closed"` \| `"Unknown"` | Địa điểm có mở cửa suốt lúc tham quan chặng này không. |

`endTime`, `items[].scheduledTime` vẫn là giờ trong ngày (`"HH:mm:ss"`), không có ngày. FE dùng hai cờ trên để ghi "(hôm sau)".

### 3.2. `openingStatus`

Tính **mỗi lần xem lịch** theo giờ mở cửa **hiện tại** (không lưu), nên có thể đổi nếu admin sửa giờ hoặc user sửa lịch.

| Giá trị | Khi nào | Gợi ý hiển thị |
|---|---|---|
| `Open` | nằm trọn trong giờ mở cửa | không cần hiện gì (hoặc nhãn xanh nhỏ "Đang mở") |
| `Closed` | địa điểm có giờ mở cửa cho ngày đó nhưng lúc tham quan quán đóng (kể cả ngày nghỉ) | **cảnh báo đỏ**: "Địa điểm có thể đã đóng cửa vào giờ này. Bạn nên đổi hoặc xoá chặng." + nút Thay địa điểm |
| `Unknown` | chưa có giờ mở cửa, hoặc chặng rơi vào thứ admin chưa nhập, hoặc trip cũ chưa đặt ngày đi | hiện nhạt hoặc không hiện |

Hay gặp `Closed` sau khi user **xoá hoặc thay chặng** (các chặng khác dời giờ) hoặc khi **áp dụng lịch mẫu** vào giờ muộn. Hệ thống không tự bỏ chặng trong các trường hợp này, chỉ cảnh báo.

### 3.3. Ví dụ (lấy từ API thật)

Lịch thứ Bảy 17/10/2026, xuất phát ga Bến Thành 22:00, 4 giờ, xe máy. Ba quán quanh ga được đặt giờ 17:00–03:00:

```json
{
  "plannedDate": "2026-10-17",
  "startTime": "22:00:00",
  "endTime": "01:18:00",
  "endsNextDay": true,
  "totalMinutes": 198,
  "items": [
    { "scheduledTime": "22:01:00", "estimatedDurationMinutes": 60, "isNextDay": false, "openingStatus": "Open", "travelMinutesFromPrevious": null },
    { "scheduledTime": "23:02:00", "estimatedDurationMinutes": 75, "isNextDay": false, "openingStatus": "Open", "travelMinutesFromPrevious": 1 },
    { "scheduledTime": "00:18:00", "estimatedDurationMinutes": 60, "isNextDay": true, "openingStatus": "Open", "travelMinutesFromPrevious": 1 }
  ]
}
```

(Đã lược bớt các field khác của response.)

Hiển thị gợi ý:
- Khung giờ đầu trang: **"22:00 – 01:18 (hôm sau)"**.
- Chặng 3: **"00:18 (hôm sau)"**, hoặc chèn một dòng phân cách "Sang ngày Chủ nhật 18/10" trước chặng đầu tiên có `isNextDay: true`.

### 3.4. Mail lịch trình

BE tự ghi "(hôm sau)" trong mail khi chốt lịch. FE không phải làm gì.

---

## 4. Thay địa điểm: đổi mã lỗi

`PUT /api/trips/{tripId}/items/{itemId}/replace`

| Trước | Bây giờ |
|---|---|
| 409 `replacement_crosses_midnight` khi lịch sau khi thay vượt 24:00 | **bỏ** — lịch được đi qua nửa đêm |
| — | 409 **`replacement_too_long`** khi lịch sau khi thay **dài quá 24 giờ** tính từ chặng đầu (rất hiếm) |

```json
{
  "status": 409,
  "title": "Replacing this place would make the itinerary longer than 24 hours.",
  "code": "replacement_too_long"
}
```

Cảnh báo `exceeds_duration` (lịch dài hơn số giờ rảnh) giữ nguyên.

Thay địa điểm **không** kiểm giờ mở cửa. Sau khi thay, gọi lại `GET /api/trips/{id}` để thấy `openingStatus` mới.

### 4.1. Gợi ý thay thế có trạng thái mở cửa

`GET /api/trips/{tripId}/items/{itemId}/alternatives?limit=` — mỗi gợi ý có thêm **`openingStatus`** (`"Open"` / `"Closed"` / `"Unknown"`), tính theo **khung giờ hiện tại của chặng đang thay** (giờ bắt đầu + thời lượng chặng). Thời lượng chặng giữ nguyên khi thay.

- Thứ tự: **`Open` trước, rồi `Unknown`, `Closed` luôn ở cuối**. Trong mỗi nhóm giữ thứ hạng cũ (khớp sở thích, gần ga…). `limit` được cắt **sau** khi xếp, nên nơi đang mở ở hạng thấp vẫn được gợi ý.
- Nơi `Closed` vẫn trả về (không bị bỏ). FE nên làm mờ + gắn nhãn "Có thể đã đóng cửa giờ này".
- Trip cũ chưa đặt ngày đi: mọi gợi ý `Unknown`, thứ tự như trước.
- Chỉ gần đúng: sau khi thay, giờ tới chặng đó có thể lệch vài phút vì đường đi khác. Kết quả chính xác xem ở `GET /api/trips/{id}` sau khi thay.

```json
[
  { "placeId": "…", "name": "Tonkin Specialty Coffee", "openingStatus": "Open", "matchScore": 0.5 },
  { "placeId": "…", "name": "Local Saigon Cafe - Bến Thành", "openingStatus": "Unknown", "matchScore": 0.5 },
  { "placeId": "…", "name": "Ben Thanh Market (Chợ Bến Thành)", "openingStatus": "Closed", "matchScore": 0.5 }
]
```

(Đã lược các field khác của gợi ý.)

---

## 5. Admin: nhập giờ mở cửa

Quyền `ManagePlaces`. Màn gợi ý: khối "Giờ mở cửa" trong trang sửa địa điểm, bảng 7 dòng Thứ 2 → Chủ nhật, mỗi dòng có ô giờ mở, ô giờ đóng, ô tick "Nghỉ cả ngày", và nút "Xoá hết giờ mở cửa".

### 5.1. `GET /api/admin/places/{placeId}/opening-hours`

Luôn trả **đủ 7 thứ**, Monday → Sunday:

```json
{
  "placeId": "f3630944-f3b5-4e8c-9b5b-4283bcfaba5a",
  "hasData": true,
  "days": [
    { "dayOfWeek": "Monday", "isClosed": true, "openTime": null, "closeTime": null },
    { "dayOfWeek": "Tuesday", "isClosed": false, "openTime": "17:00:00", "closeTime": "02:00:00" },
    { "dayOfWeek": "Wednesday", "isClosed": false, "openTime": null, "closeTime": null }
  ]
}
```

(Rút gọn: thực tế luôn có đủ 7 phần tử.)

- `hasData: false` = địa điểm chưa có giờ mở cửa nào. Mọi thứ đều là `isClosed: false` và giờ `null`.
- Thứ `isClosed: false` mà giờ `null` = **chưa nhập** cho thứ đó (không phải "mở cả ngày"). Hiện ô trống.
- `closeTime` nhỏ hơn `openTime` = **mở qua nửa đêm** (17:00 → 02:00 sáng hôm sau). Nên ghi chú cạnh ô: "Đóng sau nửa đêm: nhập giờ nhỏ hơn giờ mở".
- 404 `place_not_found`: địa điểm không có hoặc đã xoá.

### 5.2. `PUT /api/admin/places/{placeId}/opening-hours`

**Ghi đè cả tuần.** Gửi các thứ đã nhập. Thứ không gửi = chưa có dữ liệu. Gửi `"days": []` để xoá hết.

```json
{
  "days": [
    { "dayOfWeek": "Monday", "isClosed": true },
    { "dayOfWeek": "Tuesday", "isClosed": false, "openTime": "17:00", "closeTime": "02:00" },
    { "dayOfWeek": "Sunday", "isClosed": false, "openTime": "17:00", "closeTime": "02:00" }
  ]
}
```

- 200: trả cùng khuôn với GET (đủ 7 thứ).
- `dayOfWeek`: `"Monday"` … `"Sunday"` (tiếng Anh). Giờ gửi `"HH:mm"`.
- Không nên gửi dòng `isClosed: false` mà không có giờ: sẽ bị 400. Muốn để trống thứ đó thì bỏ hẳn dòng.

| Lỗi | Khi nào |
|---|---|
| 400 `invalid_opening_hours`, field `Days` | thiếu `days`, hoặc hơn 7 dòng |
| 400 `invalid_opening_hours`, field `Days[i].DayOfWeek` | thiếu thứ, hoặc một thứ bị lặp |
| 400 `invalid_opening_hours`, field `Days[i].IsClosed` | thiếu `isClosed` |
| 400 `invalid_opening_hours`, field `Days[i]` | nghỉ cả ngày mà vẫn có giờ; mở mà thiếu giờ mở/đóng; giờ mở bằng giờ đóng |
| 400 (không có `code`) | sai tên thứ hoặc sai dạng giờ (lỗi đọc JSON tự động) |
| 404 `place_not_found` | địa điểm không có / đã xoá |
| 401 / 403 | chưa đăng nhập / thiếu quyền `ManagePlaces` |

Ví dụ 400:

```json
{
  "title": "Opening hours data is invalid.",
  "status": 400,
  "errors": { "Days[1].DayOfWeek": ["Monday bị lặp lại."] },
  "code": "invalid_opening_hours"
}
```

Hai admin cùng lưu một địa điểm: lưu lần lượt, người lưu sau thắng.

---

## 6. Admin: import địa điểm

Đường dẫn và contract của `GET /api/admin/places/import-template`, `POST .../import/preview`, `POST .../import/commit` **không đổi**. Chỉ đổi cách đọc cột **"OpeningHours"** (giờ mở cửa) khi tạo địa điểm mới:

| Ô giờ trong file | Trước | Bây giờ |
|---|---|---|
| trống | tự điền 08:00–22:00 cả tuần | **không ghi giờ** (chưa có dữ liệu) |
| `06:00-21:00` | đúng | mở cả 7 ngày |
| `Mon-Sun 06:00-21:00` (dạng của file mẫu) | không đọc được ⇒ 08–22 | mở cả 7 ngày |
| `Tue-Sun 08:00-17:00` | không đọc được ⇒ 08–22 | thứ 3 → CN mở, **thứ 2 nghỉ** |
| `Fri-Mon 18:00-02:00` | không đọc được ⇒ 08–22 | thứ 6 → thứ 2 mở qua đêm, thứ 3–5 nghỉ |
| `Sat 07:00-11:00` | không đọc được ⇒ 08–22 | chỉ thứ 7 mở |
| `8h-22h`, `abc`… | 08–22 im lặng | không ghi giờ + **cảnh báo ở bước xem trước** |

Tên thứ dùng 3 chữ tiếng Anh: `Mon Tue Wed Thu Fri Sat Sun`.

Cảnh báo nằm trong `rows[].warnings` của response preview (cùng chỗ với cảnh báo nghi trùng):

```
Không đọc được giờ mở cửa '8h-22h'. Địa điểm sẽ được tạo không có giờ mở cửa.
```

Dòng có cảnh báo này **vẫn hợp lệ** (`isValid: true`) và vẫn được tạo khi commit, chỉ là chưa có giờ mở cửa. Dòng đó được tính vào `warningRowsCount`.

---

## 7. Checklist cho FE

**Màn tạo lịch**
- [ ] Bỏ giới hạn số giờ theo giờ bắt đầu; `maxDurationHours` giờ là 24 cho mọi khung.
- [ ] Xử lý 409 `no_places_open` (gợi ý chọn giờ sớm hơn).
- [ ] Xử lý 409 `metro_not_running` (gợi ý đổi phương tiện, có nút đổi nhanh).
- [ ] `feasibility-check` / `match`: hiện câu tương ứng cho `NoPlacesOpen`, `MetroNotRunning`.

**Màn chi tiết lịch**
- [ ] Ghi "(hôm sau)" cho chặng `isNextDay: true` và cho `endTime` khi `endsNextDay: true`.
- [ ] Cảnh báo đỏ cho chặng `openingStatus: "Closed"`, kèm nút Thay địa điểm.
- [ ] `Unknown` hiện nhạt hoặc không hiện.

**Thay địa điểm**
- [ ] Đổi `replacement_crosses_midnight` → `replacement_too_long`.
- [ ] Danh sách gợi ý: hiện `openingStatus`, làm mờ + gắn nhãn cho gợi ý `Closed` (luôn nằm cuối).

**Admin Portal**
- [ ] Khối "Giờ mở cửa" (bảng 7 thứ) trong trang sửa địa điểm, gọi GET/PUT `opening-hours`.
- [ ] Màn import: hiện cảnh báo giờ mở cửa trong `rows[].warnings`; cập nhật hướng dẫn định dạng cột giờ.
