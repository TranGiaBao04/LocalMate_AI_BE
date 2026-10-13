# Task BE: Gói tặng (admin tặng tay) và chương trình dùng thử khi đăng ký

> Spec chốt 2026-10-13. Chưa có mã task trong sheet. Gồm hai phần làm theo thứ tự: **Phần A** (nền: gói tặng) rồi **Phần C** (chương trình dùng thử khi đăng ký). Phần C không chạy được nếu chưa có Phần A.

## 1. Mục tiêu

Giai đoạn đầu vận hành cần cho user trải nghiệm gói Trip Pass mà không phải thanh toán:

- Admin tặng một gói trả phí cho một user cụ thể trong N ngày.
- Admin mở một chương trình có ngày bắt đầu, ngày kết thúc và số người tối đa; ai đăng ký tài khoản trong khoảng đó thì tự động được Trip Pass N ngày.

Hiện chưa có API nào cho hai việc này.

## 2. Quyết định thiết kế đã chốt

- **Gói tặng lưu ở bảng riêng `PlanGrants`, KHÔNG lưu vào `SubscriptionPeriods`.** Bảng `SubscriptionPeriods` có ràng buộc `CK_Periods_Source` và nhiều trigger bắt mỗi kỳ phải gắn với một đơn đã thanh toán; tính năng nâng cấp gói và `repair-entitlement` cũng dựa trên giả định đó.
- **Chỗ duy nhất phải biết về gói tặng là `EffectiveSubscriptionResolver`.** Tạo lịch, chốt lịch, lượt AI và `/subscription/me` đều hỏi gói hiện tại qua đây nên tự nhận.
- **Luồng thanh toán không biết gói tặng.** User đang dùng thử mà mua gói thì được xếp là "mua mới" (`SubscriptionCheckoutPolicy.Classify` trả `Purchase` khi `effective.Period is null`); kỳ mua bắt đầu ngay, phần dùng thử còn lại coi như bỏ.
- **Không có thu hồi gói tặng.**
- **Không làm:** chương trình giới thiệu bạn bè, mã khuyến mãi, tặng lượt tạo lịch theo từng user, cấp bù cho tài khoản đã có từ trước, API công khai "còn bao nhiêu suất".

---

# Phần A: Gói tặng

## A1. Dữ liệu

Bảng mới `PlanGrants`:

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | Guid | |
| `UserId` | Guid | FK `Users`, Restrict |
| `PlanId`, `PlanVersionId` | Guid | FK ghép tới `SubscriptionPlanVersions` như `SubscriptionPeriods`; phiên bản hiện hành lúc tặng |
| `StartsAt`, `EndsAt` | giờ UTC | `StartsAt < EndsAt` |
| `Days` | int | Số ngày được tặng |
| `Source` | chuỗi | `Admin` / `SignupTrial` |
| `Reason` | chuỗi ≤ 500, null | Bắt buộc với `Admin` |
| `GrantedByUserId` | Guid, null | FK `Users`, SetNull; null với `SignupTrial` |
| `TrialProgramId` | Guid, null | FK `TrialPrograms` (Phần C) |
| `DeduplicationKey` | chuỗi ≤ 200, null, unique | Dùng cho nguồn tự động |
| `CreatedAt`, `UpdatedAt` | giờ UTC | |

Index `(UserId, EndsAt)`.

## A2. Xác định gói hiện tại (sửa `EffectiveSubscriptionResolver`)

1. Ứng viên = các kỳ đã mua đang hiệu lực (như hiện tại) + các gói tặng có `StartsAt ≤ now < EndsAt`.
2. Chọn ứng viên có `EntitlementPriority` cao nhất. Cùng một gói thì ưu tiên kỳ đã mua.
3. Không có ứng viên thì là Free, như hiện tại.

`EffectiveSubscription` khi gói đến từ gói tặng: `Period = null`, `PaidThrough = null`, `EffectiveUntil` = `EndsAt` của gói tặng, thêm cờ `IsTrial = true`.

