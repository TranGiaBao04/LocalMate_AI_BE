# Task BE: API admin xem lượt dùng AI (LLM)

> Spec chốt 2026-10-13. Chỉ đọc, không migration. Chưa có mã task trong sheet.

## 1. Mục tiêu

Admin cần biết: user nào đã dùng bao nhiêu lượt AI, lượt nào lỗi, và hệ thống tốn bao nhiêu token. Hiện BE chỉ trả số lượt **hôm nay** của một user (`subscription.ai` trong `GET /api/admin/users/{userId}`); chưa có lịch sử theo ngày, chưa có danh sách từng lần gọi, chưa có tổng toàn hệ thống.

## 2. Phạm vi

Ba endpoint chỉ đọc:

| # | Endpoint | Quyền | Cache |
|---|---|---|---|
| A | `GET /api/admin/ai-usage/summary?from&to` | `ViewRevenue` | 5 phút |
| B | `GET /api/admin/users/{userId}/ai-usage?from&to` | `ManageUsers` | Không |
| C | `GET /api/admin/users/{userId}/ai-calls?page&pageSize&sortDirection&from&to&kind&status` | `ManageUsers` | Không |

**Không làm trong task này:**
- Lượt embedding (ghi chú khi tạo lịch, tìm kiếm theo nghĩa). Hiện không có bảng nào ghi lại; sẽ là task riêng.
- Quy đổi token ra tiền.
- Thêm index, thêm quyền mới, sửa hoặc xoá lượt.
- Sửa luồng cấp lượt AI (`AiUsageCoordinator`, `AiUsageAdmissionRepository`).

## 3. Định nghĩa dùng chung

**Một "lượt"** phải trùng với cách tính trần ngày ở `AiUsageAdmissionRepository.CountDailyAsync`:

1. Mọi dòng `AiUsageAdmissions` có `State` khác `Released`, xếp ngày theo cột `VietnamUsageDate`.
2. Cộng các dòng `LlmCallLogs` không có dòng admission nào trỏ tới (`LlmCallLogId`), xếp ngày theo `CreatedAt` đổi sang giờ Việt Nam.

**Trạng thái hiển thị (`status`):**

| Nguồn | `status` |
|---|---|
| Admission `Completed` | Theo `Outcome`: `Succeeded` / `ProviderFailed` / `InvalidOutput` |
| Admission `Abandoned` | `Abandoned` |
| Admission `Reserved` hoặc `DispatchAuthorized` | `InProgress` |
| Dòng `LlmCallLogs` cũ | Theo `Outcome` của log |

**Token, thời gian chạy, model:** lấy từ dòng `LlmCallLogs` gắn với lượt. Lượt `Abandoned` và `InProgress` không có log nên token = 0, `durationMs` và `model` = null.

**Thời điểm của một lượt:** `startedAt` = `AdmittedAt` (dòng log cũ: `CreatedAt`); `completedAt` = `CompletedAt` (dòng log cũ: `CreatedAt`; đang chạy: null).

## 4. Input

**A và B** dùng lại nguyên `DashboardDateRangeQuery` + `DashboardDateRangeQueryValidator`:

| Field | Kiểu | Bắt buộc | Giới hạn |
|---|---|---|---|
| `from` | `yyyy-MM-dd` | Không | Mặc định `to − 29`; ≤ `to` |
| `to` | `yyyy-MM-dd` | Không | Mặc định hôm nay giờ VN; không ở tương lai; khoảng ≤ 366 ngày |

**C** kế thừa `PagedQuery`:

| Field | Kiểu | Bắt buộc | Giới hạn |
|---|---|---|---|
| `page` | int | Không | 1–10000, mặc định 1 |
| `pageSize` | int | Không | 1–100, mặc định 20 |
| `sortDirection` | string | Không | `asc` / `desc`, mặc định `desc` theo `startedAt` |
| `from`, `to` | `yyyy-MM-dd` | Không | Không có mặc định (bỏ trống = mọi ngày); nếu gửi cả hai thì `from ≤ to` |
| `kind` | string | Không | `ParseRequest` / `Explain` |
| `status` | string | Không | `Succeeded` / `ProviderFailed` / `InvalidOutput` / `Abandoned` / `InProgress` |

