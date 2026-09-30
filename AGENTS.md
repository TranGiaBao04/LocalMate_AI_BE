# AGENTS.md — LocalMate AI Backend (BE)

> Hướng dẫn cho AI agent làm việc trong repo này. Đọc kỹ TRƯỚC khi sửa code.

## Tổng quan
- **Stack**: .NET 10 (Clean Architecture), EF Core + PostgreSQL/PostGIS, JWT Auth.
- **Solution**: `LocalMateAI.slnx` — 5 projects: `LocalMateAI.API` (Web API), `Application` (use cases/services/DTOs), `Domain` (entities), `Infrastructure` (EF Core, repos, migrations, security), `LocalMateAI.Tests` (xUnit).
- **Dự án capstone EXE201** — làm theo nhật ký task ở mục "Ghi log" bên dưới.

## Lệnh chạy & kiểm tra
```bash
# Build (phải 0 Error / 0 Warning trước khi báo xong)
dotnet build LocalMateAI.slnx

# Unit tests (LocalMateAI.Tests, xUnit)
dotnet test LocalMateAI.Tests/LocalMateAI.Tests.csproj

# Chạy API local
dotnet run --project LocalMateAI.API        # → http://localhost:5286/swagger

# DB trong Docker (PostGIS 17, port host 5433)
docker compose up -d          # container: localmate_ai_be-database-1

# Migration mới (cần `dotnet tool restore` lần đầu)
dotnet ef migrations add <Tên> --project LocalMateAI.Infrastructure --startup-project LocalMateAI.API --output-dir Persistence/Migrations

# Xem data (bảng EF Core tên IN HOA phải viết nháy kép: "TAGS", "USERS")
docker exec -e PGPASSWORD=$POSTGRES_PASSWORD localmate_ai_be-database-1 psql -U localmate -d localmateai
```

## Quy ước Git (BẮT BUỘC)
1. **Nền nhánh: `origin/dev`** — KHÔNG dùng `origin/main` (rất cũ, chỉ tới PR #8).
2. Tên nhánh: `feature/be-<dải-task>-<slug>` (vd `feature/be-31-33-matching-algorithms`).
3. **KHÔNG tự git commit** — chỉ đưa message commit (format conventional: `feat|fix|test|docs(scope): ...`), người dùng tự gõ lệnh.
4. Không đẩy trực tiếp lên `dev`/`main` — PR + review.
5. **CẤM commit/push `.env`** (chứa `POSTGRES_PASSWORD`, `Jwt__SigningKey`, `GoogleAuth__ClientId`).

## Kiến trúc & Quy ước code
- **Clean Architecture**: Controller (API) → Service/Command (Application) → Repository (Infrastructure) → Entity (Domain). DI đăng ký trong `Program.cs` (AddScoped).
- **Schema thay đổi qua EF Core Migrations** — không viết SQL thủ công, không sửa migration đã merged.
- Endpoint mới mặc định **`[Authorize(Roles = "User,Admin")]`** (login demo: `POST /api/auth/demo` → `accessToken`; demo token có role `User`).
- API base: `http://localhost:5286`. Đừng chạy đồng thời API host và container `api` (cùng port 5286).
- Guard middleware: mutation `POST/PUT/DELETE /api/trips/{id}/...` trên trip `Finalized` → 409 `trip_finalized` (exempt: `finalize`, `fork`).

## Endpoint chính
`/api/maps/build-url`, `/api/maps/validate-coordinate`, `/api/maps/metro-stations`, `/api/maps/metro-walk`, `/api/trips/routes/estimate`, `/api/trips/feasibility-check`, `/api/trips/match` (POST), `/api/trips/fallback-itinerary` (POST), `/api/trips/save`, `/api/trips/my-trips`, `/api/trips/{id}/finalize`, `/api/trips/{id}/fork`, `/api/tags`.

## Ghi log (đồng bộ bắt buộc)
- File: `D:\FPTU\Semester8\EXE201\project\LOG\IMPLEMENTATION_LOG.md` — **NGOÀI git** (chỉ lưu đĩa).
- Sau mỗi task: thêm entry audit `### [yyyy-MM-dd HH:mm] Tiêu đề` ở **ĐẦU** mục audit log, rồi **đồng bộ đủ 3 chỗ**: ① bảng PROGRESS OVERVIEW (số/tỷ lệ) ② matrix task (trạng thái ⏸️→✅) ③ header (nhánh hiện tại). Không sửa path tuyệt đối có sẵn.

## Pitfalls đã biết
- File `.cs` dùng CRLF — patch lệch indent → nên **viết lại nguyên file** (write_file) khi sửa nhiều chỗ.
- API đang chạy giữ lock bin → build lỗi MSB3021: kill process API (hoặc build ra OutDir tạm với forward slash: `-p:OutDir="D:/build-verify-localmate/"`).
- Đơn vị tiền seed là **nghìn đồng** (vd 150 = 150k VND) — FE gửi budget cùng đơn vị.
- Giờ mở cửa KHÔNG có data — BE-32 tạm bỏ filter này (đã note trong LOG).

## Task còn lại gần nhất (xem LOG để cập nhật)
FE-62, FE-64 (FE). Xem matrix đầy đủ trong IMPLEMENTATION_LOG.md.