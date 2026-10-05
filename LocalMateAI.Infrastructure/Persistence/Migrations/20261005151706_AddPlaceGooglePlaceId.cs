using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaceGooglePlaceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GooglePlaceId",
                table: "Places",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            // Dữ liệu cho DB đang chạy: gán mã địa điểm Google Maps và đưa toạ độ về đúng điểm của Google,
            // khớp theo tên. DB mới tạo thì bảng còn rỗng (lệnh không đụng dòng nào), dữ liệu đến từ places.seed.json.
            // Down không khôi phục toạ độ cũ.
            migrationBuilder.Sql("""
                UPDATE "Places" AS p
                SET "GooglePlaceId" = v.google_place_id,
                    "Location" = ST_SetSRID(ST_MakePoint(v.longitude::double precision, v.latitude::double precision), 4326),
                    "UpdatedAt" = now()
                FROM (VALUES
                    ('Ben Thanh Market (Chợ Bến Thành)', 'ChIJTeYpMT8vdTERMH8sUnkta40', 10.7725168, 106.6980208),
                    ('Bột chiên Cô Mẽ', 'ChIJXYSD7lYvdTER4jfmYCRijY0', 10.7725982, 106.6987722),
                    ('Bánh Bèo Chợ Bến Thành', 'ChIJ0cuMyzgvdTERJNp8NfIXyzo', 10.7726528, 106.6975357),
                    ('Cơm tấm 182', 'ChIJbb11IsYvdTERkWuA6Weledk', 10.7730106, 106.6972222),
                    ('Local Saigon Cafe - Bến Thành', 'ChIJ548yye0ZdTERv1ER0kMpBG4', 10.7734577, 106.6975476),
                    ('Soo Kafe Bến Thành', 'ChIJLUnNLj8vdTERtZ0FtgSj9-A', 10.772408, 106.6972096),
                    ('Bảo tàng Mỹ thuật Thành phố Hồ Chí Minh', 'ChIJPccxGQAvdTERYMYelqaxe8M', 10.7699472, 106.6992162),
                    ('Maison Marou Café Ben Thanh', 'ChIJ6XR0HOUvdTERLmL3SWTk0NU', 10.7735128, 106.6979635),
                    ('Nhà hát Thành phố Hồ Chí Minh', 'ChIJKcrnSUYvdTERO64MErYx9VU', 10.7766128, 106.7031715),
                    ('Café Carré', 'ChIJV9ERtY8vdTERGcKC7-JI488', 10.7759307, 106.7021115),
                    ('Phố đi bộ Nguyễn Huệ', 'ChIJnRF9v0YvdTERcuW8mp0X9us', 10.774231202620333, 106.70361783102214),
                    ('Dinh Độc Lập', 'ChIJL0dwVTgvdTERao3t8B1Jhxc', 10.7769942, 106.6953021),
                    ('Nhà thờ Đức Bà Sài Gòn', 'ChIJUSTY5jcvdTERRVvtbJNZT-g', 10.7797855, 106.6990189),
                    ('Bưu Điện Thành Phố Hồ Chí Minh', 'ChIJZ3RKEwAvdTERjqS3B3BQAM0', 10.7798889, 106.7006521),
                    ('Thảo Cầm Viên Sài Gòn', 'ChIJx7wwM0svdTERjuH2a9dkuU0', 10.7873344, 106.7050566),
                    ('Bảo tàng Lịch sử Thành phố Hồ Chí Minh', 'ChIJ5fqrRUsvdTERlfW0JX8JGyU', 10.788075, 106.7047291),
                    ('Công viên gầm cầu Ba Son', 'ChIJC7NGMbAvdTERhuxfbpmwkQc', 10.78088, 106.7082155),
                    ('Đường sách Thành phố Hồ Chí Minh', 'ChIJS8Yv1zcvdTERUI2V47UxkPw', 10.780963, 106.7000734),
                    ('Công viên Tao Đàn', 'ChIJsXOVajkvdTERcBdP0Xow8j4', 10.7755796, 106.6920797),
                    ('Phố đi bộ Bùi Viện', 'ChIJCdzLBRYvdTERpsMyNScNwPE', 10.767351, 106.6938836),
                    ('Công viên 23/9', 'ChIJu4YK_j4vdTERfSn0egKOyug', 10.7687114, 106.69234),
                    ('Saigon Centre', 'ChIJPY9kQ0cvdTERNEixjJGVzhY', 10.7731031, 106.70105),
                    ('Lacàph Coffee', 'ChIJm-gZAiUvdTERF1P6ptacTwc', 10.7757628, 106.7031707),
                    ('Tonkin Specialty Coffee', 'ChIJzaevQMcvdTERYE3UhLKg5NE', 10.7743893, 106.6978901),
                    ('Bếp Mẹ Ỉn', 'ChIJcwyUxjgvdTERFwXMvGKDY0U', 10.7738567, 106.6980798),
                    ('Pizza 4P''s Estella Place', 'ChIJAZp7s5sndTERvNeCZICxzWY', 10.8018374, 106.7484399),
                    ('CieL Dining', 'ChIJ15AVEwAndTER67Ng7TJICpE', 10.8048085, 106.7334666),
                    ('Landmark 81', 'ChIJEQnz-MIndTERzRrJ-HNQrDY', 10.7951153, 106.7221002),
                    ('Ănăn Saigon', 'ChIJZ_dpJ0EvdTER5wOl-TLvN-8', 10.771729, 106.702893),
                    ('Bảo tàng Tôn Đức Thắng', 'ChIJ9aHU7UUvdTERwvaHOXtRzVI', 10.7773163, 106.7065242),
                    ('Bảo tàng Chứng tích Chiến tranh', 'ChIJzwg3ojAvdTERqnQUK99K2Xw', 10.7795106, 106.6920916),
                    ('Vinhome Golden River Bason', 'ChIJfXXk_7ovdTERDpc49LAwdwI', 10.7868813, 106.7115181),
                    ('Pearl Center - Thảo Điền Pearl', 'ChIJ69WzURomdTERTP98e9_pRco', 10.8012804, 106.7328725),
                    ('Khu du lịch Tân Cảng', 'ChIJow37ah0mdTER16pHqnlbEV0', 10.7999642, 106.7247441),
                    ('Tam Sơn Yachting (Bến Du Thuyền Vinhomes Central)', 'ChIJr9UeI70ndTERG1yHM4vWWYo', 10.7956074, 106.725052),
                    ('IDOL Quán', 'ChIJAQAAAGUndTERJ0_xc4ZkZo0', 10.860436, 106.7874134),
                    ('Tô Tượng Thủ Đức - Tiệm Nhà Moon', 'ChIJ4cttQhQndTERgboulBNg1zA', 10.8610141, 106.7879204),
                    ('Bò Né Chibi (Beefsteak)', 'ChIJj4wT51bZdDEREmvxE-m6WQ8', 10.8598587, 106.7885173),
                    ('METROO COFFEE', 'ChIJxSzvOAAndTERUhQ6ijSMteE', 10.859099, 106.7885179),
                    ('Next Step Coffee', 'ChIJRZsXPgAndTER-Mo6HMqXXwI', 10.858897, 106.788113),
                    ('Tiệm bánh nhà Vi', 'ChIJDdszaAYndTERzDxP47gOH24', 10.8669332, 106.7991153),
                    ('Cơm Gà Xối Mỡ Nghĩa Ký', 'ChIJlbeHTgDZdDERN2IueEQIYMk', 10.871449, 106.7979649),
                    ('Cà Phê Chú Vũ', 'ChIJ8eTPJ43ZdDERbcaYqwkuti4', 10.8716423, 106.7978859),
                    ('Napoli Pizza', 'ChIJ7SnTGQAndTERZdDADdhqOgc', 10.8460348, 106.7706111),
                    ('Công viên văn hóa Suối Tiên', 'ChIJDXlfx1sndTERFg6NcPqJ8e8', 10.8661863, 106.8031678),
                    ('Metro Coffee - Suối Tiên', 'ChIJwR-uc6YldTERWbUZerwGopg', 10.8787351, 106.8135781),
                    ('Là gốm', 'ChIJY2EjWBkndTER_4kcfI70Cqw', 10.8467398, 106.7710356),
                    ('THE TERMINAL Nhà hàng món Âu', 'ChIJl92uo04ndTERV9zovchaVe4', 10.8471918, 106.7711351),
                    ('Xoài Thái Kitchen', 'ChIJtX5SewAndTERBu5w1p8eiig', 10.8350787, 106.7644342),
                    ('Chạm Rooftop', 'ChIJiXjoiwondTERUTcyGSQ9lEE', 10.8311988, 106.7696911),
                    ('Homes sushi', 'ChIJJ7Xz0vIndTERp3clJajavaQ', 10.8321913, 106.7698048),
                    ('The Vibes Events & Dining', 'ChIJ-z1PLPgndTERk-VwdZ9-las', 10.8085856, 106.7506565),
                    ('Huyền coffee&Tea', 'ChIJsbCaIQAndTERiNFkJbSSvYs', 10.8081094, 106.7548522),
                    ('Rach Chiec Bridge Park', 'ChIJmWhaFHgndTER6LHOm6zV1cs', 10.8126404, 106.7558227),
                    ('MM Mega Market An Phú', 'ChIJH6-CURImdTERX2oCqYyod5Q', 10.8002009, 106.7432144),
                    ('Á CHÂU DIMSUM HOUSE', 'ChIJZ4-OdTAndTER6WPwRQ8srQc', 10.7977391, 106.7415619),
                    ('Công viên Vinhomes Central Park', 'ChIJyS548sYndTERx_fKlYIochM', 10.7934308, 106.7243981),
                    ('Tháp quan sát Vinhomes Central Park', 'ChIJtXOdcwAndTERRDYVB3RupsY', 10.7934365, 106.7254665),
                    ('Vincom Plaza Thủ Đức', 'ChIJe0eA_KEndTERr0rIc3pPlGI', 10.8501, 106.7651),
                    ('Vincom Plaza Lê Văn Việt', 'ChIJ7ZttRWAndTERsTa2pg4Ddac', 10.8449727, 106.7786146),
                    ('Khu du lịch Văn Thánh', 'ChIJV8BHDV0pdTERvGqxdSId_50', 10.7987213, 106.7165604),
                    ('Pearl Plaza', 'ChIJ8zhXhNcpdTERR0mLvAon2G8', 10.7999596, 106.718563)
                ) AS v(name, google_place_id, latitude, longitude)
                WHERE p."Name" = v.name
                  AND p."DeletedAt" IS NULL
                  AND p."GooglePlaceId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GooglePlaceId",
                table: "Places");
        }
    }
}