`sortBy` chỉ nhận `startedAt`; `search` bị bỏ qua.

## 5. Logic

**A**
1. Validate khoảng ngày, điền mặc định.
2. Đọc cache theo khoá gồm `from` và `to` đã điền.
3. Tính: tổng, tách theo loại (đủ 2 loại kể cả khi bằng 0), theo ngày (đủ mọi ngày, ngày trống = 0), 10 user có nhiều lượt nhất (bằng nhau thì theo `userId`).
4. `activeUsers` = số user khác nhau có ít nhất một lượt.
5. `averageDurationMs` = trung bình trên các lượt có log; không có lượt nào thì null.
6. Không cache khi lỗi.

**B**
1. Validate khoảng ngày.
2. Kiểm user tồn tại; không có thì 404.
3. Tính tổng và theo ngày cho user đó.
4. `today` lấy từ `ISubscriptionService.GetMySubscriptionAsync(userId)` (phần `Ai`), để trùng `/subscription/me`.

**C**
1. Validate query.
2. Kiểm user tồn tại; không có thì 404.
3. Lọc theo ngày, loại, trạng thái; sắp theo `startedAt` rồi `id`; phân trang.

## 6. Business rule

- Tổng lượt hôm nay của một user ở B phải bằng `today.used`.
- Tính cả user đang bị khoá.
- Lượt `Released` không hiện ở bất kỳ endpoint nào.
- `failed` = `providerFailed + invalidOutput + abandoned`.

## 7. Output

**A — 200**
```json
{
  "from": "2026-09-14",
  "to": "2026-10-13",
  "totals": {
    "calls": 0, "succeeded": 0, "providerFailed": 0, "invalidOutput": 0,
    "abandoned": 0, "inProgress": 0,
    "inputTokens": 0, "outputTokens": 0,
    "averageDurationMs": null, "activeUsers": 0
  },
  "byKind": [
    { "kind": "ParseRequest", "calls": 0, "succeeded": 0, "inputTokens": 0, "outputTokens": 0 },
    { "kind": "Explain", "calls": 0, "succeeded": 0, "inputTokens": 0, "outputTokens": 0 }
  ],
  "days": [
    { "date": "2026-10-13", "calls": 0, "succeeded": 0, "failed": 0,
      "inputTokens": 0, "outputTokens": 0, "activeUsers": 0 }
  ],
  "topUsers": [
    { "userId": "…", "fullName": "…", "email": "…", "calls": 0, "inputTokens": 0, "outputTokens": 0 }
  ],
  "generatedAt": "2026-10-13T03:00:00Z"
}
```

**B — 200**
```json
{
  "userId": "…",
  "from": "2026-09-14",
  "to": "2026-10-13",
  "today": { "used": 3, "limit": 3, "resetAt": "2026-10-13T17:00:00Z" },
  "totals": {
    "calls": 0, "succeeded": 0, "providerFailed": 0, "invalidOutput": 0,
    "abandoned": 0, "inProgress": 0,
    "inputTokens": 0, "outputTokens": 0, "averageDurationMs": null
  },
  "days": [
    { "date": "2026-10-13", "calls": 0, "succeeded": 0, "failed": 0, "inputTokens": 0, "outputTokens": 0 }
  ],
  "generatedAt": "2026-10-13T03:00:00Z"
}
```

**C — 200** (`PagedResult`)
```json
{
  "items": [
    {
      "id": "…", "kind": "ParseRequest", "status": "ProviderFailed",
      "startedAt": "…", "completedAt": "…", "durationMs": 10012,
      "model": "gemini-3.5-flash-lite", "inputTokens": 0, "outputTokens": 0,
      "tripId": null
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1
}
```

**Không trả:** `FencingToken`, `FencingGeneration`, `ReservedUntil`, `RecoveryAfter`, `ResolvedPlanVersionId`, số tiền quy đổi.

## 8. Lỗi