Hệ quả cần biết:
- Gói tặng có `GenerateLimit` hữu hạn thì lượt tạo lịch đếm theo tháng như Free (vì `Period` là null). Trip Pass không giới hạn nên không ghi lượt.
- `POST /api/subscription/renew` với user đang dùng thử trả `NoActiveSubscription` (đúng: không có kỳ nào để gia hạn). User mua bằng `checkout`.

## A3. Endpoint

Quyền `ManagePlans`.

**`POST /api/admin/users/{userId}/grants`**

| Field | Kiểu | Bắt buộc | Giới hạn |
|---|---|---|---|
| `userId` (route) | Guid | Có | User tồn tại |
| `planId` | Guid | Có | Gói trả phí đang bật, có phiên bản hiện hành, không phải Free |
| `days` | int | Có | 1–90 |
| `reason` | string | Có | Trim, 1–500 ký tự |

Logic:
1. Validate body.
2. Mở transaction, khoá hàng `Users` của người nhận (`SELECT … FOR UPDATE`, cùng khoá với tạo lịch và lượt AI).
3. Kiểm user đang Active và gói hợp lệ.
4. `startsAt` = thời điểm hiện tại, hoặc thời điểm kết thúc phần phủ hiện có nếu muộn hơn. "Phần phủ" = các kỳ đã mua còn hiệu lực hoặc chưa bắt đầu, và các gói tặng chưa hết hạn, của gói có độ ưu tiên **bằng hoặc cao hơn** gói được tặng.
5. `endsAt = startsAt + days`.
6. `endsAt` vượt quá 365 ngày kể từ hiện tại thì từ chối.
7. Ghi `PlanGrants` (nguồn `Admin`, lý do, admin tặng) và thông báo trong app, cùng transaction.

**`GET /api/admin/users/{userId}/grants`**: mảng gói tặng của user, mới nhất trước.

## A4. Output

201 của `POST`, và mỗi phần tử của `GET`:
```json
{
  "grantId": "…", "userId": "…",
  "plan": { "code": "TRIP_PASS", "name": "Trip Pass" },
  "days": 7, "startsAt": "…", "endsAt": "…",
  "source": "Admin", "reason": "…",
  "grantedBy": { "userId": "…", "fullName": "…" },
  "createdAt": "…"
}
```
`grantedBy` và `reason` là null với nguồn `SignupTrial`.

Thay đổi ở API có sẵn:
- `GET /api/subscription/me` thêm `isTrial` (bool). Với user dùng thử: `plan` là gói được tặng, `endsAt` null, `effectiveUntil` là ngày hết dùng thử.
- `GET /api/admin/users` và `GET /api/admin/users/{id}`: gói hiện tại phải tính cả gói tặng; thêm `planIsTrial`.

## A5. Lỗi

| HTTP | `code` | Khi nào |
|---|---|---|
| 400 | `invalid_grant` | `days` ngoài 1–90, `reason` rỗng hoặc quá 500 ký tự (theo field); gói là Free, đang tắt, không tồn tại, chưa có phiên bản (field `PlanId`) |
| 404 | `user_not_found` | User không tồn tại |
| 409 | `user_locked` | User đang bị khoá |
| 409 | `grant_exceeds_max_horizon` | `endsAt` vượt 365 ngày |
| 401 / 403 | — | Không token / thiếu quyền |

## A6. Case phải xử lý

| Case | Kết quả |
|---|---|
| User Free, tặng Trip Pass 7 ngày | 201; `/subscription/me` ra Trip Pass, `isTrial: true` |
| User đang có Trip Pass đã mua tới 20/10 | 201; gói tặng bắt đầu 20/10 |
| User đang có Membership, tặng Trip Pass | 201; gói tặng bắt đầu khi Membership hết |
| User đang có gói tặng Trip Pass, tặng thêm | 201; gói mới nối sau gói cũ |
| User đang dùng thử Trip Pass, mua Trip Pass | Đơn là `Purchase`; kỳ mua bắt đầu ngay |
| User đang dùng thử Trip Pass, mua Membership | Đơn là `Purchase` (không phải `Upgrade`) |
| Gói tặng hết hạn | User về Free; lịch đã chốt vẫn giữ |
| Hai admin tặng cùng lúc cho một user | Lần lượt nhờ khoá user; gói thứ hai nối sau gói thứ nhất |
| Xoá một gói đang có gói tặng trỏ tới | 409 `plan_in_use` như gói đang có kỳ |

## A7. Code bị đụng

| Chỗ | Việc |
|---|---|
| Migration | Bảng `PlanGrants` |
| `EffectiveSubscriptionResolver`, `EffectiveSubscription` | Mục A2 |
| `ISubscriptionRepository` + `SubscriptionRepository` | Thêm hàm đọc gói tặng của user; sửa lớp giả `TestSubscriptionRepository` trong test |
| `SubscriptionService.GetMySubscriptionAsync` | Thêm `isTrial` |
| `AdminUserRepository.Rows` | Cột gói hiện tại tính cả gói tặng |
| `AdminPlanRepository.IsReferencedAsync` | Tính cả `PlanGrants` |
| `NotificationBuilder` | Loại mới `plan_granted`, đích `Subscription`, khoá `plan-grant:{grantId}` |
| Mới | `AdminPlanGrantsController`, `IPlanGrantService`, `IPlanGrantRepository` |

Không sửa: bất kỳ ràng buộc hay trigger nào trên `SubscriptionPeriods`, `PaymentOrders`; luồng checkout, nâng cấp, `repair-entitlement`.

---

# Phần C: Chương trình dùng thử khi đăng ký

## C1. Dữ liệu

Bảng mới `TrialPrograms`:

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | Guid | |
| `Name` | chuỗi ≤ 100 | |
| `StartsOn`, `EndsOn` | ngày | Theo giờ Việt Nam, tính cả hai đầu |
| `TrialDays` | int | 1–30 |
| `MaxRecipients` | int, null | null = không giới hạn |
| `IsEnabled` | bool | |
| `CreatedByUserId` | Guid, null | FK `Users`, SetNull |
| `CreatedAt`, `UpdatedAt` | giờ UTC | |

Gói dùng thử cố định là Trip Pass (theo mã gói, không lưu trong bảng).

## C2. Endpoint admin

Quyền `ManagePlans`.

- `GET /api/admin/trial-programs`: mảng mọi chương trình, mới nhất trước.
- `POST /api/admin/trial-programs`: tạo.
- `PUT /api/admin/trial-programs/{id}`: sửa toàn bộ field.

| Field | Kiểu | Bắt buộc | Giới hạn |
|---|---|---|---|
| `name` | string | Có | Trim, 1–100 ký tự |
| `startsOn` | `yyyy-MM-dd` | Có | |
| `endsOn` | `yyyy-MM-dd` | Có | ≥ `startsOn`; khi `isEnabled = true` thì không ở quá khứ |
| `trialDays` | int | Có | 1–30 |
| `maxRecipients` | int | Không | 1–100.000; bỏ trống = không giới hạn |
| `isEnabled` | bool | Có | |

Logic tạo và sửa:
1. Validate.
2. Trong một transaction có khoá tuần tự (dùng `IAdminOperationExecutor.ExecuteExclusiveAsync` với một tên khoá riêng): kiểm không có chương trình **đang bật** khác trùng khoảng ngày.
3. Lưu.

Không có API xoá.

## C3. Cấp dùng thử khi tài khoản được tạo

Gọi ở hai điểm trong `AuthService`, ngay cạnh `EnqueueWelcomeAsync`: sau xác thực OTP thành công và sau khi tạo tài khoản bằng Google lần đầu.

1. Lấy ngày hôm nay theo giờ Việt Nam.
2. Tìm chương trình đang bật có `StartsOn ≤ hôm nay ≤ EndsOn`. Không có thì dừng.
3. Mở transaction, khoá dòng chương trình (`SELECT … FOR UPDATE`).
4. Nếu có `MaxRecipients`: đếm số `PlanGrants` có `TrialProgramId` này; đã đủ thì dừng.
5. Lấy gói Trip Pass và phiên bản hiện hành; gói đang tắt hoặc thiếu phiên bản thì ghi cảnh báo và dừng.
6. Ghi `PlanGrants`: bắt đầu ngay, kết thúc sau `TrialDays` ngày, nguồn `SignupTrial`, `TrialProgramId`, khoá chống trùng `signup-trial:{userId}` (ghi bằng `INSERT … ON CONFLICT DO NOTHING`).
7. Ghi thông báo trong app.