| HTTP | `code` | Khi nào |
|---|---|---|
| 400 | `invalid_ai_usage_query` | Sai khoảng ngày (field `From` / `To`), `kind` hoặc `status` lạ, `page` / `pageSize` / `sortBy` / `sortDirection` sai |
| 401 | — | Không có token |
| 403 | — / `account_locked` | Thiếu quyền / admin bị khoá |
| 404 | `user_not_found` | `userId` không tồn tại (B, C) |

Lỗi 400 là `ValidationProblemDetails` theo field (tên field viết hoa chữ đầu như các API admin khác).

## 9. Case phải xử lý

| Case | Kết quả |
|---|---|
| A với 30 ngày mặc định | 200, đủ 30 dòng `days` |
| Không có lượt nào trong khoảng | 200; tổng 0, `topUsers: []`; C trả `items: []` |
| User chưa từng dùng AI (B, C) | 200 với số 0, không phải 404 |
| Lượt đang chạy lúc gọi | `InProgress`, token 0 |
| Lượt đã `Released` | Không xuất hiện |
| Dòng `LlmCallLogs` cũ không có admission | Được tính, đúng một lần |
| Dòng `LlmCallLogs` đã gắn admission | Không tính lần hai |
| Lượt lúc 23:30 giờ VN ngày 12 | Thuộc ngày 12 |
| Trang vượt trang cuối (C) | 200, `items: []`, `totalCount` đúng |
| DB lỗi | 500, A không cache kết quả lỗi |

## 10. Quy ước code phải theo

- Luồng: Controller → Service (Application) → Repository (Infrastructure). Service không dùng `AppDbContext`.
- Mã lỗi và message đặt ở Controller; Service trả enum trạng thái.
- Repository riêng `IAdminAiUsageRepository`. **Không thêm method vào `IAiUsageAdmissionRepository`, `ILlmCallLogRepository`, `IUserRepository`** (nhiều test đang fake các interface này).
- Controller riêng `AdminAiUsageController`, route `api/admin`, đặt `[HasPermission(...)]` trên từng action (A khác quyền với B và C).
- Phân trang dùng `PagedQuery`, `PagedResult<T>`, `ApplySort`, `ToPagedResultAsync` có sẵn.
- Cache dùng `IMemoryCache` như `AdminDashboardService`.
- Không ghi nội dung câu hỏi hay câu trả lời của AI vào log.

**Code tham khảo:**
- Định nghĩa một lượt: `LocalMateAI.Infrastructure/Repositories/AiUsageAdmissionRepository.cs` (`CountDailyAsync`).
- Khoảng ngày + thống kê: `AdminFeedbackService.GetSummaryAsync`, `AdminDashboardRepository.GetDailyRevenueAsync` (gom theo ngày giờ VN).
- Kiểm user tồn tại + lấy gói: `AdminUserService`.

## 11. Test bắt buộc

- Validator: từng luật của query C.
- Service: 404 khi user không có; A dùng cache và không cache khi lỗi; `today` lấy từ subscription service.
- Repository trên Postgres thật:
  - lượt `Released` bị loại;
  - log cũ được tính một lần, log đã gắn admission không tính hai lần;
  - ánh xạ đủ 5 trạng thái;
  - ranh giới ngày theo giờ VN;
  - tổng hôm nay của B bằng `CountDailyAsync`.

## 12. Tiêu chí nghiệm thu

1. Ba endpoint trả đúng khuôn ở mục 7 và mã lỗi ở mục 8.
2. Với một user thử: `today.used` ở B = `ai.dailyUsed` ở `/subscription/me` = số dòng hôm nay ở C.
3. Tổng `calls` của `days` ở A bằng `totals.calls`; tổng `calls` của `topUsers` không vượt `totals.calls`.
4. Không có migration mới; toàn bộ test cũ vẫn qua.
5. Đã gọi thử bằng tài khoản admin (200), tài khoản thường (403), không token (401).

## 13. Việc kéo theo cho FE

- Trang dashboard admin: thêm khối "Lượt dùng AI" gọi endpoint A.
- Trang chi tiết user: thêm tab hoặc khối gọi B và C.