Toàn bộ bước này chạy **sau** khi tài khoản đã lưu, với `CancellationToken.None`, bọc `try/catch`: lỗi chỉ ghi log, đăng ký vẫn thành công.

## C4. Business rule

- Tại một thời điểm tối đa một chương trình đang chạy.
- Mỗi tài khoản nhận dùng thử đăng ký tối đa một lần.
- Chỉ áp dụng cho tài khoản tạo trong khoảng ngày của chương trình; không cấp bù.
- Người đăng ký ngày cuối vẫn được đủ `TrialDays` ngày.
- Sửa hoặc tắt chương trình không ảnh hưởng người đã nhận.
- Hạ `MaxRecipients` xuống thấp hơn số đã nhận là hợp lệ: chương trình thành hết suất.
- Tài khoản tạo bằng seed (admin, tài khoản dev) không đi qua `AuthService` nên không có dùng thử.

## C5. Output

Mỗi chương trình:
```json
{
  "id": "…", "name": "Khai trương tháng 10",
  "startsOn": "2026-10-15", "endsOn": "2026-10-31",
  "trialDays": 7, "maxRecipients": 100,
  "isEnabled": true, "status": "Running",
  "grantedCount": 37, "remaining": 63,
  "createdAt": "…", "updatedAt": "…"
}
```

`status` do BE tính, xét theo thứ tự: `Disabled` (đang tắt) → `Ended` (hôm nay sau `endsOn`) → `Upcoming` (hôm nay trước `startsOn`) → `Full` (đã đủ `maxRecipients`) → `Running`.

`remaining` là null khi không giới hạn.

## C6. Lỗi

| HTTP | `code` | Khi nào |
|---|---|---|
| 400 | `invalid_trial_program` | Sai field (theo field `Name` / `StartsOn` / `EndsOn` / `TrialDays` / `MaxRecipients`) |
| 404 | `trial_program_not_found` | Sửa chương trình không tồn tại |
| 409 | `trial_program_overlap` | Trùng ngày với chương trình đang bật khác |
| 401 / 403 | — | Không token / thiếu quyền |

## C7. Case phải xử lý

| Case | Kết quả |
|---|---|
| Chương trình 15/10–31/10, 7 ngày; đăng ký 20/10 | Trip Pass tới 27/10 |
| Đăng ký 31/10 lúc 23:50 giờ VN | Vẫn được; Trip Pass tới 07/11 |
| Đăng ký 01/11, hoặc 14/10 | Free |
| Đăng nhập Google lần hai trở đi | Không cấp thêm |
| Admin tắt chương trình | Người đăng ký sau đó là Free; người đã nhận không đổi |
| Admin kéo dài `endsOn` | Áp dụng tới ngày mới |
| Giới hạn 100, đã có 99, hai người đăng ký cùng lúc | Một người được dùng thử, người kia là Free |
| Đã đủ suất, chương trình còn hạn | Người đăng ký sau là Free; `status: Full` |
| Admin nâng `maxRecipients` khi đã đủ | Người đăng ký tiếp theo được dùng thử |
| Admin hạ `maxRecipients` dưới số đã nhận | 200; `status: Full`, `remaining: 0` |
| Gói Trip Pass đang tắt lúc user đăng ký | Không cấp; ghi cảnh báo; đăng ký vẫn thành công |
| Ghi gói tặng lỗi do DB | Ghi lỗi vào log; tài khoản vẫn tạo, là Free |
| Hai admin cùng tạo chương trình trùng ngày | Một thành công, một nhận 409 |

## C8. Code bị đụng

| Chỗ | Việc |
|---|---|
| Migration | Bảng `TrialPrograms`; FK `PlanGrants.TrialProgramId` |
| `AuthService` | Gọi cấp dùng thử ở hai điểm tạo tài khoản; thêm một dependency (hai file test dựng `AuthService` phải thêm đối số) |
| Mới | `AdminTrialProgramsController`, `ITrialProgramService`, `ITrialProgramRepository` |

---

# Phần chung

## Quy ước code phải theo

- Luồng Controller → Service (Application) → Repository (Infrastructure). Service không dùng `AppDbContext`.
- Mã lỗi và message đặt ở Controller; Service trả enum trạng thái.
- Repository riêng cho từng phần; không thêm method vào `IUserRepository` (nhiều test đang fake).
- Thêm migration mới thì commit cùng code. Chỉ thêm bảng mới, **không thêm cột vào bảng cũ** (thêm cột vào `Users`, `Trips`, `SubscriptionPeriods` làm đỏ các test nâng cấp).
- Mọi thời điểm lưu UTC; "ngày" của chương trình tính theo giờ Việt Nam (`VietnamTime`).

**Code tham khảo:**
- Xác định gói hiện tại: `LocalMateAI.Application/Services/EffectiveSubscription.cs`.
- Khoá user trong transaction: `LocalMateAI.Infrastructure/Repositories/TripGenerationQuotaExecutor.cs`.
- Khoá tuần tự cho thao tác admin: `IAdminOperationExecutor.ExecuteExclusiveAsync` (dùng ở `AdminRoleService`).
- Ghi phụ sau khi tạo tài khoản, lỗi chỉ ghi log: `AuthService.EnqueueWelcomeAsync`.
- Ghi chống trùng bằng `ON CONFLICT DO NOTHING`: `NotificationRepository.EnqueueAsync`.

## Test bắt buộc

Phần A:
- Resolver: gói tặng đang hiệu lực được chọn; gói tặng chưa bắt đầu hoặc đã hết thì không; kỳ đã mua cùng gói thắng gói tặng; gói ưu tiên cao hơn thắng.
- `Classify` trả `Purchase` khi user đang dùng thử (cho cả gói ngang và gói cao hơn).
- Service: luật ngày bắt đầu (4 ca ở A6), trần 365 ngày, từng lỗi ở A5.
- Repository trên Postgres thật: hai lệnh tặng song song cho một user ra hai gói nối nhau.

Phần C:
- Validator: từng luật field.
- Service: chọn đúng chương trình theo ngày giờ VN (ranh giới 00:00 và 23:59); tắt, hết hạn, chưa tới thì không cấp.
- Repository trên Postgres thật: trùng ngày bị chặn; hai lượt đăng ký song song ở suất cuối chỉ một lượt được cấp; khoá chống trùng theo user.
- `AuthService`: cấp dùng thử lỗi thì đăng ký vẫn thành công.

## Tiêu chí nghiệm thu

1. Admin tặng Trip Pass 7 ngày cho một user Free: `/subscription/me` của user ra Trip Pass với `isTrial: true`; user tạo được hơn 1 lịch trong tháng; lượt AI trong ngày theo Trip Pass.
2. Hết hạn gói tặng (chỉnh `EndsAt` trong DB thử): user về Free.
3. User đang dùng thử thanh toán Trip Pass thành công, đơn loại `Purchase`.
4. Tạo chương trình đang chạy, đăng ký một tài khoản mới: tài khoản có Trip Pass và có thông báo; `grantedCount` tăng 1.
5. Đặt `maxRecipients` bằng số đã nhận, đăng ký thêm một tài khoản: tài khoản là Free.
6. Toàn bộ test cũ vẫn qua, kể cả các test Postgres về thanh toán và nâng cấp gói.

## Điều cần biết trước khi bật chương trình

Trip Pass cho 15 lượt AI mỗi ngày, gấp 5 lần Free. Quỹ miễn phí của Gemini là 500 request LLM mỗi ngày và 15 request mỗi phút cho cả hệ thống. Nên đặt `maxRecipients` cho chương trình đầu, và có task xem lượt dùng LLM (`docs/task-admin-ai-usage.md`) trước khi mở rộng.

## Việc kéo theo cho FE

- Trang gói của user: nhãn "Dùng thử" và ngày hết hạn khi `isTrial: true`; hiện nút "Mua gói" thay cho "Gia hạn".
- Admin, trang chi tiết user: nút "Tặng gói" và lịch sử tặng.
- Admin: màn "Chương trình dùng thử" (danh sách, tạo, sửa, bật/tắt).
